using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VInspector;

/// <summary>
/// HP, 피해, 회복, 사망, 부활, 선택적 HP UI 갱신을 공통으로 처리하는 체력 컴포넌트입니다.
/// </summary>
public class HealthSystemBase : MonoBehaviour
{
    [Foldout("HP Options")]
    [Tooltip("최대 HP입니다. 1보다 작은 값은 1로 보정됩니다.")]
    [FormerlySerializedAs("m_maxHP")]
    [SerializeField] protected int m_maxHp = 10;

    [Tooltip("현재 HP입니다. Start에서 최대 HP로 초기화됩니다.")]
    [FormerlySerializedAs("m_currentHP")]
    [SerializeField] protected int m_currentHp;

    [Foldout("UI Options")]
    [Tooltip("선택 사항인 HP 슬라이더입니다. 이 참조가 없어도 체력 로직은 동작합니다.")]
    [FormerlySerializedAs("hpSlider")]
    [SerializeField] protected Slider m_hpSlider;

    [Tooltip("선택 사항인 HP 텍스트입니다. 이 참조가 없어도 체력 로직은 동작합니다.")]
    [FormerlySerializedAs("hpText")]
    [SerializeField] protected TextMeshProUGUI m_hpText;

    protected bool m_isDead;

    /// <summary>현재 HP입니다.</summary>
    public int CurrentHP => m_currentHp;

    /// <summary>최대 HP입니다.</summary>
    public int MaxHP => m_maxHp;

    /// <summary>이 체력 컴포넌트가 사망 상태인지 여부입니다.</summary>
    public bool IsDead => m_isDead;

    /// <summary>현재 HP나 최대 HP가 변경될 때 발생합니다. 인자는 현재 HP와 최대 HP입니다.</summary>
    public event Action<int, int> OnHPChanged;

    /// <summary>HP가 0에 도달해 컴포넌트가 사망 상태로 진입할 때 발생합니다.</summary>
    public event Action OnDied;

    /// <summary>부활 또는 전체 회복으로 컴포넌트가 사망 상태에서 벗어날 때 발생합니다.</summary>
    public event Action OnRevive;

    private void Start()
    {
        InitializeHealth();
    }

#if UNITY_EDITOR
    protected void OnValidate()
    {
        m_maxHp = Mathf.Max(1, m_maxHp);
        m_currentHp = Mathf.Clamp(m_currentHp, 0, m_maxHp);
    }
#endif

    /// <summary>
    /// 현재 HP를 최대 HP로 초기화하고 사망 상태를 해제합니다.
    /// </summary>
    public virtual void InitializeHealth()
    {
        m_maxHp = Mathf.Max(1, m_maxHp);
        m_currentHp = m_maxHp;
        m_isDead = false;

        NotifyHPChanged();
    }

    /// <summary>
    /// 최대 HP를 설정합니다. 필요하면 현재 HP도 새 최대 HP로 채웁니다.
    /// </summary>
    public void SetMaxHP(int value, bool fillCurrentHp = false)
    {
        m_maxHp = Mathf.Max(1, value);

        if (fillCurrentHp)
        {
            RestoreFull();
            return;
        }

        m_currentHp = Mathf.Clamp(m_currentHp, 0, m_maxHp);
        RefreshDeathState();
        NotifyHPChanged();
    }

    /// <summary>
    /// 현재 HP를 설정하고 필요하면 사망 상태를 갱신합니다.
    /// </summary>
    public void SetCurrentHP(int value)
    {
        m_currentHp = Mathf.Clamp(value, 0, m_maxHp);
        RefreshDeathState();
        NotifyHPChanged();
    }

    /// <summary>
    /// 피해를 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다.
    /// </summary>
    public virtual bool TakeDamage(int damage)
    {
        if (m_isDead)
        {
            return false;
        }

        damage = Mathf.Max(0, damage);
        if (damage <= 0)
        {
            return false;
        }

        m_currentHp = Mathf.Max(m_currentHp - damage, 0);
        Debug.Log($"[HealthSystem] Hit. Current HP : {m_currentHp}", this);

        NotifyHPChanged();

        if (m_currentHp <= 0)
        {
            Die();
        }

        return true;
    }

    /// <summary>
    /// HP를 회복합니다. HP가 실제로 변경된 경우에만 true를 반환합니다.
    /// </summary>
    public bool Heal(int amount)
    {
        if (m_isDead)
        {
            return false;
        }

        amount = Mathf.Max(0, amount);
        if (amount <= 0)
        {
            return false;
        }

        int previousHp = m_currentHp;
        m_currentHp = Mathf.Min(m_currentHp + amount, m_maxHp);

        if (m_currentHp == previousHp)
        {
            return false;
        }

        NotifyHPChanged();
        return true;
    }

    /// <summary>
    /// 현재 HP를 최대 HP로 회복합니다. 사망 상태라면 컴포넌트도 함께 부활시킵니다.
    /// </summary>
    public void RestoreFull()
    {
        bool wasDead = m_isDead;

        m_currentHp = m_maxHp;
        m_isDead = false;

        if (wasDead)
        {
            OnRevive?.Invoke();
        }

        NotifyHPChanged();
    }

    /// <summary>
    /// 지정한 HP 값으로 사망 상태에서 부활합니다.
    /// </summary>
    public bool Revive(int amount)
    {
        if (!m_isDead)
        {
            return false;
        }

        m_currentHp = Mathf.Clamp(amount, 1, m_maxHp);
        m_isDead = false;

        OnRevive?.Invoke();
        NotifyHPChanged();

        return true;
    }

    /// <summary>
    /// 현재 HP 값에 맞춰 선택 사항인 UI 참조를 갱신합니다.
    /// </summary>
    protected void UpdateUI()
    {
        if (m_hpSlider != null)
        {
            m_hpSlider.value = m_maxHp > 0
                ? (float)m_currentHp / m_maxHp
                : 0.0f;
        }

        if (m_hpText != null)
        {
            m_hpText.text = $"HP {m_currentHp} / {m_maxHp}";
        }
    }

    /// <summary>
    /// UI를 갱신하고 HP 변경 이벤트를 발생시킵니다.
    /// </summary>
    protected void NotifyHPChanged()
    {
        UpdateUI();
        OnHPChanged?.Invoke(m_currentHp, m_maxHp);
    }

    private void RefreshDeathState()
    {
        if (m_currentHp <= 0)
        {
            Die();
            return;
        }

        m_isDead = false;
    }

    /// <summary>
    /// 사망 상태로 진입하고 사망 이벤트를 발생시킵니다.
    /// </summary>
    protected virtual void Die()
    {
        if (m_isDead)
        {
            return;
        }

        m_isDead = true;

        Debug.Log("[HealthSystem] Dead", this);
        OnDied?.Invoke();
    }
}
