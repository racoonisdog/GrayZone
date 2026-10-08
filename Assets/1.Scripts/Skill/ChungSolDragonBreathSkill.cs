using UnityEngine;
using VInspector;

/// <summary>Shotgun 탄창 전체를 일반 히트스캔 대신 용숨결 판정을 쓰는 특수탄으로 장전하는 청솔의 스킬입니다.</summary>
public sealed class ChungSolDragonBreathSkill : CharacterSkill
{
    private const string DragonBreathEffectResourcePath = "Skill/DragonBreathEffect";

    [Tooltip("이 스킬의 밸런스 수치 SO입니다(쿨타임, 재장전 배율, 용숨결 피해·판정 범위). 비어 있으면 인스펙터 값과 " +
             "용숨결 효과 프리팹 값을 그대로 씁니다. 같은 SO를 용숨결 효과 인스턴스에도 주입합니다.")]
    [SerializeField] private ChungSolDragonBreathSkillSO m_balanceSO;

    [Tooltip("용숨결탄 전용 재장전 속도 배율입니다. 2이면 일반 재장전보다 2배 빠르게 모션과 장전이 완료됩니다. " +
             "밸런스 SO가 있으면 SO 값으로 덮입니다.")]
    [Min(0.01f)]
    [BalanceField]
    [SerializeField] private float m_reloadSpeedMultiplier = 1.0f;

    [Foldout("Debug")]
    [Tooltip("켜면 용숨결탄 사격 시 특수탄 잔량과 실제 샷건 장탄이 줄지 않습니다. 개발 모드에서만 효과가 있습니다.")]
    [SerializeField] private bool m_debugInfiniteDragonBreathMagazine;

    private Gun m_gun;
    private AimController m_aimController;
    private DragonBreathEffect m_effectInstance;
    private bool m_isLoadingSpecialAmmo;
    private int m_specialRoundsRemaining;

    protected override ScriptableObject BalanceSource => m_balanceSO;

    public override CharacterSkillType SkillType => CharacterSkillType.ChungSolDragonBreath;
    public override string DisplayName => "화염 탄환";
    public override string Description => "강력한 화염 탄환을 장전합니다.";
    public override bool IsActive => m_isLoadingSpecialAmmo || m_specialRoundsRemaining > 0;

    /// <summary>장전 중이거나 특수탄이 남아 있을 때만 상태를 보여줍니다. 평소에는 Figma처럼 쿨타임 한 줄만 둡니다.</summary>
    public override string HudDetailText => m_isLoadingSpecialAmmo
        ? "특수탄 장전 중"
        : m_specialRoundsRemaining > 0
            ? $"화염 탄환 {m_specialRoundsRemaining} / {SpecialRoundsCapacity}"
            : null;

    public bool IsLoadingSpecialAmmo => m_isLoadingSpecialAmmo;
    public int SpecialRoundsRemaining => m_specialRoundsRemaining;
    public int SpecialRoundsCapacity => m_gun != null ? m_gun.MaxBullet : 0;
    /// <summary>현재 발사 순서의 맨 앞 탄환이 용숨결탄인지 여부입니다.</summary>
    public bool IsCurrentRoundDragonBreath => m_specialRoundsRemaining > 0;
    public float ReloadSpeedMultiplier => Mathf.Max(0.01f, m_reloadSpeedMultiplier);
    public bool DebugInfiniteDragonBreathMagazine
    {
        get => m_debugInfiniteDragonBreathMagazine;
        set => m_debugInfiniteDragonBreathMagazine = value;
    }

    private bool IsInfiniteDragonBreathMagazineDebugActive =>
        m_debugInfiniteDragonBreathMagazine && GameDevMode.DebugFeaturesEnabled;

    protected override void Awake()
    {
        base.Awake();
        ResolveGun();
        ResolveEffectInstance();
    }

    private void OnEnable()
    {
        ResolveGun();
        SubscribeGun();
    }

    private void OnDisable()
    {
        UnsubscribeGun();

        if (m_isLoadingSpecialAmmo && m_gun != null)
        {
            m_gun.CancelReload();
        }

        m_isLoadingSpecialAmmo = false;
        m_specialRoundsRemaining = 0;
    }

