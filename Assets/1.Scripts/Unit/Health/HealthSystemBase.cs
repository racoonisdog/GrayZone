using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VInspector;

/// <summary>
/// 캐릭터의 체력 값을 관리하고, 체력 UI를 갱신하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 최대 체력, 현재 체력, 피해, 회복, 사망 처리를 담당합니다.
/// UI 참조가 비어 있어도 체력 로직은 동작하며, UI 갱신만 생략됩니다.
/// </remarks>
public class HealthSystemBase : MonoBehaviour
{
    [Foldout("HP Options")]
    [Tooltip("최대 체력입니다. 1보다 작은 값은 자동으로 1로 보정됩니다.")]
    [FormerlySerializedAs("m_maxHP")]
    [SerializeField] protected int m_maxHp = 10;

    [Tooltip("현재 체력입니다. 런타임 시작 시 최대 체력으로 초기화됩니다.")]
    [FormerlySerializedAs("m_currentHP")]
    [SerializeField] protected int m_currentHp;

    [Foldout("UI Options")]
    [Tooltip("체력 비율을 표시할 UI 슬라이더입니다.")]
    [FormerlySerializedAs("hpSlider")]
    [SerializeField] protected Slider m_hpSlider;

    [Tooltip("현재 체력과 최대 체력을 표시할 TMP 텍스트입니다.")]
    [FormerlySerializedAs("hpText")]
    [SerializeField] protected TextMeshProUGUI m_hpText;

    protected bool m_isDead;

    /// <summary>
    /// 현재 체력입니다.
    /// </summary>
    public int CurrentHP => m_currentHp;

    /// <summary>
    /// 최대 체력입니다.
    /// </summary>
    public int MaxHP => m_maxHp;

    /// <summary>
    /// 현재 사망 상태 여부입니다.
    /// </summary>
    public bool IsDead => m_isDead;

    /// <summary>
    /// 컴포넌트가 시작될 때 체력을 초기화하고 UI를 갱신합니다.
    /// </summary>
    private void Start()
    {
        InitializeHealth();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector 값이 변경될 때 체력 값을 유효 범위로 보정합니다.
    /// </summary>
    protected void OnValidate()
    {
        m_maxHp = Mathf.Max(1, m_maxHp);
        m_currentHp = Mathf.Clamp(m_currentHp, 0, m_maxHp);
    }
#endif

    /// <summary>
    /// 현재 체력을 최대 체력으로 초기화합니다.
    /// </summary>
    public void InitializeHealth()
    {
        m_maxHp = Mathf.Max(1, m_maxHp);
        m_currentHp = m_maxHp;
        m_isDead = false;

        UpdateUI();
    }

    /// <summary>
    /// 최대 체력을 설정합니다.
    /// </summary>
    /// <param name="value">새 최대 체력입니다. 1보다 작은 값은 1로 보정됩니다.</param>
    /// <param name="fillCurrentHp">현재 체력도 최대 체력으로 채울지 여부입니다.</param>
    public void SetMaxHP(int value, bool fillCurrentHp = false)
    {
        m_maxHp = Mathf.Max(1, value);

        if (fillCurrentHp)
        {
            m_currentHp = m_maxHp;
            m_isDead = false;
        }
        else
        {
            m_currentHp = Mathf.Clamp(m_currentHp, 0, m_maxHp);
        }

        UpdateUI();
    }

    /// <summary>
    /// 현재 체력을 직접 설정합니다.
    /// </summary>
    /// <param name="value">새 현재 체력입니다. 0과 최대 체력 사이로 보정됩니다.</param>
    public void SetCurrentHP(int value)
    {
        m_currentHp = Mathf.Clamp(value, 0, m_maxHp);
        m_isDead = m_currentHp <= 0;

        UpdateUI();
    }

    /// <summary>
    /// 지정한 피해량만큼 현재 체력을 감소시킵니다.
    /// </summary>
    /// <param name="damage">적용할 피해량입니다. 0보다 작은 값은 무시됩니다.</param>
    public void TakeDamage(int damage)
    {
        if (m_isDead)
            return;

        damage = Mathf.Max(0, damage);

        if (damage <= 0)
            return;

        m_currentHp = Mathf.Max(m_currentHp - damage, 0);

        Debug.Log($"[HealthSystem] Player Hit. Current HP : {m_currentHp}", this);

        UpdateUI();

        if (m_currentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 지정한 회복량만큼 현재 체력을 회복합니다.
    /// </summary>
    /// <param name="amount">적용할 회복량입니다. 0보다 작은 값은 무시됩니다.</param>
    public void Heal(int amount)
    {
        if (m_isDead)
            return;

        amount = Mathf.Max(0, amount);

        if (amount <= 0)
            return;

        m_currentHp = Mathf.Min(m_currentHp + amount, m_maxHp);

        UpdateUI();
    }

    /// <summary>
    /// 체력 UI를 현재 체력 값에 맞게 갱신합니다.
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
    /// 체력이 0 이하가 되었을 때 사망 상태로 전환합니다.
    /// </summary>
    /// <remarks>
    /// 이후 애니메이션, 입력 비활성화, 게임오버 UI, 리스폰 처리 등을 이 지점에서 연결할 수 있습니다.
    /// </remarks>
    protected void Die()
    {
        if (m_isDead)
            return;

        m_isDead = true;

        Debug.Log("[HealthSystem] Player Dead", this);
    }
}
