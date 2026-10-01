using UnityEngine;
using VInspector;

/// <summary>Shotgun 탄창 전체를 일반 히트스캔 대신 용숨결 판정을 쓰는 특수탄으로 장전하는 청솔의 스킬입니다.</summary>
public sealed class ChungSolDragonBreathSkill : CharacterSkill
{
    [Tooltip("용숨결탄 전용 재장전 속도 배율입니다. 2이면 일반 재장전보다 2배 빠르게 모션과 장전이 완료됩니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_reloadSpeedMultiplier = 1.0f;

    [Foldout("Debug")]
    [Tooltip("켜면 용숨결탄 사격 시 특수탄 잔량과 실제 샷건 장탄이 줄지 않습니다. 개발 모드에서만 효과가 있습니다.")]
    [SerializeField] private bool m_debugInfiniteDragonBreathMagazine;

    private Gun m_gun;
    private AimController m_aimController;
    private DragonBreathEffect m_effectInstance;
    private bool m_isLoadingSpecialAmmo;
    private int m_specialRoundsRemaining;

    public override CharacterSkillType SkillType => CharacterSkillType.ChungSolDragonBreath;
    public override string DisplayName => "용숨결탄";
    public override string Description => "재장전 모션 후 Shotgun 탄창 전체를 전방 범위 피해 특수탄으로 채웁니다.";
    public override bool IsActive => m_isLoadingSpecialAmmo || m_specialRoundsRemaining > 0;

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
        Gun resolved = GetComponentInChildren<Gun>(true);
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

        // Shotgun/FirePos 아래에 미리 넣어 둔 비활성 프리팹 인스턴스 하나를 풀처럼 계속 재사용합니다.
        // 런타임 Instantiate를 하지 않아 프리팹 모드에서 범위·피해·화상 설정을 바로 찾고 조정할 수 있습니다.
        m_effectInstance = m_gun.GetComponentInChildren<DragonBreathEffect>(true);
        if (m_effectInstance == null)
        {
            Debug.LogError("[ChungSolDragonBreathSkill] Shotgun 자식에서 DragonBreathEffect를 찾지 못했습니다.", this);
            return null;
        }

        m_effectInstance.gameObject.SetActive(false);
        return m_effectInstance;
    }
}
