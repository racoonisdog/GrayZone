using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 하울러의 하울링(패턴 A) 능력입니다. 대상을 직접 발견하면 하울링으로 주변 변이체를 교전에 합류시킵니다.
/// </summary>
/// <remarks>
/// 하울링을 <b>보내는</b> 쪽만 담당합니다. 하울링을 받아 합류하는 규칙은 모든 적 공통이므로
/// <see cref="EnemyTargetSensor"/>와 <see cref="HowlSystem"/>에 남아 있습니다.
///
/// 교전 진입 시 즉시 공격할 수 있으면 공격 1회를 먼저 하고 하울링합니다(§5.5.2).
/// 하울링을 받아 합류한 개체는 하울링하지 않습니다(§5.5.1). 맞하울링을 막는 규칙입니다.
///
/// <b>기회 소모는 전파 시점에 확정됩니다.</b> 전파 전에 경직으로 끊기면 "하려고 했다"로 보고 다시 설 수 있게 두되,
/// <see cref="MaxHowlCancels"/>번째 취소에서 기회를 닫습니다. 기획 결정(2026-08-07)이며 공용 문서 §5.5.1의
/// "전파 전 취소도 재시도하지 않는다"와 어긋나 문서 갱신이 필요합니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.5, `변이체 잡몹 1 콘텐츠` §7.
/// </remarks>
[DisallowMultipleComponent]
public sealed class HowlAbility : EnemyAbility
{
    [Header("Howl")]
    [Tooltip("하울링이 전달되는 고정 반경(m)입니다. 벽이나 엄폐물은 판정에 쓰지 않습니다. 적 밸런스 SO가 연결돼 있으면 그 값으로 덮어씁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_howlRadius = 25f;

    [Tooltip("하울링 시작 후 전파가 확정되는 시점(초)입니다. 이 시점 전에 경직되거나 사망하면 전파가 취소됩니다. 적 밸런스 SO가 연결돼 있으면 그 값으로 덮어씁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_howlBroadcastTime = 1.2f;

    [Tooltip("하울링 행동 전체 길이(초)입니다. 끝나면 추격으로 넘어갑니다. 전파 시점보다 짧게 입력하면 전파 시점으로 보정됩니다. 적 밸런스 SO가 연결돼 있으면 그 값으로 덮어씁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_howlDuration = 3f;

    [Header("Buff")]
    [Tooltip("하울링이 전달될 때 반경 안의 다른 적에게 걸 버프입니다. 비워 두면 버프 없이 위치 정보만 전달합니다. 하울러 본인에게도 걸지는 버프 에셋의 Apply To Source로 정합니다.")]
    [SerializeField] private EnemyBuffSO m_howlBuff;

    [Header("Debug")]
    [Tooltip("이 개체를 선택했을 때 하울링 전파 반경을 Scene 뷰에 원으로 표시합니다. 에디터 전용 표시입니다.")]
    [SerializeField] private bool m_debugDrawHowlRadius = true;

    /// <summary>전파 전 취소를 몇 번까지 허용할지입니다.</summary>
    /// <remarks>2면 첫 취소는 다시 설 수 있고 두 번째 취소에서 기회가 닫힙니다. 경직을 두 번 넣어야 완전히 막습니다.</remarks>
    private const int MaxHowlCancels = 2;

    /// <summary>하울링을 시작하는 트리거입니다. 클립은 `WW_Howl`을 씁니다.</summary>
    private static readonly int AnimHowl = Animator.StringToHash("DoHowl");

    /// <summary>하울링 스테이트를 유지하는 bool입니다.</summary>
    private static readonly int AnimIsHowl = Animator.StringToHash("IsHowl");

    private HowlState m_howlState;

    // 이 교전의 하울링 기록입니다(§5.8.4의 교전 종료 초기화 대상).
    // 대상 정보가 아니라 행동 기록이므로 센서가 아니라 능력이 소유합니다.

    /// <summary>이 교전에서 하울링 기회를 모두 썼는지 여부입니다. 전파 성공 또는 취소 허용 횟수 초과 시 참입니다.</summary>
    private bool m_howlConsumed;

    /// <summary>이 교전에서 전파 시점 전에 하울링이 끊긴 횟수입니다. 전파 뒤의 취소는 세지 않습니다(§5.5.3).</summary>
    private int m_howlCancelCount;

    /// <summary>이 교전에서 하울링 전 공격 기회를 이미 썼는지 여부입니다.</summary>
    private bool m_preHowlAttackUsed;

    /// <summary>경직이 끝난 뒤 하울링으로 이어가야 하는지 여부입니다.</summary>
    private bool m_resumeWithHowlAfterStagger;

    /// <summary>전파 반경 안에 있던 수신자를 받아 두는 재사용 목록입니다.</summary>
    private readonly List<EnemyTargetSensor> m_reachedListeners = new List<EnemyTargetSensor>();

    /// <summary>하울링이 전달되는 고정 반경(m)입니다.</summary>
    public float HowlRadius => m_howlRadius;

    /// <summary>하울링 전파가 확정되는 시점(초)입니다.</summary>
    public float HowlBroadcastTime => m_howlBroadcastTime;

    /// <summary>하울링 행동 전체 길이(초)입니다. 전파 시점보다 짧아지지 않습니다.</summary>
    public float HowlDuration => Mathf.Max(m_howlBroadcastTime, m_howlDuration);

    /// <summary>가장 최근 하울링이 적용된 주변 변이체 수입니다. 진단용이며 아직 하울링하지 않았으면 -1입니다.</summary>
    public int LastHowlAppliedCount { get; private set; } = -1;

    /// <summary>가장 최근 하울링이 위치를 제공한 스쿼드 캐릭터 수입니다. 진단용이며 아직 하울링하지 않았으면 -1입니다.</summary>
    public int LastHowlMemberCount { get; private set; } = -1;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        m_howlState = new HowlState(Owner, this);
    }

    /// <inheritdoc />
    public override void ApplyBalance(EnemyBalanceSO balance)
    {
        m_howlRadius = balance.HowlRadius;
        m_howlBroadcastTime = balance.HowlBroadcastTime;
        m_howlDuration = balance.HowlDuration;
    }

    /// <summary>
    /// 교전 진입 시 하울링을 먼저 할지 정합니다.
    /// </summary>
    /// <remarks>
    /// 그 시점에 즉시 공격할 수 있으면 공격 1회를 먼저 하고, 아니면 기다리지 않고 바로 하울링합니다(§5.5.2).
    /// 공격 기회는 성공 여부와 관계없이 이때 소모합니다.
    /// </remarks>
    public override EnemyStateBase OnCombatEnter()
    {
        if (!CanTryHowl())
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

    /// <summary>하울링 전 공격을 마친 뒤 하울링으로 넘어갑니다.</summary>
    /// <remarks>공격이 맞았는지와 관계없이 하울링으로 갑니다. 공격 기회는 이미 소모했습니다(§5.5.2).</remarks>
    public override EnemyStateBase OnLeadInAttackFinished()
    {
        return CanTryHowl() ? m_howlState : null;
    }

    /// <summary>
    /// 경직으로 끊긴 행동에 따라 경직 뒤 하울링으로 이어갈지 기록합니다.
    /// </summary>
    /// <remarks>
    /// 하울링이 전파 전에 끊겼으면 취소 횟수를 올리고, 기회가 남아 있으면 경직 뒤에 다시 섭니다.
    /// 전파를 마친 뒤 끊겼으면 결과가 유지되므로 아무것도 하지 않습니다(§5.5.3).
    /// 하울링 전 공격이 끊겼으면 하울링은 아직 쓰지 않았으므로 경직 뒤 하울링으로 갑니다(§5.5.2).
    /// </remarks>
    public override void OnStaggered(EnemyStateBase interruptedSub, bool wasOwnLeadInAttack)
    {
        if (interruptedSub == m_howlState && !m_howlState.IsBroadcastDone)
        {
            NotifyHowlCanceled();
            m_resumeWithHowlAfterStagger = CanTryHowl();
            return;
        }

        m_resumeWithHowlAfterStagger = wasOwnLeadInAttack && CanTryHowl();
    }

    /// <inheritdoc />
    public override EnemyStateBase OnStaggerEnded()
    {
        if (!m_resumeWithHowlAfterStagger)
        {
            return null;
        }

        m_resumeWithHowlAfterStagger = false;
        return CanTryHowl() ? m_howlState : null;
    }

    /// <summary>하울링 기록을 초기화합니다. 다음 교전에서 다시 한 번 하울링할 수 있습니다(§5.8.4).</summary>
    /// <remarks>경직 중에 교전이 끝났을 때 남은 이어가기 표시도 지웁니다. 남기면 다음 교전의 첫 경직이 하울링으로 이어집니다.</remarks>
    public override void OnCombatExit()
    {
        m_howlConsumed = false;
        m_howlCancelCount = 0;
        m_preHowlAttackUsed = false;
        m_resumeWithHowlAfterStagger = false;
    }

    /// <summary>하울링 전파가 실제로 일어나 기회를 소모했음을 기록합니다.</summary>
    /// <remarks>진입이 아니라 전파 시점에 소모합니다. 전파 전에 끊긴 하울링은 아무것도 전달하지 못했기 때문입니다.</remarks>
    public void MarkHowlBroadcast()
    {
        m_howlConsumed = true;
    }

    /// <summary>
    /// 주변 변이체에게 하울링을 전파하고, 반경 안의 다른 적에게 하울링 버프를 겁니다.
    /// </summary>
    /// <param name="members">위치를 제공할 스쿼드 캐릭터 목록입니다.</param>
    /// <remarks>
    /// 위치 정보와 버프 모두 마지막으로 받은 하울링 기준입니다. 이미 다른 하울링을 받은 적도 덮어쓰고,
    /// 같은 버프는 새 버프 기준으로 지속 시간을 다시 셉니다. 사망한 적과 하울러 본인은 제외하며,
    /// 본인은 버프 에셋이 허용할 때만 받습니다.
    /// 버프는 1회성 부여라서 하울러가 먼저 죽어도 지속 시간이 끝날 때까지 남습니다.
    /// </remarks>
    public void BroadcastHowl(IReadOnlyList<SquadMemberController> members)
    {
        EnemyTargetSensor sensor = Owner.Sensor;
        int applied = HowlSystem.Broadcast(sensor, transform.position, m_howlRadius, members, m_reachedListeners);

        LastHowlAppliedCount = applied;
        LastHowlMemberCount = members != null ? members.Count : 0;

        if (m_howlBuff == null)
        {
            return;
        }

        for (int i = 0; i < m_reachedListeners.Count; i++)
        {
            EnemyTargetSensor listener = m_reachedListeners[i];
            EnemyController target = listener != null ? listener.GetComponent<EnemyController>() : null;
            if (target == null || target == Owner || (target.Health != null && target.Health.IsDead))
            {
                continue;
            }

            target.ApplyBuff(m_howlBuff);
        }

        if (m_howlBuff.ApplyToSource)
        {
            Owner.ApplyBuff(m_howlBuff);
        }

        m_reachedListeners.Clear();
    }

    /// <summary>하울링 애니메이션을 시작합니다. 파라미터가 없으면 건너뜁니다.</summary>
    public void PlayHowlAnimation()
    {
        Owner.PlayActionAnimation(AnimHowl, AnimIsHowl);
    }

    /// <summary>하울링 스테이트에서 빠져나왔음을 애니메이터에 알립니다.</summary>
    public void EndHowlAnimation()
    {
        Owner.EndActionAnimation(AnimIsHowl);
    }

    /// <summary>
    /// 클립의 하울링 전파 애니메이션 이벤트를 받습니다.
    /// </summary>
    /// <remarks>
    /// 문서 확정본의 이벤트 이름은 `HowlBroadcast`이며, 클립에 이벤트를 넣을 때 이 메서드를 가리키게 합니다.
    /// 하울링 중이 아닐 때 들어오면 무시합니다. 타이머로 이미 전파했다면 상태가 중복을 막습니다.
    /// </remarks>
    public void HowlBroadcast()
    {
        if (Owner == null || Owner.Current != Owner.Combat || Owner.Combat.CurrentSub != m_howlState)
        {
            return;
        }

        m_howlState.NotifyAnimationBroadcast();
    }

    /// <summary>
    /// 지금 하울링을 시도할 수 있는지 판단합니다.
    /// </summary>
    /// <remarks>
    /// 하울링을 받아 합류했다면 시도하지 않습니다. 이미 독립적으로 교전 중이었다면 이후 하울링을 받아도
    /// 자기 시도를 유지해야 하므로(§5.5.1), 수신 기록은 진입 시점의 판단에만 영향을 줍니다.
    /// </remarks>
    private bool CanTryHowl()
    {
        if (m_howlConsumed)
        {
            return false;
        }

        EnemyTargetSensor sensor = Owner.Sensor;
        return sensor != null && !sensor.HasReceivedHowl;
    }

    /// <summary>전파 시점 전에 하울링이 끊겼음을 기록하고, 허용 횟수를 넘겼으면 기회를 닫습니다.</summary>
    private void NotifyHowlCanceled()
    {
        if (m_howlConsumed)
        {
            return;
        }

        m_howlCancelCount++;

        if (m_howlCancelCount >= MaxHowlCancels)
        {
            m_howlConsumed = true;
        }
    }

    /// <summary>Inspector 입력이 음수가 되지 않게 보정합니다.</summary>
    private void OnValidate()
    {
        m_howlRadius = Mathf.Max(0.0f, m_howlRadius);
        m_howlBroadcastTime = Mathf.Max(0.0f, m_howlBroadcastTime);
        m_howlDuration = Mathf.Max(0.0f, m_howlDuration);
    }

    /// <summary>선택한 개체의 하울링 전파 반경을 Scene 뷰에 표시합니다.</summary>
    /// <remarks>하울링은 벽을 통과하므로 원 안이면 그대로 전달됩니다. 차폐를 반영한 표시는 하지 않습니다.</remarks>
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
