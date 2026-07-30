using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 작업이 시작될 때 고정하는 배치 1회당 실제 재료 비용입니다.
/// </summary>
[Serializable]
public sealed class ManufacturingMaterialCostSnapshot
{
    [SerializeField] private string m_resourceId = string.Empty;
    [Min(0)]
    [SerializeField] private int m_unitAmount;

    public string ResourceId => ResourceIds.Normalize(m_resourceId);
    public int UnitAmount => Math.Max(0, m_unitAmount);
    public bool IsValid =>
        !string.IsNullOrEmpty(ResourceId) && UnitAmount > 0;

    public ManufacturingMaterialCostSnapshot()
    {
    }

    public ManufacturingMaterialCostSnapshot(
        string resourceId,
        int unitAmount)
    {
        m_resourceId = ResourceIds.Normalize(resourceId);
        m_unitAmount = Math.Max(0, unitAmount);
    }

    public ManufacturingMaterialCostSnapshot Clone()
    {
        return new ManufacturingMaterialCostSnapshot(
            ResourceId,
            UnitAmount);
    }

    public void EnsureValid()
    {
        m_resourceId = ResourceIds.Normalize(m_resourceId);
        m_unitAmount = Math.Max(0, m_unitAmount);
    }

    internal void MergeUnitAmount(int amount)
    {
        if (amount <= 0)
            return;

        m_unitAmount = UnitAmount > int.MaxValue - amount
            ? int.MaxValue
            : UnitAmount + amount;
    }
}

/// <summary>
/// 제조 슬롯 하나에서 진행 중인 주문 한 건의 런타임 상태입니다.
/// </summary>
[Serializable]
public sealed class ManufacturingJobRuntimeData
{
    [Min(0)]
    [SerializeField] private int m_slotIndex;
    [SerializeField] private string m_recipeId = string.Empty;
    [SerializeField] private ManufacturingResultKind m_resultKind =
        ManufacturingResultKind.Item;
    [SerializeField] private string m_resultDefinitionId = string.Empty;
    [Min(0)]
    [SerializeField] private int m_requestedQuantity;
    [Min(1)]
    [SerializeField] private int m_resultQuantityPerBatchSnapshot = 1;
    [Min(0)]
    [SerializeField] private int m_unitWorkSnapshot;
    [Min(0)]
    [SerializeField] private int m_totalWork;
    [Min(0)]
    [SerializeField] private int m_processedWork;
    [SerializeField] private List<ManufacturingMaterialCostSnapshot> m_unitCostSnapshots = new();

    public int SlotIndex => m_slotIndex;
    public string RecipeId => m_recipeId ?? string.Empty;
    public ManufacturingResultKind ResultKind => m_resultKind;
    public string ResultDefinitionId => m_resultDefinitionId ?? string.Empty;
    public string ResultItemDefinitionId =>
        ResultKind == ManufacturingResultKind.Item
            ? ResultDefinitionId
            : string.Empty;
    public string ResultResourceId =>
        ResultKind == ManufacturingResultKind.Resource
            ? ResultDefinitionId
            : string.Empty;
    public int RequestedBatchCount => Math.Max(0, m_requestedQuantity);
    public int ResultQuantityPerBatchSnapshot => Math.Max(1, m_resultQuantityPerBatchSnapshot);
    public int UnitWorkSnapshot => Math.Max(0, m_unitWorkSnapshot);
    public int TotalWork => Math.Max(0, m_totalWork);
    public int ProcessedWork => Mathf.Clamp(m_processedWork, 0, TotalWork);
    public int RemainingWork => TotalWork - ProcessedWork;
    public int CompletedBatchCount => UnitWorkSnapshot <= 0
        ? 0
        : Math.Min(RequestedBatchCount, ProcessedWork / UnitWorkSnapshot);
    public int RemainingBatchCount => RequestedBatchCount - CompletedBatchCount;
    public long RequestedResultQuantity =>
        (long)RequestedBatchCount * ResultQuantityPerBatchSnapshot;
    public long CompletedResultQuantity =>
        (long)CompletedBatchCount * ResultQuantityPerBatchSnapshot;
    public long RemainingResultQuantity =>
        (long)RemainingBatchCount * ResultQuantityPerBatchSnapshot;
    public long RequestedResultItemQuantity => RequestedResultQuantity;
    public long CompletedResultItemQuantity => CompletedResultQuantity;
    public long RemainingResultItemQuantity => RemainingResultQuantity;

