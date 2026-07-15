using System;
using UnityEngine;

/// <summary>
/// 플레이어블 캐릭터의 고정 식별자입니다.
/// </summary>
public enum PlayableCharacterId
{
    /// <summary>아직 캐릭터가 지정되지 않은 상태입니다.</summary>
    Unknown = 0,

    /// <summary>나린입니다.</summary>
    Narin = 1,

    /// <summary>청솔입니다.</summary>
    Cheongsol = 2,

    /// <summary>서하입니다.</summary>
    Seoha = 3
}

[DisallowMultipleComponent]
/// <summary>
/// 플레이어 유닛의 공용 상태를 모아두는 공개 데이터 Module입니다.
/// 외부 시스템은 이 Module을 통해 식별 정보, 신뢰도, 생존 상태, 체력 요약값을 읽습니다.
/// </summary>
public class PlayerbleUnitData : MonoBehaviour
{
    [Header("Public Identity")]
    [Tooltip("보유 캐릭터 목록과 배틀 결과를 연결하는 영속 정의 ID입니다.")]
    [SerializeField] private string m_definitionId;

    [SerializeField] private string m_runtimeId;
    [SerializeField] private PlayableCharacterId m_characterId = PlayableCharacterId.Unknown;
    [SerializeField] private string m_displayName = "Player";

    [Header("Public Runtime Data")]
    [Range(0, 100)]
    [SerializeField] private int m_reliability;

    [Header("Observed Modules")]
    [SerializeField] private PlayerHealth m_health;
    [SerializeField] private SquadMemberController m_squadMember;
    [SerializeField] private InteractionController m_interactionController;
    [SerializeField] private WeaponController m_weaponController;
    [SerializeField] private Transform m_publicTarget;

    [Header("Weapon Public Data")]
    [Tooltip("이 캐릭터에 기본으로 지정된 총기 정의입니다. 배틀 입장 시 새로 스폰하지 않고 이 참조를 기준으로 총기 상태를 구성합니다.")]
    [SerializeField] private Weapon m_currentWeapon;

    [Tooltip("총기 정의가 없을 때 UI와 로그에 표시할 대체 이름입니다.")]
    [SerializeField] private string m_currentWeaponFallbackName;

    [Header("Ammo Inventory")]
    [SerializeField] private int m_reserveAmmo;
    [SerializeField] private int m_maxReserveAmmo = 120;

#if UNITY_EDITOR
    [Header("Debug")]
    [Tooltip("켜면 예비 탄약(탄창)이 줄어들지 않습니다(무한 탄창). Player 빌드에서는 항상 꺼진 것으로 취급됩니다.")]
    [SerializeField] private bool m_debugInfiniteReserveAmmo = false;

    /// <summary>무한 탄창(예비 탄약) 디버그 플래그입니다. 디버그 트레이너 창에서 사용합니다.</summary>
    public bool DebugInfiniteReserveAmmo
    {
        get => m_debugInfiniteReserveAmmo;
        set => m_debugInfiniteReserveAmmo = value;
    }

    /// <summary>
    /// 무한 체력 디버그 플래그입니다. 실제 저장·판정은 <see cref="PlayerHealth"/>가 소유하며,
    /// 이 프로퍼티는 디버그 트레이너 창이 단일 진입점(<c>PlayerbleUnitData</c>)으로만 접근하도록 하는 패스스루입니다.
    /// </summary>
    public bool DebugInfiniteHealth
    {
        get => m_health != null && m_health.DebugInfiniteHealth;
        set
        {
            if (m_health != null)
            {
                m_health.DebugInfiniteHealth = value;
            }
        }
    }

    /// <summary>
    /// 무한 장탄수(현재 탄창) 디버그 플래그입니다. 실제 저장·판정은 <see cref="WeaponController"/>가 소유하며,
    /// 이 프로퍼티는 디버그 트레이너 창이 단일 진입점(<c>PlayerbleUnitData</c>)으로만 접근하도록 하는 패스스루입니다.
    /// </summary>
    public bool DebugInfiniteMagazine
    {
        get => m_weaponController != null && m_weaponController.DebugInfiniteMagazine;
        set
        {
            if (m_weaponController != null)
            {
                m_weaponController.DebugInfiniteMagazine = value;
            }
        }
    }
#endif

