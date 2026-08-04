using System;
using UnityEngine;

/// <summary>
/// 필드 씬의 생명주기와 게임플레이 입력 모드 전환을 조율하는 씬 루트 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 데이터 스냅샷과 정산은 <see cref="FieldSceneDataManager"/>가 소유합니다.
/// 이 클래스는 결과 오버레이 진입 같은 씬 상태를 결정하고, 이후 활성 플레이어와 UI에 필요한 전환을 지시합니다.
/// 현재는 플레이어 입력·결과 UI 진입까지 연결되어 있으며, 결과 오버레이 동안의 시간 정지와 저장 완료 게이트는 후속 구현 대상입니다.
/// </remarks>
[RequireComponent(typeof(SurfaceFeedbackSystem))]
[RequireComponent(typeof(FieldAudioSystem))]
[RequireComponent(typeof(EnemyCorpseSettings))]
public class FieldManager : MonoBehaviour, IInputModeController
{
    private static FieldManager s_instance;

    [Tooltip("현재 직접 조작 중인 스쿼드 멤버를 제공하는 필드 스쿼드 매니저입니다. 비어 있으면 Awake에서 한 번 탐색합니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Tooltip("필드 공용 표면 피격 규칙과 Feedback 목록을 보관하는 같은 GameObject의 컴포넌트입니다.")]
    [SerializeField] private SurfaceFeedbackSystem m_surfaceFeedbackSystem;

    [Tooltip("필드 위치형 one-shot 사운드의 AudioSource 풀과 동시 발음 정책을 보관하는 같은 GameObject의 컴포넌트입니다.")]
    [SerializeField] private FieldAudioSystem m_audioFeedbackSystem;

    [Tooltip("필드의 모든 적 진영 유닛에 공통 적용할 시체 삭제와 래그돌 설정입니다.")]
    [SerializeField] private EnemyCorpseSettings m_enemyCorpseSettings;

    [Tooltip("스쿼드 전멸 시 표시할 게임오버 화면입니다. 비어 있으면 비활성 오브젝트까지 포함해 자동 탐색합니다.")]
    [SerializeField] private GameOverUIController m_gameOverUI;

    [Tooltip("게임오버 화면이 전멸 전에는 보이지 않도록 Awake에서 숨길지 여부입니다.")]
    [SerializeField] private bool m_hideGameOverUIOnAwake = true;

    private InputMode m_currentInputMode = InputMode.Gameplay;
    private FieldSceneDataManager m_subscribedFieldSceneDataManager;
    private SquadMemberController m_cachedPlayerSquadMember;
    private PlayerInputs m_cachedPlayerInputs;
    private ThirdPersonController m_cachedThirdPersonController;
    private AimController m_cachedAimController;

    /// <summary>현재 필드 씬의 인스턴스입니다. 필드 씬이 아니면 <c>null</c>입니다.</summary>
    /// <remarks>씬에 속하므로 씬 전환과 함께 사라집니다. 필드 밖에서는 존재하지 않는 것이 정상입니다.</remarks>
    public static FieldManager Instance => s_instance;

    /// <summary>현재 필드 씬의 표면 피드백 할당 데이터를 보관하는 컴포넌트입니다.</summary>
    public SurfaceFeedbackSystem SurfaceFeedback => m_surfaceFeedbackSystem != null
        ? m_surfaceFeedbackSystem
        : GetComponent<SurfaceFeedbackSystem>();

    /// <summary>피격·사망·표면 충돌처럼 월드 위치에 남는 필드 one-shot 사운드 출력 시스템입니다.</summary>
    public FieldAudioSystem AudioFeedback => m_audioFeedbackSystem != null
        ? m_audioFeedbackSystem
        : GetComponent<FieldAudioSystem>();

    /// <summary>현재 필드의 모든 적 진영 유닛이 사망 시 조회할 시체 처리 설정입니다.</summary>
    public EnemyCorpseSettings EnemyCorpseSettings => m_enemyCorpseSettings;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    private void Reset()
    {
        m_surfaceFeedbackSystem = GetComponent<SurfaceFeedbackSystem>();
        m_audioFeedbackSystem = GetComponent<FieldAudioSystem>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (m_surfaceFeedbackSystem == null)
        {
            m_surfaceFeedbackSystem = GetComponent<SurfaceFeedbackSystem>();
        }

        if (m_audioFeedbackSystem == null)
        {
            m_audioFeedbackSystem = GetComponent<FieldAudioSystem>();
        }
    }
#endif

    private void Awake()
    {
        // 씬에 미리 배치된 것이 있으면 그쪽이 먼저 자리를 잡고, 뒤에 생긴 쪽이 물러납니다.
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;

        if (m_surfaceFeedbackSystem == null)
        {
            m_surfaceFeedbackSystem = GetComponent<SurfaceFeedbackSystem>();
        }

        if (m_audioFeedbackSystem == null)
        {
            m_audioFeedbackSystem = GetComponent<FieldAudioSystem>();
        }

        // 기존 씬/프리팹이 새 컴포넌트를 아직 저장하지 않았어도 런타임 피드백이 끊기지 않게 보강합니다.
        if (m_audioFeedbackSystem == null)
        {
            m_audioFeedbackSystem = gameObject.AddComponent<FieldAudioSystem>();
        }

        if (m_enemyCorpseSettings == null)
        {
            m_enemyCorpseSettings = GetComponent<EnemyCorpseSettings>();
        }

        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_gameOverUI == null)
        {
            // 게임오버 화면은 시작 시 꺼져 있으므로 비활성 오브젝트까지 훑어야 찾을 수 있습니다.
            m_gameOverUI = FindFirstObjectByType<GameOverUIController>(FindObjectsInactive.Include);
        }

        if (m_hideGameOverUIOnAwake && m_gameOverUI != null)
        {
            m_gameOverUI.Hide();
        }
    }

    /// <summary>
    /// 전멸 게임오버 요청 구독을 시작합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="FieldSceneDataManager"/>가 <c>Awake</c>에서 자리를 잡은 뒤 구독해야 하므로 <c>Start</c>에서 붙입니다.
    /// 전멸은 필드 시작보다 한참 뒤에 일어나므로 이 시점이면 이벤트를 놓치지 않습니다.
    /// </remarks>
    private void Start()
    {
        SubscribeGameOverRequest();
    }

    private void OnDestroy()
    {
        UnsubscribeGameOverRequest();

        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void SubscribeGameOverRequest()
    {
        FieldSceneDataManager dataManager = FieldSceneDataManager.Instance != null
            ? FieldSceneDataManager.Instance
            : FindFirstObjectByType<FieldSceneDataManager>();

        if (dataManager == null || m_subscribedFieldSceneDataManager == dataManager)
        {
            return;
        }

        UnsubscribeGameOverRequest();
        m_subscribedFieldSceneDataManager = dataManager;
        m_subscribedFieldSceneDataManager.OnGameOverRequested += HandleGameOverRequested;
    }

    private void UnsubscribeGameOverRequest()
    {
        if (m_subscribedFieldSceneDataManager != null)
        {
            m_subscribedFieldSceneDataManager.OnGameOverRequested -= HandleGameOverRequested;
            m_subscribedFieldSceneDataManager = null;
        }
    }

    /// <summary>
    /// 스쿼드 전멸 게임오버 요청을 받아 입력을 UI 모드로 바꾸고 게임오버 화면을 표시합니다.
    /// </summary>
    /// <remarks>
    /// 귀환 정산을 거치지 않는 경로입니다. 결과값을 만들지 않으므로 이번 출격의 성과는 반영되지 않습니다.
    /// 전멸 시점에는 조작 멤버가 전부 이탈해 <see cref="SetInputMode"/>가 실패할 수 있으므로,
    /// 입력 모드 전환 실패를 화면 표시 차단 사유로 쓰지 않습니다. 게임오버 화면은 반드시 떠야 합니다.
    /// </remarks>
    private void HandleGameOverRequested()
    {
        if (SetInputMode(InputMode.UI) != 1)
        {
            Debug.LogWarning("[FieldManager] 전멸로 입력 모드를 UI로 바꾸지 못했습니다. 게임오버 화면은 그대로 표시합니다.", this);
        }

        if (m_gameOverUI == null)
        {
            m_gameOverUI = FindFirstObjectByType<GameOverUIController>(FindObjectsInactive.Include);
        }

        if (m_gameOverUI == null)
        {
            Debug.LogError("[FieldManager] 게임오버 화면을 찾지 못해 전멸 결과를 표시할 수 없습니다.", this);
            return;
        }

        m_gameOverUI.ShowSquadEliminated();
    }

    /// <summary>현재 필드 씬에 적용된 입력 모드입니다.</summary>
    public InputMode CurrentInputMode => m_currentInputMode;

    /// <summary>
    /// 필드 씬의 입력 모드를 전환합니다.
    /// </summary>
    /// <param name="mode">적용할 입력 모드입니다.</param>
    /// <returns>1은 정상 성공, 0은 정상 실패, -1은 비정상 실패입니다.</returns>
    /// <remarks>
    /// UI 모드 전환 시 현재 PlayerSquadMember의 Aim·TPS Look·게임플레이 입력을 함께 차단합니다.
    /// </remarks>
    public int SetInputMode(InputMode mode)
    {
        if (m_currentInputMode == mode)
        {
            return 1;
        }

        if (!TryResolveCurrentPlayerControls())
        {
            Debug.LogWarning("[FieldManager] 현재 PlayerSquadMember의 필수 입력 참조를 확보하지 못했습니다.", this);
            return 0;
        }

        try
        {
            switch (mode)
            {
                case InputMode.UI:
                    m_cachedAimController.ForceStopAim();
                    m_cachedThirdPersonController.SetLockCameraPosition(true);
                    m_cachedPlayerInputs.SetPlayerCursorMode(true);
                    break;

                case InputMode.Gameplay:
                    m_cachedPlayerInputs.SetPlayerCursorMode(false);
                    m_cachedThirdPersonController.SetLockCameraPosition(false);
                    break;

                default:
                    Debug.LogWarning($"[FieldManager] 지원하지 않는 입력 모드입니다: {mode}", this);
                    return 0;
            }

            m_currentInputMode = mode;
            return 1;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FieldManager] 입력 모드 전환 중 오류가 발생했습니다. mode={mode}, error={exception}", this);
            return -1;
        }
    }

    private bool TryResolveCurrentPlayerControls()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        SquadMemberController currentPlayer = m_squadManager != null
            ? m_squadManager.PlayerSquadMember
            : null;
        if (currentPlayer == null)
        {
            return false;
        }

        if (m_cachedPlayerSquadMember != currentPlayer
            || m_cachedPlayerInputs == null
            || m_cachedThirdPersonController == null
            || m_cachedAimController == null)
        {
            m_cachedPlayerSquadMember = currentPlayer;
            m_cachedPlayerInputs = currentPlayer.GetComponent<PlayerInputs>();
            m_cachedThirdPersonController = currentPlayer.GetComponent<ThirdPersonController>();
            m_cachedAimController = currentPlayer.GetComponent<AimController>();
        }

        return m_cachedPlayerInputs != null
               && m_cachedThirdPersonController != null
               && m_cachedAimController != null;
    }
}
