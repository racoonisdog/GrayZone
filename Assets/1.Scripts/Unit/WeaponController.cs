using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VInspector;

/// <summary>
/// 무기의 탄약, 사격, 재장전, 탄피/탄창/머즐 이펙트 생성, 탄약 UI 갱신을 담당하는 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 실제 탄환, 탄피, 탄창 오브젝트 생성은 <c>PoolManager</c>를 통해 수행합니다.
/// 사격 입력 자체는 외부 컨트롤러에서 판단하고, 이 컴포넌트는 <see cref="TryShoot"/> 호출을 통해 사격 가능 여부와 발사 처리를 담당합니다.
/// </remarks>
public class WeaponController : MonoBehaviour, IBalancePostProcess
{
    public event System.Action<int, int> OnBulletChanged;

    [Foldout("Balance Data")]
    [Tooltip("이 무기에 적용할 순수 수치형 밸런스 SO입니다. 비어 있으면 기존 Inspector 값을 사용합니다.")]
    [FormerlySerializedAs("m_balance")]
    [SerializeField] private WeaponControllerSO m_balanceSO;

    /// <summary>
    /// 조준 중 계산된 총구 기준 히트스캔 사격 정보를 담습니다.
    /// </summary>
    /// <remarks>
    /// <see cref="AimController"/>가 조준 프레임에서 한 번 계산하고, 마커 표시와 사격 처리에서 같은 값을 사용합니다.
    /// </remarks>
    public struct HitscanShotInfo
    {
        public bool IsValid;
        public bool HasHit;
        public bool IsObstructed;
        public Vector3 Origin;
        public Vector3 Direction;
        public Vector3 AimPoint;
        public Vector3 EndPoint;
        public RaycastHit Hit;
        public int FrameCount;
    }

    // SpreadDistribution, KickSidePattern은 밸런스 SO와 같은 타입을 공유해야 하므로
    // Assets/1.Scripts/Enum/WeaponEnum.cs로 옮겼습니다.

    [Foldout("Bullet Options")]
    [Tooltip("현재 탄약 수입니다.")]
    [FormerlySerializedAs("currentBullet")]
    [SerializeField] private int m_currentBullet = 30;

    [Tooltip("최대 탄약 수입니다.")]
    [FormerlySerializedAs("maxBullet")]
    [BalanceField(Min = 0)]
    [SerializeField] private int m_maxBullet = 30;

