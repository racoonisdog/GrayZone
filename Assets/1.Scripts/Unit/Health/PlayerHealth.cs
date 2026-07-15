using System;
using UnityEngine;
using UnityEngine.Serialization;
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
    [Tooltip("현재 전투 중 누적된 부상 게이지입니다. 전투 중에는 float로 유지하고 결과 판정 시 반올림합니다.")]
    [FormerlySerializedAs("m_accumulatedDamage")]
    [ReadOnly][SerializeField] private float m_currentInjuryGauge;

    [Tooltip("현재 반올림 부상 게이지 기준으로 판정된 부상 상태입니다.")]
    [ReadOnly][SerializeField] private PlayerInjuryState m_currentInjuryState = PlayerInjuryState.Normal;

    [Tooltip("부상 게이지 최대값입니다. 1차 프로토타입 기준값은 100입니다.")]
    [SerializeField] private float m_maxInjuryGauge = 100.0f;

    [Tooltip("실제 HP 피해량을 부상 게이지로 변환할 때 곱하는 비율입니다. 기획 공식의 r 값입니다.")]
    [SerializeField] private float m_injuryConversionRatio = 0.35f;

    [Tooltip("정상 상태로 판정되는 반올림 부상 게이지 최대값입니다. 1차 프로토타입 기준 0~10입니다.")]
    [SerializeField] private int m_normalInjuryMaxGauge = 10;

    [Tooltip("경상 상태로 판정되는 반올림 부상 게이지 최대값입니다. 1차 프로토타입 기준 11~40입니다.")]
    [SerializeField] private int m_minorInjuryMaxGauge = 40;

    [Tooltip("치명상 상태로 판정되기 시작하는 반올림 부상 게이지 최소값입니다. 1차 프로토타입 기준 71입니다.")]
    [SerializeField] private int m_criticalInjuryMinGauge = 71;

    [Tooltip("아직 구조된 적이 없을 때 적용되는 부상 게이지 배율입니다.")]
    [SerializeField] private float m_baseInjuryMultiplier = 1.0f;

    [Tooltip("첫 번째 구조 이후 적용되는 부상 게이지 배율입니다.")]
    [SerializeField] private float m_revivedOnceInjuryMultiplier = 1.2f;

    [Tooltip("두 번째 구조 이후 적용되는 부상 게이지 배율입니다.")]
    [SerializeField] private float m_revivedTwiceInjuryMultiplier = 1.5f;

    [Tooltip("세 번째 구조 이후 적용되는 부상 게이지 배율입니다.")]
    [SerializeField] private float m_revivedThreeTimesInjuryMultiplier = 1.8f;
    [Foldout("Down Options")]
    [Tooltip("다운된 플레이어가 구조되지 않았을 때 전투 이탈 처리되기까지 걸리는 시간입니다.")]
    [SerializeField] private float m_downDuration = 30.0f;

    [Tooltip("이 횟수만큼 구조된 뒤 다시 HP가 0이 되면 즉시 전투 이탈 처리됩니다.")]
    [SerializeField] private int m_maxReviveCount = 3;

    [Tooltip("현재 출격에서 첫 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다.")]
    [Range(1, 100)]
    [SerializeField] private int m_firstReviveHpPercent = 50;

    [Tooltip("현재 출격에서 두 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다.")]
    [Range(1, 100)]
    [SerializeField] private int m_secondReviveHpPercent = 25;

    [Tooltip("현재 출격에서 세 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다.")]
    [Range(1, 100)]
    [SerializeField] private int m_thirdReviveHpPercent = 10;

    private bool m_isDowned;
    private bool m_downTimerPaused;
    private float m_downTimeRemaining;
    private int m_reviveCount;
    private bool m_isDeathProcessing;

    /// <summary>현재 전투 중 누적된 부상 게이지입니다.</summary>
    public float CurrentInjuryGauge => m_currentInjuryGauge;

    /// <summary>결과 판정에 사용하는 반올림 부상 게이지입니다.</summary>
    public int RoundedInjuryGauge => Mathf.RoundToInt(m_currentInjuryGauge);

    /// <summary>현재까지 누적된 부상 게이지입니다. 기존 호출부 호환을 위해 반올림 값을 반환합니다.</summary>
    public int AccumulatedDamage => RoundedInjuryGauge;

    /// <summary>부상 게이지 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, m_maxInjuryGauge);

    /// <summary>치명상 판정이 시작되는 반올림 부상 게이지 값입니다.</summary>
    public int CriticalInjuryMinGauge => Mathf.Clamp(m_criticalInjuryMinGauge, 0, Mathf.RoundToInt(MaxInjuryGauge));

    /// <summary>현재 부상 게이지 기준 부상 상태입니다.</summary>
    public PlayerInjuryState CurrentInjuryState => m_currentInjuryState;

    /// <summary>현재 부상 게이지를 최대 부상 게이지 기준 0~1 범위로 환산한 값입니다.</summary>
    public float InjuryGaugeNormalized => Mathf.Clamp01(m_currentInjuryGauge / MaxInjuryGauge);

    public bool IsDowned => m_isDowned;

    public bool IsDownTimerPaused => m_downTimerPaused;

    public float DownDuration => Mathf.Max(0.0f, m_downDuration);

    public float DownTimeRemaining => Mathf.Max(0.0f, m_downTimeRemaining);

    public float DownTimeNormalized => m_downDuration > 0.0f
        ? Mathf.Clamp01(m_downTimeRemaining / m_downDuration)
        : 0.0f;

    public int ReviveCount => m_reviveCount;

    public int MaxReviveCount => Mathf.Max(1, m_maxReviveCount);

    /// <summary>부상 게이지가 변경될 때 발생합니다. 인자는 현재 부상 게이지와 정규화된 게이지 값입니다.</summary>
    public event Action<float, float> OnInjuryGaugeChanged;

    /// <summary>부상 게이지 기준 부상 상태가 변경될 때 발생합니다.</summary>
    public event Action<PlayerInjuryState> OnInjuryStateChanged;

    /// <summary>HP가 0에 도달해 다운(빈사) 상태로 진입해야 할 때 발생합니다.</summary>
    public event Action OnDown;

    public event Action<float, float> OnDownTimerChanged;

    public event Action<int, int> OnReviveCountChanged;

