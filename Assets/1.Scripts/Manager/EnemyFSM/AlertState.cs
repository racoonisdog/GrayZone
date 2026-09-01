using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 비교전 소음 경계 상태입니다. 소음이 들렸지만 아직 알아채지 못해 제자리에서 두리번거립니다.
/// </summary>
/// <remarks>
/// 소음 인지 게이지가 한계에 닿을 때까지 머무는 구간입니다. 여기서 나가는 길은 셋입니다.
/// 게이지가 차면 소음 추적으로, 게이지가 빠지면 배회로, 캐릭터를 직접 인식하면 교전으로 갑니다.
///
/// <b>이 상태의 목적은 플레이어에게 중간 신호를 주는 것입니다.</b> 이것이 없으면 "안 들켰다"와
/// "쫓기고 있다" 사이가 없어서, 경계선 한 걸음 차이로 결과가 뒤집히는데 플레이어는 그것을 학습할 수 없습니다.
///
/// 이동하지 않습니다. 소리가 난 방향을 모르는 채 살피는 구간이므로 목적지를 잡지 않습니다.
/// 설계 근거: 기획 확정(2026-08-03). 공용 `적 시스템` v0.2 §5.3.2의 각성 준비 시간을 누적 방식으로 일반화.
/// </remarks>
public class AlertState : EnemyStateBase
{
    /// <summary>소음 경계 상태를 생성합니다.</summary>
    public AlertState(EnemyController controller) : base(controller) { }

    /// <summary>제자리에서 살피는 자세로 들어가고 경계 피드백을 재생합니다.</summary>
    /// <remarks>소리가 난 방향을 모르는 구간이므로 목적지를 잡지 않습니다.</remarks>
    public override void Enter()
    {
        Controller.PlayAlertFeedback();

        // 두리번거리는 동안에는 제자리에 섭니다. 소리 난 방향을 아직 특정하지 못한 구간입니다.
        Controller.StopMoving();

        Controller.SetAlertAnimation(true);
    }

    /// <summary>소음 인지 게이지를 지켜보며 세 갈래 중 하나로 전이합니다.</summary>
    /// <remarks>게이지가 차면 소음 추적, 빠지면 배회, 캐릭터를 직접 인식하면 교전으로 갑니다.</remarks>
    public override void Tick()
    {
        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor == null)
        {
            Controller.TransitionTo(Controller.Wander);
            return;
        }

        // 경계 중에도 시야는 계속 봅니다. 직접 인식하면 게이지와 무관하게 즉시 교전입니다(§5.4.3).
        sensor.UpdatePerception();
        if (sensor.HasAnyValidTarget)
        {
            Controller.TransitionTo(Controller.Combat);
            return;
        }

        // 게이지가 차면 소음 위치를 추적합니다. 게이지 비우기는 NoiseChaseState 진입 시 처리합니다.
        if (sensor.IsNoiseAwarenessFull)
        {
            Controller.TransitionTo(Controller.NoiseChase);
            return;
        }

        // 소음이 끊겨 게이지가 다 빠졌으면 경계를 풉니다.
        // 소음 기록도 함께 지웁니다. 남겨 두면 배회가 그것을 다시 집어 경계로 되돌아옵니다.
        if (!sensor.IsNoiseAlert)
        {
            sensor.ClearNoise();
            Controller.TransitionTo(Controller.Wander);
        }
    }

    /// <summary>경계 자세를 풀고 살피는 연출을 정리합니다.</summary>
    public override void Exit()
    {
        Controller.SetAlertAnimation(false);

        NavMeshAgent agent = Controller.Agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }
}
