using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 완료 결과를 창고 아이템 또는 셸터 공용 자원 중 어디에 입고할지 구분합니다.
/// </summary>
public enum ManufacturingResultKind
{
    Item = 0,
    Resource = 1
}

/// <summary>
/// 제조 레시피 1회 실행(배치)당 재료 비용 항목입니다.
/// </summary>
[Serializable]
public sealed class ManufacturingMaterialCost
{
    [Tooltip("재료로 소비할 안정적인 자원 ID입니다.")]
    [SerializeField] private string m_resourceId = string.Empty;
    [Min(0)]
    [SerializeField] private int m_amount;

    public string ResourceId => ResourceIds.Normalize(m_resourceId);
    public int Amount => Mathf.Max(0, m_amount);

    public ManufacturingMaterialCost()
    {
    }

    public ManufacturingMaterialCost(string resourceId, int amount)
    {
        m_resourceId = ResourceIds.Normalize(resourceId);
        m_amount = Mathf.Max(0, amount);
    }

    public ResourceCost ToResourceCost() =>
        new ResourceCost(ResourceId, Amount);

    internal void EnsureValid()
    {
        m_resourceId = ResourceIds.Normalize(m_resourceId);
        m_amount = Mathf.Max(0, m_amount);
    }
}

/// <summary>
/// 제조 결과 묶음 하나를 만드는 정적 레시피 정의입니다.
/// </summary>
/// <remarks>
/// 요청 배치 수, 전체 비용, 작업 진행량과 실제 지불 비용은 런타임 작업이 소유합니다.
/// 작업 시작 시 이 정의의 ID, 결과 종류와 ID, 배치당 결과 수량, 단위 작업량과 비용을 스냅샷으로 복사합니다.
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
    [Tooltip("완료 결과를 창고 아이템 또는 셸터 공용 자원 중 어디에 입고할지 선택합니다.")]
    [SerializeField] private ManufacturingResultKind m_resultKind =
        ManufacturingResultKind.Item;
    [Tooltip("Item 결과 정의이며 Resource 결과에서도 목록 이름, 설명, 아이콘 표시용으로 사용합니다.")]
    [SerializeField] private ItemDefinition m_resultItem;
    [Tooltip("Resource 결과일 때 증가시킬 안정적인 자원 ID입니다.")]
    [SerializeField] private string m_resultResourceId = string.Empty;
    [Tooltip("레시피를 한 번 실행했을 때 생산되는 결과 수량입니다.")]
    [Min(1)]
    [SerializeField] private int m_resultQuantityPerBatch = 1;

    [Header("Requirements")]
    [Tooltip("플레이어에게 표시되는 해금 시설 레벨 기준입니다. Lv.1~Lv.4")]
    [Range(1, 4)]
    [SerializeField] private int m_requiredFacilityLevel = 1;

    [Tooltip("레시피를 한 번 실행하는 데 필요한 작업량입니다.")]
    [Min(1)]
    [SerializeField] private int m_unitWork = 1;

    [Tooltip("레시피를 한 번 실행할 때 필요한 자원 ID별 재료입니다.")]
    [SerializeField] private ManufacturingMaterialCost[] m_unitCosts = Array.Empty<ManufacturingMaterialCost>();

    public string RecipeId => m_recipeId;
    public ManufacturingResultKind ResultKind => m_resultKind;
    public ItemDefinition ResultItem => m_resultItem;
    public int ResultQuantityPerBatch => Mathf.Max(1, m_resultQuantityPerBatch);
    public string ResultItemDefinitionId =>
        ResultKind == ManufacturingResultKind.Item && m_resultItem != null
            ? m_resultItem.ItemDefinitionId
            : string.Empty;
    public string ResultResourceId =>
        ResultKind == ManufacturingResultKind.Resource
            ? ResourceIds.Normalize(m_resultResourceId)
            : string.Empty;
    public string ResultDefinitionId =>
        ResultKind == ManufacturingResultKind.Resource
            ? ResultResourceId
            : ResultItemDefinitionId;
    public bool HasValidResult =>
        ResultKind == ManufacturingResultKind.Resource
            ? ResourceIds.IsDefined(ResultResourceId)
            : !string.IsNullOrWhiteSpace(ResultItemDefinitionId);
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

        var costs = new List<ResourceCost>(m_unitCosts.Length);
        foreach (ManufacturingMaterialCost cost in m_unitCosts)
        {
            if (cost != null
                && !string.IsNullOrEmpty(cost.ResourceId)
                && cost.Amount > 0)
            {
                costs.Add(cost.ToResourceCost());
            }
        }

        return new CostBundle(costs.ToArray());
    }

    private void OnValidate()
    {
        m_recipeId = m_recipeId?.Trim() ?? string.Empty;
        if (m_resultKind != ManufacturingResultKind.Item
            && m_resultKind != ManufacturingResultKind.Resource)
        {
            m_resultKind = ManufacturingResultKind.Item;
        }

        m_resultResourceId = ResourceIds.Normalize(m_resultResourceId);
        m_resultQuantityPerBatch = Mathf.Max(1, m_resultQuantityPerBatch);
        m_requiredFacilityLevel = Mathf.Clamp(m_requiredFacilityLevel, 1, 4);
        m_unitWork = Mathf.Max(1, m_unitWork);
        m_unitCosts ??= Array.Empty<ManufacturingMaterialCost>();

        foreach (ManufacturingMaterialCost cost in m_unitCosts)
            cost?.EnsureValid();
    }
}
