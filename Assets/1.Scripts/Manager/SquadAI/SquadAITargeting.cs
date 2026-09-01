using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 조작 캐릭터 하나가 현재 대상을 고르고 유지하는 판단입니다.
/// </summary>
/// <remarks>
/// 공용 문서 `스쿼드 AI 시스템` v0.2 §9가 정본입니다.
///
/// <para>
/// <b>스쿼드 공용 정보와 개인 판단은 다른 층입니다.</b> 적이 어디 있는지는
/// <see cref="SquadEnemyIntel"/>가 스쿼드 전체와 공유하지만(§8), 그중 누구를 칠지는 여기서
/// AI마다 따로 정합니다(§9.1 "현재 대상은 AI 조작 슬롯별로 독립적으로 선정한다").
/// 여러 AI가 같은 적을 골라도 됩니다. 표적 분배는 하지 않습니다.
/// </para>
///
/// <para>
/// <b>대상 유지 유예</b>(§8.5)가 이 클래스의 핵심입니다. 적이 시야에서 사라졌다고 즉시 대상을 버리면
/// 엄폐물 뒤로 잠깐 숨은 것만으로 교전이 끊깁니다. 그래서 실시간 위치를 잃어도 유예시간 동안은
/// 현재 대상으로 붙잡아 두고, 마지막 확인 위치를 향해 조준만 유지하며 발사는 하지 않습니다.
/// </para>
///
/// <para>
/// <b>문서의 "AI 조작 슬롯"과의 차이</b>: 문서(§4.1)는 판단 정보를 캐릭터가 아니라 슬롯이 소유하며
/// 일반 캐릭터 전환으로 배정 캐릭터가 바뀌어도 슬롯에 남는다고 규정합니다. 현재 코드에는 슬롯 개체가
/// 없고 이 판단이 캐릭터의 <see cref="SquadAIController"/>에 붙어 있으므로, 전환 시 판단 정보가
/// 슬롯이 아니라 캐릭터를 따라갑니다. 슬롯 개념을 도입하면 이 소유를 옮겨야 합니다.
/// </para>
///
/// <para>
/// MonoBehaviour가 아니라 <see cref="SquadAIController"/>가 소유하는 일반 클래스입니다.
/// </para>
/// </remarks>
public class SquadAITargeting
{
    /// <summary>대상 판단에 필요한, 이 AI가 조작하는 캐릭터의 현재 사정입니다.</summary>
    /// <remarks>
    /// 판단에 필요한 값을 호출자가 채워 넘깁니다. 이 클래스가 컴포넌트를 직접 뒤지지 않게 해
    /// 판단 규칙만 남기기 위한 구조입니다.
    /// </remarks>
    public struct Context
    {
        /// <summary>스쿼드 공용 적 위치 정보입니다.</summary>
        public SquadEnemyIntel Intel;

        /// <summary>사격선 판정의 시작점입니다.</summary>
        public Vector3 EyePosition;

        /// <summary>거리 위협도 계산에 쓸 캐릭터 위치입니다.</summary>
        public Vector3 BodyPosition;

        /// <summary>무기가 닿는 최대 거리입니다.</summary>
        public float WeaponRange;

        /// <summary>사격선을 가로막는 고정 환경 장애물 레이어입니다.</summary>
        public int ObstacleMask;

        /// <summary>사격선 판정에서 아군 가림을 볼 스쿼드 멤버 목록입니다.</summary>
        /// <remarks>§10.1이 환경 장애물뿐 아니라 스쿼드원도 사격선을 막는다고 규정합니다.</remarks>
        public IReadOnlyList<SquadMemberController> Squad;

        /// <summary>이 판단의 주체입니다. 아군 가림 검사에서 자기 자신을 제외하는 데 씁니다.</summary>
        public SquadMemberController Self;

        /// <summary>지금 이 AI가 행동 판단을 할 수 있는 상태인지 여부입니다.</summary>
        /// <remarks>
        /// 다운·전투 이탈처럼 <b>판단 정보를 지워야 하는</b> 상황만 여기서 걸러 줍니다(§9.5).
        /// 합류는 판단을 유지해야 하므로 <see cref="IsJoining"/>으로 따로 알립니다.
        /// </remarks>
        public bool CanEngage;

        /// <summary>지금 합류 중인지 여부입니다.</summary>
        /// <remarks>
        /// 합류는 전투 위치 조정보다 우선입니다(§18.1). 다만 대상 판단 정보는 유지하고
        /// 조준·사격 요청만 종료합니다(§9.5, §18.2).
        /// </remarks>
        public bool IsJoining;
    }

