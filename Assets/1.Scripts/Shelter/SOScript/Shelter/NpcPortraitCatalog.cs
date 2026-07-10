using System;
using UnityEngine;

/// <summary>
/// NPC 정의 ID에 대응하는 초상화 스프라이트를 제공하는 카탈로그
/// </summary>
[CreateAssetMenu(menuName = "GrayZone/NPC Portrait Catalog")]
public class NpcPortraitCatalog : ScriptableObject
{
    [SerializeField] private Entry[] entries;

    /// <summary>
    /// NPC 정의 ID에 연결된 초상화를 반환
    /// </summary>
    /// <param name="definitionId">조회할 NPC 정의 ID</param>
    /// <returns>연결된 초상화가 있으면 해당 스프라이트, 없으면 <c>null</c></returns>
    public Sprite GetPortrait(string definitionId)
    {
        foreach (Entry entry in entries)
        {
            if (entry.definitionId == definitionId)
                return entry.portrait;
        }

        return null;
    }

    [Serializable]
    private struct Entry
    {
        public string definitionId;
        public Sprite portrait;
    }
}
