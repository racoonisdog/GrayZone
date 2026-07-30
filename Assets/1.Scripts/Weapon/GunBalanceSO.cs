using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// <see cref="Gun"/>에 주입할 순수 수치 밸런스 데이터를 보관합니다.
/// </summary>
/// <remarks>
/// 필드 이름은 <see cref="Gun"/>의 <see cref="BalanceFieldAttribute"/> 필드와 <b>완전히 동일</b>합니다.
/// <see cref="BindManager"/>가 이름으로 짝을 찾아 값을 대입하므로, 이름이 어긋나면 바인딩이 실패합니다.
/// 값의 정본은 엑셀 시트이며 CSV 임포터가 이 SO를 채웁니다. Inspector에서 숫자를 직접 고치지 않습니다.
/// 허용 범위(Min/Max)는 값을 받는 <see cref="Gun"/> 쪽 특성에 선언되어 있고, 실제 보정은 대입 시점에 일어납니다.
/// 프리팹, Transform, AudioClip, ParticleSystem 같은 런타임 및 미디어 참조는 담지 않습니다. 그런 참조는 Inspector에서 직접 배선합니다.
/// </remarks>
[CreateAssetMenu(fileName = "GunBalanceSO", menuName = "GrayZone/Weapon/Gun Balance SO")]
public sealed class GunBalanceSO : ScriptableObject, IBalanceTableData
{
    // 시트에서 이 행이 어느 무기인지 식별하는 값입니다. WeaponController에는 대응 필드가 없어 바인딩되지 않습니다.
    [Header("Identity")]
    [Tooltip("테이블과 향후 런타임 어댑터에서 사용할 고정 무기 ID입니다.")]
    [SerializeField] private string m_weaponId = "weapon.rifle.01";

    [Tooltip("무기 분류입니다.")]
    [SerializeField] private WeaponType m_weaponType = WeaponType.AssaultRifle;

    [Tooltip("기획 시트와 Inspector에 표시할 무기 이름입니다.")]
    [SerializeField] private string m_weaponName = "Rifle 1";

    [Header("Magazine / Timing")]
    [Tooltip("한 탄창의 최대 장탄 수입니다.")]
    [FormerlySerializedAs("m_magazineCapacity")]
    [SerializeField] private int m_maxBullet = 30;

    [Tooltip("발사 후 다음 발사가 가능해질 때까지의 지연 시간(초)입니다.")]
    [SerializeField] private float m_shootDelay = 0.12f;

    [Tooltip("재장전에 필요한 시간(초)입니다.")]
    [SerializeField] private float m_reloadTime = 1.5f;

    [Header("Hitscan")]
    [Tooltip("일반 히트스캔 명중 피해량입니다.")]
    [SerializeField] private int m_hitscanDamage = 1;

    [Tooltip("이 무기가 약점 판정을 사용하는지 여부입니다. 끄면 약점 부위를 맞혀도 일반 피해로 처리합니다.")]
    [SerializeField] private bool m_allowHeadshot = true;

    [Tooltip("헤드샷 명중 시 기본 피해량에 곱하는 배율입니다. 약점 판정이 꺼져 있으면 사용하지 않습니다.")]
    [SerializeField] private float m_headshotDamageMultiplier = 2f;

    [Tooltip("히트스캔 판정 최대 거리(m)입니다.")]
    [SerializeField] private float m_hitscanRange = 100f;

    [Header("Hipfire Spread")]
    [Tooltip("비조준 사격의 최소 방사각(도)입니다.")]
    [SerializeField] private float m_hipfireMinSpread = 4f;

    [Tooltip("비조준 연사 시 도달할 수 있는 최대 방사각(도)입니다. 최소 방사각보다 작으면 대입 시점에 되돌립니다.")]
    [SerializeField] private float m_hipfireMaxSpread = 10f;

    [Tooltip("비조준에서 최소 방사각을 유지하는 발수입니다.")]
    [SerializeField] private int m_hipfireMinSpreadShotCount = 3;

    [Tooltip("비조준 발사마다 증가하는 방사각(도)입니다.")]
    [SerializeField] private float m_hipfireSpreadIncreasePerShot = 1f;

    [Tooltip("비조준 탄퍼짐의 초당 회복량(도/초)입니다.")]
    [SerializeField] private float m_hipfireSpreadRecoveryPerSecond = 8f;

    [Tooltip("비조준 사격 중지 후 탄퍼짐 회복을 시작하는 지연 시간(초)입니다.")]
    [SerializeField] private float m_hipfireSpreadRecoveryDelay = 0.3f;

    [Header("ADS Spread")]
    [Tooltip("조준 사격의 최소 방사각(도)입니다.")]
    [SerializeField] private float m_adsMinSpread = 0f;

    [Tooltip("조준 연사 시 도달할 수 있는 최대 방사각(도)입니다. 최소 방사각보다 작으면 대입 시점에 되돌립니다.")]
    [SerializeField] private float m_adsMaxSpread = 6f;

    [Tooltip("조준 상태에서 최소 방사각을 유지하는 발수입니다.")]
    [SerializeField] private int m_adsMinSpreadShotCount = 3;

    [Tooltip("조준 발사마다 증가하는 방사각(도)입니다.")]
    [SerializeField] private float m_adsSpreadIncreasePerShot = 1f;

    [Tooltip("조준 탄퍼짐의 초당 회복량(도/초)입니다.")]
    [SerializeField] private float m_adsSpreadRecoveryPerSecond = 8f;

    [Tooltip("조준 사격 중지 후 탄퍼짐 회복을 시작하는 지연 시간(초)입니다.")]
    [SerializeField] private float m_adsSpreadRecoveryDelay = 0.3f;

    [Header("Spread Distribution")]
    [Tooltip("탄퍼짐 콘 내부에서 탄착을 분포시키는 방식입니다.")]
    [SerializeField] private SpreadDistribution m_spreadDistribution = SpreadDistribution.Gaussian;

    [Tooltip("Gaussian 분포의 중심 집중도입니다. 값이 클수록 중심에 모입니다.")]
    [SerializeField] private float m_spreadConcentration = 3f;

    [Header("Recoil")]
    [Tooltip("발사 1회당 세로 반동 각도(도)입니다.")]
    [SerializeField] private float m_recoilPitchKick = 0.6f;

    [Tooltip("발사 1회당 좌우 반동 각도(도)의 최대 크기입니다.")]
    [SerializeField] private float m_recoilYawKick = 0.2f;

    [Tooltip("좌우 반동의 방향 패턴입니다.")]
    [SerializeField] private KickSidePattern m_yawKickPattern = KickSidePattern.Random;

    [Tooltip("화면 연출용 카메라 롤 크기(도)입니다.")]
    [SerializeField] private float m_recoilRoll = 0.5f;

    [Tooltip("카메라 롤의 방향 패턴입니다.")]
    [SerializeField] private KickSidePattern m_rollKickPattern = KickSidePattern.Random;

    [Tooltip("발사 1회당 화면 연출용 FOV 펀치 크기(도)입니다.")]
    [SerializeField] private float m_recoilFovPunch = 1f;

    // WeaponController에 대응 필드가 아직 없어 바인딩되지 않습니다. 경직 소비를 구현하면 그때 연결됩니다.
    [Header("Impact")]
    [Tooltip("명중 시 적 경직력 누적에 더하는 저지력입니다. 피해와 별개이며, 경직 전달/소비 구현 전까지 데이터 자리만 둡니다.")]
    [SerializeField] private float m_stoppingPower = 0f;
}
