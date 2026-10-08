using System;
using UnityEngine;

/// <summary>
/// 캐릭터 정의 ID와 부상 상태에 대응하는 이미지를 제공하는 카탈로그.
/// <see cref="CharacterInjuryIconCatalog"/>가 상태별 공용 아이콘이라면, 이 카탈로그는 캐릭터마다 다른 부상 이미지를 담는다.
/// </summary>
[CreateAssetMenu(menuName = "GrayZone/Character Injury Portrait Catalog")]
public class CharacterInjuryPortraitCatalog : ScriptableObject
{
    [SerializeField] private Entry[] entries;

    /// <summary>
    /// 캐릭터와 부상 상태에 연결된 이미지를 반환
    /// </summary>
    /// <param name="definitionId">조회할 캐릭터 정의 ID</param>
    /// <param name="state">조회할 캐릭터 부상 상태</param>
    /// <returns>연결된 이미지가 있으면 해당 스프라이트, 없으면 <c>null</c></returns>
    public Sprite GetIcon(string definitionId, CharacterInjuryState state)
    {
        if (entries == null)
            return null;

        foreach (Entry entry in entries)
        {
            if (entry.definitionId != definitionId || entry.states == null)
                continue;

            foreach (StateIcon stateIcon in entry.states)
            {
                if (stateIcon.state == state)
                    return stateIcon.icon;
            }

            return null;
        }

        return null;
    }

    [Serializable]
    private struct Entry
    {
        public string definitionId;
        public StateIcon[] states;
    }

    [Serializable]
    private struct StateIcon
    {
        public CharacterInjuryState state;
        public Sprite icon;
    }
}
