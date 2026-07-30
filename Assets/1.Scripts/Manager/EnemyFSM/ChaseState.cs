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

    public override void Enter()
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.ChaseSpeed;
            agent.isStopped = false;
        }
    }

    public override void Tick()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        SquadMemberController target = sensor != null ? sensor.CurrentTarget : null;
        if (target == null)
        {
            // 대상 소실 자체는 CombatState가 다음 틱에 처리합니다. 여기서는 멈추기만 합니다.
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

        FaceTarget(target);
    }

    public override void Exit()
    {
        Controller.StopMoving();
    }

    /// <summary>
    /// 이동 중에도 대상을 향해 회전합니다.
    /// </summary>
    /// <remarks>
    /// NavMeshAgent의 자동 회전만으로는 공격 시작 허용 방향각을 채우기까지 느릴 수 있어 따로 돌려 줍니다.
    /// </remarks>
    private void FaceTarget(SquadMemberController target)
    {
        Vector3 delta = target.transform.position - Controller.transform.position;
        delta.y = 0f;

        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion desired = Quaternion.LookRotation(delta.normalized);
        Controller.transform.rotation = Quaternion.Slerp(
            Controller.transform.rotation,
            desired,
            Controller.RotationSpeed * Time.deltaTime);
    }
}
