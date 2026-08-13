using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카메라와 대상 루트 사이의 거리에 따라 대상 아래 Renderer만 렌더링에서 제외합니다.
/// CullingGroup이 거리 구간 변화를 감지하므로 매 프레임 Update나 Raycast를 사용하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class DistanceRendererCullingGroup : MonoBehaviour
{
    private const int VisibleBand = 0;
    private const int HysteresisBand = 1;
    private const int CulledBand = 2;

    [Header("Targets")]
    [Tooltip("각 Transform 아래의 모든 Renderer를 하나의 거리 컬링 그룹으로 취급합니다.")]
    [SerializeField] private Transform[] m_targetRoots;

    [Header("Distances")]
    [Tooltip("이 거리 안으로 들어오면 대상 Renderer를 다시 표시합니다.")]
    [Min(0f)]
    [SerializeField] private float m_showDistance = 8f;

    [Tooltip("이 거리 밖으로 나가면 대상 Renderer를 숨깁니다. 두 거리 사이에서는 현재 상태를 유지합니다.")]
    [Min(0f)]
    [SerializeField] private float m_hideDistance = 10f;

    private Camera m_camera;
    private CullingGroup m_cullingGroup;
    private RuntimeGroup[] m_runtimeGroups;
    private BoundingSphere[] m_boundingSpheres;

    private sealed class RuntimeGroup
    {
        public readonly Renderer[] Renderers;
        public readonly bool[] OriginalForceRenderingOff;
        public bool IsCulled;

        public RuntimeGroup(Renderer[] renderers)
        {
            Renderers = renderers;
            OriginalForceRenderingOff = new bool[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                OriginalForceRenderingOff[i] = renderers[i].forceRenderingOff;
            }
        }
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Build();
    }

    private void OnDisable()
    {
        DisposeAndRestore();
    }

    private void OnDestroy()
    {
        DisposeAndRestore();
    }

    private void OnValidate()
    {
        m_showDistance = Mathf.Max(0f, m_showDistance);
        m_hideDistance = Mathf.Max(m_showDistance, m_hideDistance);
    }

    /// <summary>
    /// 대상 계층이나 Renderer 구성이 런타임에 바뀐 경우 명시적으로 다시 구성합니다.
    /// 정적인 환경 소품에는 호출할 필요가 없습니다.
    /// </summary>
    public void Rebuild()
    {
        DisposeAndRestore();

        if (isActiveAndEnabled && Application.isPlaying)
        {
            Build();
        }
    }

    private void Build()
    {
        m_camera = GetComponent<Camera>();

        if (m_targetRoots == null || m_targetRoots.Length == 0)
        {
            Debug.LogWarning($"[{nameof(DistanceRendererCullingGroup)}] 거리 컬링 대상이 없습니다.", this);
            return;
        }

        List<RuntimeGroup> runtimeGroups = new List<RuntimeGroup>(m_targetRoots.Length);
        List<BoundingSphere> boundingSpheres = new List<BoundingSphere>(m_targetRoots.Length);
        HashSet<Renderer> registeredRenderers = new HashSet<Renderer>();

        for (int i = 0; i < m_targetRoots.Length; i++)
        {
            Transform targetRoot = m_targetRoots[i];
            if (targetRoot == null)
            {
                Debug.LogWarning($"[{nameof(DistanceRendererCullingGroup)}] Targets의 {i}번 항목이 비어 있습니다.", this);
                continue;
            }

            Renderer[] childRenderers = targetRoot.GetComponentsInChildren<Renderer>(true);
            List<Renderer> uniqueRenderers = new List<Renderer>(childRenderers.Length);
            Bounds bounds = default;
            bool hasBounds = false;

            for (int rendererIndex = 0; rendererIndex < childRenderers.Length; rendererIndex++)
            {
                Renderer childRenderer = childRenderers[rendererIndex];
                if (childRenderer == null || !registeredRenderers.Add(childRenderer))
                {
                    continue;
                }

                uniqueRenderers.Add(childRenderer);

                if (hasBounds)
                {
                    bounds.Encapsulate(childRenderer.bounds);
                }
                else
                {
                    bounds = childRenderer.bounds;
                    hasBounds = true;
                }
            }

            if (!hasBounds)
            {
                Debug.LogWarning(
                    $"[{nameof(DistanceRendererCullingGroup)}] '{targetRoot.name}' 아래에 등록할 Renderer가 없습니다.",
                    targetRoot);
                continue;
            }

            runtimeGroups.Add(new RuntimeGroup(uniqueRenderers.ToArray()));
            boundingSpheres.Add(new BoundingSphere(bounds.center, bounds.extents.magnitude));
        }

        if (runtimeGroups.Count == 0)
        {
            return;
        }

        m_runtimeGroups = runtimeGroups.ToArray();
        m_boundingSpheres = boundingSpheres.ToArray();

        m_cullingGroup = new CullingGroup
        {
            targetCamera = m_camera,
            onStateChanged = OnCullingStateChanged,
        };
        m_cullingGroup.SetDistanceReferencePoint(m_camera.transform);
        m_cullingGroup.SetBoundingDistances(new[] { m_showDistance, m_hideDistance });
        m_cullingGroup.SetBoundingSpheres(m_boundingSpheres);
        m_cullingGroup.SetBoundingSphereCount(m_boundingSpheres.Length);

        ApplyInitialStates();
    }

    private void ApplyInitialStates()
    {
        Vector3 referencePosition = m_camera.transform.position;

        for (int i = 0; i < m_boundingSpheres.Length; i++)
        {
            BoundingSphere sphere = m_boundingSpheres[i];
            float closestDistance = Mathf.Max(0f, Vector3.Distance(referencePosition, sphere.position) - sphere.radius);
            SetCulled(i, closestDistance >= m_hideDistance);
        }
    }

    private void OnCullingStateChanged(CullingGroupEvent cullingEvent)
    {
        if (cullingEvent.index < 0 || cullingEvent.index >= m_runtimeGroups.Length)
        {
            return;
        }

        if (cullingEvent.currentDistance == VisibleBand)
        {
            SetCulled(cullingEvent.index, false);
        }
        else if (cullingEvent.currentDistance >= CulledBand)
        {
            SetCulled(cullingEvent.index, true);
        }
        else if (cullingEvent.currentDistance == HysteresisBand)
        {
            // 8~10m 구간에서는 직전 상태를 유지해 경계 왕복 시 깜빡임을 막습니다.
        }
    }

    private void SetCulled(int groupIndex, bool shouldCull)
    {
        RuntimeGroup group = m_runtimeGroups[groupIndex];
        if (group.IsCulled == shouldCull)
        {
            return;
        }

        group.IsCulled = shouldCull;

        for (int i = 0; i < group.Renderers.Length; i++)
        {
            Renderer targetRenderer = group.Renderers[i];
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.forceRenderingOff = shouldCull || group.OriginalForceRenderingOff[i];
        }
    }

    private void DisposeAndRestore()
    {
        if (m_cullingGroup != null)
        {
            m_cullingGroup.onStateChanged = null;
            m_cullingGroup.Dispose();
            m_cullingGroup = null;
        }

        if (m_runtimeGroups != null)
        {
            for (int groupIndex = 0; groupIndex < m_runtimeGroups.Length; groupIndex++)
            {
                RuntimeGroup group = m_runtimeGroups[groupIndex];

                for (int rendererIndex = 0; rendererIndex < group.Renderers.Length; rendererIndex++)
                {
                    Renderer targetRenderer = group.Renderers[rendererIndex];
                    if (targetRenderer != null)
                    {
                        targetRenderer.forceRenderingOff = group.OriginalForceRenderingOff[rendererIndex];
                    }
                }
            }
        }

        m_runtimeGroups = null;
        m_boundingSpheres = null;
    }
}
