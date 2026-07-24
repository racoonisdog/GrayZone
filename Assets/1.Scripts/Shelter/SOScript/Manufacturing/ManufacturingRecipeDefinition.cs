using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 레시피의 아이템 1개당 재료 비용 항목입니다.
/// </summary>
[Serializable]
public sealed class ManufacturingMaterialCost
{
    [SerializeField] private CurrencyType m_type;
    [Min(0)]
    [SerializeField] private int m_amount;

    public CurrencyType Type => m_type;
    public int Amount => Mathf.Max(0, m_amount);

    public ManufacturingMaterialCost()
    {
    }

    public ManufacturingMaterialCost(CurrencyType type, int amount)
    {
        m_type = type;
        m_amount = Mathf.Max(0, amount);
    }

    public CurrencyCost ToCurrencyCost() => new CurrencyCost(Type, Amount);

    internal void EnsureValid()
    {
        m_amount = Mathf.Max(0, m_amount);
    }
}

/// <summary>
/// 제조 결과 아이템 하나를 만드는 정적 레시피 정의입니다.
/// </summary>
/// <remarks>
/// 생산 수량, 전체 비용, 작업 진행량과 실제 지불 비용은 런타임 작업이 소유합니다.
/// 작업 시작 시 이 정의의 ID, 결과 아이템 ID, 단위 작업량과 비용을 스냅샷으로 복사합니다.
/// </remarks>
[CreateAssetMenu(
    fileName = "ManufacturingRecipeDefinition",
    menuName = "GrayZone/Manufacturing/Recipe Definition")]
public sealed class ManufacturingRecipeDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("레시피 조회와 저장에 사용하는 변경되지 않는 고유 ID입니다.")]
    [SerializeField] private string m_recipeId = string.Empty;

    [Header("Result")]
    [SerializeField] private ItemDefinition m_resultItem;

    [Header("Requirements")]
    [Tooltip("플레이어에게 표시되는 시설 레벨 기준입니다. Lv.1~Lv.4")]
    [Range(1, 4)]
    [SerializeField] private int m_requiredFacilityLevel = 1;

    [Tooltip("결과 아이템 1개를 완성하는 데 필요한 작업량입니다.")]
    [Min(1)]
    [SerializeField] private int m_unitWork = 1;

    [Tooltip("결과 아이템 1개를 생산할 때 필요한 CurrencyType별 재료입니다.")]
    [SerializeField] private ManufacturingMaterialCost[] m_unitCosts = Array.Empty<ManufacturingMaterialCost>();

    public string RecipeId => m_recipeId;
    public ItemDefinition ResultItem => m_resultItem;
    public string ResultItemDefinitionId => m_resultItem != null
        ? m_resultItem.ItemDefinitionId
        : string.Empty;
    public string DisplayName => m_resultItem != null
        ? m_resultItem.DisplayName
        : name;
    public string Info => m_resultItem != null
        ? m_resultItem.Info
        : string.Empty;
    public Sprite Icon => m_resultItem != null ? m_resultItem.Icon : null;
    public ItemCategory Category => m_resultItem != null
        ? m_resultItem.Category
        : ItemCategory.None;
    public int RequiredFacilityLevel => Mathf.Clamp(m_requiredFacilityLevel, 1, 4);
    public int UnitWork => Mathf.Max(1, m_unitWork);
    public IReadOnlyList<ManufacturingMaterialCost> UnitCosts => m_unitCosts;

    /// <summary>
    /// 인스펙터의 단위 재료 항목을 기존 자원 비용 묶음으로 변환합니다.
    /// </summary>
    public CostBundle BuildUnitCost()
    {
        if (m_unitCosts == null || m_unitCosts.Length == 0)
            return new CostBundle();

        var costs = new List<CurrencyCost>(m_unitCosts.Length);
        foreach (ManufacturingMaterialCost cost in m_unitCosts)
        {
            if (cost != null && cost.Amount > 0)
                costs.Add(cost.ToCurrencyCost());
        }

        return new CostBundle(costs.ToArray());
    }

    private void OnValidate()
    {
        m_recipeId = m_recipeId?.Trim() ?? string.Empty;
        m_requiredFacilityLevel = Mathf.Clamp(m_requiredFacilityLevel, 1, 4);
        m_unitWork = Mathf.Max(1, m_unitWork);
        m_unitCosts ??= Array.Empty<ManufacturingMaterialCost>();

        foreach (ManufacturingMaterialCost cost in m_unitCosts)
            cost?.EnsureValid();
    }
}