    /// <summary>후보를 어느 후보군으로 분류했는지입니다(§9.2).</summary>
    private enum CandidateTier
    {
        /// <summary>지금 개인이 사격할 수 있는 적입니다. 1순위입니다.</summary>
        Shootable = 0,

        /// <summary>위치는 공유됐지만 개인이 지금 사격할 수 없는 적입니다. 2순위입니다.</summary>
        Known = 1,
    }

    private readonly Dictionary<EnemyController, float> m_damageThreat =
        new Dictionary<EnemyController, float>();

    private readonly List<EnemyController> m_threatRemoveBuffer = new List<EnemyController>();

    // 사격선이 막히기 시작한 시각입니다. 일시적인 가림과 지속적인 가림을 가르는 데 씁니다(§10.1).
    private readonly Dictionary<EnemyController, float> m_blockedSince =
        new Dictionary<EnemyController, float>();

    private readonly List<EnemyController> m_blockRemoveBuffer = new List<EnemyController>();

    private EnemyController m_currentTarget;
    private float m_nextReevaluateTime;
    private float m_graceExpireTime;
    private bool m_inGrace;

    // 반응시간이 끝나 사격이 열리는 시각입니다. 대상이 바뀔 때만 다시 잡습니다(§11.2).
    private float m_reactionReadyTime;

    /// <summary>정규 대상 재평가 주기입니다.</summary>
    public float ReevaluateInterval { get; set; } = 0.5f;

    /// <summary>실시간 위치를 잃은 현재 대상을 붙잡아 두는 시간입니다(§8.5).</summary>
    public float TargetHoldGrace { get; set; } = 5.0f;

    /// <summary>거리 위협도의 최대값입니다. 붙어 있을수록 이 값에 가까워집니다.</summary>
    public float DistanceThreatWeight { get; set; } = 10.0f;

    /// <summary>거리 위협도가 0이 되는 거리입니다.</summary>
    public float DistanceThreatFalloff { get; set; } = 30.0f;

    /// <summary>현재 대상에게만 얹는 유지 보정입니다(§9.3). 누적되지 않습니다.</summary>
    public float CurrentTargetBonus { get; set; } = 3.0f;

    /// <summary>받은 피해 1당 위협도 환산값입니다.</summary>
    public float DamageThreatPerPoint { get; set; } = 0.5f;

    /// <summary>피해 위협도가 초당 줄어드는 양입니다.</summary>
    public float DamageThreatDecayPerSecond { get; set; } = 1.0f;

    /// <summary>사격선 가림이 이 시간을 넘으면 지속적인 가림으로 봅니다(§10.1).</summary>
    /// <remarks>
    /// 이보다 짧은 가림은 후보군을 낮추지 않습니다. 동료가 잠깐 앞을 지나갈 때마다 대상이 바뀌면
    /// 조준이 계속 튀기 때문입니다. 발사는 가려진 즉시 멈추므로 오사는 이 값과 무관합니다.
    /// </remarks>
    public float SustainedBlockDuration { get; set; } = 1.0f;

    /// <summary>아군이 사격선 이 반경 안에 있으면 가린 것으로 봅니다.</summary>
    public float AllyBlockRadius { get; set; } = 0.6f;

    /// <summary>새 대상을 잡은 뒤 실제로 쏘기까지 기다리는 반응시간입니다(§11.2).</summary>
    /// <remarks>
    /// 사람과 AI의 차이를 만드는 값입니다. 대상이 <b>바뀔 때만</b> 적용하며, 유예 중 같은 대상을
    /// 다시 확인하거나 사격선이 잠깐 끊겼다 복구된 경우에는 다시 적용하지 않습니다.
    /// </remarks>
    public float ReactionTime { get; set; } = 0.35f;

    /// <summary>지금 고른 현재 대상입니다. 없으면 null입니다.</summary>
    public EnemyController CurrentTarget => m_currentTarget;

    /// <summary>실시간 위치를 잃은 대상을 유예로 붙잡고 있는 중인지 여부입니다.</summary>
    /// <remarks>유예 중에는 조준과 방향 유지만 하고 발사하지 않습니다(§8.5).</remarks>
    public bool IsHoldingLostTarget => m_inGrace;

    /// <summary>유예가 끝나기까지 남은 시간입니다. 유예 중이 아니면 0입니다.</summary>
    public float GraceRemaining => m_inGrace ? Mathf.Max(0.0f, m_graceExpireTime - Time.time) : 0.0f;

