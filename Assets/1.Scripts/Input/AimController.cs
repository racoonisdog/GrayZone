using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 플레이어의 조준 카메라, 조준 UI, 조준 방향 회전, IK 리그, 사격 및 재장전 입력을 제어하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 <see cref="PlayerInputs"/>, <see cref="ThirdPersonController"/>,
/// <see cref="Animator"/>, <see cref="AudioSource"/>를 같은 GameObject의 필수 참조로 사용합니다.
/// 필수 참조는 <c>Awake</c>에서 캐싱하고, 누락 시 컴포넌트를 비활성화하여 런타임 null 참조를 방지합니다.
/// </remarks>
[RequireComponent(typeof(PlayerInputs))]
[RequireComponent(typeof(ThirdPersonController))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(AudioSource))]
public class AimController : MonoBehaviour
{
    [Tooltip("이 플레이어에 적용할 공용 밸런스 SO입니다. 비어 있으면 Inspector 값을 그대로 씁니다.")]
    [SerializeField] private PlayerCommonBalanceSO m_balanceSO;

    private const int WeaponLayerIndex = 1;
    private const float AimRotationLerpSpeed = 50.0f;

    // 이 시간(초) 이상 사격이 끊기면 좌우 킥 번갈이 패턴을 첫 발부터 다시 시작합니다.
    private const float KickPatternResetGap = 0.25f;

    private static readonly int AnimIDShoot = Animator.StringToHash("IsShoot");
    private static readonly int AnimIDReload = Animator.StringToHash("DoReload");

    /// <summary>전투 시점 상태입니다. 조준선 디버그 캡처를 이 상태의 전환 시점에만 수행합니다.</summary>
    private enum CombatStance
    {
        /// <summary>비전투 자유 TPS 시점입니다.</summary>
        Free,

        /// <summary>힙파이어(비조준 사격) 백뷰입니다.</summary>
        Hipfire,

        /// <summary>ADS(조준) 백뷰입니다.</summary>
        Ads,
    }

    /// <summary>시각 킥(롤·FOV 펀치)의 회복 방식입니다. 실제 탄착에는 영향이 없습니다.</summary>
    public enum VisualKickRecoveryMode
    {
        /// <summary>반동과 같은 회복 속도를 공유합니다. 연사 중에는 0으로 안 꺼지고 밴드로 누적됩니다.</summary>
        MatchRecoil,

        /// <summary>발사 간격(ShootDelay) 기준 N발 안에 거의 회복합니다. 다음 발 전에 대부분 리셋되어 발당 펀치가 또렷합니다.</summary>
        PerShotReset,
    }

    [Foldout("Aim Options")]
    [Tooltip("조준 중 활성화할 Cinemachine 카메라입니다.")]
    [FormerlySerializedAs("aimCam")]
    [SerializeField] private CinemachineCamera m_aimCamera;

    [Tooltip("조준선으로 사용할 UI 오브젝트입니다.")]
    [FormerlySerializedAs("aimImage")]
    [SerializeField] private GameObject m_aimImage;

    [Tooltip("켜면 조준/힙파이어 상태가 아니어도 조준선을 항상 표시합니다.")]
    [SerializeField] private bool m_showAimImageAlways = true;

    [Tooltip("탄퍼짐 조준선 UI 컨트롤러입니다. 비워두면 Aim Image 하위 또는 자기 하위에서 자동으로 찾습니다.")]
    [SerializeField] private CrosshairController m_crosshairController;


    [Tooltip("지향점(LookPoint)을 표시하거나 상체 회전 IK 타겟으로 사용할 오브젝트입니다. 캐릭터가 항상 바라보는 먼 지점을 따라갑니다.")]
    [FormerlySerializedAs("m_aimTarget")]
    [FormerlySerializedAs("aimObj")]
    [SerializeField] private GameObject m_lookTarget;

