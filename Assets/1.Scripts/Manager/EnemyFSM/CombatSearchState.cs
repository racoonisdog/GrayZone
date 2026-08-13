using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 교전 하위 상태: 마지막 확인 위치로 달려가 그 주변을 한 번 수색합니다.
/// </summary>
/// <remarks>
/// 실시간 위치가 끊겼다고 곧바로 교전을 끝내지 않게 하는 상태입니다. 한 번 피격당했거나 직접 인식했던 개체는
/// 대상을 놓쳐도 <b>마지막으로 본 곳까지는 찾아옵니다.</b> 그 수색이 실패했을 때만 교전이 끝납니다(§5.8.4).
/// 이 상태가 없으면 시야 유지 시간이 끝나는 순간 배회로 돌아가 "총 맞고도 그냥 잊는" 모습이 됩니다.
///
/// <b>교전 상태는 유지됩니다</b>(§5.8.3). 그래서 비전투 감지 보호를 다시 적용하지 않고, 경직 누적치도 남습니다.
/// 소음 수색(<see cref="NoiseSearchState"/>)과 이동 방식은 같지만 상태와 종료 결과가 달라 별도 상태로 둡니다.
///
/// 수색 시작 지점은 <b>진입할 때 한 번만</b> 고릅니다(§5.8.2). 저장된 다른 마지막 확인 위치들을 순서대로
/// 돌며 반복 수색하지 않습니다.
///
/// 설계 근거: 공용 `적 시스템` v0.2 §5.8.2(마지막 확인 위치), §5.8.3(공통 수색 행동), §5.8.4(교전 상태 종료).
/// </remarks>
public class CombatSearchState : EnemyStateBase
{
    /// <summary>이번 수색의 기준 지점입니다. 진입할 때 한 번만 정합니다.</summary>
    private Vector3 m_searchOrigin;

    /// <summary>기준 지점에 도착했는지 여부입니다. 도착 전에는 수색 시간이 흐르지 않습니다.</summary>
    private bool m_arrived;

    /// <summary>수색 시간이 끝나는 시각입니다. 도착한 뒤에 정합니다.</summary>
    private float m_searchEndTime;

    /// <summary>교전 수색 상태를 생성합니다.</summary>
    public CombatSearchState(EnemyController controller) : base(controller) { }