    /// <summary>현재 대상에 대한 사격선이 지속적으로 막혀 있는지 여부입니다(§10.1).</summary>
    /// <remarks>
    /// 일시적인 가림은 false입니다. 전투 위치를 옮길지 판단할 때 이 값을 봅니다.
    /// 잠깐 가릴 때마다 자리를 옮기면 동료가 계속 서성입니다.
    /// </remarks>
    public bool IsFiringLineBlockedLong
    {
        get
        {
            if (m_currentTarget == null)
            {
                return false;
            }

            if (!m_blockedSince.TryGetValue(m_currentTarget, out float since))
            {
                return false;
            }

            return Time.time - since >= SustainedBlockDuration;
        }
    }

    /// <summary>지금 현재 대상을 향해 발사해도 되는지 여부입니다.</summary>
    /// <remarks>
    /// 유예 중에는 false입니다. 마지막 확인 위치를 향해 겨누고는 있지만 실제로 거기 있는지 모르기 때문입니다.
    /// 실제 발사 직전의 사격선 재확인은 사격 쪽(§9.4의 FireCheck)이 따로 합니다.
    /// </remarks>
    public bool CanFireAtCurrentTarget { get; private set; }

    /// <summary>현재 대상을 향해 겨눌 지점입니다. 대상이 없으면 false를 돌려줍니다.</summary>
    /// <param name="point">겨눌 지점입니다. 유예 중에는 마지막 확인 위치입니다.</param>
    /// <returns>겨눌 대상이 있으면 true입니다.</returns>
    public bool TryGetAimPoint(SquadEnemyIntel intel, out Vector3 point)
    {
        point = Vector3.zero;

        if (m_currentTarget == null || intel == null)
        {
            return false;
        }

        if (!intel.TryGet(m_currentTarget, out SquadEnemyIntel.EnemyIntel record))
        {
            return false;
        }

        point = record.KnownPosition;
        return true;
    }

    /// <summary>
    /// 이 AI가 받은 피해를 공격자별 위협도에 누적합니다(§4.3).
    /// </summary>
    /// <param name="attacker">피해를 준 적입니다.</param>
    /// <param name="damage">실제로 적용된 피해량입니다.</param>
    /// <remarks>
    /// AI로 조작되는 동안 받은 피해만 여기 쌓입니다. 플레이어 조작 중 받은 피해는 공격자 위치만
    /// 공유하고 개인 위협도를 만들지 않습니다(§8.4). 그 구분은 호출자가 합니다.
    /// </remarks>
    public void NotifyDamagedBy(EnemyController attacker, int damage)
    {
        if (attacker == null || damage <= 0)
        {
            return;
        }

        m_damageThreat.TryGetValue(attacker, out float current);
        m_damageThreat[attacker] = current + damage * DamageThreatPerPoint;
    }

    /// <summary>
    /// 대상 판단을 한 번 갱신합니다.
    /// </summary>
    /// <param name="context">이 AI가 조작하는 캐릭터의 현재 사정입니다.</param>
    /// <remarks>
    /// 매 프레임 불러도 됩니다. 전체 후보 비교는 정규 재평가 주기에만 하고, 그 사이에는
    /// 현재 대상의 유효성과 사격 가능 여부만 확인합니다(§9.4).
    /// </remarks>
    public void Tick(in Context context)
    {
        DecayDamageThreat();

        if (!context.CanEngage || context.Intel == null)
        {
            ClearTarget();
            return;
        }

        // 합류 중에는 판단을 <b>멈추되 지우지는 않습니다</b>(§9.5, §18.2).
        // 지워 버리면 합류가 끝날 때마다 대상을 처음부터 다시 잡게 되고, 그러면 §11.2가 금지한
        // 반응시간 재적용이 매번 일어납니다. 문서도 "현재 대상 정보는 유지하되 조준과 사격 요청을
        // 종료한다"고 규정합니다. 실행 중단은 CanFireAtCurrentTarget과 호출자의 조준 억제로 합니다.
        if (context.IsJoining)
        {
            CanFireAtCurrentTarget = false;
            return;
        }

        UpdateBlockTimers(context);

        bool needsImmediate = UpdateCurrentTargetValidity(context);

        if (needsImmediate || Time.time >= m_nextReevaluateTime)
        {
            m_nextReevaluateTime = Time.time + Mathf.Max(0.05f, ReevaluateInterval);
            SelectTarget(context);
        }

        // 발사 판정은 지속 가림이 아니라 <b>지금</b> 뚫려 있는지로 합니다. §10.1이 "사격선이 막히면
        // 발사 입력을 즉시 해제한다"고 규정하므로, 잠깐 가려도 그 순간에는 쏘면 안 됩니다.
        // 반응시간이 남아 있으면 조준은 하되 아직 쏘지 않습니다(§11.2).
        CanFireAtCurrentTarget = m_currentTarget != null
                                 && !m_inGrace
                                 && Time.time >= m_reactionReadyTime
                                 && HasClearFiringLine(context, m_currentTarget);
    }

