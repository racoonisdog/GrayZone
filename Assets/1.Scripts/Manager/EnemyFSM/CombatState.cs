using UnityEngine;

/// <summary>
/// 교전 엄브렐라(composite) 상태입니다. 교전 공통 로직을 처리하고 하위 상태(추격/공격/수색)를 구동합니다.
/// </summary>
/// <remarks>
/// 공통 로직을 하위 상태 실행 前에 매 틱 한 번 처리해 하위 중복을 없애는 것이 엄브렐라의 핵심입니다.
/// 추격과 공격이 각자 대상을 다시 고르면 두 상태의 판단이 어긋날 수 있으므로 대상 선정은 여기서만 합니다.
/// 슬라이스 1: 감지 갱신 · 대상 재평가 · 대상 소실 시 배회 복귀까지.
/// 교전 수색(마지막 확인 위치 기반)과 경직력 누적은 슬라이스 2에서 추가하며,
/// 그 전까지는 유효 대상이 없어지면 바로 교전을 끝냅니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.7(대상 정보와 대상 선정), §5.8.4(교전 상태 종료).
/// </remarks>
public class CombatState : EnemyStateBase
{
    private EnemyStateBase m_sub;

    /// <summary>추격 하위 상태입니다.</summary>
    public ChaseState Chase { get; private set; }

    /// <summary>공격 하위 상태입니다.</summary>
    public AttackState Attack { get; private set; }

    /// <summary>하울링 하위 상태입니다.</summary>
    public HowlState Howl { get; private set; }

    /// <summary>교전 수색 하위 상태입니다.</summary>
    public CombatSearchState Search { get; private set; }

    /// <summary>현재 활성 하위 상태입니다. 지금 추격 중인지 공격 중인지 밖에서 확인할 때 씁니다.</summary>
    public EnemyStateBase CurrentSub => m_sub;

    // =========================
    // 하울링 기록 (§5.8.4의 교전 종료 초기화 대상)
    // =========================
    // 대상 정보가 아니라 행동 기록이므로 센서가 아니라 교전 상태가 소유합니다.
    // 교전 상태는 개체마다 하나만 만들어 재사용되므로 Enter/Exit 사이에 값이 유지됩니다.

    /// <summary>이 교전에서 하울링 기회를 모두 썼는지 여부입니다.</summary>
    /// <remarks>
    /// 전파에 성공했거나, 전파 전 취소가 허용 횟수를 넘겼을 때 참이 됩니다.
    /// </remarks>
    private bool m_howlConsumed;

    /// <summary>이 교전에서 전파 시점 전에 하울링이 끊긴 횟수입니다.</summary>
    /// <remarks>
    /// <b>전파 전 취소는 "하려고 했다"로 봅니다.</b> 끊긴 하울링은 주변에 아무것도 전달하지 못했으므로
    /// 시도를 소모한 것으로 치지 않습니다. 다만 무한히 다시 서게 두면 경직으로 저지하는 플레이가
    /// 성립하지 않으므로, <see cref="MaxHowlCancels"/>번째 취소에서 기회를 닫습니다.
    ///
    /// 전파 시점을 지난 뒤의 취소는 이 횟수에 넣지 않습니다. 그때는 이미 전달이 끝나 목적을 달성했고,
    /// 남은 연출만 끊긴 것이기 때문입니다(§5.5.3).
    ///
    /// 기획 결정(2026-08-07, 사용자). 공용 문서 §5.5.1은 "전파 전 취소도 재시도하지 않는다"로 되어 있어
    /// 이 규칙과 어긋납니다. 문서 갱신이 필요합니다.
    /// </remarks>
    private int m_howlCancelCount;

    /// <summary>전파 전 취소를 몇 번까지 다시 시도하게 둘지입니다.</summary>
    /// <remarks>
    /// 2면 "첫 취소는 다시 설 수 있고, 두 번째 취소에서 끝"이 됩니다. 플레이어 입장에서는 경직을 두 번
    /// 넣어야 하울링을 완전히 막는 셈입니다.
    /// </remarks>
    private const int MaxHowlCancels = 2;

