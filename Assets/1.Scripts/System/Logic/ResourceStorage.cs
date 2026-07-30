using System;
using System.Collections.Generic;

/// <summary>
/// 안정적인 자원 ID별 수량을 보관하는 런타임 저장소입니다.
/// </summary>
public sealed class ResourceStorage
{
    private readonly Dictionary<string, int> m_amounts =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Amounts => m_amounts;

    public int GetAmount(string resourceId)
    {
        string id = ResourceIds.Normalize(resourceId);
        return !string.IsNullOrEmpty(id)
            && m_amounts.TryGetValue(id, out int amount)
                ? amount
                : 0;
    }

    public bool Add(string resourceId, int amount)
    {
        string id = ResourceIds.Normalize(resourceId);
        if (string.IsNullOrEmpty(id) || amount <= 0)
            return false;

        int current = GetAmount(id);
        if (current > int.MaxValue - amount)
            return false;

        m_amounts[id] = current + amount;
        return true;
    }

    public bool SetAmount(string resourceId, int amount)
    {
        string id = ResourceIds.Normalize(resourceId);
        if (string.IsNullOrEmpty(id))
            return false;

        m_amounts[id] = Math.Max(0, amount);
        return true;
    }

    public bool CanSpend(ResourceCost cost)
    {
        return cost.IsValid
            && GetAmount(cost.ResourceId) >= cost.Amount;
    }

    public bool TrySpend(ResourceCost cost)
    {
        if (!CanSpend(cost))
            return false;

        m_amounts[cost.ResourceId] =
            GetAmount(cost.ResourceId) - cost.Amount;
        return true;
    }

    public Dictionary<string, int> CreateSnapshot()
    {
        return new Dictionary<string, int>(
            m_amounts,
            StringComparer.Ordinal);
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

    public void ApplySnapshot(IReadOnlyDictionary<string, int> snapshot)
    {
        m_amounts.Clear();
        if (snapshot == null)
            return;

        foreach (KeyValuePair<string, int> entry in snapshot)
            SetAmount(entry.Key, entry.Value);
    }

    public void Clear()
    {
        m_amounts.Clear();
    }
}
