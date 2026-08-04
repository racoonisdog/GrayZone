using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소음 이벤트를 듣는 쪽에 전달하는 방송 지점입니다.
/// </summary>
/// <remarks>
/// 발신자는 누가 듣는지 모르고, 수신자는 누가 냈는지 모릅니다. 그 사이를 이 클래스가 잇습니다.
///
/// 듣는 쪽을 미리 등록해 두는 이유는 공용 `적 시스템` v0.2 §5.4.4가 "소음 이벤트가 발생한 시점에만
/// 우선순위를 평가하며 매 프레임 모든 소음을 비교하지 않는다"고 정하기 때문입니다. 사건이 생긴 순간에만
/// 도는 구조라야 그 규칙이 성립합니다. 발신할 때마다 씬을 훑으면 발소리처럼 잦은 소음에서 비용이 커집니다.
///
/// 가청 판정과 강도 계산을 여기서 하지 않습니다. 청각 배수는 개체마다 다르므로 수신자가 판단합니다.
/// 여기는 전달만 하며, 이 클래스는 소음 규칙을 하나도 들고 있지 않습니다.
/// </remarks>
public static class NoiseSystem
{
    /// <summary>현재 소음을 듣고 있는 센서 목록입니다.</summary>
    private static readonly List<EnemyTargetSensor> s_listeners = new List<EnemyTargetSensor>();

    /// <summary>등록된 수신자 수입니다. 진단용입니다.</summary>
    public static int ListenerCount => s_listeners.Count;

    /// <summary>
    /// 플레이 시작 시 등록 목록을 비웁니다.
    /// </summary>
    /// <remarks>
    /// Enter Play Mode Options로 도메인 리로드를 끄면 static 목록이 이전 플레이 세션의 항목을 들고 남습니다.
    /// 파괴된 센서가 남아 있으면 방송이 그것들을 계속 훑게 되므로 진입 시점에 한 번 비웁니다.
    /// </remarks>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners()
    {
        s_listeners.Clear();
    }

    /// <summary>소음을 들을 센서를 등록합니다.</summary>
    /// <param name="listener">등록할 센서입니다.</param>
    public static void Register(EnemyTargetSensor listener)
    {
        if (listener == null || s_listeners.Contains(listener))
        {
            return;
        }

        s_listeners.Add(listener);
    }

    /// <summary>소음 수신 등록을 해제합니다.</summary>
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
    /// 소음을 발생시켜 등록된 모든 수신자에게 알립니다.
    /// </summary>
    /// <param name="noise">발생한 소음입니다.</param>
    /// <remarks>
    /// 모든 수신자에게 그대로 넘깁니다. 들리는지는 수신자가 자신의 청각 배수로 판단합니다.
    /// 여기서 거리로 미리 걸러 내면 청각 배수를 아는 곳이 두 군데가 되어 규칙이 갈라집니다.
    /// </remarks>
    public static void Emit(in NoiseEvent noise)
    {
        // 뒤에서부터 도는 것은 파괴된 항목을 도중에 제거해도 순회가 어긋나지 않게 하기 위한 것입니다.
        for (int i = s_listeners.Count - 1; i >= 0; i--)
        {
            EnemyTargetSensor listener = s_listeners[i];
            if (listener == null)
            {
                s_listeners.RemoveAt(i);
                continue;
            }

            listener.NotifyNoise(noise);
        }
    }
}