    /// <summary>이 교전에서 하울링 전 공격 기회를 이미 썼는지 여부입니다.</summary>
    private bool m_preHowlAttackUsed;

    /// <summary>교전 상태와 하위 상태들을 생성합니다.</summary>
    public CombatState(EnemyController controller) : base(controller)
    {
        Chase = new ChaseState(controller);
        Attack = new AttackState(controller);
        Howl = new HowlState(controller);
        Search = new CombatSearchState(controller);
    }

    /// <summary>교전을 열고 첫 대상을 고른 뒤 하위 상태를 시작합니다.</summary>
    public override void Enter()
    {
        Controller.PlayAlertFeedback();

        // 교전에 들어가면 비전투 감지 보호를 해제합니다. 이제 AI 조작 캐릭터도 감지 대상입니다(§5.6).
        Controller.Sensor?.SetEngaged(true);

        // 진입 직후에는 재평가 주기를 기다리지 않고 바로 대상을 정합니다.
        Controller.Sensor?.ReevaluateTarget(true);

        SetSubState(ResolveEntrySubState());
    }

    /// <summary>
    /// 교전 진입 시 첫 하위 상태를 고릅니다.
    /// </summary>
    /// <remarks>
    /// 하울링 조건을 충족하고 아직 시도하지 않았다면 하울링이 먼저입니다.
    /// 그 시점에 즉시 공격할 수 있으면 근접 공격 1회를 먼저 수행하고 그 뒤에 하울링합니다(§5.5.2).
    /// 공격할 수 없으면 기다리지 않고 바로 하울링합니다.
    ///
    /// 하울링을 수신해 합류한 개체는 하울링하지 않습니다(§5.5.1). 이것이 맞하울링을 막는 규칙입니다.
    /// </remarks>
    private EnemyStateBase ResolveEntrySubState()
    {
        if (!CanTryHowl())
        {
            return Chase;
        }

        // 하울링 전 공격: 즉시 때릴 수 있을 때만 1회. 기회는 성공 여부와 무관하게 소모합니다(§5.5.2).
        if (!m_preHowlAttackUsed && CanAttackImmediately())
        {
            m_preHowlAttackUsed = true;
            Attack.BeginAsPreHowlAttack();
            return Attack;
        }

        return Howl;
    }

    /// <summary>
    /// 지금 하울링을 시도할 수 있는지 판단합니다.
    /// </summary>
    /// <remarks>
    /// 하울링을 수신해 합류했다면 시도하지 않습니다. 다만 이미 독립적으로 교전 중이었다면
    /// 이후 하울링을 수신해도 자신의 시도를 유지해야 하므로(§5.5.1), 수신 기록은 진입 시점에만 봅니다.
    /// 진입 이후에 받은 하울링은 이 판단에 영향을 주지 않습니다.
    /// </remarks>
    private bool CanTryHowl()
    {
        if (m_howlConsumed)
        {
            return false;
        }

        EnemyTargetSensor sensor = Controller.Sensor;
        return sensor != null && !sensor.HasReceivedHowl;
    }

    /// <summary>현재 대상을 지금 바로 때릴 수 있는지 확인합니다.</summary>
    private bool CanAttackImmediately()
    {
        SquadMemberController target = Controller.Sensor != null ? Controller.Sensor.CurrentTarget : null;
        return target != null && Controller.Attack != null && Controller.Attack.CanStartAttack(target);
    }

