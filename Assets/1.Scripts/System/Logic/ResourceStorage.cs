using System.Collections.Generic;
using System;

//창고시스템
public class ResourceStorage
{
    private readonly Dictionary<CurrencyType, int> m_amounts = new();

    public IReadOnlyDictionary<CurrencyType, int> Amounts => m_amounts;

    public int GetAmount(CurrencyType type)
    {
        return m_amounts.TryGetValue(type, out int amount) ? amount : 0;
    }

    public bool Add(CurrencyType type, int amount)
    {
        if (amount <= 0)
            return false;

        m_amounts[type] = GetAmount(type) + amount;
        return true;
    }

    public void SetAmount(CurrencyType type, int amount)
    {
        m_amounts[type] = Math.Max(0, amount);
    }

    public bool CanSpend(CurrencyCost cost)
    {
        return cost.Amount <= 0 || GetAmount(cost.Type) >= cost.Amount;
    }

    public bool TrySpend(CurrencyCost cost)
    {
        //ToDo : 자원재화가 부족하다는 이벤트 연결
        if (!CanSpend(cost))
            return false;

        m_amounts[cost.Type] = GetAmount(cost.Type) - cost.Amount;
        return true;
    }

    public Dictionary<CurrencyType, int> CreateSnapshot()
    {
        return new Dictionary<CurrencyType, int>(m_amounts);
    }

    public void CopyFrom(ResourceStorage source)
    {
        if (source == null)
        {
            Clear();
            return;
        }

        ApplySnapshot(source.m_amounts);
    }

    public void ApplySnapshot(IReadOnlyDictionary<CurrencyType, int> snapshot)
    {
        m_amounts.Clear();
        if (snapshot == null)
            return;

        foreach (KeyValuePair<CurrencyType, int> entry in snapshot)
        {
            SetAmount(entry.Key, entry.Value);
        }
    }

    public void Clear()
    {
        m_amounts.Clear();
    }
}
