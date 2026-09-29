using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 페이지를 화면에 그리는 공용 템플릿입니다. 페이지 순서와 넘김 조건은 <see cref="TutorialManager"/>가 정합니다.
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: 이 컴포넌트는 UI를 만들지 않고, 씬에 미리 배치된 자식을
/// 이름으로 찾아 값(텍스트, 이미지, 표시 여부)만 채웁니다. 레이아웃은 씬에서 조정합니다.
///
/// 페이지에 <see cref="TutorialPage.OverridePrefab"/>이 있으면 공용 패널을 끄고 그 프리팹을 이 오브젝트 아래에 띄웁니다.
/// 프리팹 안에 Title/Body/Hint/Image 이름의 자식이 있으면 같은 내용을 채웁니다.
/// </remarks>
[DisallowMultipleComponent]
public class DefenseTutorialOverlay : MonoBehaviour
{
    private const string PanelName = "Panel";
    private const string TitleName = "Title";
    private const string BodyName = "Body";
    private const string HintName = "Hint";
    private const string ImageName = "Image";

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

        if (page.OverridePrefab != null)
        {
            SetPanelVisible(false);
            m_overrideInstance = Instantiate(page.OverridePrefab, transform);
            FillByName(m_overrideInstance.transform, page);
            return;
        }

        ApplyTexts(m_titleText, m_bodyText, m_hintText, m_image, page);
        SetPanelVisible(true);
    }

    /// <summary>안내를 감춥니다. 띄워 둔 프리팹도 제거합니다.</summary>
    public void Hide()
    {
        m_showRequested = false;
        ClearOverride();
        SetPanelVisible(false);
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
    }

    private static void FillByName(Transform root, TutorialPage page)
    {
        ApplyTexts(
            FindChild<TMP_Text>(root, TitleName),
            FindChild<TMP_Text>(root, BodyName),
            FindChild<TMP_Text>(root, HintName),
            FindChild<Image>(root, ImageName),
            page);
    }

    private static void ApplyTexts(TMP_Text title, TMP_Text body, TMP_Text hint, Image image, TutorialPage page)
    {
        if (title != null)
        {
            title.text = page.Title;
        }

        if (body != null)
        {
            body.text = page.Body;
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
}
