using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 작업이 시작될 때 고정하는 아이템 1개당 실제 재료 비용입니다.
/// </summary>
[Serializable]
public sealed class ManufacturingMaterialCostSnapshot
{
    [SerializeField] private CurrencyType m_type;
    [Min(0)]
    [SerializeField] private int m_unitAmount;

    public CurrencyType Type => m_type;
    public int UnitAmount => Math.Max(0, m_unitAmount);
    public bool IsValid => UnitAmount > 0;

    public ManufacturingMaterialCostSnapshot()
    {
    }

    public ManufacturingMaterialCostSnapshot(CurrencyType type, int unitAmount)
    {
        m_type = type;
        m_unitAmount = Math.Max(0, unitAmount);
    }

    public ManufacturingMaterialCostSnapshot Clone()
    {
        return new ManufacturingMaterialCostSnapshot(Type, UnitAmount);
    }

    public void EnsureValid()
    {
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
    [SerializeField] private string m_resultItemDefinitionId = string.Empty;
    [Min(0)]
    [SerializeField] private int m_requestedQuantity;
    [Min(0)]
    [SerializeField] private int m_unitWorkSnapshot;
    [Min(0)]
    [SerializeField] private int m_totalWork;
    [Min(0)]
    [SerializeField] private int m_processedWork;
    [SerializeField] private List<ManufacturingMaterialCostSnapshot> m_unitCostSnapshots = new();

    public int SlotIndex => m_slotIndex;
    public string RecipeId => m_recipeId ?? string.Empty;
    public string ResultItemDefinitionId => m_resultItemDefinitionId ?? string.Empty;
    public int RequestedQuantity => Math.Max(0, m_requestedQuantity);
    public int UnitWorkSnapshot => Math.Max(0, m_unitWorkSnapshot);
    public int TotalWork => Math.Max(0, m_totalWork);
    public int ProcessedWork => Mathf.Clamp(m_processedWork, 0, TotalWork);
    public int RemainingWork => TotalWork - ProcessedWork;
    public int CompletedQuantity => UnitWorkSnapshot <= 0
        ? 0
        : Math.Min(RequestedQuantity, ProcessedWork / UnitWorkSnapshot);
    public int RemainingQuantity => RequestedQuantity - CompletedQuantity;
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
                && !string.IsNullOrWhiteSpace(ResultItemDefinitionId)
                && RequestedQuantity > 0
                && UnitWorkSnapshot > 0
                && TryCalculateTotalWork(UnitWorkSnapshot, RequestedQuantity, out int expectedTotalWork)
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
        int requestedQuantity,
        int unitWorkSnapshot,
        IEnumerable<CurrencyCost> unitCosts)
    {
        m_slotIndex = slotIndex;
        m_recipeId = NormalizeId(recipeId);
        m_resultItemDefinitionId = NormalizeId(resultItemDefinitionId);
        m_requestedQuantity = Math.Max(0, requestedQuantity);
        m_unitWorkSnapshot = Math.Max(0, unitWorkSnapshot);
        m_totalWork = TryCalculateTotalWork(m_unitWorkSnapshot, m_requestedQuantity, out int totalWork)
            ? totalWork
            : 0;
        m_processedWork = 0;
        m_unitCostSnapshots = CreateCostSnapshots(unitCosts);
        EnsureValid();
    }

    /// <summary>
    /// 작업량을 더하고 이번 호출로 새로 완성된 아이템 수량을 반환합니다.
    /// </summary>
    public int ApplyWork(int workAmount)
    {
        EnsureValid();
        if (!IsValid || workAmount <= 0 || IsComplete)
            return 0;

        int completedBefore = CompletedQuantity;
        m_processedWork = workAmount >= RemainingWork
            ? TotalWork
            : ProcessedWork + workAmount;
        return CompletedQuantity - completedBefore;
    }

    public ManufacturingJobRuntimeData Clone()
    {
        EnsureValid();
        return new ManufacturingJobRuntimeData
        {
            m_slotIndex = SlotIndex,
            m_recipeId = RecipeId,
            m_resultItemDefinitionId = ResultItemDefinitionId,
            m_requestedQuantity = RequestedQuantity,
            m_unitWorkSnapshot = UnitWorkSnapshot,
            m_totalWork = TotalWork,
            m_processedWork = ProcessedWork,
            m_unitCostSnapshots = CloneCostSnapshots(m_unitCostSnapshots)
        };
    }

    public void EnsureValid()
    {
        m_recipeId = NormalizeId(m_recipeId);
        m_resultItemDefinitionId = NormalizeId(m_resultItemDefinitionId);
        m_requestedQuantity = Math.Max(0, m_requestedQuantity);
        m_unitWorkSnapshot = Math.Max(0, m_unitWorkSnapshot);
        m_totalWork = TryCalculateTotalWork(m_unitWorkSnapshot, m_requestedQuantity, out int totalWork)
            ? totalWork
            : 0;
        m_processedWork = Mathf.Clamp(m_processedWork, 0, m_totalWork);
        m_unitCostSnapshots = NormalizeCostSnapshots(m_unitCostSnapshots);
    }

    private static bool TryCalculateTotalWork(int unitWork, int quantity, out int totalWork)
    {
        totalWork = 0;
        if (unitWork <= 0 || quantity <= 0)
            return false;

        long calculated = (long)unitWork * quantity;
        if (calculated > int.MaxValue)
            return false;

        totalWork = (int)calculated;
        return true;
    }

    private static List<ManufacturingMaterialCostSnapshot> CreateCostSnapshots(
        IEnumerable<CurrencyCost> source)
    {
        List<ManufacturingMaterialCostSnapshot> snapshots = new();
        if (source == null)
            return snapshots;

        foreach (CurrencyCost cost in source)
        {
            if (cost.Amount > 0)
                snapshots.Add(new ManufacturingMaterialCostSnapshot(cost.Type, cost.Amount));
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

            ManufacturingMaterialCostSnapshot existing = normalized.Find(item => item.Type == cost.Type);
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
}
