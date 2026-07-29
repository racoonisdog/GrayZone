using UnityEngine;

namespace AE_Camera
{
    /// <summary>
    /// Fly camera:
    /// - WASD: move relative to view direction (W goes where you look)
    /// - Q/E: down/up (world up)
    /// - RMB hold: rotate (yaw/pitch) + locks cursor
    /// - Shift: fast
    /// - Ctrl: slow
    /// - Mouse wheel: zoom (move forward/back) - does NOT change speed
    /// </summary>
    public class AE_SimpleCameraController : MonoBehaviour
    {
        [Header("Movement")]
        [Min(0.01f)] public float moveSpeed = 3.5f;
        [Min(1f)] public float fastMultiplier = 4f;
        [Range(0.05f, 1f)] public float slowMultiplier = 0.25f;

        [Header("Rotation")]
        [Min(0.01f)] public float mouseSensitivity = 2.0f;
        [Range(1f, 89f)] public float pitchLimit = 80f;
        public bool invertY = false;

        [Header("Zoom")]
        [Min(0f)] public float zoomSpeed = 12f;

        [Header("Smoothing")]
        [Range(0f, 30f)] public float smoothing = 12f;

        private Vector3 _targetPos;
        private float _yaw;
        private float _pitch;

        void OnEnable()
        {
            _targetPos = transform.position;

            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = NormalizePitch(e.x);
        }

        void Update()
        {
            HandleCursorLock();
            float dt = Time.unscaledDeltaTime;

            // Rotation (RMB)
            if (Input.GetMouseButton(1))
            {
                float mx = Input.GetAxisRaw("Mouse X");
                float my = Input.GetAxisRaw("Mouse Y") * (invertY ? 1f : -1f);

                _yaw += mx * mouseSensitivity;
                _pitch += my * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);
            }

            Quaternion viewRot = Quaternion.Euler(_pitch, _yaw, 0f);

            // Movement input
            Vector3 localDir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) localDir += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) localDir += Vector3.back;
            if (Input.GetKey(KeyCode.A)) localDir += Vector3.left;
            if (Input.GetKey(KeyCode.D)) localDir += Vector3.right;

            // Optional vertical (world up/down)
            if (Input.GetKey(KeyCode.E)) localDir += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) localDir += Vector3.down;

            float speed = moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed *= fastMultiplier;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) speed *= slowMultiplier;

            // Convert to world:
            // - For WASD we use full view rotation so W goes where you look
            // - For vertical we keep world up/down (so E/Q always vertical)
            Vector3 worldMove = Vector3.zero;

            Vector3 wasd = new Vector3(localDir.x, 0f, localDir.z);
            if (wasd.sqrMagnitude > 0.0001f)
                worldMove += (viewRot * wasd.normalized);

            float vertical = localDir.y;
            if (Mathf.Abs(vertical) > 0.0001f)
                worldMove += Vector3.up * Mathf.Sign(vertical);

            _targetPos += worldMove * (speed * dt);

            // Zoom (wheel) forward/back by view direction (no speed change)
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.001f && zoomSpeed > 0f)
            {
                Vector3 zoomDir = viewRot * Vector3.forward;
                _targetPos += zoomDir * (wheel * zoomSpeed);
            }

            // Apply
            if (smoothing <= 0f)
            {
                transform.position = _targetPos;
                transform.rotation = viewRot;
            }
            else
            {
                float t = 1f - Mathf.Exp(-smoothing * dt);
                transform.position = Vector3.Lerp(transform.position, _targetPos, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, viewRot, t);
            }
        }

        private void HandleCursorLock()
        {
            if (Input.GetMouseButtonDown(1))
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (Input.GetMouseButtonUp(1))
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private static float NormalizePitch(float pitchEuler)
        {
            if (pitchEuler > 180f) pitchEuler -= 360f;
            return pitchEuler;
        }
    }
}
