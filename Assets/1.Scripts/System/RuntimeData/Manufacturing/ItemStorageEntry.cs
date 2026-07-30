using System;
using UnityEngine;

/// <summary>
/// 셸터 창고가 보유한 수량형 아이템 한 종류의 런타임 상태입니다.
/// </summary>
/// <remarks>
/// 정적 표시 정보는 <see cref="ItemDefinition"/>에서 조회하며,
/// 이 타입은 저장과 복사에 필요한 아이템 정의 ID와 총수량만 보관합니다.
/// </remarks>
[Serializable]
public sealed class ItemStorageEntry
{
    [SerializeField] private string m_itemDefinitionId = string.Empty;
    [Min(0)]
    [SerializeField] private int m_quantity;

    public string ItemDefinitionId => m_itemDefinitionId ?? string.Empty;
    public int Quantity => Math.Max(0, m_quantity);
    public bool IsValid => !string.IsNullOrWhiteSpace(ItemDefinitionId) && Quantity > 0;

    /// <summary>Unity 직렬화를 위한 기본 생성자</summary>
    public ItemStorageEntry()
    {
    }

    public ItemStorageEntry(string itemDefinitionId, int quantity)
    {
        m_itemDefinitionId = NormalizeItemDefinitionId(itemDefinitionId);
        m_quantity = Math.Max(0, quantity);
    }

    public ItemStorageEntry Clone()
    {
        return new ItemStorageEntry(ItemDefinitionId, Quantity);
    }

    /// <summary>로드하거나 외부 데이터를 적용한 뒤 ID와 수량을 유효 범위로 보정합니다.</summary>
    public void EnsureValid()
    {
        m_itemDefinitionId = NormalizeItemDefinitionId(m_itemDefinitionId);
        m_quantity = Math.Max(0, m_quantity);
    }

    /// <summary>
    /// 같은 아이템 ID의 수량을 증가시킵니다. ID 조회와 중복 항목 병합은 StorageFacility의 책임입니다.
    /// </summary>
    internal bool TryAddQuantity(int amount)
    {
        if (amount <= 0 || Quantity > int.MaxValue - amount)
            return false;

        m_quantity = Quantity + amount;
        return true;
    }

    /// <summary>
    /// 보유 수량 안에서 수량을 감소시킵니다. 0이 된 항목을 목록에서 제거하는 것은 StorageFacility의 책임입니다.
    /// </summary>
    internal bool TryRemoveQuantity(int amount)
    {
        if (amount <= 0 || Quantity < amount)
            return false;

        m_quantity = Quantity - amount;
        return true;
    }

    private static string NormalizeItemDefinitionId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
