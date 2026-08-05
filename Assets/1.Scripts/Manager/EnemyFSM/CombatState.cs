/// <summary>
/// 교전 엄브렐라(composite) 상태입니다. 교전 공통 로직을 처리하고 하위 상태(추격/공격)를 구동합니다.
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
    // 수색(SearchState)은 슬라이스 2에서 추가.

    /// <summary>현재 활성 하위 상태입니다. 지금 추격 중인지 공격 중인지 밖에서 확인할 때 씁니다.</summary>
    public EnemyStateBase CurrentSub => m_sub;

    // =========================
    // 하울링 기록 (§5.8.4의 교전 종료 초기화 대상)
    // =========================
    // 대상 정보가 아니라 행동 기록이므로 센서가 아니라 교전 상태가 소유합니다.
    // 교전 상태는 개체마다 하나만 만들어 재사용되므로 Enter/Exit 사이에 값이 유지됩니다.

    /// <summary>이 교전에서 하울링을 이미 시도했는지 여부입니다.</summary>
    /// <remarks>성공·취소와 무관하게 소모됩니다(§5.5.1).</remarks>
    private bool m_howlAttempted;

    /// <summary>이 교전에서 하울링 전 공격 기회를 이미 썼는지 여부입니다.</summary>
    private bool m_preHowlAttackUsed;

    /// <summary>교전 상태와 하위 상태들을 생성합니다.</summary>
    public CombatState(EnemyController controller) : base(controller)
    {
        Chase = new ChaseState(controller);
        Attack = new AttackState(controller);
        Howl = new HowlState(controller);
    }

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
        if (m_howlAttempted)
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

    /// <summary>하울링 시도 기록을 소모합니다.</summary>
    /// <remarks><see cref="HowlState"/>가 진입할 때 부릅니다.</remarks>
    public void MarkHowlAttempted()
    {
        m_howlAttempted = true;
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

        if (!sensor.HasAnyValidTarget)
        {
            // 슬라이스 2에서는 여기서 마지막 확인 위치를 골라 교전 수색으로 들어갑니다.
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        m_sub?.Tick();
    }

    public override void Exit()
    {
        m_sub?.Exit();
        m_sub = null;

        // 교전이 끝나면 알고 있던 내용을 비우고 비전투 감지 보호를 다시 적용합니다(§5.8.4).
        Controller.Sensor?.ClearAllInfo();
        Controller.Sensor?.SetEngaged(false);

        // 하울링 기록도 함께 초기화합니다(§5.8.4). 다음 교전에서 다시 한 번 하울링할 수 있습니다.
        m_howlAttempted = false;
        m_preHowlAttackUsed = false;
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
