using UnityEngine;

public readonly struct CurrencyCost
{
    public CurrencyType Type { get; }
    public int Amount { get; }

    public CurrencyCost(CurrencyType type, int amount)
    {
        Type = type;
        Amount = amount;
    }
}
