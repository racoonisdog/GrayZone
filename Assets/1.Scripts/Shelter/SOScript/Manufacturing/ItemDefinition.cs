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

    [Header("Presentation")]
    [SerializeField] private string m_displayName = string.Empty;
    [SerializeField] private Sprite m_icon;
    [SerializeField] private ItemCategory m_category = ItemCategory.Miscellaneous;

    public string ItemDefinitionId => m_itemDefinitionId;
    public string DisplayName => string.IsNullOrWhiteSpace(m_displayName) ? name : m_displayName;
    public Sprite Icon => m_icon;
    public ItemCategory Category => m_category;

    private void OnValidate()
    {
        m_itemDefinitionId = m_itemDefinitionId?.Trim() ?? string.Empty;
        m_displayName = m_displayName?.Trim() ?? string.Empty;
    }
}
