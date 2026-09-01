using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHealth_TPZ : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private int maxHP = 10;
    [SerializeField] private int currentHP;

    [Header("UI")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TextMeshProUGUI hpText;

    public int CurrentHP => currentHP;
    public int MaxHP => maxHP;

    private void Start()
    {
        currentHP = maxHP;
        UpdateUI();
    }

    public void TakeDamage(int damage)
    {
        currentHP -= damage;
        currentHP = Mathf.Max(currentHP, 0);

        Debug.Log("Player Hit! Current HP : " + currentHP);

        UpdateUI();

        if (currentHP <= 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        currentHP += amount;
        currentHP = Mathf.Min(currentHP, maxHP);

        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hpSlider != null)
        {
            hpSlider.value = (float)currentHP / maxHP;
        }

        if (hpText != null)
        {
            hpText.text = $"HP {currentHP} / {maxHP}";
        }
    }

    private void Die()
    {
        Debug.Log("Player Dead");
        // 나중에 여기서:
        // 애니메이션
        // 입력 비활성화
        // 게임오버 UI
        // 리스폰 처리
    }
}