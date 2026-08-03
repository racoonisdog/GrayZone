using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 비전투 배회 상태입니다. 앵커 지점 주변을 이동하며 각성 조건(현재는 시야 감지)을 확인합니다.
/// </summary>
/// <remarks>
/// 슬라이스 2: 배회 이동 + 시야 감지 → 교전 전이. 앵커는 진입 시점의 위치로 잡으므로,
/// 교전 이탈 후 재진입하면 이탈 지점 주변을 배회한다(설계 §6).
/// TODO(후속): 휴면 배치 구분, 소음/근접/경고 각성, 각성 준비(AwakenState) 경유.
/// </remarks>
public class WanderState : EnemyStateBase
{
    /// <summary>배회 기준점입니다. 진입 시점 위치로 잡습니다.</summary>
    private Vector3 m_anchor;

    /// <summary>다음 배회 목적지 갱신 가능 시각입니다.</summary>
    private float m_nextRepathTime;

    /// <summary>배회 상태를 생성합니다.</summary>
    public WanderState(EnemyController controller) : base(controller) { }

    public override void Enter()
    {
        Controller.PlayIdleFeedback();

        // 배회 기준점 = 진입 시점 위치(최초 스폰 또는 교전 이탈 지점).
        m_anchor = Controller.transform.position;

        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.WanderSpeed;
            agent.isStopped = false;
        }

        PickNewDestination();
    }

    public override void Tick()
    {
        // 캐릭터를 직접 인식하면 교전으로 전이한다. (각성 준비 경유는 후속 슬라이스.)
        // 비교전 상태이므로 센서가 AI 조작 캐릭터는 감지 대상에서 제외한다(§5.6).
        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor != null)
        {
            sensor.UpdatePerception();
            if (sensor.HasAnyValidTarget)
            {
                Controller.TransitionTo(Controller.Combat);
                return;
            }
        }

        NavMeshAgent agent = Controller.Agent;
        if (agent == null || !agent.isOnNavMesh || agent.pathPending)
        {
            return;
        }

        // 목적지에 도달했거나 갱신 주기가 지나면 새 배회 목적지를 고른다.
        bool arrived = agent.remainingDistance <= agent.stoppingDistance + 0.2f;
        if (arrived || Time.time >= m_nextRepathTime)
        {
            PickNewDestination();
        }
    }

    /// <summary>앵커 주변 NavMesh 위에서 새 배회 목적지를 골라 이동을 지시합니다.</summary>
    private void PickNewDestination()
    {
        m_nextRepathTime = Time.time + Controller.WanderInterval;

        Vector3 candidate = m_anchor + Random.insideUnitSphere * Controller.WanderRadius;
        candidate.y = Controller.transform.position.y;

        Vector3 destination = NavMesh.SamplePosition(candidate, out NavMeshHit hit, Controller.WanderRadius, NavMesh.AllAreas)
            ? hit.position
            : Controller.transform.position;

        Controller.MoveTo(destination);
    }
}
