using UnityEngine;

public class HPComponent : MonoBehaviour
{
    [SerializeField] private int maxHP = 100;
    [SerializeField] private int currentHP;

    public int MaxHP => maxHP;
    public int CurrentHP => currentHP;
    public bool IsDead => currentHP <= 0;

    public event System.Action<int, int> OnHPChanged;
    public event System.Action OnDied;
    public event System.Action OnRevive;

    private void Awake()
    {
        currentHP = maxHP;
    }

    public bool TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return false;

        currentHP = Mathf.Max(0, currentHP - amount);
        OnHPChanged?.Invoke(currentHP, maxHP);

        if (currentHP <= 0)
            OnDied?.Invoke();

        return true;
    }

    public bool Heal(int amount)
    {
        if (amount <= 0 || IsDead)
            return false;

        currentHP = Mathf.Min(maxHP, currentHP + amount);
        OnHPChanged?.Invoke(currentHP, maxHP);
        return true;
    }

    public void RestoreFull()
    {
        currentHP = maxHP;
        OnHPChanged?.Invoke(currentHP, maxHP);
    }

    public bool Revive(int amount)
    {
        if (!IsDead) return false;

        currentHP = Mathf.Clamp(amount, 1, maxHP);

        OnRevive?.Invoke();
        OnHPChanged?.Invoke(currentHP, maxHP);

        return true;
    }
}
