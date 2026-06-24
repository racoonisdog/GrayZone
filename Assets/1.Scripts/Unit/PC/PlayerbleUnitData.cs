using System;
using UnityEngine;

[DisallowMultipleComponent]
/// <summary>
/// 플레이어 유닛의 공용 상태를 모아두는 공개 데이터 Module입니다.
/// 외부 시스템은 이 Module을 통해 식별 정보, 신뢰도, 생존 상태, 체력 요약값을 읽습니다.
/// </summary>
public class PlayerbleUnitData : MonoBehaviour
{
    [Header("Public Identity")]
    [SerializeField] private string m_runtimeId;
    [SerializeField] private string m_displayName = "Player";

    [Header("Public Runtime Data")]
    [Range(0, 100)]
    [SerializeField] private int m_reliability;

    [Header("Observed Modules")]
    [SerializeField] private PlayerHealth m_health;
    [SerializeField] private SquadMemberController m_squadMember;
    [SerializeField] private Transform m_publicTarget;

    /// <summary>
    /// 공개 상태 중 외부에 노출되는 값이 바뀌었을 때 발생합니다.
    /// </summary>
    public event Action OnPublicDataChanged;

    /// <summary>
    /// 이 유닛을 외부 시스템에서 식별하기 위한 런타임 ID입니다.
    /// 값이 비어 있으면 GameObject 이름을 반환합니다.
    /// </summary>
    public string RuntimeId => string.IsNullOrWhiteSpace(m_runtimeId)
        ? gameObject.name
        : m_runtimeId.Trim();

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
    public bool IsPlayerControlled => m_squadMember != null && m_squadMember.IsPlayerControlled;

    /// <summary>
    /// 출격 또는 배치 가능한 상태인지 여부입니다.
    /// </summary>
    public bool CanDeploy => IsAlive && !IsDown;

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
    /// 외부 시스템이 이 유닛을 대상으로 삼을 기준 Transform입니다.
    /// </summary>
    public Transform PublicTarget => m_publicTarget != null ? m_publicTarget : transform;

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
    }

    private void OnDisable()
    {
        UnsubscribeHealth();
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
        m_health.OnDied += NotifyPublicDataChanged;
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
        m_health.OnDied -= NotifyPublicDataChanged;
        m_health.OnRevive -= NotifyPublicDataChanged;
        m_health.OnInjuryGaugeChanged -= HandleInjuryGaugeChanged;
        m_health.OnInjuryStateChanged -= HandleInjuryStateChanged;
    }

    private void HandleHpChanged(int currentHp, int maxHp)
    {
        NotifyPublicDataChanged();
    }

    private void HandleInjuryGaugeChanged(int accumulatedDamage, float normalizedValue)
    {
        NotifyPublicDataChanged();
    }

    private void HandleInjuryStateChanged(PlayerInjuryState injuryState)
    {
        NotifyPublicDataChanged();
    }

    private void NotifyPublicDataChanged()
    {
        OnPublicDataChanged?.Invoke();
    }

    private void ClampValues()
    {
        m_reliability = Mathf.Clamp(m_reliability, 0, 100);
    }
}
