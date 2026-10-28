using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터 Scramble 업그레이드의 레벨별 비용과 Info 문구를 모아 둔 표입니다.
/// </summary>
/// <remarks>
/// 정본은 테이블 시트 <c>Assets/5.Data/DataSheet/TableData/ScrambleUpgradeLevel.csv</c>입니다.
/// 업그레이드·레벨 하나당 한 행이며, Inspector에서 고친 값은 다음 시트 가져오기에서 덮어씁니다.
/// 업그레이드 이름·아이콘·설명은 씬의 카드에 있습니다.
///
/// 레벨 행의 비용은 그 레벨로 올릴 때 드는 비용이고, 한 업그레이드의 가장 높은 Level이 최대 레벨입니다.
/// 레벨 행이 없는 업그레이드는 <see cref="ScrambleFacility"/>의 기존 단일 비용·최대 레벨 필드를 씁니다.
///
/// 방어전에서 실제로 적용되는 강화 수치(범위 배율, 내구도 등)는 <see cref="TrapUpgradeTableSO"/>가 정본입니다.
/// Info 문구는 표시용이므로, 수치를 바꾸면 두 표를 함께 맞춰야 합니다.
/// </remarks>
[CreateAssetMenu(fileName = "ScrambleUpgradeTable", menuName = "GrayZone/Shelter/Scramble Upgrade Table")]
public sealed class ScrambleUpgradeTableSO : ScriptableObject
{
    /// <summary><c>ScrambleUpgradeLevel</c> 시트 한 행입니다.</summary>
    [Serializable]
    public struct LevelEntry
    {
        public ScrambleUpgradeType UpgradeId;
        [Min(1)] public int Level;
        [Tooltip("이 레벨로 올릴 때 소비하는 업그레이드 자원 수량입니다.")]
        [Min(0)] public int Cost;
        [TextArea] public string Info;
    }

    [SerializeField] private List<LevelEntry> m_levels = new List<LevelEntry>();

    /// <summary>시트에 적힌 최대 레벨을 찾습니다. 해당 업그레이드의 레벨 행이 없으면 false입니다.</summary>
    public bool TryGetMaxLevel(ScrambleUpgradeType upgradeId, out int maxLevel)
    {
        maxLevel = 0;
        foreach (LevelEntry candidate in m_levels)
        {
            if (candidate.UpgradeId == upgradeId)
                maxLevel = Mathf.Max(maxLevel, candidate.Level);
        }

        return maxLevel > 0;
    }

    /// <summary>지정한 레벨(1부터)의 비용과 문구를 찾습니다. 행이 없으면 false입니다.</summary>
    public bool TryGetLevel(ScrambleUpgradeType upgradeId, int level, out LevelEntry entry)
    {
        foreach (LevelEntry candidate in m_levels)
        {
            if (candidate.UpgradeId == upgradeId && candidate.Level == level)
            {
                entry = candidate;
                entry.Cost = Mathf.Max(0, entry.Cost);
                entry.Info ??= string.Empty;
                return true;
            }
        }

        entry = default;
        return false;
    }

#if UNITY_EDITOR
    /// <summary>시트 임포터 전용입니다. 검증을 마친 <c>ScrambleUpgradeLevel</c> 시트 행으로 통째로 바꿉니다.</summary>
    public void EditorReplaceLevels(List<LevelEntry> rows)
    {
        m_levels = rows;
    }
#endif
}