    /// <summary>
    /// 공개 상태 중 외부에 노출되는 값이 바뀌었을 때 발생합니다.
    /// </summary>
    public event Action OnPublicDataChanged;

    /// <summary>
    /// 보유 캐릭터 목록과 배틀 결과를 연결하는 영속 정의 ID입니다.
    /// </summary>
    public string DefinitionId => m_definitionId?.Trim() ?? string.Empty;

    /// <summary>
    /// 이 유닛을 외부 시스템에서 식별하기 위한 런타임 ID입니다.
    /// 값이 비어 있으면 GameObject 이름을 반환합니다.
    /// </summary>
    public string RuntimeId => string.IsNullOrWhiteSpace(m_runtimeId)
        ? gameObject.name
        : m_runtimeId.Trim();

    /// <summary>
    /// 플레이어블 캐릭터의 고정 식별자입니다.
    /// </summary>
    public PlayableCharacterId CharacterId => m_characterId;

    /// <summary>
    /// UI나 로그에서 표시할 이름입니다.
    /// 값이 비어 있으면 GameObject 이름을 반환합니다.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(m_displayName)
        ? gameObject.name
        : m_displayName.Trim();

    /// <summary>
    /// 셸터, 관계, 출격 판정 등에 사용할 수 있는 공용 신뢰도 값입니다.
    /// </summary>
    public int Reliability => m_reliability;

    /// <summary>
    /// 현재 HP입니다. 실제 원본 값은 PlayerHealth가 소유합니다.
    /// </summary>
    public int CurrentHp => m_health != null ? m_health.CurrentHP : 0;

    /// <summary>
    /// 최대 HP입니다. 실제 원본 값은 PlayerHealth가 소유합니다.
    /// </summary>
    public int MaxHp => m_health != null ? m_health.MaxHP : 0;

    /// <summary>
    /// 현재 HP를 최대 HP 기준 0~100으로 환산한 값입니다.
    /// </summary>
    public int HealthPercent => MaxHp > 0 ? CurrentHp * 100 / MaxHp : 0;

    /// <summary>
    /// 체력 기준으로 사망 상태인지 여부입니다.
    /// </summary>
    public bool IsDead => m_health != null && m_health.IsDead;

    /// <summary>
    /// 플레이어가 살아 있는지 여부입니다.
    /// SquadMemberController 상태까지 함께 반영합니다.
    /// </summary>
    public bool IsAlive => !IsDead && (m_squadMember == null || m_squadMember.IsAlive);

    /// <summary>
    /// 현재 다운 상태인지 여부입니다.
    /// </summary>
    public bool IsDown => m_squadMember != null && m_squadMember.IsDown;

    /// <summary>
    /// 직접 조작 중인지 여부입니다.
    /// </summary>
    public bool IsPlayerSquadMember => m_squadMember != null && m_squadMember.IsPlayerSquadMember;

    /// <summary>현재 스쿼드 AI가 조작하는 AiSquadMember인지 여부입니다.</summary>
    public bool IsAiSquadMember => m_squadMember != null && m_squadMember.IsAiSquadMember;

    /// <summary>
    /// 출격 또는 배치 가능한 상태인지 여부입니다.
    /// </summary>
    public bool CanDeploy => IsAlive && !IsDown;

    public DownedAllyInteractable CurrentReviveInteractionTarget => m_interactionController != null
        ? m_interactionController.Current as DownedAllyInteractable
        : null;

    public bool HasReviveInteractionTarget => CurrentReviveInteractionTarget != null;

    public bool IsReviving => CurrentReviveInteractionTarget != null
        && (m_interactionController.HoldProgress01 > 0.0f
            || CurrentReviveInteractionTarget.IsReviveHoldActive);

    public float ReviveGaugeAmount => CurrentReviveInteractionTarget != null
        ? Mathf.Clamp01(Mathf.Max(m_interactionController.HoldProgress01, CurrentReviveInteractionTarget.ReviveHoldProgress01))
        : 0.0f;

    /// <summary>
    /// 현재 부상 상태입니다.
    /// </summary>
    public PlayerInjuryState InjuryState => m_health != null
        ? m_health.CurrentInjuryState
        : PlayerInjuryState.Normal;

