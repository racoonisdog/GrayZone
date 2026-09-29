using System;

/// <summary>
/// <see cref="PlayerInputController.SetInputLock"/>으로 닫을 수 있는 플레이어 입력 종류입니다.
/// </summary>
/// <remarks>
/// 여러 개를 함께 고를 수 있습니다. 값은 비트로 저장되므로 기존 항목의 번호를 바꾸지 말고 끝에 추가하세요.
/// </remarks>
[Flags]
public enum PlayerInputLock
{
    None = 0,
    Move = 1 << 0,
    Look = 1 << 1,
    Jump = 1 << 2,
    Sprint = 1 << 3,
    Aim = 1 << 4,
    Shoot = 1 << 5,
    ThrowMode = 1 << 6,
    Reload = 1 << 7,
    Crouch = 1 << 8,
    Interact = 1 << 9,
    Inventory = 1 << 10,
}
