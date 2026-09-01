using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 하울링을 반경 안의 다른 변이체에게 전달하는 방송 지점입니다.
/// </summary>
/// <remarks>
/// 소음과 같은 모양이지만 <b>별개 시스템</b>입니다. 콘텐츠 문서 §7.3이 "포효 음향은 행동을 알리는
/// 전투 피드백이며 별도 소음 이벤트로 처리하지 않는다"고 정하므로, 하울링을 소음으로 흘리면
/// 주변 변이체가 소음 추적을 시작해 버려 규칙이 어긋납니다.
///
/// 전달 내용도 다릅니다. 소음은 위치 하나를 주고 유효 대상을 만들지 않지만,
/// 하울링은 스쿼드 캐릭터 목록을 주고 그들을 유효 대상으로 만듭니다(§5.5.5).
///
/// 장애물을 판정에 쓰지 않습니다(§5.5.4). 벽 뒤 변이체도 받습니다.
/// 송신자 자신도 목록에 있으므로 전달 대상에서 제외합니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.5.4(전파 범위), §5.5.5(스쿼드 실시간 위치 정보).
/// </remarks>
public static class HowlSystem
{
    /// <summary>하울링을 받을 수 있는 센서 목록입니다.</summary>
    private static readonly List<EnemyTargetSensor> s_listeners = new List<EnemyTargetSensor>();

    /// <summary>등록된 수신자 수입니다. 진단용입니다.</summary>
    public static int ListenerCount => s_listeners.Count;

    /// <summary>
    /// 플레이 시작 시 등록 목록을 비웁니다.
    /// </summary>
    /// <remarks>
    /// 도메인 리로드를 끄면 static 목록이 이전 플레이 세션의 항목을 들고 남습니다.
    /// 파괴된 센서가 남아 있으면 방송이 그것들을 계속 훑게 되므로 진입 시점에 한 번 비웁니다.
    /// </remarks>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners()
    {
        s_listeners.Clear();
    }

    /// <summary>하울링을 받을 센서를 등록합니다.</summary>
    /// <param name="listener">등록할 센서입니다.</param>
    public static void Register(EnemyTargetSensor listener)
    {
        if (listener == null || s_listeners.Contains(listener))
        {
            return;
        }

        s_listeners.Add(listener);
    }

    /// <summary>하울링 수신 등록을 해제합니다.</summary>
    /// <param name="listener">해제할 센서입니다.</param>
    public static void Unregister(EnemyTargetSensor listener)
    {
        if (listener == null)
        {
            return;
        }

        s_listeners.Remove(listener);
    }

    /// <summary>
    /// 하울링을 전파합니다.
    /// </summary>
    /// <param name="sender">하울링을 수행한 변이체의 센서입니다. 전달 대상에서 제외합니다.</param>
    /// <param name="origin">전파 중심 위치입니다.</param>
    /// <param name="radius">전파 반경입니다.</param>
    /// <param name="members">위치를 제공할 스쿼드 캐릭터 목록입니다.</param>
    /// <returns>실제로 하울링을 적용한 수신자 수입니다.</returns>
    /// <remarks>
    /// 반경 밖이거나 교전에 참여하지 않은 변이체는 스쿼드 위치 정보를 얻지 않습니다(§5.5.5).
    /// 이미 하울링 정보를 적용받은 변이체는 추가 하울링을 적용하지 않으므로 수신자 쪽에서 걸러집니다(§5.5.4).
    /// </remarks>
    public static int Broadcast(
        EnemyTargetSensor sender,
        Vector3 origin,
        float radius,
        IReadOnlyList<SquadMemberController> members)
    {
        if (radius <= 0f || members == null || members.Count == 0)
        {
            return 0;
        }

        int applied = 0;
        float sqrRadius = radius * radius;

        // 뒤에서부터 도는 것은 파괴된 항목을 도중에 제거해도 순회가 어긋나지 않게 하기 위한 것입니다.
        for (int i = s_listeners.Count - 1; i >= 0; i--)
        {
            EnemyTargetSensor listener = s_listeners[i];
            if (listener == null)
            {
                s_listeners.RemoveAt(i);
                continue;
            }

            if (listener == sender)
            {
                continue;
            }

            if ((listener.transform.position - origin).sqrMagnitude > sqrRadius)
            {
                continue;
            }

            if (listener.NotifyHowl(members))
            {
                applied++;
            }
        }

        return applied;
    }
}
