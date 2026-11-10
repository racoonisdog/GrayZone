using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 셸터 3인칭 카메라의 마우스 회전과 커서 잠금 상태를 제어
/// </summary>
public class CameraLook : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 70f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnStart = true;
    [SerializeField] private bool relockCursorOnLeftClick = true;

    [Header("Control Lock")]
    [SerializeField] private bool m_lookLocked;

    // 커서를 잠그는 순간 커서가 화면 중앙으로 이동하면서 큰 마우스 델타가 한 번 들어온다.
    // 그 값으로 시점이 튀지 않게 잠근 직후 몇 프레임은 델타를 버린다.
    private const int IgnoreDeltaFramesAfterLock = 2;

    private float yaw;
    private float pitch;
    private bool m_inputSuspended;
    private int m_ignoreDeltaFrames;

    /// <summary>현재 카메라 좌우 회전각</summary>
    public float Yaw => yaw;

    /// <summary>현재 카메라 상하 회전각</summary>
    public float Pitch => pitch;

    /// <summary>카메라 회전 입력이 잠겨 있는지 여부</summary>
    public bool IsLookLocked => m_lookLocked;

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = NormalizePitch(angles.x);

        if (lockCursorOnStart)
            LockCursor();
    }

    private void Update()
    {
        if (m_lookLocked || m_inputSuspended)
            return;

        HandleCursorInput();
    }

    private void LateUpdate()
    {
        if (m_lookLocked || m_inputSuspended)
            return;

        if (lockCursorOnStart && Cursor.lockState != CursorLockMode.Locked)
            return;

        Vector2 mouseDelta = ReadMouseDelta();
        if (m_ignoreDeltaFrames > 0)
        {
            m_ignoreDeltaFrames--;
            return;
        }

        // yaw는 제한 없이 좌우 회전하고, pitch만 위아래 각도를 제한한다.
        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    /// <summary>
    /// UI 열림 등으로 카메라 회전 입력 잠금 상태를 변경
    /// </summary>
    /// <param name="locked">회전 입력을 잠그려면 <c>true</c></param>
    public void SetLookLocked(bool locked)
    {
        if (m_lookLocked == locked)
            return;

        m_lookLocked = locked;

        if (m_lookLocked)
        {
            UnlockCursor();
            return;
        }

        if (lockCursorOnStart)
            LockCursor();
    }

    /// <summary>
    /// 컷신 재생 등으로 마우스 회전만 잠시 멈춤. 커서 잠금 상태는 건드리지 않는다.
    /// </summary>
    /// <remarks><see cref="SetLookLocked"/>와 따로 관리하므로 UI 잠금과 서로 덮어쓰지 않는다.</remarks>
    /// <param name="suspended">회전 입력을 멈추려면 <c>true</c></param>
    public void SetInputSuspended(bool suspended)
    {
        if (m_inputSuspended == suspended)
            return;

        m_inputSuspended = suspended;

        // 멈춘 동안 쌓인 델타가 재개 첫 프레임에 한꺼번에 들어오지 않게 버린다.
        if (!m_inputSuspended)
            m_ignoreDeltaFrames = IgnoreDeltaFramesAfterLock;
    }

    /// <summary>카메라 시점을 지정한 좌우·상하 각도로 바로 맞춤</summary>
    /// <param name="newYaw">월드 기준 좌우 회전각</param>
    /// <param name="newPitch">상하 회전각. minPitch~maxPitch로 제한된다.</param>
    public void SetView(float newYaw, float newPitch)
    {
        yaw = newYaw;
        pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private Vector2 ReadMouseDelta()
    {
        Vector2 delta = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        // 현재 프로젝트는 새 Input System을 사용하므로 마우스 델타를 직접 읽는다.
        if (Mouse.current != null)
            delta = Mouse.current.delta.ReadValue();
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        // Legacy Input Manager를 함께 켠 경우 Mouse X/Y 축도 사용할 수 있다.
        if (delta.sqrMagnitude <= Mathf.Epsilon)
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif

        return delta;
    }

    private void HandleCursorInput()
    {
        if (!lockCursorOnStart) return;

        if (WasEscapePressed())
            UnlockCursor();

        if (relockCursorOnLeftClick && Cursor.lockState != CursorLockMode.Locked && WasLeftClickPressed())
            LockCursor();
    }

    private bool WasEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape))
            return true;
#endif

        return false;
    }

    private bool WasLeftClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
            return true;
#endif

        return false;
    }

    private void LockCursor()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
            m_ignoreDeltaFrames = IgnoreDeltaFramesAfterLock;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private float NormalizePitch(float angle)
    {
        return Mathf.DeltaAngle(0f, angle);
    }
}