    protected override bool ActivateSkill(SquadManager squadManager)
    {
        ResolveGun();
        if (m_gun == null || m_gun.WeaponType != WeaponType.Shotgun || m_aimController == null)
        {
            return false;
        }

        m_gun.CancelReload();
        m_specialRoundsRemaining = 0;
        m_isLoadingSpecialAmmo = true;

        if (m_aimController.BeginSkillReload(ReloadSpeedMultiplier))
        {
            return true;
        }

        m_isLoadingSpecialAmmo = false;
        return false;
    }

    private void ResolveGun()
    {
        // 활성 총을 먼저 찾습니다. 모델을 바꾸면 옛 모델 아래에 쓰지 않는 총이 비활성으로 남아, 비활성까지 한 번에 찾으면
        // 그 총을 구독해 특수탄 발사와 장전 완료를 놓칩니다(PlayerbleUnitData.FindOwnedGun과 같은 규칙).
        Gun active = GetComponentInChildren<Gun>(false);
        Gun resolved = active != null ? active : GetComponentInChildren<Gun>(true);
        if (resolved == m_gun)
        {
            return;
        }

        UnsubscribeGun();
        m_gun = resolved;
        m_aimController = GetComponent<AimController>();
        SubscribeGun();
    }

    private void SubscribeGun()
    {
        if (m_gun != null)
        {
            m_gun.OnReloadCompleted -= HandleReloadCompleted;
            m_gun.OnReloadCompleted += HandleReloadCompleted;
            m_gun.OnTryOverrideShot -= TryHandleDragonBreathShot;
            m_gun.OnTryOverrideShot += TryHandleDragonBreathShot;
        }
    }

    private void UnsubscribeGun()
    {
        if (m_gun != null)
        {
            m_gun.OnReloadCompleted -= HandleReloadCompleted;
            m_gun.OnTryOverrideShot -= TryHandleDragonBreathShot;
        }
    }

    private void HandleReloadCompleted()
    {
        if (!m_isLoadingSpecialAmmo || m_gun == null)
        {
            return;
        }

        m_isLoadingSpecialAmmo = false;
        m_specialRoundsRemaining = m_gun.CurrentBullet;
        NotifyStateChanged();
    }

    private bool TryHandleDragonBreathShot(Gun.HitscanShotInfo shot)
    {
        if (m_specialRoundsRemaining <= 0 || !shot.IsValid || shot.Direction.sqrMagnitude <= 0.0f)
        {
            return false;
        }

        DragonBreathEffect effect = ResolveEffectInstance();
        if (effect == null)
        {
            return false;
        }

        if (IsInfiniteDragonBreathMagazineDebugActive)
        {
            // Gun은 특수탄 대체 콜백 전에 실제 장탄을 먼저 1발 줄입니다.
            // 일반 무한 장탄 디버그가 꺼져 있을 때만 그 1발을 복구해 두 카운터를 함께 유지합니다.
            if (m_gun != null && !m_gun.DebugInfiniteMagazine)
            {
                m_gun.SetCurrentBullet(m_gun.CurrentBullet + 1);
            }
        }
        else
        {
            m_specialRoundsRemaining--;
        }

        effect.Play(shot.Origin, shot.Direction.normalized, Faction.Player, gameObject);
        NotifyStateChanged();
        return true;
    }

    private DragonBreathEffect ResolveEffectInstance()
    {
        if (m_effectInstance != null)
        {
            return m_effectInstance;
        }

        if (m_gun == null)
        {
            return null;
        }

        // 기존 무기 프리팹에 효과가 포함되어 있으면 그 인스턴스를 우선 재사용합니다.
        m_effectInstance = m_gun.GetComponentInChildren<DragonBreathEffect>(true);
        if (m_effectInstance == null)
        {
            DragonBreathEffect effectPrefab = Resources.Load<DragonBreathEffect>(DragonBreathEffectResourcePath);
            if (effectPrefab == null)
            {
                Debug.LogError(
                    $"[ChungSolDragonBreathSkill] Resources/{DragonBreathEffectResourcePath} 프리팹을 찾지 못했습니다.",
                    this);
                return null;
            }

            // 장착 무기가 바뀌어도 같은 효과 인스턴스를 계속 쓸 수 있도록 캐릭터 아래에 생성합니다.
            m_effectInstance = Instantiate(effectPrefab, transform);
            m_effectInstance.name = effectPrefab.name;
        }

        // 용숨결 피해·판정 범위도 청솔 스킬 SO가 정합니다. 효과 프리팹 값은 SO가 없을 때의 기본값입니다.
        BindBalance(m_effectInstance);

        m_effectInstance.gameObject.SetActive(false);
        return m_effectInstance;
    }
}
