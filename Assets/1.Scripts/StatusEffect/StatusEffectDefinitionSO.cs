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

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_duration = Mathf.Max(0.01f, m_duration);
        m_maxStacks = Mathf.Max(1, m_maxStacks);
        m_movementSpeedPercent = Mathf.Max(1.0f, m_movementSpeedPercent);
        m_actionSpeedPercent = Mathf.Max(1.0f, m_actionSpeedPercent);
        m_periodicDamage = Mathf.Max(0, m_periodicDamage);
        m_tickInterval = Mathf.Max(0.01f, m_tickInterval);
    }
#endif
}
