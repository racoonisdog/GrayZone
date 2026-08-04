using System;
using UnityEngine;

/// <summary>
/// 런타임 코드가 에디터 전용 밸런스 역동기화를 부를 수 있게 하는 연결점입니다.
/// </summary>
/// <remarks>
/// <para>
/// 실제 구현은 <c>Editor</c> 폴더의 <c>BalanceReverseSync</c>에 있습니다. 그 폴더는 별도 에디터
/// 어셈블리로 컴파일되고, 런타임 어셈블리는 에디터 어셈블리를 참조하지 않습니다.
/// 그래서 런타임 스크립트에서 그 타입을 직접 쓰면 <c>#if UNITY_EDITOR</c>로 감싸도 컴파일되지 않습니다.
/// </para>
/// <para>
/// 방향을 뒤집어, 참조가 가능한 에디터 쪽이 시작할 때 자기 구현을 여기 꽂아 둡니다.
/// 런타임 쪽은 이 정적 클래스만 알면 되고, 빌드에서는 꽂히는 것이 없어 <see cref="IsAvailable"/>이
/// 계속 false로 남습니다. 리플렉션을 쓰지 않으므로 양쪽 모두 컴파일 타임에 검증됩니다.
/// </para>
/// </remarks>
public static class BalanceReverseSyncHook
{
    /// <summary>대상의 현재 값을 밸런스 SO와 CSV로 되돌려 쓰고 결과 요약을 돌려주는 구현입니다.</summary>
    public static Func<Component, string> Handler;

    /// <summary>대상이 역동기화할 수 있는 상태인지(밸런스 SO를 물고 있는지) 확인하는 구현입니다.</summary>
    public static Func<Component, bool> Probe;

    /// <summary>에디터 구현이 꽂혀 있는지 여부입니다. 빌드에서는 항상 false입니다.</summary>
    public static bool IsAvailable => Handler != null;

    /// <summary>대상을 역동기화할 수 있는지 확인합니다.</summary>
    /// <param name="target">확인할 컴포넌트입니다.</param>
    /// <returns>구현이 꽂혀 있고 대상이 밸런스 SO를 물고 있으면 true입니다.</returns>
    public static bool CanRun(Component target)
    {
        return target != null && Probe != null && Probe(target);
    }

    /// <summary>
    /// 대상을 역동기화합니다.
    /// </summary>
    /// <param name="target">현재 값을 읽어 올 컴포넌트입니다.</param>
    /// <returns>사람이 읽을 수 있는 결과 요약입니다. 구현이 없으면 그 사실을 알리는 문구입니다.</returns>
    public static string Run(Component target)
    {
        if (target == null)
        {
            return "대상이 없습니다.";
        }

        if (Handler == null)
        {
            return "에디터에서만 사용할 수 있습니다. SO와 CSV는 프로젝트 자산이라 빌드에서는 쓸 수 없습니다.";
        }

        return Handler(target);
    }
}
