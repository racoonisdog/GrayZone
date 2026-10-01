using System.Collections.Generic;
using UnityEngine;

/// <summary>주변 적에게 단일 소유권 버프를 부여하고 조건이 다시 차면 재하울링하는 능력입니다.</summary>
/// <remarks>
/// 스쿼드 위치를 주변 적에게 공유하던 기존 호출 기능은 <see cref="BroadcastSquadCall"/>에 보존하지만
/// 현재 하울링 행동에서는 사용하지 않습니다. 실제 행동은 위치 공유 없이 버프 대상만 별도로 수집합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class HowlAbility : EnemyAbility
{
    [Header("Howl")]
    [Tooltip("버프 대상과 재하울링 후보를 검사할 반경(m)입니다. 벽이나 엄폐물은 판정하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_howlRadius = 25.0f;

    [Tooltip("하울링 시작 후 버프 전파가 확정되는 시점(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_howlBroadcastTime = 1.2f;

    [Tooltip("하울링 행동 전체 길이(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_howlDuration = 3.0f;

    [Header("Buff")]
    [Tooltip("반경 안의 적에게 부여할 버프입니다. 지속시간은 이 에셋에서 조정합니다.")]
    [SerializeField] private EnemyBuffSO m_howlBuff;

    [Tooltip("반경 안에 이 버프를 새로 받을 수 있는 적이 몇 명 이상일 때 하울링할지 정합니다.")]
    [Min(1)]
    [SerializeField] private int m_unbuffedEnemyThreshold = 2;

    [Tooltip("후보 인원 검사를 반복하는 간격(초)입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_candidateCheckInterval = 0.5f;

    [Tooltip("전파 완료 또는 연속 취소 뒤 다음 하울링을 허용하기까지의 최소 간격(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_rehowlCooldown = 2.0f;

    [Header("Debug")]
    [SerializeField] private bool m_debugDrawHowlRadius = true;

    private const int MaxHowlCancels = 2;
    private static readonly int AnimHowl = Animator.StringToHash("DoHowl");
    private static readonly int AnimIsHowl = Animator.StringToHash("IsHowl");

    private readonly List<EnemyController> m_candidateBuffer = new List<EnemyController>();
    private HowlState m_howlState;
    private int m_howlCancelCount;
    private bool m_preHowlAttackUsed;
    private bool m_resumeWithHowlAfterStagger;
    private float m_nextHowlAllowedTime;
    private float m_nextCandidateCheckTime;
    private int m_cachedEligibleCount;

    public float HowlRadius => m_howlRadius;
    public float HowlBroadcastTime => m_howlBroadcastTime;
    public float HowlDuration => Mathf.Max(m_howlBroadcastTime, m_howlDuration);
    public int UnbuffedEnemyThreshold => Mathf.Max(1, m_unbuffedEnemyThreshold);
    public int LastEligibleEnemyCount { get; private set; }
    public int LastHowlAppliedCount { get; private set; } = -1;
    public int LastHowlMemberCount { get; private set; } = -1;

    protected override void OnInitialized()
    {
        m_howlState = new HowlState(Owner, this);
    }

    public override void ApplyBalance(EnemyBalanceSO balance)
    {
        m_howlRadius = balance.HowlRadius;
        m_howlBroadcastTime = balance.HowlBroadcastTime;
        m_howlDuration = balance.HowlDuration;
    }

    public override EnemyStateBase OnCombatEnter()
    {
        if (!CanTryHowl(forceCandidateRefresh: true))
        {
            return null;
        }

        CombatState combat = Owner.Combat;
        if (!m_preHowlAttackUsed && combat.CanAttackCurrentTargetNow())
        {
            m_preHowlAttackUsed = true;
            return combat.BeginLeadInAttack(this);
        }

        return m_howlState;
    }

    public override EnemyStateBase OnLeadInAttackFinished()
    {
        return CanTryHowl(forceCandidateRefresh: true) ? m_howlState : null;
    }

    public override EnemyStateBase OnCombatActionOpportunity()
    {
        return CanTryHowl(forceCandidateRefresh: false) ? m_howlState : null;
    }

    public override void OnStaggered(EnemyStateBase interruptedSub, bool wasOwnLeadInAttack)
    {
        if (interruptedSub == m_howlState && !m_howlState.IsBroadcastDone)
        {
            NotifyHowlCanceled();
            m_resumeWithHowlAfterStagger = CanTryHowl(forceCandidateRefresh: true);
            return;
        }

        m_resumeWithHowlAfterStagger = wasOwnLeadInAttack
            && CanTryHowl(forceCandidateRefresh: true);
    }

    public override EnemyStateBase OnStaggerEnded()
    {
        if (!m_resumeWithHowlAfterStagger)
        {
            return null;
        }

        m_resumeWithHowlAfterStagger = false;
        return CanTryHowl(forceCandidateRefresh: true) ? m_howlState : null;
    }

    public override void OnCombatExit()
    {
        m_howlCancelCount = 0;
        m_preHowlAttackUsed = false;
        m_resumeWithHowlAfterStagger = false;
        m_nextHowlAllowedTime = 0.0f;
        InvalidateCandidateCache();
    }

    /// <summary>버프 전파가 완료된 시점을 기록하고 재하울링 쿨다운을 시작합니다.</summary>
    public void MarkHowlBroadcast()
    {
        m_howlCancelCount = 0;
        m_nextHowlAllowedTime = Time.time + m_rehowlCooldown;
        InvalidateCandidateCache();
    }

    /// <summary>현재 하울링 행동이 사용하는 버프 전파입니다. 스쿼드 위치 공유는 하지 않습니다.</summary>
    public int BroadcastBuffHowl()
    {
        LastHowlAppliedCount = 0;
        if (Owner == null || m_howlBuff == null)
        {
            return 0;
        }

        HowlSystem.CollectEnemiesInRange(
            Owner.Sensor,
            transform.position,
            m_howlRadius,
            m_candidateBuffer);

        for (int i = 0; i < m_candidateBuffer.Count; i++)
        {
            EnemyController target = m_candidateBuffer[i];
            if (target != Owner
                && target.CanReceiveBuffFrom(m_howlBuff, Owner)
                && target.ApplyBuff(m_howlBuff, Owner))
            {
                LastHowlAppliedCount++;
            }
        }

        if (m_howlBuff.ApplyToSource)
        {
            Owner.ApplyBuff(m_howlBuff, Owner);
        }

        InvalidateCandidateCache();
        return LastHowlAppliedCount;
    }

    /// <summary>
    /// 기존 스쿼드 위치 공유 하울링입니다. 현재 하울러 행동에서는 호출하지 않습니다.
    /// </summary>
    public int BroadcastSquadCall(IReadOnlyList<SquadMemberController> members)
    {
        EnemyTargetSensor sensor = Owner != null ? Owner.Sensor : null;
        if (sensor == null || members == null)
        {
            LastHowlMemberCount = 0;
            return 0;
        }

        sensor.ApplyOwnHowl(members);
        LastHowlMemberCount = members.Count;
        return HowlSystem.Broadcast(sensor, transform.position, m_howlRadius, members);
    }

    /// <summary>기존 호출부 호환용 별칭입니다. 신규 행동에서는 사용하지 않습니다.</summary>
    public void BroadcastHowl(IReadOnlyList<SquadMemberController> members)
    {
        BroadcastSquadCall(members);
    }

    public void PlayHowlAnimation()
    {
        Owner.PlayActionAnimation(AnimHowl, AnimIsHowl);
    }

    public void EndHowlAnimation()
    {
        Owner.EndActionAnimation(AnimIsHowl);
    }

    /// <summary>WW_Howl 클립의 전파 애니메이션 이벤트 진입점입니다.</summary>
    public void HowlBroadcast()
    {
        if (Owner == null || Owner.Current != Owner.Combat || Owner.Combat.CurrentSub != m_howlState)
        {
            return;
        }

        m_howlState.NotifyAnimationBroadcast();
    }

    private bool CanTryHowl(bool forceCandidateRefresh)
    {
        if (Owner == null
            || m_howlBuff == null
            || Time.time < m_nextHowlAllowedTime)
        {
            return false;
        }

        return CountEligibleEnemies(forceCandidateRefresh) >= UnbuffedEnemyThreshold;
    }

    private int CountEligibleEnemies(bool forceRefresh)
    {
        float now = Time.time;
        if (!forceRefresh && now < m_nextCandidateCheckTime)
        {
            return m_cachedEligibleCount;
        }

        m_nextCandidateCheckTime = now + m_candidateCheckInterval;
        m_cachedEligibleCount = 0;

        HowlSystem.CollectEnemiesInRange(
            Owner.Sensor,
            transform.position,
            m_howlRadius,
            m_candidateBuffer);

        for (int i = 0; i < m_candidateBuffer.Count; i++)
        {
            if (m_candidateBuffer[i] != Owner
                && m_candidateBuffer[i].CanReceiveBuffFrom(m_howlBuff, Owner))
            {
                m_cachedEligibleCount++;
            }
        }

        LastEligibleEnemyCount = m_cachedEligibleCount;
        return m_cachedEligibleCount;
    }

    private void NotifyHowlCanceled()
    {
        m_howlCancelCount++;
        if (m_howlCancelCount < MaxHowlCancels)
        {
            return;
        }

        m_howlCancelCount = 0;
        m_nextHowlAllowedTime = Time.time + m_rehowlCooldown;
    }

    private void InvalidateCandidateCache()
    {
        m_nextCandidateCheckTime = 0.0f;
        m_cachedEligibleCount = 0;
        LastEligibleEnemyCount = 0;
    }

    private void OnValidate()
    {
        m_howlRadius = Mathf.Max(0.0f, m_howlRadius);
        m_howlBroadcastTime = Mathf.Max(0.0f, m_howlBroadcastTime);
        m_howlDuration = Mathf.Max(0.0f, m_howlDuration);
        m_unbuffedEnemyThreshold = Mathf.Max(1, m_unbuffedEnemyThreshold);
        m_candidateCheckInterval = Mathf.Max(0.05f, m_candidateCheckInterval);
        m_rehowlCooldown = Mathf.Max(0.0f, m_rehowlCooldown);
    }

    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawHowlRadius)
        {
            return;
        }

        Gizmos.color = new Color(1.0f, 0.55f, 0.0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, m_howlRadius);
    }
}
