using UnityEngine;

/// <summary>
/// 개발/플레이테스트용 디버그·트레이너 기능의 전역 활성화 스위치입니다.
/// </summary>
/// <remarks>
/// 이 값의 소유자는 <see cref="GameManager"/>입니다. 게임 시작 시 한 번 설정하고, 이후 게임 수명을 따릅니다.
/// <para>
/// 정적 상태의 기본값은 항상 <c>false</c>입니다. 개발 모드를 켰더라도 Editor 또는
/// Development Build가 아니면 디버그 기능은 활성화되지 않습니다.
/// </para>
/// <para>
/// 트레이너 같은 디버그 도구는 이 값을 읽기만 합니다. 도구가 자기 활성 상태로 이 값을 켜면,
/// 그 도구를 만들지 판단하는 쪽이 도구가 켜 줄 값을 봐야 하는 순환이 생기기 때문입니다.
/// </para>
/// </remarks>
public static class GameDevMode
{
    /// <summary>
    /// 개발 모드가 켜져 있는지 여부입니다. <see cref="GameManager"/>가 설정합니다.
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

    /// <summary>개발 모드를 켜거나 끕니다. <see cref="GameManager"/>가 호출합니다.</summary>
    public static void SetDevelopMode(bool enabled)
    {
        IsDevelopMode = enabled;
    }
}
