using System;
using System.Collections.Generic;
using UnityEngine;

public enum ShelterInventoryDisplayEntryKind
{
    Item = 0,
    Resource = 1
}

/// <summary>
/// 창고 아이템과 셸터 자원을 같은 읽기 전용 슬롯에 표시하기 위한 UI 값입니다.
/// 저장 구조나 도메인 명령을 소유하지 않습니다.
/// </summary>
public readonly struct ShelterInventoryDisplayEntry
{
    public ShelterInventoryDisplayEntryKind Kind { get; }
    public string StableKey { get; }
    public string DisplayName { get; }
    public Sprite Icon { get; }
    public int Quantity { get; }
    public string ItemDefinitionId { get; }
    public string ResourceId { get; }

    private ShelterInventoryDisplayEntry(
        ShelterInventoryDisplayEntryKind kind,
        string stableKey,
        string displayName,
        Sprite icon,
        int quantity,
        string itemDefinitionId,
        string resourceId)
    {
        Kind = kind;
        StableKey = stableKey ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        Icon = icon;
        Quantity = Math.Max(0, quantity);
        ItemDefinitionId = itemDefinitionId ?? string.Empty;
        ResourceId = ResourceIds.Normalize(resourceId);
    }

    public static ShelterInventoryDisplayEntry FromItem(
        ItemDefinition definition,
        int quantity)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        string itemDefinitionId = definition.ItemDefinitionId;
        return new ShelterInventoryDisplayEntry(
            ShelterInventoryDisplayEntryKind.Item,
            $"item:{itemDefinitionId}",
            definition.DisplayName,
            definition.Icon,
            quantity,
            itemDefinitionId,
            string.Empty);
    }

    public static ShelterInventoryDisplayEntry FromResource(
        ResourcePresentation presentation,
        int quantity)
    {
        return new ShelterInventoryDisplayEntry(
            ShelterInventoryDisplayEntryKind.Resource,
            $"resource:{presentation.ResourceId}",
            presentation.DisplayName,
            presentation.Icon,
            quantity,
            string.Empty,
            presentation.ResourceId);
    }
}

/// <summary>
/// StorageFacility의 두 데이터 원천을 하나의 읽기 전용 인벤토리 표시 목록으로 투영합니다.
/// </summary>
public sealed class ShelterInventoryProjectionBuilder
{
    private readonly List<ResourcePresentation> m_resourcePresentations =
        new();
    private readonly HashSet<string> m_registeredItemIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> m_registeredResourceIds =
        new(StringComparer.Ordinal);

    /// <returns>모든 창고 아이템의 정적 정의를 찾았으면 true입니다.</returns>
    public bool FillEntries(
        StorageFacility storage,
        ItemDefinitionCatalog itemCatalog,
        ResourceDefinitionCatalog resourceCatalog,
        List<ShelterInventoryDisplayEntry> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        m_registeredItemIds.Clear();
        m_registeredResourceIds.Clear();

        if (storage == null || itemCatalog == null || resourceCatalog == null)
            return false;

        // 고정 자원 슬롯을 먼저 유지하고, 새로 획득한 제작 아이템은
        // ItemStorageEntries의 획득 순서대로 그 뒤에 추가합니다.
        FillResourceEntries(storage, resourceCatalog, results);
        bool allItemsResolved = FillItemEntries(
            storage,
            itemCatalog,
            results);
        return allItemsResolved;
    }

    private bool FillItemEntries(
        StorageFacility storage,
        ItemDefinitionCatalog itemCatalog,
        List<ShelterInventoryDisplayEntry> results)
    {
        bool allItemsResolved = true;
        IReadOnlyList<ItemStorageEntry> items = storage.ItemStorageEntries;
        for (int i = 0; i < items.Count; i++)
        {
            ItemStorageEntry item = items[i];
            if (item == null || item.Quantity <= 0)
                continue;

            string itemDefinitionId = item.ItemDefinitionId;
            if (string.IsNullOrWhiteSpace(itemDefinitionId)
                || !m_registeredItemIds.Add(itemDefinitionId)
                || !itemCatalog.TryGetDefinition(
                    itemDefinitionId,
                    out ItemDefinition definition))
            {
                allItemsResolved = false;
                continue;
            }

            results.Add(ShelterInventoryDisplayEntry.FromItem(
                definition,
                item.Quantity));
        }

        return allItemsResolved;
    }

    private void FillResourceEntries(
        StorageFacility storage,
        ResourceDefinitionCatalog resourceCatalog,
        List<ShelterInventoryDisplayEntry> results)
    {
        resourceCatalog.FillPresentations(m_resourcePresentations);
        for (int i = 0; i < m_resourcePresentations.Count; i++)
        {
            ResourcePresentation presentation =
                m_resourcePresentations[i];
            if (!m_registeredResourceIds.Add(presentation.ResourceId))
                continue;

            results.Add(ShelterInventoryDisplayEntry.FromResource(
                presentation,
                storage.GetResourceAmount(presentation.ResourceId)));
        }
    }
}
