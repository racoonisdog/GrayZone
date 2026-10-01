using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 페이지를 화면에 그리는 공용 템플릿입니다. 페이지 순서와 넘김 조건은 <see cref="TutorialManager"/>가 정합니다.
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: 이 컴포넌트는 UI를 만들지 않고, 씬에 미리 배치된 자식을
/// 이름으로 찾아 값(텍스트, 이미지, 표시 여부)만 채웁니다. 레이아웃은 씬에서 조정합니다(Figma `Tutorial HUD` 기준).
///
/// 튜토리얼이 떠 있는 동안 좌측 공지(<see cref="m_noticeRoot"/>)도 함께 띄웁니다. 공지 문구는 씬에 적어 둔 그대로입니다.
///
/// 페이지에 <see cref="TutorialPage.OverridePrefab"/>이 있으면 공용 패널을 끄고 그 프리팹을 이 오브젝트 아래에 띄웁니다.
/// 프리팹 안에 Title/Body/Hint/Image/KeyIcon 이름의 자식이 있으면 같은 내용을 채웁니다.
/// </remarks>
[DisallowMultipleComponent]
public class DefenseTutorialOverlay : MonoBehaviour
{
    private const string PanelName = "Panel";
    private const string TitleName = "Title";
    private const string BodyName = "Body";
    private const string HintName = "Hint";
    private const string ImageName = "Image";
    private const string KeyIconName = "KeyIcon";
    private const string NoticeName = "Notice";
    private const string KeyGaugeFillName = "KeyGauge/Fill";

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("안내 패널 루트입니다. 이 오브젝트를 켜고 끄는 것으로 표시를 전환합니다. 비어 있으면 자식 'Panel'을 찾습니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("페이지 제목 텍스트입니다. 선택 항목이며, 비어 있으면 패널 아래 'Title'을 찾습니다.")]
    [SerializeField] private TMP_Text m_titleText;

    [Tooltip("안내 본문 텍스트입니다. 비어 있으면 자식 'Body'를 찾습니다.")]
    [SerializeField] private TMP_Text m_bodyText;

    [Tooltip("하단 안내 텍스트입니다. 비어 있으면 자식 'Hint'를 찾습니다.")]
    [SerializeField] private TMP_Text m_hintText;

    [Tooltip("페이지 이미지입니다. 선택 항목이며, 비어 있으면 패널 아래 'Image'를 찾습니다.")]
    [SerializeField] private Image m_image;

    [Tooltip("본문 사이에 끼워 넣는 키 아이콘입니다. 선택 항목이며, 비어 있으면 패널 아래 'KeyIcon'을 찾습니다.")]
    [SerializeField] private Image m_keyIcon;

    [Tooltip("넘김 키 게이지의 채움 링입니다(Image, Filled / Radial 360). 선택 항목이며, 비어 있으면 패널 아래 'KeyGauge/Fill'을 찾습니다.")]
    [SerializeField] private Image m_keyGaugeFill;

    [Tooltip("튜토리얼과 함께 띄울 좌측 공지 루트입니다. 선택 항목이며, 비어 있으면 자식 'Notice'를 찾습니다.")]
    [SerializeField] private GameObject m_noticeRoot;

    [Header("Text")]
    [Min(0.0f)]
    [Tooltip("본문 줄 간격(px)입니다. Figma 본문은 39px입니다. 0이면 폰트 기본 줄 간격을 씁니다.")]
    [SerializeField] private float m_bodyLineHeight = 39.0f;

    /// <summary>현재 페이지를 대신 그리고 있는 프리팹 인스턴스입니다.</summary>
    private GameObject m_overrideInstance;

    /// <summary>Awake 전에 Show가 불렸는지입니다. 오브젝트가 꺼진 채 시작해 Awake가 늦게 돌 때 표시를 덮어쓰지 않기 위해 씁니다.</summary>
    private bool m_showRequested;

    /// <summary>안내가 현재 표시 중인지 여부입니다.</summary>
    public bool IsShown => (m_panelRoot != null && m_panelRoot.activeSelf) || m_overrideInstance != null;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();

        // 첫 페이지는 TutorialManager가 띄웁니다. 매니저가 없으면 안내는 뜨지 않습니다.
        if (!m_showRequested)
        {
            SetPanelVisible(false);
            SetNoticeVisible(false);
        }
    }

    /// <summary>
    /// 페이지 내용을 표시합니다. 이전 페이지가 띄운 프리팹은 제거합니다.
    /// </summary>
    public void Show(TutorialPage page)
    {
        m_showRequested = true;
        AutoFindReferences();
        ClearOverride();
        SetNoticeVisible(true);

        if (page.OverridePrefab != null)
        {
            SetPanelVisible(false);
            m_overrideInstance = Instantiate(page.OverridePrefab, transform);
            FillByName(m_overrideInstance.transform, page);
            return;
        }

        ApplyPage(m_titleText, m_bodyText, m_hintText, m_image, m_keyIcon, page);
        SetPanelVisible(true);
    }

