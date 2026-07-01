using UnityEngine;

/// <summary>
/// 플레이어가 상호작용할 수 있는 대상이 구현하는 인터페이스입니다.
/// </summary>
/// <remarks>
/// <see cref="InteractionController"/>는 대상 탐지·입력(탭/홀드) 타이밍·디스패치만 담당하고,
/// 실제 동작(예: 아군 부활, 시설 사용, 아이템 획득)은 이 인터페이스를 구현한 대상이 자기 도메인 로직으로 수행합니다.
/// 부활처럼 특정 시스템이 소유한 기능은 그 시스템의 함수(예: PlayerHealth)를 호출하는 얇은 위임으로 구현하세요.
/// </remarks>
public interface IInteractable
{
    /// <summary>
    /// 지금 이 대상과 상호작용할 수 있는지 여부입니다. 탐지 후보 필터링과 실행 직전 재확인에 사용됩니다.
    /// </summary>
    /// <param name="interactor">상호작용을 시도하는 주체(보통 플레이어 조작 멤버)입니다.</param>
    /// <returns>상호작용 가능하면 <c>true</c>입니다.</returns>
    bool CanInteract(GameObject interactor);

    /// <summary>
    /// 실제 상호작용을 실행합니다. 탭이면 즉시, 홀드면 <see cref="HoldDuration"/>이 채워졌을 때 호출됩니다.
    /// </summary>
    /// <param name="interactor">상호작용을 실행한 주체입니다.</param>
    void Interact(GameObject interactor);

    /// <summary>
    /// UI 프롬프트에 표시할 문구입니다(예: "열기", "부활"). 표시는 소비 측에서 처리합니다.
    /// </summary>
    /// <returns>프롬프트 문구입니다.</returns>
    string GetPrompt();

    /// <summary>
    /// 상호작용에 필요한 홀드(꾹 누르기) 시간(초)입니다. <c>0</c> 이하이면 탭(즉시 실행)입니다.
    /// </summary>
    float HoldDuration { get; }
}
