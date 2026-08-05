using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 교전 하위 상태: 하울링(패턴 A)을 수행해 주변 변이체를 교전에 합류시킵니다.
/// </summary>
/// <remarks>
/// 첫 발견자만 수행합니다. 소음만 감지한 상태에서는 진입하지 않고, 시야·근접 감지·직접 피격으로
/// 캐릭터를 직접 인식했을 때만 조건을 충족합니다(§5.5.1).
///
/// <b>전파는 시작과 동시에 일어나지 않습니다.</b> 정해진 시점에 전파되며, 그 전에 사망하면 취소됩니다(§5.5.3).
/// 이 지연이 "나린이 전파 전에 경직을 넣어 저지한다"(콘텐츠 §7.5)가 성립하는 근거이므로 없애면 안 됩니다.
///
/// 시도 기록은 성공·취소와 무관하게 소모됩니다(§5.5.1). 그래서 취소된 개체도 같은 교전에서 다시 시도하지 않습니다.
///
/// 경직으로 인한 취소는 경직 시스템이 아직 없어 구현하지 않았습니다. 사망 취소만 있습니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.5, `변이체 잡몹 1 콘텐츠` §7.
/// </remarks>
public class HowlState : EnemyStateBase
{
    /// <summary>하울링을 시작한 시각입니다.</summary>
    private float m_startTime;

    /// <summary>이번 하울링에서 전파를 이미 수행했는지 여부입니다.</summary>
    private bool m_broadcastDone;

    /// <summary>하울링 상태를 생성합니다.</summary>
    public HowlState(EnemyController controller) : base(controller) { }

    public override void Enter()
    {
        m_startTime = Time.time;
        m_broadcastDone = false;

        // 양발을 지지하고 이동을 멈춥니다(콘텐츠 §7.3).
        Controller.StopMoving();

        // 시도 기록을 진입 시점에 소모합니다. 전파에 성공했는지와 무관하게 1회만 시도하므로(§5.5.1),
        // 전파 시점까지 미루면 그 전에 취소된 개체가 다시 시도할 수 있게 됩니다.
        Controller.Combat.MarkHowlAttempted();

        Controller.PlayHowlAnimation();
    }

    public override void Tick()
    {
        float elapsed = Time.time - m_startTime;

        if (!m_broadcastDone && elapsed >= Controller.HowlBroadcastTime)
        {
            DoBroadcast();
        }

        if (elapsed >= Controller.HowlDuration)
        {
            FinishHowl();
        }
    }

    public override void Exit()
    {
        Controller.EndHowlAnimation();
    }

    /// <summary>
    /// 클립의 전파 이벤트가 알려 준 시점을 처리합니다.
    /// </summary>
    /// <remarks>
    /// 이미 타이머로 전파했다면 무시합니다. 한 번의 하울링에서 전파는 한 번뿐입니다.
    /// 공격 판정과 같은 방식으로, 클립에 이벤트가 없어도 동작하고 생겨도 두 번 전파되지 않습니다.
    /// </remarks>
    public void NotifyAnimationBroadcast()
    {
        if (m_broadcastDone)
        {
            return;
        }

        DoBroadcast();
    }

    /// <summary>주변 변이체에게 스쿼드 실시간 위치를 전파합니다.</summary>
    private void DoBroadcast()
    {
        m_broadcastDone = true;

        EnemyTargetSensor sensor = Controller.Sensor;
        if (sensor == null)
        {
            return;
        }

        IReadOnlyList<SquadMemberController> members = sensor.BuildHowlMemberList();

        // 송신자도 스쿼드 위치를 얻습니다(§5.5.5). 이것을 빼면 자기가 부른 결과를 자기만 못 받아,
        // 시야가 끊기는 순간 교전을 놓치고 비교전 속도로 걸어가며 잠시 뒤 하울링을 다시 합니다.
        // 전파보다 먼저 적용하는 이유는, 전파 도중 목록이 바뀌지 않더라도 순서를 읽는 사람이
        // "송신자가 빠졌나" 의심하지 않게 하려는 것입니다.
        sensor.ApplyOwnHowl(members);

        int applied = HowlSystem.Broadcast(
            sensor,
            Controller.transform.position,
            Controller.HowlRadius,
            members);

        Controller.NotifyHowlBroadcast(applied, members.Count);
    }

    /// <summary>하울링이 끝난 뒤 다음 행동을 고릅니다.</summary>
    /// <remarks>
    /// 대상 유효성은 엄브렐라가 매 틱 확인하므로 여기서는 추격으로만 넘깁니다.
    /// 사거리 안이면 추격 상태가 곧바로 공격으로 넘깁니다.
    /// </remarks>
    private void FinishHowl()
    {
        Controller.Combat.SetSubState(Controller.Combat.Chase);
    }
}