    [Tooltip("사격 후 다음 사격이 가능해질 때까지의 지연 시간입니다.")]
    [FormerlySerializedAs("shootDelay")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_shootDelay = 0.12f;

    [Tooltip("재장전에 필요한 시간입니다. 현재 스크립트에서는 상태값 용도로 보관하며, 실제 완료 타이밍은 애니메이션 이벤트에서 처리할 수 있습니다.")]
    [FormerlySerializedAs("reloadTime")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_reloadTime = 1.5f;

    [Tooltip("탄약이 최대치일 때도 재장전을 허용할지 여부입니다. 기본값은 false로, 풀 탄창에서는 재장전이 막히고 빈 장전 사운드만 재생됩니다. 디버그 용도로만 true로 켭니다.")]
    [SerializeField] private bool m_allowFullMagReload = false;

    [Foldout("Spawn Points")]
    [Tooltip("탄환이 생성될 위치입니다.")]
    [FormerlySerializedAs("firePos")]
    [SerializeField] private Transform m_firePos;

    [Tooltip("탄피가 생성될 위치입니다.")]
    [FormerlySerializedAs("shellPos")]
    [SerializeField] private Transform m_shellPos;

    [Tooltip("탄창이 떨어질 위치입니다.")]
    [FormerlySerializedAs("clipPos")]
    [SerializeField] private Transform m_clipPos;

    [Tooltip("머즐 플래시 위치입니다. 파티클 방식 사용 시 보조 참조로 사용합니다.")]
    [FormerlySerializedAs("muzzleFlashPos")]
    [SerializeField] private Transform m_muzzleFlashPos;

    [Foldout("Pool Index")]
    [Tooltip("탄환 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("bulletPoolIndex")]
    [SerializeField] private int m_bulletPoolIndex = 0;

    [Tooltip("탄피 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("shellPoolIndex")]
    [SerializeField] private int m_shellPoolIndex = 1;

    [Tooltip("탄창 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("clipPoolIndex")]
    [SerializeField] private int m_clipPoolIndex = 2;

    [Tooltip("머즐 플래시 오브젝트 풀 인덱스입니다. 현재 파티클 방식에서는 사용하지 않습니다.")]
    [FormerlySerializedAs("muzzleFlashPoolIndex")]
    [SerializeField] private int m_muzzleFlashPoolIndex = 3;

    [Foldout("UI Options")]
    [Tooltip("현재 탄약 수를 표시할 UI 텍스트입니다.")]
    [FormerlySerializedAs("bulletText")]
    [SerializeField] private Text m_bulletText;

    [Foldout("Audio Options")]
    [Tooltip("무기 효과음을 재생할 AudioSource입니다. 비어 있으면 같은 GameObject에서 자동 탐색합니다.")]
    [FormerlySerializedAs("audioSource")]
    [SerializeField] private AudioSource m_audioSource;

    [Tooltip("사격 효과음입니다.")]
    [FormerlySerializedAs("shootClip")]
    [SerializeField] private AudioClip m_shootClip;

    [Tooltip("재장전 효과음입니다.")]
    [FormerlySerializedAs("reloadClip")]
    [SerializeField] private AudioClip m_reloadClip;

    [Tooltip("풀 탄창 등으로 재장전이 막힐 때 재생할 빈 장전(드라이) 효과음입니다. 비워두면 무음입니다.")]
    [HideIf("m_allowFullMagReload")]
    [SerializeField] private AudioClip m_emptyReloadClip;

    [Foldout("Effect Options")]
    [EndIf]
    [Tooltip("사격 시 재생할 머즐 플래시 파티클입니다.")]
    [FormerlySerializedAs("muzzleFlashParticle")]
    [SerializeField] private ParticleSystem m_muzzleFlashParticle;

    [Foldout("Hitscan Options")]
    [BalanceField(Min = 0)]
    [SerializeField] private int m_hitscanDamage = 1;

    [Tooltip("이 무기가 약점 판정을 사용하는지 여부입니다. 끄면 약점 부위를 맞혀도 일반 피해로 처리하고 약점 표시도 뜨지 않습니다.")]
    [BalanceField]
    [SerializeField] private bool m_allowHeadshot = true;

    [Tooltip("약점 부위를 맞혔을 때 곱하는 피해 배율입니다. 약점 판정이 꺼져 있으면 사용하지 않습니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_headshotDamageMultiplier = 2.0f;

    [BalanceField(Min = 0)]
    [SerializeField] private float m_hitscanRange = 100.0f;

    [SerializeField] private LayerMask m_hitscanLayerMask = ~0;

    [Foldout("Spread Options")]
    [Header("Hipfire")]
    [Tooltip("힙파이어(비조준) 시 최소 방사각(도).")]
    [FormerlySerializedAs("m_hipfireBaseSpread")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_hipfireMinSpread = 4.0f;

    [Tooltip("힙파이어(비조준) 시 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_hipfireMaxSpread = 10.0f;

    [ReadOnly][SerializeField] private float m_hipfireCurrentSpread;

    [Tooltip("힙파이어에서 이 발수까지는 최소 방사각을 유지하고 연사 증가값을 누적하지 않습니다.")]
    [FormerlySerializedAs("m_accurateShotCount")]
    [FormerlySerializedAs("m_minSpreadShotCount")]
    [BalanceField(Min = 0)]
    [SerializeField] private int m_hipfireMinSpreadShotCount = 3;

    [Tooltip("힙파이어 발사마다 누적되는 방사각 증가량(도).")]
    [FormerlySerializedAs("m_bloomPerShot")]
    [FormerlySerializedAs("m_spreadIncreasePerShot")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_hipfireSpreadIncreasePerShot = 1.0f;

    [Tooltip("힙파이어 사격을 멈춘 뒤 초당 회복(감소)하는 방사각(도/초).")]
    [FormerlySerializedAs("m_bloomRecovery")]
    [FormerlySerializedAs("m_spreadRecoveryPerSecond")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_hipfireSpreadRecoveryPerSecond = 8.0f;

    [Tooltip("힙파이어 사격을 멈춘 뒤 이 시간(초)이 지나면 탄퍼짐 회복을 시작하고 연사 발수 카운트를 리셋합니다.")]
    [FormerlySerializedAs("m_spreadResetTime")]
    [FormerlySerializedAs("m_spreadRecoveryDelay")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_hipfireSpreadRecoveryDelay = 0.3f;

    [Header("ADS")]
    [Tooltip("ADS(조준) 시 최소 방사각(도). 0이면 정밀 사격입니다.")]
    [FormerlySerializedAs("m_adsBaseSpread")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_adsMinSpread = 0.0f;

    [Tooltip("ADS(조준) 시 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_adsMaxSpread = 6.0f;

    [ReadOnly][SerializeField] private float m_adsCurrentSpread;

    [Tooltip("ADS에서 이 발수까지는 최소 방사각을 유지하고 연사 증가값을 누적하지 않습니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private int m_adsMinSpreadShotCount = 3;

    [Tooltip("ADS 발사마다 누적되는 방사각 증가량(도).")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_adsSpreadIncreasePerShot = 1.0f;

    [Tooltip("ADS 사격을 멈춘 뒤 초당 회복(감소)하는 방사각(도/초).")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_adsSpreadRecoveryPerSecond = 8.0f;

    [Tooltip("ADS 사격을 멈춘 뒤 이 시간(초)이 지나면 탄퍼짐 회복을 시작하고 연사 발수 카운트를 리셋합니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_adsSpreadRecoveryDelay = 0.3f;

    [Header("Distribution")]
    [Tooltip("탄퍼짐 분포 방식입니다. Uniform=원판 전체 균일, Gaussian=중심 가중 정규분포(기본). 샷건(콘 3분할) 등은 추후 추가 예정입니다.")]
    [BalanceField]
    [SerializeField] private SpreadDistribution m_spreadDistribution = SpreadDistribution.Gaussian;

    [Tooltip("Gaussian 분포의 중심 집중도입니다. 콘 반각(최대 방사각)을 몇 σ로 볼지 정합니다. 값이 클수록 탄이 중심에 더 몰립니다(기본 3 = 약 99%가 콘 안, 평균 편향은 반각의 약 0.42배). 낮출수록 가장자리로 퍼집니다. Uniform에는 영향이 없습니다.")]
    [BalanceField(Min = 1)]
    [SerializeField] private float m_spreadConcentration = 3.0f;

    [Foldout("Recoil Options")]
    [Header("Aim Recoil (탄착에 영향)")]
    [Tooltip("발사 1회당 세로(피치) 반동 각도(도)입니다. 양수면 조준이 위로 솟습니다(머즐 클라임). 실제 조준을 밀어 탄착에도 영향을 주며(LogicalAim), 사격을 멈추면 자동 회복됩니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_recoilPitchKick = 0.6f;

    [Tooltip("발사 1회당 좌우(요) 반동 각도(도)의 크기입니다. 실제 조준을 밀어 탄착에도 영향을 줍니다. 0이면 좌우 반동이 없습니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_recoilYawKick = 0.2f;

    [Tooltip("좌우 반동(Yaw)의 방향 패턴입니다. Random=매 발 ±범위 무작위, AlternateLeftFirst=좌·우 번갈아(첫 발 왼쪽), AlternateRightFirst=우·좌 번갈아(첫 발 오른쪽). Alternate는 위 크기를 그대로 좌우로 씁니다.")]
    [BalanceField]
    [SerializeField] private KickSidePattern m_yawKickPattern = KickSidePattern.Random;

    [Header("Visual Kick (에임 무영향, juice)")]
    [Tooltip("발사 1회당 카메라 롤(Dutch) 크기(도)입니다. 화면만 살짝 기울입니다. 조준/탄착에는 영향이 없습니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_recoilRoll = 0.5f;

    [Tooltip("카메라 롤(Dutch)의 방향 패턴입니다. Random=매 발 ±범위 무작위, AlternateLeftFirst=좌·우 번갈아(첫 발 왼쪽), AlternateRightFirst=우·좌 번갈아(첫 발 오른쪽). Yaw 반동과 독립적으로 설정됩니다.")]
    [BalanceField]
    [SerializeField] private KickSidePattern m_rollKickPattern = KickSidePattern.Random;

    [Tooltip("발사 1회당 카메라 FOV 펀치(도)입니다. 순간적으로 시야가 벌어졌다 회복되는 시각 반동 연출입니다. 조준/탄착에는 영향이 없습니다.")]
    [BalanceField(Min = 0)]
    [SerializeField] private float m_recoilFovPunch = 1.0f;

#if UNITY_EDITOR
    [Foldout("Debug")]
    [Tooltip("Editor-only SpreadDebug console log. Calls are stripped from Player builds.")]
    [SerializeField] private bool m_debugLogSpread = false;
#endif

    // 무한 장탄수는 플레이테스트 트레이너에서도 쓰기 위해 빌드에도 컴파일하고,
    // 실제 효과는 런타임 트레이너가 활성화된 Editor/Development Build에서만 동작합니다.
    [Foldout("Debug")]
    [Tooltip("켜면 사격해도 현재 장전된 탄약이 줄지 않습니다(무한 장탄수). 개발 모드에서만 효과가 있습니다.")]
    [SerializeField] private bool m_debugInfiniteMagazine = false;

    /// <summary>무한 장탄수 디버그 플래그입니다. 디버그 트레이너에서 사용하며 개발 모드에서만 효과가 있습니다.</summary>
    public bool DebugInfiniteMagazine
    {
        get => m_debugInfiniteMagazine;
        set => m_debugInfiniteMagazine = value;
    }

    /// <summary>무한 장탄수가 실제로 적용되는 상태인지 여부입니다. 개발 모드에서 플래그가 켜졌을 때만 <c>true</c>입니다.</summary>
    private bool IsInfiniteMagazineDebugActive => m_debugInfiniteMagazine && GameDevMode.DebugFeaturesEnabled;

    public const float HitscanAimTolerance = 0.05f;

    private bool m_canShoot = true;
    private bool m_isReloading;
    private float m_reloadStartTime;
    private bool m_hasRequiredReferences;
    private Faction m_ownerFaction = Faction.Player;
    private float m_hipfireCurrentSpreadAdd;
    private float m_adsCurrentSpreadAdd;
    private int m_hipfireShotsInBurst;
    private int m_adsShotsInBurst;
    private float m_hipfireLastShotTime;
    private float m_adsLastShotTime;

    // 시트 값이 뒤집혔을 때 되돌릴 기준입니다. 프리팹마다 튜닝이 달라 클래스 공용 상수를 쓸 수 없으므로,
    // 첫 바인딩 직전에 이 프리팹이 저작한 값을 그대로 보관합니다.
    private bool m_fallbackRangesCaptured;
    private float m_fallbackHipfireMinSpread;
    private float m_fallbackHipfireMaxSpread;
    private float m_fallbackAdsMinSpread;
    private float m_fallbackAdsMaxSpread;

    /// <summary>현재 탄약 수입니다.</summary>
    public int CurrentBullet => m_currentBullet;

    /// <summary>현재 이 무기에 지정된 순수 수치형 밸런스 SO입니다.</summary>
    public WeaponControllerSO Balance => m_balanceSO;

    /// <summary>최대 탄약 수입니다.</summary>
    public int MaxBullet => m_maxBullet;

    /// <summary>현재 사격 가능한 상태인지 여부입니다.</summary>
    public bool CanShoot => m_canShoot;

    /// <summary>
    /// 히트스캔 피격이 확정되어 피해가 적용됐을 때 발생합니다. 인자는 헤드샷·킬 여부를 담은 피드백입니다.
    /// </summary>
    /// <remarks>조준선 히트마커/킬 표시가 구독합니다.</remarks>
    public event System.Action<CombatDamage.HitFeedback> OnHitFeedback;

    /// <summary>현재 재장전 중인지 여부입니다.</summary>
    public bool IsReloading => m_isReloading;

    /// <summary>현재 재장전 진행도(0~1)입니다. 재장전 중이 아니면 0입니다.</summary>
    public float ReloadProgress => !m_isReloading ? 0.0f
        : m_reloadTime <= 0.0f ? 1.0f
        : Mathf.Clamp01((Time.time - m_reloadStartTime) / m_reloadTime);

    /// <summary>
    /// 지금 재장전을 시작할 수 있는 상태인지 여부입니다.
    /// </summary>
    /// <remarks>필수 참조를 갖추고, 재장전 중이 아니며, 탄약이 최대치 미만일 때만 <c>true</c>입니다. 풀 탄창 재장전 진입을 막는 데 사용합니다. <see cref="AllowFullMagReload"/>가 켜져 있으면 풀 탄창에서도 <c>true</c>입니다.</remarks>
    public bool CanReload => m_hasRequiredReferences && !m_isReloading && (m_allowFullMagReload || m_currentBullet < m_maxBullet);

    /// <summary>탄약이 최대치일 때도 재장전을 허용할지 여부입니다. 기본값은 <c>false</c>이며 디버그 용도입니다.</summary>
    public bool AllowFullMagReload => m_allowFullMagReload;

    /// <summary>사격 지연 시간입니다.</summary>
    public float ShootDelay => m_shootDelay;

    /// <summary>재장전 시간입니다.</summary>
    public float ReloadTime => m_reloadTime;

    /// <summary>탄환 생성 위치입니다.</summary>
    public Transform FirePos => m_firePos;

    /// <summary>탄피 생성 위치입니다.</summary>
    public Transform ShellPos => m_shellPos;

    /// <summary>탄창 생성 위치입니다.</summary>
    public Transform ClipPos => m_clipPos;

    /// <summary>머즐 플래시 위치입니다.</summary>
    public Transform MuzzleFlashPos => m_muzzleFlashPos;

    /// <summary>탄약 UI 텍스트입니다.</summary>
    public Text BulletText => m_bulletText;

    /// <summary>히트스캔 판정에 사용할 최대 사거리입니다.</summary>
    public float HitscanRange => m_hitscanRange;

    /// <summary>히트스캔 사격이 적에게 적용할 피해량입니다.</summary>
    public int HitscanDamage => m_hitscanDamage;

    /// <summary>이 무기가 약점 판정을 사용하는지 여부입니다.</summary>
    public bool AllowHeadshot => m_allowHeadshot;

    /// <summary>헤드샷 히트박스에 명중했을 때 이 무기가 적용할 피해 배율입니다.</summary>
    public float HeadshotDamageMultiplier => m_headshotDamageMultiplier;

    /// <summary>히트스캔 레이캐스트가 충돌 검사할 레이어 마스크입니다.</summary>
    public LayerMask HitscanLayerMask => m_hitscanLayerMask;

    /// <summary>발사 1회당 세로(피치) 반동 각도(도)입니다. 실제 조준을 밀어 탄착에도 영향을 줍니다.</summary>
    public float RecoilPitchKick => m_recoilPitchKick;

    /// <summary>발사 1회당 좌우(요) 반동 각도(도)의 최대 크기입니다. 실제 조준을 밀어 탄착에도 영향을 줍니다.</summary>
    public float RecoilYawKick => Mathf.Max(0.0f, m_recoilYawKick);

    /// <summary>발사 1회당 카메라 롤(Dutch) 최대 크기(도)입니다. 시각 전용 juice이며 조준/탄착에는 영향이 없습니다.</summary>
    public float RecoilRoll => Mathf.Max(0.0f, m_recoilRoll);

    /// <summary>발사 1회당 카메라 FOV 펀치(도)입니다. 시각 전용 juice이며 조준/탄착에는 영향이 없습니다.</summary>
    public float RecoilFovPunch => Mathf.Max(0.0f, m_recoilFovPunch);

    /// <summary>좌우 반동(Yaw)의 좌우 방향 패턴입니다.</summary>
    public KickSidePattern YawKickPattern => m_yawKickPattern;

    /// <summary>시각 롤(Dutch)의 좌우 방향 패턴입니다. Yaw 반동과 독립입니다.</summary>
    public KickSidePattern RollKickPattern => m_rollKickPattern;

    /// <summary>
    /// 현재 모드의 표시용 탄퍼짐 방사각(도)을 반환합니다.
    /// </summary>
    /// <param name="isAds">ADS면 true, 힙파이어면 false입니다.</param>
    /// <returns>사격 상태를 변경하지 않고 계산한 현재 탄퍼짐 방사각(도)입니다.</returns>
    public float GetCurrentSpread(bool isAds)
    {
        return isAds
            ? GetCurrentSpread(m_adsMinSpread, m_adsMaxSpread, m_adsCurrentSpreadAdd)
            : GetCurrentSpread(m_hipfireMinSpread, m_hipfireMaxSpread, m_hipfireCurrentSpreadAdd);
    }

    public void GetSpreadRange(bool isAds, out float minSpread, out float maxSpread)
    {
        minSpread = Mathf.Max(0.0f, isAds ? m_adsMinSpread : m_hipfireMinSpread);
        maxSpread = Mathf.Max(minSpread, isAds ? m_adsMaxSpread : m_hipfireMaxSpread);
    }

    /// <summary>탄퍼짐 콘 안에서의 분포 방식입니다. 크로스헤어가 표시 배율을 계산할 때 읽습니다(읽기 전용).</summary>
    public SpreadDistribution Distribution => m_spreadDistribution;

    /// <summary>Gaussian 분포의 중심 집중도(σ=1/이 값)입니다. 크로스헤어가 표시 배율을 계산할 때 읽습니다(읽기 전용, 최소 1).</summary>
    public float SpreadConcentration => Mathf.Max(1.0f, m_spreadConcentration);

    /// <summary>힙파이어에서 최소 탄퍼짐을 유지하는 연속 발사 수입니다.</summary>
    public int HipfireMinSpreadShotCount => Mathf.Max(0, m_hipfireMinSpreadShotCount);

    /// <summary>힙파이어 연사 시 발마다 누적하는 탄퍼짐 각도입니다.</summary>
    public float HipfireSpreadIncreasePerShot => Mathf.Max(0.0f, m_hipfireSpreadIncreasePerShot);

    /// <summary>힙파이어 사격 중단 후 탄퍼짐 회복을 시작하기까지의 지연 시간입니다.</summary>
    public float HipfireSpreadRecoveryDelay => Mathf.Max(0.0f, m_hipfireSpreadRecoveryDelay);

    /// <summary>힙파이어 탄퍼짐의 초당 회복량입니다.</summary>
    public float HipfireSpreadRecoveryPerSecond => Mathf.Max(0.0f, m_hipfireSpreadRecoveryPerSecond);

    /// <summary>ADS에서 최소 탄퍼짐을 유지하는 연속 발사 수입니다.</summary>
    public int AdsMinSpreadShotCount => Mathf.Max(0, m_adsMinSpreadShotCount);

    /// <summary>ADS 연사 시 발마다 누적하는 탄퍼짐 각도입니다.</summary>
    public float AdsSpreadIncreasePerShot => Mathf.Max(0.0f, m_adsSpreadIncreasePerShot);

    /// <summary>ADS 사격 중단 후 탄퍼짐 회복을 시작하기까지의 지연 시간입니다.</summary>
    public float AdsSpreadRecoveryDelay => Mathf.Max(0.0f, m_adsSpreadRecoveryDelay);

    /// <summary>ADS 탄퍼짐의 초당 회복량입니다.</summary>
    public float AdsSpreadRecoveryPerSecond => Mathf.Max(0.0f, m_adsSpreadRecoveryPerSecond);

    /// <summary>
    /// 컴포넌트 참조를 캐싱하고 필수 참조를 검증합니다.
    /// </summary>
    private void Awake()
    {
        CacheReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        m_hasRequiredReferences = true;
        BindConfiguredBalance();
    }

    /// <summary>
    /// 초기 탄약 UI를 갱신합니다.
    /// </summary>
    private void Start()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        ClampBulletValues();
        UpdateCurrentSpreadInspectorFields();
        UpdateBulletUI();
    }

    /// <summary>
    /// 예약된 사격 쿨다운 호출을 정리합니다.
    /// </summary>
    private void OnDisable()
    {
        CancelInvoke(nameof(ResetShoot));
        CancelInvoke(nameof(CompleteReload));
    }

    /// <summary>
    /// 사격을 멈춘 뒤 일정 시간이 지나면 누적된 탄퍼짐 증가값을 회복합니다.
    /// </summary>
    private void Update()
    {
        RecoverSpread(ref m_hipfireCurrentSpreadAdd, m_hipfireLastShotTime, m_hipfireSpreadRecoveryDelay, m_hipfireSpreadRecoveryPerSecond);
        RecoverSpread(ref m_adsCurrentSpreadAdd, m_adsLastShotTime, m_adsSpreadRecoveryDelay, m_adsSpreadRecoveryPerSecond);
        UpdateCurrentSpreadInspectorFields();
    }

    /// <summary>
    /// 자동으로 찾을 수 있는 내부 참조를 캐싱합니다.
    /// </summary>
    private void CacheReferences()
    {
        if (m_audioSource == null)
        {
            m_audioSource = GetComponent<AudioSource>();
        }

        // 무기를 소유한 유닛의 진영을 사격 주체 진영으로 사용합니다.
        // (스쿼드 멤버 자식에 부착되어 부모의 HealthSystemBase를 찾습니다. 없으면 Player로 가정.)
        HealthSystemBase ownerHealth = GetComponentInParent<HealthSystemBase>();
        m_ownerFaction = ownerHealth != null ? ownerHealth.Faction : Faction.Player;
    }

    /// <summary>
    /// 필수 참조가 올바르게 설정되어 있는지 확인합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 <c>true</c>, 하나라도 누락되면 <c>false</c>입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_firePos == null)
        {
            Debug.LogError("[WeaponController] FirePos가 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_shellPos == null)
        {
            Debug.LogWarning("[WeaponController] ShellPos가 할당되지 않았습니다. 탄피 생성은 생략됩니다.", this);
        }

        if (m_clipPos == null)
        {
            Debug.LogWarning("[WeaponController] ClipPos가 할당되지 않았습니다. 탄창 드롭은 생략됩니다.", this);
        }

        if (m_audioSource == null)
        {
            Debug.LogWarning("[WeaponController] AudioSource가 없습니다. 무기 효과음은 재생되지 않습니다.", this);
        }
        /*
        if (PoolManager.instance == null)
        {
            Debug.LogError("[WeaponController] PoolManager 인스턴스를 찾지 못했습니다.", this);
            isValid = false;
        }*/

        return isValid;
    }

    /// <summary>
    /// 새로운 무기 밸런스 SO로 교체하고 공용 ID 바인딩을 즉시 다시 수행합니다.
    /// </summary>
    public BalanceBindResult SetBalance(WeaponControllerSO balance)
    {
        m_balanceSO = balance;
        return BindConfiguredBalance();
    }

    /// <summary>
    /// 지정된 SO가 있을 때 공용 BindManager를 통해 같은 ID의 필드 값을 적용합니다.
    /// </summary>
    private BalanceBindResult BindConfiguredBalance()
    {
        if (m_balanceSO == null)
        {
            return default;
        }

        // 대입이 값을 덮어쓰기 전에 프리팹 저작값을 확보해 둡니다.
        CaptureFallbackSpreadRanges();
        return BindManager.Instance.Bind(m_balanceSO, this, this);
    }

    /// <summary>
    /// 프리팹이 저작한 방사각 범위를 최초 한 번만 보관합니다.
    /// </summary>
    /// <remarks>
    /// 첫 <see cref="BindConfiguredBalance"/> 호출 시점에는 아직 SO 값이 대입되지 않아 필드에 프리팹 값이 남아 있습니다.
    /// 런타임에 SO를 교체해도 기준은 항상 이 최초 프리팹 값입니다.
    /// </remarks>
    private void CaptureFallbackSpreadRanges()
    {
        if (m_fallbackRangesCaptured)
        {
            return;
        }

        m_fallbackHipfireMinSpread = m_hipfireMinSpread;
        m_fallbackHipfireMaxSpread = m_hipfireMaxSpread;
        m_fallbackAdsMinSpread = m_adsMinSpread;
        m_fallbackAdsMaxSpread = m_adsMaxSpread;
        m_fallbackRangesCaptured = true;
    }

    /// <summary>
    /// 최소·최대 방사각이 뒤집혀 들어온 경우 프리팹 저작값으로 되돌리고 경고합니다.
    /// </summary>
    /// <remarks>
    /// 한 필드의 Min/Max 선언으로는 "다른 필드보다 커야 한다"를 표현할 수 없어, 모든 값이 대입된 뒤에 검사합니다.
    /// 시트에 min=5, max=2처럼 잘못 적힌 경우가 여기서 걸립니다.
    /// </remarks>
    private void RestoreInvertedSpreadRanges()
    {
        if (!m_fallbackRangesCaptured)
        {
            return;
        }

        if (m_hipfireMaxSpread < m_hipfireMinSpread)
        {
            Debug.LogWarning(
                $"[WeaponController] 비조준 방사각 범위가 뒤집혔습니다(min={m_hipfireMinSpread}, max={m_hipfireMaxSpread}). " +
                $"프리팹 값(min={m_fallbackHipfireMinSpread}, max={m_fallbackHipfireMaxSpread})으로 되돌립니다.",
                this);
            m_hipfireMinSpread = m_fallbackHipfireMinSpread;
            m_hipfireMaxSpread = m_fallbackHipfireMaxSpread;
        }

        if (m_adsMaxSpread < m_adsMinSpread)
        {
            Debug.LogWarning(
                $"[WeaponController] 조준 방사각 범위가 뒤집혔습니다(min={m_adsMinSpread}, max={m_adsMaxSpread}). " +
                $"프리팹 값(min={m_fallbackAdsMinSpread}, max={m_fallbackAdsMaxSpread})으로 되돌립니다.",
                this);
            m_adsMinSpread = m_fallbackAdsMinSpread;
            m_adsMaxSpread = m_fallbackAdsMaxSpread;
        }
    }

    /// <summary>
    /// 공용 대입 이후 런타임 상태와 변수 간 관계를 정리합니다.
    /// </summary>
    public void OnBalanceApplied()
    {
        // 뒤집힌 범위를 먼저 되돌린 뒤 나머지 보정을 적용합니다.
        RestoreInvertedSpreadRanges();
        ClampBulletValues();
        UpdateCurrentSpreadInspectorFields();

        if (m_hasRequiredReferences)
        {
            UpdateBulletUI();
        }
    }

    /// <summary>
    /// 탄약 관련 수치가 유효 범위를 벗어나지 않도록 보정합니다.
    /// </summary>
    private void ClampBulletValues()
    {
        m_maxBullet = Mathf.Max(0, m_maxBullet);
        m_currentBullet = Mathf.Clamp(m_currentBullet, 0, m_maxBullet);
        m_shootDelay = Mathf.Max(0.0f, m_shootDelay);
        m_reloadTime = Mathf.Max(0.0f, m_reloadTime);
        m_hitscanDamage = Mathf.Max(0, m_hitscanDamage);
        m_headshotDamageMultiplier = Mathf.Max(0.0f, m_headshotDamageMultiplier);
        m_hitscanRange = Mathf.Max(0.0f, m_hitscanRange);
        m_adsMinSpread = Mathf.Max(0.0f, m_adsMinSpread);
        m_hipfireMinSpread = Mathf.Max(0.0f, m_hipfireMinSpread);
        m_adsMaxSpread = Mathf.Max(m_adsMinSpread, m_adsMaxSpread);
        m_hipfireMaxSpread = Mathf.Max(m_hipfireMinSpread, m_hipfireMaxSpread);
        m_hipfireMinSpreadShotCount = Mathf.Max(0, m_hipfireMinSpreadShotCount);
        m_adsMinSpreadShotCount = Mathf.Max(0, m_adsMinSpreadShotCount);
        m_hipfireSpreadIncreasePerShot = Mathf.Max(0.0f, m_hipfireSpreadIncreasePerShot);
        m_adsSpreadIncreasePerShot = Mathf.Max(0.0f, m_adsSpreadIncreasePerShot);
        m_hipfireSpreadRecoveryPerSecond = Mathf.Max(0.0f, m_hipfireSpreadRecoveryPerSecond);
        m_adsSpreadRecoveryPerSecond = Mathf.Max(0.0f, m_adsSpreadRecoveryPerSecond);
        m_hipfireSpreadRecoveryDelay = Mathf.Max(0.0f, m_hipfireSpreadRecoveryDelay);
        m_adsSpreadRecoveryDelay = Mathf.Max(0.0f, m_adsSpreadRecoveryDelay);
        m_spreadConcentration = Mathf.Max(1.0f, m_spreadConcentration);
    }

    private void OnValidate()
    {
        ClampBulletValues();
        UpdateCurrentSpreadInspectorFields();
    }

    /// <summary>
    /// 지정한 목표 위치를 향해 사격을 시도합니다.
    /// </summary>
    /// <param name="targetPosition">탄환이 향할 월드 좌표입니다.</param>
    /// <returns>사격에 성공하면 <c>true</c>, 사격 불가능 상태면 <c>false</c>입니다.</returns>
    public bool TryShoot(Vector3 targetPosition)
    {
        if (!m_hasRequiredReferences)
        {
            return false;
        }

        if (!m_canShoot || m_isReloading || (m_currentBullet <= 0 && !IsInfiniteMagazineDebugActive))
        {
            return false;
        }

        if (PoolManager.instance == null)
        {
            Debug.LogWarning("[WeaponController] PoolManager 인스턴스가 없어 사격을 처리할 수 없습니다.", this);
            return false;
        }

        if (!IsInfiniteMagazineDebugActive)
        {
            m_currentBullet--;
        }
        m_canShoot = false;
        // 오브젝트 풀링
        SpawnBullet(targetPosition);
        SpawnShell();


        SpawnMuzzleFlash();
        PlayShootSound();
        UpdateBulletUI();

        Invoke(nameof(ResetShoot), m_shootDelay);
        return true;
    }

    /// <summary>
    /// 조준 컨트롤러가 계산한 조준 정보에 탄퍼짐을 적용해 사격을 시도합니다.
    /// </summary>
    /// <param name="shotInfo">총구 원점·조준 방향·조준점을 담은 조준 정보입니다(탄퍼짐 미적용 정밀 값).</param>
    /// <param name="isAds">조준(ADS) 상태면 <c>true</c>, 힙파이어면 <c>false</c>입니다. 기본 방사각 선택에 사용합니다.</param>
    /// <param name="firedShot">탄퍼짐이 적용된 실제 발사 방향과 탄착 정보입니다. 마커/이펙트가 이 값을 씁니다.</param>
    /// <returns>사격에 성공하면 <c>true</c>, 필수 참조 누락·무효 정보·쿨다운·재장전·탄약 부족이면 <c>false</c>입니다.</returns>
    public bool TryLayShoot(HitscanShotInfo shotInfo, bool isAds, out HitscanShotInfo firedShot)
    {
        firedShot = shotInfo;

        if (!m_hasRequiredReferences)
        {
            return false;
        }

        if (!shotInfo.IsValid)
        {
            return false;
        }

        if (!m_canShoot || m_isReloading || (m_currentBullet <= 0 && !IsInfiniteMagazineDebugActive))
        {
            return false;
        }

        if (!IsInfiniteMagazineDebugActive)
        {
            m_currentBullet--;
        }
        m_canShoot = false;

        firedShot = BuildFiredShot(shotInfo, isAds);
        LayShoot(firedShot);
        SpawnShell();
        SpawnMuzzleFlash();
        PlayShootSound();
        UpdateBulletUI();

        Invoke(nameof(ResetShoot), m_shootDelay);
        return true;
    }

    /// <summary>
    /// 현재 방사각으로 발사 방향을 흩뜨려 실제 발사 사격 정보를 구성합니다.
    /// </summary>
    /// <param name="aimShot">탄퍼짐 미적용 정밀 조준 정보입니다.</param>
    /// <param name="isAds">조준(ADS) 상태 여부입니다.</param>
    /// <returns>탄퍼짐이 적용된 방향으로 재레이캐스트한 발사 사격 정보입니다.</returns>
    private HitscanShotInfo BuildFiredShot(HitscanShotInfo aimShot, bool isAds)
    {
        float spread = ResolveShotSpread(isAds);
        Vector3 direction = ApplySpread(aimShot.Direction, spread);

        // 사격 순간 방사각/편향 진단 로그(에디터 전용, 빌드에서 호출 스트립).
        LogSpreadDebug(isAds, spread, aimShot.Direction, direction);

        HitscanShotInfo fired = new()
        {
            IsValid = true,
            AimPoint = aimShot.AimPoint,
            Origin = aimShot.Origin,
            Direction = direction,
            FrameCount = aimShot.FrameCount,
            EndPoint = aimShot.Origin + direction * m_hitscanRange,
        };

        // 피격 히트박스는 물리로 밀치지 않도록 trigger로 두므로, 전역 설정과 무관하게 trigger를 맞히도록 못 박습니다.
        // UseGlobal로 두면 Physics.queriesHitTriggers를 끄는 순간 사격이 통째로 먹히지 않습니다.
        if (Physics.Raycast(aimShot.Origin, direction, out RaycastHit hit, m_hitscanRange, m_hitscanLayerMask, QueryTriggerInteraction.Collide))
        {
            fired.HasHit = true;
            fired.Hit = hit;
            fired.EndPoint = hit.point;
        }

        return fired;
    }

    /// <summary>
    /// 이번 사격에 적용할 방사각(도)을 계산하고 연사 탄퍼짐 상태를 갱신합니다.
    /// </summary>
    /// <param name="isAds">조준(ADS) 상태 여부입니다. 모드별 탄퍼짐 파라미터와 누적 상태 선택에 사용합니다.</param>
    /// <returns>최소 방사각 + 누적 증가값으로 계산한 이번 사격의 방사각(도)입니다.</returns>
    private float ResolveShotSpread(bool isAds)
    {
        return isAds
            ? ResolveShotSpread(
                m_adsMinSpread,
                m_adsMaxSpread,
                m_adsMinSpreadShotCount,
                m_adsSpreadIncreasePerShot,
                m_adsSpreadRecoveryDelay,
                ref m_adsCurrentSpreadAdd,
                ref m_adsShotsInBurst,
                ref m_adsLastShotTime)
            : ResolveShotSpread(
                m_hipfireMinSpread,
                m_hipfireMaxSpread,
                m_hipfireMinSpreadShotCount,
                m_hipfireSpreadIncreasePerShot,
                m_hipfireSpreadRecoveryDelay,
                ref m_hipfireCurrentSpreadAdd,
                ref m_hipfireShotsInBurst,
                ref m_hipfireLastShotTime);
    }

    /// <summary>
    /// 모드(ADS/힙파이어)별 탄퍼짐 파라미터와 연사 누적 상태로 이번 사격의 방사각(도)을 계산합니다.
    /// </summary>
    /// <param name="minSpread">이 모드의 최소 방사각(도)입니다. 연사 누적이 없을 때의 기본 방사각이며, 0이면 정밀 사격입니다.</param>
    /// <param name="maxSpread">이 모드의 최대 방사각(도)입니다. 연사 누적값을 더해도 이 값을 넘지 않습니다. <paramref name="minSpread"/>보다 작으면 minSpread로 보정됩니다.</param>
    /// <param name="minSpreadShotCount">이 발수까지는 최소 방사각을 유지하고 누적 증가를 시작하지 않는, 첫 정밀 구간 발수입니다.</param>
    /// <param name="spreadIncreasePerShot">정밀 구간을 넘긴 뒤 발사할 때마다 누적되는 방사각 증가량(도)입니다.</param>
    /// <param name="spreadRecoveryDelay">마지막 사격 이후 이 시간(초)이 지나면 연사로 간주하지 않고 발수 카운트를 리셋합니다.</param>
    /// <param name="currentSpreadAdd">현재까지 누적된 방사각 증가값(도)입니다. 이 메서드에서 정밀 구간 이후 증가시키며, 회복은 <see cref="RecoverSpread"/>가 담당합니다.</param>
    /// <param name="shotsInBurst">현재 연사에서 발사한 누적 발수입니다. 사격 간격이 <paramref name="spreadRecoveryDelay"/>를 넘으면 0으로 리셋됩니다.</param>
    /// <param name="lastShotTime">마지막 사격 시각입니다. 연사 판정과 회복 지연에 사용하며, 이 메서드에서 현재 시각으로 갱신합니다.</param>
    /// <returns>최소 방사각 + 누적 증가값을 <paramref name="maxSpread"/>로 제한한, 이번 사격에 적용할 방사각(도)입니다.</returns>
    /// <remarks>
    /// 이번 발에 적용할 방사각은 "현재까지 누적된 값" 기준으로 먼저 정하고, 그 뒤에 다음 발을 위한 누적값을 증가시킵니다.
    /// 따라서 정밀 구간(<paramref name="minSpreadShotCount"/>) 직후 첫 발은 아직 최소 방사각이고, 그 다음 발부터 증가가 반영됩니다.
    /// </remarks>
    private static float ResolveShotSpread(
        float minSpread,
        float maxSpread,
        int minSpreadShotCount,
        float spreadIncreasePerShot,
        float spreadRecoveryDelay,
        ref float currentSpreadAdd,
        ref int shotsInBurst,
        ref float lastShotTime)
    {
        // 마지막 사격 이후 충분히 시간이 지났으면 새 연사로 보고 발수 카운트를 리셋합니다.
        // (누적값 currentSpreadAdd 자체의 회복은 RecoverSpread가 별도로 처리합니다.)
        if (Time.time - lastShotTime > spreadRecoveryDelay)
        {
            shotsInBurst = 0;
        }

        // maxSpread가 minSpread보다 작게 설정된 경우를 보정합니다(Clamp의 min>max 방지).
        maxSpread = Mathf.Max(minSpread, maxSpread);

        // 이번 발의 방사각 = 최소 방사각 + 현재까지 누적된 증가값, 단 [min, max] 범위로 제한.
        // min=max=0이면 누적이 있어도 항상 0이 되어 정밀 사격이 됩니다.
        float spread = Mathf.Clamp(minSpread + currentSpreadAdd, minSpread, maxSpread);

        // 이번 발을 카운트하고, 정밀 구간을 넘긴 뒤부터 다음 발을 위한 누적값을 키웁니다.
        // 누적값은 (max - min)을 상한으로 하여 최종 방사각이 maxSpread를 넘지 않게 합니다.
        shotsInBurst++;
        if (shotsInBurst > minSpreadShotCount)
        {
            float maxSpreadAdd = Mathf.Max(0.0f, maxSpread - minSpread);
            currentSpreadAdd = Mathf.Min(maxSpreadAdd, currentSpreadAdd + spreadIncreasePerShot);
        }

        // 연사 판정과 회복 지연 기준점이 되도록 마지막 사격 시각을 갱신합니다.
        lastShotTime = Time.time;
        return spread;
    }

    /// <summary>
    /// 지정한 모드의 누적 탄퍼짐 증가값을 회복합니다.
    /// </summary>
    private static void RecoverSpread(ref float currentSpreadAdd, float lastShotTime, float recoveryDelay, float recoveryPerSecond)
    {
        if (currentSpreadAdd > 0.0f && Time.time - lastShotTime > recoveryDelay)
        {
            currentSpreadAdd = Mathf.Max(0.0f, currentSpreadAdd - recoveryPerSecond * Time.deltaTime);
        }
    }

    /// <summary>
    /// 지정한 모드의 현재 탄퍼짐 방사각을 상태 변경 없이 계산합니다.
    /// </summary>
    private static float GetCurrentSpread(float minSpread, float maxSpread, float currentSpreadAdd)
    {
        maxSpread = Mathf.Max(minSpread, maxSpread);
        return Mathf.Clamp(minSpread + currentSpreadAdd, minSpread, maxSpread);
    }

    private void UpdateCurrentSpreadInspectorFields()
    {
        m_hipfireCurrentSpread = GetCurrentSpread(m_hipfireMinSpread, m_hipfireMaxSpread, m_hipfireCurrentSpreadAdd);
        m_adsCurrentSpread = GetCurrentSpread(m_adsMinSpread, m_adsMaxSpread, m_adsCurrentSpreadAdd);
    }

    /// <summary>
    /// 발사 방향을 총구 기준 콘(cone) 안에서 무작위로 흩뜨립니다.
    /// </summary>
    /// <param name="direction">탄퍼짐 미적용 발사 방향입니다.</param>
    /// <param name="spreadDegrees">콘의 최대 편향 각도(도)입니다. 0 이하면 그대로 반환합니다.</param>
    /// <returns>콘 안에서 무작위로 편향된 정규화 방향입니다. 빗나감 거리는 사거리에 비례합니다.</returns>
    /// <remarks>
    /// 편향 = 단위오프셋 × tan(<paramref name="spreadDegrees"/>). 즉 콘의 "각도 크기"(min/max spread)와 콘 "안에서의 분포 모양"
    /// (<see cref="m_spreadDistribution"/>·<see cref="m_spreadConcentration"/>)은 서로 직교하며 곱으로 합성됩니다. 서로 상쇄되지 않고,
    /// 하나가 크기·이상치 사거리(하드 캡)를, 다른 하나가 중심 몰림 정도(코어 조임)를 담당합니다.
    /// 콘 안에서의 편향 분포는 <see cref="m_spreadDistribution"/>가 결정합니다. 기본값 Gaussian은 콘 반각(<paramref name="spreadDegrees"/>)을
    /// <see cref="m_spreadConcentration"/> σ로 보고 중심 가중 정규분포로 샘플링한 뒤 콘 경계로 클램프하므로, 탄이 대부분 중심 근처에 몰립니다.
    /// 어느 분포든 콘 반각은 최대 편향(하드 캡)으로 유지됩니다. (spreadDegrees=0이면 편향 0이라 분포/집중도는 작용할 대상이 없습니다.)
    /// </remarks>
    private Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0.0f)
        {
            return direction.normalized;
        }

        Vector3 forward = direction.normalized;
        Vector3 right = Vector3.Cross(forward, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(forward, Vector3.forward);
        }

        right.Normalize();
        Vector3 up = Vector3.Cross(right, forward);

        Vector2 unitOffset = m_spreadDistribution switch
        {
            SpreadDistribution.Uniform => Random.insideUnitCircle,
            _ => SampleGaussianUnitOffset(m_spreadConcentration),
        };
        Vector2 offset = unitOffset * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
        return (forward + right * offset.x + up * offset.y).normalized;
    }

    /// <summary>
    /// 단위 원판 안에서 중심 가중(가우시안) 2D 오프셋을 샘플링합니다.
    /// </summary>
    /// <param name="concentration">콘 경계를 몇 σ로 볼지 정하는 집중도입니다. 클수록 중심에 더 몰립니다.</param>
    /// <returns>크기가 [0, 1]로 클램프된, 중심에 밀집한 2D 오프셋입니다.</returns>
    /// <remarks>Box-Muller 변환으로 회전 대칭 표준정규 2D 벡터를 만든 뒤 σ = 1/concentration 비율로 축소하고, 드물게 단위 원 밖으로 나가는 표본은 경계로 클램프해 최대 편향(하드 캡)을 유지합니다.</remarks>
    private static Vector2 SampleGaussianUnitOffset(float concentration)
    {
        // Box-Muller: 균일 난수 2개 -> 회전 대칭 표준정규 2D 벡터.
        float u1 = Mathf.Max(1e-6f, 1.0f - Random.value);
        float u2 = 1.0f - Random.value;
        float radius = Mathf.Sqrt(-2.0f * Mathf.Log(u1));
        float angle = 2.0f * Mathf.PI * u2;
        Vector2 standardNormal = new(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));

        // 콘 경계 = concentration σ가 되도록 축소(σ = 1/concentration).
        float sigma = 1.0f / Mathf.Max(1.0f, concentration);
        Vector2 offset = standardNormal * sigma;

        // 드물게 경계를 넘는 표본은 콘 경계로 클램프합니다.
        return offset.sqrMagnitude > 1.0f ? offset.normalized : offset;
    }

    /// <summary>
    /// 재장전을 시작합니다.
    /// </summary>
    public void StartReload()
    {
        if (m_isReloading || (!m_allowFullMagReload && m_currentBullet >= m_maxBullet))
        {
            return;
        }

        m_isReloading = true;
        m_reloadStartTime = Time.time;
        PlayReloadSound();
        CancelInvoke(nameof(CompleteReload));
        Invoke(nameof(CompleteReload), m_reloadTime);
    }

    /// <summary>
    /// 재장전을 완료하고 탄약을 최대치로 채웁니다.
    /// </summary>
    public void CompleteReload()
    {
        CancelInvoke(nameof(CompleteReload));
        m_currentBullet = m_maxBullet;
        m_isReloading = false;
        UpdateBulletUI();
    }

    /// <summary>
    /// 진행 중인 재장전을 취소합니다.
    /// </summary>
    public void CancelReload()
    {
        CancelInvoke(nameof(CompleteReload));
        m_isReloading = false;
        UpdateBulletUI();
    }

    /// <summary>
    /// 계산된 히트스캔 사격 정보를 소비해 즉시 사격 결과를 처리합니다.
    /// </summary>
    /// <param name="shotInfo">조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>Enemy 레이어에 맞은 경우 부모에서 <see cref="EnemyHealth"/>를 찾아 피해를 적용합니다.</remarks>
    private void LayShoot(HitscanShotInfo shotInfo)
    {
        if (!shotInfo.IsValid)
        {
            return;
        }

        if (shotInfo.HasHit)
        {
            DrawShotDebugRay(shotInfo, Color.red);
            ApplyHitscanDamage(shotInfo);
            return;
        }

        DrawShotDebugRay(shotInfo, Color.yellow);
    }

    /// <summary>
    /// (에디터 전용) 사격 히트스캔 경로를 Scene 뷰 디버그 레이로 그립니다. 빌드에서는 호출이 스트립됩니다.
    /// </summary>
    /// <param name="shotInfo">그릴 사격 정보입니다.</param>
    /// <param name="color">레이 색상입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void DrawShotDebugRay(HitscanShotInfo shotInfo, Color color)
    {
        Debug.DrawLine(shotInfo.Origin, shotInfo.EndPoint, color, 1.0f, false);
    }

    /// <summary>
    /// (에디터 전용) 사격 순간 방사각·편향 진단 로그를 출력합니다. 빌드에서는 호출이 스트립됩니다.
    /// </summary>
    /// <param name="isAds">조준(ADS) 상태 여부입니다.</param>
    /// <param name="spread">이번 사격에 적용된 방사각(도)입니다.</param>
    /// <param name="aimDirection">탄퍼짐 미적용 조준 방향입니다.</param>
    /// <param name="firedDirection">탄퍼짐 적용 후 실제 발사 방향입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogSpreadDebug(bool isAds, float spread, Vector3 aimDirection, Vector3 firedDirection)
    {
#if UNITY_EDITOR
        if (!m_debugLogSpread)
        {
            return;
        }

        float deviationDeg = Vector3.Angle(aimDirection, firedDirection);
        Debug.Log($"[SpreadDebug] isAds={isAds} spread={spread:F3}° deviation={deviationDeg:F3}° " +
                  $"(ads[{m_adsMinSpread:F2}~{m_adsMaxSpread:F2}] add={m_adsCurrentSpreadAdd:F2} / " +
                  $"hip[{m_hipfireMinSpread:F2}~{m_hipfireMaxSpread:F2}] add={m_hipfireCurrentSpreadAdd:F2})", this);
#endif
    }

    /// <summary>
    /// 히트스캔 충돌 대상이 적대 진영이면 공용 피해 경로로 피해를 전달합니다.
    /// </summary>
    /// <param name="shotInfo">사격으로 발생한 히트스캔 충돌 정보입니다.</param>
    /// <remarks>대상 구체 타입을 모른 채 <see cref="CombatDamage"/>가 진영·부위 판정 후 적용하고, 피격 확정 시 <see cref="OnHitFeedback"/>를 발생시킵니다.</remarks>
    private void ApplyHitscanDamage(HitscanShotInfo shotInfo)
    {
        if (m_hitscanDamage <= 0 || shotInfo.Hit.collider == null)
        {
            return;
        }

        CombatDamage.HitFeedback feedback = CombatDamage.ResolveHit(
            shotInfo.Hit.collider,
            m_ownerFaction,
            m_hitscanDamage,
            m_headshotDamageMultiplier,
            m_allowHeadshot);
        if (feedback.Applied)
        {
            OnHitFeedback?.Invoke(feedback);
        }
    }

    /// <summary>
    /// 사격 쿨다운을 종료하고 다시 사격 가능한 상태로 전환합니다.
    /// </summary>
    private void ResetShoot()
    {
        m_canShoot = true;
    }

    /// <summary>
    /// 탄환 오브젝트를 풀에서 꺼내 목표 방향으로 생성합니다.
    /// </summary>
    /// <param name="targetPosition">탄환이 향할 월드 좌표입니다.</param>
    private void SpawnBullet(Vector3 targetPosition)
    {
        if (m_firePos == null || PoolManager.instance == null)
        {
            return;
        }

        Vector3 shootDirection = targetPosition - m_firePos.position;

        if (shootDirection.sqrMagnitude < 0.0001f)
        {
            shootDirection = m_firePos.forward;
        }
        else
        {
            shootDirection.Normalize();
        }

        Quaternion bulletRotation = Quaternion.LookRotation(shootDirection);

        PoolManager.instance.GetObject(
            m_bulletPoolIndex,
            m_firePos.position,
            bulletRotation);
    }

    /// <summary>
    /// 탄피 오브젝트를 풀에서 생성합니다.
    /// </summary>
    private void SpawnShell()
    {
        if (m_shellPos == null || PoolManager.instance == null)
        {
            return;
        }

        PoolManager.instance.GetObject(
            m_shellPoolIndex,
            m_shellPos.position,
            m_shellPos.rotation);
    }

    /// <summary>
    /// 탄창 오브젝트를 풀에서 생성합니다.
    /// </summary>
    private void SpawnClip()
    {
        if (m_clipPos == null || PoolManager.instance == null)
        {
            return;
        }

        PoolManager.instance.GetObject(
            m_clipPoolIndex,
            m_clipPos.position,
            m_clipPos.rotation);
    }

    /// <summary>
    /// 머즐 플래시 파티클을 재생합니다.
    /// </summary>
    private void SpawnMuzzleFlash()
    {
        if (m_muzzleFlashParticle == null)
        {
            return;
        }

        m_muzzleFlashParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        m_muzzleFlashParticle.Play();
    }

    /// <summary>
    /// 재장전 애니메이션에서 탄창이 빠지는 타이밍에 호출되는 이벤트입니다.
    /// </summary>
    public void OnReloadMagOut()
    {
        SpawnClip();
    }

    /// <summary>
    /// 사격 효과음을 재생합니다.
    /// </summary>
    private void PlayShootSound()
    {
        if (m_audioSource == null || m_shootClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_shootClip);
    }

    /// <summary>
    /// 재장전 효과음을 재생합니다.
    /// </summary>
    private void PlayReloadSound()
    {
        if (m_audioSource == null || m_reloadClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_reloadClip);
    }

    /// <summary>
    /// 풀 탄창 등으로 재장전이 막혔을 때 빈 장전(드라이) 효과음을 재생합니다.
    /// </summary>
    /// <remarks>효과음 클립이 비어 있으면 아무 소리도 내지 않습니다. 사운드 배선 전이라도 호출 틀은 유지됩니다.</remarks>
    public void PlayEmptyReloadSound()
    {
        if (m_audioSource == null || m_emptyReloadClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_emptyReloadClip);
    }

    /// <summary>
    /// 현재 탄약 UI를 갱신합니다.
    /// </summary>
    public void UpdateBulletUI()
    {
        if (m_bulletText != null)
        {
            m_bulletText.text = m_currentBullet.ToString();
        }

        OnBulletChanged?.Invoke(m_currentBullet, m_maxBullet);
    }

    /// <summary>
    /// 탄약 표시용 UI 텍스트를 교체하고 즉시 갱신합니다.
    /// </summary>
    /// <param name="targetUI">새로 사용할 탄약 UI 텍스트입니다.</param>
    public void SetBulletUI(Text targetUI)
    {
        m_bulletText = targetUI;
        UpdateBulletUI();
    }

    /// <summary>
    /// 현재 탄약 수를 설정합니다.
    /// </summary>
    /// <param name="value">새 현재 탄약 수입니다.</param>
    public void SetCurrentBullet(int value)
    {
        m_currentBullet = Mathf.Clamp(value, 0, m_maxBullet);
        UpdateBulletUI();
    }

    /// <summary>
    /// 최대 탄약 수를 설정합니다.
    /// </summary>
    /// <param name="value">새 최대 탄약 수입니다.</param>
    public void SetMaxBullet(int value)
    {
        m_maxBullet = Mathf.Max(0, value);
        m_currentBullet = Mathf.Clamp(m_currentBullet, 0, m_maxBullet);
        UpdateBulletUI();
    }

    /// <summary>
    /// 사격 지연 시간을 설정합니다.
    /// </summary>
    /// <param name="value">새 사격 지연 시간입니다.</param>
    public void SetShootDelay(float value)
    {
        m_shootDelay = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 재장전 시간을 설정합니다.
    /// </summary>
    /// <param name="value">새 재장전 시간입니다.</param>
    public void SetReloadTime(float value)
    {
        m_reloadTime = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 탄약이 최대치일 때도 재장전을 허용할지 여부를 설정합니다.
    /// </summary>
    /// <param name="value">풀 탄창 재장전을 허용하면 <c>true</c>입니다. 기본값은 <c>false</c>이며 디버그 용도입니다.</param>
    public void SetAllowFullMagReload(bool value)
    {
        m_allowFullMagReload = value;
    }

    // ─────────────────────────────────────────────────────────────
    // 플레이테스트 트레이너용 런타임 스탯 setter. 값 보정 규칙은 ClampBulletValues와 일치시킵니다.
    // ─────────────────────────────────────────────────────────────

    /// <summary>이번 무기의 최소 힙파이어 방사각(도)입니다.</summary>
    public float HipfireMinSpread => m_hipfireMinSpread;

    /// <summary>이번 무기의 최대 힙파이어 방사각(도)입니다.</summary>
    public float HipfireMaxSpread => m_hipfireMaxSpread;

    /// <summary>이번 무기의 최소 ADS 방사각(도)입니다.</summary>
    public float AdsMinSpread => m_adsMinSpread;

    /// <summary>이번 무기의 최대 ADS 방사각(도)입니다.</summary>
    public float AdsMaxSpread => m_adsMaxSpread;

    /// <summary>히트스캔 사격 피해량을 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 피해량입니다.</param>
    public void SetHitscanDamage(int value) => m_hitscanDamage = Mathf.Max(0, value);

    /// <summary>이 무기의 약점 판정 사용 여부를 설정합니다.</summary>
    /// <param name="value">약점 판정을 쓰면 true입니다.</param>
    public void SetAllowHeadshot(bool value) => m_allowHeadshot = value;

    /// <summary>헤드샷 피해 배율을 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 배율입니다.</param>
    public void SetHeadshotDamageMultiplier(float value) => m_headshotDamageMultiplier = Mathf.Max(0.0f, value);

    /// <summary>히트스캔 사거리를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 사거리입니다.</param>
    public void SetHitscanRange(float value) => m_hitscanRange = Mathf.Max(0.0f, value);

    /// <summary>세로(피치) 반동 각도(도)를 설정합니다.</summary>
    /// <param name="value">새 반동 각도입니다.</param>
    public void SetRecoilPitchKick(float value) => m_recoilPitchKick = value;

    /// <summary>좌우(요) 반동 각도(도)를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 반동 각도입니다.</param>
    public void SetRecoilYawKick(float value) => m_recoilYawKick = Mathf.Max(0.0f, value);

    /// <summary>카메라 롤(Dutch) 시각 킥 크기(도)를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 시각 킥 크기입니다.</param>
    public void SetRecoilRoll(float value) => m_recoilRoll = Mathf.Max(0.0f, value);

    /// <summary>카메라 FOV 펀치 시각 킥 크기(도)를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 FOV 펀치 크기입니다.</param>
    public void SetRecoilFovPunch(float value) => m_recoilFovPunch = Mathf.Max(0.0f, value);

    /// <summary>좌우 반동 방향 패턴을 설정합니다.</summary>
    /// <param name="value">새 반동 방향 패턴입니다.</param>
    public void SetYawKickPattern(KickSidePattern value) => m_yawKickPattern = value;

    /// <summary>카메라 롤 방향 패턴을 설정합니다.</summary>
    /// <param name="value">새 시각 킥 방향 패턴입니다.</param>
    public void SetRollKickPattern(KickSidePattern value) => m_rollKickPattern = value;

    /// <summary>히트스캔 충돌 판정 레이어 마스크를 설정합니다.</summary>
    /// <param name="value">새 충돌 판정 레이어 마스크입니다.</param>
    public void SetHitscanLayerMask(LayerMask value) => m_hitscanLayerMask = value;

    /// <summary>탄퍼짐 콘 내부의 분포 방식을 설정합니다.</summary>
    /// <param name="value">새 분포 방식입니다.</param>
    public void SetSpreadDistribution(SpreadDistribution value) => m_spreadDistribution = value;

    /// <summary>Gaussian 탄퍼짐 분포의 중심 집중도를 설정합니다.</summary>
    /// <param name="value">1보다 작은 값은 1로 보정됩니다.</param>
    public void SetSpreadConcentration(float value) => m_spreadConcentration = Mathf.Max(1.0f, value);

    /// <summary>힙파이어 최소/최대 방사각(도)을 설정합니다. max는 min 이상으로 보정합니다.</summary>
    /// <param name="minSpread">새 최소 방사각입니다.</param>
    /// <param name="maxSpread">새 최대 방사각입니다.</param>
    public void SetHipfireSpread(float minSpread, float maxSpread)
    {
        m_hipfireMinSpread = Mathf.Max(0.0f, minSpread);
        m_hipfireMaxSpread = Mathf.Max(m_hipfireMinSpread, maxSpread);
    }

    /// <summary>ADS 최소/최대 방사각(도)을 설정합니다. max는 min 이상으로 보정합니다.</summary>
    /// <param name="minSpread">새 최소 방사각입니다.</param>
    /// <param name="maxSpread">새 최대 방사각입니다.</param>
    public void SetAdsSpread(float minSpread, float maxSpread)
    {
        m_adsMinSpread = Mathf.Max(0.0f, minSpread);
        m_adsMaxSpread = Mathf.Max(m_adsMinSpread, maxSpread);
    }

    /// <summary>힙파이어 연사 탄퍼짐 누적과 회복 규칙을 설정합니다.</summary>
    /// <param name="minSpreadShotCount">최소 탄퍼짐을 유지할 첫 연속 발사 수입니다.</param>
    /// <param name="spreadIncreasePerShot">그 이후 발마다 누적할 방사각입니다.</param>
    /// <param name="recoveryDelay">사격 중단 후 회복 시작 지연 시간입니다.</param>
    /// <param name="recoveryPerSecond">초당 회복 방사각입니다.</param>
    public void SetHipfireBurstSpread(
        int minSpreadShotCount,
        float spreadIncreasePerShot,
        float recoveryDelay,
        float recoveryPerSecond)
    {
        m_hipfireMinSpreadShotCount = Mathf.Max(0, minSpreadShotCount);
        m_hipfireSpreadIncreasePerShot = Mathf.Max(0.0f, spreadIncreasePerShot);
        m_hipfireSpreadRecoveryDelay = Mathf.Max(0.0f, recoveryDelay);
        m_hipfireSpreadRecoveryPerSecond = Mathf.Max(0.0f, recoveryPerSecond);
    }

    /// <summary>ADS 연사 탄퍼짐 누적과 회복 규칙을 설정합니다.</summary>
    /// <param name="minSpreadShotCount">최소 탄퍼짐을 유지할 첫 연속 발사 수입니다.</param>
    /// <param name="spreadIncreasePerShot">그 이후 발마다 누적할 방사각입니다.</param>
    /// <param name="recoveryDelay">사격 중단 후 회복 시작 지연 시간입니다.</param>
    /// <param name="recoveryPerSecond">초당 회복 방사각입니다.</param>
    public void SetAdsBurstSpread(
        int minSpreadShotCount,
        float spreadIncreasePerShot,
        float recoveryDelay,
        float recoveryPerSecond)
    {
        m_adsMinSpreadShotCount = Mathf.Max(0, minSpreadShotCount);
        m_adsSpreadIncreasePerShot = Mathf.Max(0.0f, spreadIncreasePerShot);
        m_adsSpreadRecoveryDelay = Mathf.Max(0.0f, recoveryDelay);
        m_adsSpreadRecoveryPerSecond = Mathf.Max(0.0f, recoveryPerSecond);
    }
}
