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
    [SerializeField] private string m_titleSceneName = "TitleScene";

    [Header("Pause")]
    [Tooltip("메뉴가 열려 있는 동안 Time.timeScale을 0으로 만들어 게임플레이를 멈춥니다.")]
    [SerializeField] private bool m_pauseTime = true;

    private readonly List<PlayerInputController> m_fallbackInputControllers = new();
    private readonly Image[] m_tabBackgrounds = new Image[3];
    private readonly Outline[] m_tabOutlines = new Outline[3];
    private readonly GameObject[] m_settingsContentPages = new GameObject[3];
    private readonly Image[] m_combatSubTabBackgrounds = new Image[3];
    private readonly Outline[] m_combatSubTabOutlines = new Outline[3];
    private readonly GameObject[] m_combatSubPages = new GameObject[3];

    private GameObject m_viewRoot;
    private GameObject m_pausePage;
    private GameObject m_settingsPage;
    private Button m_resumeButton;
    private Button m_firstSettingsTab;
    private TextMeshProUGUI m_pauseSelectionArrow;
    private Button m_pointerPauseButton;
    private Button m_selectedPauseButton;
    private SquadManager m_squadManager;
    private GameSettingManager.SettingSnapshot m_settingsBaseline;
    private float m_previousTimeScale = 1f;
    private CursorLockMode m_previousCursorLockMode;
    private bool m_previousCursorVisible;
    private bool m_isOpen;
    private bool m_restoreCursorInLateUpdate;
    private bool m_keyboardSelectionRequested;
    private int m_selectedCombatSubTab;
    private CrosshairSettingsMockupPanel m_crosshairSettingsPanel;
    private CrosshairSettingsMockupPanel m_throwableCrosshairSettingsPanel;
    private Slider m_mouseSensitivitySlider;
    private TextMeshProUGUI m_mouseSensitivityValue;
    private Toggle m_cameraKickToggle;
    private float m_mouseSensitivity = 1f;
    private float m_committedMouseSensitivity = 1f;
    private bool m_cameraKickEnabled = true;
    private bool m_committedCameraKickEnabled = true;
    private bool m_isRefreshingCombatGeneral;

    /// <summary>현재 일시정지 UI가 열려 있으면 <c>true</c>입니다.</summary>
    public bool IsOpen => m_isOpen;

    private void Awake()
    {
        EnsureViewBuilt();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            EnsureViewBuilt();
        }
    }

    private void Update()
    {
        TrackPauseNavigationInput();

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
        if (m_isOpen && m_pausePage != null && m_pausePage.activeSelf && m_keyboardSelectionRequested)
        {
            RefreshPauseSelection();
        }

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

    private void OnDisable()
    {
        // Play Mode 종료, 스크립트 리로드, 씬 전환처럼 메뉴가 열린 채 컴포넌트가
        // 비활성화되어도 스쿼드 입력 게이트와 timeScale이 잠긴 채 남지 않게 합니다.
        if (!m_isOpen)
        {
            return;
        }

        SetViewActive(false);
        RestoreGameplayState();
        m_restoreCursorInLateUpdate = false;
    }

    /// <summary>게임플레이를 멈추고 Figma Gameplay Settings Screen을 엽니다.</summary>
    public void OpenPauseMenu()
    {
        EnsureViewBuilt();
        if (m_pausePage == null || m_settingsPage == null)
        {
            Debug.LogError("[GameplayPauseMenu] 런타임 UI를 생성하지 못해 메뉴를 열지 않습니다.", this);
            return;
        }

        if (m_isOpen)
        {
            SetViewActive(true);
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
        EnsureViewBuilt();
        GameSettingManager settingManager = GameSettingManager.Instance;
        m_settingsBaseline = settingManager != null ? settingManager.CreateSnapshot() : null;
        SyncCombatGeneralSettings(settingManager, true);

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
        m_crosshairSettingsPanel?.RevertToCommittedValues();
        m_throwableCrosshairSettingsPanel?.RevertToCommittedValues();

        if (m_settingsBaseline == null || GameSettingManager.Instance == null)
        {
            RevertCombatGeneralSettings();
            Debug.LogWarning("[GameplayPauseMenu] 되돌릴 설정 스냅샷이 없습니다.", this);
            return;
        }

        GameSettingManager.Instance.ApplySnapshot(m_settingsBaseline);
        SyncCombatGeneralSettings(GameSettingManager.Instance, true);
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
            m_crosshairSettingsPanel?.CommitCurrentValues();
            m_throwableCrosshairSettingsPanel?.CommitCurrentValues();
            CommitCombatGeneralSettings();
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
        ResetPauseSelection();
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

            if (m_settingsContentPages[i] != null)
            {
                m_settingsContentPages[i].SetActive(selected);
            }
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

    private void EnsureViewBuilt()
    {
        if (m_viewRoot != null && m_pausePage != null && m_settingsPage != null)
        {
            return;
        }

        if (m_viewRoot != null)
        {
            m_viewRoot.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(m_viewRoot);
            }
            else
            {
                DestroyImmediate(m_viewRoot);
            }
        }

        m_viewRoot = null;
        m_pausePage = null;
        m_settingsPage = null;
        m_resumeButton = null;
        m_firstSettingsTab = null;
        m_pauseSelectionArrow = null;
        m_crosshairSettingsPanel = null;
        m_throwableCrosshairSettingsPanel = null;
        m_mouseSensitivitySlider = null;
        m_mouseSensitivityValue = null;
        m_cameraKickToggle = null;

        ResolveFonts();
        BuildView();
        SetViewActive(false);
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
        BindPauseSelection(m_resumeButton);

        Button settingsButton = CreateButton(
            "Settings",
            m_pausePage.transform,
            new Vector2(773f, -468f),
            new Vector2(374f, 133f),
            "설정",
            48f,
            new Color(30f / 255f, 30f / 255f, 30f / 255f, 1f));
        settingsButton.onClick.AddListener(OpenSettings);
        BindPauseSelection(settingsButton);

        Button titleButton = CreateButton(
            "Return To Title",
            m_pausePage.transform,
            new Vector2(773f, -687f),
            new Vector2(374f, 133f),
            "타이틀로 돌아가기",
            42f,
            new Color(30f / 255f, 30f / 255f, 30f / 255f, 1f));
        titleButton.onClick.AddListener(ReturnToTitle);
        BindPauseSelection(titleButton);

        GameObject selectionOverlay = CreateStretchObject("Selection Overlay", m_pausePage.transform);
        CanvasGroup selectionOverlayGroup = selectionOverlay.AddComponent<CanvasGroup>();
        selectionOverlayGroup.interactable = false;
        selectionOverlayGroup.blocksRaycasts = false;

        m_pauseSelectionArrow = CreateTopLeftText(
            "Selection Arrow",
            selectionOverlay.transform,
            Vector2.zero,
            new Vector2(32f, 48f),
            ">",
            36f,
            TextAlignmentOptions.Center);
        m_pauseSelectionArrow.font = m_boldFont != null ? m_boldFont : m_regularFont;
        m_pauseSelectionArrow.raycastTarget = false;
        m_pauseSelectionArrow.gameObject.SetActive(false);
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

        BuildSettingsContent();

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

    private void BuildSettingsContent()
    {
        for (int i = 0; i < m_settingsContentPages.Length; i++)
        {
            m_settingsContentPages[i] = CreateTopLeftObject(
                $"Settings Content {i}",
                m_settingsPage.transform,
                new Vector2(0f, -48f),
                new Vector2(ReferenceWidth, 971f));
        }

        BuildPlaceholderPage(
            m_settingsContentPages[0].transform,
            "오디오",
            "기존 오디오 설정이 배치될 영역입니다.");
        BuildCombatSettingsPage(m_settingsContentPages[1].transform);
        BuildPlaceholderPage(
            m_settingsContentPages[2].transform,
            "고급 설정",
            "고급 그래픽 및 접근성 설정이 배치될 영역입니다.");
    }

    private void BuildCombatSettingsPage(Transform parent)
    {
        CreateTopLeftText(
            "Combat Section Label",
            parent,
            new Vector2(145f, -38f),
            new Vector2(220f, 38f),
            "전투",
            24f,
            TextAlignmentOptions.Left).font = m_boldFont != null ? m_boldFont : m_regularFont;

        string[] labels = { "전투 일반", "캐릭터 조준선", "투척물 조준선" };
        for (int i = 0; i < labels.Length; i++)
        {
            int subTabIndex = i;
            Button button = CreateButton(
                $"Combat SubTab {labels[i]}",
                parent,
                new Vector2(145f, -96f - (58f * i)),
                new Vector2(220f, 46f),
                labels[i],
                19f,
                Color.clear);
            Image background = button.GetComponent<Image>();
            Outline outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(242f / 255f, 0f, 0f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
            button.onClick.AddListener(() => SelectCombatSubTab(subTabIndex));
            m_combatSubTabBackgrounds[i] = background;
            m_combatSubTabOutlines[i] = outline;
        }

        m_combatSubPages[0] = CreateStretchObject("Combat General Page", parent);
        m_combatSubPages[1] = CreateStretchObject("Crosshair Settings Page", parent);
        m_combatSubPages[2] = CreateStretchObject("Throwable Crosshair Settings Page", parent);
        m_combatSubPages[0].transform.SetAsFirstSibling();
        m_combatSubPages[1].transform.SetAsFirstSibling();
        m_combatSubPages[2].transform.SetAsFirstSibling();

        BuildCombatGeneralSettings(m_combatSubPages[0].transform);

        m_crosshairSettingsPanel = m_combatSubPages[1].AddComponent<CrosshairSettingsMockupPanel>();
        m_crosshairSettingsPanel.Build(
            m_combatSubPages[1].transform as RectTransform,
            m_regularFont,
            m_boldFont);

        m_throwableCrosshairSettingsPanel = m_combatSubPages[2].AddComponent<CrosshairSettingsMockupPanel>();
        m_throwableCrosshairSettingsPanel.BuildSharedThrowable(
            m_combatSubPages[2].transform as RectTransform,
            m_regularFont,
            m_boldFont);

        SelectCombatSubTab(0);
    }

    private void BuildCombatGeneralSettings(Transform parent)
    {
        TextMeshProUGUI title = CreateTopLeftText(
            "Combat General Title",
            parent,
            new Vector2(430f, -34f),
            new Vector2(500f, 50f),
            "전투 일반",
            32f,
            TextAlignmentOptions.Left);
        title.font = m_boldFont != null ? m_boldFont : m_regularFont;

        TextMeshProUGUI notice = CreateTopLeftText(
            "Combat General Notice",
            parent,
            new Vector2(430f, -82f),
            new Vector2(760f, 32f),
            "입력 감도와 전투 화면 효과 설정 초안입니다.",
            17f,
            TextAlignmentOptions.Left);
        notice.color = new Color(1f, 1f, 1f, 0.6f);

        Image panel = CreateTopLeftImage(
            "Combat General Controls",
            parent,
            new Vector2(430f, -132f),
            new Vector2(650f, 270f),
            new Color(0.035f, 0.035f, 0.035f, 0.92f));
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
        outline.effectDistance = new Vector2(1f, -1f);

        CreateTopLeftText(
            "Mouse Sensitivity Label",
            panel.transform,
            new Vector2(32f, -30f),
            new Vector2(220f, 30f),
            "마우스 감도",
            18f,
            TextAlignmentOptions.Left);
        m_mouseSensitivityValue = CreateTopLeftText(
            "Mouse Sensitivity Value",
            panel.transform,
            new Vector2(518f, -30f),
            new Vector2(96f, 30f),
            string.Empty,
            18f,
            TextAlignmentOptions.Right);
        m_mouseSensitivityValue.font = m_boldFont != null ? m_boldFont : m_regularFont;

        GameObject sliderObject = CreateTopLeftObject(
            "Mouse Sensitivity Slider",
            panel.transform,
            new Vector2(32f, -76f),
            new Vector2(582f, 24f));
        m_mouseSensitivitySlider = sliderObject.AddComponent<Slider>();
        m_mouseSensitivitySlider.minValue = 0.01f;
        m_mouseSensitivitySlider.maxValue = 10f;

        Image sliderBackground = CreateStretchImage(
            "Background",
            sliderObject.transform,
            new Color(1f, 1f, 1f, 0.13f));
        sliderBackground.rectTransform.offsetMin = new Vector2(0f, 8f);
        sliderBackground.rectTransform.offsetMax = new Vector2(0f, -8f);

        GameObject fillArea = CreateStretchObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.offsetMin = new Vector2(0f, 8f);
        fillAreaRect.offsetMax = new Vector2(-12f, -8f);
        Image fill = CreateStretchImage("Fill", fillArea.transform, new Color(0.95f, 0.08f, 0.08f, 1f));

        GameObject handleArea = CreateStretchObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.offsetMin = new Vector2(8f, 0f);
        handleAreaRect.offsetMax = new Vector2(-8f, 0f);
        Image handle = CreateTopLeftImage(
            "Handle",
            handleArea.transform,
            Vector2.zero,
            new Vector2(18f, 18f),
            Color.white);
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;

        m_mouseSensitivitySlider.fillRect = fill.rectTransform;
        m_mouseSensitivitySlider.handleRect = handleRect;
        m_mouseSensitivitySlider.targetGraphic = handle;
        m_mouseSensitivitySlider.direction = Slider.Direction.LeftToRight;
        m_mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);

        m_cameraKickToggle = CreateToggle(
            "Camera Kick Toggle",
            panel.transform,
            new Vector2(32f, -150f),
            "카메라 킥 사용");
        m_cameraKickToggle.onValueChanged.AddListener(OnCameraKickChanged);
        TextMeshProUGUI help = CreateTopLeftText(
            "Camera Kick Help",
            panel.transform,
            new Vector2(80f, -192f),
            new Vector2(520f, 26f),
            "끄면 사격 시 카메라 롤·FOV 시각 효과를 표시하지 않습니다.",
            15f,
            TextAlignmentOptions.Left);
        help.color = new Color(1f, 1f, 1f, 0.5f);

        RefreshCombatGeneralControls();
        CommitCombatGeneralSettings();
    }

    private Toggle CreateToggle(string name, Transform parent, Vector2 position, string label)
    {
        GameObject toggleObject = CreateTopLeftObject(name, parent, position, new Vector2(582f, 38f));
        Toggle toggle = toggleObject.AddComponent<Toggle>();
        Image background = CreateTopLeftImage(
            "Background",
            toggleObject.transform,
            Vector2.zero,
            new Vector2(30f, 30f),
            new Color(1f, 1f, 1f, 0.16f));
        Image checkmark = CreateTopLeftImage(
            "Checkmark",
            background.transform,
            new Vector2(6f, -6f),
            new Vector2(18f, 18f),
            new Color(0.95f, 0.08f, 0.08f, 1f));
        CreateTopLeftText(
            "Label",
            toggleObject.transform,
            new Vector2(48f, -1f),
            new Vector2(360f, 32f),
            label,
            18f,
            TextAlignmentOptions.Left);
        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        return toggle;
    }

    private void OnMouseSensitivityChanged(float value)
    {
        if (m_isRefreshingCombatGeneral)
        {
            return;
        }

        m_mouseSensitivity = Mathf.Clamp(Mathf.Round(value * 100f) / 100f, 0.01f, 10f);
        m_mouseSensitivitySlider.SetValueWithoutNotify(m_mouseSensitivity);
        m_mouseSensitivityValue.text = m_mouseSensitivity.ToString("0.00");
        GameSettingManager.Instance?.SetMouseSensitivity(m_mouseSensitivity);
    }

    private void OnCameraKickChanged(bool enabled)
    {
        if (!m_isRefreshingCombatGeneral)
        {
            m_cameraKickEnabled = enabled;
            GameSettingManager.Instance?.SetCameraKickEnabled(enabled);
        }
    }

    private void RefreshCombatGeneralControls()
    {
        if (m_mouseSensitivitySlider == null || m_cameraKickToggle == null)
        {
            return;
        }

        m_isRefreshingCombatGeneral = true;
        m_mouseSensitivitySlider.SetValueWithoutNotify(m_mouseSensitivity);
        m_cameraKickToggle.SetIsOnWithoutNotify(m_cameraKickEnabled);
        m_mouseSensitivityValue.text = m_mouseSensitivity.ToString("0.00");
        m_isRefreshingCombatGeneral = false;
    }

    private void CommitCombatGeneralSettings()
    {
        m_committedMouseSensitivity = m_mouseSensitivity;
        m_committedCameraKickEnabled = m_cameraKickEnabled;
    }

    private void SyncCombatGeneralSettings(GameSettingManager settingManager, bool commit)
    {
        if (settingManager == null)
        {
            return;
        }

        m_mouseSensitivity = settingManager.MouseSensitivity;
        m_cameraKickEnabled = settingManager.CameraKickEnabled;
        RefreshCombatGeneralControls();

        if (commit)
        {
            CommitCombatGeneralSettings();
        }
    }

    private void RevertCombatGeneralSettings()
    {
        m_mouseSensitivity = m_committedMouseSensitivity;
        m_cameraKickEnabled = m_committedCameraKickEnabled;
        RefreshCombatGeneralControls();
    }

    private void BuildPlaceholderPage(Transform parent, string title, string description, float left = 145f)
    {
        TextMeshProUGUI titleText = CreateTopLeftText(
            $"{title} Placeholder Title",
            parent,
            new Vector2(left, -72f),
            new Vector2(760f, 48f),
            title,
            32f,
            TextAlignmentOptions.Left);
        titleText.font = m_boldFont != null ? m_boldFont : m_regularFont;

        TextMeshProUGUI descriptionText = CreateTopLeftText(
            $"{title} Placeholder Description",
            parent,
            new Vector2(left, -132f),
            new Vector2(900f, 36f),
            description,
            18f,
            TextAlignmentOptions.Left);
        descriptionText.color = new Color(1f, 1f, 1f, 0.55f);
    }

    private void SelectCombatSubTab(int selectedIndex)
    {
        m_selectedCombatSubTab = Mathf.Clamp(selectedIndex, 0, m_combatSubPages.Length - 1);
        for (int i = 0; i < m_combatSubPages.Length; i++)
        {
            bool selected = i == m_selectedCombatSubTab;
            if (m_combatSubTabBackgrounds[i] != null)
            {
                m_combatSubTabBackgrounds[i].color = selected
                    ? new Color(1f, 1f, 1f, 0.16f)
                    : Color.clear;
            }

            if (m_combatSubTabOutlines[i] != null)
            {
                m_combatSubTabOutlines[i].enabled = selected;
            }

            if (m_combatSubPages[i] != null)
            {
                m_combatSubPages[i].SetActive(selected);
            }
        }
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

    private void BindPauseSelection(Button button)
    {
        AddEventTrigger(button, EventTriggerType.PointerEnter, _ => SetPointerPauseSelection(button));
        AddEventTrigger(button, EventTriggerType.PointerExit, _ => ClearPointerPauseSelection(button));
        AddEventTrigger(button, EventTriggerType.Select, _ => SetKeyboardPauseSelection(button));
        AddEventTrigger(button, EventTriggerType.Deselect, _ => ClearKeyboardPauseSelection(button));
    }

    private void SetPointerPauseSelection(Button button)
    {
        m_pointerPauseButton = button;
        RefreshPauseSelection();
    }

    private void ClearPointerPauseSelection(Button button)
    {
        if (m_pointerPauseButton != button)
        {
            return;
        }

        m_pointerPauseButton = null;
        RefreshPauseSelection();
    }

    private void SetKeyboardPauseSelection(Button button)
    {
        m_selectedPauseButton = button;
        RefreshPauseSelection();
    }

    private void ClearKeyboardPauseSelection(Button button)
    {
        if (m_selectedPauseButton != button)
        {
            return;
        }

        m_selectedPauseButton = null;
        RefreshPauseSelection();
    }

    private void RefreshPauseSelection()
    {
        if (m_pauseSelectionArrow == null)
        {
            return;
        }

        Button visibleButton = m_pointerPauseButton;
        if (visibleButton == null && m_keyboardSelectionRequested)
        {
            visibleButton = m_selectedPauseButton;
        }

        if (visibleButton == null || !visibleButton.gameObject.activeInHierarchy)
        {
            m_pauseSelectionArrow.gameObject.SetActive(false);
            return;
        }

        RectTransform buttonRect = visibleButton.transform as RectTransform;
        RectTransform arrowRect = m_pauseSelectionArrow.rectTransform;
        if (buttonRect == null)
        {
            m_pauseSelectionArrow.gameObject.SetActive(false);
            return;
        }

        const float ArrowGap = 24f;
        arrowRect.anchoredPosition = new Vector2(
            buttonRect.anchoredPosition.x - arrowRect.rect.width - ArrowGap,
            buttonRect.anchoredPosition.y - ((buttonRect.rect.height - arrowRect.rect.height) * 0.5f));
        m_pauseSelectionArrow.gameObject.SetActive(true);
    }

    private void ResetPauseSelection()
    {
        m_pointerPauseButton = null;
        m_selectedPauseButton = null;
        m_keyboardSelectionRequested = false;
        if (m_pauseSelectionArrow != null)
        {
            m_pauseSelectionArrow.gameObject.SetActive(false);
        }
    }

    private void TrackPauseNavigationInput()
    {
        if (!m_isOpen || m_pausePage == null || !m_pausePage.activeSelf || m_keyboardSelectionRequested)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        bool keyboardNavigationPressed = keyboard != null
            && (keyboard.upArrowKey.wasPressedThisFrame
                || keyboard.downArrowKey.wasPressedThisFrame
                || keyboard.leftArrowKey.wasPressedThisFrame
                || keyboard.rightArrowKey.wasPressedThisFrame
                || keyboard.wKey.wasPressedThisFrame
                || keyboard.aKey.wasPressedThisFrame
                || keyboard.sKey.wasPressedThisFrame
                || keyboard.dKey.wasPressedThisFrame
                || keyboard.tabKey.wasPressedThisFrame);

        Gamepad gamepad = Gamepad.current;
        bool gamepadNavigationPressed = gamepad != null
            && (gamepad.dpad.up.wasPressedThisFrame
                || gamepad.dpad.down.wasPressedThisFrame
                || gamepad.dpad.left.wasPressedThisFrame
                || gamepad.dpad.right.wasPressedThisFrame);

        if (!keyboardNavigationPressed && !gamepadNavigationPressed)
        {
            return;
        }

        m_keyboardSelectionRequested = true;
        GameObject selectedObject = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject
            : null;
        if (selectedObject != null)
        {
            m_selectedPauseButton = selectedObject.GetComponent<Button>();
        }

        RefreshPauseSelection();
    }

    private static void AddEventTrigger(
        Button button,
        EventTriggerType eventType,
        UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        EventTrigger trigger = button.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = button.gameObject.AddComponent<EventTrigger>();
        }

        trigger.triggers ??= new List<EventTrigger.Entry>();
        EventTrigger.Entry entry = new() { eventID = eventType, callback = new EventTrigger.TriggerEvent() };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
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
