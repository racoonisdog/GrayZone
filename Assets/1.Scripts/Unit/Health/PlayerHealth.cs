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

    private PlayerInjuryState m_currentInjuryState = PlayerInjuryState.Normal;

    /// <summary>현재까지 누적된 실제 피해량입니다.</summary>
    public int AccumulatedDamage => m_accumulatedDamage;

    /// <summary>현재 누적 데미지 기준 부상 상태입니다.</summary>
    public PlayerInjuryState CurrentInjuryState => m_currentInjuryState;

    /// <summary>누적 데미지를 최대 HP 기준 0~1 범위로 환산한 부상 게이지 값입니다.</summary>
    public float InjuryGaugeNormalized => m_maxHp > 0
        ? Mathf.Clamp01((float)m_accumulatedDamage / m_maxHp)
        : 0.0f;

    /// <summary>부상 게이지가 변경될 때 발생합니다. 인자는 누적 데미지와 정규화된 게이지 값입니다.</summary>
    public event Action<int, float> OnInjuryGaugeChanged;

    /// <summary>누적 데미지 기준 부상 상태가 변경될 때 발생합니다.</summary>
    public event Action<PlayerInjuryState> OnInjuryStateChanged;

#if UNITY_EDITOR
    protected new void OnValidate()
    {
        base.OnValidate();

        m_minorInjuryDamage = Mathf.Max(0, m_minorInjuryDamage);
        m_seriousInjuryDamage = Mathf.Max(m_minorInjuryDamage, m_seriousInjuryDamage);
        m_criticalInjuryDamage = Mathf.Max(m_seriousInjuryDamage, m_criticalInjuryDamage);
        m_accumulatedDamage = Mathf.Max(0, m_accumulatedDamage);
        m_currentInjuryState = GetInjuryState(m_accumulatedDamage);
    }
#endif

    public override void InitializeHealth()
    {
        base.InitializeHealth();
        ResetInjuryDamage();
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
