using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 작전 실패(게임오버)·귀환 정산 오버레이가 화면에 떠 있는지 알려 줍니다.
/// </summary>
/// <remarks>
/// 디버그 창과 안내 문구는 IMGUI(OnGUI)로 그려서 uGUI 캔버스 정렬 순서와 상관없이 항상 맨 위에 나옵니다.
/// 그래서 오버레이가 떠 있는 동안에는 각 OnGUI가 이 값을 보고 그리지 않습니다.
/// 오버레이 쪽은 표시할 때 <see cref="Register"/>, 숨길 때 <see cref="Unregister"/>를 부릅니다.
/// </remarks>
public static class MissionOverlayVisibility
{
    private static readonly HashSet<Object> s_shownOverlays = new();

    /// <summary>오버레이가 하나라도 떠 있으면 true입니다.</summary>
    public static bool IsShown
    {
        get
        {
            // 씬 전환으로 파괴된 오버레이가 남아 있으면 정리합니다.
            s_shownOverlays.RemoveWhere(overlay => overlay == null);
            return s_shownOverlays.Count > 0;
        }
    }

    /// <summary>오버레이가 떴다고 등록합니다.</summary>
    public static void Register(Object overlay)
    {
        if (overlay != null)
        {
            s_shownOverlays.Add(overlay);
        }
    }

    /// <summary>오버레이가 사라졌다고 등록을 풉니다.</summary>
    public static void Unregister(Object overlay)
    {
        s_shownOverlays.Remove(overlay);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 Play의 등록이 남지 않게 합니다.
        s_shownOverlays.Clear();
    }
}