    /// <summary>
    /// 부상 게이지를 0~1 범위로 정규화한 값입니다.
    /// </summary>
    public float InjuryGaugeNormalized => m_health != null
        ? m_health.InjuryGaugeNormalized
        : 0.0f;

    /// <summary>
    /// 현재 장착 무기의 컨트롤러입니다.
    /// </summary>
    public WeaponController WeaponController => m_weaponController;

    /// <summary>
    /// 현재 장착 무기의 정의 데이터입니다.
    /// 비어 있으면 무기 컨트롤러와 fallback 이름만 사용합니다.
    /// </summary>
    public Weapon CurrentWeapon => m_currentWeapon;

    /// <summary>
    /// 현재 무기 정의 데이터가 연결되어 있는지 여부입니다.
    /// </summary>
    public bool HasCurrentWeaponDefinition => m_currentWeapon != null;

    /// <summary>
    /// 현재 무기 ID입니다.
    /// </summary>
    public string CurrentWeaponId => m_currentWeapon != null
        ? (m_currentWeapon.weaponId?.Trim() ?? string.Empty)
        : string.Empty;

    /// <summary>
    /// 현재 무기 이름입니다. 정의 데이터, fallback 이름, 컨트롤러 오브젝트 이름 순으로 반환합니다.
    /// </summary>
    public string CurrentWeaponName => ResolveCurrentWeaponName();

    /// <summary>
    /// 현재 무기 타입입니다. 정의 데이터가 없으면 enum 기본값을 반환합니다.
    /// </summary>
    public WeaponType CurrentWeaponType => m_currentWeapon != null
        ? m_currentWeapon.weaponType
        : default;

    /// <summary>
    /// 현재 무기의 한 탄창 기준 장탄 수입니다.
    /// </summary>
    public int CurrentMagazineAmmo => m_weaponController != null ? m_weaponController.CurrentBullet : 0;

    /// <summary>
    /// 현재 무기의 탄창 용량입니다.
    /// </summary>
    public int MagazineCapacity => m_weaponController != null ? m_weaponController.MaxBullet : 0;

    /// <summary>
    /// 플레이어가 탄창 밖에 보유 중인 예비 탄약 수입니다.
    /// </summary>
    public int ReserveAmmo => m_reserveAmmo;

    /// <summary>
    /// 플레이어가 보유할 수 있는 예비 탄약 최대치입니다.
    /// </summary>
    public int MaxReserveAmmo => m_maxReserveAmmo;

    /// <summary>
    /// 현재 탄창과 예비 탄약을 합산한 총 보유 탄약 수입니다.
    /// </summary>
    public int TotalAmmo => CurrentMagazineAmmo + m_reserveAmmo;

    /// <summary>
    /// 현재 무기와 탄약 상태를 로그/UI용으로 짧게 요약한 문자열입니다.
    /// </summary>
    public string CurrentWeaponShortInfo => BuildCurrentWeaponShortInfo();

    /// <summary>
    /// 외부 시스템이 이 유닛을 대상으로 삼을 기준 Transform입니다.
    /// </summary>
    public Transform PublicTarget => m_publicTarget != null ? m_publicTarget : transform;

    /// <summary>현재 씬 캐릭터와 장착 총기 상태를 공용 스냅샷 구조로 깊은 복사하여 반환합니다.</summary>
    public CharacterSnapshotData CreateCharacterSnapshot(NPCType npcType = default)
    {
        PlayerHealth health = m_health;
        WeaponSnapshotData weaponSnapshot = WeaponSnapshotData.Create(
            m_currentWeapon,
            m_weaponController,
            m_reserveAmmo,
            m_maxReserveAmmo);

        return new CharacterSnapshotData(
            DefinitionId,
            RuntimeId,
            CharacterId,
            npcType,
            DisplayName,
            Reliability,
            CurrentHp,
            Mathf.Max(1, MaxHp),
            health != null ? health.CurrentInjuryGauge : 0.0f,
            health != null ? health.MaxInjuryGauge : 100.0f,
            health != null ? health.CurrentInjuryState : PlayerInjuryState.Normal,
            health != null ? health.IsDowned : IsDown,
            IsDead,
            IsPlayerSquadMember,
            0,
            weaponSnapshot);
    }

