using System;
using UnityEngine;
using VInspector;

public enum PlayerInjuryState
{
    Normal,
    Minor,
    Serious,
    Critical
}

public class PlayerHealth : HealthSystemBase
{
    [Foldout("Injury Options")]
    [Tooltip("경상으로 판정되는 누적 데미지 기준입니다.")]
    [SerializeField] private int m_minorInjuryDamage = 20;

    [Tooltip("중상으로 판정되는 누적 데미지 기준입니다.")]
    [SerializeField] private int m_seriousInjuryDamage = 50;

    [Tooltip("치명상으로 판정되는 누적 데미지 기준입니다.")]
    [SerializeField] private int m_criticalInjuryDamage = 80;

    [Tooltip("현재까지 실제 HP에서 차감된 누적 데미지입니다.")]
    [SerializeField] private int m_accumulatedDamage;

    [Foldout("Down Options")]
    [Tooltip("Seconds before a downed player becomes combat-out if not revived.")]
    [SerializeField] private float m_downDuration = 30.0f;

    [Tooltip("After this many successful revives, the next HP-0 event becomes combat-out immediately.")]
    [SerializeField] private int m_maxReviveCount = 3;

    [Tooltip("Max HP percent restored by the first revive in the current sortie.")]
    [Range(1, 100)]
    [SerializeField] private int m_firstReviveHpPercent = 50;

    [Tooltip("Max HP percent restored by the second revive in the current sortie.")]
    [Range(1, 100)]
    [SerializeField] private int m_secondReviveHpPercent = 25;

    [Tooltip("Max HP percent restored by the third revive in the current sortie.")]
    [Range(1, 100)]
    [SerializeField] private int m_thirdReviveHpPercent = 10;

    private PlayerInjuryState m_currentInjuryState = PlayerInjuryState.Normal;
    private bool m_isDowned;
    private bool m_downTimerPaused;
    private float m_downTimeRemaining;
    private int m_reviveCount;

    /// <summary>현재까지 누적된 실제 피해량입니다.</summary>
    public int AccumulatedDamage => m_accumulatedDamage;

    /// <summary>현재 누적 데미지 기준 부상 상태입니다.</summary>
    public PlayerInjuryState CurrentInjuryState => m_currentInjuryState;

    /// <summary>누적 데미지를 최대 HP 기준 0~1 범위로 환산한 부상 게이지 값입니다.</summary>
    public float InjuryGaugeNormalized => m_maxHp > 0
        ? Mathf.Clamp01((float)m_accumulatedDamage / m_maxHp)
        : 0.0f;

    public bool IsDowned => m_isDowned;

    public bool IsDownTimerPaused => m_downTimerPaused;

    public float DownDuration => Mathf.Max(0.0f, m_downDuration);

    public float DownTimeRemaining => Mathf.Max(0.0f, m_downTimeRemaining);

    public float DownTimeNormalized => m_downDuration > 0.0f
        ? Mathf.Clamp01(m_downTimeRemaining / m_downDuration)
        : 0.0f;

    public int ReviveCount => m_reviveCount;

    public int MaxReviveCount => Mathf.Max(1, m_maxReviveCount);

    /// <summary>부상 게이지가 변경될 때 발생합니다. 인자는 누적 데미지와 정규화된 게이지 값입니다.</summary>
    public event Action<int, float> OnInjuryGaugeChanged;

    /// <summary>누적 데미지 기준 부상 상태가 변경될 때 발생합니다.</summary>
    public event Action<PlayerInjuryState> OnInjuryStateChanged;

    /// <summary>HP가 0에 도달해 다운(빈사) 상태로 진입해야 할 때 발생합니다.</summary>
    public event Action OnDown;

    public event Action<float, float> OnDownTimerChanged;

    public event Action<int, int> OnReviveCountChanged;

#if UNITY_EDITOR
    protected new void OnValidate()
    {
        base.OnValidate();

        m_minorInjuryDamage = Mathf.Max(0, m_minorInjuryDamage);
        m_seriousInjuryDamage = Mathf.Max(m_minorInjuryDamage, m_seriousInjuryDamage);
        m_criticalInjuryDamage = Mathf.Max(m_seriousInjuryDamage, m_criticalInjuryDamage);
        m_accumulatedDamage = Mathf.Max(0, m_accumulatedDamage);
        m_downDuration = Mathf.Max(0.0f, m_downDuration);
        m_maxReviveCount = Mathf.Max(1, m_maxReviveCount);
        m_firstReviveHpPercent = Mathf.Clamp(m_firstReviveHpPercent, 1, 100);
        m_secondReviveHpPercent = Mathf.Clamp(m_secondReviveHpPercent, 1, 100);
        m_thirdReviveHpPercent = Mathf.Clamp(m_thirdReviveHpPercent, 1, 100);
        m_currentInjuryState = GetInjuryState(m_accumulatedDamage);
    }
#endif

    private void Update()
    {
        if (!m_isDowned || m_isDead || m_downTimerPaused)
        {
            return;
        }

        m_downTimeRemaining = Mathf.Max(0.0f, m_downTimeRemaining - Time.deltaTime);
        NotifyDownTimerChanged();

        if (m_downTimeRemaining <= 0.0f)
        {
            ExpireDownTimer();
        }
    }

