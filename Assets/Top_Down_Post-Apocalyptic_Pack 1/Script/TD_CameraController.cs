using UnityEngine;

public class TDCameraController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Camera movement speed on the XZ plane.")]
    public float moveSpeed = 20f;

    [Tooltip("Screen-edge detection width in pixels.")]
    public float edgeSize = 20f;

    [Tooltip("Allows camera movement when the pointer reaches a screen edge.")]
    public bool useEdgeScroll = true;

    [Header("Edge Scroll")]
    [Tooltip("Frames to wait before enabling edge scrolling. This prevents an initial jump after loading.")]
    public int edgeScrollIgnoreFramesOnStart = 2;

    [Tooltip("Ignores edge scrolling while the pointer position is reported as (0, 0).")]
    public bool ignoreEdgeScrollWhenMouseAtZero = true;

    [Header("Zoom")]
    [Tooltip("Mouse-wheel zoom speed.")]
    public float zoomSpeed = 50f;

    [Tooltip("Minimum camera height.")]
    public float minHeight = 10f;

    [Tooltip("Maximum camera height.")]
    public float maxHeight = 60f;

    [Header("Movement Bounds")]
    [Tooltip("Restricts camera movement to the configured XZ bounds.")]
    public bool useBounds = true;

    [Tooltip("Minimum and maximum world-space X positions.")]
    public Vector2 minXmaxX = new Vector2(-50f, 50f);

    [Tooltip("Minimum and maximum world-space Z positions.")]
    public Vector2 minZmaxZ = new Vector2(-50f, 50f);

    private Camera cam;
    private int edgeIgnoreFramesLeft;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("TDCameraController requires a Camera component.");
            enabled = false;
            return;
        }

        cam.orthographic = false;
        edgeIgnoreFramesLeft = Mathf.Max(0, edgeScrollIgnoreFramesOnStart);
    }

    private void Update()
    {
        HandleMove();
        HandleZoom();

        if (useBounds)
            ClampPosition();

        if (edgeIgnoreFramesLeft > 0)
            edgeIgnoreFramesLeft--;
    }

    private void HandleMove()
    {
        Vector3 dir = Vector3.zero;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            dir.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            dir.x += 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            dir.z += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            dir.z -= 1f;

        bool edgeAllowedThisFrame = useEdgeScroll && edgeIgnoreFramesLeft <= 0;

        if (edgeAllowedThisFrame)
        {
            Vector3 mousePosition3D = Input.mousePosition;
            Vector2 mousePosition = new Vector2(mousePosition3D.x, mousePosition3D.y);

            if (!(ignoreEdgeScrollWhenMouseAtZero &&
                  mousePosition.x <= 0.5f &&
                  mousePosition.y <= 0.5f))
            {
                if (mousePosition.x <= edgeSize)
                    dir.x -= 1f;
                else if (mousePosition.x >= Screen.width - edgeSize)
                    dir.x += 1f;

                if (mousePosition.y <= edgeSize)
                    dir.z -= 1f;
                else if (mousePosition.y >= Screen.height - edgeSize)
                    dir.z += 1f;
            }
        }

        if (dir.sqrMagnitude > 1f)
            dir.Normalize();

        Vector3 moveDir = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * dir;
        moveDir.y = 0f;

        transform.position += moveDir * moveSpeed * Time.unscaledDeltaTime;
    }

    private void HandleZoom()
    {
        float rawScroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(rawScroll) < 0.0001f)
            return;

        float scaledScroll = rawScroll * 120f;
        float zoomDelta = scaledScroll * zoomSpeed * Time.unscaledDeltaTime;

        Vector3 newPos = transform.position + transform.forward * zoomDelta;
        newPos.y = Mathf.Clamp(newPos.y, minHeight, maxHeight);
        transform.position = newPos;
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;

        pos.x = Mathf.Clamp(pos.x, minXmaxX.x, minXmaxX.y);
        pos.z = Mathf.Clamp(pos.z, minZmaxZ.x, minZmaxZ.y);

        transform.position = pos;
    }
}
