using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터 UI에서 사용하는 자원 표시 정보를 전달하는 불변 값입니다.
/// </summary>
public readonly struct ResourcePresentation
{
    public string ResourceId { get; }
    public string DisplayName { get; }
    public Sprite Icon { get; }

    public ResourcePresentation(
        string resourceId,
        string displayName,
        Sprite icon)
    {
        ResourceId = ResourceIds.Normalize(resourceId);
        DisplayName = displayName ?? string.Empty;
        Icon = icon;
    }
}

/// <summary>
/// 안정적인 자원 ID를 셸터 UI용 이름과 아이콘에 연결하는 공유 카탈로그입니다.
/// </summary>
/// <remarks>
/// 자원 수량은 소유하지 않습니다. 사용하는 UI가 동일한 에셋을 Inspector로 참조하고,
/// 실제 보유량은 <see cref="StorageFacility"/>에서 조회해야 합니다.
/// </remarks>
[CreateAssetMenu(
    fileName = "ResourceDefinitionCatalog",
    menuName = "GrayZone/Shelter/Resource Definition Catalog")]
public sealed class ResourceDefinitionCatalog : ScriptableObject
{
    [SerializeField] private Entry[] m_entries = Array.Empty<Entry>();

    public int Count => m_entries?.Length ?? 0;

    /// <summary>
    /// 자원 타입에 대응하는 표시 정보를 반환합니다.
    /// </summary>
    public bool TryGetPresentation(
        string resourceId,
        out ResourcePresentation presentation)
    {
        string normalizedId = ResourceIds.Normalize(resourceId);
        if (string.IsNullOrEmpty(normalizedId))
        {
            presentation = default;
            return false;
        }

        if (m_entries != null)
        {
            for (int i = 0; i < m_entries.Length; i++)
            {
                Entry entry = m_entries[i];
                if (!string.Equals(
                        entry.ResourceId,
                        normalizedId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                presentation = new ResourcePresentation(
                    entry.ResourceId,
                    entry.DisplayName,
                    entry.Icon);
                return true;
            }
        }

        presentation = default;
        return false;
    }

    /// <summary>
    /// 카탈로그에 등록된 자원 표시 정보를 Inspector 순서대로 호출자 목록에 채웁니다.
    /// </summary>
    public void FillPresentations(
        List<ResourcePresentation> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (m_entries == null)
            return;

        for (int i = 0; i < m_entries.Length; i++)
        {
            Entry entry = m_entries[i];
            results.Add(new ResourcePresentation(
                entry.ResourceId,
                entry.DisplayName,
                entry.Icon));
        }
    }

    private void OnValidate()
    {
        if (m_entries == null)
        {
            m_entries = Array.Empty<Entry>();
            return;
        }

        HashSet<string> registeredIds = new(StringComparer.Ordinal);
        for (int i = 0; i < m_entries.Length; i++)
        {
            Entry entry = m_entries[i];
            entry.Normalize();
            m_entries[i] = entry;

            if (string.IsNullOrEmpty(entry.ResourceId))
            {
                Debug.LogWarning(
                    $"[{nameof(ResourceDefinitionCatalog)}] "
                    + $"Entry {i} has no resource ID.",
                    this);
            }
            else if (!registeredIds.Add(entry.ResourceId))
            {
                Debug.LogWarning(
                    $"[{nameof(ResourceDefinitionCatalog)}] "
                    + $"Duplicate presentation for '{entry.ResourceId}'.",
                    this);
            }
        }
    }

    [Serializable]
    private struct Entry
    {
        [SerializeField] private string m_resourceId;
        [SerializeField] private string m_displayName;
        [SerializeField] private Sprite m_icon;

        public string ResourceId => m_resourceId ?? string.Empty;
        public string DisplayName => m_displayName ?? string.Empty;
        public Sprite Icon => m_icon;

        public void Normalize()
        {
            m_resourceId = ResourceIds.Normalize(m_resourceId);
            m_displayName = m_displayName?.Trim() ?? string.Empty;
        }
    }
}
