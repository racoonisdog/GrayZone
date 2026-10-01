using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>
/// 공격로 하나가 웨이브 한 번 동안 무엇을 어떤 간격으로 내보낼지 정합니다.
/// </summary>
/// <remarks>
/// 방어전 스포너는 전투가 시작되면 첫 스폰 대기를 한 번 뽑고, 이후 스폰할 때마다 후보 그룹 중 하나를
/// 무작위(균등)로 골라 생성한 뒤 다음 간격을 다시 뽑습니다. 정문은 최소=최대로 두면 일정한 간격이 되고,
/// 터널은 범위를 넓게 두면 비정기적으로 나옵니다.
///
/// 전투·휴식 시간은 여기에 두지 않습니다. 모든 공격로에 일괄 적용되도록 DefenseManager가 가집니다.
/// 설계 근거: privateDoc `DEFENSE_WAVE_SPAWN_SPEC_KR.md` §4.2.
/// </remarks>
[CreateAssetMenu(fileName = "DefenseWave_Name", menuName = "GrayZone/Defense/Defense Wave")]
public sealed class DefenseWaveSO : ScriptableObject
{
    [Header("Groups")]
    [Tooltip("이 웨이브에 스폰할 때마다 하나를 무작위로 고를 후보 그룹입니다. 비어 있거나 생성할 항목이 없는 그룹은 고르지 않습니다.")]
    [SerializeField] private List<SpawnGroupSO> m_groups = new List<SpawnGroupSO>();