    /// <summary>GameDataManager에서 받은 공용 캐릭터·총기 스냅샷을 씬 유닛의 입장 상태에 적용합니다.</summary>
    /// <remarks>
    /// 총기 정의는 캐릭터에 지정된 <c>m_currentWeapon</c> 참조를 유지하고, 스냅샷에서는 성장·파츠·탄약 상태를 전달합니다.
    /// 현재 구현은 식별 정보, HP, 부상과 탄약까지 씬 컴포넌트에 적용합니다. 파츠에 따른 외형 변경은 정책이 확정되지 않아
    /// 처리하지 않으며, 계산 스탯과 파츠 ID는 이후 적용 지점을 위해 스냅샷에 그대로 보존합니다.
    /// </remarks>
    public bool ApplyCharacterSnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            m_definitionId = snapshot.DefinitionId;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RuntimeId))
        {
            m_runtimeId = snapshot.RuntimeId;
        }

        if (snapshot.CharacterId != PlayableCharacterId.Unknown)
        {
            m_characterId = snapshot.CharacterId;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DisplayName))
        {
            m_displayName = snapshot.DisplayName;
        }

        m_reliability = snapshot.Reliability;
        m_health?.ApplySnapshotState(
            snapshot.CurrentHp,
            snapshot.MaxHp,
            snapshot.InjurySeverityGauge,
            snapshot.MaxInjuryGauge);

        WeaponSnapshotData weaponSnapshot = snapshot.Weapon;
        bool hasWeaponState = !string.IsNullOrWhiteSpace(weaponSnapshot.WeaponId)
            || weaponSnapshot.MagazineCapacity > 0
            || weaponSnapshot.MaxReserveAmmo > 0;
        if (hasWeaponState)
        {
            m_maxReserveAmmo = weaponSnapshot.MaxReserveAmmo;
            m_reserveAmmo = weaponSnapshot.ReserveAmmo;
            if (m_currentWeapon == null && !string.IsNullOrWhiteSpace(weaponSnapshot.DisplayName))
            {
                m_currentWeaponFallbackName = weaponSnapshot.DisplayName;
            }

            if (m_weaponController != null)
            {
                m_weaponController.SetMaxBullet(weaponSnapshot.MagazineCapacity);
                m_weaponController.SetCurrentBullet(weaponSnapshot.CurrentMagazineAmmo);
            }
        }

        NotifyPublicDataChanged();
        return true;
    }

    private void Reset()
    {
        CacheReferences();
        ClampValues();
    }

    private void Awake()
    {
        CacheReferences();
        ClampValues();
    }

    private void OnEnable()
    {
        CacheReferences();
        SubscribeHealth();
        SubscribeSquadMember();
        SubscribeWeapon();
    }

    private void OnDisable()
    {
        UnsubscribeHealth();
        UnsubscribeSquadMember();
        UnsubscribeWeapon();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ClampValues();
    }