    /// <summary>
    /// 이 상태로 들어갈 수 있는지, 즉 쓸 수 있는 마지막 확인 위치가 있는지 확인합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="CombatState"/>가 수색과 교전 종료 중 무엇을 할지 정할 때 씁니다.
    /// 쓸 수 있는 위치가 없으면 교전을 끝내는 것이 문서 규칙입니다(§5.8.4).
    /// </remarks>
    public bool CanSearch()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        return sensor != null && sensor.TryGetSearchPosition(out _);
    }

    /// <summary>마지막 확인 위치를 한 번 골라 추격 속도로 그곳을 향합니다.</summary>
    /// <remarks>이동 속도는 추격과 같습니다(§5.8.3). 수색이라고 느려지면 놓친 대상을 따라잡을 수 없습니다.</remarks>
    public override void Enter()
    {
        m_arrived = false;
        m_searchEndTime = 0.0f;

        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor == null || !sensor.TryGetSearchPosition(out m_searchOrigin))
        {
            // 진입 직전에 위치가 사라진 경우입니다. 다음 틱에서 교전 종료로 처리됩니다.
            m_searchOrigin = Controller.transform.position;
            m_arrived = true;
            m_searchEndTime = Time.time;
            return;
        }

        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.ChaseSpeed;
            agent.isStopped = false;
        }

        Controller.MoveTo(m_searchOrigin);
    }

    /// <summary>기준 지점까지 이동한 뒤 그 주변을 배회하며 대상을 다시 찾습니다.</summary>
    /// <remarks>
    /// 대상을 다시 직접 인식하면 교전 상태를 유지한 채 추격으로 돌아갑니다(§5.8.3).
    /// 대상 재확보 판정은 엄브렐라(<see cref="CombatState"/>)가 매 틱 하므로 여기서는 결과만 봅니다.
    /// </remarks>
    public override void Tick()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor == null)
        {
            return;
        }

        // 다시 찾았으면 수색을 끝내고 추격으로 돌아갑니다. 사거리 안이면 추격이 곧바로 공격으로 넘깁니다.
        // 하울링을 받아 유효 대상이 생긴 경우도 이 경로로 들어옵니다(§5.5, 393줄: 교전 유지한 채 후보에 추가).
        if (sensor.CurrentTarget != null)
        {
            Controller.Combat.SetSubState(Controller.Combat.Chase);
            return;
        }

        // 새 소음을 받아들이면 수색을 중단하고 그 위치로 옮겨 갑니다(§5.8.3).
        // 상태를 바꾸지 않는 것이 핵심입니다. 소음 수색과 달리 교전 수색은 교전 상태와 경직 누적치를
        // 유지해야 하므로, 여기서 NoiseChase로 전이하면 비교전으로 떨어져 규칙이 깨집니다.
        if (sensor.ConsumeNoiseUpdated() && sensor.TryGetNoisePosition(out Vector3 noisePosition))
        {
            m_searchOrigin = noisePosition;

            // 도착 전으로 되돌립니다. 이동하는 동안 수색 시간은 흐르지 않고, 도착하면 전체 시간을
            // 처음부터 다시 셉니다(§5.8.3).
            m_arrived = false;
            m_searchEndTime = 0.0f;

            NavMeshAgent noiseAgent = Controller.Agent;
            if (noiseAgent != null && noiseAgent.isOnNavMesh)
            {
                noiseAgent.speed = Controller.ChaseSpeed;
                noiseAgent.isStopped = false;
            }

            Controller.MoveTo(m_searchOrigin);
            return;
        }

        if (!m_arrived)
        {
            if (!HasArrivedAtSearchOrigin())
            {
                return;
            }

            m_arrived = true;
            m_searchEndTime = Time.time + Controller.CombatSearchDuration;

            // 도착했으므로 이제 주변을 훑습니다. 속도는 배회 속도로 낮춥니다.
            NavMeshAgent arrivedAgent = Controller.Agent;
            if (arrivedAgent != null && arrivedAgent.isOnNavMesh)
            {
                arrivedAgent.speed = Controller.WanderSpeed;
            }

            PickWanderDestination(m_searchOrigin, Controller.CombatSearchRadius);
            return;
        }

        if (Time.time >= m_searchEndTime)
        {
            // 수색 실패 = 교전 상태 종료. 이 지점이 새 배회 기준점이 됩니다(§5.8.3, §5.8.4).
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        TickWanderMovement(m_searchOrigin, Controller.CombatSearchRadius);
    }

    /// <summary>수색 이동을 멈춥니다.</summary>
    public override void Exit()
    {
        Controller.StopMoving();
    }

    /// <summary>경직이 풀린 뒤 이 상태의 이동을 다시 켭니다.</summary>
    /// <remarks>도착 전에는 추격 속도, 도착 후에는 배회 속도로 되살립니다.</remarks>
    public override void ResumeMovement()
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = m_arrived ? Controller.WanderSpeed : Controller.ChaseSpeed;
        }

        base.ResumeMovement();
    }

    /// <summary>기준 지점에 도착했는지 확인합니다.</summary>
    /// <remarks>
    /// 경로가 없으면 도착한 것으로 봅니다. 갈 수 없는 지점을 향해 영원히 서 있으면 교전이 끝나지 않아
    /// 개체가 그 자리에 굳습니다. 도착 처리로 넘기면 수색 시간이 흐르고 정상적으로 교전이 종료됩니다.
    /// </remarks>
    private bool HasArrivedAtSearchOrigin()
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return true;
        }

        if (agent.pathPending)
        {
            return false;
        }

        if (agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            return true;
        }

        return agent.remainingDistance <= agent.stoppingDistance + ArrivalMargin;
    }

    /// <summary>도착 판정에 더하는 여유 거리(m)입니다.</summary>
    private const float ArrivalMargin = 0.2f;
}