    [Tooltip("지향점(LookPoint)을 카메라 전방 이 거리에 항상 둡니다. 레이캐스트와 무관하게 늘 먼 지점을 바라보며, 무기 히트스캔 사거리보다 작으면 사거리만큼으로 보정됩니다.")]
    [FormerlySerializedAs("m_aimTargetDistance")]
    [FormerlySerializedAs("aimObjDis")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_lookDistance = 100.0f;

    [Tooltip("조준점(카메라 트레이스) 및 탄착점(총구 히트스캔) 판정에 사용할 레이어입니다. 비어 있으면 무기 히트스캔 레이어 또는 전체를 사용합니다.")]
    [FormerlySerializedAs("targetLayer")]
    [SerializeField] private LayerMask m_targetLayer;

    [Foldout("Hitscan Aim Options")]
    [FormerlySerializedAs("m_hitscanObstructionMarker")]
    [SerializeField] private GameObject m_hitscanBlockMarker;

    [FormerlySerializedAs("m_hitscanObstructionMarkerOffset")]
    [BalanceField]
    [SerializeField] private float m_hitscanBlockMarkerOffset = 0.01f;

    [Foldout("Hipfire Options")]
    [Tooltip("힙파이어(비조준 사격) 후 백뷰를 유지하다 자유 시점으로 복귀하기까지의 유지 시간(초)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireHoldDuration = 2.0f;

    [Foldout("Combat Zoom Options")]
    [Tooltip("ADS(조준) 시 백뷰 카메라 FOV입니다. 값이 작을수록 더 확대됩니다.")]
    [BalanceField]
    [Clamp(Min = 1)]
    [SerializeField] private float m_adsFov = 20.0f;

    [Tooltip("힙파이어(비조준) 시 백뷰 카메라 FOV입니다. 줌 없는 기본 시야 값(기본 30)입니다.")]
    [BalanceField]
    [Clamp(Min = 1)]
    [SerializeField] private float m_hipfireFov = 30.0f;

    [Tooltip("ADS↔힙파이어 전환 시 FOV 보간 속도입니다. 매우 크게 두면 즉시 전환에 가까워집니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_zoomLerpSpeed = 10.0f;

    [Foldout("Recoil Visual Kick Options")]
    [Tooltip("켜면 발사마다 실제 조준과 탄착에 영향을 주는 피치/요 반동을 적용합니다. 플레이테스트 트레이너에서 즉시 켜고 끌 수 있습니다.")]
    [SerializeField] private bool m_enableAimRecoil = true;

    [Tooltip("켜면 발사마다 카메라 롤과 FOV 펀치 시각 킥을 적용합니다. 실제 조준과 탄착에는 영향을 주지 않습니다.")]
    [SerializeField] private bool m_enableVisualKick = true;

    [Tooltip("시각 킥 회복 방식입니다. MatchRecoil=반동과 같은 속도(연사 중 밴드로 누적), PerShotReset=발사 간격 기준 N발 안에 회복(발당 리셋). Play Mode에서 바꿔가며 체감을 비교할 수 있습니다.")]
    [SerializeField] private VisualKickRecoveryMode m_visualKickRecoveryMode = VisualKickRecoveryMode.MatchRecoil;

    [Tooltip("PerShotReset일 때, 시각 킥이 거의(~95%) 회복되는 데 걸리는 발수(무기 ShootDelay 기준)입니다. 1이면 다음 발 전에 거의 리셋됩니다.")]
    [ShowIf(nameof(m_visualKickRecoveryMode), VisualKickRecoveryMode.PerShotReset)]
    [BalanceField]
    [Clamp(Min = 0.01)]
    [SerializeField] private float m_visualKickRecoverShots = 1.0f;

    [EndIf]
    [Tooltip("누적될 수 있는 카메라 롤(Dutch) 상한(도)입니다. 유지 없이 발당 순간 펀치 후 회복합니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_visualKickMaxRoll = 3.0f;

    [Tooltip("누적될 수 있는 FOV 펀치 상한(도)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_visualKickMaxFovPunch = 5.0f;

    [Tooltip("켜면 카메라 롤 킥의 상승과 회복을 곡선 하나로 처리합니다. 끄면 발사 순간 즉시 더하고 회복만 보간합니다.")]
    [SerializeField] private bool m_useRollKickEnvelope = false;

    [Tooltip("카메라 롤 킥 곡선 하나의 길이(초)입니다. 정규화 시간 0~1을 재는 기준입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_rollKickEnvelopeDuration = 0.25f;

    [Tooltip("카메라 롤 킥 곡선입니다. x는 정규화 시간(0~1), y는 세기 배율입니다. y가 가장 큰 x가 피크 위치입니다.")]
    [SerializeField]
    private AnimationCurve m_rollKickEnvelopeCurve = ImpulseEnvelope.BuildCurve(0.2f, 1.0f, 1.0f);

    [Tooltip("켜면 FOV 펀치의 상승과 회복을 곡선으로 처리합니다. 힙파이어와 ADS를 따로 둡니다.")]
    [SerializeField] private bool m_useFovPunchEnvelope = false;

    [Tooltip("힙파이어 FOV 펀치 곡선 하나의 길이(초)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireFovPunchEnvelopeDuration = 0.2f;

    [Tooltip("힙파이어 FOV 펀치 곡선입니다.")]
    [SerializeField]
    private AnimationCurve m_hipfireFovPunchEnvelopeCurve = ImpulseEnvelope.BuildCurve(0.2f, 1.0f, 1.0f);

    [Tooltip("ADS FOV 펀치 곡선 하나의 길이(초)입니다. 조준 중에는 화면이 확대돼 같은 펀치도 더 크게 보이므로 따로 둡니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_adsFovPunchEnvelopeDuration = 0.2f;

    [Tooltip("ADS FOV 펀치 곡선입니다.")]
    [SerializeField]
    private AnimationCurve m_adsFovPunchEnvelopeCurve = ImpulseEnvelope.BuildCurve(0.2f, 1.0f, 1.0f);

    [Tooltip("켜면 ADS 확대·축소를 지속시간과 곡선으로 처리합니다. 끄면 기존처럼 FOV 전환 속도 하나로 양쪽을 함께 보간합니다.")]
    [SerializeField] private bool m_useZoomEnvelope = false;

    [Tooltip("ADS 진입(확대)에 걸리는 시간(초)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_zoomInDuration = 0.15f;

    [Tooltip("ADS 진입 곡선입니다. x는 진행률(0~1), y는 목표 FOV까지의 비율입니다. 0에서 시작해 1로 끝나야 합니다.")]
    [SerializeField]
    private AnimationCurve m_zoomInCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

    [Tooltip("ADS 해제(축소)에 걸리는 시간(초)입니다. 진입과 따로 둘 수 있어 빠르게 들어가고 느리게 나오는 식이 가능합니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_zoomOutDuration = 0.2f;

    [Tooltip("ADS 해제 곡선입니다. x는 진행률(0~1), y는 목표 FOV까지의 비율입니다.")]
    [SerializeField]
    private AnimationCurve m_zoomOutCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

    /// <summary>카메라 롤 킥 엔벨로프의 런타임 상태입니다.</summary>
    private readonly ImpulseEnvelope m_rollKickEnvelope = new ImpulseEnvelope();

    /// <summary>힙파이어에서 발생한 FOV 펀치 엔벨로프입니다.</summary>
    /// <remarks>
    /// ADS와 나눠 두는 이유는, 힙파이어에서 쏜 뒤 곧바로 조준하면 그 발의 곡선이
    /// ADS 지속시간으로 갈아타 도중에 모양이 바뀌기 때문입니다.
    /// </remarks>
    private readonly ImpulseEnvelope m_hipfireFovPunchEnvelope = new ImpulseEnvelope();

    /// <summary>ADS에서 발생한 FOV 펀치 엔벨로프입니다.</summary>
    private readonly ImpulseEnvelope m_adsFovPunchEnvelope = new ImpulseEnvelope();

    /// <summary>지금 진행 중인 FOV 전환의 시작 값입니다.</summary>
    private float m_zoomFromFov;

    /// <summary>지금 진행 중인 FOV 전환의 경과 시간(초)입니다.</summary>
    private float m_zoomElapsed;

    /// <summary>지난 프레임의 조준 상태입니다. 바뀐 프레임에 전환을 새로 시작하기 위한 것입니다.</summary>
    private bool m_zoomWasAds;


    [Foldout("IK Options")]
    [Tooltip("손 위치 보정에 사용할 Rig입니다.")]
    [FormerlySerializedAs("handRig")]
    [SerializeField] private Rig m_handRig;

    [Tooltip("조준 자세 보정에 사용할 Rig입니다.")]
    [FormerlySerializedAs("aimRig")]
    [SerializeField] private Rig m_aimRig;

    [Foldout("Audio Options")]
    [Tooltip("사격 사운드입니다. 실제 사격 사운드를 Gun가 처리한다면 비워둘 수 있습니다.")]
    [FormerlySerializedAs("shootingSound")]
    [SerializeField] private AudioClip m_shootingSound;

    [Tooltip("재장전 애니메이션 이벤트에서 사용할 사운드 배열입니다. 0: 탄창 제거, 1: 탄창 삽입, 2: 재장전 완료.")]
    [FormerlySerializedAs("reloadSound")]
    [SerializeField] private AudioClip[] m_reloadSounds;

    [Foldout("Debug")]
    [Tooltip("조준 중 총구→탄착점 히트스캔 레이를 그립니다.")]
    [SerializeField] private bool m_drawHitscanDebugRay = true;

    [Tooltip("카메라에서 조준점까지의 트레이스 선을 그립니다(캠→조준점). 총구 기준 탄착점 레이와 얼마나 벌어지는지 확인용입니다.")]
    [SerializeField] private bool m_drawAimTraceLine = false;

    [Tooltip("지향점(논리 조준, 킥 제거: green) 레이와 단순 카메라 forward(렌더 방향, 킥 포함: blue) 레이를 함께 그립니다. 사격 시 두 선이 벌어지면 카메라 킥이 에임과 분리된 것이고, 안 벌어지면 킥이 활성 카메라에 안 닿은 것입니다.")]
    [SerializeField] private bool m_drawCameraForwardRay = false;

    [Tooltip("지향점(캐릭터가 항상 바라보는 먼 지점)에 디버그 스피어를 그립니다.")]
    [SerializeField] private bool m_drawLookPointSphere = false;

    [Tooltip("조준점(카메라 트레이스가 잡은 실제 사격 목표)에 디버그 스피어를 그립니다.")]
    [SerializeField] private bool m_drawAimPointSphere = false;

    [Tooltip("탄착점(총구 히트스캔이 실제로 끝나는 지점)에 디버그 스피어를 그립니다.")]
    [SerializeField] private bool m_drawImpactPointSphere = false;

    [Tooltip("디버그 스피어의 반지름입니다.")]
    [SerializeField] private float m_debugSphereRadius = 0.15f;

    [Tooltip("사격이 실제로 발사될 때 탄착점에 디버그 마커 오브젝트를 생성합니다.")]
    [SerializeField] private bool m_spawnImpactMarkerOnShot = false;

    [Tooltip("사격 시 탄착점에 생성할 디버그 오브젝트(스피어 등)입니다. 비어 있으면 생성을 생략합니다.")]
    [SerializeField] private GameObject m_impactMarkerPrefab;

    [Tooltip("임팩트 마커를 유지할 시간(초)입니다. 프리팹이 비어 있으면 런타임 디버그 스피어에도 적용됩니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_impactMarkerLifetime = 1.5f;

    [Tooltip("프리팹이 비어 있을 때 생성하는 런타임 디버그 스피어의 지름(월드 단위)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_impactMarkerSize = 0.16f;

    private PlayerInputs m_input;
    private ThirdPersonController m_controller;
    private Animator m_animator;
    private AudioSource m_weaponAudioSource;
    private Gun m_weaponController;
    private Camera m_mainCamera;
    private EnemyController m_currentAimEnemy;
    private bool m_hasRequiredReferences;
    private bool m_inCombatStance;
    private bool m_isAds;
    private float m_hipfireTimer;
    private CombatStance m_lastCombatStance = CombatStance.Free;

    /// <summary>시각 킥(FOV 펀치)을 얹기 전의 기준 전투 FOV입니다. ADS/힙파이어 목표로 보간됩니다.</summary>
    private float m_baseFov = 60.0f;

    /// <summary>현재 카메라 롤(Dutch) 시각 킥 오프셋(도)입니다. 0으로 회복합니다. 에임/탄 무영향.</summary>
    private float m_visualKickRoll;

    /// <summary>현재 FOV 펀치 시각 킥 오프셋(도)입니다. 0으로 회복합니다. 에임/탄 무영향.</summary>
    private float m_visualKickFovPunch;

    /// <summary>좌우 킥 번갈이 패턴의 발 인덱스입니다. 버스트 간격이 벌어지면 리셋됩니다.</summary>
    private int m_kickShotIndex;

    /// <summary>마지막 킥 시각입니다. 버스트 사이 간격이 벌어지면 좌우 패턴을 첫 발부터 다시 시작합니다.</summary>
    private float m_lastKickTime = float.NegativeInfinity;

    /// <summary>조준 카메라 참조입니다.</summary>
    public CinemachineCamera AimCamera => m_aimCamera;

    /// <summary>조준 UI 오브젝트 참조입니다.</summary>
    public GameObject AimImage => m_aimImage;

    /// <summary>지향점(LookPoint)을 따라가는 상체 회전 IK 타겟 오브젝트 참조입니다.</summary>
    public GameObject LookTarget => m_lookTarget;

    /// <summary>지향점(LookPoint)을 둘 카메라 전방 거리입니다.</summary>
    public float LookDistance => m_lookDistance;

    /// <summary>조준점/탄착점 판정 레이어입니다.</summary>
    public LayerMask TargetLayer => m_targetLayer;

    /// <summary>탄퍼짐·피격 피드백 표시를 담당하는 선택형 조준선 컨트롤러입니다.</summary>
    public CrosshairController CrosshairController => m_crosshairController;

    /// <summary>조준/힙파이어 상태가 아니어도 조준선을 항상 표시할지 여부입니다.</summary>
    public bool ShowAimImageAlways => m_showAimImageAlways;

    /// <summary>총구 히트스캔이 장애물에 막힐 때 마커를 표면에서 띄울 거리입니다.</summary>
    public float HitscanBlockMarkerOffset => m_hitscanBlockMarkerOffset;

    /// <summary>힙파이어 사격 후 전투 자세를 유지하는 시간입니다.</summary>
    public float HipfireHoldDuration => m_hipfireHoldDuration;

    /// <summary>ADS 카메라 기본 FOV입니다.</summary>
    public float AdsFov => m_adsFov;

    /// <summary>힙파이어 카메라 기본 FOV입니다.</summary>
    public float HipfireFov => m_hipfireFov;

    /// <summary>ADS와 힙파이어 FOV 전환 보간 속도입니다.</summary>
    public float ZoomLerpSpeed => m_zoomLerpSpeed;

    /// <summary>현재 히트스캔이 조준 중인 적입니다.</summary>
    public EnemyController CurrentAimEnemy => m_currentAimEnemy;

    /// <summary>총구 기준 히트스캔이 중간 장애물에 막혔을 때 표시할 월드 마커입니다.</summary>
    public GameObject HitscanBlockMarker => m_hitscanBlockMarker;


    public AudioClip ShootingSound => m_shootingSound;

    /// <summary>재장전 사운드 클립 배열입니다.</summary>
    public AudioClip[] ReloadSounds => m_reloadSounds;

    /// <summary>실제 조준과 탄착에 영향을 주는 반동 적용 여부입니다.</summary>
    public bool AimRecoilEnabled => m_enableAimRecoil;

    /// <summary>카메라 롤과 FOV 펀치로 구성된 시각 킥 적용 여부입니다.</summary>
    public bool VisualKickEnabled => m_enableVisualKick;

    /// <summary>시각 킥 회복 방식입니다.</summary>
    public VisualKickRecoveryMode CurrentVisualKickRecoveryMode => m_visualKickRecoveryMode;

    /// <summary>PerShotReset 회복에 사용할 발수입니다.</summary>
    public float VisualKickRecoverShots => m_visualKickRecoverShots;

    /// <summary>누적 가능한 카메라 롤 상한(도)입니다.</summary>
    public float VisualKickMaxRoll => m_visualKickMaxRoll;

    /// <summary>누적 가능한 FOV 펀치 상한(도)입니다.</summary>
    public float VisualKickMaxFovPunch => m_visualKickMaxFovPunch;

    /// <summary>발사 시 탄착점에 임팩트 마커를 생성할지 여부입니다.</summary>
    public bool ImpactMarkerEnabled => m_spawnImpactMarkerOnShot;

    /// <summary>임팩트 마커 유지 시간(초)입니다.</summary>
    public float ImpactMarkerLifetime => m_impactMarkerLifetime;

    /// <summary>프리팹이 없을 때 생성하는 임팩트 마커 지름입니다.</summary>
    public float ImpactMarkerSize => m_impactMarkerSize;

    /// <summary>총구 기준 히트스캔 디버그 레이 표시 여부입니다.</summary>
    public bool HitscanDebugRayEnabled => m_drawHitscanDebugRay;

    /// <summary>카메라 조준점 트레이스 디버그 선 표시 여부입니다.</summary>
    public bool DrawAimTraceLine => m_drawAimTraceLine;

    /// <summary>논리 조준과 렌더 카메라 전방 비교 레이 표시 여부입니다.</summary>
    public bool DrawCameraForwardRay => m_drawCameraForwardRay;

    /// <summary>지향점 디버그 스피어 표시 여부입니다.</summary>
    public bool DrawLookPointSphere => m_drawLookPointSphere;

    /// <summary>카메라 조준점 디버그 스피어 표시 여부입니다.</summary>
    public bool DrawAimPointSphere => m_drawAimPointSphere;

    /// <summary>총구 기준 탄착점 디버그 스피어 표시 여부입니다.</summary>
    public bool DrawImpactPointSphere => m_drawImpactPointSphere;

    /// <summary>조준 디버그 스피어 반지름입니다.</summary>
    public float DebugSphereRadius => Mathf.Max(0.0f, m_debugSphereRadius);

    /// <summary>
    /// 조준 카메라 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 카메라입니다.</param>
    public void SetAimCamera(CinemachineCamera value) => m_aimCamera = value;

    /// <summary>
    /// 조준 UI 오브젝트 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 UI 오브젝트입니다.</param>
    public void SetAimImage(GameObject value) => m_aimImage = value;

    /// <summary>
    /// 지향점 IK 타겟 오브젝트 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 지향점 타겟 오브젝트입니다.</param>
    public void SetLookTarget(GameObject value) => m_lookTarget = value;

    /// <summary>
    /// 지향점(LookPoint)을 둘 카메라 전방 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 지향점 거리입니다.</param>
    public void SetLookDistance(float value) => m_lookDistance = value;

    /// <summary>
    /// 조준 Raycast 대상 레이어를 설정합니다.
    /// </summary>
    /// <param name="value">새 대상 레이어 마스크입니다.</param>
    public void SetTargetLayer(LayerMask value) => m_targetLayer = value;

    /// <summary>조준/힙파이어 상태가 아니어도 조준선을 항상 표시할지 여부를 설정합니다.</summary>
    /// <param name="value">항상 표시하려면 <c>true</c>입니다.</param>
    public void SetShowAimImageAlways(bool value)
    {
        m_showAimImageAlways = value;
        if (m_aimImage != null)
        {
            m_aimImage.SetActive(m_isAds || m_showAimImageAlways);
        }
    }

    /// <summary>힙파이어 사격 후 전투 자세를 유지하는 시간을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetHipfireHoldDuration(float value) => m_hipfireHoldDuration = value;

    /// <summary>ADS 카메라 기본 FOV를 설정합니다.</summary>
    /// <param name="value">1보다 작은 값은 1로 보정됩니다.</param>
    public void SetAdsFov(float value) => m_adsFov = value;

    /// <summary>힙파이어 카메라 기본 FOV를 설정합니다.</summary>
    /// <param name="value">1보다 작은 값은 1로 보정됩니다.</param>
    public void SetHipfireFov(float value) => m_hipfireFov = value;

    /// <summary>ADS와 힙파이어 FOV 전환 보간 속도를 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetZoomLerpSpeed(float value) => m_zoomLerpSpeed = value;

    /// <summary>
    /// 총구 기준 히트스캔 장애물 마커 오브젝트를 설정합니다.
    /// </summary>
    /// <param name="value">장애물 탄착점에 표시할 월드 오브젝트입니다. <c>null</c>이면 마커 표시를 생략합니다.</param>
    public void SetHitscanBlockMarker(GameObject value)
    {
        if (m_hitscanBlockMarker != null)
        {
            m_hitscanBlockMarker.SetActive(false);
        }

        m_hitscanBlockMarker = value;
        HideHitscanBlockMarker();
    }

    /// <summary>
    /// 장애물 마커가 표면과 겹치지 않도록 충돌 법선 방향으로 띄울 거리를 설정합니다.
    /// </summary>
    /// <param name="value">표면 법선 방향 오프셋입니다. 0보다 작은 값은 0으로 보정됩니다.</param>
    public void SetHitscanBlockMarkerOffset(float value)
    {
        m_hitscanBlockMarkerOffset = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 조준 중 총구 기준 히트스캔 디버그 레이 표시 여부를 설정합니다.
    /// </summary>
    /// <param name="value">표시하려면 <c>true</c>, 숨기려면 <c>false</c>입니다.</param>
    public void SetDrawHitscanDebugRay(bool value) => m_drawHitscanDebugRay = value;

    /// <summary>카메라 조준점 트레이스 디버그 선 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawAimTraceLine(bool value) => m_drawAimTraceLine = value;

    /// <summary>논리 조준과 렌더 카메라 전방 비교 레이 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawCameraForwardRay(bool value) => m_drawCameraForwardRay = value;

    /// <summary>지향점 디버그 스피어 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawLookPointSphere(bool value) => m_drawLookPointSphere = value;

    /// <summary>카메라 조준점 디버그 스피어 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawAimPointSphere(bool value) => m_drawAimPointSphere = value;

    /// <summary>총구 기준 탄착점 디버그 스피어 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawImpactPointSphere(bool value) => m_drawImpactPointSphere = value;

    /// <summary>조준 디버그 스피어 반지름을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetDebugSphereRadius(float value) => m_debugSphereRadius = Mathf.Max(0.0f, value);

    /// <summary>
    /// 사격 사운드 클립을 설정합니다.
    /// </summary>
    /// <param name="value">새 사격 사운드 클립입니다.</param>
    public void SetShootingSound(AudioClip value) => m_shootingSound = value;

    /// <summary>
    /// 재장전 사운드 배열을 설정합니다.
    /// </summary>
    /// <param name="value">새 재장전 사운드 배열입니다.</param>
    public void SetReloadSounds(AudioClip[] value) => m_reloadSounds = value;

    /// <summary>실제 조준과 탄착에 영향을 주는 반동 적용 여부를 설정합니다.</summary>
    /// <param name="value">반동을 적용하려면 <c>true</c>입니다.</param>
    public void SetAimRecoilEnabled(bool value) => m_enableAimRecoil = value;

    /// <summary>카메라 롤과 FOV 펀치 시각 킥 적용 여부를 설정합니다.</summary>
    /// <param name="value">시각 킥을 적용하려면 <c>true</c>입니다.</param>
    public void SetVisualKickEnabled(bool value)
    {
        m_enableVisualKick = value;

        if (!m_enableVisualKick)
        {
            m_visualKickRoll = 0.0f;
            m_visualKickFovPunch = 0.0f;
            m_rollKickEnvelope.Clear();
            m_hipfireFovPunchEnvelope.Clear();
            m_adsFovPunchEnvelope.Clear();
        }
    }

    /// <summary>카메라 롤 킥을 곡선 엔벨로프로 처리할지 여부입니다.</summary>
    public bool UseRollKickEnvelope => m_useRollKickEnvelope;

    /// <summary>카메라 롤 킥 곡선 하나의 길이(초)입니다.</summary>
    public float RollKickEnvelopeDuration => Mathf.Max(0.0f, m_rollKickEnvelopeDuration);

    /// <summary>카메라 롤 킥 곡선입니다.</summary>
    public AnimationCurve RollKickEnvelopeCurve => m_rollKickEnvelopeCurve;

    /// <summary>FOV 펀치를 곡선 엔벨로프로 처리할지 여부입니다.</summary>
    public bool UseFovPunchEnvelope => m_useFovPunchEnvelope;

    /// <summary>힙파이어 FOV 펀치 곡선 하나의 길이(초)입니다.</summary>
    public float HipfireFovPunchEnvelopeDuration => Mathf.Max(0.0f, m_hipfireFovPunchEnvelopeDuration);

    /// <summary>힙파이어 FOV 펀치 곡선입니다.</summary>
    public AnimationCurve HipfireFovPunchEnvelopeCurve => m_hipfireFovPunchEnvelopeCurve;

    /// <summary>ADS FOV 펀치 곡선 하나의 길이(초)입니다.</summary>
    public float AdsFovPunchEnvelopeDuration => Mathf.Max(0.0f, m_adsFovPunchEnvelopeDuration);

    /// <summary>ADS FOV 펀치 곡선입니다.</summary>
    public AnimationCurve AdsFovPunchEnvelopeCurve => m_adsFovPunchEnvelopeCurve;

    /// <summary>ADS 확대·축소를 곡선으로 처리할지 여부입니다.</summary>
    public bool UseZoomEnvelope => m_useZoomEnvelope;

    /// <summary>ADS 진입(확대)에 걸리는 시간(초)입니다.</summary>
    public float ZoomInDuration => Mathf.Max(0.0f, m_zoomInDuration);

    /// <summary>ADS 진입 곡선입니다.</summary>
    public AnimationCurve ZoomInCurve => m_zoomInCurve;

    /// <summary>ADS 해제(축소)에 걸리는 시간(초)입니다.</summary>
    public float ZoomOutDuration => Mathf.Max(0.0f, m_zoomOutDuration);

    /// <summary>ADS 해제 곡선입니다.</summary>
    public AnimationCurve ZoomOutCurve => m_zoomOutCurve;

    /// <summary>카메라 롤 킥 엔벨로프 사용 여부를 설정합니다.</summary>
    /// <remarks>방식을 바꿀 때 진행 중이던 값이 다른 방식에 남지 않도록 함께 정리합니다.</remarks>
    public void SetUseRollKickEnvelope(bool value)
    {
        if (m_useRollKickEnvelope == value)
        {
            return;
        }

        m_useRollKickEnvelope = value;
        m_rollKickEnvelope.Clear();
        m_visualKickRoll = 0.0f;
    }

    /// <summary>카메라 롤 킥 곡선 길이를 설정합니다.</summary>
    public void SetRollKickEnvelopeDuration(float value) => m_rollKickEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>카메라 롤 킥 곡선을 교체합니다.</summary>
    /// <remarks>복제해서 보관합니다. 참조를 그대로 들면 밸런스 SO의 곡선과 같은 인스턴스를 공유합니다.</remarks>
    public void SetRollKickEnvelopeCurve(AnimationCurve value)
    {
        m_rollKickEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>FOV 펀치 엔벨로프 사용 여부를 설정합니다.</summary>
    public void SetUseFovPunchEnvelope(bool value)
    {
        if (m_useFovPunchEnvelope == value)
        {
            return;
        }

        m_useFovPunchEnvelope = value;
        m_hipfireFovPunchEnvelope.Clear();
        m_adsFovPunchEnvelope.Clear();
        m_visualKickFovPunch = 0.0f;
    }

    /// <summary>힙파이어 FOV 펀치 곡선 길이를 설정합니다.</summary>
    public void SetHipfireFovPunchEnvelopeDuration(float value)
        => m_hipfireFovPunchEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>힙파이어 FOV 펀치 곡선을 교체합니다.</summary>
    public void SetHipfireFovPunchEnvelopeCurve(AnimationCurve value)
    {
        m_hipfireFovPunchEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>ADS FOV 펀치 곡선 길이를 설정합니다.</summary>
    public void SetAdsFovPunchEnvelopeDuration(float value)
        => m_adsFovPunchEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>ADS FOV 펀치 곡선을 교체합니다.</summary>
    public void SetAdsFovPunchEnvelopeCurve(AnimationCurve value)
    {
        m_adsFovPunchEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>ADS 확대·축소 곡선 사용 여부를 설정합니다.</summary>
    /// <remarks>방식을 바꾸면 진행률을 초기화해 지금 FOV에서 새 전환이 시작되게 합니다.</remarks>
    public void SetUseZoomEnvelope(bool value)
    {
        if (m_useZoomEnvelope == value)
        {
            return;
        }

        m_useZoomEnvelope = value;
        m_zoomFromFov = m_baseFov;
        m_zoomElapsed = 0.0f;
    }

    /// <summary>ADS 진입 시간을 설정합니다.</summary>
    public void SetZoomInDuration(float value) => m_zoomInDuration = Mathf.Max(0.0f, value);

    /// <summary>ADS 해제 시간을 설정합니다.</summary>
    public void SetZoomOutDuration(float value) => m_zoomOutDuration = Mathf.Max(0.0f, value);

    /// <summary>ADS 진입 곡선을 교체합니다.</summary>
    public void SetZoomInCurve(AnimationCurve value)
    {
        m_zoomInCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>ADS 해제 곡선을 교체합니다.</summary>
    public void SetZoomOutCurve(AnimationCurve value)
    {
        m_zoomOutCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>시각 킥 회복 방식을 설정합니다.</summary>
    /// <param name="value">새 회복 방식입니다.</param>
    public void SetVisualKickRecoveryMode(VisualKickRecoveryMode value) => m_visualKickRecoveryMode = value;

    /// <summary>PerShotReset 회복에 사용할 발수를 설정합니다.</summary>
    /// <param name="value">0보다 작은 값은 0.01로 보정됩니다.</param>
    public void SetVisualKickRecoverShots(float value) => m_visualKickRecoverShots = value;

    /// <summary>누적 가능한 카메라 롤 상한을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetVisualKickMaxRoll(float value) => m_visualKickMaxRoll = value;

    /// <summary>누적 가능한 FOV 펀치 상한을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetVisualKickMaxFovPunch(float value) => m_visualKickMaxFovPunch = value;

    /// <summary>발사 시 탄착점 임팩트 마커 생성 여부를 설정합니다.</summary>
    /// <param name="value">생성하려면 <c>true</c>입니다.</param>
    public void SetImpactMarkerEnabled(bool value) => m_spawnImpactMarkerOnShot = value;

    /// <summary>임팩트 마커 유지 시간을 설정합니다.</summary>
    /// <param name="value">0.05초보다 작은 값은 0.05초로 보정됩니다.</param>
    public void SetImpactMarkerLifetime(float value) => m_impactMarkerLifetime = Mathf.Max(0.05f, value);

    /// <summary>프리팹이 없을 때 생성하는 임팩트 마커 지름을 설정합니다.</summary>
    /// <param name="value">0.01보다 작은 값은 0.01로 보정됩니다.</param>
    public void SetImpactMarkerSize(float value) => m_impactMarkerSize = Mathf.Max(0.01f, value);

    /// <summary>
    /// Unity 생명주기 초기화 함수입니다.
    /// 필수 참조를 캐싱하고 누락 여부를 검증합니다.
    /// </summary>
    /// <summary>
    /// 지정된 SO가 있을 때 공용 BindManager로 같은 이름의 필드 값을 적용합니다.
    /// </summary>
    /// <returns>이번 바인딩의 집계 결과입니다. SO가 없으면 기본값입니다.</returns>
    /// <remarks>
    /// 같은 SO를 이 오브젝트의 다른 컴포넌트도 각자 바인드합니다. 대상이 요구한 필드만 가져가므로
    /// 서로 간섭하지 않고, 컴포넌트 간 Awake 실행 순서에도 의존하지 않습니다.
    /// </remarks>
    private BalanceBindResult BindConfiguredBalance()
    {
        if (m_balanceSO == null)
        {
            return default;
        }

        return BindManager.Instance.Bind(m_balanceSO, this, this);
    }

    private void Awake()
    {
        CacheRequiredReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        CacheOptionalCrosshairController();
        m_hasRequiredReferences = true;
        BindConfiguredBalance();
        ApplyCombatStanceState(false, false, 0.0f);

        if (m_weaponController != null)
        {
            m_weaponController.OnHitFeedback += OnWeaponHitFeedback;
        }
    }

    /// <summary>
    /// Unity 생명주기 종료 함수입니다. 구독한 무기 피드백 이벤트를 해제합니다.
    /// </summary>
    private void OnDestroy()
    {
        if (m_weaponController != null)
        {
            m_weaponController.OnHitFeedback -= OnWeaponHitFeedback;
        }
    }

    /// <summary>
    /// 히트스캔 피격 피드백을 조준선 UI로 전달합니다(히트마커 색상 구분 + 킬 시 해골 표시).
    /// </summary>
    /// <param name="feedback">헤드샷·킬 여부를 담은 피격 피드백입니다.</param>
    private void OnWeaponHitFeedback(CombatDamage.HitFeedback feedback)
    {
        if (m_crosshairController == null)
        {
            return;
        }

        m_crosshairController.ShowHitMarker(feedback.Headshot);

        if (feedback.Killed)
        {
            m_crosshairController.ShowKill();
        }
    }

    /// <summary>
    /// 매 프레임 조준, 사격, 재장전 입력을 처리합니다.
    /// </summary>
    private void Update()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        UpdateAimAndWeapon();
        UpdateCrosshairDebugOnStanceChange();
        UpdateReloadCrosshair();
    }

    /// <summary>
    /// 무기 재장전 상태와 탄약 게이지 채움 비율을 조준선 UI에 전달합니다(재장전 중 크로스헤어↔탄약 아이콘 스왑 + 아크 게이지).
    /// </summary>
    private void UpdateReloadCrosshair()
    {
        if (m_crosshairController == null)
        {
            return;
        }

        bool reloading = m_weaponController != null && m_weaponController.IsReloading;
        m_crosshairController.SetReloading(reloading);

        if (m_weaponController != null)
        {
            // 게이지 채움: 재장전 중에는 재장전 진행도, 평소에는 현재 탄약 비율을 표시합니다.
            float fill = reloading
                ? m_weaponController.ReloadProgress
                : m_weaponController.MaxBullet > 0
                    ? (float)m_weaponController.CurrentBullet / m_weaponController.MaxBullet
                    : 0.0f;
            m_crosshairController.SetAmmoGaugeFill(fill);
        }
    }

    /// <summary>
    /// 같은 GameObject 또는 자식 오브젝트에서 필요한 참조를 캐싱합니다.
    /// </summary>
    private void CacheRequiredReferences()
    {
        m_input = GetComponent<PlayerInputs>();
        m_controller = GetComponent<ThirdPersonController>();
        m_animator = GetComponent<Animator>();
        m_weaponAudioSource = GetComponent<AudioSource>();
        m_weaponController = GetComponentInChildren<Gun>();
        m_mainCamera = Camera.main;
    }

    /// <summary>
    /// Aim Image 또는 플레이어 하위에 배치된 선택형 조준선 컨트롤러를 캐싱합니다.
    /// </summary>
    private void CacheOptionalCrosshairController()
    {
        if (m_crosshairController != null)
        {
            return;
        }

        if (m_aimImage != null)
        {
            m_crosshairController = m_aimImage.GetComponentInChildren<CrosshairController>(true);
        }

        if (m_crosshairController == null)
        {
            m_crosshairController = GetComponentInChildren<CrosshairController>(true);
        }
    }

    /// <summary>
    /// 필수 참조가 정상적으로 준비되었는지 검증합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 true입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_input == null)
        {
            Debug.LogError("[AimController] PlayerInputs 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_controller == null)
        {
            Debug.LogError("[AimController] ThirdPersonController 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_animator == null)
        {
            Debug.LogError("[AimController] Animator 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_weaponAudioSource == null)
        {
            Debug.LogError("[AimController] AudioSource 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_mainCamera == null)
        {
            Debug.LogError("[AimController] MainCamera 태그를 가진 카메라를 찾지 못했습니다.", this);
            isValid = false;
        }

        if (m_aimCamera == null)
        {
            Debug.LogError("[AimController] Aim Camera가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_aimImage == null)
        {
            Debug.LogError("[AimController] Aim Image가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_lookTarget == null)
        {
            Debug.LogError("[AimController] Look Target이 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_handRig == null)
        {
            Debug.LogError("[AimController] Hand Rig가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_aimRig == null)
        {
            Debug.LogError("[AimController] Aim Rig가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_weaponController == null)
        {
            Debug.LogWarning("[AimController] Gun를 자식 오브젝트에서 찾지 못했습니다. 사격과 재장전 무기 처리는 생략됩니다.", this);
        }

        return isValid;
    }

    /// <summary>
    /// 재장전 입력과 조준 입력을 순서대로 처리합니다.
    /// </summary>
    private void UpdateAimAndWeapon()
    {
        if (HandleReloadInput())
        {
            return;
        }

        if (m_controller.IsReload)
        {
            ExitCombatStance();
            return;
        }

        // 조준(ADS): Aim 입력이 최우선. 백뷰 + (slice②)줌.
        if (m_input.Aim)
        {
            EnterCombatStance(true);
            UpdateCombat();
            return;
        }

        // ADS가 아닌 힙파이어/잔류 상태는 전력질주에 양보합니다.
        if (m_input.Sprint)
        {
            ExitCombatStance();
            return;
        }

        // 비조준 사격(힙파이어): Shoot 입력 시 백뷰 진입/유지하고 복귀 타이머를 리셋합니다.
        if (m_input.Shoot)
        {
            EnterCombatStance(false);
            m_hipfireTimer = m_hipfireHoldDuration;
            UpdateCombat();
            return;
        }

        // 힙파이어 잔류: 마지막 사격 후 유지 시간 동안 백뷰를 유지하고, 끝나면 자유 시점으로 복귀합니다.
        // 잔류는 타이머 기준이라, 우클릭(ADS)에 잠깐 다녀와도 잔류 시간이 남아 있으면 힙파이어로 복귀해 취소되지 않습니다.
        // (순수 ADS 후 해제는 타이머가 0이라 이 분기를 건너뛰고 즉시 복귀합니다.)
        if (m_inCombatStance && m_hipfireTimer > 0.0f)
        {
            m_isAds = false;
            m_hipfireTimer -= Time.deltaTime;

            if (m_hipfireTimer > 0.0f)
            {
                UpdateCombat();
                return;
            }
        }

        ExitCombatStance();
    }

    /// <summary>
    /// 재장전 입력이 들어온 경우 재장전 상태와 애니메이션을 시작합니다.
    /// </summary>
    /// <returns>재장전 입력을 처리했으면 true입니다.</returns>
    private bool HandleReloadInput()
    {
        if (!m_input.Reload)
        {
            return false;
        }

        m_input.ReloadInput(false);

        if (m_controller.IsReload)
        {
            return true;
        }

        // 풀 탄창(또는 이미 재장전 중)이면 재장전 상태(IsReload)와 애니메이션을 아예 세우지 않습니다.
        // 무기측 StartReload는 풀 탄창을 무시하므로, 여기서 막지 않으면 조작 잠금만 걸려 헛장전/데드락이 됩니다.
        if (m_weaponController != null && !m_weaponController.CanReload)
        {
            // 풀 탄창 등으로 장전이 막힌 경우 빈 장전(드라이) 피드백만 재생합니다(클립이 없으면 무음).
            m_weaponController.PlayEmptyReloadSound();
            return true;
        }

        BeginReload();

        return true;
    }

    /// <summary>
    /// 재장전 시작 시 한 번만 필요한 조준 해제, 애니메이션, 무기 상태를 적용합니다.
    /// </summary>
    private void BeginReload()
    {
        m_inCombatStance = false;
        m_isAds = false;
        m_hipfireTimer = 0.0f;
        SetAimState(false);
        HideHitscanBlockMarker();
        SetRigWeight(0.0f);
        m_animator.SetBool(AnimIDShoot, false);
        m_animator.SetLayerWeight(WeaponLayerIndex, 1.0f);
        m_animator.SetTrigger(AnimIDReload);
        m_controller.SetReload(true);

        if (m_weaponController != null)
        {
            m_weaponController.StartReload();
        }
    }

    /// <summary>
    /// 전투 자세(백뷰)에 진입합니다. 조준(ADS)과 힙파이어가 공유하며, ads로 줌 여부만 구분합니다.
    /// </summary>
    /// <param name="ads">조준(ADS)이면 true, 힙파이어면 false입니다.</param>
    private void EnterCombatStance(bool ads)
    {
        m_isAds = ads;

        if (!m_inCombatStance)
        {
            // 새 교전 진입이므로 좌우 킥 번갈이 패턴을 첫 발부터 시작합니다.
            m_kickShotIndex = 0;
            ApplyCombatStanceState(true, false, 1.0f);
            // 자유 카메라에서 백뷰로 막 진입한 프레임은 목표 FOV로 즉시 스냅(줌 점프 방지).
            ApplyCombatZoom(true);
            UpdateCrosshair(true);
        }
    }

    /// <summary>
    /// 전투 자세(조준/힙파이어) 중 매 프레임 지향점/조준점/탄착점, 회전, 마커, 사격 입력을 처리합니다.
    /// </summary>
    private void UpdateCombat()
    {
        // 지향점: 레이캐스트와 무관하게 항상 카메라 전방 먼 고정점. 캐릭터(몸통/상체 IK)가 일관되게 이 지점을 바라봅니다.
        Vector3 lookPoint = ResolveLookPoint();
        ApplyLookTarget(lookPoint);
        RotateToLookPoint(lookPoint);

        // 조준점: 카메라 트레이스가 잡은 실제 사격 목표. 총알이 겨누는 지점입니다.
        Vector3 aimPoint = ResolveAimPoint(lookPoint);

        // 탄착점: 총구에서 조준점으로 가다가 걸리는 지점(shotInfo.EndPoint). 실제 사격이 이 결과를 사용합니다.
        Gun.HitscanShotInfo shotInfo = EvaluateHitscanShot(aimPoint);
        UpdateCurrentAimEnemy(shotInfo);

        DrawHitscanDebugRay(shotInfo);
        DrawAimTraceDebugLine(shotInfo);
        DrawCameraForwardDebugRay(lookPoint);
        DrawAimDebugSpheres(lookPoint, shotInfo);
        UpdateHitscanBlockMarker(shotInfo);
        UpdateShootState(shotInfo);
        ApplyCombatZoom(false);
        UpdateCrosshair(false);
    }

    /// <summary>
    /// 전투 자세 카메라(백뷰)의 FOV와 시각 킥(롤·FOV 펀치)을 상태에 맞춰 적용합니다. ADS는 확대(작은 FOV), 힙파이어는 기본 FOV입니다.
    /// </summary>
    /// <param name="snap"><c>true</c>면 목표 FOV로 즉시 설정하고 시각 킥을 초기화합니다. <c>false</c>면 보간하고 시각 킥을 회복시킵니다.</param>
    /// <remarks>
    /// 기준 FOV(<see cref="m_baseFov"/>) 위에 FOV 펀치를 얹고, 롤(Dutch)도 조준 카메라 렌즈에만 적용합니다.
    /// 롤·FOV 펀치는 시각 전용 juice라 조준값(<see cref="ThirdPersonController.LogicalAimRotation"/>)이나 탄착에는 영향이 없습니다.
    /// </remarks>
    private void ApplyCombatZoom(bool snap)
    {
        if (m_aimCamera == null)
        {
            return;
        }

        float targetFov = m_isAds ? m_adsFov : m_hipfireFov;
        UpdateBaseFov(targetFov, snap);

        if (snap)
        {
            // 전투 자세 진입 등 스냅 시엔 시각 킥도 초기화(재진입 시 롤/펀치 잔상 방지).
            m_visualKickRoll = 0.0f;
            m_visualKickFovPunch = 0.0f;
            m_rollKickEnvelope.Clear();
            m_hipfireFovPunchEnvelope.Clear();
            m_adsFovPunchEnvelope.Clear();
        }
        else
        {
            // 유지(hold) 없이 발당 순간 펀치 후 회복시켜 지속 틸트/멀미를 피합니다. 회복 속도는 모드에 따라 결정합니다.
            float recover = Mathf.Clamp01(Time.deltaTime * GetVisualKickRecoverySpeed());

            if (!m_useRollKickEnvelope)
            {
                m_visualKickRoll = Mathf.Lerp(m_visualKickRoll, 0.0f, recover);
            }

            if (!m_useFovPunchEnvelope)
            {
                m_visualKickFovPunch = Mathf.Lerp(m_visualKickFovPunch, 0.0f, recover);
            }
        }

        // 엔벨로프를 켠 축은 살아 있는 곡선의 합이 그대로 현재 값입니다. 상한은 합에 걸어 둡니다.
        float roll = m_useRollKickEnvelope
            ? Mathf.Clamp(
                m_rollKickEnvelope.Evaluate(m_rollKickEnvelopeDuration, m_rollKickEnvelopeCurve, Time.deltaTime),
                -m_visualKickMaxRoll,
                m_visualKickMaxRoll)
            : m_visualKickRoll;

        float fovPunch = m_visualKickFovPunch;
        if (m_useFovPunchEnvelope)
        {
            float hipfireSum = m_hipfireFovPunchEnvelope.Evaluate(
                m_hipfireFovPunchEnvelopeDuration, m_hipfireFovPunchEnvelopeCurve, Time.deltaTime);
            float adsSum = m_adsFovPunchEnvelope.Evaluate(
                m_adsFovPunchEnvelopeDuration, m_adsFovPunchEnvelopeCurve, Time.deltaTime);

            fovPunch = Mathf.Clamp(hipfireSum + adsSum, 0.0f, m_visualKickMaxFovPunch);
        }

        // 기준 FOV 위에 펀치를 얹고, 롤은 렌즈에만 반영(에임/탄 무영향).
        m_aimCamera.Lens.FieldOfView = m_baseFov + fovPunch;
        m_aimCamera.Lens.Dutch = roll;
    }

    /// <summary>
    /// 기준 FOV를 목표 값으로 옮깁니다.
    /// </summary>
    /// <param name="targetFov">이번 자세의 목표 FOV입니다.</param>
    /// <param name="snap">즉시 맞출지 여부입니다.</param>
    /// <remarks>
    /// 엔벨로프를 끄면 기존처럼 속도 하나로 지수 보간합니다. 이 방식은 목표에 점근하기만 해서
    /// 지속시간 개념이 없고, 확대와 축소가 같은 속도를 씁니다.
    ///
    /// 켜면 자세가 바뀐 프레임에 그때의 FOV를 시작점으로 기억하고 진행률을 0부터 셉니다.
    /// 그래서 진입과 해제에 서로 다른 시간과 곡선을 줄 수 있고, 전환 도중에 자세를 되돌려도
    /// 현재 FOV에서 새 전환이 시작돼 튀지 않습니다.
    /// </remarks>
    private void UpdateBaseFov(float targetFov, bool snap)
    {
        if (snap)
        {
            m_baseFov = targetFov;
            m_zoomFromFov = targetFov;
            m_zoomElapsed = 0.0f;
            m_zoomWasAds = m_isAds;
            return;
        }

        if (!m_useZoomEnvelope)
        {
            m_baseFov = Mathf.Lerp(m_baseFov, targetFov, Time.deltaTime * m_zoomLerpSpeed);
            m_zoomWasAds = m_isAds;
            return;
        }

        if (m_zoomWasAds != m_isAds)
        {
            m_zoomWasAds = m_isAds;
            m_zoomFromFov = m_baseFov;
            m_zoomElapsed = 0.0f;
        }

        float duration = m_isAds ? m_zoomInDuration : m_zoomOutDuration;
        if (duration <= 0.0f)
        {
            m_baseFov = targetFov;
            return;
        }

        m_zoomElapsed = Mathf.Min(m_zoomElapsed + Time.deltaTime, duration);
        float progress = m_zoomElapsed / duration;
        AnimationCurve curve = m_isAds ? m_zoomInCurve : m_zoomOutCurve;
        float weight = curve != null && curve.length > 0 ? curve.Evaluate(progress) : progress;

        m_baseFov = Mathf.LerpUnclamped(m_zoomFromFov, targetFov, weight);
    }

    /// <summary>
    /// 현재 모드에 따른 시각 킥 회복 속도(초당)를 반환합니다.
    /// </summary>
    /// <returns>
    /// MatchRecoil이면 반동 회복 속도(<see cref="ThirdPersonController.RecoilRecoverySpeed"/>)를 공유해 연사 중 밴드로 누적합니다.
    /// PerShotReset이면 발사 간격(ShootDelay)의 <see cref="m_visualKickRecoverShots"/>배 안에 ~95% 회복되도록 역산해(e^(-speed·T)=0.05 → speed=3/T) 발당 리셋에 가깝게 만듭니다.
    /// </returns>
    private float GetVisualKickRecoverySpeed()
    {
        if (m_visualKickRecoveryMode == VisualKickRecoveryMode.PerShotReset && m_weaponController != null)
        {
            float shots = Mathf.Max(0.01f, m_visualKickRecoverShots);
            float interval = Mathf.Max(0.0001f, m_weaponController.ShootDelay * shots);
            return 3.0f / interval;
        }

        return m_controller != null ? m_controller.RecoilRecoverySpeed : 8.0f;
    }

    /// <summary>
    /// 현재 무기 탄퍼짐 방사각과 전투 카메라 FOV를 조준선 UI 컨트롤러에 전달합니다.
    /// </summary>
    /// <param name="snap"><c>true</c>면 조준선 위치를 즉시 반영합니다.</param>
    private void UpdateCrosshair(bool snap)
    {
        if (m_crosshairController == null)
        {
            return;
        }

        float spreadDegrees = m_weaponController != null ? m_weaponController.GetCurrentSpread(m_isAds) : 0.0f;
        SpreadDistribution distribution = m_weaponController != null
            ? m_weaponController.Distribution
            : SpreadDistribution.Gaussian;
        float concentration = m_weaponController != null ? m_weaponController.SpreadConcentration : 3.0f;
        // 시각 FOV 펀치가 아니라 기준 FOV를 써서, 크로스헤어가 발사 juice에 따라 숨쉬지 않게 합니다.
        float fovDegrees = m_baseFov;
        m_crosshairController.SetSpread(spreadDegrees, distribution, concentration, fovDegrees, snap);
    }

    /// <summary>
    /// 전투 스탠스(자유시점/힙파이어/ADS)가 바뀐 프레임에만 조준선 디버그 스냅샷을 한 번 캡처합니다.
    /// </summary>
    /// <remarks>
    /// 매 프레임 디버그 갱신을 피하고, 전환 시점에 상태별 목표 FOV(ADS/힙파이어)와 자유시점 카메라 FOV를 크로스헤어로 전달합니다.
    /// 시각 표시(<see cref="UpdateCrosshair"/>)는 매 프레임 그대로 갱신되며, 이 캡처는 디버그 값에만 영향을 줍니다.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void UpdateCrosshairDebugOnStanceChange()
    {
        if (m_crosshairController == null)
        {
            return;
        }

        CombatStance stance = !m_inCombatStance
            ? CombatStance.Free
            : (m_isAds ? CombatStance.Ads : CombatStance.Hipfire);

        if (stance == m_lastCombatStance)
        {
            return;
        }

        m_lastCombatStance = stance;

        // 값 복사가 아니라 라이브 소스 포인터를 연결만 한다. 이후 디버그 표시는 이 포인터로 현재값을 읽는다.
        switch (stance)
        {
            case CombatStance.Ads:
                m_crosshairController.BindSpreadDebug(
                    "Ads",
                    () => m_weaponController != null ? m_weaponController.GetCurrentSpread(true) : 0.0f,
                    () => m_adsFov);
                break;

            case CombatStance.Hipfire:
                m_crosshairController.BindSpreadDebug(
                    "Hipfire",
                    () => m_weaponController != null ? m_weaponController.GetCurrentSpread(false) : 0.0f,
                    () => m_hipfireFov);
                break;

            default:
                m_crosshairController.BindSpreadDebug(
                    "Free",
                    () => 0.0f,
                    () => m_mainCamera != null ? m_mainCamera.fieldOfView : 60.0f);
                break;
        }
    }

    /// <summary>
    /// 조준선 UI를 기본 간격 상태로 되돌립니다.
    /// </summary>
    private void ResetCrosshair()
    {
        if (m_crosshairController != null)
        {
            m_crosshairController.ResetSpread();
        }
    }

    /// <summary>
    /// 전투 자세를 해제하고 자유 TPS 시점으로 복귀합니다.
    /// </summary>
    private void ExitCombatStance()
    {
        if (!m_inCombatStance)
        {
            return;
        }

        m_isAds = false;
        m_hipfireTimer = 0.0f;
        ApplyCombatStanceState(false, false, 0.0f);
    }

    /// <summary>
    /// 레이캐스트와 무관하게 카메라 전방 먼 고정점을 지향점(LookPoint)으로 계산합니다.
    /// </summary>
    /// <returns>카메라 전방 지향 거리(무기 히트스캔 사거리 이상)에 위치한 월드 지향점입니다.</returns>
    /// <remarks>항상 먼 지점을 바라보므로 가까운 장애물이 끼어도 몸통/상체 회전이 급변하지 않습니다.</remarks>
    private Vector3 ResolveLookPoint()
    {
        Transform cameraTransform = m_mainCamera.transform;

        // 지향점은 항상 먼 지점이어야 하므로, 무기 히트스캔 사거리보다 짧지 않게 보정합니다.
        float lookDistance = m_lookDistance;
        if (m_weaponController != null)
        {
            lookDistance = Mathf.Max(lookDistance, m_weaponController.HitscanRange);
        }

        return cameraTransform.position + GetAimForward() * lookDistance;
    }

    /// <summary>
    /// 조준 계산에 사용할 전방 방향을 반환합니다. 카메라 킥(시각 흔들림)이 빠진 논리 조준 방향이라 에임이 흔들림과 독립됩니다.
    /// </summary>
    /// <returns>카메라 킥이 빠진 논리 조준의 정규화 전방 방향입니다.</returns>
    /// <remarks>
    /// PO 핸드오프 기준(2026-06-30 카메라 킥/AimPoint): AimPoint는 카메라 킥이 반영된 렌더 방향이 아니라 논리 조준 방향으로 계산한다.
    /// 화면(뷰)은 카메라 킥으로 흔들리되 지향점/조준점/탄착점은 킥의 영향을 받지 않는다. 명중 영향은 탄퍼짐(과 추후 총기 반동)이 담당한다.
    /// </remarks>
    private Vector3 GetAimForward()
    {
        if (m_controller != null)
        {
            return m_controller.LogicalAimForward;
        }

        return m_mainCamera.transform.forward;
    }

    /// <summary>
    /// 카메라 트레이스로 조준점(AimPoint, 실제 사격 목표)을 계산합니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점입니다. 카메라 트레이스가 아무것도 못 맞히면 이 먼 지점을 조준점으로 사용합니다.</param>
    /// <returns>카메라가 크로스헤어로 가리키는 실제 월드 지점(미충돌 시 지향점)입니다.</returns>
    /// <remarks>총알은 총구→이 지점으로 향하므로, 가까운 적도 시차 없이 정확히 겨눕니다.</remarks>
    private Vector3 ResolveAimPoint(Vector3 lookPoint)
    {
        Transform cameraTransform = m_mainCamera.transform;
        float aimDistance = Vector3.Distance(cameraTransform.position, lookPoint);

        // 레이어가 지정돼 있으면 그것을, 아니면 무기 히트스캔 레이어(없으면 전체)에서 소유(본인) 레이어를 제외해
        // 카메라 트레이스가 자기 콜라이더를 조준점으로 잡지 않게 합니다.
        int mask;
        if (m_targetLayer.value != 0)
        {
            mask = m_targetLayer.value;
        }
        else
        {
            int baseMask = m_weaponController != null ? m_weaponController.HitscanLayerMask.value : ~0;
            mask = baseMask & ~(1 << gameObject.layer);
        }

        if (Physics.Raycast(cameraTransform.position, GetAimForward(), out RaycastHit hit, aimDistance, mask, QueryTriggerInteraction.UseGlobal))
        {
            return hit.point;
        }

        return lookPoint;
    }

    /// <summary>
    /// 히트스캔 충돌 결과로부터 현재 조준 중인 적을 갱신합니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>조준 대상 적 판정은 실제 탄착 경로(총구 히트스캔)를 기준으로 합니다.</remarks>
    private void UpdateCurrentAimEnemy(Gun.HitscanShotInfo shotInfo)
    {
        m_currentAimEnemy = shotInfo.HasHit && shotInfo.Hit.collider != null
            ? shotInfo.Hit.collider.GetComponentInParent<EnemyController>()
            : null;
    }

    /// <summary>
    /// 상체 회전 IK가 바라보는 지향점 타겟 오브젝트의 위치를 지향점으로 갱신합니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점(먼 지점)입니다.</param>
    /// <remarks>이 오브젝트는 MultiAimConstraint(상체 회전)의 source이며, 손목 IK는 별도 타겟을 사용해 영향을 받지 않습니다.</remarks>
    private void ApplyLookTarget(Vector3 lookPoint)
    {
        if (m_lookTarget == null)
        {
            return;
        }

        m_lookTarget.transform.position = lookPoint;
    }

    /// <summary>
    /// 조준점(카메라 트레이스 목표)과 무기 총구를 기준으로 현재 프레임의 히트스캔 사격 정보를 계산합니다.
    /// </summary>
    /// <param name="targetPosition">카메라 트레이스로 계산한 조준점(AimPoint)입니다. 총구가 이 지점을 향해 발사합니다.</param>
    /// <returns>총구 원점, 발사 방향, 탄착점(EndPoint), 충돌 및 중간 장애물 여부를 포함한 사격 정보입니다.</returns>
    /// <remarks>이 결과는 조준 마커 표시와 실제 히트스캔 사격 처리에서 동일하게 사용됩니다.</remarks>
    private Gun.HitscanShotInfo EvaluateHitscanShot(Vector3 targetPosition)
    {
        Gun.HitscanShotInfo shotInfo = new()
        {
            AimPoint = targetPosition,
            EndPoint = targetPosition,
            FrameCount = Time.frameCount,
        };

        if (m_weaponController == null || m_weaponController.FirePos == null)
        {
            return shotInfo;
        }

        Transform firePos = m_weaponController.FirePos;
        Vector3 origin = firePos.position;
        Vector3 aimVector = targetPosition - origin;
        float aimDistance = aimVector.magnitude;
        Vector3 direction = aimDistance <= 0.0001f
            ? firePos.forward
            : aimVector / aimDistance;

        float hitscanRange = Mathf.Max(0.0f, m_weaponController.HitscanRange);
        float rayDistance = aimDistance <= 0.0001f
            ? hitscanRange
            : Mathf.Min(hitscanRange, aimDistance);

        shotInfo.IsValid = rayDistance > Gun.HitscanAimTolerance;
        shotInfo.Origin = origin;
        shotInfo.Direction = direction;
        shotInfo.EndPoint = origin + direction * rayDistance;

        if (!shotInfo.IsValid)
        {
            return shotInfo;
        }

        if (!Physics.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                rayDistance,
                m_weaponController.HitscanLayerMask,
                QueryTriggerInteraction.UseGlobal))
        {
            return shotInfo;
        }

        shotInfo.HasHit = true;
        shotInfo.Hit = hit;
        shotInfo.EndPoint = hit.point;
        shotInfo.IsObstructed = aimDistance > 0.0001f
            && hit.distance < aimDistance - Gun.HitscanAimTolerance;

        return shotInfo;
    }

    /// <summary>
    /// 히트스캔 사격 정보에 중간 장애물이 있으면 월드 마커를 탄착점에 표시합니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    private void UpdateHitscanBlockMarker(Gun.HitscanShotInfo shotInfo)
    {
        if (m_hitscanBlockMarker == null || m_weaponController == null)
        {
            return;
        }

        if (!shotInfo.IsValid || !shotInfo.IsObstructed)
        {
            HideHitscanBlockMarker();
            return;
        }

        Vector3 markerPosition = shotInfo.EndPoint + shotInfo.Hit.normal * m_hitscanBlockMarkerOffset;
        Quaternion markerRotation = GetHitscanBlockMarkerRotation(shotInfo.Hit.normal);

        m_hitscanBlockMarker.transform.SetPositionAndRotation(markerPosition, markerRotation);
        m_hitscanBlockMarker.SetActive(true);
    }

    private Quaternion GetHitscanBlockMarkerRotation(Vector3 hitNormal)
    {
        if (hitNormal.sqrMagnitude <= 0.0001f)
        {
            return m_hitscanBlockMarker.transform.rotation;
        }

        Vector3 surfaceNormal = hitNormal.normalized;
        Vector3 tangentForward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal);

        if (tangentForward.sqrMagnitude <= 0.0001f && m_aimCamera != null)
        {
            tangentForward = Vector3.ProjectOnPlane(m_aimCamera.transform.up, surfaceNormal);
        }

        if (tangentForward.sqrMagnitude <= 0.0001f)
        {
            tangentForward = Vector3.Cross(surfaceNormal, Vector3.right);
        }

        if (tangentForward.sqrMagnitude <= 0.0001f)
        {
            tangentForward = Vector3.Cross(surfaceNormal, Vector3.forward);
        }

        return Quaternion.LookRotation(tangentForward.normalized, surfaceNormal);
    }

    /// <summary>
    /// 히트스캔 장애물 마커를 숨깁니다.
    /// </summary>
    private void HideHitscanBlockMarker()
    {
        if (m_hitscanBlockMarker != null)
        {
            m_hitscanBlockMarker.SetActive(false);
        }
    }

    /// <summary>
    /// 조준 중 계산된 히트스캔 사격 정보를 Scene 뷰 디버그 레이로 표시합니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawHitscanDebugRay(Gun.HitscanShotInfo shotInfo)
    {
        if (!m_drawHitscanDebugRay || !shotInfo.IsValid)
        {
            return;
        }

        Debug.DrawLine(
            shotInfo.Origin,
            shotInfo.EndPoint,
            shotInfo.IsObstructed ? Color.red : Color.yellow,
            0.0f,
            false);
    }

    /// <summary>
    /// 카메라에서 조준점까지의 트레이스 선을 그립니다(캠→조준점). 총구 기준 탄착점 레이와의 벌어짐 확인용입니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다. <see cref="Gun.HitscanShotInfo.AimPoint"/>가 조준점입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawAimTraceDebugLine(Gun.HitscanShotInfo shotInfo)
    {
        if (!m_drawAimTraceLine)
        {
            return;
        }

        Debug.DrawLine(m_mainCamera.transform.position, shotInfo.AimPoint, Color.cyan, 0.0f, false);
    }

    /// <summary>
    /// 지향점(논리 조준, 킥 제거) 레이와 단순 카메라 forward(렌더 방향, 킥 포함) 레이를 함께 그려 카메라 킥의 에임 분리 여부를 확인합니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점(논리 조준 먼 지점)입니다.</param>
    /// <remarks>
    /// green = 지향점 레이(`GetAimForward`, 킥 제거 / 실제 사격 방향), blue = 카메라 forward 레이(`Camera.main.forward`, 킥 포함 / 렌더 방향).
    /// 사격 시 두 선이 벌어지면 킥이 카메라에만 적용되고 에임에는 분리된 것이며, 안 벌어지면 킥이 활성 카메라에 닿지 않은 것입니다.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawCameraForwardDebugRay(Vector3 lookPoint)
    {
        if (!m_drawCameraForwardRay)
        {
            return;
        }

        Transform cameraTransform = m_mainCamera.transform;
        float rayLength = Vector3.Distance(cameraTransform.position, lookPoint);

        // 지향점(논리 조준, 킥 제거) 레이.
        Debug.DrawLine(cameraTransform.position, lookPoint, Color.green, 0.0f, false);

        // 단순 카메라 forward(렌더 방향, 킥 포함) 레이.
        Debug.DrawLine(
            cameraTransform.position,
            cameraTransform.position + cameraTransform.forward * rayLength,
            Color.blue,
            0.0f,
            false);
    }

    /// <summary>
    /// 지향점(green)/조준점(cyan)/탄착점(magenta)에 디버그 스피어를 그립니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점(캐릭터가 바라보는 먼 지점)입니다.</param>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>조준점은 <see cref="Gun.HitscanShotInfo.AimPoint"/>(카메라 트레이스 목표), 탄착점은 <see cref="Gun.HitscanShotInfo.EndPoint"/>(총구 히트스캔 최종 지점)입니다.</remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawAimDebugSpheres(Vector3 lookPoint, Gun.HitscanShotInfo shotInfo)
    {
        if (m_drawLookPointSphere)
        {
            DrawDebugSphere(lookPoint, m_debugSphereRadius, Color.green);
        }

        if (m_drawAimPointSphere)
        {
            DrawDebugSphere(shotInfo.AimPoint, m_debugSphereRadius, Color.cyan);
        }

        if (m_drawImpactPointSphere)
        {
            DrawDebugSphere(shotInfo.EndPoint, m_debugSphereRadius, Color.magenta);
        }
    }

    /// <summary>
    /// 세 직교 평면의 원으로 와이어 스피어를 한 프레임 동안 그립니다.
    /// </summary>
    /// <param name="center">스피어 중심 월드 좌표입니다.</param>
    /// <param name="radius">스피어 반지름입니다.</param>
    /// <param name="color">스피어 색상입니다.</param>
    /// <remarks><see cref="Debug.DrawLine"/> 기반이라 Scene 뷰, 그리고 Gizmos가 켜진 Game 뷰에서 표시됩니다.</remarks>
    private static void DrawDebugSphere(Vector3 center, float radius, Color color)
    {
        const int segments = 16;
        float step = 2.0f * Mathf.PI / segments;

        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            float b = (i + 1) * step;
            float ca = Mathf.Cos(a);
            float sa = Mathf.Sin(a);
            float cb = Mathf.Cos(b);
            float sb = Mathf.Sin(b);

            Debug.DrawLine(center + new Vector3(ca, sa, 0.0f) * radius, center + new Vector3(cb, sb, 0.0f) * radius, color, 0.0f, false);
            Debug.DrawLine(center + new Vector3(ca, 0.0f, sa) * radius, center + new Vector3(cb, 0.0f, sb) * radius, color, 0.0f, false);
            Debug.DrawLine(center + new Vector3(0.0f, ca, sa) * radius, center + new Vector3(0.0f, cb, sb) * radius, color, 0.0f, false);
        }
    }

    /// <summary>
    /// 사격이 발사된 프레임에 탄착점에 디버그 마커 오브젝트를 생성합니다.
    /// </summary>
    /// <param name="shotInfo">발사된 사격의 히트스캔 정보입니다.</param>
    /// <remarks>
    /// 토글이 켜진 개발 모드에서만 생성합니다. 마커 프리팹이 비어 있으면 플레이테스트에서도 바로 확인할 수 있도록
    /// 작은 마젠타 스피어를 임시로 만들고, 충돌 표면이 있으면 법선 방향으로 정렬합니다.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void SpawnImpactMarker(Gun.HitscanShotInfo shotInfo)
    {
        if (!GameDevMode.DebugFeaturesEnabled || !m_spawnImpactMarkerOnShot)
        {
            return;
        }

        Quaternion rotation = shotInfo.HasHit && shotInfo.Hit.normal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(shotInfo.Hit.normal)
            : Quaternion.identity;

        GameObject marker;
        if (m_impactMarkerPrefab != null)
        {
            marker = Instantiate(m_impactMarkerPrefab, shotInfo.EndPoint, rotation);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "ImpactMarker(Trainer)";
            marker.transform.SetPositionAndRotation(shotInfo.EndPoint, rotation);
            marker.transform.localScale = Vector3.one * m_impactMarkerSize;

            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer markerRenderer = marker.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                markerRenderer.material.color = Color.magenta;
            }
        }

        Destroy(marker, m_impactMarkerLifetime);
    }


    /// <summary>
    /// 지향점(먼 지점)을 향해 캐릭터 몸통의 수평 회전(yaw)을 보간합니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점(먼 지점)입니다. 항상 멀리 있으므로 가까운 장애물에 급회전하지 않습니다.</param>
    private void RotateToLookPoint(Vector3 lookPoint)
    {
        Vector3 aimDirection = lookPoint - transform.position;
        aimDirection.y = 0.0f;

        if (aimDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.forward = Vector3.Lerp(
            transform.forward,
            aimDirection.normalized,
            Time.deltaTime * AimRotationLerpSpeed);
    }

    /// <summary>
    /// 사격 입력 상태를 애니메이터와 무기 컨트롤러에 반영합니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    private void UpdateShootState(Gun.HitscanShotInfo shotInfo)
    {
        if (m_input.Shoot)
        {
            m_animator.SetBool(AnimIDShoot, true);

            if (m_weaponController != null)
            {
                //m_weaponController.TryShoot(targetPosition); // 오브젝트 풀링
                bool fired = m_weaponController.TryLayShoot(shotInfo, m_isAds, out Gun.HitscanShotInfo firedShot); // 히트스캔(탄퍼짐 적용)

                if (fired)
                {
                    SpawnImpactMarker(firedShot);
                    ApplyRecoilAndVisualKick();
                }
            }

            return;
        }

        m_animator.SetBool(AnimIDShoot, false);
    }

    /// <summary>
    /// 발사가 성사된 프레임에 무기별 수치를 읽어 (1) 에임에 영향을 주는 반동과 (2) 에임 무영향 시각 킥을 함께 가합니다.
    /// </summary>
    /// <remarks>
    /// 반동(에임): 좌우(요)는 매 발 <c>-RecoilYawKick ~ +RecoilYawKick</c> 무작위. <see cref="ThirdPersonController.AddRecoil"/>가
    /// 논리 조준에 얹어 탄착까지 밀며, 사격을 멈추면 자동 회복합니다.
    /// 시각 킥(juice): 카메라 롤(Dutch)과 FOV 펀치를 누적하며, 조준/탄착에는 영향이 없습니다(<see cref="ApplyCombatZoom"/>에서 회복·적용).
    /// </remarks>
    private void ApplyRecoilAndVisualKick()
    {
        if (m_controller == null || m_weaponController == null)
        {
            return;
        }

        // 버스트 사이 간격이 벌어졌으면 좌우 번갈이 패턴을 첫 발부터 다시 시작합니다.
        if (Time.time - m_lastKickTime > KickPatternResetGap)
        {
            m_kickShotIndex = 0;
        }
        m_lastKickTime = Time.time;

        // 좌우 패턴에 따라 이번 발의 Yaw 반동·롤 부호(및 크기)를 각각 독립적으로 결정합니다(같은 발 인덱스 공유).
        float yawSigned = ResolveKickValue(m_weaponController.YawKickPattern, m_weaponController.RecoilYawKick, m_kickShotIndex);
        float rollSigned = ResolveKickValue(m_weaponController.RollKickPattern, m_weaponController.RecoilRoll, m_kickShotIndex);
        m_kickShotIndex++;

        // (1) 반동 — 실제 조준을 밀어 탄착에도 영향(세로 pitch + 좌우 yaw).
        if (m_enableAimRecoil)
        {
            m_controller.AddRecoil(m_weaponController.RecoilPitchKick, yawSigned);
        }

        // (2) 시각 킥 — 롤·FOV 펀치 누적(조준/탄 무영향, 상한 클램프).
        if (m_enableVisualKick)
        {
            // 엔벨로프를 켠 축은 이번 발의 곡선 하나를 시작만 하고, 값은 매 프레임 합에서 나옵니다.
            if (m_useRollKickEnvelope)
            {
                m_rollKickEnvelope.Add(rollSigned);
            }
            else
            {
                m_visualKickRoll = Mathf.Clamp(m_visualKickRoll + rollSigned, -m_visualKickMaxRoll, m_visualKickMaxRoll);
            }

            // 조준 중에는 화면이 확대돼 같은 펀치도 더 크게 보이므로 크기를 자세별로 나눠 씁니다.
            float fovPunch = m_isAds ? m_weaponController.RecoilFovPunchAds : m_weaponController.RecoilFovPunch;

            if (m_useFovPunchEnvelope)
            {
                // 쏜 시점의 자세에 해당하는 엔벨로프에 넣습니다. 쏜 뒤 자세를 바꿔도 그 발의 길이와 모양이 유지됩니다.
                if (m_isAds)
                {
                    m_adsFovPunchEnvelope.Add(fovPunch);
                }
                else
                {
                    m_hipfireFovPunchEnvelope.Add(fovPunch);
                }
            }
            else
            {
                m_visualKickFovPunch = Mathf.Clamp(m_visualKickFovPunch + fovPunch, 0.0f, m_visualKickMaxFovPunch);
            }
        }
    }

    /// <summary>
    /// 좌우 킥 패턴에 따라 이번 발의 부호 있는 킥 값(도)을 계산합니다. 왼쪽을 음수로 둡니다.
    /// </summary>
    /// <param name="pattern">적용할 좌우 킥 패턴입니다.</param>
    /// <param name="magnitude">킥 크기(도)입니다. 0 이하이면 0을 반환합니다.</param>
    /// <param name="shotIndex">현재 발 인덱스입니다. 번갈이 패턴의 짝/홀 판정에 씁니다.</param>
    /// <returns>Random이면 ±범위 무작위, Alternate이면 발 인덱스로 좌우 교대한 부호 있는 크기입니다.</returns>
    /// <remarks>Yaw 반동과 시각 롤이 같은 발 인덱스를 공유하되 각자 자기 패턴으로 독립 계산됩니다.</remarks>
    private static float ResolveKickValue(KickSidePattern pattern, float magnitude, int shotIndex)
    {
        if (magnitude <= 0.0f)
        {
            return 0.0f;
        }

        if (pattern == KickSidePattern.Random)
        {
            return Random.Range(-magnitude, magnitude);
        }

        // 번갈이: 발 인덱스 짝/홀로 좌우 교대. 왼쪽 = 음수.
        bool even = (shotIndex % 2) == 0;
        bool leftFirst = pattern == KickSidePattern.AlternateLeftFirst;
        float sign = even == leftFirst ? -1.0f : 1.0f;
        return magnitude * sign;
    }

    /// <summary>
    /// 조준 카메라, 조준 UI, 이동 컨트롤러의 조준 이동 상태를 설정합니다.
    /// </summary>
    /// <param name="isAiming">조준 상태이면 true입니다.</param>
    private void SetAimState(bool isAiming)
    {
        bool showAimImage = isAiming || m_showAimImageAlways;

        if (m_aimCamera != null)
        {
            m_aimCamera.gameObject.SetActive(isAiming);
        }

        if (m_aimImage != null)
        {
            m_aimImage.SetActive(showAimImage);
        }

        if (m_crosshairController != null)
        {
            m_crosshairController.SetVisible(showAimImage);
        }

        if (!isAiming)
        {
            ResetCrosshair();
        }

        if (m_controller != null)
        {
            m_controller.SetAimMove(isAiming);
        }
    }

    /// <summary>
    /// 재장전 완료 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void Reload()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        FinishReloadVisualState(true);
        PlayWeaponSound(GetReloadSound(2));
    }

    /// <summary>
    /// 재장전 완료 후 조작 컨트롤러와 조준 보정 상태를 정리합니다.
    /// </summary>
    /// <param name="completeWeaponReload">무기 탄약도 완료 처리할지 여부입니다.</param>
    private void FinishReloadVisualState(bool completeWeaponReload)
    {
        m_controller.SetReload(false);
        m_inCombatStance = false;
        m_isAds = false;
        m_hipfireTimer = 0.0f;
        SetAimState(false);
        HideHitscanBlockMarker();
        SetRigWeight(0.0f);
        m_animator.SetLayerWeight(WeaponLayerIndex, 0.0f);
        m_animator.SetBool(AnimIDShoot, false);

        if (completeWeaponReload && m_weaponController != null)
        {
            m_weaponController.CompleteReload();
        }
    }

    /// <summary>
    /// 탄창 제거 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void ReloadWeaponClip()
    {
        if (m_weaponController != null)
        {
            m_weaponController.OnReloadMagOut();
        }

        PlayWeaponSound(GetReloadSound(0));
    }

    /// <summary>
    /// 탄창 삽입 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void ReloadInsertClip()
    {
        PlayWeaponSound(GetReloadSound(1));
    }

    /// <summary>
    /// 스쿼드 멤버 전환 직후 얕은 조준/사격 입력 상태를 현재 멤버에 반영합니다.
    /// </summary>
    /// <param name="isAiming">조준 입력을 유지할지 여부입니다.</param>
    /// <param name="isShooting">사격 입력을 유지할지 여부입니다.</param>
    public void ApplySwitchCarryoverState(bool isAiming, bool isShooting)
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        // ADS(isAiming)이거나 힙파이어(사격 중)면 전투 자세를 유지한 채 전환합니다.
        bool inCombat = isAiming || isShooting;

        m_isAds = isAiming;
        // 힙파이어 carryover면 잔류 타이머를 부여해, 전환 직후 사격을 멈춰도 백뷰가 곧장 풀리지 않습니다.
        m_hipfireTimer = inCombat && !isAiming ? m_hipfireHoldDuration : 0.0f;

        ApplyCombatStanceState(inCombat, isShooting, inCombat ? 1.0f : 0.0f);

        if (inCombat)
        {
            ApplyCombatZoom(true);
            UpdateCrosshair(true);
        }
    }

    /// <summary>
    /// 외부 상태 전환에 의해 조준을 강제로 해제합니다.
    /// </summary>
    public void ForceStopAim()
    {
        bool keepReloadAnimation = m_controller != null
                                && m_controller.IsReload
                                && m_weaponController != null
                                && m_weaponController.IsReloading;

        ForceStopAim(keepReloadAnimation);
    }

    /// <summary>
    /// 다운/사망 등으로 전투 비주얼(조준·손 IK 리그, 무기 상체 레이어)을 조건 없이 완전히 해제합니다.
    /// </summary>
    /// <remarks>
    /// 재장전 여부와 무관하게 무기 레이어 weight까지 0으로 내려, 다운/사망 모션이 상체 IK나 무기 레이어에
    /// 의해 깨지지 않도록 합니다.
    /// </remarks>
    public void ReleaseCombatVisuals()
    {
        ForceStopAim(false);
    }

    /// <summary>
    /// 조준을 강제로 해제합니다.
    /// </summary>
    /// <param name="keepReloadAnimation">true이면 재장전 상체 애니메이션을 위해 무기 레이어 weight를 유지합니다.</param>
    private void ForceStopAim(bool keepReloadAnimation)
    {
        m_inCombatStance = false;
        m_isAds = false;
        m_hipfireTimer = 0.0f;
        SetAimState(false);
        HideHitscanBlockMarker();
        SetRigWeight(0.0f);

        if (m_animator != null)
        {
            m_animator.SetLayerWeight(WeaponLayerIndex, keepReloadAnimation ? 1.0f : 0.0f);
            m_animator.SetBool(AnimIDShoot, false);
        }
    }

    /// <summary>
    /// 전투 자세 진입/해제 전환에서 한 번만 적용할 카메라, UI, 이동, 리그, 애니메이션 상태를 모읍니다.
    /// </summary>
    /// <param name="active">전환 후 전투 자세(백뷰) 활성 상태입니다.</param>
    /// <param name="keepShooting">전환 직후 사격 애니메이션을 유지할지 여부입니다.</param>
    /// <param name="weaponLayerWeight">무기 레이어에 적용할 weight입니다.</param>
    private void ApplyCombatStanceState(bool active, bool keepShooting, float weaponLayerWeight)
    {
        m_inCombatStance = active;
        SetAimState(active);
        SetRigWeight(active ? 1.0f : 0.0f);

        if (m_animator != null)
        {
            m_animator.SetLayerWeight(WeaponLayerIndex, weaponLayerWeight);
            m_animator.SetBool(AnimIDShoot, active && keepShooting);
        }

        if (!active)
        {
            HideHitscanBlockMarker();
        }
    }

    /// <summary>
    /// 조준 및 손 IK 리그의 weight를 설정합니다.
    /// </summary>
    /// <param name="weight">적용할 리그 weight입니다. 0이면 비활성, 1이면 활성입니다.</param>
    private void SetRigWeight(float weight)
    {
        if (m_aimRig != null)
        {
            m_aimRig.weight = weight;
        }

        if (m_handRig != null)
        {
            m_handRig.weight = weight;
        }
    }

    /// <summary>
    /// 재장전 사운드 배열에서 지정한 인덱스의 클립을 가져옵니다.
    /// </summary>
    /// <param name="index">가져올 재장전 사운드 인덱스입니다.</param>
    /// <returns>유효한 인덱스이면 해당 AudioClip, 아니면 null입니다.</returns>
    private AudioClip GetReloadSound(int index)
    {
        if (m_reloadSounds == null || index < 0 || index >= m_reloadSounds.Length)
        {
            Debug.LogWarning($"[AimController] ReloadSounds[{index}]가 없습니다.", this);
            return null;
        }

        return m_reloadSounds[index];
    }

    /// <summary>
    /// 무기 사운드를 AudioSource로 재생합니다.
    /// </summary>
    /// <param name="sound">재생할 사운드 클립입니다.</param>
    private void PlayWeaponSound(AudioClip sound)
    {
        if (m_weaponAudioSource == null || sound == null)
        {
            return;
        }

        m_weaponAudioSource.clip = sound;
        m_weaponAudioSource.Play();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 지금 인스펙터에 들어 있는 값을 이 컴포넌트가 물고 있는 밸런스 SO와 CSV로 되돌려 씁니다.
    /// </summary>
    /// <remarks>
    /// 플레이테스트로 잡은 값을 정본으로 승격시키는 용도입니다.
    /// 이 작업을 하지 않으면 인스펙터에서 만진 값은 다음 실행의 Awake에서 SO 값에 덮여 사라집니다.
    /// 에디터 전용입니다. SO와 CSV는 프로젝트 자산이라 빌드에서는 쓸 수 없습니다.
    /// </remarks>
    [ContextMenu("밸런스: 현재 인스펙터 → SO + CSV 갱신")]
    private void ReverseSyncBalanceToAsset()
    {
        UnityEngine.Debug.Log($"[BalanceReverseSync] {name}: {BalanceReverseSyncHook.Run(this)}", this);
    }
#endif
}
