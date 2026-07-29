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
    // 수색(SearchState)은 슬라이스 2에서 추가.

    /// <summary>현재 활성 하위 상태입니다. 지금 추격 중인지 공격 중인지 밖에서 확인할 때 씁니다.</summary>
    public EnemyStateBase CurrentSub => m_sub;

    /// <summary>교전 상태와 하위 상태들을 생성합니다.</summary>
    public CombatState(EnemyController controller) : base(controller)
    {
        Chase = new ChaseState(controller);
        Attack = new AttackState(controller);
    }

    public override void Enter()
    {
        // 교전에 들어가면 비전투 감지 보호를 해제합니다. 이제 AI 조작 캐릭터도 감지 대상입니다(§5.6).
        Controller.Sensor?.SetEngaged(true);

        // 진입 직후에는 재평가 주기를 기다리지 않고 바로 대상을 정합니다.
        Controller.Sensor?.ReevaluateTarget(true);

        SetSubState(Chase);
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
