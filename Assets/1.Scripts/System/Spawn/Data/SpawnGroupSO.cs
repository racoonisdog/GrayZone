using System;
using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>
/// 한 번의 스폰에 함께 나오는 적 묶음입니다. [항목 SO + 마릿수] 목록으로 구성합니다.
/// </summary>
/// <remarks>
/// 방어전 스포너가 스폰 시점마다 웨이브 SO의 후보 중 하나를 골라 이 구성을 통째로 생성합니다.
/// 그룹 밸류는 Σ(적의 위험도 × 마릿수)이며, 그룹끼리 위협 수준을 비교하는 기준입니다.
/// 여러 공격로, 웨이브, 방어전에서 같은 그룹을 재사용할 수 있습니다.
/// 설계 근거: privateDoc `DEFENSE_WAVE_SPAWN_SPEC_KR.md` §4.1.
/// </remarks>
[CreateAssetMenu(fileName = "SpawnGroup_Name", menuName = "GrayZone/Defense/Spawn Group")]
public sealed class SpawnGroupSO : ScriptableObject
{
    /// <summary>그룹에 들어가는 항목 하나와 그 마릿수입니다.</summary>
    [Serializable]
    public struct Member
    {
        [Tooltip("생성할 적과 생성 시 덮어쓸 값을 가진 항목 SO입니다.")]
        [SerializeField] private EnemySpawnEntrySO m_entry;

        [Tooltip("이 항목을 한 번의 스폰에 몇 마리 생성할지입니다.")]
        [Min(1)]
        [SerializeField] private int m_count;

        /// <summary>생성할 항목 SO입니다.</summary>
        public EnemySpawnEntrySO Entry => m_entry;

        /// <summary>한 번의 스폰에 생성할 마릿수입니다. 1보다 작지 않습니다.</summary>
        public int Count => Mathf.Max(1, m_count);

        /// <summary>이 항목 한 줄의 밸류입니다. 항목이나 프리팹이 비어 있으면 0입니다.</summary>
        public int Value => m_entry != null && m_entry.EnemyPrefab != null ? m_entry.EnemyPrefab.DangerLevel * Count : 0;

        /// <summary>Inspector 입력의 하한을 보정한 복사본을 돌려줍니다.</summary>
        internal Member Sanitized()
        {
            Member copy = this;
            copy.m_count = Mathf.Max(1, m_count);
            return copy;
        }
    }

    [Header("Members")]
    [Tooltip("한 번의 스폰에 함께 생성할 항목과 마릿수입니다. 항목이 비어 있는 줄은 생성하지 않습니다.")]
    [SerializeField] private List<Member> m_members = new List<Member>();

    /// <summary>그룹 구성입니다.</summary>
    public IReadOnlyList<Member> Members => m_members;

    /// <summary>그룹 밸류입니다. Σ(적의 위험도 × 마릿수)입니다.</summary>
    [ShowInInspector]
    public int TotalValue
    {
        get
        {
            int total = 0;
            for (int i = 0; i < m_members.Count; i++)
            {
                total += m_members[i].Value;
            }

            return total;
        }
    }

    /// <summary>한 번의 스폰에 생성되는 전체 마릿수입니다. 항목이 비어 있는 줄은 세지 않습니다.</summary>
    [ShowInInspector]
    public int TotalCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < m_members.Count; i++)
            {
                if (m_members[i].Entry != null)
                {
                    total += m_members[i].Count;
                }
            }

            return total;
        }
    }

    /// <summary>생성할 수 있는 항목이 하나라도 있는지 여부입니다.</summary>
    public bool HasSpawnableMember => TotalCount > 0;

    /// <summary>이 그룹에 쓰이는 항목 SO를 목록에 더합니다. 풀을 미리 만들 때 씁니다.</summary>
    /// <param name="entries">항목을 추가할 집합입니다.</param>
    public void CollectEntries(HashSet<EnemySpawnEntrySO> entries)
    {
        for (int i = 0; i < m_members.Count; i++)
        {
            if (m_members[i].Entry != null)
            {
                entries.Add(m_members[i].Entry);
            }
        }
    }

    /// <summary>마릿수가 1보다 작게 입력되지 않도록 보정합니다.</summary>
    private void OnValidate()
    {
        for (int i = 0; i < m_members.Count; i++)
        {
            m_members[i] = m_members[i].Sanitized();
        }
    }
}