    /// <summary>대상 판단 정보를 모두 지웁니다.</summary>
    /// <remarks>다운·전투 이탈·스쿼드 전투 종료에서 부릅니다(§9.5).</remarks>
    public void Clear()
    {
        ClearTarget();
        m_damageThreat.Clear();
        m_blockedSince.Clear();
    }

    /// <summary>
    /// 현재 대상이 아직 쓸 수 있는지 확인하고 유예 상태를 갱신합니다.
    /// </summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <returns>정규 주기를 기다리지 않고 즉시 다시 골라야 하면 true입니다.</returns>
    /// <remarks>
    /// 즉시 재선정 조건은 §9.5가 정합니다. 현재 대상이 완전히 무효화됐거나, 대상이 없는 상태에서
    /// 첫 유효 후보가 생긴 경우입니다. 유예 만료도 "완전 무효화"에 해당합니다(§8.5).
    /// </remarks>
    private bool UpdateCurrentTargetValidity(in Context context)
    {
        if (m_currentTarget == null)
        {
            m_inGrace = false;
            return HasAnyCandidate(context);
        }

        // 적 사망과 교전 종료는 유예를 적용하지 않고 즉시 버립니다(§8.5).
        if (!context.Intel.TryGet(m_currentTarget, out SquadEnemyIntel.EnemyIntel record) || record.Enemy == null)
        {
            ClearTarget();
            return true;
        }

        if (record.HasLivePosition)
        {
            // 다시 확인됐습니다. 같은 적이므로 그대로 이어서 씁니다(§8.5).
            m_inGrace = false;
            return false;
        }

        // 실시간 위치를 잃었습니다. 유예 동안만 붙잡습니다.
        if (!m_inGrace)
        {
            m_inGrace = true;
            m_graceExpireTime = Time.time + Mathf.Max(0.0f, TargetHoldGrace);
            return false;
        }

        if (Time.time >= m_graceExpireTime)
        {
            ClearTarget();
            return true;
        }

        return false;
    }

