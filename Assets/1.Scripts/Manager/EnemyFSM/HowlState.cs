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
/// <b>기회 소모는 전파 시점에 확정됩니다.</b> 전파 전에 경직으로 끊기면 주변에 아무것도 전달되지 않았으므로
/// "하려고 했다"로 보고 다시 설 수 있게 둡니다. 다만 무한 재시도는 경직으로 저지하는 플레이를 무의미하게 만들어,
/// 정해진 횟수를 넘긴 취소에서 기회가 닫힙니다(<see cref="CombatState.NotifyHowlCanceled"/>).
/// 기획 결정(2026-08-07)이며 공용 문서 §5.5.1의 "전파 전 취소도 재시도하지 않는다"와 어긋나 문서 갱신이 필요합니다.
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

    /// <summary>하울링 연출을 시작하고 전파 시점과 행동 종료 시점을 잡습니다.</summary>
    /// <remarks>
    /// <b>진입만으로는 기회를 소모하지 않습니다.</b> 전파 시점 전에 끊기면 주변에 아무것도 전달되지 않았으므로
    /// "하려고 했다"로 보고 다시 설 수 있게 둡니다. 소모는 전파가 실제로 일어날 때 확정됩니다.
    /// </remarks>
    public override void Enter()
    {
        m_startTime = Time.time;
        m_broadcastDone = false;

        // 양발을 지지하고 이동을 멈춥니다(콘텐츠 §7.3).
        Controller.StopMoving();

        Controller.PlayHowlAnimation();
    }

    /// <summary>전파 시점에 주변 변이체를 합류시키고, 행동이 끝나면 다음 행동을 고릅니다.</summary>
    /// <remarks>
    /// 전파는 시작과 동시에 일어나지 않습니다. 이 지연이 "전파 전에 경직을 넣어 저지한다"가 성립하는 근거이므로
    /// 없애면 안 됩니다. 전파 전에 사망하면 취소됩니다.
    /// </remarks>
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

    /// <summary>하울링 연출과 전파 대기 상태를 정리합니다.</summary>
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

    /// <summary>이번 하울링이 전파 시점을 지났는지 여부입니다.</summary>
    /// <remarks>
    /// 경직으로 끊겼을 때 기회를 소모할지 판단하는 기준입니다. 전파 전이면 "하려고 했다"로 보고 다시 설 수
    /// 있게 두고, 전파 뒤면 이미 목적을 달성했으므로 남은 연출만 끊긴 것으로 봅니다(§5.5.3).
    /// </remarks>
    public bool IsBroadcastDone => m_broadcastDone;

    /// <summary>주변 변이체에게 스쿼드 실시간 위치를 전파합니다.</summary>
    private void DoBroadcast()
    {
        m_broadcastDone = true;

        // 전파가 실제로 일어난 이 시점에 기회를 소모합니다. 진입 시점에 소모하면 전파 전에 끊긴 개체가
        // 아무것도 전달하지 못하고도 기회를 잃습니다.
        Controller.Combat.MarkHowlBroadcast();

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
