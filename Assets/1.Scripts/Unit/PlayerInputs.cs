using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 플레이어 입력 상태를 보관하고 Input System 이벤트를 런타임 입력 값으로 변환하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 클래스는 입력 액션 콜백에서 받은 값을 내부 필드에 캐싱하고,
/// 이동/시점/점프/전력질주/조준/공격/재장전 상태를 다른 시스템에서 읽을 수 있게 제공합니다.
/// <para>
/// 변수 컨벤션은 <c>m_</c> 접두사를 사용하는 private serialized field를 기준으로 하며,
/// 기존 Starter Assets 스타일의 <c>move</c>, <c>look</c>, <c>jump</c> 접근도 호환용 프로퍼티로 유지합니다.
/// </para>
/// </remarks>
public class PlayerInputs : MonoBehaviour
{
    [Header("Character Input Values")]
    [Tooltip("현재 이동 입력값입니다. x는 좌우, y는 전후 입력을 의미합니다.")]
    [FormerlySerializedAs("move")]
    [SerializeField] private Vector2 m_move;

    [Tooltip("현재 시점 입력값입니다. x는 좌우 회전, y는 상하 회전을 의미합니다.")]
    [FormerlySerializedAs("look")]
    [SerializeField] private Vector2 m_look;

    [Tooltip("점프 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("jump")]
    [SerializeField] private bool m_jump;

    [Tooltip("전력질주 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("sprint")]
    [SerializeField] private bool m_sprint;

    [Tooltip("조준 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("aim")]
    [SerializeField] private bool m_aim;

    [Tooltip("발사 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("shoot")]
    [SerializeField] private bool m_shoot;

    [Tooltip("재장전 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("reload")]
    [SerializeField] private bool m_reload;

    [Header("Movement Settings")]
    [Tooltip("아날로그 이동 입력을 사용할지 여부입니다. true이면 입력 세기 magnitude를 이동 속도에 반영합니다.")]
    [FormerlySerializedAs("analogMovement")]
    [SerializeField] private bool m_analogMovement;

    [Header("Mouse Cursor Settings")]
    [Tooltip("애플리케이션 포커스 시 커서를 화면 중앙에 잠글지 여부입니다.")]
    [FormerlySerializedAs("cursorLocked")]
    [SerializeField] private bool m_cursorLocked = true;

    [Tooltip("마우스 커서 입력을 시점 회전에 사용할지 여부입니다.")]
    [FormerlySerializedAs("cursorInputForLook")]
    [SerializeField] private bool m_cursorInputForLook = true;

    /// <summary>현재 이동 입력값입니다.</summary>
    public Vector2 Move => m_move;

    /// <summary>현재 시점 입력값입니다.</summary>
    public Vector2 Look => m_look;

    /// <summary>점프 입력 상태입니다.</summary>
    public bool Jump => m_jump;

    /// <summary>전력질주 입력 상태입니다.</summary>
    public bool Sprint => m_sprint;

    /// <summary>조준 입력 상태입니다.</summary>
    public bool Aim => m_aim;

    /// <summary>발사 입력 상태입니다.</summary>
    public bool Shoot => m_shoot;

    /// <summary>재장전 입력 상태입니다.</summary>
    public bool Reload => m_reload;

    /// <summary>아날로그 이동 입력 사용 여부입니다.</summary>
    public bool AnalogMovement => m_analogMovement;

    /// <summary>커서 잠금 사용 여부입니다.</summary>
    public bool CursorLocked => m_cursorLocked;

    /// <summary>커서 입력을 시점 회전에 사용할지 여부입니다.</summary>
    public bool CursorInputForLook => m_cursorInputForLook;

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 이동 입력 프로퍼티입니다.
    /// </summary>
    public Vector2 move
    {
        get => m_move;
        set => m_move = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 시점 입력 프로퍼티입니다.
    /// </summary>
    public Vector2 look
    {
        get => m_look;
        set => m_look = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 점프 입력 프로퍼티입니다.
    /// </summary>
    public bool jump
    {
        get => m_jump;
        set => m_jump = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 전력질주 입력 프로퍼티입니다.
    /// </summary>
    public bool sprint
    {
        get => m_sprint;
        set => m_sprint = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 조준 입력 프로퍼티입니다.
    /// </summary>
    public bool aim
    {
        get => m_aim;
        set => m_aim = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 발사 입력 프로퍼티입니다.
    /// </summary>
    public bool shoot
    {
        get => m_shoot;
        set => m_shoot = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 재장전 입력 프로퍼티입니다.
    /// </summary>
    public bool reload
    {
        get => m_reload;
        set => m_reload = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 아날로그 이동 설정 프로퍼티입니다.
    /// </summary>
    public bool analogMovement
    {
        get => m_analogMovement;
        set => m_analogMovement = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 커서 잠금 설정 프로퍼티입니다.
    /// </summary>
    public bool cursorLocked
    {
        get => m_cursorLocked;
        set => m_cursorLocked = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 시점 입력 허용 설정 프로퍼티입니다.
    /// </summary>
    public bool cursorInputForLook
    {
        get => m_cursorInputForLook;
        set => m_cursorInputForLook = value;
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// 이동 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 이동 입력값입니다.</param>
    public void OnMove(InputValue value)
    {
        MoveInput(value.Get<Vector2>());
    }

    /// <summary>
    /// 시점 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 시점 입력값입니다.</param>
    public void OnLook(InputValue value)
    {
        if (m_cursorInputForLook)
        {
            LookInput(value.Get<Vector2>());
        }
    }

    /// <summary>
    /// 점프 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 점프 입력 상태입니다.</param>
    public void OnJump(InputValue value)
    {
        JumpInput(value.isPressed);
    }

    /// <summary>
    /// 전력질주 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 전력질주 입력 상태입니다.</param>
    public void OnSprint(InputValue value)
    {
        SprintInput(value.isPressed);
    }

    /// <summary>
    /// 조준 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 조준 입력 상태입니다.</param>
    public void OnAim(InputValue value)
    {
        AimInput(value.isPressed);
    }

    /// <summary>
    /// 발사 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 발사 입력 상태입니다.</param>
    public void OnShoot(InputValue value)
    {
        ShootInput(value.isPressed);
    }

    /// <summary>
    /// 재장전 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 재장전 입력 상태입니다.</param>
    public void OnReload(InputValue value)
    {
        ReloadInput(value.isPressed);
    }
#endif

    /// <summary>
    /// 이동 입력값을 갱신합니다.
    /// </summary>
    /// <param name="newMoveDirection">새 이동 입력 방향입니다.</param>
    public void MoveInput(Vector2 newMoveDirection)
    {
        m_move = newMoveDirection;
    }

    /// <summary>
    /// 시점 입력값을 갱신합니다.
    /// </summary>
    /// <param name="newLookDirection">새 시점 입력 방향입니다.</param>
    public void LookInput(Vector2 newLookDirection)
    {
        m_look = newLookDirection;
    }

    /// <summary>
    /// 점프 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newJumpState">새 점프 입력 상태입니다.</param>
    public void JumpInput(bool newJumpState)
    {
        m_jump = newJumpState;
    }

    /// <summary>
    /// 전력질주 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newSprintState">새 전력질주 입력 상태입니다.</param>
    public void SprintInput(bool newSprintState)
    {
        m_sprint = newSprintState;
    }

    /// <summary>
    /// 조준 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newAimState">새 조준 입력 상태입니다.</param>
    public void AimInput(bool newAimState)
    {
        m_aim = newAimState;
    }

    /// <summary>
    /// 발사 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newShootState">새 발사 입력 상태입니다.</param>
    public void ShootInput(bool newShootState)
    {
        m_shoot = newShootState;
    }

    /// <summary>
    /// 재장전 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newReloadState">새 재장전 입력 상태입니다.</param>
    public void ReloadInput(bool newReloadState)
    {
        m_reload = newReloadState;
    }

    /// <summary>
    /// 커서 잠금 사용 여부를 설정합니다.
    /// </summary>
    /// <param name="value">커서를 잠그려면 true, 해제하려면 false입니다.</param>
    public void SetCursorLocked(bool value)
    {
        m_cursorLocked = value;
        SetCursorState(m_cursorLocked);
    }

    /// <summary>
    /// 시점 회전에 커서 입력을 사용할지 설정합니다.
    /// </summary>
    /// <param name="value">커서 입력을 시점 회전에 사용하려면 true입니다.</param>
    public void SetCursorInputForLook(bool value)
    {
        m_cursorInputForLook = value;
    }

    /// <summary>
    /// 아날로그 이동 입력 사용 여부를 설정합니다.
    /// </summary>
    /// <param name="value">입력 세기를 이동 속도에 반영하려면 true입니다.</param>
    public void SetAnalogMovement(bool value)
    {
        m_analogMovement = value;
    }

    /// <summary>
    /// 애플리케이션 포커스 상태가 바뀔 때 커서 상태를 갱신합니다.
    /// </summary>
    /// <param name="hasFocus">애플리케이션이 포커스를 얻었으면 true입니다.</param>
    private void OnApplicationFocus(bool hasFocus)
    {
        SetCursorState(m_cursorLocked);
    }

    /// <summary>
    /// Unity 커서 잠금 상태를 적용합니다.
    /// </summary>
    /// <param name="newState">커서를 잠그려면 true, 해제하려면 false입니다.</param>
    private void SetCursorState(bool newState)
    {
        Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
    }

    /// <summary>
    /// 모든 런타임 입력 상태를 기본값으로 초기화합니다.
    /// </summary>
    /// <remarks>
    /// 이동과 시점 입력은 <see cref="Vector2.zero"/>로 초기화하고,
    /// 버튼형 입력은 모두 false로 초기화합니다.
    /// </remarks>
    public void ResetInputState()
    {
        m_move = Vector2.zero;
        m_look = Vector2.zero;
        m_jump = false;
        m_sprint = false;
        m_aim = false;
        m_shoot = false;
        m_reload = false;
    }
}