    /// <summary>이전 호출부 호환용 별칭입니다. 값의 의미는 요청 배치 수입니다.</summary>
    public int RequestedQuantity => RequestedBatchCount;
    /// <summary>이전 호출부 호환용 별칭입니다. 값의 의미는 완료 배치 수입니다.</summary>
    public int CompletedQuantity => CompletedBatchCount;
    /// <summary>이전 호출부 호환용 별칭입니다. 값의 의미는 남은 배치 수입니다.</summary>
    public int RemainingQuantity => RemainingBatchCount;

    public bool IsComplete => IsValid && ProcessedWork >= TotalWork;
    public float Progress01 => TotalWork <= 0 ? 0.0f : (float)ProcessedWork / TotalWork;
    public IReadOnlyList<ManufacturingMaterialCostSnapshot> UnitCostSnapshots
    {
        get
        {
            m_unitCostSnapshots ??= new List<ManufacturingMaterialCostSnapshot>();
            return m_unitCostSnapshots;
        }
    }

    public bool IsValid
    {
        get
        {
            return SlotIndex >= 0
                && !string.IsNullOrWhiteSpace(RecipeId)
                && IsValidResultDefinition(ResultKind, ResultDefinitionId)
                && RequestedBatchCount > 0
                && ResultQuantityPerBatchSnapshot > 0
                && UnitWorkSnapshot > 0
                && TryCalculateTotalWork(
                    UnitWorkSnapshot,
                    RequestedBatchCount,
                    out int expectedTotalWork)
                && TotalWork == expectedTotalWork;
        }
    }

    /// <summary>Unity 직렬화를 위한 기본 생성자입니다.</summary>
    public ManufacturingJobRuntimeData()
    {
    }

    public ManufacturingJobRuntimeData(
        int slotIndex,
        string recipeId,
        string resultItemDefinitionId,
        int requestedBatchCount,
        int unitWorkSnapshot,
        IEnumerable<ResourceCost> unitCosts)
        : this(
            slotIndex,
            recipeId,
            ManufacturingResultKind.Item,
            resultItemDefinitionId,
            requestedBatchCount,
            1,
            unitWorkSnapshot,
            unitCosts)
    {
    }

    public ManufacturingJobRuntimeData(
        int slotIndex,
        string recipeId,
        string resultItemDefinitionId,
        int requestedBatchCount,
        int resultQuantityPerBatchSnapshot,
        int unitWorkSnapshot,
        IEnumerable<ResourceCost> unitCosts)
        : this(
            slotIndex,
            recipeId,
            ManufacturingResultKind.Item,
            resultItemDefinitionId,
            requestedBatchCount,
            resultQuantityPerBatchSnapshot,
            unitWorkSnapshot,
            unitCosts)
    {
    }

    public ManufacturingJobRuntimeData(
        int slotIndex,
        string recipeId,
        ManufacturingResultKind resultKind,
        string resultDefinitionId,
        int requestedBatchCount,
        int unitWorkSnapshot,
        IEnumerable<ResourceCost> unitCosts)
        : this(
            slotIndex,
            recipeId,
            resultKind,
            resultDefinitionId,
            requestedBatchCount,
            1,
            unitWorkSnapshot,
            unitCosts)
    {
    }

    public ManufacturingJobRuntimeData(
        int slotIndex,
        string recipeId,
        ManufacturingResultKind resultKind,
        string resultDefinitionId,
        int requestedBatchCount,
        int resultQuantityPerBatchSnapshot,
        int unitWorkSnapshot,
        IEnumerable<ResourceCost> unitCosts)
    {
        m_slotIndex = slotIndex;
        m_recipeId = NormalizeId(recipeId);
        m_resultKind = NormalizeResultKind(resultKind);
        m_resultDefinitionId = NormalizeResultDefinitionId(
            m_resultKind,
            resultDefinitionId);
        m_requestedQuantity = Math.Max(0, requestedBatchCount);
        m_resultQuantityPerBatchSnapshot = Math.Max(1, resultQuantityPerBatchSnapshot);
        m_unitWorkSnapshot = Math.Max(0, unitWorkSnapshot);
        m_totalWork = TryCalculateTotalWork(m_unitWorkSnapshot, m_requestedQuantity, out int totalWork)
            ? totalWork
            : 0;
        m_processedWork = 0;
        m_unitCostSnapshots = CreateCostSnapshots(unitCosts);
        EnsureValid();
    }

    /// <summary>
    /// 작업량을 더하고 이번 호출로 새로 완료된 배치 수를 반환합니다.
    /// </summary>
    public int ApplyWork(int workAmount)
    {
        EnsureValid();
        if (!IsValid || workAmount <= 0 || IsComplete)
            return 0;

        int completedBefore = CompletedBatchCount;
        m_processedWork = workAmount >= RemainingWork
            ? TotalWork
            : ProcessedWork + workAmount;
        return CompletedBatchCount - completedBefore;
    }

