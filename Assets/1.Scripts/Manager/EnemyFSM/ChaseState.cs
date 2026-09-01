using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 교전 하위 상태: 현재 대상을 이동 가능한 최단 경로로 추격합니다.
/// </summary>
/// <remarks>
/// 대상을 고르는 것은 <see cref="CombatState"/>의 몫이고, 이 상태는 정해진 대상에게 다가가기만 합니다.
/// 추격 거리 제한이나 원래 자리로 강제 귀환하는 규칙은 두지 않습니다.
/// 실시간 위치를 아는 동안에는 배회 범위를 벗어나도 계속 쫓습니다(§5.8.1).
/// 마지막 확인 위치로 이동하는 갈래는 별도 상태가 아니라 이 상태의 한 모드로 슬라이스 2에서 추가합니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.8.1(추격), §5.9.1(공격 시작 조건).
/// </remarks>
public class ChaseState : EnemyStateBase
{
    /// <summary>추격 상태를 생성합니다.</summary>
    public ChaseState(EnemyController controller) : base(controller) { }

    /// <summary>추격 속도로 바꾸고 추격 피드백을 재생합니다.</summary>
    public override void Enter()
    {
        Controller.PlayChaseFeedback();

        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.ChaseSpeed;
            agent.isStopped = false;
        }
    }

    /// <summary>정해진 대상에게 최단 경로로 다가가고, 공격 시작 조건이 서면 공격으로 넘깁니다.</summary>
    /// <remarks>대상을 고르는 것은 <see cref="CombatState"/>의 몫입니다. 추격 거리 제한이나 강제 귀환은 두지 않습니다.</remarks>
    public override void Tick()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        SquadMemberController target = sensor != null ? sensor.CurrentTarget : null;
        if (target == null)
        {
            // 여기서 멈추는 것은 "실시간 위치는 아는데 갈 경로가 없다"는 뜻입니다.
            // 유효 대상이 아예 없어졌다면 CombatState가 배회로 보내므로 이 분기까지 오지 않습니다.
            //
            // 두 판정이 갈리는 이유: HasAnyValidTarget은 위치를 아는지만 보고,
            // CurrentTarget은 ReevaluateTarget이 경로(PathComplete)까지 확인해 고릅니다.
            // 그래서 개체가 NavMesh 밖에 있거나 대상이 닿을 수 없는 곳에 있으면 이쪽이 null이 됩니다.
            //
            // ReevaluateTarget은 현재 대상을 잃은 상태면 주기를 무시하고 매 틱 다시 고르므로,
            // 경로가 생기는 순간 스스로 회복합니다. 다만 개체가 NavMesh 밖에 갇히면 영원히 회복하지 못해
            // 제자리에 굳습니다. 그 원인은 경직 루트 모션이었고 EnemyController가 NavMesh 위로 붙잡습니다.
            Controller.StopMoving();
            return;
        }

        // 공격을 시작할 수 있으면 이동보다 공격이 우선입니다.
        if (Controller.Attack != null && Controller.Attack.CanStartAttack(target))
        {
            Controller.Combat.SetSubState(Controller.Combat.Attack);
            return;
        }

        if (sensor.TryGetCurrentTargetPosition(out Vector3 destination))
        {
            Controller.MoveTo(destination);
        }

        FaceTowards(target.transform.position);
    }

    /// <summary>추격 이동을 멈춥니다.</summary>
    public override void Exit()
    {
        Controller.StopMoving();
    }

    /// <summary>경직이 풀린 뒤 추격 속도로 이동을 다시 켭니다.</summary>
    /// <remarks>
    /// 속도는 상태마다 다르므로(배회 2 / 추격 6) 기본 구현에 맡기지 않고 여기서 자기 값을 되살립니다.
    /// 목적지는 <see cref="Tick"/>이 매 프레임 다시 지정하므로 건드리지 않습니다.
    /// </remarks>
    public override void ResumeMovement()
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.ChaseSpeed;
        }

        base.ResumeMovement();
    }

    // 대상을 향한 회전은 EnemyStateBase.FaceTowards가 공격 준비와 함께 소유합니다.
    // NavMeshAgent의 자동 회전만으로는 공격 시작 허용 방향각을 채우기까지 느려서 따로 돌려 줍니다.
}