#if UNITY_EDITOR
    protected new void OnValidate()
    {
        base.OnValidate();

        m_maxInjuryGauge = Mathf.Max(1.0f, m_maxInjuryGauge);
        m_injuryConversionRatio = Mathf.Max(0.0f, m_injuryConversionRatio);
        int maxRoundedGauge = Mathf.Max(1, Mathf.RoundToInt(m_maxInjuryGauge));
        m_normalInjuryMaxGauge = Mathf.Clamp(m_normalInjuryMaxGauge, 0, maxRoundedGauge);
        m_minorInjuryMaxGauge = Mathf.Clamp(m_minorInjuryMaxGauge, m_normalInjuryMaxGauge, maxRoundedGauge);
        int criticalMinLowerBound = Mathf.Min(m_minorInjuryMaxGauge + 1, maxRoundedGauge);
        m_criticalInjuryMinGauge = Mathf.Clamp(m_criticalInjuryMinGauge, criticalMinLowerBound, maxRoundedGauge);
        m_baseInjuryMultiplier = Mathf.Max(0.0f, m_baseInjuryMultiplier);
        m_revivedOnceInjuryMultiplier = Mathf.Max(0.0f, m_revivedOnceInjuryMultiplier);
        m_revivedTwiceInjuryMultiplier = Mathf.Max(0.0f, m_revivedTwiceInjuryMultiplier);
        m_revivedThreeTimesInjuryMultiplier = Mathf.Max(0.0f, m_revivedThreeTimesInjuryMultiplier);
        m_currentInjuryGauge = Mathf.Clamp(m_currentInjuryGauge, 0.0f, MaxInjuryGauge);
        m_downDuration = Mathf.Max(0.0f, m_downDuration);
        m_maxReviveCount = Mathf.Max(1, m_maxReviveCount);
        m_firstReviveHpPercent = Mathf.Clamp(m_firstReviveHpPercent, 1, 100);
        m_secondReviveHpPercent = Mathf.Clamp(m_secondReviveHpPercent, 1, 100);
        m_thirdReviveHpPercent = Mathf.Clamp(m_thirdReviveHpPercent, 1, 100);
        m_currentInjuryState = GetInjuryState(RoundedInjuryGauge);
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
        m_isDeathProcessing = false;
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
        if (m_isDead || m_isDowned)
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

    protected override void OnDamageApplied(int actualDamage, int previousHp)
    {
        AddInjuryGaugeFromDamage(actualDamage);
    }

    public override bool TakeDamage(int damage)
    {
#if UNITY_EDITOR
        if (m_debugInfiniteHealth)
        {
            return false;
        }
#endif
        return base.TakeDamage(damage);
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

        if (m_reviveCount >= MaxReviveCount)
        {
            // 부활 횟수를 이미 다 쓴 상태입니다. 구조를 시도해도 되살리지 않고 즉시 전투 이탈(사망) 처리합니다.
            m_isDowned = false;
            m_downTimerPaused = false;
            m_downTimeRemaining = 0.0f;
            NotifyDownTimerChanged();
            Death();
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
        if (m_isDead || m_isDeathProcessing)
        {
            return;
        }

        m_isDeathProcessing = true;

        try
        {
            SetCurrentInjuryGauge(Mathf.Max(m_currentInjuryGauge, CriticalInjuryMinGauge));

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
        finally
        {
            m_isDeathProcessing = false;
        }
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
    /// 전달된 반올림 부상 게이지 기준의 부상 상태를 반환합니다.
    /// </summary>
    public PlayerInjuryState GetInjuryState(int roundedGauge)
    {
        roundedGauge = Mathf.Clamp(roundedGauge, 0, Mathf.RoundToInt(MaxInjuryGauge));

        if (roundedGauge >= CriticalInjuryMinGauge)
        {
            return PlayerInjuryState.Critical;
        }

        if (roundedGauge > m_minorInjuryMaxGauge)
        {
            return PlayerInjuryState.Serious;
        }

        if (roundedGauge > m_normalInjuryMaxGauge)
        {
            return PlayerInjuryState.Minor;
        }

        return PlayerInjuryState.Normal;
    }

    /// <summary>공용 캐릭터 스냅샷의 최대 HP, 현재 HP와 누적 부상 게이지를 전투 체력 상태에 적용합니다.</summary>
    /// <remarks>씬 입장 초기화 경로에서 사용하며 구조 횟수와 다운 타이머는 새 출격의 기본값으로 초기화합니다.</remarks>
    public void ApplySnapshotState(int currentHp, int maxHp, float injurySeverityGauge, float maxInjuryGauge)
    {
        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        m_reviveCount = 0;
        m_maxInjuryGauge = Mathf.Max(1.0f, maxInjuryGauge);
        SetMaxHP(maxHp);
        SetCurrentHP(currentHp);
        m_currentInjuryGauge = Mathf.Clamp(injurySeverityGauge, 0.0f, MaxInjuryGauge);
        UpdateInjuryStateAndNotify();
        NotifyDownTimerChanged();
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);
    }

    /// <summary>현재 출격에서 누적된 부상 게이지를 0으로 초기화합니다.</summary>
    public void ResetInjuryDamage()
    {
        SetCurrentInjuryGauge(0.0f);
    }

    /// <summary>현재 출격에서 누적된 부상 게이지를 0으로 초기화합니다.</summary>
    public void ResetInjuryGauge()
    {
        SetCurrentInjuryGauge(0.0f);
    }

    private void AddInjuryGaugeFromDamage(int actualDamage)
    {
        actualDamage = Mathf.Max(0, actualDamage);
        if (actualDamage <= 0 || m_maxHp <= 0)
        {
            return;
        }

        float healthDamageRatio = actualDamage / (float)m_maxHp;
        float deltaGauge = healthDamageRatio
            * MaxInjuryGauge
            * m_injuryConversionRatio
            * GetCurrentInjuryMultiplier();

        if (deltaGauge <= 0.0f)
        {
            return;
        }

        PlayerInjuryState previousState = m_currentInjuryState;
        float previousGauge = m_currentInjuryGauge;

        SetCurrentInjuryGauge(m_currentInjuryGauge + deltaGauge);
        LogInjuryStateChangeFromDamage(actualDamage, deltaGauge, previousGauge, previousState);
    }

    private float GetCurrentInjuryMultiplier()
    {
        return m_reviveCount switch
        {
            <= 0 => m_baseInjuryMultiplier,
            1 => m_revivedOnceInjuryMultiplier,
            2 => m_revivedTwiceInjuryMultiplier,
            _ => m_revivedThreeTimesInjuryMultiplier,
        };
    }

    private void SetCurrentInjuryGauge(float value)
    {
        float clampedValue = Mathf.Clamp(value, 0.0f, MaxInjuryGauge);
        if (Mathf.Approximately(m_currentInjuryGauge, clampedValue))
        {
            return;
        }

        m_currentInjuryGauge = clampedValue;
        UpdateInjuryStateAndNotify();
    }

    private void UpdateInjuryStateAndNotify()
    {
        PlayerInjuryState previousState = m_currentInjuryState;
        m_currentInjuryState = GetInjuryState(RoundedInjuryGauge);

        OnInjuryGaugeChanged?.Invoke(m_currentInjuryGauge, InjuryGaugeNormalized);

        if (m_currentInjuryState != previousState)
        {
            OnInjuryStateChanged?.Invoke(m_currentInjuryState);
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogInjuryStateChangeFromDamage(
        int actualDamage,
        float deltaGauge,
        float previousGauge,
        PlayerInjuryState previousState)
    {
#if UNITY_EDITOR
        if (!m_debugLogInjuryStateChange || m_currentInjuryState == previousState)
        {
            return;
        }

        Debug.Log(
            $"[PlayerHealth] Injury state changed by damage. {previousState} -> {m_currentInjuryState}, " +
            $"damage={actualDamage}, gauge={previousGauge:0.##}->{m_currentInjuryGauge:0.##}, " +
            $"delta={deltaGauge:0.##}, rounded={RoundedInjuryGauge}, reviveCount={m_reviveCount}",
            this);
#endif
    }

#if UNITY_EDITOR
    [Foldout("Debug")]
    [Tooltip("켜면 Editor에서 피해/사망 로그를 출력합니다. Player 빌드에서는 호출 자체가 제거됩니다.")]
    [SerializeField] private bool m_debugLogHealth = false;

    [Foldout("Debug")]
    [Tooltip("켜면 피격으로 부상 상태가 바뀔 때 Editor 로그를 출력합니다. Player 빌드에서는 호출 자체가 제거됩니다.")]
    [SerializeField] private bool m_debugLogInjuryStateChange = false;

    [Foldout("Debug")]
    [Tooltip("켜면 피해를 전혀 받지 않습니다(무한 체력). Player 빌드에서는 항상 꺼진 것으로 취급됩니다.")]
    [SerializeField] private bool m_debugInfiniteHealth = false;

    protected override bool DebugLogHealthEnabled => m_debugLogHealth;

    [Foldout("Debug")]
    [Button("즉시 기절시키기")]
    public void Debug_InstantDown()
    {
        if (m_isDead || m_isDowned)
        {
            return;
        }

        // OnHpDepleted의 다운 진입 분기와 동일한 처리입니다(부활 횟수 소진 판정은 디버그 목적상 건너뜁니다).
        m_currentHp = 0;
        m_isDowned = true;
        m_downTimerPaused = false;
        m_downTimeRemaining = DownDuration;
        NotifyHPChanged();
        OnDown?.Invoke();
        NotifyDownTimerChanged();
    }

    [Foldout("Debug")]
    [Button("즉시 전투 이탈")]
    public void Debug_InstantCombatOut()
    {
        // 실제 사망 경로(Death)를 그대로 재사용해 OnDeath 구독자(조작 전환, 애니메이터 등)가 정상 동작합니다.
        Death();
    }

    [Foldout("Debug")]
    [Button("즉시 살리기")]
    public void Debug_InstantRevive()
    {
        // 디버그 전용: 사망(전투 이탈)·부활 횟수 소진 상태에서도 강제로 되살립니다.
        // 정상 게임플레이에서는 사망은 되돌릴 수 없고 부활 횟수도 이렇게 초기화되지 않습니다.
        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        NotifyDownTimerChanged();

        m_reviveCount = 0;
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);

        ResetInjuryDamage();
        ReviveToHp(m_maxHp);
    }

    /// <summary>켜면 피해를 전혀 받지 않습니다(무한 체력). 디버그 트레이너 창에서 사용합니다.</summary>
    public bool DebugInfiniteHealth
    {
        get => m_debugInfiniteHealth;
        set => m_debugInfiniteHealth = value;
    }
#endif
}
