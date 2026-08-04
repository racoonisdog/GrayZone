using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 비교전 소음 수색 상태입니다. 소음이 났던 자리 주변을 배회하며 캐릭터를 찾습니다.
/// </summary>
/// <remarks>
/// 배회와 <b>이동 방식만</b> 공유하고 상태는 따로 둡니다(§5.8.3). 종료 결과가 다르기 때문입니다.
/// 수색에 실패하면 그 지점을 새 배회 기준점으로 삼아 배회로 돌아갑니다.
///
/// 교전 수색과도 다른 상태입니다. 이쪽은 비교전이라 비전투 감지 보호가 유지되고 경직 누적치를 쓰지 않습니다.
/// 하나로 합치면 그 차이를 구분할 수 없습니다.
///
/// 설계 근거: 공용 `적 시스템` v0.2 §5.4.5(소음 추적), §5.8.3(공통 수색 행동), `변이체 잡몹 1 콘텐츠` §6.3.
/// </remarks>
public class NoiseSearchState : EnemyStateBase
{
    /// <summary>수색의 중심입니다. 소음이 났던 자리입니다.</summary>
    private Vector3 m_center;

    /// <summary>수색을 끝낼 시각입니다.</summary>
    private float m_endTime;

    /// <summary>소음 수색 상태를 생성합니다.</summary>
    public NoiseSearchState(EnemyController controller) : base(controller) { }

    /// <summary>
    /// 수색 중심을 지정합니다. 상태에 들어가기 전에 호출합니다.
    /// </summary>
    /// <param name="center">수색의 중심이 될 지점입니다.</param>
    /// <remarks>
    /// 진입 시점의 자기 위치를 쓰지 않고 넘겨받는 이유는, 도착 판정에 허용 거리가 있어서
    /// 실제 소음 위치와 조금 떨어진 곳에서 멈출 수 있기 때문입니다. 소리가 난 자리를 중심으로 삼아야 합니다.
    /// </remarks>
    public void SetSearchCenter(Vector3 center)
    {
        m_center = center;
    }

    public override void Enter()
    {
        Controller.PlayIdleFeedback();

        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            // 수색 중 이동은 배회 방식이므로 배회 속도를 씁니다(§5.8.3).
            agent.speed = Controller.WanderSpeed;
            agent.isStopped = false;
        }

        m_endTime = Time.time + Controller.NoiseSearchDuration;
        PickWanderDestination(m_center, Controller.NoiseSearchRadius);
    }

    public override void Tick()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor == null)
        {
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        // 수색의 목적은 직접 인식입니다. 성공하면 교전에 진입하고 하울링 조건을 충족합니다(§5.8.3).
        sensor.UpdatePerception();
        if (sensor.HasAnyValidTarget)
        {
            Controller.TransitionTo(Controller.Combat);
            return;
        }

        // 수색 중 새 소음을 받아들이면 수색을 중단하고 다시 소음 추적으로 전환합니다(§5.4.5).
        // 새 위치에 도착하면 전체 수색 시간을 처음부터 시작하므로, 여기서 남은 시간을 이어받지 않습니다.
        if (sensor.ConsumeNoiseUpdated())
        {
            Controller.TransitionTo(Controller.NoiseChase);
            return;
        }

        if (Time.time >= m_endTime)
        {
            // 수색 실패: 이 지점을 새 배회 기준점으로 삼아 배회합니다(§5.4.5).
            // 소음 기록을 지우지 않으면 배회 상태가 같은 소음을 다시 집어 추적과 수색을 무한히 반복합니다.
            sensor.ClearNoise();
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        TickWanderMovement(m_center, Controller.NoiseSearchRadius);
    }
}
