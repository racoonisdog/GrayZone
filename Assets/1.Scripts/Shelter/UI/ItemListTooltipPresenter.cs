using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 마스크 밖에 배치된 공용 아이템 정보 말풍선의 내용과 위치를 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ItemListTooltipPresenter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform m_infoRoot;
    [SerializeField] private TMP_Text m_nameText;
    [SerializeField] private TMP_Text m_infoText;
    [SerializeField] private RectTransform m_boundaryRoot;

    [Header("Position")]
    [SerializeField] private Vector2 m_offset = new(12f, 0f);
    [SerializeField] private Vector2 m_pivot = new(0f, 1f);
    [SerializeField] private bool m_clampToBoundary = true;

    private readonly Vector3[] m_worldCorners = new Vector3[4];

    private Object m_owner;
    private bool m_showRequested;

    private void Awake()
    {
        ResolveReferences();

        if (!m_showRequested)
            SetRootActive(false);
    }

    private void OnDisable()
    {
        m_owner = null;

        if (m_infoRoot != null && m_infoRoot.gameObject != gameObject)
            SetRootActive(false);
    }

    /// <summary>
    /// 요청한 슬롯의 오른쪽 위를 기준으로 말풍선 내용과 위치를 갱신합니다.
    /// </summary>
    public bool Show(
        Object owner,
        RectTransform anchor,
        string displayName,
        string info)
    {
        if (owner == null || anchor == null)
            return false;

        m_showRequested = true;
        ResolveReferences();
        if (m_infoRoot == null)
        {
            m_showRequested = false;
            return false;
        }

        if (m_nameText != null)
            m_nameText.text = displayName ?? string.Empty;

        if (m_infoText != null)
            m_infoText.text = info ?? string.Empty;

        m_infoRoot.pivot = m_pivot;
        SetRootActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(m_infoRoot);

        anchor.GetWorldCorners(m_worldCorners);
        m_infoRoot.position = m_worldCorners[2];
        m_infoRoot.anchoredPosition += m_offset;

        if (m_clampToBoundary && m_boundaryRoot != null)
            ClampToBoundary();

        m_owner = owner;
        m_showRequested = false;
        return true;
    }

    /// <summary>현재 말풍선 소유 슬롯과 일치할 때만 숨깁니다.</summary>
    public void Hide(Object owner)
    {
        if (owner != null && m_owner != owner)
            return;

        Hide();
    }

    /// <summary>소유 슬롯과 무관하게 공용 말풍선을 숨깁니다.</summary>
    public void Hide()
    {
        m_owner = null;
        SetRootActive(false);
    }

    private void ResolveReferences()
    {
        if (m_infoRoot == null)
            m_infoRoot = transform as RectTransform;

        if (m_nameText == null && m_infoRoot != null)
        {
            Transform nameRoot = m_infoRoot.Find("name");
            if (nameRoot != null)
                m_nameText = nameRoot.GetComponent<TMP_Text>();
        }

        if (m_infoText == null && m_infoRoot != null)
        {
            Transform infoRoot = m_infoRoot.Find("info");
            if (infoRoot != null)
                m_infoText = infoRoot.GetComponent<TMP_Text>();
        }

        if (m_boundaryRoot == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                m_boundaryRoot = canvas.transform as RectTransform;
        }
    }

    private void ClampToBoundary()
    {
        m_infoRoot.GetWorldCorners(m_worldCorners);

        Vector3 tooltipMin =
            m_boundaryRoot.InverseTransformPoint(m_worldCorners[0]);
        Vector3 tooltipMax =
            m_boundaryRoot.InverseTransformPoint(m_worldCorners[2]);
        Rect boundary = m_boundaryRoot.rect;

        float deltaX = 0f;
        if (tooltipMin.x < boundary.xMin)
            deltaX = boundary.xMin - tooltipMin.x;
        else if (tooltipMax.x > boundary.xMax)
            deltaX = boundary.xMax - tooltipMax.x;

        float deltaY = 0f;
        if (tooltipMin.y < boundary.yMin)
            deltaY = boundary.yMin - tooltipMin.y;
        else if (tooltipMax.y > boundary.yMax)
            deltaY = boundary.yMax - tooltipMax.y;

        Vector3 worldDelta = m_boundaryRoot.TransformVector(
            new Vector3(deltaX, deltaY, 0f));
        m_infoRoot.position += worldDelta;
    }

    private void SetRootActive(bool isActive)
    {
        if (m_infoRoot != null
            && m_infoRoot.gameObject.activeSelf != isActive)
        {
            m_infoRoot.gameObject.SetActive(isActive);
        }
    }
}
