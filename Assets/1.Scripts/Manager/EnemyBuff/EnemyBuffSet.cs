using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 적에게 걸린 버프 목록을 관리합니다. 적용, 교체, 시간 만료를 담당합니다.
/// </summary>
/// <remarks>
/// <see cref="EnemyController"/>가 하나씩 소유하는 일반 클래스입니다. 모든 적이 버프를 받을 수 있으므로
/// 받는 쪽 기능은 공통 코드에 있고, 버프를 거는 쪽(예: <see cref="HowlAbility"/>)은 적용만 요청합니다.
///
/// <b>한 번 걸면 지속 시간이 끝날 때까지 유지합니다.</b> 버프를 건 개체가 먼저 죽어도 풀리지 않습니다.
/// 건 쪽을 추적해 해제하면 여러 개체가 같은 버프를 갱신하는 경우의 예외 규칙이 늘어나므로 1회성 부여로 정했습니다
/// (기획 결정 2026-09-28).
///
/// 이동 속도는 <see cref="EnemyController.AddMoveSpeedMultiplier"/>에 <b>버프 에셋을 키로</b> 겁니다.
/// 같은 종류는 키가 같아 한 번만 적용되고, 함정 감속과도 키가 겹치지 않습니다.
/// </remarks>
public sealed class EnemyBuffSet
{
    /// <summary>걸려 있는 버프 하나의 기록입니다.</summary>
    private struct Entry
    {
        /// <summary>버프 종류입니다.</summary>
        public EnemyBuffSO Buff;

        /// <summary>만료 시각(Time.time 기준)입니다.</summary>
        public float ExpireTime;
    }

    private readonly EnemyController m_owner;
    private readonly List<Entry> m_entries = new List<Entry>();

    /// <summary>버프 목록을 만듭니다.</summary>
    /// <param name="owner">버프 효과를 반영할 적입니다.</param>
    public EnemyBuffSet(EnemyController owner)
    {
        m_owner = owner;
    }

    /// <summary>걸려 있는 버프 수입니다.</summary>
    public int Count => m_entries.Count;

    /// <summary>지정한 종류의 버프가 걸려 있는지 확인합니다.</summary>
    /// <param name="buff">확인할 버프 종류입니다.</param>
    public bool Has(EnemyBuffSO buff) => IndexOf(buff) >= 0;

    /// <summary>
    /// 버프를 겁니다. 같은 종류가 이미 있으면 새 버프 기준으로 교체하고 지속 시간을 다시 셉니다.
    /// </summary>
    /// <param name="buff">걸 버프 종류입니다.</param>
    public void Apply(EnemyBuffSO buff)
    {
        if (buff == null)
        {
            return;
        }

        var entry = new Entry
        {
            Buff = buff,
            ExpireTime = Time.time + buff.Duration,
        };

        int index = IndexOf(buff);
        if (index >= 0)
        {
            m_entries[index] = entry;
        }
        else
        {
            m_entries.Add(entry);
        }

        m_owner.AddMoveSpeedMultiplier(buff, buff.MoveSpeedMultiplier);
    }

    /// <summary>만료 시각이 지난 버프를 해제합니다.</summary>
    /// <remarks>걸린 버프가 없으면 바로 돌아가므로 매 프레임 불러도 됩니다.</remarks>
    public void Tick()
    {
        if (m_entries.Count == 0)
        {
            return;
        }

        float now = Time.time;
        for (int i = m_entries.Count - 1; i >= 0; i--)
        {
            if (now >= m_entries[i].ExpireTime)
            {
                EnemyBuffSO buff = m_entries[i].Buff;
                m_entries.RemoveAt(i);
                m_owner.RemoveMoveSpeedMultiplier(buff);
            }
        }
    }

    /// <summary>
    /// 기록만 비웁니다. 이동 속도 배율은 호출하는 쪽이 이미 비웠을 때 씁니다.
    /// </summary>
    /// <remarks>풀 반환 시 <see cref="EnemyController.ClearSpawnConfiguration"/>이 배율을 한꺼번에 비운 뒤 부릅니다.</remarks>
    public void ClearRecordsOnly()
    {
        m_entries.Clear();
    }

    private int IndexOf(EnemyBuffSO buff)
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            if (m_entries[i].Buff == buff)
            {
                return i;
            }
        }

        return -1;
    }
}
