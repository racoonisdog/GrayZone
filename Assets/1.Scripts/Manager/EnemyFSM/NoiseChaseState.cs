using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 비교전 소음 추적 상태입니다. 받아들인 소음 위치로 이동합니다.
/// </summary>
/// <remarks>
/// <b>비교전 상태입니다.</b> 소음은 발생 위치만 알려주고 캐릭터를 유효 대상으로 만들지 않으므로(§5.4.4),
/// 여기서는 교전에 들어가지 않고 비전투 감지 보호도 유지됩니다. 소음만 감지한 상태에서는 하울링 조건도
/// 충족하지 않습니다(§5.5.1).
///
/// 소음 위치에 도착하면 소음 수색으로 넘어갑니다. 도중에 캐릭터를 직접 인식하면 즉시 교전에 진입합니다(§5.4.5).
/// 설계 근거: 공용 `적 시스템` v0.2 §5.4.5(소음 추적), `변이체 잡몹 1 콘텐츠` §6.3.
/// </remarks>
public class NoiseChaseState : EnemyStateBase
{
    /// <summary>지금 향하고 있는 소음 위치입니다.</summary>
    private Vector3 m_destination;

    /// <summary>소음 추적 상태를 생성합니다.</summary>
    public NoiseChaseState(EnemyController controller) : base(controller) { }

    public override void Enter()
    {
        Controller.PlayChaseFeedback();

        // 인지 게이지를 비웁니다. 남겨 두면 수색에 실패해 배회로 돌아온 순간 아직 만충이라
        // 곧바로 다시 추적으로 튕겨 나가 무한히 반복합니다(기획 확정 2026-08-03).
        Controller.Sensor?.ClearNoiseAwareness();

        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.NoiseChaseSpeed;
            agent.isStopped = false;
        }

        // 진입 시점의 소음 위치를 목적지로 잡습니다. 갱신은 Tick에서 새 소음을 받을 때만 합니다.
        RefreshDestination();
    }

    public override void Tick()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor == null)
        {
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        // 소음을 쫓는 중에도 시야는 계속 봅니다. 직접 인식하면 즉시 교전에 진입하고 하울링 조건을 충족합니다(§5.4.5).
        sensor.UpdatePerception();
        if (sensor.HasAnyValidTarget)
        {
            Controller.TransitionTo(Controller.Combat);
            return;
        }

        // 같은 소음원의 반복이든 더 강한 다른 소음이든, 센서가 이미 우선순위를 적용해 하나로 정리해 둡니다.
        // 상태는 "바뀌었는지"만 확인하고 목적지를 다시 잡습니다.
        if (sensor.ConsumeNoiseUpdated())
        {
            RefreshDestination();
        }

        if (!sensor.TryGetNoisePosition(out Vector3 _))
        {
            // 쫓을 소음이 사라졌으면 배회로 돌아갑니다. 정상 경로에서는 일어나지 않습니다.
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        NavMeshAgent agent = Controller.Agent;
        if (agent == null || !agent.isOnNavMesh || agent.pathPending)
        {
            return;
        }

        // 소음 위치에 도착하면 그 주변을 수색합니다(§5.4.5).
        float planarDistance = Vector3.Distance(
            new Vector3(Controller.transform.position.x, 0f, Controller.transform.position.z),
            new Vector3(m_destination.x, 0f, m_destination.z));

        if (planarDistance <= Controller.NoiseArriveDistance)
        {
            Controller.NoiseSearch.SetSearchCenter(m_destination);
            Controller.TransitionTo(Controller.NoiseSearch);
        }
    }

    /// <summary>센서가 들고 있는 소음 위치로 목적지를 다시 잡습니다.</summary>
    private void RefreshDestination()
    {
        if (Controller.Sensor == null || !Controller.Sensor.TryGetNoisePosition(out Vector3 position))
        {
            return;
        }

        m_destination = position;
        Controller.MoveTo(position);
    }
}
