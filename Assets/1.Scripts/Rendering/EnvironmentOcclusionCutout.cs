using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 카메라와 플레이어 사이 중간점에 구를 두고, 그 안에 들어온 배경(지정 레이어) 픽셀을 디더 패턴으로 잘라 냅니다.
/// 실제로 픽셀을 버리는 일은 GZOcclusionCutout.hlsl을 포함한 배경 셰이더가 합니다. 이 컴포넌트는 구 위치와
/// 반지름을 전역 셰이더 값으로 넘기고, 잘라 낼 렌더러에 전용 렌더링 레이어 비트를 켭니다.
/// </summary>
/// <remarks>
/// 벽이 잘려 보이므로 카메라가 벽 앞으로 당겨질 필요가 없습니다. 그래서 기본값으로 카메라 충돌
/// (<see cref="DirectionalCameraCollisionExtension"/>)을 꺼서 카메라가 벽을 그대로 지나가게 합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class EnvironmentOcclusionCutout : MonoBehaviour
{
    /// <summary>잘라 낼 렌더러에 켜는 렌더링 레이어 비트입니다. 셰이더의 GZ_OCCLUSION_RENDERING_LAYER_BIT와 같아야 합니다.</summary>
    public const int RenderingLayerBit = 30;

    /// <summary>구 반지름을 정하는 방식입니다.</summary>
    public enum RadiusMode
    {
        /// <summary>카메라-플레이어 거리에 비례합니다.</summary>
        DistanceScaled,
        /// <summary>정해 둔 반지름을 그대로 씁니다.</summary>
        Fixed,
    }

    private static readonly int SphereId = Shader.PropertyToID("_GZ_OcclusionSphere");
    private static readonly int ParamsId = Shader.PropertyToID("_GZ_OcclusionParams");

    [Header("Targets")]
    [Tooltip("잘라 낼 렌더러의 레이어입니다. 시작할 때 이 레이어의 렌더러에 전용 렌더링 레이어 비트를 켭니다.")]
    [SerializeField] private LayerMask m_cutoutLayers;
    [Tooltip("구의 플레이어 쪽 끝입니다. 비워 두면 지금 활성인 Cinemachine 카메라의 Follow 대상을 씁니다.")]
    [SerializeField] private Transform m_targetOverride;

    [Header("Sphere")]
    [Tooltip("DistanceScaled: 카메라-플레이어 거리에 비례해 반지름을 정합니다(Radius Scale 사용). " +
             "Fixed: 거리와 상관없이 Fixed Radius를 반지름으로 씁니다.")]
    [SerializeField] private RadiusMode m_radiusMode = RadiusMode.DistanceScaled;
    [Tooltip("DistanceScaled일 때 카메라-플레이어 거리의 절반에 곱할 값입니다. 1이면 구가 카메라와 플레이어에 딱 닿습니다. " +
             "1보다 작으면 플레이어 바로 뒤 벽이 덜 잘립니다.")]
    [SerializeField, Range(0.1f, 2.5f)] private float m_radiusScale = 0.9f;
    [Tooltip("Fixed일 때 쓸 반지름(미터)입니다. 구 중심은 그대로 카메라와 플레이어의 중간점입니다.")]
    [SerializeField, Min(0.01f)] private float m_fixedRadius = 1.5f;
    [Tooltip("카메라와 플레이어가 이보다 가까우면 자르지 않습니다.")]
    [SerializeField, Min(0f)] private float m_minDistance = 0.5f;
    [Tooltip("구 경계에서 디더가 서서히 바뀌는 폭입니다. 반지름 대비 비율입니다.")]
    [SerializeField, Range(0f, 1f)] private float m_edgeSoftness = 0.35f;
    [Tooltip("구 중심에서도 남겨 둘 픽셀 비율입니다. 0이면 중심은 완전히 뚫립니다.")]
    [SerializeField, Range(0f, 1f)] private float m_minimumVisibility = 0f;
    [Tooltip("구 중심을 카메라-플레이어 방향으로 옮기는 거리(미터)입니다. 0이면 둘의 중간점, 양수면 플레이어 쪽(앞), 음수면 카메라 쪽(뒤)으로 옮깁니다. " +
             "구를 키웠을 때 플레이어 앞 벽까지 잘리면 음수로 당깁니다.")]
    [SerializeField] private float m_centerOffset = 0f;

    [Header("Cone")]
    [Tooltip("켜면 플레이어에서 카메라 쪽으로 벌어지는 원뿔에 닿은 오브젝트(렌더러)만 구로 자르는 대상이 됩니다. " +
             "대상이 된 오브젝트는 원뿔 밖 부분까지 구 모양대로 잘립니다. 원뿔에 닿지 않은 오브젝트는 구 안이어도 자르지 않습니다. 끄면 지정 레이어 전부가 대상입니다.")]
    [SerializeField] private bool m_useCone = true;
    [Tooltip("원뿔의 반각(도)입니다. 플레이어→카메라 방향에서 이 각도 안에 오브젝트가 걸치면 대상이 됩니다.")]
    [SerializeField, Range(5f, 89f)] private float m_coneHalfAngle = 45f;
    [Tooltip("원뿔 꼭짓점을 플레이어에서 카메라 반대쪽(앞)으로 옮기는 거리(미터)입니다. 0이면 꼭짓점이 플레이어 위치입니다. " +
             "양수면 플레이어 주변에서 원뿔이 넓어져 어깨 바로 옆 오브젝트도 대상이 되고, 음수면 카메라 쪽으로 당겨집니다.")]
    [SerializeField] private float m_coneApexOffset = 0f;

    /// <summary>원뿔에 닿는지 볼 때 꼭짓점에서 카메라까지 축 위에 찍는 점 수입니다. 각 점에서 가장 가까운 바운드 지점이 원뿔 안인지 봅니다.</summary>
    private const int ConeAxisSamples = 8;

    [Tooltip("후보 렌더러의 바운드는 씬이 시작될 때(로딩 단계) 한 번 저장해 씁니다. 0이면 이후 다시 읽지 않습니다(배경이 고정이면 0). " +
             "컷아웃 대상 중 움직이는 물체(열리는 문 등)가 있으면, 한 프레임에 이 수만큼 돌아가며 다시 읽어 위치를 따라갑니다.")]
    [SerializeField, Min(0)] private int m_boundsRefreshPerFrame = 0;

    [Header("Camera")]
    [Tooltip("켜져 있으면 카메라 충돌 확장을 꺼서 카메라가 벽을 그대로 지나가게 합니다. 이 컴포넌트를 끄면 되돌립니다.")]
    [SerializeField] private bool m_disableCameraCollision = true;

    private readonly List<Renderer> m_taggedRenderers = new List<Renderer>();

    /// <summary><see cref="m_taggedRenderers"/>와 같은 순서로, 씬 시작 때 저장해 둔 바운드입니다.</summary>
    private readonly List<Bounds> m_cachedBounds = new List<Bounds>();

    /// <summary><see cref="m_taggedRenderers"/>와 같은 순서로, 지금 전용 비트를 켜 둔 상태인지입니다. 렌더러 값을 매번 읽지 않으려고 따로 둡니다.</summary>
    private readonly List<bool> m_isTarget = new List<bool>();
    private int m_boundsRefreshCursor;
    private readonly List<DirectionalCameraCollisionExtension> m_disabledCollisions =
        new List<DirectionalCameraCollisionExtension>();
    private Camera m_camera;
    private CinemachineBrain m_brain;
    private bool m_started;
    private bool m_active;

    /// <summary>마지막으로 넘긴 구입니다(xyz 중심, w 반지름). w가 0이면 꺼진 상태입니다.</summary>
    public Vector4 CurrentSphere { get; private set; }

    /// <summary>전용 렌더링 레이어 비트를 켠 렌더러 수입니다.</summary>
    public int TaggedRendererCount => m_taggedRenderers.Count;

    private void Reset()
    {
        int environmentLayer = LayerMask.NameToLayer("Environment");
        if (environmentLayer >= 0)
            m_cutoutLayers = 1 << environmentLayer;
    }

    private void Awake()
    {
        m_camera = GetComponent<Camera>();
        m_brain = GetComponent<CinemachineBrain>();
    }

    /// <summary>
    /// 씬이 처음 켜질 때는 다른 물체의 초기화가 끝난 Start에서 적용합니다. OnEnable 시점에는 카메라 리그 쪽 초기화가
    /// 끝나지 않아 충돌 확장을 꺼도 다시 켜진 상태로 남는 경우가 있었습니다. 이후 껐다 켤 때는 OnEnable에서 적용합니다.
    /// </summary>
    private void Start()
    {
        m_started = true;
        Activate();
    }

    private void OnEnable()
    {
        if (m_started)
            Activate();
    }

    private void OnDisable()
    {
        if (!m_active)
            return;

        m_active = false;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        SetSphere(Vector4.zero);
        UntagRenderers();
        RestoreCameraCollisions();
    }

    private void Activate()
    {
        if (m_active || !Application.isPlaying)
            return;

        m_active = true;
        TagRenderers();
        if (m_disableCameraCollision)
            DisableCameraCollisions();

        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void OnValidate()
    {
        m_minDistance = Mathf.Max(0f, m_minDistance);
        m_fixedRadius = Mathf.Max(0.01f, m_fixedRadius);
    }

    /// <summary>
    /// 이 카메라가 그려지기 직전에 구를 맞춥니다. Cinemachine이 LateUpdate에서 카메라를 옮긴 뒤라 위치가 어긋나지 않습니다.
    /// 다른 카메라(캡처용 등)가 그릴 때는 자르지 않습니다.
    /// </summary>
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera != m_camera)
        {
            SetSphere(Vector4.zero);
            return;
        }

        Vector4 sphere = CalculateSphere(out Vector3 coneApex, out Vector3 coneAxis, out float coneLength);
        SetSphere(sphere);
        if (sphere.w > 0f)
            UpdateConeTargets(sphere, coneApex, coneAxis, coneLength);
    }

    /// <param name="coneApex">원뿔 꼭짓점(플레이어 + 앞뒤 보정)입니다.</param>
    /// <param name="coneAxis">꼭짓점에서 카메라로 향하는 방향(단위 벡터)입니다.</param>
    /// <param name="coneLength">꼭짓점에서 카메라까지 거리입니다. 원뿔은 이 길이까지만 봅니다.</param>
    private Vector4 CalculateSphere(out Vector3 coneApex, out Vector3 coneAxis, out float coneLength)
    {
        coneApex = Vector3.zero;
        coneAxis = Vector3.zero;
        coneLength = 0f;
        Transform target = ResolveTarget();
        if (target == null)
            return Vector4.zero;

        Vector3 cameraPosition = m_camera.transform.position;
        Vector3 targetPosition = target.position;
        float distance = Vector3.Distance(cameraPosition, targetPosition);
        if (distance < m_minDistance)
            return Vector4.zero;

        // 중간점에서 카메라→플레이어 방향으로 앞뒤 보정을 더합니다.
        Vector3 direction = (targetPosition - cameraPosition) / distance;
        Vector3 center = (cameraPosition + targetPosition) * 0.5f + direction * m_centerOffset;
        float radius = m_radiusMode == RadiusMode.Fixed ? m_fixedRadius : distance * 0.5f * m_radiusScale;

        // 원뿔은 카메라-플레이어 축을 따라 돌므로 카메라를 어느 각도로 돌려도 기준이 같습니다.
        coneApex = targetPosition + direction * m_coneApexOffset;
        Vector3 toCamera = cameraPosition - coneApex;
        coneLength = toCamera.magnitude;
        coneAxis = coneLength > 1e-4f ? toCamera / coneLength : -direction;

        return new Vector4(center.x, center.y, center.z, radius);
    }

    private Transform ResolveTarget()
    {
        if (m_targetOverride != null)
            return m_targetOverride;

        if (m_brain == null)
            return null;

        CinemachineVirtualCameraBase activeCamera = m_brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
        return activeCamera != null ? activeCamera.Follow : null;
    }

    private void SetSphere(Vector4 sphere)
    {
        CurrentSphere = sphere;
        Shader.SetGlobalVector(SphereId, sphere);
        Shader.SetGlobalVector(ParamsId, new Vector4(m_edgeSoftness, m_minimumVisibility, 0f, 0f));
    }

    /// <summary>
    /// 구로 자를 대상을 오브젝트(렌더러) 단위로 고릅니다. 구에 걸치고 원뿔에도 닿은 렌더러만 전용 비트를 켜고, 나머지는 끕니다.
    /// </summary>
    /// <remarks>
    /// 원뿔은 자르는 모양을 정하지 않습니다. 대상이 된 렌더러는 셰이더에서 원래대로 구 모양 전체로 잘립니다.
    /// 원뿔을 끄면 지정 레이어 렌더러가 모두 대상입니다. 비트가 바뀔 때만 값을 씁니다.
    /// </remarks>
    private void UpdateConeTargets(Vector4 sphere, Vector3 coneApex, Vector3 coneAxis, float coneLength)
    {
        RefreshSomeBounds();

        uint bit = 1u << RenderingLayerBit;
        Vector3 center = sphere;
        float radiusSqr = sphere.w * sphere.w;
        float cosHalf = Mathf.Cos(m_coneHalfAngle * Mathf.Deg2Rad);
        for (int i = 0; i < m_taggedRenderers.Count; i++)
        {
            bool isTarget = true;
            if (m_useCone)
            {
                Bounds bounds = m_cachedBounds[i];
                isTarget = bounds.SqrDistance(center) <= radiusSqr && TouchesCone(bounds, coneApex, coneAxis, coneLength, cosHalf);
            }

            if (isTarget == m_isTarget[i])
                continue;

            Renderer targetRenderer = m_taggedRenderers[i];
            if (targetRenderer == null)
                continue;

            m_isTarget[i] = isTarget;
            targetRenderer.renderingLayerMask = isTarget ? targetRenderer.renderingLayerMask | bit : targetRenderer.renderingLayerMask & ~bit;
        }
    }

    /// <summary>저장해 둔 바운드를 이번 프레임 몫(Bounds Refresh Per Frame)만큼 돌아가며 다시 읽습니다. 0이면 로딩 때 값만 씁니다.</summary>
    private void RefreshSomeBounds()
    {
        int count = m_taggedRenderers.Count;
        if (count == 0 || m_boundsRefreshPerFrame <= 0)
            return;

        int refresh = Mathf.Min(m_boundsRefreshPerFrame, count);
        for (int n = 0; n < refresh; n++)
        {
            m_boundsRefreshCursor = (m_boundsRefreshCursor + 1) % count;
            Renderer targetRenderer = m_taggedRenderers[m_boundsRefreshCursor];
            if (targetRenderer != null)
                m_cachedBounds[m_boundsRefreshCursor] = targetRenderer.bounds;
        }
    }

    /// <summary>
    /// 바운드가 원뿔(꼭짓점에서 축 방향으로 정한 길이, 반각의 cos)에 닿는지 여부입니다.
    /// 축 위 몇 지점에서 가장 가까운 바운드 지점을 구해, 그 지점이 꼭짓점에서 본 각도로 원뿔 안이면 닿은 것으로 봅니다.
    /// </summary>
    private static bool TouchesCone(Bounds bounds, Vector3 apex, Vector3 axis, float length, float cosHalf)
    {
        for (int i = 0; i < ConeAxisSamples; i++)
        {
            Vector3 onAxis = apex + axis * (length * i / (ConeAxisSamples - 1));
            Vector3 toPoint = bounds.ClosestPoint(onAxis) - apex;
            float sqr = toPoint.sqrMagnitude;
            if (sqr < 1e-6f || Vector3.Dot(toPoint, axis) >= cosHalf * Mathf.Sqrt(sqr))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 지정 레이어 렌더러와 <see cref="OcclusionCutoutTarget"/> 아래 렌더러에 전용 비트를 켭니다.
    /// 같은 셰이더를 쓰는 다른 레이어 물체는 잘리지 않습니다.
    /// </summary>
    private void TagRenderers()
    {
        m_taggedRenderers.Clear();
        m_cachedBounds.Clear();
        m_isTarget.Clear();
        m_boundsRefreshCursor = 0;
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            if ((m_cutoutLayers.value & (1 << renderers[i].gameObject.layer)) != 0)
                TagRenderer(renderers[i]);
        }

        OcclusionCutoutTarget[] targets = FindObjectsByType<OcclusionCutoutTarget>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        for (int i = 0; i < targets.Length; i++)
        {
            Renderer[] children = targets[i].GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < children.Length; j++)
                TagRenderer(children[j]);
        }
    }

    private void TagRenderer(Renderer targetRenderer)
    {
        uint bit = 1u << RenderingLayerBit;
        if ((targetRenderer.renderingLayerMask & bit) != 0)
            return;

        targetRenderer.renderingLayerMask |= bit;
        m_taggedRenderers.Add(targetRenderer);
        m_cachedBounds.Add(targetRenderer.bounds);
        m_isTarget.Add(true);
    }

    private void UntagRenderers()
    {
        uint bit = 1u << RenderingLayerBit;
        for (int i = 0; i < m_taggedRenderers.Count; i++)
        {
            Renderer targetRenderer = m_taggedRenderers[i];
            if (targetRenderer != null)
                targetRenderer.renderingLayerMask &= ~bit;
        }

        m_taggedRenderers.Clear();
        m_cachedBounds.Clear();
        m_isTarget.Clear();
    }

    private void DisableCameraCollisions()
    {
        m_disabledCollisions.Clear();
        DirectionalCameraCollisionExtension[] collisions = FindObjectsByType<DirectionalCameraCollisionExtension>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        for (int i = 0; i < collisions.Length; i++)
        {
            if (!collisions[i].enabled)
                continue;

            collisions[i].enabled = false;
            m_disabledCollisions.Add(collisions[i]);
        }
    }

    private void RestoreCameraCollisions()
    {
        for (int i = 0; i < m_disabledCollisions.Count; i++)
        {
            if (m_disabledCollisions[i] != null)
                m_disabledCollisions[i].enabled = true;
        }

        m_disabledCollisions.Clear();
    }
}
