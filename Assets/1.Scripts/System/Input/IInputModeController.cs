/// <summary>
/// 씬별 게임플레이 입력과 UI 입력 상태를 전환하는 계약입니다.
/// </summary>
/// <remarks>
/// 구현체는 필드·셸터처럼 각 씬의 실제 입력 구조에 맞게 조준, 카메라 Look, 상호작용을 제어합니다.
/// 공용 UI는 이 계약만 사용하며 특정 플레이어 컨트롤러를 직접 참조하지 않습니다.
/// </remarks>
public interface IInputModeController
{
    /// <summary>현재 적용된 입력 모드입니다.</summary>
    InputMode CurrentInputMode { get; }

    /// <summary>
    /// 입력 모드를 전환합니다.
    /// </summary>
    /// <param name="mode">적용할 입력 모드입니다.</param>
    /// <returns>1은 정상 성공, 0은 정상 실패, -1은 예외가 발생한 비정상 실패입니다.</returns>
    int SetInputMode(InputMode mode);
}

/// <summary>
/// 게임플레이 조작과 UI 커서 조작 사이의 입력 상태입니다.
/// </summary>
public enum InputMode
{
    /// <summary>TPS 이동·시점·전투 또는 씬 게임플레이 입력을 받는 상태입니다.</summary>
    Gameplay,

    /// <summary>게임플레이 입력을 차단하고 UI 커서 조작을 받는 상태입니다.</summary>
    UI
}
