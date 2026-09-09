using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 안에서는 월드 아이콘을 표시하고, HUD에서는 항상 목표 방향을 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ObjectiveIndicatorController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("화면 안/밖을 판정할 월드 위치입니다. 월드 아이콘 루트 자체를 지정해도 됩니다.")]
    [SerializeField] private Transform m_target;

    [Tooltip("목표 Transform에서 판정할 위치를 보정합니다.")]
    [SerializeField] private Vector3 m_targetOffset;

    [Header("Views")]
    [Tooltip("목표가 화면 안에 있을 때 표시할 World Space UI 루트입니다.")]
    [SerializeField] private GameObject m_worldViewRoot;

    [Tooltip("목표 방향 아이콘이 속한 Screen Space Canvas의 RectTransform입니다.")]
    [SerializeField] private RectTransform m_overlayCanvasRect;

    [Tooltip("화면 가장자리에 배치하고 목표 방향으로 회전시킬 Image의 RectTransform입니다.")]
    [SerializeField] private RectTransform m_offscreenImage;

    [Header("Settings")]
    [Tooltip("비워 두면 Main Camera를 사용합니다.")]
    [SerializeField] private Camera m_worldCamera;

    [Min(0.0f)]
    [SerializeField] private float m_edgePaddingPixels = 60.0f;

    [Tooltip("테스트용 초기 표시 상태입니다. 실제 기능에서는 SetVisible로 제어합니다.")]
    [SerializeField] private bool m_visibleOnStart;

    private bool m_visibleRequested;

    private void Reset()
    {
        AutoFindViewReferences();
    }

    private void Awake()
    {
        AutoFindViewReferences();
        CacheMainCamera();

        m_visibleRequested = m_visibleOnStart;
        UpdateIndicator();
    }

    private void OnEnable()
    {
        CacheMainCamera();
        UpdateIndicator();
    }

    private void LateUpdate()
    {
        UpdateIndicator();
    }

    private void OnDisable()
    {
        HideViews();
    }

    /// <summary>표시할 목표를 변경합니다.</summary>
    public void SetTarget(Transform target)
    {
        m_target = target;
        UpdateIndicator();
    }

    /// <summary>현재 목표 표시를 켜거나 끕니다.</summary>
    public void SetVisible(bool visible)
    {
        m_visibleRequested = visible;
        UpdateIndicator();
    }

    private void UpdateIndicator()
    {
        if (!m_visibleRequested || !ResolveRequiredReferences())
        {
            HideViews();
            return;
        }

        Vector3 viewport = m_worldCamera.WorldToViewportPoint(m_target.position + m_targetOffset);
        bool isOnScreen = IsOnScreen(viewport);

        SetActive(m_worldViewRoot, isOnScreen);
        SetActive(m_offscreenImage.gameObject, true);
        UpdateOffscreenView(viewport);
    }

    private bool ResolveRequiredReferences()
    {
        if (m_worldCamera == null)
        {
            CacheMainCamera();
        }

        return m_target != null
            && m_worldViewRoot != null
            && m_overlayCanvasRect != null
            && m_offscreenImage != null
            && m_worldCamera != null;
    }

    private bool IsOnScreen(Vector3 viewport)
    {
        float paddingX = Screen.width > 0 ? m_edgePaddingPixels / Screen.width : 0.0f;
        float paddingY = Screen.height > 0 ? m_edgePaddingPixels / Screen.height : 0.0f;

        return viewport.z > 0.0f
            && viewport.x >= paddingX
            && viewport.x <= 1.0f - paddingX
            && viewport.y >= paddingY
            && viewport.y <= 1.0f - paddingY;
    }

    private void UpdateOffscreenView(Vector3 viewport)
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 screenPosition = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        Vector2 direction = screenPosition - screenCenter;

        if (viewport.z < 0.0f)
        {
            direction = -direction;
        }

        if (direction.sqrMagnitude < 1.0f)
        {
            direction = viewport.z >= 0.0f ? Vector2.up : Vector2.down;
        }

        float availableHalfWidth = Mathf.Max(0.0f, screenCenter.x - m_edgePaddingPixels);
        float availableHalfHeight = Mathf.Max(0.0f, screenCenter.y - m_edgePaddingPixels);
        float scaleX = availableHalfWidth / Mathf.Max(Mathf.Abs(direction.x), 0.0001f);
        float scaleY = availableHalfHeight / Mathf.Max(Mathf.Abs(direction.y), 0.0001f);
        Vector2 clampedScreenPosition = screenCenter + direction * Mathf.Min(scaleX, scaleY);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            m_overlayCanvasRect,
            clampedScreenPosition,
            null,
            out Vector2 localPosition);

        m_offscreenImage.anchoredPosition = localPosition;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90.0f;
        m_offscreenImage.localRotation = Quaternion.Euler(0.0f, 0.0f, angle);
    }

    private void AutoFindViewReferences()
    {
        Canvas canvas = GetComponentInChildren<Canvas>(true);

        if (m_overlayCanvasRect == null && canvas != null)
        {
            m_overlayCanvasRect = canvas.transform as RectTransform;
        }

        if (m_offscreenImage == null && canvas != null)
        {
            Image image = canvas.GetComponentInChildren<Image>(true);
            if (image != null)
            {
                m_offscreenImage = image.rectTransform;
            }
        }
    }

    private void CacheMainCamera()
    {
        if (m_worldCamera == null && Camera.main != null)
        {
            m_worldCamera = Camera.main;
        }
    }

    private void HideViews()
    {
        SetActive(m_worldViewRoot, false);

        if (m_offscreenImage != null)
        {
            SetActive(m_offscreenImage.gameObject, false);
        }
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
