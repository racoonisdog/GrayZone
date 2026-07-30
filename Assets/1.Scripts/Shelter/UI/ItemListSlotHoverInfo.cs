using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// 아이템 목록 슬롯의 포인터 진입 여부와 표시 데이터를 공용 말풍선에 전달합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ItemListSlotHoverInfo : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [FormerlySerializedAs("m_infoRoot")]
    [SerializeField, HideInInspector] private GameObject m_legacyInfoRoot;
    [SerializeField] private RectTransform m_anchor;
    [SerializeField] private bool m_infoEnabled = true;

    private ItemListTooltipPresenter m_presenter;
    private string m_displayName = string.Empty;
    private string m_info = string.Empty;

    public bool InfoEnabled => m_infoEnabled;

    private void Awake()
    {
        if (m_anchor == null)
            m_anchor = transform as RectTransform;

        if (m_legacyInfoRoot != null)
            m_legacyInfoRoot.SetActive(false);

        HideInfo();
    }

    private void OnDisable()
    {
        HideInfo();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (m_infoEnabled && m_presenter != null)
        {
            m_presenter.Show(
                this,
                m_anchor,
                m_displayName,
                m_info);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideInfo();
    }

    /// <summary>
    /// 현재 슬롯의 호버 정보 표시 허용 여부를 갱신합니다.
    /// </summary>
    public void SetInfoEnabled(bool isEnabled)
    {
        m_infoEnabled = isEnabled;
        if (!m_infoEnabled)
            HideInfo();
    }

    /// <summary>공용 말풍선과 이 슬롯이 표시할 정적 정보를 연결합니다.</summary>
    public void Bind(
        ItemListTooltipPresenter presenter,
        string displayName,
        string info)
    {
        HideInfo();
        m_presenter = presenter;
        m_displayName = displayName ?? string.Empty;
        m_info = info ?? string.Empty;
    }

    /// <summary>현재 말풍선 연결과 표시 정보를 비웁니다.</summary>
    public void Clear()
    {
        HideInfo();
        m_presenter = null;
        m_displayName = string.Empty;
        m_info = string.Empty;
        m_infoEnabled = false;
    }

    private void HideInfo()
    {
        m_presenter?.Hide(this);
    }
}