    /// <summary>
    /// 하울링 전파가 실제로 일어나 기회를 소모했음을 기록합니다.
    /// </summary>
    /// <remarks>
    /// <b>진입이 아니라 전파 시점에 소모합니다.</b> 전파 전에 끊긴 하울링은 주변에 아무것도 전달하지
    /// 못했으므로 시도를 쓴 것으로 보지 않습니다(<see cref="NotifyHowlCanceled"/>).
    /// 전파가 끝난 뒤에는 남은 연출이 끊겨도 결과가 유지되므로(§5.5.3) 여기서 확정합니다.
    /// </remarks>
    public void MarkHowlBroadcast()
    {
        m_howlConsumed = true;
    }

    /// <summary>
    /// 전파 시점 전에 하울링이 끊겼음을 기록하고, 허용 횟수를 넘겼으면 기회를 닫습니다.
    /// </summary>
    /// <remarks>
    /// 첫 취소는 "하려고 했다"로 보고 다시 설 수 있게 둡니다. 허용 횟수를 넘기면 이 교전에서는 끝입니다.
    /// 무한 재시도를 두면 경직으로 하울링을 저지하는 플레이가 성립하지 않습니다.
    /// </remarks>
    public void NotifyHowlCanceled()
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

    /// <summary>경직이 끝난 뒤 하울링으로 이어가야 하는지 여부입니다.</summary>
    private bool m_resumeWithHowlAfterStagger;

    /// <summary>
    /// 경직으로 현재 하위 행동을 취소합니다.
    /// </summary>
    /// <remarks>
    /// 무엇이 끊겼는지에 따라 경직 이후가 달라집니다(공용 문서 §5.5).
    /// <para>
    /// <b>하울링 전 공격이 끊긴 경우</b>: 경직이 끝나면 바로 하울링으로 갑니다(§5.5.2). 공격 기회는
    /// 이미 소모했지만 하울링 시도는 아직 쓰지 않았기 때문입니다. 후딜레이 중 끊긴 경우도 같습니다.
    /// </para>
    /// <para>
    /// <b>하울링이 전파 전에 끊긴 경우</b>: 전달된 것이 없으므로 "하려고 했다"로 기록하고, 허용 횟수가
    /// 남아 있으면 경직 후 다시 섭니다. 전파를 이미 마쳤다면 결과가 유지되므로 그대로 둡니다(§5.5.3).
    /// </para>
    /// 판단을 <b>하위 상태를 바꾸기 전에</b> 해야 합니다. <see cref="AttackState.Exit"/>가 하울링 전 공격
    /// 표시를 지우므로, 순서를 뒤집으면 항상 false로 읽힙니다.
    /// </remarks>
    public void CancelForStagger()
    {
        // 하울링이 전파 전에 끊긴 경우: 전달된 것이 없으므로 "하려고 했다"로 기록하고, 허용 횟수가 남아 있으면
        // 경직이 끝난 뒤 다시 섭니다. 전파를 이미 마쳤다면 결과가 유지되므로 아무것도 하지 않습니다(§5.5.3).
        if (m_sub == Howl && !Howl.IsBroadcastDone)
        {
            NotifyHowlCanceled();
            m_resumeWithHowlAfterStagger = CanTryHowl();

            SetSubState(Chase);
            return;
        }

        // 하울링 전 공격이 끊긴 경우: 공격 기회는 소모했지만 하울링은 아직 남아 있으므로 경직 후 하울링으로
        // 이어집니다(§5.5.2).
        m_resumeWithHowlAfterStagger = m_sub == Attack && Attack.IsPreHowlAttack && CanTryHowl();

        SetSubState(Chase);
    }

