using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방어전 HUD에 현재 함정 재료 보유량을 상시 표시합니다.
/// </summary>
/// <remarks>
/// 임시 HUD라 실행할 때 시각 요소를 만들며, 씬에는 이 컴포넌트와 공용 아이콘·폰트 참조만 둡니다.
/// 함정 설치와 방어전 트레이너가 함께 사용하는 <see cref="StorageFacility"/>의 변경 알림을 받아 즉시 갱신합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TrapMaterialHud : MonoBehaviour
{
    [Header("Layout (1920 x 1080 Reference)")]
    [SerializeField] private Vector2 m_anchoredPosition = new(40f, -278f);
    [SerializeField] private Vector2 m_size = new(280f, 52f);

    [Header("Presentation")]
    [SerializeField] private Sprite m_iconSprite;
    [SerializeField] private TMP_FontAsset m_fontAsset;
    [SerializeField] private string m_label = "함정 재료";
    [SerializeField] private Color m_backgroundColor = new(0.025f, 0.035f, 0.045f, 0.82f);
    [SerializeField] private Color m_accentColor = new(0.953f, 0.741f, 0.008f, 1f);
    [SerializeField] private Color m_textColor = Color.white;

    private StorageFacility m_storage;
    private TextMeshProUGUI m_amountText;

    private void Awake()
    {
        BuildVisual();
    }

    private void OnEnable()
    {
        BindStorage();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindStorage();
    }

    private void BuildVisual()
    {
        GameObject panelObject = CreateUiObject("Trap Material HUD", transform);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = m_anchoredPosition;
        panelRect.sizeDelta = m_size;

        Image background = panelObject.AddComponent<Image>();
        background.color = m_backgroundColor;
        background.raycastTarget = false;

        GameObject accentObject = CreateUiObject("Accent", panelRect);
        RectTransform accentRect = accentObject.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(4f, 0f);
        Image accent = accentObject.AddComponent<Image>();
        accent.color = m_accentColor;
        accent.raycastTarget = false;

        GameObject iconObject = CreateUiObject("Icon", panelRect);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(30f, 0f);
        iconRect.sizeDelta = new Vector2(30f, 30f);
        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = m_iconSprite;
        icon.color = m_accentColor;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        GameObject textObject = CreateUiObject("Amount", panelRect);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(54f, 4f);
        textRect.offsetMax = new Vector2(-12f, -4f);

        m_amountText = textObject.AddComponent<TextMeshProUGUI>();
        m_amountText.font = m_fontAsset;
        m_amountText.fontSize = 22f;
        m_amountText.fontStyle = FontStyles.Normal;
        m_amountText.color = m_textColor;
        m_amountText.alignment = TextAlignmentOptions.MidlineLeft;
        m_amountText.textWrappingMode = TextWrappingModes.NoWrap;
        m_amountText.overflowMode = TextOverflowModes.Overflow;
        m_amountText.raycastTarget = false;
    }

    private void BindStorage()
    {
        StorageFacility nextStorage = ShelterSceneDataManager.Instance?.Storage;
        if (ReferenceEquals(m_storage, nextStorage))
        {
            return;
        }

        UnbindStorage();
        m_storage = nextStorage;
        if (m_storage != null)
        {
            m_storage.ResourcesChanged += Refresh;
        }
    }

    private void UnbindStorage()
    {
        if (m_storage != null)
        {
            m_storage.ResourcesChanged -= Refresh;
            m_storage = null;
        }
    }

    private void Refresh()
    {
        if (m_amountText == null)
        {
            return;
        }

        int amount = m_storage?.GetResourceAmount(ResourceIds.TrapMaterial) ?? 0;
        m_amountText.text = $"{m_label}   <b>{amount}</b>";
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject child = new(objectName, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return child;
    }
}
