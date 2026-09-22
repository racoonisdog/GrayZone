using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임플레이 중 Escape 입력으로 일시정지 메뉴와 설정 화면을 전환합니다.
/// </summary>
/// <remarks>
/// Figma의 1920x1080 Gameplay Settings UI를 기준으로 런타임 uGUI 계층을 만들며,
/// 설정 값의 실제 소유권은 <see cref="GameSettingManager"/>에 유지합니다.
/// </remarks>
[DefaultExecutionOrder(-1000)]
public sealed class GameplayPauseMenuController : MonoBehaviour
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    [Header("Figma UI Assets")]
    [Tooltip("메뉴와 설정 화면의 한글을 표시할 기본 TMP 폰트입니다.")]
    [SerializeField] private TMP_FontAsset m_regularFont;

    [Tooltip("큰 메뉴 버튼에 사용할 굵은 TMP 폰트입니다. 비어 있으면 기본 폰트를 사용합니다.")]
    [SerializeField] private TMP_FontAsset m_boldFont;

    [Tooltip("Figma Setting Scene에서 내보낸 24x24 설정 아이콘입니다.")]
    [SerializeField] private Sprite m_settingsIcon;

    [Tooltip("Figma Setting Scene에서 내보낸 뒤로가기 버튼 프레임입니다.")]
    [SerializeField] private Sprite m_backButtonFrame;

    [Header("Navigation")]
    [Tooltip("타이틀로 돌아가기 버튼이 불러올 Build Settings 씬 이름입니다.")]
    [SerializeField] private string m_titleSceneName = "MainMenu";

    [Header("Pause")]
    [Tooltip("메뉴가 열려 있는 동안 Time.timeScale을 0으로 만들어 게임플레이를 멈춥니다.")]
    [SerializeField] private bool m_pauseTime = true;

    private readonly List<PlayerInputController> m_fallbackInputControllers = new();
    private readonly Image[] m_tabBackgrounds = new Image[3];
    private readonly Outline[] m_tabOutlines = new Outline[3];

    private GameObject m_viewRoot;
    private GameObject m_pausePage;
    private GameObject m_settingsPage;
    private Button m_resumeButton;
    private Button m_firstSettingsTab;
    private SquadManager m_squadManager;
    private GameSettingManager.SettingSnapshot m_settingsBaseline;
    private float m_previousTimeScale = 1f;
    private CursorLockMode m_previousCursorLockMode;
    private bool m_previousCursorVisible;
    private bool m_isOpen;
    private bool m_restoreCursorInLateUpdate;

    /// <summary>현재 일시정지 UI가 열려 있으면 <c>true</c>입니다.</summary>
    public bool IsOpen => m_isOpen;

    private void Awake()
    {
        ResolveFonts();
        BuildView();
        SetViewActive(false);
    }

    private void Update()
    {
        if (!WasEscapePressed())
        {
            return;
        }

        if (m_isOpen)
        {
            if (m_settingsPage.activeSelf)
            {
                ShowPausePage();
            }
            else
            {
                ResumeGameplay();
            }

            return;
        }

        // 다른 차단 UI가 Escape를 먼저 소비해야 하는 프레임에는 일시정지 메뉴를 열지 않습니다.
        if (Time.timeScale <= 0f || HasOpenBlockingUi())
        {
            return;
        }

        OpenPauseMenu();
    }

    private void LateUpdate()
    {
        if (!m_restoreCursorInLateUpdate)
        {
            return;
        }

        m_restoreCursorInLateUpdate = false;
        Cursor.lockState = m_previousCursorLockMode;
        Cursor.visible = m_previousCursorVisible;
    }

    private void OnDestroy()
    {
        if (m_isOpen)
        {
            RestoreGameplayState();
        }
    }

    /// <summary>게임플레이를 멈추고 Figma Gameplay Settings Screen을 엽니다.</summary>
    public void OpenPauseMenu()
    {
        if (m_isOpen)
        {
            ShowPausePage();
            return;
        }

        m_previousTimeScale = Time.timeScale;
        m_previousCursorLockMode = Cursor.lockState;
        m_previousCursorVisible = Cursor.visible;
        m_isOpen = true;

        SetGameplayInputEnabled(false);
        if (m_pauseTime)
        {
            Time.timeScale = 0f;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetViewActive(true);
        ShowPausePage();
    }

    /// <summary>일시정지 UI를 닫고 이전 게임플레이 상태로 돌아갑니다.</summary>
    public void ResumeGameplay()
    {
        if (!m_isOpen)
        {
            return;
        }

        SetViewActive(false);
        RestoreGameplayState();
        m_restoreCursorInLateUpdate = true;
    }

    /// <summary>현재 설정을 기준점으로 기억하고 Figma Setting Scene 화면을 엽니다.</summary>
    public void OpenSettings()
    {
        GameSettingManager settingManager = GameSettingManager.Instance;
        m_settingsBaseline = settingManager != null ? settingManager.CreateSnapshot() : null;

        m_pausePage.SetActive(false);
        m_settingsPage.SetActive(true);
        SelectSettingsTab(0);
        SelectUiObject(m_firstSettingsTab);
    }

    /// <summary>설정 화면에서 일시정지 메뉴로 돌아갑니다.</summary>
    public void ReturnToPauseMenu()
    {
        ShowPausePage();
    }

    /// <summary>설정 화면을 열었을 때의 값으로 되돌립니다.</summary>
    public void RevertSettings()
    {
        if (m_settingsBaseline == null || GameSettingManager.Instance == null)
        {
            Debug.LogWarning("[GameplayPauseMenu] 되돌릴 설정 스냅샷이 없습니다.", this);
            return;
        }

        GameSettingManager.Instance.ApplySnapshot(m_settingsBaseline);
    }

    /// <summary>현재 설정을 적용하고 사용자 설정 파일에 저장합니다.</summary>
    public void SaveSettings()
    {
        GameSettingManager settingManager = GameSettingManager.Instance;
        if (settingManager == null)
        {
            Debug.LogWarning("[GameplayPauseMenu] GameSettingManager를 찾지 못해 설정을 저장할 수 없습니다.", this);
            return;
        }

        settingManager.ApplySettings();
        if (settingManager.SaveSettings())
        {
            m_settingsBaseline = settingManager.CreateSnapshot();
        }
    }

    /// <summary>Build Settings에 등록된 타이틀 씬으로 이동합니다.</summary>
    public void ReturnToTitle()
    {
        if (string.IsNullOrWhiteSpace(m_titleSceneName)
            || !Application.CanStreamedLevelBeLoaded(m_titleSceneName))
        {
            Debug.LogWarning(
                $"[GameplayPauseMenu] 타이틀 씬 '{m_titleSceneName}'이 Build Settings에 없어 이동하지 않습니다.",
                this);
            return;
        }

        SetViewActive(false);
        RestoreGameplayState();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(m_titleSceneName);
    }

    private void RestoreGameplayState()
    {
        m_isOpen = false;
        if (m_pauseTime)
        {
            Time.timeScale = m_previousTimeScale;
        }

        SetGameplayInputEnabled(true);
        Cursor.lockState = m_previousCursorLockMode;
        Cursor.visible = m_previousCursorVisible;
    }

    private void SetGameplayInputEnabled(bool enabled)
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        }

        if (m_squadManager != null)
        {
            m_squadManager.ApplyInputModeToSquad(enabled);
            return;
        }

        if (!enabled)
        {
            m_fallbackInputControllers.Clear();
            PlayerInputController[] inputs = FindObjectsByType<PlayerInputController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < inputs.Length; i++)
            {
                PlayerInputController input = inputs[i];
                if (input == null || !input.gameObject.activeInHierarchy)
                {
                    continue;
                }

                input.SetInputGate(false);
                m_fallbackInputControllers.Add(input);
            }

            return;
        }

        for (int i = 0; i < m_fallbackInputControllers.Count; i++)
        {
            PlayerInputController input = m_fallbackInputControllers[i];
            if (input != null)
            {
                input.SetInputGate(true);
            }
        }

        m_fallbackInputControllers.Clear();
    }

    private void ShowPausePage()
    {
        m_settingsPage.SetActive(false);
        m_pausePage.SetActive(true);
        SelectUiObject(m_resumeButton);
    }

    private void SelectSettingsTab(int selectedIndex)
    {
        for (int i = 0; i < m_tabBackgrounds.Length; i++)
        {
            bool selected = i == selectedIndex;
            m_tabBackgrounds[i].color = selected
                ? new Color(1f, 1f, 1f, 0.53f)
                : Color.clear;
            m_tabOutlines[i].enabled = selected;
        }
    }

    private bool HasOpenBlockingUi()
    {
        UIManager[] managers = FindObjectsByType<UIManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null && managers[i].isActiveAndEnabled && managers[i].HasOpenBlockingUI)
            {
                return true;
            }
        }

        return false;
    }

    private void BuildView()
    {
        Canvas canvas = GetOrAddComponent<Canvas>(gameObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;

        CanvasScaler scaler = GetOrAddComponent<CanvasScaler>(gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        // Figma의 하단 1019px 액션 바가 초광폭 Game View에서도 잘리지 않도록 높이를 기준으로 맞춥니다.
        scaler.matchWidthOrHeight = 1f;

        GetOrAddComponent<GraphicRaycaster>(gameObject);

        m_viewRoot = CreateStretchObject("RuntimeView", transform);
        BuildPausePage();
        BuildSettingsPage();
    }

    private void BuildPausePage()
    {
        m_pausePage = CreateStretchObject("Gameplay Settings Screen", m_viewRoot.transform);
        CreateStretchImage("Dim Background", m_pausePage.transform, new Color(74f / 255f, 74f / 255f, 74f / 255f, 0.86f));

        m_resumeButton = CreateButton(
            "Resume",
            m_pausePage.transform,
            new Vector2(773f, -249f),
            new Vector2(374f, 133f),
            "이어서 하기",
            48f,
            new Color(30f / 255f, 30f / 255f, 30f / 255f, 1f));
        m_resumeButton.onClick.AddListener(ResumeGameplay);

        Button settingsButton = CreateButton(
            "Settings",
            m_pausePage.transform,
            new Vector2(773f, -468f),
            new Vector2(374f, 133f),
            "설정",
            48f,
            new Color(30f / 255f, 30f / 255f, 30f / 255f, 1f));
        settingsButton.onClick.AddListener(OpenSettings);

        Button titleButton = CreateButton(
            "Return To Title",
            m_pausePage.transform,
            new Vector2(773f, -687f),
            new Vector2(374f, 133f),
            "타이틀로 돌아가기",
            42f,
            new Color(30f / 255f, 30f / 255f, 30f / 255f, 1f));
        titleButton.onClick.AddListener(ReturnToTitle);
    }

    private void BuildSettingsPage()
    {
        m_settingsPage = CreateStretchObject("Setting Scene", m_viewRoot.transform);
        CreateStretchImage("Dim Background", m_settingsPage.transform, new Color(74f / 255f, 74f / 255f, 74f / 255f, 0.86f));

        Image header = CreateTopLeftImage(
            "Setting Popup",
            m_settingsPage.transform,
            new Vector2(45f, 0f),
            new Vector2(1920f, 48f),
            new Color(217f / 255f, 217f / 255f, 217f / 255f, 0.46f));
        header.raycastTarget = true;

        Image icon = CreateTopLeftImage(
            "Settings Icon",
            m_settingsPage.transform,
            new Vector2(60f, -13f),
            new Vector2(24f, 24f),
            Color.white);
        icon.sprite = m_settingsIcon;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        CreateTopLeftText(
            "Settings Title",
            m_settingsPage.transform,
            new Vector2(145f, -8f),
            new Vector2(110f, 32f),
            "설정",
            24f,
            TextAlignmentOptions.Left);

        CreateTopLeftImage(
            "Tabs Background",
            m_settingsPage.transform,
            new Vector2(399f, 0f),
            new Vector2(1566f, 48f),
            new Color(217f / 255f, 217f / 255f, 217f / 255f, 0.56f));

        string[] tabLabels = { "오디오", "전투", "고급 설정" };
        for (int i = 0; i < tabLabels.Length; i++)
        {
            int tabIndex = i;
            Button tab = CreateButton(
                $"Tab {tabLabels[i]}",
                m_settingsPage.transform,
                new Vector2(399f + (200f * i), 0f),
                new Vector2(200f, 48f),
                tabLabels[i],
                24f,
                Color.clear);

            Image tabImage = tab.GetComponent<Image>();
            Outline outline = tab.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(242f / 255f, 0f, 0f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
            tab.onClick.AddListener(() => SelectSettingsTab(tabIndex));

            m_tabBackgrounds[i] = tabImage;
            m_tabOutlines[i] = outline;
            if (i == 0)
            {
                m_firstSettingsTab = tab;
            }
        }

        Button revertButton = CreateButton(
            "Revert",
            m_settingsPage.transform,
            new Vector2(1455f, -1019f),
            new Vector2(102f, 32f),
            "되돌리기",
            16f,
            new Color(147f / 255f, 147f / 255f, 147f / 255f, 0.85f));
        revertButton.onClick.AddListener(RevertSettings);

        Button saveButton = CreateButton(
            "Save Changes",
            m_settingsPage.transform,
            new Vector2(1627f, -1019f),
            new Vector2(102f, 32f),
            "변경사항 저장",
            16f,
            new Color(147f / 255f, 147f / 255f, 147f / 255f, 0.85f));
        saveButton.onClick.AddListener(SaveSettings);

        Button backButton = CreateButton(
            "Back",
            m_settingsPage.transform,
            new Vector2(1786f, -1019f),
            new Vector2(102f, 32f),
            "뒤로가기",
            24f,
            Color.clear,
            m_backButtonFrame);
        backButton.onClick.AddListener(ReturnToPauseMenu);

        SelectSettingsTab(0);
    }

    private Button CreateButton(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size,
        string label,
        float fontSize,
        Color backgroundColor,
        Sprite backgroundSprite = null)
    {
        GameObject buttonObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        Image image = buttonObject.AddComponent<Image>();
        image.color = backgroundColor;
        image.sprite = backgroundSprite;
        image.raycastTarget = true;

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        TextMeshProUGUI text = CreateStretchText("Label", buttonObject.transform, label, fontSize, TextAlignmentOptions.Center);
        text.font = fontSize >= 40f && m_boldFont != null ? m_boldFont : m_regularFont;
        return button;
    }

    private TextMeshProUGUI CreateTopLeftText(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        return ConfigureText(textObject.AddComponent<TextMeshProUGUI>(), value, fontSize, alignment);
    }

    private TextMeshProUGUI CreateStretchText(
        string name,
        Transform parent,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateStretchObject(name, parent);
        return ConfigureText(textObject.AddComponent<TextMeshProUGUI>(), value, fontSize, alignment);
    }

    private TextMeshProUGUI ConfigureText(
        TextMeshProUGUI text,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        text.text = value;
        text.font = m_regularFont;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Normal;
        text.color = Color.white;
        text.alignment = alignment;
        text.overflowMode = TextOverflowModes.Overflow;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateStretchImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = CreateStretchObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Image CreateTopLeftImage(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size,
        Color color)
    {
        GameObject imageObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateStretchObject(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return child;
    }

    private static GameObject CreateTopLeftObject(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftPosition;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return child;
    }

    private void ResolveFonts()
    {
        TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (m_regularFont == null)
        {
            m_regularFont = FindFont(loadedFonts, "GmarketSansTTFMedium")
                ?? TMP_Settings.defaultFontAsset;
        }

        if (m_boldFont == null)
        {
            m_boldFont = FindFont(loadedFonts, "GmarketSansTTFBold")
                ?? m_regularFont;
        }
    }

    private static TMP_FontAsset FindFont(TMP_FontAsset[] fonts, string namePart)
    {
        for (int i = 0; i < fonts.Length; i++)
        {
            TMP_FontAsset font = fonts[i];
            if (font != null && font.name.Contains(namePart, System.StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }
        }

        return null;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private void SetViewActive(bool active)
    {
        if (m_viewRoot != null && m_viewRoot.activeSelf != active)
        {
            m_viewRoot.SetActive(active);
        }
    }

    private static void SelectUiObject(Selectable selectable)
    {
        if (selectable == null || EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(selectable.gameObject);
    }

    private static bool WasEscapePressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }
}
