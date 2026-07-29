using UnityEngine;

/// <summary>
/// 개발/플레이테스트용 디버그·트레이너 기능의 전역 활성화 스위치입니다.
/// </summary>
/// <remarks>
/// 런타임 디버그 트레이너 프리팹이 활성화되어 있는 동안에만 개발 모드를 켭니다.
/// <para>
/// 정적 상태의 기본값은 항상 <c>false</c>입니다. 트레이너가 씬에 존재하더라도 Editor 또는
/// Development Build가 아니면 디버그 기능은 활성화되지 않습니다.
/// </para>
/// </remarks>
public static class GameDevMode
{
    /// <summary>
    /// 현재 런타임 디버그 트레이너가 개발 모드를 소유하고 있는지 여부입니다.
    /// </summary>
    public static bool IsDevelopMode { get; private set; }

    /// <summary>
    /// 디버그/트레이너 기능을 사용할 수 있는 상태인지 여부입니다.
    /// 트레이너가 개발 모드를 켰고, 현재 실행 환경이 Editor 또는 Development Build이면 <c>true</c>입니다.
    /// </summary>
    public static bool DebugFeaturesEnabled =>
        IsDevelopMode && (Application.isEditor || Debug.isDebugBuild);

    /// <summary>
    /// Domain Reload 비활성 상태에서도 이전 Play 세션의 정적 값이 남지 않도록 매 실행 전에 초기화합니다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        IsDevelopMode = false;
    }

    /// <summary>런타임 디버그 트레이너가 활성 상태를 획득하거나 반납할 때 호출합니다.</summary>
    public static void SetDevelopMode(bool enabled)
    {
        IsDevelopMode = enabled;
    }
}