    /// <summary>
    /// 경직이 끝났을 때 이어갈 하위 상태를 고릅니다.
    /// </summary>
    /// <remarks>
    /// 끊긴 것이 하울링 전 공격이거나 전파 전 하울링이고 기회가 남았으면 하울링으로 갑니다.
    /// 그 밖에는 <b>추격을 다시 시작</b>합니다.
    ///
    /// 추격을 다시 켜는 것이 핵심입니다. 경직에 들어갈 때 하위 상태를 <see cref="Chase"/>로 바꾸지만
    /// 그 직후 잠금이 <c>StopMoving</c>으로 이동을 멈춥니다. 경직이 끝나도 이미 <see cref="Chase"/>였기 때문에
    /// <see cref="SetSubState"/>는 같은 상태로 보고 아무것도 하지 않고, 결과적으로 이동을 다시 켜는 곳이 없어
    /// 개체가 제자리에 선 채로 남습니다. 하울링 기회를 모두 쓴 개체가 일어나서 추격을 포기하던 증상이 이것입니다.
    /// </remarks>
    public void ResumeAfterStagger()
    {
        if (!m_resumeWithHowlAfterStagger)
        {
            // 잠금이 멈춘 이동을 다시 켭니다. 상태는 그대로이므로 진입 처리 전체가 아니라 이동만 되살립니다.
            m_sub?.ResumeMovement();
            return;
        }

        m_resumeWithHowlAfterStagger = false;
        SetSubState(CanTryHowl() ? Howl : Chase);
    }

    /// <summary>하울링 전 공격을 마친 뒤 하울링으로 넘어갑니다.</summary>
    /// <remarks>
    /// <see cref="AttackState"/>가 하울링 전 공격을 끝냈을 때 부릅니다.
    /// 공격이 끊겼는지와 무관하게 하울링으로 갑니다 - 기회는 이미 소모됐습니다(§5.5.2).
    /// </remarks>
    public void ContinueToHowlAfterPreAttack()
    {
        SetSubState(CanTryHowl() ? Howl : Chase);
    }