    /// <summary>안내를 감춥니다. 띄워 둔 프리팹과 좌측 공지도 함께 감춥니다.</summary>
    public void Hide()
    {
        m_showRequested = false;
        ClearOverride();
        SetPanelVisible(false);
        SetNoticeVisible(false);
    }

    private void ClearOverride()
    {
        if (m_overrideInstance != null)
        {
            Destroy(m_overrideInstance);
            m_overrideInstance = null;
        }
    }

    private void AutoFindReferences()
    {
        if (m_panelRoot == null)
        {
            Transform panel = transform.Find(PanelName);
            if (panel != null)
            {
                m_panelRoot = panel.gameObject;
            }
        }

        if (m_noticeRoot == null)
        {
            Transform notice = transform.Find(NoticeName);
            if (notice != null)
            {
                m_noticeRoot = notice.gameObject;
            }
        }

        Transform searchRoot = m_panelRoot != null ? m_panelRoot.transform : transform;

        if (m_titleText == null)
        {
            m_titleText = FindChild<TMP_Text>(searchRoot, TitleName);
        }

        if (m_bodyText == null)
        {
            m_bodyText = FindChild<TMP_Text>(searchRoot, BodyName);
        }

        if (m_hintText == null)
        {
            m_hintText = FindChild<TMP_Text>(searchRoot, HintName);
        }

        if (m_image == null)
        {
            m_image = FindChild<Image>(searchRoot, ImageName);
        }

        if (m_keyIcon == null)
        {
            m_keyIcon = FindChild<Image>(searchRoot, KeyIconName);
        }

        if (m_keyGaugeFill == null)
        {
            m_keyGaugeFill = FindChild<Image>(searchRoot, KeyGaugeFillName);
        }
    }

    private void FillByName(Transform root, TutorialPage page)
    {
        ApplyPage(
            FindChild<TMP_Text>(root, TitleName),
            FindChild<TMP_Text>(root, BodyName),
            FindChild<TMP_Text>(root, HintName),
            FindChild<Image>(root, ImageName),
            FindChild<Image>(root, KeyIconName),
            page);
    }

    private void ApplyPage(TMP_Text title, TMP_Text body, TMP_Text hint, Image image, Image keyIcon, TutorialPage page)
    {
        // 제목이 비어 있으면 템플릿에 적힌 제목("튜토리얼")을 그대로 둡니다.
        if (title != null && !string.IsNullOrEmpty(page.Title))
        {
            title.text = page.Title;
        }

        if (body != null)
        {
            // Figma 본문 줄 간격(39px)을 폰트와 관계없이 맞추려고 줄 간격 태그를 앞에 붙입니다.
            body.text = m_bodyLineHeight > 0.0f
                ? $"<line-height={m_bodyLineHeight:0.##}px>{page.Body}"
                : page.Body;
        }

        if (hint != null)
        {
            hint.text = page.Hint;
        }

        if (image != null)
        {
            image.sprite = page.Image;
            image.gameObject.SetActive(page.Image != null);
        }

        ApplyKeyIcon(keyIcon, page);
    }

    /// <summary>
    /// 페이지의 키 아이콘을 지정한 위치와 크기로 놓습니다. 위치는 패널 왼쪽 위 기준이고 y는 아래쪽이 양수입니다.
    /// </summary>
    private static void ApplyKeyIcon(Image keyIcon, TutorialPage page)
    {
        if (keyIcon == null)
        {
            return;
        }

        bool hasIcon = page.KeyIcon != null;
        keyIcon.gameObject.SetActive(hasIcon);
        if (!hasIcon)
        {
            return;
        }

        keyIcon.sprite = page.KeyIcon;

        RectTransform rect = keyIcon.rectTransform;
        rect.anchorMin = new Vector2(0.0f, 1.0f);
        rect.anchorMax = new Vector2(0.0f, 1.0f);
        rect.pivot = new Vector2(0.0f, 1.0f);
        rect.anchoredPosition = new Vector2(page.KeyIconPosition.x, -page.KeyIconPosition.y);
        if (page.KeyIconSize.x > 0.0f && page.KeyIconSize.y > 0.0f)
        {
            rect.sizeDelta = page.KeyIconSize;
        }
    }

    /// <summary>
    /// 넘김 키 게이지를 채웁니다. 0이면 바닥 링만 보이고, 1이면 한 바퀴가 다 찬 상태입니다.
    /// </summary>
    public void SetGaugeProgress(float progress01)
    {
        if (m_keyGaugeFill != null)
        {
            m_keyGaugeFill.fillAmount = Mathf.Clamp01(progress01);
        }
    }

    private static T FindChild<T>(Transform root, string name) where T : Component
    {
        Transform child = root.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }

    private void SetPanelVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }

    private void SetNoticeVisible(bool visible)
    {
        if (m_noticeRoot != null && m_noticeRoot.activeSelf != visible)
        {
            m_noticeRoot.SetActive(visible);
        }
    }
}
