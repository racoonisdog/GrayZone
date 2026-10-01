using UnityEngine;

/// <summary>상태효과가 이로운 효과인지, 해로운 효과인지 구분합니다.</summary>
public enum StatusEffectCategory
{
    Buff,
    Debuff,
    CrowdControl,
}

/// <summary>같은 상태효과가 다시 들어왔을 때 처리 방식입니다.</summary>
public enum StatusEffectStackPolicy
{
    RefreshDuration,
    StackIntensity,
    IndependentBySource,
}

/// <summary>버프·디버프·상태이상의 고정 수치와 중첩 규칙을 정의하는 에셋입니다.</summary>
[CreateAssetMenu(fileName = "StatusEffect_Name", menuName = "GrayZone/Status Effect/Definition")]
public sealed class StatusEffectDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string m_displayName = "상태효과";
    [SerializeField] private StatusEffectCategory m_category = StatusEffectCategory.Buff;

    [Header("Lifetime")]
    [Min(0.01f)]
    [SerializeField] private float m_duration = 5.0f;
    [SerializeField] private StatusEffectStackPolicy m_stackPolicy = StatusEffectStackPolicy.RefreshDuration;
    [Min(1)]
    [SerializeField] private int m_maxStacks = 1;

    [Header("Recipient Visual")]
    [Tooltip("Optional pooled visual shown around the recipient while this status is active.")]
    [SerializeField] private StatusEffectVisualSO m_recipientVisual;

    [Header("Stat Modifiers")]
    [Tooltip("기본 이동속도를 100으로 본 배율입니다. 150이면 50% 증가, 70이면 30% 감소입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_movementSpeedPercent = 100.0f;
    [Tooltip("기본 행동속도를 100으로 본 배율입니다. 재장전·자세변경·Shotgun 펌프 속도에 적용됩니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_actionSpeedPercent = 100.0f;

    [Header("Periodic Damage")]
    [Tooltip("틱마다 적용할 피해입니다. 0이면 주기 피해가 없습니다.")]
    [Min(0)]
    [SerializeField] private int m_periodicDamage;
    [Tooltip("주기 피해 간격(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_tickInterval = 1.0f;
    [Tooltip("받는 쪽이 플레이어 진영일 때 주기 피해를 몇 % 줄일지입니다. 0이면 그대로, 50이면 절반, 100이면 피해가 없습니다. 줄인 값은 올림하므로 100% 미만이면 최소 1은 들어갑니다.")]
    [Range(0.0f, 100.0f)]
    [SerializeField] private float m_playerDamageReductionPercent;

    public string DisplayName => string.IsNullOrWhiteSpace(m_displayName) ? name : m_displayName;
    public StatusEffectCategory Category => m_category;
    public float Duration => Mathf.Max(0.01f, m_duration);
    public StatusEffectStackPolicy StackPolicy => m_stackPolicy;
    public int MaxStacks => Mathf.Max(1, m_maxStacks);
    public StatusEffectVisualSO RecipientVisual => m_recipientVisual;
    public float MovementSpeedMultiplier => Mathf.Max(0.01f, m_movementSpeedPercent * 0.01f);
    public float ActionSpeedMultiplier => Mathf.Max(0.01f, m_actionSpeedPercent * 0.01f);
    public int PeriodicDamage => Mathf.Max(0, m_periodicDamage);
    public float TickInterval => Mathf.Max(0.01f, m_tickInterval);
    public bool HasPeriodicDamage => PeriodicDamage > 0;
    public float PlayerDamageReductionPercent => Mathf.Clamp(m_playerDamageReductionPercent, 0.0f, 100.0f);

    /// <summary>받는 쪽 진영에 맞춰 주기 피해를 보정합니다. 플레이어 진영이면 감소율을 적용합니다.</summary>
    /// <param name="damage">중첩까지 반영한 원래 피해입니다.</param>
    /// <param name="recipientFaction">피해를 받는 쪽의 진영입니다.</param>
    /// <returns>실제로 넣을 피해입니다. 0이면 이번 틱은 피해가 없습니다.</returns>
    public int ResolvePeriodicDamage(int damage, Faction recipientFaction)
    {
        if (damage <= 0 || recipientFaction != Faction.Player)
        {
            return Mathf.Max(0, damage);
        }

        // 부동소수 오차로 2 × 0.5가 1.0000001이 되어 2로 올라가는 일을 막습니다.
        float reduced = damage * (1.0f - PlayerDamageReductionPercent * 0.01f);
        return Mathf.Max(0, Mathf.CeilToInt(reduced - 0.0001f));
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_duration = Mathf.Max(0.01f, m_duration);
        m_maxStacks = Mathf.Max(1, m_maxStacks);
        m_movementSpeedPercent = Mathf.Max(1.0f, m_movementSpeedPercent);
        m_actionSpeedPercent = Mathf.Max(1.0f, m_actionSpeedPercent);
        m_periodicDamage = Mathf.Max(0, m_periodicDamage);
        m_tickInterval = Mathf.Max(0.01f, m_tickInterval);
        m_playerDamageReductionPercent = Mathf.Clamp(m_playerDamageReductionPercent, 0.0f, 100.0f);
    }
#endif
}
