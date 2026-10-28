using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터 시설(의료실·제조실 등) 하나의 레벨별 업그레이드 비용과 효과 문구를 모아 둔 표입니다.
/// </summary>
/// <remarks>
/// 정본은 시설별 테이블 시트(<c>Assets/5.Data/DataSheet/TableData/MedicalUpgradeLevel.csv</c> 등)이고,
/// 시설마다 에셋을 하나씩 둡니다. Inspector에서 고친 값은 다음 시트 가져오기에서 덮어씁니다.
///
/// <c>Level</c>은 화면에 보이는 <b>목표 레벨</b>입니다. 시설은 Lv.1에서 시작하므로 첫 행은 Lv.2이고,
/// 가장 높은 Level이 최대 레벨입니다. 시설 내부 레벨(0부터)이 <c>n</c>이면 다음 업그레이드 행은 <c>n + 2</c>입니다.
///
/// Effect 문구는 표시용입니다. 실제 슬롯·보너스 수치는 각 시설 코드가 정본이므로, 효과를 바꾸면 함께 맞춰야 합니다.
/// </remarks>
[CreateAssetMenu(fileName = "FacilityUpgradeTable", menuName = "GrayZone/Shelter/Facility Upgrade Table")]
public sealed class FacilityUpgradeTableSO : ScriptableObject
{
    /// <summary>시설 업그레이드 시트 한 행입니다.</summary>
    [Serializable]
    public struct LevelEntry
    {
        [Tooltip("이 행이 나타내는 목표 레벨입니다(화면 표기 기준, 2부터).")]
        [Min(2)] public int Level;
        [Tooltip("이 레벨로 올릴 때 소비하는 자원 ID입니다.")]
        public string ResourceId;
        [Tooltip("이 레벨로 올릴 때 소비하는 자원 수량입니다.")]
        [Min(0)] public int Amount;
        [TextArea] public string Effect;
    }

    /// <summary>시트에 적을 수 있는 첫 목표 레벨입니다. Lv.1은 시작 레벨이라 행이 없습니다.</summary>
    public const int FirstLevel = 2;

    [SerializeField] private List<LevelEntry> m_levels = new List<LevelEntry>();

    /// <summary>시트에 적힌 최대 레벨(화면 표기 기준)을 찾습니다. 행이 없으면 false입니다.</summary>
    public bool TryGetMaxLevel(out int maxLevel)
    {
        maxLevel = 0;
        foreach (LevelEntry candidate in m_levels)
            maxLevel = Mathf.Max(maxLevel, candidate.Level);

        return maxLevel >= FirstLevel;
    }

    /// <summary>지정한 목표 레벨(화면 표기 기준)의 행을 찾습니다. 행이 없으면 false입니다.</summary>
    public bool TryGetLevel(int level, out LevelEntry entry)
    {
        foreach (LevelEntry candidate in m_levels)
        {
            if (candidate.Level == level)
            {
                entry = candidate;
                entry.ResourceId = ResourceIds.Normalize(entry.ResourceId);
                entry.Amount = Mathf.Max(0, entry.Amount);
                entry.Effect ??= string.Empty;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>지정한 목표 레벨로 올리는 비용을 자원 묶음으로 찾습니다. 행이 없으면 false입니다.</summary>
    public bool TryGetCost(int level, out CostBundle cost)
    {
        if (!TryGetLevel(level, out LevelEntry entry))
        {
            cost = new CostBundle();
            return false;
        }

        cost = entry.Amount > 0 && entry.ResourceId.Length > 0
            ? new CostBundle(new ResourceCost(entry.ResourceId, entry.Amount))
            : new CostBundle();
        return true;
    }

#if UNITY_EDITOR
    /// <summary>시트 임포터 전용입니다. 검증을 마친 시트 행으로 통째로 바꿉니다.</summary>
    public void EditorReplaceLevels(List<LevelEntry> rows)
    {
        m_levels = rows;
    }
#endif
}