#endif

    /// <summary>
    /// 런타임 ID를 설정합니다.
    /// </summary>
    /// <param name="value">새 런타임 ID입니다.</param>
    public void SetRuntimeId(string value)
    {
        string nextValue = value?.Trim() ?? string.Empty;
        if (m_runtimeId == nextValue)
        {
            return;
        }

        m_runtimeId = nextValue;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 보유 캐릭터 목록과 연결할 영속 정의 ID를 설정합니다.
    /// </summary>
    /// <param name="value">셸터 NPC 정의 ID입니다.</param>
    public void SetDefinitionId(string value)
    {
        string nextValue = value?.Trim() ?? string.Empty;
        if (m_definitionId == nextValue)
        {
            return;
        }

        m_definitionId = nextValue;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 플레이어블 캐릭터 식별자를 설정합니다.
    /// </summary>
    /// <param name="value">새 캐릭터 식별자입니다.</param>
    public void SetCharacterId(PlayableCharacterId value)
    {
        if (m_characterId == value)
        {
            return;
        }

        m_characterId = value;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 표시 이름을 설정합니다.
    /// </summary>
    /// <param name="value">새 표시 이름입니다.</param>
    public void SetDisplayName(string value)
    {
        string nextValue = value?.Trim() ?? string.Empty;
        if (m_displayName == nextValue)
        {
            return;
        }

        m_displayName = nextValue;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 신뢰도를 0~100 범위로 설정합니다.
    /// </summary>
    /// <param name="value">새 신뢰도 값입니다.</param>
    public void SetReliability(int value)
    {
        int clampedValue = Mathf.Clamp(value, 0, 100);
        if (m_reliability == clampedValue)
        {
            return;
        }

        m_reliability = clampedValue;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 신뢰도를 증감합니다.
    /// </summary>
    /// <param name="amount">더할 값입니다. 음수도 허용됩니다.</param>
    public void AddReliability(int amount)
    {
        SetReliability(m_reliability + amount);
    }

    /// <summary>
    /// 현재 무기 정의 데이터를 설정합니다.
    /// </summary>
    /// <param name="weapon">새 무기 정의 데이터입니다.</param>
    public void SetCurrentWeapon(Weapon weapon)
    {
        if (m_currentWeapon == weapon)
        {
            return;
        }

        m_currentWeapon = weapon;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 현재 무기 이름 fallback 값을 설정합니다.
    /// </summary>
    /// <param name="value">무기 정의 데이터가 없을 때 표시할 이름입니다.</param>
    public void SetCurrentWeaponFallbackName(string value)
    {
        string nextValue = value?.Trim() ?? string.Empty;
        if (m_currentWeaponFallbackName == nextValue)
        {
            return;
        }

        m_currentWeaponFallbackName = nextValue;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 관찰할 무기 컨트롤러를 설정합니다.
    /// </summary>
    /// <param name="weaponController">새 무기 컨트롤러입니다.</param>
    public void SetWeaponController(WeaponController weaponController)
    {
        if (m_weaponController == weaponController)
        {
            return;
        }

        bool wasActive = isActiveAndEnabled;
        if (wasActive)
        {
            UnsubscribeWeapon();
        }

        m_weaponController = weaponController;

        if (wasActive)
        {
            SubscribeWeapon();
        }

        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 예비 탄약 수를 설정합니다.
    /// </summary>
    /// <param name="value">새 예비 탄약 수입니다.</param>
    public void SetReserveAmmo(int value)
    {
        int clampedValue = Mathf.Clamp(value, 0, m_maxReserveAmmo);

#if UNITY_EDITOR
        if (m_debugInfiniteReserveAmmo && clampedValue < m_reserveAmmo)
        {
            // 무한 탄창 디버그가 켜져 있으면 소모(감소) 호출은 무시합니다. 증가(예: 보급)는 그대로 반영됩니다.
            return;
        }
#endif

        if (m_reserveAmmo == clampedValue)
        {
            return;
        }

        m_reserveAmmo = clampedValue;
        NotifyPublicDataChanged();
    }

    /// <summary>
    /// 예비 탄약 수를 증감합니다.
    /// </summary>
    /// <param name="amount">더할 값입니다. 음수도 허용됩니다.</param>
    public void AddReserveAmmo(int amount)
    {
        SetReserveAmmo(m_reserveAmmo + amount);
    }

    /// <summary>
    /// 예비 탄약 최대치를 설정합니다.
    /// </summary>
    /// <param name="value">새 예비 탄약 최대치입니다.</param>
    public void SetMaxReserveAmmo(int value)
    {
        int nextMaxValue = Mathf.Max(0, value);
        int nextReserveAmmo = Mathf.Clamp(m_reserveAmmo, 0, nextMaxValue);
        if (m_maxReserveAmmo == nextMaxValue && m_reserveAmmo == nextReserveAmmo)
        {
            return;
        }

        m_maxReserveAmmo = nextMaxValue;
        m_reserveAmmo = nextReserveAmmo;
        NotifyPublicDataChanged();
    }

    private void CacheReferences()
    {
        if (m_health == null)
        {
            m_health = GetComponent<PlayerHealth>();
        }

        if (m_squadMember == null)
        {
            m_squadMember = GetComponent<SquadMemberController>();
        }

        if (m_interactionController == null)
        {
            m_interactionController = GetComponent<InteractionController>();
        }

        if (m_weaponController == null)
        {
            m_weaponController = GetComponentInChildren<WeaponController>(true);
        }

        if (m_publicTarget == null && m_squadMember != null)
        {
            m_publicTarget = m_squadMember.CameraTarget;
        }
    }

    private void SubscribeHealth()
    {
        if (m_health == null)
        {
            return;
        }

        m_health.OnHPChanged += HandleHpChanged;
        m_health.OnDown += NotifyPublicDataChanged;
        m_health.OnDeath += NotifyPublicDataChanged;
        m_health.OnRevive += NotifyPublicDataChanged;
        m_health.OnInjuryGaugeChanged += HandleInjuryGaugeChanged;
        m_health.OnInjuryStateChanged += HandleInjuryStateChanged;
    }

    private void UnsubscribeHealth()
    {
        if (m_health == null)
        {
            return;
        }

        m_health.OnHPChanged -= HandleHpChanged;
        m_health.OnDown -= NotifyPublicDataChanged;
        m_health.OnDeath -= NotifyPublicDataChanged;
        m_health.OnRevive -= NotifyPublicDataChanged;
        m_health.OnInjuryGaugeChanged -= HandleInjuryGaugeChanged;
        m_health.OnInjuryStateChanged -= HandleInjuryStateChanged;
    }

    /// <summary>스쿼드 조작 역할 변경을 공개 데이터 변경 이벤트로 전달하도록 구독합니다.</summary>
    private void SubscribeSquadMember()
    {
        if (m_squadMember != null)
        {
            m_squadMember.OnPlayerSquadMemberChanged += HandlePlayerSquadMemberChanged;
        }
    }

    /// <summary>스쿼드 조작 역할 변경 이벤트 구독을 해제합니다.</summary>
    private void UnsubscribeSquadMember()
    {
        if (m_squadMember != null)
        {
            m_squadMember.OnPlayerSquadMemberChanged -= HandlePlayerSquadMemberChanged;
        }
    }

    private void SubscribeWeapon()
    {
        if (m_weaponController == null)
        {
            return;
        }

        m_weaponController.OnBulletChanged += HandleWeaponBulletChanged;
    }

    private void UnsubscribeWeapon()
    {
        if (m_weaponController == null)
        {
            return;
        }

        m_weaponController.OnBulletChanged -= HandleWeaponBulletChanged;
    }

    private void HandleHpChanged(int currentHp, int maxHp)
    {
        NotifyPublicDataChanged();
    }

    private void HandleInjuryGaugeChanged(float currentGauge, float normalizedValue)
    {
        NotifyPublicDataChanged();
    }

    private void HandleInjuryStateChanged(PlayerInjuryState injuryState)
    {
        NotifyPublicDataChanged();
    }

    /// <summary>PlayerSquadMember 또는 AiSquadMember 역할 변경을 공개 상태 변경으로 전달합니다.</summary>
    private void HandlePlayerSquadMemberChanged(bool isPlayerSquadMember)
    {
        NotifyPublicDataChanged();
    }

    private void HandleWeaponBulletChanged(int currentBullet, int maxBullet)
    {
        NotifyPublicDataChanged();
    }

    private void NotifyPublicDataChanged()
    {
        OnPublicDataChanged?.Invoke();
    }

    private string ResolveCurrentWeaponName()
    {
        if (m_currentWeapon != null && !string.IsNullOrWhiteSpace(m_currentWeapon.weaponName))
        {
            return m_currentWeapon.weaponName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(m_currentWeaponFallbackName))
        {
            return m_currentWeaponFallbackName.Trim();
        }

        return m_weaponController != null ? m_weaponController.gameObject.name : string.Empty;
    }

    private string BuildCurrentWeaponShortInfo()
    {
        string weaponName = CurrentWeaponName;
        string ammoInfo = $"{CurrentMagazineAmmo}/{MagazineCapacity}, reserve {m_reserveAmmo}";
        return string.IsNullOrWhiteSpace(weaponName)
            ? ammoInfo
            : $"{weaponName} {ammoInfo}";
    }

    private void ClampValues()
    {
        m_reliability = Mathf.Clamp(m_reliability, 0, 100);
        m_maxReserveAmmo = Mathf.Max(0, m_maxReserveAmmo);
        m_reserveAmmo = Mathf.Clamp(m_reserveAmmo, 0, m_maxReserveAmmo);
    }
}
