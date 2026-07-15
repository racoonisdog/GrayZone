using System;
using UnityEngine;

/// <summary>
/// 캐릭터 부상 상태에 대응하는 아이콘 스프라이트를 제공하는 카탈로그
/// </summary>
[CreateAssetMenu(menuName = "GrayZone/Character Injury Icon Catalog")]
public class CharacterInjuryIconCatalog : ScriptableObject
{
    [SerializeField] private Entry[] entries;

    /// <summary>
    /// 부상 상태에 연결된 아이콘을 반환
    /// </summary>
    /// <param name="state">조회할 캐릭터 부상 상태</param>
    /// <returns>연결된 아이콘이 있으면 해당 스프라이트, 없으면 <c>null</c></returns>
    public Sprite GetIcon(PlayerInjuryState state)
    {
        foreach (Entry entry in entries)
        {
            if (entry.state == state)
                return entry.icon;
        }

        return null;
    }

    [Serializable]
    private struct Entry
    {
        public PlayerInjuryState state;
        public Sprite icon;
    }
}