    /// <summary>후보가 하나라도 있는지 확인합니다.</summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <returns>후보가 있으면 true입니다.</returns>
    private static bool HasAnyCandidate(in Context context)
    {
        foreach (var record in context.Intel.All)
        {
            if (IsCandidate(record))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 후보군과 점수로 현재 대상을 고릅니다(§9.2, §9.3).
    /// </summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <remarks>
    /// 1순위(지금 쏠 수 있는 적)가 하나라도 있으면 2순위는 보지 않습니다. 후보군 안에서만 점수를 겨룹니다.
    /// 유예 중인 현재 대상은 실시간 위치가 없어 후보 자체가 아니지만, 점수가 같거나 더 높은 후보가
    /// 없으면 그대로 남습니다. 최고 점수가 같으면 현재 대상을 유지합니다(§9.3).
    /// </remarks>
    private void SelectTarget(in Context context)
    {
        EnemyController best = null;
        CandidateTier bestTier = CandidateTier.Known;
        float bestScore = float.NegativeInfinity;

        foreach (var record in context.Intel.All)
        {
            if (!IsCandidate(record))
            {
                continue;
            }

            EnemyController enemy = record.Enemy;
            CandidateTier tier = IsShootableForTier(context, enemy) ? CandidateTier.Shootable : CandidateTier.Known;
            float score = CalculateScore(context, enemy);

            if (best == null || tier < bestTier || (tier == bestTier && score > bestScore))
            {
                best = enemy;
                bestTier = tier;
                bestScore = score;
            }
        }

        if (best == null)
        {
            // 후보가 없습니다. 유예 중인 현재 대상은 유예가 끝날 때까지 그대로 둡니다.
            return;
        }

        if (best == m_currentTarget)
        {
            return;
        }

        // 대상이 실제로 바뀔 때만 반응시간을 겁니다(§11.2). 유예 중 같은 대상을 다시 확인한 경우나
        // 사격선이 잠깐 끊겼다 복구된 경우는 여기까지 오지 않으므로 자연히 재적용되지 않습니다.
        m_currentTarget = best;
        m_inGrace = false;
        m_reactionReadyTime = Time.time + Mathf.Max(0.0f, ReactionTime);
    }

    /// <summary>
    /// 이 적이 새 대상 후보가 될 수 있는지 확인합니다(§9.1).
    /// </summary>
    /// <param name="record">확인할 적 정보입니다.</param>
    /// <returns>후보로 쓸 수 있으면 true입니다.</returns>
    /// <remarks>
    /// 마지막 확인 위치만 남은 적은 새 후보가 아닙니다. 그런 적을 후보로 넣으면 AI가 유령을 쫓습니다.
    /// </remarks>
    private static bool IsCandidate(SquadEnemyIntel.EnemyIntel record)
    {
        if (record == null || record.Enemy == null || !record.HasLivePosition)
        {
            return false;
        }

        EnemyHealth health = record.Enemy.Health;
        return health == null || !health.IsDead;
    }

    /// <summary>
    /// 지금 이 적에게 사격선이 열려 있는지 판정합니다(§10.1).
    /// </summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <param name="enemy">확인할 적입니다.</param>
    /// <returns>사거리 안이고 환경 장애물·아군 모두에 막히지 않으면 true입니다.</returns>
    /// <remarks>
    /// 환경 장애물뿐 아니라 <b>스쿼드원</b>도 사격선을 막습니다. 이것이 없으면 동료 등 뒤에 대고 쏩니다.
    /// 재장전처럼 일시적인 캐릭터 행동 제한은 보지 않습니다. 문서가 그것만으로 후보군을 바꾸지 말라고
    /// 규정하기 때문입니다.
    /// </remarks>
    private bool HasClearFiringLine(in Context context, EnemyController enemy)
    {
        if (enemy == null)
        {
            return false;
        }

        Vector3 target = enemy.transform.position + Vector3.up;
        Vector3 delta = target - context.EyePosition;

        if (delta.sqrMagnitude > context.WeaponRange * context.WeaponRange)
        {
            return false;
        }

        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        Vector3 direction = delta / distance;

        if (Physics.Raycast(
                context.EyePosition,
                direction,
                distance,
                context.ObstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return !IsAllyOnLine(context, direction, distance);
    }

    /// <summary>
    /// 사격선 위에 다른 스쿼드원이 있는지 확인합니다(§10.1).
    /// </summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <param name="direction">사격 방향입니다.</param>
    /// <param name="distance">대상까지의 거리입니다.</param>
    /// <returns>아군이 선 위에 있으면 true입니다.</returns>
    /// <remarks>
    /// 콜라이더 대신 선분과 아군 위치의 거리로 판정합니다. 아군의 콜라이더가 역할에 따라 켜졌다 꺼지므로
    /// (직접 조작 멤버는 CharacterController, AI 멤버는 CapsuleCollider) Raycast로는 판정이 들쭉날쭉합니다.
    /// 다운·전투 이탈한 아군은 바닥에 있으므로 막는 것으로 보지 않습니다.
    /// </remarks>
    private bool IsAllyOnLine(in Context context, Vector3 direction, float distance)
    {
        if (context.Squad == null)
        {
            return false;
        }

        float radiusSqr = AllyBlockRadius * AllyBlockRadius;

        for (int i = 0; i < context.Squad.Count; i++)
        {
            SquadMemberController ally = context.Squad[i];
            if (ally == null || ally == context.Self)
            {
                continue;
            }

            if (!ally.IsAlive || ally.IsDown)
            {
                continue;
            }

            // 아군의 가슴 높이를 기준점으로 씁니다. 발밑을 쓰면 총구보다 아래라 항상 빗겨 갑니다.
            Vector3 allyPoint = ally.transform.position + Vector3.up;
            Vector3 toAlly = allyPoint - context.EyePosition;

            float along = Vector3.Dot(toAlly, direction);
            if (along <= 0.0f || along >= distance)
            {
                // 내 뒤에 있거나 대상보다 멀리 있으면 막지 않습니다.
                continue;
            }

            Vector3 closest = context.EyePosition + direction * along;
            if ((allyPoint - closest).sqrMagnitude < radiusSqr)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 후보별로 사격선이 막힌 시간을 갱신합니다(§10.1).
    /// </summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <remarks>
    /// 일시적인 가림과 지속적인 가림을 가르기 위한 타이머입니다. 뚫리면 즉시 지웁니다.
    /// 교전 목록에서 빠진 적의 기록도 함께 정리합니다.
    /// </remarks>
    private void UpdateBlockTimers(in Context context)
    {
        m_blockRemoveBuffer.Clear();

        foreach (var record in context.Intel.All)
        {
            EnemyController enemy = record.Enemy;
            if (enemy == null || !record.HasLivePosition)
            {
                continue;
            }

            if (HasClearFiringLine(context, enemy))
            {
                if (m_blockedSince.ContainsKey(enemy))
                {
                    m_blockRemoveBuffer.Add(enemy);
                }

                continue;
            }

            if (!m_blockedSince.ContainsKey(enemy))
            {
                m_blockedSince[enemy] = Time.time;
            }
        }

        for (int i = 0; i < m_blockRemoveBuffer.Count; i++)
        {
            m_blockedSince.Remove(m_blockRemoveBuffer[i]);
        }
    }

    /// <summary>
    /// 후보군 분류에 쓸 사격 가능 여부입니다(§9.2, §10.1).
    /// </summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <param name="enemy">확인할 적입니다.</param>
    /// <returns>1순위 후보군으로 볼 수 있으면 true입니다.</returns>
    /// <remarks>
    /// 발사 판정과 다릅니다. 지금 막혀 있어도 <b>일시적인 가림이면 1순위를 유지</b>합니다.
    /// §10.1이 "일시적인 가림만으로 현재 대상을 변경하지 않는다"고 규정하기 때문입니다.
    /// 가림이 <see cref="SustainedBlockDuration"/>을 넘겨야 2순위로 내려갑니다.
    /// </remarks>
    private bool IsShootableForTier(in Context context, EnemyController enemy)
    {
        if (HasClearFiringLine(context, enemy))
        {
            return true;
        }

        if (!m_blockedSince.TryGetValue(enemy, out float since))
        {
            // 아직 가림이 기록되지 않았습니다. 이번 틱에 막 막힌 것이므로 일시적으로 봅니다.
            return true;
        }

        return Time.time - since < SustainedBlockDuration;
    }

    /// <summary>
    /// 대상 점수를 계산합니다(§9.3).
    /// </summary>
    /// <param name="context">현재 사정입니다.</param>
    /// <param name="enemy">점수를 매길 적입니다.</param>
    /// <returns>거리 위협도 + 최근 피해 위협도 + 현재 대상 유지 보정입니다.</returns>
    private float CalculateScore(in Context context, EnemyController enemy)
    {
        float distance = Vector3.Distance(context.BodyPosition, enemy.transform.position);
        float distanceThreat = DistanceThreatFalloff <= 0.0f
            ? DistanceThreatWeight
            : DistanceThreatWeight * Mathf.Clamp01(1.0f - distance / DistanceThreatFalloff);

        m_damageThreat.TryGetValue(enemy, out float damageThreat);

        float keepBonus = enemy == m_currentTarget ? CurrentTargetBonus : 0.0f;

        return distanceThreat + damageThreat + keepBonus;
    }

    /// <summary>시간이 지난 만큼 피해 위협도를 줄이고 0이 된 항목을 정리합니다.</summary>
    private void DecayDamageThreat()
    {
        if (m_damageThreat.Count == 0)
        {
            return;
        }

        float decay = DamageThreatDecayPerSecond * Time.deltaTime;

        m_threatRemoveBuffer.Clear();
        var keys = new List<EnemyController>(m_damageThreat.Keys);

        for (int i = 0; i < keys.Count; i++)
        {
            EnemyController enemy = keys[i];
            float value = m_damageThreat[enemy] - decay;

            if (enemy == null || value <= 0.0f)
            {
                m_threatRemoveBuffer.Add(enemy);
                continue;
            }

            m_damageThreat[enemy] = value;
        }

        for (int i = 0; i < m_threatRemoveBuffer.Count; i++)
        {
            m_damageThreat.Remove(m_threatRemoveBuffer[i]);
        }
    }

    /// <summary>현재 대상과 유예 상태만 지웁니다. 피해 위협도는 남깁니다.</summary>
    private void ClearTarget()
    {
        m_currentTarget = null;
        m_inGrace = false;
        m_graceExpireTime = 0.0f;
        CanFireAtCurrentTarget = false;
    }
}
