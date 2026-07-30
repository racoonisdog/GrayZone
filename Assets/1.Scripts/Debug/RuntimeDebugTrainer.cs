using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 테스트 씬 공용 런타임 디버그 트레이너입니다. 지정한 키로 TPS 조작에서 벗어나 커서로 조작하며,
/// 선택한 캐릭터의 체력/이동/무기 스탯을 조절하고, 좀비(적)를 좌표로 스폰할 수 있습니다.
/// </summary>
/// <remarks>
/// <para>
/// 이 컴포넌트가 들어 있는 공용 프리팹은 필드 씬 시작 시 <see cref="FieldSceneDataManager"/>가 생성하며,
/// 생성된 뒤 주변 런타임 컴포넌트를 자동 탐색합니다. 씬에 직접 배치해도 동작합니다.
/// 개발 모드 자체는 <see cref="GameManager"/>가 소유하고 이 트레이너는 <see cref="GameDevMode"/>를 읽기만 합니다.
/// 동작 여부는 <see cref="GameDevMode.DebugFeaturesEnabled"/>가 정합니다. Editor와 Development Build에서 살아 있고,
/// 정식 빌드에서는 개발 모드가 켜져 있어도 창이 열리지 않습니다.
/// IMGUI(OnGUI) 기반이라 별도의 uGUI 연결은 필요 없습니다.
/// </para>
/// <para>
/// 동작 범위는 필드 씬으로 한정합니다. 판정 기준은 <see cref="FieldManager"/>의 존재이며,
/// 셸터에는 별도의 트레이너가 있으므로 이쪽이 남의 씬 입력을 건드리지 않게 합니다.
/// </para>
/// <para>
/// 값 조절은 기존 컴포넌트의 공개 setter를 그대로 사용하고, 무한 체력/장탄수/예비탄약 같은 치트는
/// 각 컴포넌트가 이미 소유한 디버그 플래그(개발 모드에서만 효과)를 통해 켭니다.
/// </para>
/// </remarks>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class RuntimeDebugTrainer : MonoBehaviour
{
    private static RuntimeDebugTrainer s_instance;

    private bool m_isDuplicate;
    private IInputModeController m_sceneInputModeController;
    private bool m_usesSceneInputModeController;
    private InputMode m_inputModeBeforeTrainerOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    // ─────────────────────────────────────────────────────────────
    // 상태
    // ─────────────────────────────────────────────────────────────

    [Header("Activation")]
    [SerializeField, Tooltip("개발 모드와 독립적으로 트레이너 UI와 토글 입력만 활성화합니다.")]
    private bool m_trainerEnabled = true;

    [SerializeField, Tooltip("트레이너 창을 열고 닫을 키입니다. None이면 키 입력으로 열 수 없습니다.")]
    private Key m_toggleKey = Key.F9;

    /// <summary>개발 모드 상태를 바꾸지 않고 트레이너 UI와 입력만 켜거나 끕니다.</summary>
    public bool TrainerEnabled
    {
        get => m_trainerEnabled;
        set
        {
            m_trainerEnabled = value;
            if (!m_trainerEnabled && m_open)
            {
                SetMenuOpen(false);
            }
        }
    }

    /// <summary>트레이너 창을 열고 닫는 키입니다.</summary>
    public Key ToggleKey
    {
        get => m_toggleKey;
        set => m_toggleKey = value;
    }

    /// <summary>트레이너 패널(메뉴)이 열려 있는지 여부입니다. 열려 있으면 TPS 조작이 잠깁니다.</summary>
    private bool m_open;

    /// <summary>인스펙터 대상 인덱스입니다. -1이면 "현재 조작 중인 캐릭터"를 계속 따라갑니다.</summary>
    private int m_targetIndex = -1;

    private Vector2 m_scroll;

    private bool m_showWeaponHitscanLayers;
    private bool m_showAimTargetLayers;

    // IMGUI 런타임 창은 기본 리사이즈 핸들이 없으므로 우하단 그립으로 크기를 바꿉니다.
    private Rect m_windowRect = new Rect(10f, 10f, 500f, 720f);
    private Vector2 m_resizeStartMouse;
    private Vector2 m_resizeStartSize;
    private bool m_isResizing;

    private const float MinWindowWidth = 440f;
    private const float MinWindowHeight = 360f;
    private const float WindowMargin = 10f;
    private const float ResizeGripSize = 20f;

    /// <summary>창 테두리에서 크기 조절을 잡을 수 있는 두께입니다.</summary>
    private const float ResizeBorderSize = 6f;

    /// <summary>글자 배율 1을 적용할 기준 창 너비입니다. 기본 창 크기와 같게 두어 처음에는 배율이 1이 됩니다.</summary>
    private const float ReferenceWindowWidth = 500f;

    /// <summary>글자가 읽을 수 없을 만큼 작아지지 않게 하는 하한입니다.</summary>
    private const float MinUiScale = 0.75f;

    /// <summary>창을 넓혀도 글자가 지나치게 커지지 않게 하는 상한입니다.</summary>
    private const float MaxUiScale = 2.0f;

    /// <summary>지금 스타일에 반영된 배율입니다.</summary>
    private float m_appliedUiScale = -1f;

    /// <summary>배율을 적용한 스킨과 그 배율입니다.</summary>
    private GUISkin m_scaledSkin;
    private float m_scaledSkinScale = -1f;

    /// <summary>창을 그리기 직전의 전역 스킨입니다. 다 그린 뒤 되돌립니다.</summary>
    private GUISkin m_previousSkin;

    // 좀비 스폰 좌표 입력 버퍼입니다.
    private string m_spawnX = "0";
    private string m_spawnY = "0";
    private string m_spawnZ = "0";
    private int m_spawnCount = 1;

    // 스폰 원본(씬의 기존 좀비를 비활성 복제로 보관해 두어 원본이 죽어도 계속 스폰 가능).
    private GameObject m_enemyTemplate;
    private GameObject m_enemyTemplateHolder;

    private GUIStyle m_titleStyle;
    private GUIStyle m_headerStyle;

    private void Awake()
    {
        // 빌드에서 오브젝트를 지우지 않습니다.
        // 포함 여부는 GameDevMode.DebugFeaturesEnabled가 정하며, 그 조건에 Debug.isDebugBuild가 이미 들어 있어
        // Development Build에서는 살아 있고 정식 빌드에서는 꺼집니다.
        // 컴파일 단계에서 잘라내면 그 런타임 판단이 도달하지 못해, 개발자용 빌드에서도 트레이너를 쓸 수 없습니다.
        if (s_instance != null && s_instance != this)
        {
            m_isDuplicate = true;
            Destroy(gameObject);
            return;
        }

        s_instance = this;

        // 여기서 개발 모드를 보고 스스로 끄지 않습니다.
        // 개발 모드는 GameManager가 자기 Awake에서 켜는데, Awake 실행 순서는 보장되지 않습니다.
        // 트레이너가 먼저 깨면 아직 꺼져 있는 값을 보고 자신을 끄고, 그 뒤에 켜져도 다시 살아나지 않습니다.
        // 실제로 개발 모드가 켜져 있는데도 F9가 먹지 않는 상태가 이렇게 만들어졌습니다.
        //
        // Update가 매 프레임 개발 모드를 확인해 열려 있던 창을 닫고 입력을 무시하므로,
        // 컴포넌트를 켜 둔 채로도 꺼진 것과 같이 동작합니다.
    }

    private void OnEnable()
    {
        if (m_isDuplicate)
        {
            return;
        }

        if (s_instance != null && s_instance != this)
        {
            m_isDuplicate = true;
            Destroy(gameObject);
            return;
        }

        s_instance = this;
    }

    private void OnDisable()
    {
        if (m_open)
        {
            m_open = false;
            UnlockControls();
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }

        // 씬 전환으로 파괴될 때는 OnDisable의 복구가 이미 끝난 뒤이지만, 그 복구는 함께 파괴되는
        // FieldManager를 거치므로 실패할 수 있습니다. 커서만은 남기지 않도록 여기서 마지막으로 되돌립니다.
        // 커서 상태는 씬을 넘어 유지되는 전역 값이라, 열린 채로 전환하면 다음 씬이 잠긴 커서를 물려받습니다.
        RestoreCursorForSceneExit();

        // DontDestroyOnLoad로 남겨둔 스폰 원본 홀더를 함께 정리합니다.
        // 이 홀더는 씬을 넘어 살아남으므로, 트레이너가 사라질 때 반드시 같이 지워야 셸터로 따라가지 않습니다.
        if (m_enemyTemplateHolder != null)
        {
            Destroy(m_enemyTemplateHolder);
            m_enemyTemplateHolder = null;
            m_enemyTemplate = null;
        }

        m_sceneInputModeController = null;
        m_usesSceneInputModeController = false;
    }

    /// <summary>
    /// 트레이너가 사라질 때 커서를 게임플레이 기본값으로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// 다음 씬이 셸터처럼 커서를 쓰는 씬이면 그 씬의 UI가 다시 풀어 줍니다.
    /// 반대로 잠긴 채 넘어가면 커서를 되돌릴 주체가 없어 조작이 막히므로, 잠그는 쪽이 아니라 푸는 쪽으로 둡니다.
    /// </remarks>
    private void RestoreCursorForSceneExit()
    {
        if (!m_open)
        {
            return;
        }

        m_open = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }


    private void Update()
    {
        if (!m_trainerEnabled || !GameDevMode.DebugFeaturesEnabled || !IsFieldScene())
        {
            if (m_open)
            {
                SetMenuOpen(false);
            }
            return;
        }

        if (m_toggleKey != Key.None
            && Keyboard.current != null
            && Keyboard.current[m_toggleKey].wasPressedThisFrame)
        {
            SetMenuOpen(!m_open);
        }

        // 씬이 입력 모드 계약을 제공하면 그 구현이 커서·플레이어 입력을 소유합니다.
        // 계약이 없는 레거시/독립 테스트 씬만 아래 범용 안전장치를 사용합니다.
        if (m_open && !m_usesSceneInputModeController)
        {
            // 메뉴가 열려 있는 동안에는 커서를 항상 풀고, 조작 캐릭터의 입력이 다시 켜졌으면(캐릭터 전환 등) 다시 끕니다.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SuppressActivePlayerInput();
        }
    }

    /// <summary>지금 씬이 이 트레이너가 동작해도 되는 필드 씬인지 확인합니다.</summary>
    /// <remarks>
    /// 판정 기준은 <see cref="FieldManager"/>의 존재입니다. 씬 이름으로 판정하면 테스트 씬이 늘어날 때마다
    /// 목록을 고쳐야 하지만, 필드 씬이라면 반드시 이 컨트롤러를 두므로 존재 여부가 더 안정적인 기준입니다.
    /// 디버그 모드를 켠 빌드에서 필드 씬에 있다면 토글 키로 언제든 열 수 있고, 필드가 아니면 키를 받지 않습니다.
    /// </remarks>
    private bool IsFieldScene()
    {
        return TryResolveSceneInputModeController();
    }

    // ─────────────────────────────────────────────────────────────
    // 조작 잠금 / 해제
    // ─────────────────────────────────────────────────────────────

    private void SetMenuOpen(bool open)
    {
        if (m_open == open)
        {
            return;
        }

        m_open = open;

        if (m_open)
        {
            LockControls();
            PrefillSpawnCoordsFromPlayer();
        }
        else
        {
            UnlockControls();
        }
    }

    /// <summary>커서를 풀고 모든 활성 플레이어 입력을 비활성화해 TPS 조작에서 벗어납니다.</summary>
    private void LockControls()
    {
        m_usesSceneInputModeController = TryEnterSceneInputMode();
        if (m_usesSceneInputModeController)
        {
            return;
        }

        LockControlsFallback();
    }

    /// <summary>입력 모드 계약이 없는 테스트 씬을 위한 기존 범용 잠금 처리입니다.</summary>
    private void LockControlsFallback()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        foreach (PlayerInput playerInput in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
        {
            playerInput.GetComponent<PlayerInputs>()?.ResetInputState();

            if (playerInput.inputIsActive)
            {
                playerInput.DeactivateInput();
            }
        }

        // 셸터 카메라(CameraLook)가 있으면 회전 입력·재잠금 로직도 함께 잠급니다.
        foreach (CameraLook cameraLook in FindObjectsByType<CameraLook>(FindObjectsSortMode.None))
        {
            cameraLook.SetLookLocked(true);
        }
    }

    /// <summary>커서를 다시 잠그고 현재 조작 캐릭터의 입력을 복구해 TPS 조작으로 돌아갑니다.</summary>
    /// <remarks>
    /// 씬을 벗어나는 중이라면 범용 안전장치를 쓰지 않습니다. 그 처리는 커서를 잠그는데,
    /// 씬이 사라지는 시점에는 되돌려 줄 주체가 없어 다음 씬이 잠긴 커서를 물려받기 때문입니다.
    /// </remarks>
    private void UnlockControls()
    {
        if (m_usesSceneInputModeController && TryRestoreSceneInputMode())
        {
            m_usesSceneInputModeController = false;
            return;
        }

        m_usesSceneInputModeController = false;

        // 필드 씬이 이미 사라진 뒤라면 복구할 대상도 없습니다. 커서만 풀어 두고 끝냅니다.
        if (!IsFieldScene())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        UnlockControlsFallback();
    }

    /// <summary>입력 모드 계약이 없는 테스트 씬을 위한 기존 범용 복구 처리입니다.</summary>
    private void UnlockControlsFallback()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        SquadMemberController player = squadManager != null ? squadManager.PlayerSquadMember : null;
        if (player != null)
        {
            PlayerInput playerInput = player.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.ActivateInput();
                playerInput.SwitchCurrentActionMap("Player");
            }
        }

        foreach (CameraLook cameraLook in FindObjectsByType<CameraLook>(FindObjectsSortMode.None))
        {
            cameraLook.SetLookLocked(false);
        }
    }

    /// <summary>
    /// 씬별 입력 모드 컨트롤러를 한 번 찾고, 이후 F9 토글은 그 계약을 통해 처리합니다.
    /// </summary>
    /// <remarks>
    /// 반환 규약은 <see cref="IInputModeController.SetInputMode"/>와 같습니다.
    /// 정상 실패/비정상 실패면 디버그 트레이너의 범용 안전장치로 되돌아갑니다.
    /// </remarks>
    private bool TrySetSceneInputMode(InputMode mode)
    {
        if (!TryResolveSceneInputModeController())
        {
            return false;
        }

        int result = m_sceneInputModeController.SetInputMode(mode);
        if (result == 1)
        {
            return true;
        }

        Debug.LogWarning($"[RuntimeDebugTrainer] 씬 입력 모드 전환에 실패했습니다. mode={mode}, result={result}. 범용 안전장치로 처리합니다.", this);
        return false;
    }

    /// <summary>트레이너를 열기 전 씬 입력 모드를 기억하고 UI 모드를 요청합니다.</summary>
    private bool TryEnterSceneInputMode()
    {
        if (!TryResolveSceneInputModeController())
        {
            return false;
        }

        m_inputModeBeforeTrainerOpen = m_sceneInputModeController.CurrentInputMode;
        return m_inputModeBeforeTrainerOpen == InputMode.UI
               || TrySetSceneInputMode(InputMode.UI);
    }

    /// <summary>트레이너가 열기 전의 씬 입력 모드로 복구합니다.</summary>
    private bool TryRestoreSceneInputMode()
    {
        return TrySetSceneInputMode(m_inputModeBeforeTrainerOpen);
    }

    /// <summary>현재 씬의 <see cref="FieldManager"/>를 입력 모드 계약 구현체로 캐시합니다.</summary>
    /// <remarks>
    /// 이 트레이너는 필드 전용이므로 계약 구현체를 아무거나 받지 않고 <see cref="FieldManager"/>로 한정합니다.
    /// 셸터에는 <c>ShelterTrainerOverlay</c>가 따로 있어, 임의의 구현체를 잡으면 남의 씬 입력을 건드리게 됩니다.
    /// 캐시는 파괴된 오브젝트를 계속 붙잡지 않도록 Unity의 null 판정으로 매번 확인합니다.
    /// 씬이 바뀌면 이전 씬의 FieldManager가 파괴되므로 여기서 자연히 재탐색이 일어납니다.
    /// </remarks>
    private bool TryResolveSceneInputModeController()
    {
        // MonoBehaviour의 == null은 파괴된 오브젝트도 true를 주므로, 씬 전환으로 죽은 캐시가 걸러집니다.
        if (m_sceneInputModeController is MonoBehaviour cached && cached != null)
        {
            return true;
        }

        m_sceneInputModeController = null;

        FieldManager fieldManager = FieldManager.Instance;
        if (fieldManager == null)
        {
            return false;
        }

        m_sceneInputModeController = fieldManager;
        return true;
    }

    /// <summary>메뉴가 열린 동안 현재 조작 캐릭터의 입력이 다시 활성화됐으면(전환 등) 즉시 비활성화합니다.</summary>
    private void SuppressActivePlayerInput()
    {
        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        SquadMemberController player = squadManager != null ? squadManager.PlayerSquadMember : null;
        if (player == null)
        {
            return;
        }

        PlayerInput playerInput = player.GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.inputIsActive)
        {
            player.GetComponent<PlayerInputs>()?.ResetInputState();
            playerInput.DeactivateInput();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!m_trainerEnabled || !GameDevMode.DebugFeaturesEnabled)
        {
            return;
        }

        EnsureStyles();

        if (!m_open)
        {
            GUI.Label(new Rect(10, 10, 600, 24), $"{m_toggleKey}: 런타임 디버그 트레이너 열기 / 닫기", m_headerStyle);
            return;
        }

        ClampWindowRectToScreen();

        try
        {
            Rect movedWindow = GUI.Window(GetInstanceID(), m_windowRect, DrawWindow, "런타임 디버그 트레이너");
            m_windowRect.position = movedWindow.position;
        }
        finally
        {
            // 전역 스킨은 반드시 되돌립니다. 남겨 두면 다른 IMGUI 창이 이 배율을 물려받습니다.
            RestoreSkin();
        }
    }

    private void DrawWindow(int windowId)
    {
        GUILayout.Label($"{m_toggleKey} 또는 아래 버튼으로 게임플레이 복귀. (개발 모드에서만 표시)");

        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        if (squadManager == null)
        {
            GUILayout.Label("SquadManager를 찾을 수 없습니다.");
            if (GUILayout.Button("닫기(게임플레이 복귀)"))
            {
                SetMenuOpen(false);
            }
            DrawWindowFooter();
            DrawWindowChrome(windowId);
            return;
        }

        m_scroll = GUILayout.BeginScrollView(m_scroll);

        DrawTargetPicker(squadManager);
        GUILayout.Space(6);

        PlayerbleUnitData target = ResolveTarget(squadManager);
        if (target == null)
        {
            GUILayout.Label("대상 캐릭터 데이터를 찾을 수 없습니다.");
        }
        else
        {
            DrawCharacterSection(target);
            GUILayout.Space(6);
            DrawWeaponSection(target);
            GUILayout.Space(6);
            DrawCombatFeedbackSection(target);
        }

        GUILayout.Space(6);
        DrawEnemySpawnSection();

        GUILayout.Space(6);
        DrawFieldControlSection();

        GUILayout.EndScrollView();

        DrawWindowFooter();
        DrawWindowChrome(windowId);
    }

    private void DrawWindowFooter()
    {
        GUILayout.Space(4);
        if (GUILayout.Button("닫기 (게임플레이 복귀)", GUILayout.Height(28)))
        {
            SetMenuOpen(false);
        }

        string exitLabel = Application.isEditor ? "Play Mode 종료" : "빌드 종료";
        if (GUILayout.Button(exitLabel, GUILayout.Height(26)))
        {
            ExitPlayTest();
        }
    }

    private static void ExitPlayTest()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void DrawWindowChrome(int windowId)
    {
        HandleWindowResize();
        // 좌우 위 테두리는 크기 조절이 먼저 잡아야 하므로 그만큼 안쪽에서 시작합니다.
        GUI.DragWindow(new Rect(
            ResizeBorderSize,
            ResizeBorderSize,
            m_windowRect.width - ResizeBorderSize * 2f - ResizeGripSize,
            24f));
    }

    /// <summary>어느 테두리를 잡고 있는지입니다.</summary>
    /// <remarks>가로와 세로를 따로 두어 모서리를 잡으면 두 축이 함께 움직입니다.</remarks>
    private int m_resizeEdgeX;
    private int m_resizeEdgeY;

    /// <summary>크기 조절을 시작한 시점의 창 위치와 크기입니다.</summary>
    private Rect m_resizeStartRect;

    /// <summary>
    /// 창 테두리를 잡아 크기를 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 일반 창처럼 네 변과 네 모서리 어디를 잡아도 조절됩니다.
    /// 왼쪽이나 위쪽을 잡으면 반대쪽 변이 제자리에 남아야 하므로 위치도 함께 옮깁니다.
    ///
    /// 마우스 좌표는 창 안쪽 기준이라 창을 옮기는 도중에도 값이 흔들리지 않도록
    /// 시작 시점의 화면 좌표를 따로 기억해 두고 그 차이로 계산합니다.
    /// </remarks>
    private void HandleWindowResize()
    {
        Rect inner = new Rect(0f, 0f, m_windowRect.width, m_windowRect.height);

        // 테두리 판정 영역. 제목 표시줄은 창을 옮기는 데 쓰므로 위쪽만 조금 안쪽에서 시작합니다.
        bool onLeft = Event.current.mousePosition.x <= ResizeBorderSize;
        bool onRight = Event.current.mousePosition.x >= inner.width - ResizeBorderSize;
        bool onTop = Event.current.mousePosition.y <= ResizeBorderSize;
        bool onBottom = Event.current.mousePosition.y >= inner.height - ResizeBorderSize;
        bool onEdge = (onLeft || onRight || onTop || onBottom) && inner.Contains(Event.current.mousePosition);

        // 잡을 수 있는 곳임을 알 수 있도록 모서리에 표시를 남깁니다.
        GUI.Label(new Rect(inner.width - ResizeGripSize, inner.height - ResizeGripSize,
                           ResizeGripSize, ResizeGripSize), "↘");

        Event currentEvent = Event.current;
        switch (currentEvent.type)
        {
            case EventType.MouseDown when onEdge:
                m_isResizing = true;
                m_resizeEdgeX = onLeft ? -1 : (onRight ? 1 : 0);
                m_resizeEdgeY = onTop ? -1 : (onBottom ? 1 : 0);
                m_resizeStartMouse = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
                m_resizeStartRect = m_windowRect;
                currentEvent.Use();
                break;

            case EventType.MouseDrag when m_isResizing:
                Vector2 delta = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition) - m_resizeStartMouse;
                ApplyResize(delta);
                GUI.changed = true;
                currentEvent.Use();
                break;

            case EventType.MouseUp when m_isResizing:
                m_isResizing = false;
                m_resizeEdgeX = 0;
                m_resizeEdgeY = 0;
                currentEvent.Use();
                break;
        }
    }

    /// <summary>잡은 테두리에 따라 창의 위치와 크기를 갱신합니다.</summary>
    /// <param name="delta">크기 조절을 시작한 지점에서 마우스가 움직인 거리입니다.</param>
    private void ApplyResize(Vector2 delta)
    {
        Rect r = m_resizeStartRect;

        if (m_resizeEdgeX > 0)
        {
            float maxWidth = Mathf.Max(MinWindowWidth, Screen.width - r.x - WindowMargin);
            m_windowRect.width = Mathf.Clamp(r.width + delta.x, MinWindowWidth, maxWidth);
        }
        else if (m_resizeEdgeX < 0)
        {
            // 오른쪽 변을 제자리에 두고 왼쪽만 움직입니다.
            float right = r.x + r.width;
            float maxWidth = Mathf.Max(MinWindowWidth, right - WindowMargin);
            m_windowRect.width = Mathf.Clamp(r.width - delta.x, MinWindowWidth, maxWidth);
            m_windowRect.x = right - m_windowRect.width;
        }

        if (m_resizeEdgeY > 0)
        {
            float maxHeight = Mathf.Max(MinWindowHeight, Screen.height - r.y - WindowMargin);
            m_windowRect.height = Mathf.Clamp(r.height + delta.y, MinWindowHeight, maxHeight);
        }
        else if (m_resizeEdgeY < 0)
        {
            // 아래쪽 변을 제자리에 두고 위쪽만 움직입니다.
            float bottom = r.y + r.height;
            float maxHeight = Mathf.Max(MinWindowHeight, bottom - WindowMargin);
            m_windowRect.height = Mathf.Clamp(r.height - delta.y, MinWindowHeight, maxHeight);
            m_windowRect.y = bottom - m_windowRect.height;
        }
    }

    private void ClampWindowRectToScreen()
    {
        float maxWidth = Mathf.Max(MinWindowWidth, Screen.width - WindowMargin * 2f);
        float maxHeight = Mathf.Max(MinWindowHeight, Screen.height - WindowMargin * 2f);
        m_windowRect.width = Mathf.Clamp(m_windowRect.width, MinWindowWidth, maxWidth);
        m_windowRect.height = Mathf.Clamp(m_windowRect.height, MinWindowHeight, maxHeight);
        m_windowRect.x = Mathf.Clamp(m_windowRect.x, WindowMargin - m_windowRect.width, Screen.width - WindowMargin);
        m_windowRect.y = Mathf.Clamp(m_windowRect.y, WindowMargin, Screen.height - WindowMargin);
    }

    private void DrawTargetPicker(SquadManager squadManager)
    {
        GUILayout.Label("■ 캐릭터 선택 (인스펙터 대상)", m_headerStyle);

        IReadOnlyList<SquadMemberController> members = squadManager.SquadMembers;

        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(m_targetIndex < 0, "자동(조작중)", "Button") && m_targetIndex >= 0)
        {
            m_targetIndex = -1;
        }

        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null)
            {
                continue;
            }

            string label = member.MemberName
                + (member.IsPlayerSquadMember ? " (조작중)" : string.Empty)
                + (!member.IsAlive ? " (사망)" : member.IsDown ? " (다운)" : string.Empty);

            bool selected = m_targetIndex == i;
            if (GUILayout.Toggle(selected, label, "Button") && !selected)
            {
                m_targetIndex = i;
            }
        }
        GUILayout.EndHorizontal();

        // 선택한 캐릭터를 실제로 조작 대상으로 전환.
        if (m_targetIndex >= 0 && m_targetIndex < members.Count && GUILayout.Button("선택한 캐릭터 조작하기"))
        {
            squadManager.SwitchToMember(m_targetIndex);
        }
    }

    private PlayerbleUnitData ResolveTarget(SquadManager squadManager)
    {
        IReadOnlyList<SquadMemberController> members = squadManager.SquadMembers;
        if (m_targetIndex < 0 || m_targetIndex >= members.Count)
        {
            return squadManager.PlayerSquadMemberData;
        }

        SquadMemberController member = members[m_targetIndex];
        return member != null ? member.GetComponent<PlayerbleUnitData>() : null;
    }

    // ─────────────────────────────────────────────────────────────
    // 캐릭터 섹션
    // ─────────────────────────────────────────────────────────────

    private void DrawCharacterSection(PlayerbleUnitData target)
    {
        GUILayout.Label($"■ 캐릭터: {target.DisplayName}", m_headerStyle);

        PlayerHealth health = target.GetComponent<PlayerHealth>();
        if (health != null)
        {
            GUILayout.Label($"HP {health.CurrentHP} / {health.MaxHP}  " +
                $"(다운={health.IsDowned}, 사망={health.IsDead}, 부활 {health.ReviveCount}/{health.MaxReviveCount})");

            target.DebugInfiniteHealth = GUILayout.Toggle(target.DebugInfiniteHealth, " 무한 체력(무적)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("체력 풀회복"))
            {
                health.SetCurrentHP(health.MaxHP);
            }
            if (GUILayout.Button("즉시 기절"))
            {
                health.Debug_InstantDown();
            }
            if (GUILayout.Button("전투 이탈"))
            {
                health.Debug_InstantCombatOut();
            }
            if (GUILayout.Button("즉시 부활"))
            {
                health.Debug_InstantRevive();
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label("PlayerHealth를 찾을 수 없습니다.");
        }

        ThirdPersonController controller = target.GetComponent<ThirdPersonController>();
        if (controller != null)
        {
            controller.SetMoveSpeed(SliderRow("이동 속도", controller.MoveSpeed, 0f, 15f));
            controller.SetSprintSpeed(SliderRow("질주 속도", controller.SprintSpeed, 0f, 20f));
            controller.SetJumpHeight(SliderRow("점프 높이", controller.JumpHeight, 0f, 5f));
        }
        else
        {
            GUILayout.Label("ThirdPersonController를 찾을 수 없습니다(이동 스탯 조절 불가).");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 무기 섹션
    // ─────────────────────────────────────────────────────────────

    private void DrawWeaponSection(PlayerbleUnitData target)
    {
        GUILayout.Label($"■ 무기: {target.CurrentWeaponName}", m_headerStyle);

        Gun weapon = target.Gun;
        if (weapon == null)
        {
            GUILayout.Label("Gun를 찾을 수 없습니다.");
            return;
        }

        GUILayout.Label($"장전 {weapon.CurrentBullet} / {weapon.MaxBullet},  예비 {target.ReserveAmmo} / {target.MaxReserveAmmo}");

        GUILayout.BeginHorizontal();
        target.DebugInfiniteMagazine = GUILayout.Toggle(target.DebugInfiniteMagazine, " 무한 장탄수");
        target.DebugInfiniteReserveAmmo = GUILayout.Toggle(target.DebugInfiniteReserveAmmo, " 무한 예비탄");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("탄창 가득"))
        {
            weapon.SetCurrentBullet(weapon.MaxBullet);
        }
        if (GUILayout.Button("예비탄 가득"))
        {
            target.SetReserveAmmo(target.MaxReserveAmmo);
        }
        GUILayout.EndHorizontal();

        weapon.SetMaxBullet(Mathf.RoundToInt(SliderRow("탄창 용량", weapon.MaxBullet, 1f, 200f, "0")));
        weapon.SetCurrentBullet(Mathf.RoundToInt(SliderRow("현재 탄약", weapon.CurrentBullet, 0f, weapon.MaxBullet, "0")));
        target.SetMaxReserveAmmo(Mathf.RoundToInt(SliderRow("예비탄 최대", target.MaxReserveAmmo, 0f, 999f, "0")));
        weapon.SetAllowFullMagReload(GUILayout.Toggle(weapon.AllowFullMagReload, " 풀 탄창 재장전 허용"));

        weapon.SetHitscanDamage(Mathf.RoundToInt(SliderRow("데미지", weapon.HitscanDamage, 0f, 100f, "0")));
        weapon.SetHeadshotDamageMultiplier(SliderRow("헤드샷 배율", weapon.HeadshotDamageMultiplier, 1f, 5f));
        weapon.SetShootDelay(SliderRow("사격 딜레이(초)", weapon.ShootDelay, 0.02f, 1f));
        weapon.SetReloadTime(SliderRow("재장전(초)", weapon.ReloadTime, 0f, 5f));
        weapon.SetHitscanRange(SliderRow("사거리", weapon.HitscanRange, 10f, 300f, "0"));
        weapon.SetHitscanLayerMask(DrawLayerMaskRow("무기 명중 레이어", weapon.HitscanLayerMask, ref m_showWeaponHitscanLayers));

        weapon.SetRecoilPitchKick(SliderRow("반동(상하)", weapon.RecoilPitchKick, 0f, 5f));
        weapon.SetRecoilYawKick(SliderRow("반동(좌우)", weapon.RecoilYawKick, 0f, 5f));
        weapon.SetYawKickPattern(DrawKickSidePattern("좌우 반동 패턴", weapon.YawKickPattern));
        weapon.SetRecoilRoll(SliderRow("카메라 롤 킥", weapon.RecoilRoll, 0f, 5f));
        weapon.SetRollKickPattern(DrawKickSidePattern("카메라 롤 패턴", weapon.RollKickPattern));
        weapon.SetRecoilFovPunch(SliderRow("카메라 FOV 펀치", weapon.RecoilFovPunch, 0f, 10f));

        float hipMin = SliderRow("힙 탄퍼짐 최소", weapon.HipfireMinSpread, 0f, 20f);
        float hipMax = SliderRow("힙 탄퍼짐 최대", weapon.HipfireMaxSpread, 0f, 20f);
        weapon.SetHipfireSpread(hipMin, hipMax);

        float adsMin = SliderRow("ADS 탄퍼짐 최소", weapon.AdsMinSpread, 0f, 20f);
        float adsMax = SliderRow("ADS 탄퍼짐 최대", weapon.AdsMaxSpread, 0f, 20f);
        weapon.SetAdsSpread(adsMin, adsMax);

        DrawBurstSpreadSection(weapon);
    }

    private void DrawBurstSpreadSection(Gun weapon)
    {
        GUILayout.Space(3);
        GUILayout.Label("연사 탄퍼짐 (실제 탄착)", m_headerStyle);
        GUILayout.Label($"현재 힙 {weapon.GetCurrentSpread(false):0.##}° / ADS {weapon.GetCurrentSpread(true):0.##}°");

        weapon.SetHipfireBurstSpread(
            Mathf.RoundToInt(SliderRow("힙 정확 발수", weapon.HipfireMinSpreadShotCount, 0f, 20f, "0")),
            SliderRow("힙 발당 증가", weapon.HipfireSpreadIncreasePerShot, 0f, 10f),
            SliderRow("힙 회복 지연", weapon.HipfireSpreadRecoveryDelay, 0f, 3f),
            SliderRow("힙 초당 회복", weapon.HipfireSpreadRecoveryPerSecond, 0f, 30f));

        weapon.SetAdsBurstSpread(
            Mathf.RoundToInt(SliderRow("ADS 정확 발수", weapon.AdsMinSpreadShotCount, 0f, 20f, "0")),
            SliderRow("ADS 발당 증가", weapon.AdsSpreadIncreasePerShot, 0f, 10f),
            SliderRow("ADS 회복 지연", weapon.AdsSpreadRecoveryDelay, 0f, 3f),
            SliderRow("ADS 초당 회복", weapon.AdsSpreadRecoveryPerSecond, 0f, 30f));

        SpreadDistribution distribution =
            (SpreadDistribution)GUILayout.SelectionGrid(
                (int)weapon.Distribution,
                new[] { "균일 분포", "중심 집중" },
                2);
        weapon.SetSpreadDistribution(distribution);
        weapon.SetSpreadConcentration(SliderRow("중심 집중도", weapon.SpreadConcentration, 1f, 10f));
    }

    private void DrawCombatFeedbackSection(PlayerbleUnitData target)
    {
        AimController aimController = target.GetComponent<AimController>();
        if (aimController == null)
        {
            GUILayout.Label("AimController를 찾을 수 없습니다. 반동/카메라 킥/임팩트 마커 조절은 사용할 수 없습니다.");
            return;
        }

        GUILayout.Label("사격 피드백", m_headerStyle);

        DrawAimHandlingSection(aimController);
        DrawLogicalRecoilSection(target);

        aimController.SetAimRecoilEnabled(
            GUILayout.Toggle(aimController.AimRecoilEnabled, " 반동 적용 (탄착 영향)"));
        aimController.SetVisualKickEnabled(
            GUILayout.Toggle(aimController.VisualKickEnabled, " 카메라 킥 적용 (롤/FOV, 탄착 무영향)"));

        AimController.VisualKickRecoveryMode selectedRecoveryMode =
            (AimController.VisualKickRecoveryMode)GUILayout.SelectionGrid(
                (int)aimController.CurrentVisualKickRecoveryMode,
                new[] { "반동 회복 공유", "발당 리셋" },
                2);
        aimController.SetVisualKickRecoveryMode(selectedRecoveryMode);
        aimController.SetVisualKickRecoverShots(
            SliderRow("카메라 킥 회복 발수", aimController.VisualKickRecoverShots, 0.1f, 10f));
        aimController.SetVisualKickMaxRoll(
            SliderRow("카메라 롤 상한", aimController.VisualKickMaxRoll, 0f, 15f));
        aimController.SetVisualKickMaxFovPunch(
            SliderRow("FOV 펀치 상한", aimController.VisualKickMaxFovPunch, 0f, 20f));

        GUILayout.Space(3);
        aimController.SetImpactMarkerEnabled(
            GUILayout.Toggle(aimController.ImpactMarkerEnabled, " 임팩트 마커 생성"));
        if (aimController.ImpactMarkerEnabled)
        {
            GUILayout.Label("마커 프리팹이 없으면 작은 마젠타 스피어를 임시 생성합니다.");
            aimController.SetImpactMarkerLifetime(
                SliderRow("마커 유지 시간", aimController.ImpactMarkerLifetime, 0.05f, 10f));
            aimController.SetImpactMarkerSize(
                SliderRow("마커 크기", aimController.ImpactMarkerSize, 0.01f, 2f));
        }

        DrawAimDebugSection(aimController);
        DrawCrosshairFeedbackSection(aimController.CrosshairController);
    }

    private void DrawAimHandlingSection(AimController aimController)
    {
        GUILayout.Label("조준 / 히트스캔", m_headerStyle);
        aimController.SetTargetLayer(DrawLayerMaskRow("조준점 레이어", aimController.TargetLayer, ref m_showAimTargetLayers));
        aimController.SetHipfireHoldDuration(SliderRow("힙파이어 자세 유지", aimController.HipfireHoldDuration, 0f, 5f));
        aimController.SetAdsFov(SliderRow("ADS FOV", aimController.AdsFov, 1f, 90f));
        aimController.SetHipfireFov(SliderRow("힙파이어 FOV", aimController.HipfireFov, 1f, 120f));
        aimController.SetZoomLerpSpeed(SliderRow("FOV 전환 속도", aimController.ZoomLerpSpeed, 0f, 40f));
    }

    private void DrawAimDebugSection(AimController aimController)
    {
        GUILayout.Space(3);
        GUILayout.Label("조준 디버그 (에디터 Gizmo)", m_headerStyle);
        aimController.SetShowAimImageAlways(
            GUILayout.Toggle(aimController.ShowAimImageAlways, " 조준선을 항상 표시"));
        aimController.SetLookDistance(SliderRow("지향점 거리", aimController.LookDistance, 0f, 500f, "0"));
        aimController.SetHitscanBlockMarkerOffset(
            SliderRow("장애물 마커 오프셋", aimController.HitscanBlockMarkerOffset, 0f, 1f));
        aimController.SetDrawHitscanDebugRay(
            GUILayout.Toggle(aimController.HitscanDebugRayEnabled, " 총구→탄착점 레이"));
        aimController.SetDrawAimTraceLine(
            GUILayout.Toggle(aimController.DrawAimTraceLine, " 카메라→조준점 레이"));
        aimController.SetDrawCameraForwardRay(
            GUILayout.Toggle(aimController.DrawCameraForwardRay, " 논리 조준/카메라 비교 레이"));
        aimController.SetDrawLookPointSphere(
            GUILayout.Toggle(aimController.DrawLookPointSphere, " 지향점 스피어"));
        aimController.SetDrawAimPointSphere(
            GUILayout.Toggle(aimController.DrawAimPointSphere, " 카메라 조준점 스피어"));
        aimController.SetDrawImpactPointSphere(
            GUILayout.Toggle(aimController.DrawImpactPointSphere, " 총구 탄착점 스피어"));

        if (aimController.DrawLookPointSphere || aimController.DrawAimPointSphere || aimController.DrawImpactPointSphere)
        {
            aimController.SetDebugSphereRadius(
                SliderRow("디버그 스피어 반지름", aimController.DebugSphereRadius, 0.01f, 3f));
        }
    }

    private void DrawLogicalRecoilSection(PlayerbleUnitData target)
    {
        ThirdPersonController controller = target.GetComponent<ThirdPersonController>();
        if (controller == null)
        {
            return;
        }

        GUILayout.Space(3);
        GUILayout.Label("실제 조준 반동 (탄착 영향)", m_headerStyle);
        controller.SetRecoilRecoverySpeed(SliderRow("반동 회복 속도", controller.RecoilRecoverySpeed, 0f, 40f));
        controller.SetRecoilRecoveryDelay(SliderRow("반동 회복 지연", controller.RecoilRecoveryDelay, 0f, 2f));
        controller.SetRecoilOnsetInterpolationEnabled(
            GUILayout.Toggle(controller.RecoilOnsetInterpolationEnabled, " 반동 온셋 보간"));
        if (controller.RecoilOnsetInterpolationEnabled)
        {
            controller.SetRecoilOnsetSpeed(SliderRow("온셋 보간 속도", controller.RecoilOnsetSpeed, 0f, 100f));
        }

        controller.SetRecoilCompensationAbsorbEnabled(
            GUILayout.Toggle(controller.RecoilCompensationAbsorbEnabled, " 되잡기 입력 우선 상쇄"));
        controller.SetPitchRecoveryRatio(SliderRow("피치 자동 회복 비율", controller.PitchRecoveryRatio, 0f, 1f));
        controller.SetYawRecoveryRatio(SliderRow("요 자동 회복 비율", controller.YawRecoveryRatio, 0f, 1f));
        controller.SetUsePitchOffsetCap(GUILayout.Toggle(controller.UsePitchOffsetCap, " 피치 반동 상한 사용"));
        if (controller.UsePitchOffsetCap)
        {
            controller.SetRecoilMaxPitch(SliderRow("피치 반동 상한", controller.RecoilMaxPitch, 0f, 30f));
        }

        controller.SetUseYawOffsetCap(GUILayout.Toggle(controller.UseYawOffsetCap, " 요 반동 상한 사용"));
        if (controller.UseYawOffsetCap)
        {
            controller.SetRecoilMaxYaw(SliderRow("요 반동 상한", controller.RecoilMaxYaw, 0f, 30f));
        }

        GUILayout.Label($"현재 반동 오프셋: Pitch {controller.CurrentRecoilPitchOffset:0.##} / Yaw {controller.CurrentRecoilYawOffset:0.##}");
        if (GUILayout.Button("현재 자동 반동 오프셋 제거"))
        {
            controller.ClearRecoilOffsets();
        }
    }

    private void DrawCrosshairFeedbackSection(CrosshairController crosshairController)
    {
        if (crosshairController == null)
        {
            return;
        }

        GUILayout.Space(3);
        GUILayout.Label("크로스헤어 피드백 (표시 전용)", m_headerStyle);
        crosshairController.SetSpreadAccuracyEnabled(
            GUILayout.Toggle(crosshairController.UseSpreadAccuracy, " 탄퍼짐 간격 표시"));
        crosshairController.SetSpreadDisplayBasis(
            (CrosshairController.SpreadDisplayBasis)GUILayout.SelectionGrid(
                (int)crosshairController.CurrentSpreadDisplayBasis,
                new[] { "콘 경계", "대부분", "일반", "코어" },
                2));
        crosshairController.SetClampToMaxGap(
            GUILayout.Toggle(crosshairController.ClampToMaxGap, " 최대 간격 제한"));
        if (crosshairController.ClampToMaxGap)
        {
            crosshairController.SetMaxGapPixels(SliderRow("최대 간격(px)", crosshairController.MaxGapPixels, 0f, 600f, "0"));
        }

        crosshairController.SetSpreadLerpSpeed(SliderRow("간격 보간 속도", crosshairController.SpreadLerpSpeed, 0f, 50f));
    }

    private KickSidePattern DrawKickSidePattern(string label, KickSidePattern current)
    {
        GUILayout.Label(label);
        return (KickSidePattern)GUILayout.SelectionGrid(
            (int)current,
            new[] { "무작위", "좌→우", "우→좌" },
            3);
    }

    private LayerMask DrawLayerMaskRow(string label, LayerMask value, ref bool expanded)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {GetLayerMaskSummary(value)}", GUILayout.ExpandWidth(true));
        if (GUILayout.Button(expanded ? "접기" : "레이어 선택", GUILayout.Width(94f)))
        {
            expanded = !expanded;
        }
        GUILayout.EndHorizontal();

        if (!expanded)
        {
            return value;
        }

        int mask = value.value;
        for (int layer = 0; layer < 32; layer++)
        {
            string layerName = LayerMask.LayerToName(layer);
            if (string.IsNullOrEmpty(layerName))
            {
                continue;
            }

            int bit = 1 << layer;
            bool included = (mask & bit) != 0;
            bool requested = GUILayout.Toggle(included, $"  {layerName}");
            if (requested != included)
            {
                mask = requested ? mask | bit : mask & ~bit;
            }
        }

        value.value = mask;
        return value;
    }

    private static string GetLayerMaskSummary(LayerMask value)
    {
        if (value.value == 0)
        {
            return "Nothing";
        }

        if (value.value == ~0)
        {
            return "Everything";
        }

        List<string> names = new List<string>();
        for (int layer = 0; layer < 32; layer++)
        {
            if ((value.value & (1 << layer)) == 0)
            {
                continue;
            }

            string layerName = LayerMask.LayerToName(layer);
            names.Add(string.IsNullOrEmpty(layerName) ? $"Layer {layer}" : layerName);
        }

        return names.Count == 0 ? "Nothing" : string.Join(", ", names);
    }

    // ─────────────────────────────────────────────────────────────
    // 적(좀비) 스폰 섹션
    // ─────────────────────────────────────────────────────────────

    /// <summary>탈출 지점까지 가지 않고 필드를 끝내는 디버그 조작을 그립니다.</summary>
    /// <remarks>
    /// 정산과 결과 UI를 확인하는 데 쓰는 지름길입니다. 판정만 건너뛰고 이후 절차는
    /// <see cref="EscapeSystem.ForceEscape"/>를 통해 실제 탈출과 같은 경로를 타므로 결과가 달라지지 않습니다.
    /// </remarks>
    private void DrawFieldControlSection()
    {
        GUILayout.Label("■ 필드 제어", m_headerStyle);

        EscapeSystem escapeSystem = FindFirstObjectByType<EscapeSystem>(FindObjectsInactive.Include);
        FieldSceneDataManager fieldData = FieldSceneDataManager.Instance;

        if (escapeSystem == null)
        {
            GUILayout.Label("EscapeSystem을 찾을 수 없어 즉시 탈출을 쓸 수 없습니다.");
            return;
        }

        if (fieldData != null && fieldData.IsFinalized)
        {
            GUILayout.Label($"이미 정산이 끝났습니다. (처치 {fieldData.KillCount})");
            return;
        }

        GUILayout.Label(fieldData != null
            ? $"현재 처치 {fieldData.KillCount} / 임무 {(fieldData.MissionCompleted ? "달성" : "미달성")}"
            : "필드 데이터 매니저를 찾을 수 없습니다. 정산 없이 결과 UI만 열릴 수 있습니다.");

        if (GUILayout.Button("즉시 탈출 (정산 후 결과 UI)", GUILayout.Height(26)))
        {
            // 결과 UI가 입력을 가져가므로 트레이너를 먼저 닫습니다.
            // 열어 둔 채로 두면 트레이너와 결과 UI가 같은 커서를 두고 다툽니다.
            SetMenuOpen(false);
            escapeSystem.ForceEscape();
        }
    }

    private void DrawEnemySpawnSection()
    {
        int enemyCount = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length;
        GUILayout.Label($"■ 좀비 스폰 (현재 적 {enemyCount}마리)", m_headerStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("좌표", GUILayout.Width(40));
        GUILayout.Label("X", GUILayout.Width(14));
        m_spawnX = GUILayout.TextField(m_spawnX, GUILayout.Width(90));
        GUILayout.Label("Y", GUILayout.Width(14));
        m_spawnY = GUILayout.TextField(m_spawnY, GUILayout.Width(90));
        GUILayout.Label("Z", GUILayout.Width(14));
        m_spawnZ = GUILayout.TextField(m_spawnZ, GUILayout.Width(90));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"마리 수: {m_spawnCount}", GUILayout.Width(90));
        if (GUILayout.Button("-", GUILayout.Width(30)))
        {
            m_spawnCount = Mathf.Max(1, m_spawnCount - 1);
        }
        if (GUILayout.Button("+", GUILayout.Width(30)))
        {
            m_spawnCount = Mathf.Min(50, m_spawnCount + 1);
        }
        if (GUILayout.Button("플레이어 위치로 좌표 채우기"))
        {
            PrefillSpawnCoordsFromPlayer();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("좌표에 스폰"))
        {
            if (TryParseSpawnPosition(out Vector3 position))
            {
                SpawnEnemies(position, m_spawnCount);
            }
        }
        if (GUILayout.Button("플레이어 앞에 스폰"))
        {
            SpawnEnemies(GetPlayerFrontPosition(), m_spawnCount);
        }
        if (GUILayout.Button("모든 적 제거"))
        {
            KillAllEnemies();
        }
        GUILayout.EndHorizontal();
    }

    private bool TryParseSpawnPosition(out Vector3 position)
    {
        position = Vector3.zero;

        bool ok = float.TryParse(m_spawnX, out float x)
            & float.TryParse(m_spawnY, out float y)
            & float.TryParse(m_spawnZ, out float z);

        if (!ok)
        {
            Debug.LogWarning("[RuntimeDebugTrainer] 좌표 입력이 올바르지 않습니다.");
            return false;
        }

        position = new Vector3(x, y, z);
        return true;
    }

    private void PrefillSpawnCoordsFromPlayer()
    {
        Vector3 position = GetPlayerFrontPosition();
        m_spawnX = position.x.ToString("0.##");
        m_spawnY = position.y.ToString("0.##");
        m_spawnZ = position.z.ToString("0.##");
    }

    private Vector3 GetPlayerFrontPosition()
    {
        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        SquadMemberController player = squadManager != null ? squadManager.PlayerSquadMember : null;
        if (player == null)
        {
            return Vector3.zero;
        }

        Transform playerTransform = player.transform;
        return playerTransform.position + playerTransform.forward * 4f;
    }

    /// <summary>씬의 기존 좀비를 원본 삼아 지정 위치에 지정 마리 수만큼 복제 스폰합니다.</summary>
    private void SpawnEnemies(Vector3 origin, int count)
    {
        GameObject template = ResolveEnemyTemplate();
        if (template == null)
        {
            Debug.LogWarning("[RuntimeDebugTrainer] 스폰할 좀비 원본(EnemyController)을 씬에서 찾지 못했습니다.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            // 여러 마리는 겹치지 않도록 원 둘레로 살짝 흩뿌립니다(마리 수에 따라).
            Vector3 offset = count <= 1
                ? Vector3.zero
                : Quaternion.Euler(0f, 360f * i / count, 0f) * Vector3.forward * 1.5f;
            Vector3 spawnPosition = origin + offset;

            // NavMesh 위로 보정해 배회/추적이 정상 동작하도록 합니다.
            if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
            }

            GameObject spawned = Instantiate(template, spawnPosition, template.transform.rotation);
            spawned.name = "Enemy(Trainer)";
            spawned.SetActive(true);
        }
    }

    /// <summary>스폰 원본을 마련합니다. 씬의 기존 좀비를 비활성 복제로 보관해, 원본이 죽어도 계속 스폰할 수 있게 합니다.</summary>
    private GameObject ResolveEnemyTemplate()
    {
        if (m_enemyTemplate != null)
        {
            return m_enemyTemplate;
        }

        // 한 번 원본 확보를 시도했고 실패했더라도, 이후 씬에 좀비가 생겼을 수 있으니 매번 재시도합니다.
        EnemyController source = FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        if (source == null)
        {
            return null;
        }

        // 비활성 홀더의 자식으로 복제하면 원본 클론이 활성화되지 않아 Awake/OnEnable(필드 집계 등)이 실행되지 않습니다.
        // 스폰 시에는 부모 없이 다시 Instantiate하므로 그때 비로소 정상 활성화됩니다.
        if (m_enemyTemplateHolder == null)
        {
            m_enemyTemplateHolder = new GameObject("[TrainerEnemyTemplateHolder]");
            m_enemyTemplateHolder.SetActive(false);
            DontDestroyOnLoad(m_enemyTemplateHolder);
        }

        m_enemyTemplate = Instantiate(source.gameObject, m_enemyTemplateHolder.transform);
        m_enemyTemplate.name = "EnemyTemplate(Trainer)";
        return m_enemyTemplate;
    }

    private void KillAllEnemies()
    {
        foreach (EnemyHealth enemyHealth in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            enemyHealth.TakeDamage(int.MaxValue);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────

    private float SliderRow(string label, float value, float min, float max, string format = "0.##")
    {
        GUILayout.BeginHorizontal();
        float labelWidth = Mathf.Clamp(m_windowRect.width * 0.30f, 130f, 210f);
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(140f), GUILayout.ExpandWidth(true));
        GUILayout.Label(result.ToString(format), GUILayout.Width(60));
        GUILayout.EndHorizontal();
        return result;
    }

    private void EnsureStyles()
    {
        // 창 크기를 바꾸면 글자와 위젯도 같이 커지고 작아져야 인스펙터처럼 보입니다.
        // 기준 너비에서 얼마나 벗어났는지로 배율을 구하고, 읽을 수 없을 만큼 작아지지 않게 제한합니다.
        float scale = Mathf.Clamp(m_windowRect.width / ReferenceWindowWidth, MinUiScale, MaxUiScale);

        // 배율이 바뀌지 않았으면 스타일을 다시 만들지 않습니다. OnGUI는 프레임마다 여러 번 불립니다.
        if (m_titleStyle != null && Mathf.Approximately(scale, m_appliedUiScale))
        {
            ApplyScaledSkin(scale);
            return;
        }

        m_appliedUiScale = scale;

        m_titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(18f * scale),
            fontStyle = FontStyle.Bold,
        };

        m_headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(GUI.skin.label.fontSize <= 0 ? 12f * scale : GUI.skin.label.fontSize * scale),
            fontStyle = FontStyle.Bold,
        };

        ApplyScaledSkin(scale);
    }

    /// <summary>
    /// 이번 창에 쓸 기본 위젯 크기를 배율에 맞게 바꿉니다.
    /// </summary>
    /// <remarks>
    /// <see cref="GUI.skin"/>은 전역이라 창을 그리는 동안만 바꾸고 <see cref="RestoreSkin"/>으로 되돌립니다.
    /// 되돌리지 않으면 다른 IMGUI 창까지 이 배율을 물려받습니다.
    ///
    /// 글꼴 크기가 0인 스타일은 스킨 기본값을 쓰겠다는 뜻이라, 그런 경우에만 기준값을 곱해 넣습니다.
    /// 0이 아닌 값을 그대로 곱하면 매 프레임 누적되어 글자가 계속 커집니다.
    /// </remarks>
    private void ApplyScaledSkin(float scale)
    {
        if (m_scaledSkin == null || !Mathf.Approximately(scale, m_scaledSkinScale))
        {
            m_scaledSkin = Object.Instantiate(GUI.skin);
            m_scaledSkinScale = scale;

            int baseSize = Mathf.RoundToInt(12f * scale);

            foreach (GUIStyle style in m_scaledSkin.customStyles)
            {
                ScaleStyle(style, scale, baseSize);
            }

            ScaleStyle(m_scaledSkin.label, scale, baseSize);
            ScaleStyle(m_scaledSkin.button, scale, baseSize);
            ScaleStyle(m_scaledSkin.box, scale, baseSize);
            ScaleStyle(m_scaledSkin.textField, scale, baseSize);
            ScaleStyle(m_scaledSkin.toggle, scale, baseSize);
            ScaleStyle(m_scaledSkin.window, scale, baseSize);

            m_scaledSkin.horizontalSlider.fixedHeight = GUI.skin.horizontalSlider.fixedHeight * scale;
            m_scaledSkin.horizontalSliderThumb.fixedWidth = GUI.skin.horizontalSliderThumb.fixedWidth * scale;
            m_scaledSkin.horizontalSliderThumb.fixedHeight = GUI.skin.horizontalSliderThumb.fixedHeight * scale;
        }

        m_previousSkin = GUI.skin;
        GUI.skin = m_scaledSkin;

        static void ScaleStyle(GUIStyle style, float s, int baseSize)
        {
            if (style == null)
            {
                return;
            }

            style.fontSize = style.fontSize <= 0 ? baseSize : Mathf.RoundToInt(style.fontSize * s);

            if (style.fixedHeight > 0f)
            {
                style.fixedHeight *= s;
            }
        }
    }

    /// <summary>창을 다 그린 뒤 전역 스킨을 되돌립니다.</summary>
    private void RestoreSkin()
    {
        if (m_previousSkin != null)
        {
            GUI.skin = m_previousSkin;
            m_previousSkin = null;
        }
    }
}
