using System.Collections.Generic;
using System;

//창고시스템
public class ResourceStorage
{
    private readonly Dictionary<CurrencyType, int> _amounts = new();

    public int GetAmount(CurrencyType type)
    {
        return _amounts.TryGetValue(type, out int amount) ? amount : 0;
    }

    public bool Add(CurrencyType type, int amount)
    {
        if (amount <= 0)
            return false;

        _amounts[type] = GetAmount(type) + amount;
        return true;
    }

    public void SetAmount(CurrencyType type, int amount)
    {
        _amounts[type] = Math.Max(0, amount);
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

        _amounts[cost.Type] = GetAmount(cost.Type) - cost.Amount;
        return true;
    }
}
