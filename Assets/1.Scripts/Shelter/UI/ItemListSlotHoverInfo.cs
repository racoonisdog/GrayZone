using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 아이템 목록 슬롯의 포인터 진입 여부에 따라 정보 영역을 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ItemListSlotHoverInfo : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField] private GameObject m_infoRoot;
    [SerializeField] private bool m_infoEnabled = true;

    public bool InfoEnabled => m_infoEnabled;

    private void Awake()
    {
        HideInfo();
    }

    private void OnDisable()
    {
        HideInfo();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (m_infoEnabled && m_infoRoot != null)
            m_infoRoot.SetActive(true);
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

    private void HideInfo()
    {
        if (m_infoRoot != null)
            m_infoRoot.SetActive(false);
    }
}
