using System;
using System.Collections.Generic;

/// <summary>
/// 셸터 자원 작업본의 조회·입고·소비 규칙을 담당하는 비가시 시설 기능입니다.
/// 자원 상태를 별도로 소유하지 않고 ShelterRuntimeData의 ResourceStorage를 사용합니다.
/// </summary>
public sealed class StorageFacility
{
    private readonly ResourceStorage storage;
    private readonly List<ItemStorageEntry> itemStorageEntries;
    private readonly IReadOnlyList<ItemStorageEntry> readOnlyItemStorageEntries;
    private readonly Action notifyShelterDataChanged;

    /// <summary>자원 수량이 변경되거나 새 스냅샷이 적용됐을 때 발생합니다.</summary>
    public event Action ResourcesChanged;
    /// <summary>창고 아이템 수량이 변경되거나 새 스냅샷이 적용됐을 때 발생합니다.</summary>
    public event Action ItemsChanged;

    /// <summary>현재 자원 수량을 읽기 전용으로 제공합니다.</summary>
    public IReadOnlyDictionary<string, int> ResourceAmounts => storage.Amounts;
    /// <summary>현재 보유 중인 수량형 아이템 목록을 읽기 전용으로 제공합니다.</summary>
    public IReadOnlyList<ItemStorageEntry> ItemStorageEntries => readOnlyItemStorageEntries;

    public StorageFacility(
        ResourceStorage storage,
        List<ItemStorageEntry> itemStorageEntries,
        Action notifyShelterDataChanged)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.itemStorageEntries = itemStorageEntries
            ?? throw new ArgumentNullException(nameof(itemStorageEntries));
        readOnlyItemStorageEntries = this.itemStorageEntries.AsReadOnly();
        this.notifyShelterDataChanged = notifyShelterDataChanged;
    }

    public int GetResourceAmount(string resourceId)
    {
        return storage.GetAmount(resourceId);
    }

    public bool CanSpendResource(ResourceCost cost)
    {
        return cost.IsValid
            && storage.GetAmount(cost.ResourceId) >= cost.Amount;
    }

    public bool CanSpendResources(CostBundle costBundle)
    {
        if (costBundle == null || costBundle.IsFree)
            return true;

        if (!TryBuildCostTotals(costBundle, out Dictionary<string, int> totals))
            return false;

        foreach (KeyValuePair<string, int> total in totals)
        {
            if (storage.GetAmount(total.Key) < total.Value)
                return false;
        }

        return true;
    }

    public bool TrySpendResource(ResourceCost cost)
    {
        if (cost.Amount <= 0)
            return true;
        if (!storage.TrySpend(cost))
            return false;

        NotifyResourcesChanged();
        return true;
    }

    /// <summary>같은 자원 타입의 비용을 합산해 전체 확인 후 한 번에 차감합니다.</summary>
    public bool TrySpendResources(CostBundle costBundle)
    {
        if (costBundle == null || costBundle.IsFree)
            return true;

        if (!TryBuildCostTotals(costBundle, out Dictionary<string, int> totals))
            return false;

        foreach (KeyValuePair<string, int> total in totals)
        {
            if (storage.GetAmount(total.Key) < total.Value)
                return false;
        }

        foreach (KeyValuePair<string, int> total in totals)
        {
            storage.SetAmount(total.Key, storage.GetAmount(total.Key) - total.Value);
        }

        NotifyResourcesChanged();
        return true;
    }

    public bool TryAddResource(string resourceId, int amount)
    {
        if (!storage.Add(resourceId, amount))
            return false;

        NotifyResourcesChanged();
        return true;
    }

    /// <summary>
    /// 같은 자원 타입을 합산하고 모든 자원을 추가할 수 있을 때만 한 번에 입고합니다.
    /// 제조 작업 취소 환불처럼 여러 재화를 함께 되돌릴 때 사용합니다.
    /// </summary>
    public bool TryAddResources(CostBundle costBundle)
    {
        if (costBundle == null || costBundle.IsFree)
            return true;

        if (!TryBuildCostTotals(costBundle, out Dictionary<string, int> totals))
            return false;

        foreach (KeyValuePair<string, int> total in totals)
        {
            if (storage.GetAmount(total.Key) > int.MaxValue - total.Value)
                return false;
        }

        foreach (KeyValuePair<string, int> total in totals)
        {
            storage.SetAmount(total.Key, storage.GetAmount(total.Key) + total.Value);
        }

        NotifyResourcesChanged();
        return true;
    }

    public void SetResourceAmount(string resourceId, int amount)
    {
        if (storage.SetAmount(resourceId, amount))
            NotifyResourcesChanged();
    }

    /// <summary>아이템 정의 ID에 해당하는 현재 총 보유 수량을 반환합니다.</summary>
    public int GetItemQuantity(string itemDefinitionId)
    {
        ItemStorageEntry entry = FindItemStorageEntry(itemDefinitionId);
        return entry?.Quantity ?? 0;
    }

    /// <summary>
    /// 완성 아이템을 창고에 입고합니다. 같은 ID가 있으면 수량을 합치고, 없으면 새 항목을 추가합니다.
    /// </summary>
    public bool TryAddItem(string itemDefinitionId, int amount)
    {
        string id = NormalizeItemDefinitionId(itemDefinitionId);
        if (string.IsNullOrEmpty(id) || amount <= 0)
            return false;

        ItemStorageEntry entry = FindItemStorageEntry(id);
        if (entry != null)
        {
            if (!entry.TryAddQuantity(amount))
                return false;
        }
        else
        {
            itemStorageEntries.Add(new ItemStorageEntry(id, amount));
        }

        NotifyItemsChanged();
        return true;
    }

    /// <summary>셸터 진입/배틀 귀환 등 외부 스냅샷 적용을 UI에 알립니다.</summary>
    internal void NotifySnapshotApplied()
    {
        ResourcesChanged?.Invoke();
        ItemsChanged?.Invoke();
    }

    private void NotifyResourcesChanged()
    {
        notifyShelterDataChanged?.Invoke();
        ResourcesChanged?.Invoke();
    }

    private void NotifyItemsChanged()
    {
        notifyShelterDataChanged?.Invoke();
        ItemsChanged?.Invoke();
    }

    private ItemStorageEntry FindItemStorageEntry(string itemDefinitionId)
    {
        string id = NormalizeItemDefinitionId(itemDefinitionId);
        if (string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < itemStorageEntries.Count; i++)
        {
            ItemStorageEntry entry = itemStorageEntries[i];
            if (entry != null
                && string.Equals(entry.ItemDefinitionId, id, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    private static string NormalizeItemDefinitionId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static bool TryBuildCostTotals(
        CostBundle costBundle,
        out Dictionary<string, int> totals)
    {
        totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ResourceCost cost in costBundle.Costs)
        {
            if (!cost.IsValid)
                continue;

            totals.TryGetValue(cost.ResourceId, out int current);
            if (current > int.MaxValue - cost.Amount)
                return false;

            totals[cost.ResourceId] = current + cost.Amount;
        }

        return true;
    }
}
