using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 비전투 배회 상태입니다. 앵커 지점 주변을 이동하며 각성 조건(시야 감지·소음)을 확인합니다.
/// </summary>
/// <remarks>
/// 앵커는 진입 시점의 위치로 잡으므로, 교전이나 수색을 마치고 재진입하면 그 지점 주변을 배회합니다(§5.3.3).
/// 배회 중인 개체는 이미 각성한 상태이므로 소음을 감지해도 각성 준비 시간을 적용하지 않고
/// 바로 소음 추적으로 전환합니다(§5.3.3).
/// TODO(후속): 휴면 배치 구분과 각성 준비(AwakenState) 경유.
/// </remarks>
public class WanderState : EnemyStateBase
{
    /// <summary>배회 기준점입니다. 진입 시점 위치로 잡습니다.</summary>
    private Vector3 m_anchor;

    /// <summary>배회 상태를 생성합니다.</summary>
    public WanderState(EnemyController controller) : base(controller) { }

    /// <summary>진입 위치를 배회 기준점으로 잡고 대기 피드백을 재생합니다.</summary>
    /// <remarks>기준점을 여기서 잡으므로 교전이나 수색을 마치고 돌아오면 그 지점 주변을 배회합니다.</remarks>
    public override void Enter()
    {
        Controller.PlayIdleFeedback();

        // 배회 기준점 = 진입 시점 위치(최초 스폰, 교전 이탈 지점 또는 수색 종료 지점).
        m_anchor = Controller.transform.position;

        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.WanderSpeed;
            agent.isStopped = false;
        }

        PickWanderDestination(m_anchor, Controller.WanderRadius);
    }

    /// <summary>경직이 풀린 뒤 배회 속도로 이동을 다시 켭니다.</summary>
    /// <remarks>
    /// <see cref="Enter"/>를 다시 부르지 않는 이유는 그러면 배회 기준점이 현재 위치로 다시 잡혀,
    /// 경직을 당할 때마다 배회 범위가 밀려나기 때문입니다. 기준점은 그대로 두고 속도만 되살립니다.
    /// </remarks>
    public override void ResumeMovement()
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.speed = Controller.WanderSpeed;
        }

        base.ResumeMovement();
    }

    /// <summary>기준점 주변을 배회하면서 각성 조건을 확인하고 해당하는 상태로 전이합니다.</summary>
    /// <remarks>배회 중인 개체는 이미 각성해 있으므로 소음을 들으면 준비 시간 없이 바로 소음 추적으로 넘어갑니다.</remarks>
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

            // 소음이 들리면 곧바로 추적하지 않고 먼저 경계(두리번)로 넘어갑니다.
            // 인지 게이지가 한계에 닿아야 소음 위치로 이동합니다(기획 확정 2026-08-03).
            // 직접 인식을 먼저 보는 것은 소음보다 확실한 정보이기 때문입니다.
            //
            // 게이지가 이미 만충이면 경계를 건너뛰고 바로 추적합니다.
            // 가까운 총성처럼 한 번에 한계를 넘는 소음에서 두리번 한 프레임이 끼는 것을 막습니다.
            if (sensor.IsNoiseAwarenessFull)
            {
                Controller.TransitionTo(Controller.NoiseChase);
                return;
            }

            if (sensor.IsNoiseAlert)
            {
                Controller.TransitionTo(Controller.Alert);
                return;
            }
        }

        TickWanderMovement(m_anchor, Controller.WanderRadius);
    }
}
