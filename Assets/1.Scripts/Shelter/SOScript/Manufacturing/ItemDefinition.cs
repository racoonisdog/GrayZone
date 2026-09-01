using UnityEngine;

/// <summary>
/// 제조 레시피 목록 필터링에 사용하는 임시 아이템 분류입니다.
/// </summary>
public enum ItemCategory
{
    None = 0,
    MedicalSupply = 1,
    WeaponPart = 2,
    Miscellaneous = 3
}

/// <summary>
/// 제조 결과와 창고 표시에서 공유하는 임시 아이템 정적 정의입니다.
/// </summary>
/// <remarks>
/// 현재 제조 범위에서는 동일한 <see cref="ItemDefinitionId"/>를 가진 아이템을 수량형으로 취급합니다.
/// 개별 런타임 ID, 내구도, 품질, 랜덤 옵션은 이 정의의 책임이 아닙니다.
/// </remarks>
[CreateAssetMenu(fileName = "ItemDefinition", menuName = "GrayZone/Manufacturing/Item Definition")]
public sealed class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("저장과 레시피 참조에 사용하는 변경되지 않는 고유 ID입니다.")]
    [SerializeField] private string m_itemDefinitionId = string.Empty;

    [Header("Inventory")]
    [Tooltip("한 슬롯에 쌓을 수 있는 최대 수량입니다. 이 수량을 넘는 분량은 다음 슬롯에 새 스택으로 들어갑니다.")]
    [Min(1)]
    [SerializeField] private int m_maxStackQuantity = 99;

    [Header("Presentation")]
    [SerializeField] private string m_displayName = string.Empty;
    [TextArea(2, 6)]
    [SerializeField] private string m_info = string.Empty;
    [SerializeField] private Sprite m_icon;
    [SerializeField] private ItemCategory m_category = ItemCategory.Miscellaneous;

    public string ItemDefinitionId => m_itemDefinitionId;

    /// <summary>한 슬롯에 쌓을 수 있는 최대 수량입니다. 최소 1을 보장합니다.</summary>
    /// <remarks>
    /// 이 값을 정의에 둔 이유는 "아이템별 최대 스택 수량"(공용 `인벤토리 시스템` §17.4)의 정본이 하나여야
    /// 필드와 셸터가 같은 아이템을 다르게 쌓지 않기 때문입니다. 이 필드가 추가되기 전에 만든 에셋은
    /// YAML에 키가 없어 C# 초기값 99로 올라옵니다. 값 유실이 아니며, 밸런스 확정 시 각 에셋에서 지정하면 됩니다.
    /// </remarks>
    public int MaxStackQuantity => Mathf.Max(1, m_maxStackQuantity);
    public string DisplayName => string.IsNullOrWhiteSpace(m_displayName) ? name : m_displayName;
    public string Info => m_info;
    public Sprite Icon => m_icon;
    public ItemCategory Category => m_category;

    private void OnValidate()
    {
        m_itemDefinitionId = m_itemDefinitionId?.Trim() ?? string.Empty;
        m_displayName = m_displayName?.Trim() ?? string.Empty;
        m_info = m_info?.Trim() ?? string.Empty;
    }
}
