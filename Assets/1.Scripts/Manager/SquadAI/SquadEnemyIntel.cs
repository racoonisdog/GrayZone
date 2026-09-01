using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스쿼드가 공유하는 교전 적 위치 정보입니다.
/// </summary>
/// <remarks>
/// 공용 문서 `스쿼드 AI 시스템` v0.2 §8이 정본입니다.
///
/// <para>
/// <b>등록 대상은 교전 적뿐입니다</b>(§8.1). 교전하지 않은 적을 플레이어나 AI가 보는 것만으로는
/// 공유하지 않습니다. 그래서 후보를 <see cref="SquadEngagement"/>의 교전 목록에서만 가져옵니다.
/// 이 제약이 순회 비용도 함께 눌러 줍니다.
/// </para>
///
/// <para>
/// <b>확인 경로는 셋입니다.</b> 플레이어는 게임플레이 카메라 기준(§8.2), AI는 자기 캐릭터의 시야 기준(§8.3),
/// 그리고 피격당하면 공격자를 즉시 확인합니다(§8.4). 어느 경로든 한 명만 확인하면 스쿼드 전체가
/// 실시간 위치를 공유합니다.
/// </para>
///
/// <para>
/// <b>여기는 "무엇을 아는가"만 다룹니다.</b> 그 정보로 누구를 칠지 정하는 것은 §9 대상 선정의 몫입니다.
/// 특히 §8.5의 대상 유지 유예는 현재 대상을 붙잡아 두는 규칙이라 대상 선정 쪽에 속하며,
/// 여기서는 실시간 위치를 잃은 순간 마지막 확인 위치를 남기는 데까지만 합니다.
/// </para>
///
/// <para>
/// MonoBehaviour가 아니라 <see cref="SquadManager"/>가 소유하는 일반 클래스입니다.
/// <see cref="SquadEngagement"/>와 같은 방식이라 씬 배선이 필요 없습니다.
/// </para>
/// </remarks>
public class SquadEnemyIntel
{
    /// <summary>스쿼드가 적 하나에 대해 아는 내용입니다.</summary>
    public class EnemyIntel
    {
        /// <summary>이 기록이 가리키는 적입니다.</summary>
        public EnemyController Enemy;

        /// <summary>지금 스쿼드원 중 누군가가 직접 확인하고 있는지 여부입니다.</summary>
        public bool HasLivePosition;

        /// <summary>마지막으로 확인한 위치입니다. 적이 움직여도 따라가지 않습니다(§8.5).</summary>
        public Vector3 LastKnownPosition;

        /// <summary>마지막으로 확인한 시각입니다.</summary>
        public float LastKnownTime;

        /// <summary>피격으로 확인한 공격자 위치의 만료 시각입니다.</summary>
        /// <remarks>§8.4가 공격자 위치에만 별도의 짧은 유지 시간을 두라고 규정합니다.</remarks>
        public float AttackerHoldExpireTime;

        /// <summary>지금 알고 있는 위치입니다. 실시간이면 적의 현재 위치, 아니면 마지막 확인 위치입니다.</summary>
        public Vector3 KnownPosition =>
            HasLivePosition && Enemy != null ? Enemy.transform.position : LastKnownPosition;
    }

    private readonly Dictionary<EnemyController, EnemyIntel> m_intel =
        new Dictionary<EnemyController, EnemyIntel>();

    // 순회 중 목록이 바뀌어도 안전하도록 쓰는 임시 버퍼입니다. 매 갱신마다 새로 만들지 않습니다.
    private readonly List<EnemyController> m_removeBuffer = new List<EnemyController>();