    /// <summary>교전 공통 로직을 먼저 처리한 뒤 하위 상태를 구동합니다.</summary>
    /// <remarks>
    /// 감지 갱신과 대상 재평가를 <b>하위 상태 실행 전에 한 번만</b> 합니다. 그것이 엄브렐라의 핵심입니다.
    /// 대상 선정을 여기서만 하므로 추격과 공격의 판단이 어긋나지 않습니다. 유효 대상이 없어지면 교전을 끝냅니다.
    /// </remarks>
    public override void Tick()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor == null)
        {
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        // 하위 상태보다 먼저: 무엇을 아는지 갱신하고, 그 결과로 누구를 칠지 정합니다.
        sensor.UpdatePerception();
        sensor.ReevaluateTarget();

        if (!sensor.HasAnyValidTarget || IsStuckWithNoReachableTarget(sensor))
        {
            // 유효 대상이 없다고 곧바로 교전을 끝내지 않습니다. 마지막으로 본 곳까지는 찾아가야 하며,
            // 그 수색이 실패했을 때만 교전이 끝납니다(§5.8.4).
            // 이 단계를 건너뛰면 시야 유지 시간이 끝나는 순간 배회로 돌아가 "총 맞고도 그냥 잊는" 모습이 됩니다.
            if (m_sub != Search)
            {
                if (Search.CanSearch())
                {
                    SetSubState(Search);
                }
                else
                {
                    // 쓸 수 있는 마지막 확인 위치조차 없는 경우입니다(§5.8.4 두 번째 조건).
                    Controller.TransitionTo(Controller.Wander);
                    return;
                }
            }

            // 수색 중에는 하위 상태가 스스로 종료를 판단하므로 아래로 흘려보냅니다.
        }

        m_sub?.Tick();
    }

    /// <summary>갈 수 있는 대상이 없는 상태가 시작된 시각입니다. 없으면 0입니다.</summary>
    private float m_noReachableTargetSince;

    /// <summary>이 시간(초) 넘게 갈 수 있는 대상이 없으면 교착으로 봅니다.</summary>
    /// <remarks>
    /// 경로 계산은 한두 프레임 실패했다가 곧 성공하기도 합니다(경로 계산 대기, 순간적인 NavMesh 이탈).
    /// 즉시 판정하면 그런 흔들림에도 수색으로 튀어 추격이 끊깁니다.
    /// </remarks>
    private const float NoReachableTargetGrace = 1.0f;

    /// <summary>
    /// 유효 대상은 있는데 갈 수 있는 경로가 없는 상태로 굳었는지 판단합니다.
    /// </summary>
    /// <remarks>
    /// <b>이 검사가 없으면 개체가 영원히 제자리에 섭니다.</b> 두 판정의 기준이 다르기 때문입니다.
    /// <see cref="EnemyTargetSensor.HasAnyValidTarget"/>은 위치를 아는지만 보고,
    /// <see cref="EnemyTargetSensor.CurrentTarget"/>은 <c>ReevaluateTarget</c>이 경로(PathComplete)까지
    /// 확인해 고릅니다. 그래서 대상이 닿을 수 없는 곳에 있으면 앞은 true인데 뒤는 null이 되고,
    /// <see cref="ChaseState"/>는 매 프레임 <c>StopMoving</c>만 부르며 아무 데도 가지 않습니다.
    ///
    /// 특히 <b>하울링을 받은 개체</b>에서 이 교착이 영구적입니다. 하울링 위치 정보는 시간으로 만료되지 않고
    /// 교전이 끝날 때만 지워지는데(2026-08-04 확정), 그 교전이 끝나려면 유효 대상이 없어져야 하므로
    /// 서로를 기다리며 빠져나오지 못합니다.
    ///
    /// 교착으로 판정되면 유효 대상이 없을 때와 같은 경로를 탑니다. 마지막 확인 위치로 수색을 가고,
    /// 그것도 없으면 교전을 끝냅니다(§5.8.4). 갈 수 없는 상대를 노려보며 서 있는 것보다 낫습니다.
    /// </remarks>
    private bool IsStuckWithNoReachableTarget(EnemyTargetSensor sensor)
    {
        if (sensor.CurrentTarget != null)
        {
            m_noReachableTargetSince = 0.0f;
            return false;
        }

        if (m_noReachableTargetSince <= 0.0f)
        {
            m_noReachableTargetSince = Time.time;
            return false;
        }

        return Time.time - m_noReachableTargetSince >= NoReachableTargetGrace;
    }

    /// <summary>하위 상태를 끝내고 교전 중에만 유지되던 상태를 초기화합니다.</summary>
    public override void Exit()
    {
        m_sub?.Exit();
        m_sub = null;

        // 교전이 끝나면 알고 있던 내용을 비우고 비전투 감지 보호를 다시 적용합니다(§5.8.4).
        Controller.Sensor?.ClearAllInfo();
        Controller.Sensor?.SetEngaged(false);

        // 하울링 기록도 함께 초기화합니다(§5.8.4). 다음 교전에서 다시 한 번 하울링할 수 있습니다.
        m_howlConsumed = false;
        m_howlCancelCount = 0;
        m_preHowlAttackUsed = false;

        // 경직 중에 교전이 끝나면 이어갈 하울링도 사라집니다. 남겨 두면 다음 교전의 첫 경직이
        // 엉뚱하게 하울링으로 이어집니다.
        m_resumeWithHowlAfterStagger = false;

        // 교착 감시 시계도 초기화합니다. 남겨 두면 다음 교전이 시작하자마자 교착으로 잘못 판정됩니다.
        m_noReachableTargetSince = 0.0f;

        // 경직력 누적도 비웁니다(§6). 체력과 달리 경직은 교전 안에서만 의미가 있어,
        // 한참 뒤에 다시 마주친 개체가 예전에 맞은 값 때문에 한 발에 경직되면 어긋납니다.
        Controller.Health?.ResetStagger();
    }

    /// <summary>교전 하위 상태를 전환합니다.</summary>
    /// <param name="next">전환할 하위 상태입니다.</param>
    public void SetSubState(EnemyStateBase next)
    {
        if (next == null || next == m_sub)
        {
            return;
        }

        m_sub?.Exit();
        m_sub = next;
        m_sub.Enter();
    }
}
