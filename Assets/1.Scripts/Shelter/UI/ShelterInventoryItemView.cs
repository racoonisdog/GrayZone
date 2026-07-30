using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 셸터 인벤토리에서 자원 또는 제작 아이템 하나의 아이콘과 보유 수량만 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShelterInventoryItemView : MonoBehaviour
{
    [SerializeField] private Image m_iconImage;
    [SerializeField] private TMP_Text m_quantityText;

    public string StableKey { get; private set; } = string.Empty;

    /// <summary>UI 표시용 값만 반영하며 창고 데이터는 변경하지 않습니다.</summary>
    public void Bind(ShelterInventoryDisplayEntry entry)
    {
        CacheReferences();
        StableKey = entry.StableKey;
        gameObject.SetActive(true);

        if (m_iconImage != null)
        {
            m_iconImage.sprite = entry.Icon;
            m_iconImage.enabled = entry.Icon != null;
        }

        if (m_quantityText != null)
            m_quantityText.text = entry.Quantity.ToString();
    }

    public void Hide()
    {
        StableKey = string.Empty;
        gameObject.SetActive(false);
    }

    private void Reset()
    {
        CacheReferences();
    }

    private void CacheReferences()
    {
        if (m_iconImage == null)
            m_iconImage = GetComponentInChildren<Image>(true);

        if (m_quantityText == null)
            m_quantityText = GetComponentInChildren<TMP_Text>(true);
    }
}