    /// <summary>지금 실시간 위치를 공유 중인 적의 수입니다.</summary>
    public int LiveCount
    {
        get
        {
            int count = 0;
            foreach (var pair in m_intel)
            {
                if (pair.Value.HasLivePosition)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>기록 중인 적의 수입니다. 실시간 위치를 잃은 적도 포함합니다.</summary>
    public int TrackedCount => m_intel.Count;

    /// <summary>기록 전체입니다. 대상 선정이 후보를 훑을 때 씁니다.</summary>
    public IReadOnlyCollection<EnemyIntel> All => m_intel.Values;

    /// <summary>
    /// 특정 적에 대한 기록을 가져옵니다.
    /// </summary>
    /// <param name="enemy">조회할 적입니다.</param>
    /// <param name="intel">찾은 기록입니다.</param>
    /// <returns>기록이 있으면 true입니다.</returns>
    public bool TryGet(EnemyController enemy, out EnemyIntel intel)
    {
        intel = null;
        return enemy != null && m_intel.TryGetValue(enemy, out intel);
    }

    /// <summary>
    /// 피격으로 공격자를 즉시 확인해 공유합니다(§8.4).
    /// </summary>
    /// <param name="attacker">피해를 입힌 적입니다.</param>
    /// <param name="holdDuration">공격자 위치를 실시간으로 유지할 시간입니다.</param>
    /// <remarks>
    /// 시야와 무관하게 성립합니다. 뒤에서 맞아도 누가 때렸는지는 알기 때문입니다.
    /// 같은 공격자에게 다시 맞으면 유지 시간이 갱신됩니다.
    /// 교전 적이 아니면 무시합니다. 등록 대상은 교전 적뿐이라는 §8.1 제약 때문입니다.
    /// </remarks>
    public void NotifyDamagedBy(EnemyController attacker, float holdDuration)
    {
        if (attacker == null || !m_intel.TryGetValue(attacker, out EnemyIntel intel))
        {
            return;
        }

        intel.AttackerHoldExpireTime = Time.time + Mathf.Max(0.0f, holdDuration);
        MarkConfirmed(intel);
    }

    /// <summary>
    /// 교전 목록과 확인 결과를 반영해 기록을 갱신합니다.
    /// </summary>
    /// <param name="engagement">교전 중인 적을 제공하는 스쿼드 전투 상태입니다.</param>
    /// <param name="confirm">해당 적을 지금 누군가 직접 확인하고 있는지 판정하는 함수입니다.</param>
    /// <remarks>
    /// 교전에서 빠지거나 파괴된 적은 유예 없이 즉시 제거합니다(§8.5).
    /// 확인 판정을 함수로 받는 이유는 플레이어(카메라)와 AI(캐릭터 시야)의 기준이 다르고,
    /// 그 판정 주체가 여기가 아니라 각자에게 있기 때문입니다.
    /// </remarks>
    public void Refresh(SquadEngagement engagement, System.Func<EnemyController, bool> confirm)
    {
        if (engagement == null)
        {
            return;
        }

        // 1) 교전에서 빠졌거나 사라진 적을 걷어냅니다.
        m_removeBuffer.Clear();
        foreach (var pair in m_intel)
        {
            EnemyController enemy = pair.Key;
            if (enemy == null || !engagement.IsEngaged(enemy))
            {
                m_removeBuffer.Add(enemy);
            }
        }

        for (int i = 0; i < m_removeBuffer.Count; i++)
        {
            m_intel.Remove(m_removeBuffer[i]);
        }

        // 2) 교전 적을 등록하고 확인 여부를 갱신합니다.
        foreach (EnemyController enemy in engagement.EngagedEnemies)
        {
            if (enemy == null)
            {
                continue;
            }

            if (!m_intel.TryGetValue(enemy, out EnemyIntel intel))
            {
                intel = new EnemyIntel { Enemy = enemy };
                m_intel.Add(enemy, intel);
            }

            bool confirmedNow = confirm != null && confirm(enemy);
            bool attackerHoldActive = Time.time < intel.AttackerHoldExpireTime;

            if (confirmedNow || attackerHoldActive)
            {
                MarkConfirmed(intel);
                continue;
            }

            // 확인이 끊기면 유예 없이 즉시 마지막 확인 위치로 굳힙니다(§8.5).
            // 현재 대상을 붙잡아 두는 유예는 대상 선정이 따로 판단합니다.
            intel.HasLivePosition = false;
        }
    }

    /// <summary>기록을 모두 지웁니다.</summary>
    public void Clear()
    {
        m_intel.Clear();
    }

    /// <summary>지금 확인된 것으로 기록하고 위치를 갱신합니다.</summary>
    /// <param name="intel">갱신할 기록입니다.</param>
    private static void MarkConfirmed(EnemyIntel intel)
    {
        intel.HasLivePosition = true;

        if (intel.Enemy != null)
        {
            intel.LastKnownPosition = intel.Enemy.transform.position;
            intel.LastKnownTime = Time.time;
        }
    }
}
