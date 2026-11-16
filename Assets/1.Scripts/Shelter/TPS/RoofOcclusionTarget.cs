using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 하나의 지붕 구획을 구성하는 Renderer들을 묶고,
/// 카메라 가림 정도를 MaterialPropertyBlock으로 전달합니다.
/// </summary>
/// <remarks>
/// 전봇대처럼 가는 오브젝트는 Detection Mode를 <see cref="DetectionMode.BetweenCameraAndTarget"/>로 둡니다.
/// 이 모드는 SphereCast 대신 카메라-캐릭터 시선과 오브젝트 세로축 사이의 각도로
/// 가림 정도(0~1)를 계산해, 겹치기 직전부터 서서히 디더링합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class RoofOcclusionTarget : MonoBehaviour
{
    /// <summary>가림 판정 방식입니다.</summary>
    public enum DetectionMode
    {
        /// <summary>감지 SphereCast에 닿기만 해도 가린 것으로 봅니다. 지붕용 기본값입니다.</summary>
        AnyHit,
        /// <summary>시선과 오브젝트 세로축 사이의 각도로 가림 정도를 0~1로 계산합니다. 전봇대 등 가는 오브젝트용입니다.</summary>
        BetweenCameraAndTarget,
    }

    private const string DefaultFadeProperty = "_OcclusionFade";

    private static readonly List<RoofOcclusionTarget> s_proximityTargets = new List<RoofOcclusionTarget>();

    /// <summary>각도 기반 판정(<see cref="DetectionMode.BetweenCameraAndTarget"/>)을 쓰는 활성 대상 목록입니다.</summary>
    public static IReadOnlyList<RoofOcclusionTarget> ProximityTargets => s_proximityTargets;

    [Header("Roof Visuals")]
    [Tooltip("이 지붕 구획과 함께 페이드할 Renderer들입니다. 비어 있으면 Awake에서 자식 Renderer를 자동 수집합니다.")]
    [SerializeField] private Renderer[] m_renderers = new Renderer[0];

    [Tooltip("Renderer 배열이 비어 있을 때 비활성 자식까지 포함해 자동 수집합니다.")]
    [SerializeField] private bool m_collectChildRenderersWhenEmpty = true;

    [Tooltip("가림 감지에는 쓰지 않고 페이드만 함께 적용할 Renderer들입니다. 철골 등 지붕 부속물에 사용합니다.")]
    [SerializeField] private Renderer[] m_followerRenderers = new Renderer[0];

    [Header("Shader")]
    [Tooltip("지붕 셰이더가 가림 정도(0=표시, 1=가림)를 받을 Float 프로퍼티 이름입니다.")]
    [SerializeField] private string m_fadeProperty = DefaultFadeProperty;

    [Header("Detection")]
    [Tooltip("AnyHit: 감지에 닿기만 해도 페이드합니다(지붕). " +
             "BetweenCameraAndTarget: 시선과 이 오브젝트 세로축 사이의 각도로 가림 정도를 계산해 서서히 페이드합니다(전봇대 등).")]
    [SerializeField] private DetectionMode m_detectionMode = DetectionMode.AnyHit;

    [Tooltip("BetweenCameraAndTarget에서 캐릭터 폭의 절반(미터)입니다. 완전히 겹친 것으로 보는 각도에 더해집니다.")]
    [SerializeField, Min(0f)] private float m_targetHalfWidth = 0.15f;

    [Tooltip("BetweenCameraAndTarget에서 캐릭터가 이 오브젝트보다 이만큼(미터) 뒤에 있을 때 최대로 페이드합니다. 그보다 가까우면 비례해서 약해집니다.")]
    [SerializeField, Min(0f)] private float m_minTargetGap = 0.2f;

    [Tooltip("BetweenCameraAndTarget에서 카메라로부터 이 거리(미터) 안에서 겹친 경우는 무시합니다. 카메라가 오브젝트 옆을 스칠 때의 오작동을 막습니다.")]
    [SerializeField, Min(0f)] private float m_ignoreNearCameraDistance = 0.3f;

    [Header("Proximity Fade (BetweenCameraAndTarget)")]
    [Tooltip("세로축(기둥)의 반경(미터)입니다. 가로대 등 부속물은 제외한 기둥 몸통 기준으로 넣습니다.")]
    [SerializeField, Min(0f)] private float m_poleRadius = 0.2f;

    [Tooltip("완전히 겹치는 각도 바깥으로 이 각도(도)만큼 떨어진 지점부터 디더를 시작합니다.")]
    [SerializeField, Min(0.01f)] private float m_fadeStartMarginDegrees = 5f;

    [Tooltip("세로축 높이(미터)입니다. 0이면 Renderer bounds 최상단까지로 자동 계산합니다. 축은 이 Transform의 위치에서 up 방향으로 뻗습니다.")]
    [SerializeField, Min(0f)] private float m_axisHeight = 0f;

    private MaterialPropertyBlock m_propertyBlock;
    private int m_fadePropertyId;
    private float m_currentFade;
    private float m_resolvedAxisHeight;
    private bool m_initialized;

    public float CurrentFade => m_currentFade;

    /// <summary>SphereCast 대신 각도 기반으로 가림 정도를 계산하는 대상인지 여부입니다.</summary>
    public bool UsesProximityDetection => m_detectionMode == DetectionMode.BetweenCameraAndTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_proximityTargets.Clear();
    }

    private void Reset()
    {
        CollectChildRenderers();
    }

    private void Awake()
    {
        Initialize();
        ApplyFade(0f);
    }

    private void OnEnable()
    {
        if (UsesProximityDetection && !s_proximityTargets.Contains(this))
            s_proximityTargets.Add(this);
    }

    private void OnDisable()
    {
        s_proximityTargets.Remove(this);
        ApplyFade(0f);
    }

    /// <summary>
    /// 0이면 완전히 표시하고 1이면 셰이더가 정의한 최대 가림 상태로 만듭니다.
    /// </summary>
    public void ApplyFade(float fade)
    {
        Initialize();

        m_currentFade = Mathf.Clamp01(fade);

        ApplyFadeToRenderers(m_renderers);
        ApplyFadeToRenderers(m_followerRenderers);
    }

    private void ApplyFadeToRenderers(Renderer[] renderers)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null)
                continue;

            m_propertyBlock.Clear();
            targetRenderer.GetPropertyBlock(m_propertyBlock);
            m_propertyBlock.SetFloat(m_fadePropertyId, m_currentFade);
            targetRenderer.SetPropertyBlock(m_propertyBlock);
        }
    }

    public void RestoreImmediately()
    {
        ApplyFade(0f);
    }

    /// <summary>
    /// 감지된 Collider가 이 구획 자체이거나 등록된 지붕 Renderer 계층에 속하는지 확인합니다.
    /// 같은 그룹 아래의 조명, 기둥 등 다른 Collider로는 페이드하지 않기 위해 사용합니다.
    /// </summary>
    public bool OwnsCollider(Collider hitCollider)
    {
        if (hitCollider == null)
            return false;

        Initialize();

        Transform hitTransform = hitCollider.transform;
        if (hitTransform == transform)
            return true;

        for (int i = 0; i < m_renderers.Length; i++)
        {
            Renderer targetRenderer = m_renderers[i];
            if (targetRenderer != null && hitTransform.IsChildOf(targetRenderer.transform))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 카메라-캐릭터 시선이 이 오브젝트의 세로축과 얼마나 겹치는지 0~1로 돌려줍니다.
    /// 완전히 겹치는 각도 안이면 1, 그 바깥 <see cref="m_fadeStartMarginDegrees"/>까지 부드럽게 0으로 줄어듭니다.
    /// </summary>
    public float EvaluateProximityOcclusion(Vector3 cameraPosition, Vector3 targetPosition)
    {
        Initialize();

        float sightLength = Vector3.Distance(cameraPosition, targetPosition);
        if (sightLength <= Mathf.Epsilon)
            return 0f;

        Vector3 axisBase = transform.position;
        Vector3 axisTop = axisBase + transform.up * m_resolvedAxisHeight;

        float sightParam = ClosestPointsOnSegments(
            cameraPosition, targetPosition,
            axisBase, axisTop,
            out Vector3 pointOnSight,
            out Vector3 pointOnAxis);

        // 시선 위에서 축과 가장 가까운 지점까지의 카메라 거리입니다.
        float distanceFromCamera = sightParam * sightLength;
        if (distanceFromCamera < m_ignoreNearCameraDistance)
            return 0f;

        // 캐릭터가 축보다 뒤에 있을수록(최대 m_minTargetGap) 강하게 가린 것으로 봅니다.
        float gapBehind = sightLength - distanceFromCamera;
        float depthFactor = m_minTargetGap > 0f
            ? Mathf.SmoothStep(0f, 1f, gapBehind / m_minTargetGap)
            : (gapBehind > 0f ? 1f : 0f);
        if (depthFactor <= 0f)
            return 0f;

        // 카메라에서 본 시선-축 사이 각도와, 완전히 겹친 것으로 볼 각도(기둥 각반경 + 캐릭터 각반경)를 비교합니다.
        float offsetDegrees =
            Mathf.Atan2(Vector3.Distance(pointOnSight, pointOnAxis), distanceFromCamera) * Mathf.Rad2Deg;
        float fullOverlapDegrees =
            (Mathf.Atan2(m_poleRadius, distanceFromCamera) + Mathf.Atan2(m_targetHalfWidth, sightLength)) * Mathf.Rad2Deg;
        float fadeStartDegrees = fullOverlapDegrees + m_fadeStartMarginDegrees;

        float overlap = 1f - Mathf.InverseLerp(fullOverlapDegrees, fadeStartDegrees, offsetDegrees);
        overlap = Mathf.SmoothStep(0f, 1f, overlap);

        return overlap * depthFactor;
    }

    /// <summary>
    /// 두 선분 사이의 최근접점을 구하고, 첫 선분 위 최근접점의 매개변수(0~1)를 돌려줍니다.
    /// </summary>
    private static float ClosestPointsOnSegments(
        Vector3 p1, Vector3 q1,
        Vector3 p2, Vector3 q2,
        out Vector3 closestOnFirst,
        out Vector3 closestOnSecond)
    {
        const float Epsilon = 1e-6f;

        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        float s;
        float t;

        if (a <= Epsilon && e <= Epsilon)
        {
            s = 0f;
            t = 0f;
        }
        else if (a <= Epsilon)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= Epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = a * e - b * b;
                s = denominator > Epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                t = (b * s + f) / e;

                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        closestOnFirst = p1 + d1 * s;
        closestOnSecond = p2 + d2 * t;
        return s;
    }

    [ContextMenu("Collect Child Renderers")]
    private void CollectChildRenderers()
    {
        m_renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ResolveAxisHeight()
    {
        if (m_axisHeight > 0f)
        {
            m_resolvedAxisHeight = m_axisHeight;
            return;
        }

        float maxY = float.NegativeInfinity;
        for (int i = 0; i < m_renderers.Length; i++)
        {
            if (m_renderers[i] != null)
                maxY = Mathf.Max(maxY, m_renderers[i].bounds.max.y);
        }

        m_resolvedAxisHeight = float.IsNegativeInfinity(maxY)
            ? 0f
            : Mathf.Max(0f, maxY - transform.position.y);
    }

    private void Initialize()
    {
        if (m_initialized)
            return;

        if (m_collectChildRenderersWhenEmpty &&
            (m_renderers == null || m_renderers.Length == 0))
        {
            CollectChildRenderers();
        }

        if (m_renderers == null)
            m_renderers = new Renderer[0];

        if (m_followerRenderers == null)
            m_followerRenderers = new Renderer[0];

        if (string.IsNullOrWhiteSpace(m_fadeProperty))
            m_fadeProperty = DefaultFadeProperty;

        m_fadePropertyId = Shader.PropertyToID(m_fadeProperty);
        m_propertyBlock = new MaterialPropertyBlock();
        ResolveAxisHeight();
        m_initialized = true;
    }
}
