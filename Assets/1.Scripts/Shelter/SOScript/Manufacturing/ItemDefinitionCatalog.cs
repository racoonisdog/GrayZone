using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 안정적인 item definition ID를 읽기 전용 정적 정의에 연결합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "ItemDefinitionCatalog",
    menuName = "GrayZone/Manufacturing/Item Definition Catalog")]
public sealed class ItemDefinitionCatalog : ScriptableObject
{
    [SerializeField] private ItemDefinition[] m_definitions =
        Array.Empty<ItemDefinition>();

    public int Count => m_definitions?.Length ?? 0;

    public bool TryGetDefinition(
        string itemDefinitionId,
        out ItemDefinition definition)
    {
        definition = null;
        string normalizedId = NormalizeId(itemDefinitionId);
        if (string.IsNullOrEmpty(normalizedId) || m_definitions == null)
            return false;

        for (int i = 0; i < m_definitions.Length; i++)
        {
            ItemDefinition candidate = m_definitions[i];
            if (candidate != null
                && string.Equals(
                    candidate.ItemDefinitionId,
                    normalizedId,
                    StringComparison.Ordinal))
            {
                definition = candidate;
                return true;
            }
        }

        return false;
    }

    public void FillDefinitions(List<ItemDefinition> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (m_definitions == null)
            return;

        for (int i = 0; i < m_definitions.Length; i++)
        {
            ItemDefinition definition = m_definitions[i];
            if (definition != null)
                results.Add(definition);
        }
    }

    private void OnValidate()
    {
        m_definitions ??= Array.Empty<ItemDefinition>();

        HashSet<string> registeredIds = new(StringComparer.Ordinal);
        for (int i = 0; i < m_definitions.Length; i++)
        {
            ItemDefinition definition = m_definitions[i];
            if (definition == null)
                continue;

            string id = NormalizeId(definition.ItemDefinitionId);
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning(
                    $"[{nameof(ItemDefinitionCatalog)}] "
                    + $"Definition entry {i} has no item definition ID.",
                    this);
                continue;
            }

            if (!registeredIds.Add(id))
            {
                Debug.LogWarning(
                    $"[{nameof(ItemDefinitionCatalog)}] "
                    + $"Duplicate item definition ID '{id}'.",
                    this);
            }
        }
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