    [Header("Timing")]
    [Tooltip("전투 시작 뒤 첫 그룹을 내보내기까지 기다릴 최소 시간(초)입니다. 터널처럼 늦게 열리는 공격로는 크게 둡니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_firstSpawnDelayMin;

    [Tooltip("전투 시작 뒤 첫 그룹을 내보내기까지 기다릴 최대 시간(초)입니다. 최솟값보다 작게 입력하면 최솟값으로 보정됩니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_firstSpawnDelayMax;

    [Tooltip("그룹을 내보낸 뒤 다음 그룹까지 기다릴 최소 시간(초)입니다. 최소와 최대를 같게 두면 일정한 간격이 됩니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_spawnIntervalMin = 10.0f;

    [Tooltip("그룹을 내보낸 뒤 다음 그룹까지 기다릴 최대 시간(초)입니다. 최솟값보다 작게 입력하면 최솟값으로 보정됩니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_spawnIntervalMax = 10.0f;

    [Header("Preview")]
    [Tooltip("아래 예상 수치를 계산할 때 쓰는 전투 시간(초)입니다. 에디터 표시용이며 실제 전투 시간은 DefenseManager가 정합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_previewCombatDuration = 60.0f;

    /// <summary>후보 그룹 목록입니다.</summary>
    public IReadOnlyList<SpawnGroupSO> Groups => m_groups;

    /// <summary>후보 그룹의 평균 그룹 밸류입니다. 고를 수 있는 그룹이 없으면 0입니다.</summary>
    [ShowInInspector]
    public float AverageGroupValue
    {
        get
        {
            int sum = 0;
            int count = 0;
            for (int i = 0; i < m_groups.Count; i++)
            {
                SpawnGroupSO group = m_groups[i];
                if (IsPickable(group))
                {
                    sum += group.TotalValue;
                    count++;
                }
            }

            return count > 0 ? (float)sum / count : 0.0f;
        }
    }

    /// <summary>미리보기 전투 시간 동안 예상되는 스폰 횟수 범위입니다. 상한에 막히는 경우는 반영하지 않습니다.</summary>
    [ShowInInspector]
    public string PreviewSpawnCountRange
    {
        get
        {
            GetPreviewSpawnCountRange(m_previewCombatDuration, out int min, out int max);
            return min == max ? min.ToString() : $"{min} ~ {max}";
        }
    }

    /// <summary>미리보기 전투 시간 동안 예상되는 총 밸류 범위입니다(평균 그룹 밸류 × 스폰 횟수).</summary>
    [ShowInInspector]
    public string PreviewTotalValueRange
    {
        get
        {
            GetPreviewSpawnCountRange(m_previewCombatDuration, out int min, out int max);
            float average = AverageGroupValue;
            float low = average * min;
            float high = average * max;
            return Mathf.Approximately(low, high) ? low.ToString("0.#") : $"{low:0.#} ~ {high:0.#}";
        }
    }

    /// <summary>이번 전투에서 첫 그룹까지 기다릴 시간(초)을 뽑습니다.</summary>
    public float SampleFirstSpawnDelay()
    {
        float min = Mathf.Max(0.0f, m_firstSpawnDelayMin);
        float max = Mathf.Max(min, m_firstSpawnDelayMax);
        return Random.Range(min, max);
    }

    /// <summary>다음 그룹까지 기다릴 시간(초)을 뽑습니다. 스폰할 때마다 다시 부릅니다.</summary>
    public float SampleSpawnInterval()
    {
        float min = Mathf.Max(0.1f, m_spawnIntervalMin);
        float max = Mathf.Max(min, m_spawnIntervalMax);
        return Random.Range(min, max);
    }

    /// <summary>
    /// 후보 중 하나를 균등한 확률로 고릅니다.
    /// </summary>
    /// <returns>고른 그룹입니다. 고를 수 있는 그룹이 없으면 null입니다.</returns>
    /// <remarks>비어 있거나 생성할 항목이 없는 그룹은 후보에서 빼고 고릅니다. 같은 그룹이 연달아 나올 수 있습니다.</remarks>
    public SpawnGroupSO PickGroup()
    {
        int pickable = 0;
        for (int i = 0; i < m_groups.Count; i++)
        {
            if (IsPickable(m_groups[i]))
            {
                pickable++;
            }
        }

        if (pickable == 0)
        {
            return null;
        }

        int target = Random.Range(0, pickable);
        for (int i = 0; i < m_groups.Count; i++)
        {
            if (!IsPickable(m_groups[i]))
            {
                continue;
            }

            if (target == 0)
            {
                return m_groups[i];
            }

            target--;
        }

        return null;
    }

    /// <summary>이 웨이브의 후보 그룹에 쓰이는 항목 SO를 집합에 더합니다. 풀을 미리 만들 때 씁니다.</summary>
    /// <param name="entries">항목을 추가할 집합입니다.</param>
    public void CollectEntries(HashSet<EnemySpawnEntrySO> entries)
    {
        for (int i = 0; i < m_groups.Count; i++)
        {
            m_groups[i]?.CollectEntries(entries);
        }
    }

    /// <summary>
    /// 주어진 전투 시간 동안 스폰 횟수의 최소·최대를 구합니다.
    /// </summary>
    /// <remarks>
    /// 첫 스폰은 첫 대기 뒤에, 이후는 간격마다 일어난다고 봅니다. 최소는 가장 긴 대기와 간격, 최대는 가장 짧은
    /// 대기와 간격을 씁니다. 전투 시간이 끝나는 순간과 겹치는 스폰은 포함합니다.
    /// </remarks>
    private void GetPreviewSpawnCountRange(float duration, out int min, out int max)
    {
        float firstMin = Mathf.Max(0.0f, m_firstSpawnDelayMin);
        float firstMax = Mathf.Max(firstMin, m_firstSpawnDelayMax);
        float intervalMin = Mathf.Max(0.1f, m_spawnIntervalMin);
        float intervalMax = Mathf.Max(intervalMin, m_spawnIntervalMax);

        min = CountSpawns(duration, firstMax, intervalMax);
        max = CountSpawns(duration, firstMin, intervalMin);
    }

    private static int CountSpawns(float duration, float firstDelay, float interval)
    {
        if (duration < firstDelay)
        {
            return 0;
        }

        return Mathf.FloorToInt((duration - firstDelay) / interval) + 1;
    }

    private static bool IsPickable(SpawnGroupSO group) => group != null && group.HasSpawnableMember;

    /// <summary>최댓값이 최솟값보다 작아지지 않게 보정합니다.</summary>
    private void OnValidate()
    {
        m_firstSpawnDelayMin = Mathf.Max(0.0f, m_firstSpawnDelayMin);
        m_firstSpawnDelayMax = Mathf.Max(m_firstSpawnDelayMin, m_firstSpawnDelayMax);
        m_spawnIntervalMin = Mathf.Max(0.1f, m_spawnIntervalMin);
        m_spawnIntervalMax = Mathf.Max(m_spawnIntervalMin, m_spawnIntervalMax);
        m_previewCombatDuration = Mathf.Max(0.0f, m_previewCombatDuration);
    }
}