    public ManufacturingJobRuntimeData Clone()
    {
        EnsureValid();
        return new ManufacturingJobRuntimeData
        {
            m_slotIndex = SlotIndex,
            m_recipeId = RecipeId,
            m_resultKind = ResultKind,
            m_resultDefinitionId = ResultDefinitionId,
            m_requestedQuantity = RequestedBatchCount,
            m_resultQuantityPerBatchSnapshot = ResultQuantityPerBatchSnapshot,
            m_unitWorkSnapshot = UnitWorkSnapshot,
            m_totalWork = TotalWork,
            m_processedWork = ProcessedWork,
            m_unitCostSnapshots = CloneCostSnapshots(m_unitCostSnapshots)
        };
    }

    public void EnsureValid()
    {
        m_recipeId = NormalizeId(m_recipeId);
        m_resultKind = NormalizeResultKind(m_resultKind);
        m_resultDefinitionId = NormalizeResultDefinitionId(
            m_resultKind,
            m_resultDefinitionId);
        m_requestedQuantity = Math.Max(0, m_requestedQuantity);
        m_resultQuantityPerBatchSnapshot = Math.Max(1, m_resultQuantityPerBatchSnapshot);
        m_unitWorkSnapshot = Math.Max(0, m_unitWorkSnapshot);
        m_totalWork = TryCalculateTotalWork(m_unitWorkSnapshot, m_requestedQuantity, out int totalWork)
            ? totalWork
            : 0;
        m_processedWork = Mathf.Clamp(m_processedWork, 0, m_totalWork);
        m_unitCostSnapshots = NormalizeCostSnapshots(m_unitCostSnapshots);
    }

    private static bool TryCalculateTotalWork(int unitWork, int batchCount, out int totalWork)
    {
        totalWork = 0;
        if (unitWork <= 0 || batchCount <= 0)
            return false;

        long calculated = (long)unitWork * batchCount;
        if (calculated > int.MaxValue)
            return false;

        totalWork = (int)calculated;
        return true;
    }

    private static List<ManufacturingMaterialCostSnapshot> CreateCostSnapshots(
        IEnumerable<ResourceCost> source)
    {
        List<ManufacturingMaterialCostSnapshot> snapshots = new();
        if (source == null)
            return snapshots;

        foreach (ResourceCost cost in source)
        {
            if (cost.IsValid)
            {
                snapshots.Add(new ManufacturingMaterialCostSnapshot(
                    cost.ResourceId,
                    cost.Amount));
            }
        }

        return NormalizeCostSnapshots(snapshots);
    }

    private static List<ManufacturingMaterialCostSnapshot> CloneCostSnapshots(
        IEnumerable<ManufacturingMaterialCostSnapshot> source)
    {
        List<ManufacturingMaterialCostSnapshot> clone = new();
        if (source == null)
            return clone;

        foreach (ManufacturingMaterialCostSnapshot cost in source)
        {
            if (cost != null && cost.IsValid)
                clone.Add(cost.Clone());
        }

        return clone;
    }

    private static List<ManufacturingMaterialCostSnapshot> NormalizeCostSnapshots(
        IEnumerable<ManufacturingMaterialCostSnapshot> source)
    {
        List<ManufacturingMaterialCostSnapshot> normalized = new();
        if (source == null)
            return normalized;

        foreach (ManufacturingMaterialCostSnapshot cost in source)
        {
            if (cost == null)
                continue;

            cost.EnsureValid();
            if (!cost.IsValid)
                continue;

            ManufacturingMaterialCostSnapshot existing = normalized.Find(
                item => string.Equals(
                    item.ResourceId,
                    cost.ResourceId,
                    StringComparison.Ordinal));
            if (existing == null)
                normalized.Add(cost.Clone());
            else
                existing.MergeUnitAmount(cost.UnitAmount);
        }

        return normalized;
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static ManufacturingResultKind NormalizeResultKind(
        ManufacturingResultKind resultKind)
    {
        return resultKind == ManufacturingResultKind.Resource
            ? ManufacturingResultKind.Resource
            : ManufacturingResultKind.Item;
    }

    private static string NormalizeResultDefinitionId(
        ManufacturingResultKind resultKind,
        string resultDefinitionId)
    {
        return resultKind == ManufacturingResultKind.Resource
            ? ResourceIds.Normalize(resultDefinitionId)
            : NormalizeId(resultDefinitionId);
    }

    private static bool IsValidResultDefinition(
        ManufacturingResultKind resultKind,
        string resultDefinitionId)
    {
        return resultKind == ManufacturingResultKind.Resource
            ? ResourceIds.IsDefined(resultDefinitionId)
            : !string.IsNullOrWhiteSpace(resultDefinitionId);
    }
}
