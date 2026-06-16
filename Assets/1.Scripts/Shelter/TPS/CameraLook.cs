using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraLook : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 70f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnStart = true;
    [SerializeField] private bool relockCursorOnLeftClick = true;

    private float yaw;
    private float pitch;

    public float Yaw => yaw;
    public float Pitch => pitch;

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
        HandleCursorInput();
    }

    private void LateUpdate()
    {
        if (lockCursorOnStart && Cursor.lockState != CursorLockMode.Locked)
            return;

        Vector2 mouseDelta = ReadMouseDelta();

        // yaw는 제한 없이 좌우 회전하고, pitch만 위아래 각도를 제한한다.
        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

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