    public override void InitializeHealth()
    {
        base.InitializeHealth();
        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        m_reviveCount = 0;
        ResetInjuryDamage();
        NotifyDownTimerChanged();
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);
    }

    /// <summary>
    /// 플레이어는 HP가 0에 도달해도 즉시 사망하지 않고 다운(빈사) 상태로 진입합니다.
    /// </summary>
    /// <remarks>
    /// 사망(<see cref="HealthSystemBase.Death"/>)은 구조 실패 등 특수 조건에서만 별도로 발동합니다.
    /// 따라서 여기서는 사망 플래그를 세우지 않고 다운 진입 이벤트만 발신합니다.
    /// </remarks>
    protected override void OnHpDepleted()
    {
        if (m_isDowned)
        {
            return;
        }

        if (m_reviveCount >= Mathf.Max(1, m_maxReviveCount))
        {
            Death();
            return;
        }

        m_isDowned = true;
        m_downTimerPaused = false;
        m_downTimeRemaining = DownDuration;
        OnDown?.Invoke();
        NotifyDownTimerChanged();

        if (m_downTimeRemaining <= 0.0f)
        {
            ExpireDownTimer();
        }
    }

    public override bool TakeDamage(int damage)
    {
        int previousHp = m_currentHp;

        if (!base.TakeDamage(damage))
        {
            return false;
        }

        int actualDamage = Mathf.Max(previousHp - m_currentHp, 0);
        AddInjuryDamage(actualDamage);

        return true;
    }

    /// <summary>
    /// 다운 상태에서 지정한 HP로 전투에 복귀시킵니다.
    /// </summary>
    /// <remarks>
    /// 플레이어 다운은 사망 플래그를 세우지 않는 HP 0 상태이므로,
    /// 기본 <see cref="HealthSystemBase.Revive"/> 대신 이 경로를 사용합니다.
    /// </remarks>
    public bool ReviveFromDown(int amount)
    {
        if (m_isDead || m_currentHp > 0 || !m_isDowned)
        {
            return false;
        }

        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        m_reviveCount = Mathf.Min(m_reviveCount + 1, MaxReviveCount);
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);
        NotifyDownTimerChanged();

        return ReviveToHp(amount);
    }

    public bool ReviveFromDown()
    {
        return ReviveFromDown(CalculateNextReviveHp());
    }

    public void SetDownTimerPaused(bool value)
    {
        if (!m_isDowned || m_isDead)
        {
            m_downTimerPaused = false;
            return;
        }

        m_downTimerPaused = value;
    }

    public int CalculateNextReviveHp()
    {
        int percent = GetReviveHpPercent(m_reviveCount + 1);
        return Mathf.Clamp(Mathf.CeilToInt(m_maxHp * (percent / 100.0f)), 1, m_maxHp);
    }

    public override void Death()
    {
        bool wasDowned = m_isDowned;

        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;

        if (wasDowned)
        {
            NotifyDownTimerChanged();
        }

        base.Death();
    }

    private void ExpireDownTimer()
    {
        if (!m_isDowned || m_isDead)
        {
            return;
        }

        Death();
    }

    private void NotifyDownTimerChanged()
    {
        OnDownTimerChanged?.Invoke(DownTimeRemaining, DownDuration);
    }

    private int GetReviveHpPercent(int reviveNumber)
    {
        return reviveNumber switch
        {
            1 => m_firstReviveHpPercent,
            2 => m_secondReviveHpPercent,
            _ => m_thirdReviveHpPercent,
        };
    }

    /// <summary>
    /// 전달된 데미지 값 기준의 부상 상태를 반환합니다.
    /// </summary>
    public PlayerInjuryState GetInjuryState(int damage)
    {
        damage = Mathf.Max(0, damage);

        if (damage >= m_criticalInjuryDamage)
        {
            return PlayerInjuryState.Critical;
        }

        if (damage >= m_seriousInjuryDamage)
        {
            return PlayerInjuryState.Serious;
        }

        if (damage >= m_minorInjuryDamage)
        {
            return PlayerInjuryState.Minor;
        }

        return PlayerInjuryState.Normal;
    }

    public void ResetInjuryDamage()
    {
        m_accumulatedDamage = 0;
        UpdateInjuryStateAndNotify();
    }

    private void AddInjuryDamage(int damage)
    {
        damage = Mathf.Max(0, damage);
        if (damage <= 0)
        {
            return;
        }

        m_accumulatedDamage += damage;
        UpdateInjuryStateAndNotify();
    }

    private void UpdateInjuryStateAndNotify()
    {
        PlayerInjuryState previousState = m_currentInjuryState;
        m_currentInjuryState = GetInjuryState(m_accumulatedDamage);

        OnInjuryGaugeChanged?.Invoke(m_accumulatedDamage, InjuryGaugeNormalized);

        if (m_currentInjuryState != previousState)
        {
            OnInjuryStateChanged?.Invoke(m_currentInjuryState);
        }
    }
}
