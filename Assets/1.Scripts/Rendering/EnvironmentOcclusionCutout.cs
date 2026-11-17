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

    /// <summary>
    /// 잘림 강도 단계(0~3)를 담는 렌더링 레이어 비트의 시작 번호입니다(28·29번 두 비트). 셰이더의 GZ_OCCLUSION_FADE_LEVEL_SHIFT와 같아야 합니다.
    /// 단계 0~3은 강도 25·50·75·100%이고, 대상 비트(30번)가 꺼져 있으면 자르지 않습니다.
    /// </summary>
    public const int FadeLevelShift = 28;

    private const int FadeLevelCount = 4;
    private const uint FadeLevelMask = 3u << FadeLevelShift;
    private const uint CutoutBits = (1u << RenderingLayerBit) | FadeLevelMask;

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
    [Tooltip("원뿔의 반각(도)입니다. 플레이어→카메라 방향 축에서 좌우·위아래 모든 방향으로 이 각도 안(3D 원뿔)에 오브젝트가 걸치면 대상이 됩니다.")]
    [SerializeField, Range(5f, 89f)] private float m_coneHalfAngle = 45f;
    [Tooltip("이미 대상인 오브젝트가 대상에서 빠지는 각도 여유(도)입니다. 반각에서 대상이 되고, 반각 + 이 값을 벗어나야 빠집니다. " +
             "경계에서 플레이어가 조금씩 움직일 때 잘림이 켜졌다 꺼졌다 깜빡이지 않게 합니다.")]
    [SerializeField, Range(0f, 30f)] private float m_coneExitExtraAngle = 5f;
    [Tooltip("대상이 되거나 빠질 때 잘림 강도가 0에서 1(또는 1에서 0)로 바뀌는 시간(초, 실제 시간)입니다. " +
             "새로 들어온 오브젝트가 다른 대상과 갑자기 같이 잘리지 않고 서서히 합류합니다. 0이면 바로 바뀝니다.")]
    [SerializeField, Min(0f)] private float m_fadeSeconds = 0.2f;
    [Tooltip("원뿔 꼭짓점을 플레이어에서 카메라 반대쪽(앞)으로 옮기는 거리(미터)입니다. 0이면 꼭짓점이 플레이어 위치입니다. " +
             "양수면 플레이어 주변에서 원뿔이 넓어져 어깨 바로 옆 오브젝트도 대상이 되고, 음수면 카메라 쪽으로 당겨집니다.")]
    [SerializeField] private float m_coneApexOffset = 0f;
    [Tooltip("원뿔이 꼭짓점에서 카메라 쪽(뒤)으로 감지하는 길이(미터)입니다. 0이면 카메라까지 봅니다. " +
             "줄이면 카메라 가까이에서만 원뿔에 걸리는 오브젝트(플레이어 옆을 따라 카메라 쪽으로 길게 이어진 벽 등)는 대상이 아닙니다.")]
    [SerializeField, Min(0f)] private float m_coneLength = 0f;
    [Tooltip("켜면 카메라에서 볼 때 플레이어 너머에서 시작하는 오브젝트는 원뿔에 닿아도 자르지 않습니다. " +
             "구가 플레이어 뒤쪽까지 넘어가도 플레이어 바로 앞의 엄폐물이 잘리지 않게 합니다.")]
    [SerializeField] private bool m_skipBeyondTarget = true;
    [Tooltip("플레이어 너머로 보는 여유(미터)입니다. 오브젝트의 카메라 쪽 면이 플레이어보다 이 거리 이상 카메라 쪽에 있어야 대상이 됩니다.")]
    [SerializeField, Min(0f)] private float m_beyondTargetMargin = 0.3f;

    /// <summary>원뿔에 닿는지 볼 때 꼭짓점에서 카메라까지 축 위에 찍는 점 수입니다. 각 점에서 가장 가까운 바운드 지점이 원뿔 안인지 봅니다.</summary>
    private const int ConeAxisSamples = 8;

    [Tooltip("시작할 때 없던(나중에 생성된) 후보 렌더러를 찾아 등록하는 간격(초)입니다. 0이면 시작 때 등록한 것만 씁니다. " +
             "찾을 때 씬의 렌더러를 한 번 훑으므로 너무 짧게 두지 않습니다.")]
    [SerializeField, Min(0f)] private float m_registerScanInterval = 3f;

    [Header("Camera")]
    [Tooltip("켜져 있으면 카메라 충돌 확장을 꺼서 카메라가 벽을 그대로 지나가게 합니다. 이 컴포넌트를 끄면 되돌립니다.")]
    [SerializeField] private bool m_disableCameraCollision = true;

    [Header("Debug")]
    [Tooltip("켜면 실행 중 구(하늘색), 원뿔(노란색), 지금 자르는 대상 오브젝트의 상자(빨간색)를 기즈모로 그립니다. Scene 뷰나 Game 뷰의 Gizmos를 켜야 보입니다.")]
    [SerializeField] private bool m_drawDebug = true;

    /// <summary>
    /// 컷아웃 후보 렌더러 하나의 등록 정보입니다. 바운드를 캐싱해 두고 매 프레임 이 값으로 대상 여부를 판정합니다.
    /// </summary>
    private struct CutoutEntry
    {
        public Renderer Renderer;
        public Transform Transform;

        /// <summary>캐싱해 둔 월드 바운드입니다.</summary>
        public Bounds Bounds;

        /// <summary>바운드를 캐싱했는지 여부입니다. 등록 때 꺼져 있던 렌더러는 켜질 때 캐싱합니다.</summary>
        public bool HasBounds;

        /// <summary>정적 배치가 아니어서 움직일 수 있는지 여부입니다. 움직일 수 있는 것만 Transform 변화를 봅니다.</summary>
        public bool Movable;

        /// <summary>지금 전용 비트를 켜 둔 상태인지입니다. 렌더러 값을 매번 읽지 않으려고 따로 둡니다.</summary>
        public bool IsTarget;

        /// <summary>지금 잘림 강도(0~1)입니다. 대상이면 1로, 아니면 0으로 <see cref="m_fadeSeconds"/>에 걸쳐 바뀝니다.</summary>
        public float Weight;

        /// <summary>렌더러에 적어 둔 강도 단계입니다. -1이면 대상 비트를 끈 상태입니다. 바뀔 때만 렌더러 값을 씁니다.</summary>
        public int AppliedLevel;

        /// <summary>회전을 반영한 실제 상자(로컬 바운드)입니다. 원뿔 판정에 씁니다. <see cref="HasOrientedBounds"/>가 거짓이면 쓰지 않습니다.</summary>
        public Bounds LocalBounds;
        public Matrix4x4 LocalToWorld;
        public Matrix4x4 WorldToLocal;

        /// <summary>로컬 바운드를 쓸 수 있는지 여부입니다. 스킨드 메시(로컬 바운드가 루트 본 기준)와 정적 배치 렌더러는 월드 바운드로 판정합니다.</summary>
        public bool HasOrientedBounds;
    }

    /// <summary>
    /// 후보 렌더러 등록부입니다. 스포너 풀처럼 한 번 등록한 항목은 계속 쓰고, 파괴된 렌더러의 자리는 비워 두었다가 새 등록에 다시 씁니다.
    /// </summary>
    private readonly List<CutoutEntry> m_entries = new List<CutoutEntry>();
    private readonly Dictionary<Renderer, int> m_entryIndex = new Dictionary<Renderer, int>();
    private readonly Stack<int> m_freeSlots = new Stack<int>();
    private float m_nextRegisterScanTime;
    private readonly List<DirectionalCameraCollisionExtension> m_disabledCollisions =
        new List<DirectionalCameraCollisionExtension>();
    private Camera m_camera;
    private CinemachineBrain m_brain;
    private bool m_started;
    private bool m_active;

    /// <summary>마지막으로 넘긴 구입니다(xyz 중심, w 반지름). w가 0이면 꺼진 상태입니다.</summary>
    public Vector4 CurrentSphere { get; private set; }

    /// <summary>전용 렌더링 레이어 비트를 켠 렌더러 수입니다.</summary>
    public int TaggedRendererCount => m_entryIndex.Count;

    // 디버그 그리기용으로 마지막 원뿔 값을 기억합니다.
    private Vector3 m_lastConeApex;
    private Vector3 m_lastConeAxis;
    private float m_lastConeLength;

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
        m_lastConeApex = coneApex;
        m_lastConeAxis = coneAxis;
        m_lastConeLength = coneLength;
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
        RegisterNewRenderersIfDue();
        RefreshEntryBounds();

        float fadeStep = m_fadeSeconds > 0f ? Time.unscaledDeltaTime / m_fadeSeconds : 1f;
        Vector3 center = sphere;
        float radiusSqr = sphere.w * sphere.w;
        float cosHalf = Mathf.Cos(m_coneHalfAngle * Mathf.Deg2Rad);
        float cosExitHalf = Mathf.Cos(Mathf.Min(89.9f, m_coneHalfAngle + m_coneExitExtraAngle) * Mathf.Deg2Rad);

        // 카메라에서 시선 방향(플레이어 쪽)으로 잰 깊이입니다. 꼭짓점은 플레이어에서 앞뒤 보정만큼 옮겨져 있어 그만큼 빼서 플레이어 깊이를 구합니다.
        Vector3 cameraPosition = coneApex + coneAxis * coneLength;
        Vector3 viewDirection = -coneAxis;
        float maxNearDepth = coneLength - m_coneApexOffset - m_beyondTargetMargin;
        float detectLength = ConeDetectLength(coneLength);
        for (int i = 0; i < m_entries.Count; i++)
        {
            CutoutEntry entry = m_entries[i];
            if (entry.Renderer == null)
                continue;

            bool isTarget = true;
            if (m_useCone)
            {
                isTarget = entry.HasBounds
                    && entry.Bounds.SqrDistance(center) <= radiusSqr
                    && (!m_skipBeyondTarget || NearDepth(entry.Bounds, cameraPosition, viewDirection) < maxNearDepth)
                    && TouchesCone(entry, coneApex, coneAxis, detectLength, entry.IsTarget ? cosExitHalf : cosHalf);
            }

            float targetWeight = isTarget ? 1f : 0f;
            if (isTarget == entry.IsTarget && entry.Weight == targetWeight)
                continue;

            // 강도를 시간에 따라 옮기고, 단계가 바뀔 때만 렌더러 값을 씁니다.
            entry.IsTarget = isTarget;
            entry.Weight = Mathf.MoveTowards(entry.Weight, targetWeight, fadeStep);
            int level = FadeLevelOf(entry.Weight);
            if (level != entry.AppliedLevel)
            {
                entry.AppliedLevel = level;
                ApplyCutoutBits(entry.Renderer, level);
            }

            m_entries[i] = entry;
        }
    }

    /// <summary>잘림 강도(0~1)를 렌더러에 적을 단계로 바꿉니다. 0이면 -1(대상 아님), 그 밖에는 0~3입니다.</summary>
    private static int FadeLevelOf(float weight)
    {
        return weight <= 0f ? -1 : Mathf.Clamp(Mathf.CeilToInt(weight * FadeLevelCount) - 1, 0, FadeLevelCount - 1);
    }

    /// <summary>렌더러의 대상 비트와 강도 단계 비트를 씁니다. 단계가 -1이면 모두 끕니다.</summary>
    private static void ApplyCutoutBits(Renderer targetRenderer, int level)
    {
        uint mask = targetRenderer.renderingLayerMask & ~CutoutBits;
        if (level >= 0)
            mask |= (1u << RenderingLayerBit) | ((uint)level << FadeLevelShift);

        targetRenderer.renderingLayerMask = mask;
    }

    /// <summary>
    /// 등록부의 캐싱 바운드를 맞춥니다. 움직이지 않는 것은 그대로 두고, 움직인 것과 아직 캐싱하지 못한 것만 다시 읽습니다.
    /// </summary>
    /// <remarks>
    /// 움직였는지는 <see cref="Transform.hasChanged"/>로 봅니다. 위치·회전·크기가 바뀌면(부모가 움직여도) Unity가 켜 주는 표시로,
    /// 캐싱할 때 끄고 다시 켜진 것만 갱신합니다. 이전 프레임과 변화가 없으면 마지막 값이 그대로 캐시로 남습니다.
    /// 정적 배치(Static Batching)된 렌더러는 움직일 수 없어 보지 않습니다. Static 표시만으로는 고정이라 볼 수 없습니다(정문 철망 문은 Static 표시지만 시작 뒤 움직임).
    /// 파괴된 렌더러는 등록을 풀고 자리를 비워 다음 등록에 씁니다.
    /// </remarks>
    private void RefreshEntryBounds()
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            CutoutEntry entry = m_entries[i];
            if (ReferenceEquals(entry.Renderer, null))
                continue;

            if (entry.Renderer == null)
            {
                ReleaseEntry(i);
                continue;
            }

            if (entry.HasBounds && (!entry.Movable || !entry.Transform.hasChanged))
                continue;

            // 꺼져 있는 렌더러는 바운드가 비어 있어 캐싱하지 않고, 켜졌을 때 캐싱합니다.
            if (!entry.Renderer.enabled || !entry.Renderer.gameObject.activeInHierarchy)
                continue;

            entry.Bounds = entry.Renderer.bounds;
            // 정적 배치된 렌더러는 합쳐진 메시라 로컬 바운드가 원래 물체 기준이 아니므로 월드 바운드로 판정합니다.
            entry.HasOrientedBounds = entry.Movable && entry.Renderer is not SkinnedMeshRenderer;
            if (entry.HasOrientedBounds)
            {
                entry.LocalBounds = entry.Renderer.localBounds;
                entry.LocalToWorld = entry.Renderer.localToWorldMatrix;
                entry.WorldToLocal = entry.Renderer.worldToLocalMatrix;
            }

            entry.HasBounds = true;
            entry.Transform.hasChanged = false;
            m_entries[i] = entry;
        }
    }

    /// <summary>등록 검사 간격이 지났으면 새로 생긴 후보 렌더러를 찾아 등록합니다.</summary>
    private void RegisterNewRenderersIfDue()
    {
        if (m_registerScanInterval <= 0f || Time.unscaledTime < m_nextRegisterScanTime)
            return;

        m_nextRegisterScanTime = Time.unscaledTime + m_registerScanInterval;
        RegisterCandidates(FindObjectsInactive.Exclude);
    }

    /// <summary>바운드에서 카메라에 가장 가까운 면의 깊이(카메라에서 시선 방향으로 잰 거리)입니다.</summary>
    private static float NearDepth(Bounds bounds, Vector3 cameraPosition, Vector3 viewDirection)
    {
        Vector3 extents = bounds.extents;
        float reach = Mathf.Abs(extents.x * viewDirection.x) + Mathf.Abs(extents.y * viewDirection.y) + Mathf.Abs(extents.z * viewDirection.z);
        return Vector3.Dot(bounds.center - cameraPosition, viewDirection) - reach;
    }

    /// <summary>
    /// 오브젝트가 원뿔(꼭짓점에서 축 방향으로 정한 길이, 반각의 cos)에 닿는지 여부입니다.
    /// 축 위 몇 지점에서 오브젝트의 가장 가까운 지점을 구해, 그 지점이 꼭짓점에서 본 각도로 원뿔 안이면 닿은 것으로 봅니다.
    /// </summary>
    /// <remarks>
    /// 가장 가까운 지점은 회전을 반영한 실제 상자(로컬 바운드)에서 구합니다. 월드 축 정렬 바운드는 비스듬히 놓인 오브젝트(정문 철망 문 등)에서
    /// 크게 부풀어, 실제로는 원뿔 밖(플레이어 옆·앞)에 있는데도 귀퉁이가 원뿔에 걸려 대상이 됐습니다.
    /// </remarks>
    private static bool TouchesCone(CutoutEntry entry, Vector3 apex, Vector3 axis, float length, float cosHalf)
    {
        for (int i = 0; i < ConeAxisSamples; i++)
        {
            Vector3 onAxis = apex + axis * (length * i / (ConeAxisSamples - 1));
            Vector3 toPoint = ClosestPoint(entry, onAxis) - apex;
            float sqr = toPoint.sqrMagnitude;
            if (sqr < 1e-6f || Vector3.Dot(toPoint, axis) >= cosHalf * Mathf.Sqrt(sqr))
                return true;
        }

        return false;
    }

    /// <summary>원뿔이 실제로 감지하는 길이입니다. 감지 길이를 정했으면 꼭짓점-카메라 거리와 둘 중 짧은 쪽입니다.</summary>
    private float ConeDetectLength(float coneLength)
    {
        return m_coneLength > 0f ? Mathf.Min(m_coneLength, coneLength) : coneLength;
    }

    /// <summary>실행 중 구·원뿔·자르는 대상을 기즈모로 그립니다(<see cref="m_drawDebug"/>).</summary>
    private void OnDrawGizmos()
    {
        if (!m_drawDebug || !Application.isPlaying || CurrentSphere.w <= 0f)
            return;

        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        Gizmos.DrawWireSphere(CurrentSphere, CurrentSphere.w);

        if (m_useCone && m_lastConeLength > 1e-4f)
        {
            // 꼭짓점에서 카메라까지 반각으로 벌어지는 원뿔을, 카메라 쪽 끝 원과 옆선으로 그립니다.
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
            float drawLength = ConeDetectLength(m_lastConeLength);
            Vector3 endCenter = m_lastConeApex + m_lastConeAxis * drawLength;
            float endRadius = drawLength * Mathf.Tan(m_coneHalfAngle * Mathf.Deg2Rad);
            Vector3 side = Vector3.Cross(m_lastConeAxis, Mathf.Abs(m_lastConeAxis.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(side, m_lastConeAxis);
            const int Segments = 24;
            Vector3 previous = endCenter + side * endRadius;
            for (int i = 1; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector3 point = endCenter + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * endRadius;
                Gizmos.DrawLine(previous, point);
                if (i % 3 == 0)
                    Gizmos.DrawLine(m_lastConeApex, point);
                previous = point;
            }

            Gizmos.DrawLine(m_lastConeApex, endCenter);
        }

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        for (int i = 0; i < m_entries.Count; i++)
        {
            CutoutEntry entry = m_entries[i];
            if (!entry.IsTarget || entry.Renderer == null)
                continue;

            if (entry.HasOrientedBounds)
            {
                Gizmos.matrix = entry.LocalToWorld;
                Gizmos.DrawWireCube(entry.LocalBounds.center, entry.LocalBounds.size);
                Gizmos.matrix = Matrix4x4.identity;
            }
            else
            {
                Gizmos.DrawWireCube(entry.Bounds.center, entry.Bounds.size);
            }
        }
    }

    /// <summary>오브젝트에서 <paramref name="point"/>에 가장 가까운 지점입니다. 로컬 바운드가 있으면 회전을 반영한 상자에서 구합니다.</summary>
    private static Vector3 ClosestPoint(CutoutEntry entry, Vector3 point)
    {
        if (!entry.HasOrientedBounds)
            return entry.Bounds.ClosestPoint(point);

        Vector3 local = entry.WorldToLocal.MultiplyPoint3x4(point);
        Vector3 min = entry.LocalBounds.min;
        Vector3 max = entry.LocalBounds.max;
        local = new Vector3(Mathf.Clamp(local.x, min.x, max.x), Mathf.Clamp(local.y, min.y, max.y), Mathf.Clamp(local.z, min.z, max.z));
        return entry.LocalToWorld.MultiplyPoint3x4(local);
    }

    /// <summary>
    /// 지정 레이어 렌더러와 <see cref="OcclusionCutoutTarget"/> 아래 렌더러에 전용 비트를 켭니다.
    /// 같은 셰이더를 쓰는 다른 레이어 물체는 잘리지 않습니다.
    /// </summary>
    private void TagRenderers()
    {
        UntagRenderers();
        RegisterCandidates(FindObjectsInactive.Include);
        m_nextRegisterScanTime = Time.unscaledTime + m_registerScanInterval;
    }

    /// <summary>지정 레이어 렌더러와 <see cref="OcclusionCutoutTarget"/> 아래 렌더러 중 아직 등록하지 않은 것을 등록합니다.</summary>
    /// <param name="inactive">꺼진 오브젝트까지 찾을지입니다. 시작 때는 포함하고, 늦은 등록 검사 때는 켜진 것만 봅니다.</param>
    private void RegisterCandidates(FindObjectsInactive inactive)
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(inactive, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            if ((m_cutoutLayers.value & (1 << renderers[i].gameObject.layer)) != 0)
                RegisterRenderer(renderers[i]);
        }

        OcclusionCutoutTarget[] targets = FindObjectsByType<OcclusionCutoutTarget>(inactive, FindObjectsSortMode.None);
        for (int i = 0; i < targets.Length; i++)
        {
            Renderer[] children = targets[i].GetComponentsInChildren<Renderer>(inactive == FindObjectsInactive.Include);
            for (int j = 0; j < children.Length; j++)
                RegisterRenderer(children[j]);
        }
    }

    /// <summary>
    /// 렌더러를 등록부에 올리고 전용 비트를 켭니다. 비어 있는 자리가 있으면 그 자리를 다시 씁니다. 이미 등록돼 있으면 아무것도 하지 않습니다.
    /// </summary>
    /// <remarks>
    /// 처음부터 전용 비트가 켜져 있던 렌더러는 이 컴포넌트가 켠 것이 아니므로 등록하지 않습니다. 끌 때 남의 비트를 지우지 않기 위해서입니다.
    /// 바운드는 켜져 있으면 바로 캐싱하고, 꺼져 있으면 켜질 때 캐싱합니다.
    /// </remarks>
    private void RegisterRenderer(Renderer targetRenderer)
    {
        if (targetRenderer == null || m_entryIndex.ContainsKey(targetRenderer))
            return;

        uint bit = 1u << RenderingLayerBit;
        if ((targetRenderer.renderingLayerMask & bit) != 0)
            return;

        ApplyCutoutBits(targetRenderer, FadeLevelCount - 1);
        bool visible = targetRenderer.enabled && targetRenderer.gameObject.activeInHierarchy;
        Transform targetTransform = targetRenderer.transform;
        targetTransform.hasChanged = false;
        CutoutEntry entry = new CutoutEntry
        {
            Renderer = targetRenderer,
            Transform = targetTransform,
            Bounds = visible ? targetRenderer.bounds : default,
            HasBounds = visible,
            Movable = !targetRenderer.isPartOfStaticBatch,
            IsTarget = true,
            Weight = 1f,
            AppliedLevel = FadeLevelCount - 1,
        };

        int slot;
        if (m_freeSlots.Count > 0)
        {
            slot = m_freeSlots.Pop();
            m_entries[slot] = entry;
        }
        else
        {
            slot = m_entries.Count;
            m_entries.Add(entry);
        }

        m_entryIndex[targetRenderer] = slot;
    }

    /// <summary>파괴된 렌더러의 등록을 풀고 자리를 비워 둡니다. 비운 자리는 다음 등록 때 다시 씁니다.</summary>
    private void ReleaseEntry(int slot)
    {
        m_entryIndex.Remove(m_entries[slot].Renderer);
        m_entries[slot] = default;
        m_freeSlots.Push(slot);
    }

    private void UntagRenderers()
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            Renderer targetRenderer = m_entries[i].Renderer;
            if (targetRenderer != null)
                targetRenderer.renderingLayerMask &= ~CutoutBits;
        }

        m_entries.Clear();
        m_entryIndex.Clear();
        m_freeSlots.Clear();
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
