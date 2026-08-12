using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 필드 씬의 생명주기와 게임플레이 입력 모드 전환을 조율하는 씬 루트 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 데이터 스냅샷과 정산은 <see cref="FieldSceneDataManager"/>가 소유합니다.
/// 이 클래스는 결과 오버레이 진입 같은 씬 상태를 결정하고, 이후 활성 플레이어와 UI에 필요한 전환을 지시합니다.
/// 현재는 플레이어 입력·결과 UI 진입까지 연결되어 있으며, 결과 오버레이 동안의 시간 정지와 저장 완료 게이트는 후속 구현 대상입니다.
/// </remarks>
public class FieldManager : MonoBehaviour, IInputModeController
{
    private static FieldManager s_instance;

    [Tooltip("현재 직접 조작 중인 스쿼드 멤버를 제공하는 필드 스쿼드 매니저입니다. 비어 있으면 Awake에서 한 번 탐색합니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Tooltip("일회성 시각 피드백(탄흔·혈흔·임팩트)을 내보내는 자식 오브젝트의 매니저입니다.")]
    [FormerlySerializedAs("m_surfaceFeedbackSystem")]
    [SerializeField] private EffectManager m_effectManager;

    [Tooltip("필드 위치형 one-shot 사운드의 AudioSource 풀과 동시 발음 정책을 보관하는 자식 오브젝트의 매니저입니다.")]
    [FormerlySerializedAs("m_audioFeedbackSystem")]
    [SerializeField] private AudioManager m_audioManager;

    [Tooltip("적 진영 전반의 공용 설정과 제어를 소유하는 자식 오브젝트의 매니저입니다. 현재는 시체 삭제와 래그돌 설정을 담당합니다.")]
    [FormerlySerializedAs("m_enemyCorpseSettings")]
    [SerializeField] private EnemyManager m_enemyManager;

    [Tooltip("스쿼드 전멸 시 표시할 게임오버 화면입니다. 비어 있으면 비활성 오브젝트까지 포함해 자동 탐색합니다.")]
    [SerializeField] private GameOverUIController m_gameOverUI;

    [Tooltip("게임오버 화면이 전멸 전에는 보이지 않도록 Awake에서 숨길지 여부입니다.")]
    [SerializeField] private bool m_hideGameOverUIOnAwake = true;

    private InputMode m_currentInputMode = InputMode.Gameplay;
    private FieldSceneDataManager m_subscribedFieldSceneDataManager;
    private SquadMemberController m_cachedPlayerSquadMember;
    private PlayerInputController m_cachedPlayerInputController;
    private ThirdPersonController m_cachedThirdPersonController;
    private AimController m_cachedAimController;

    /// <summary>현재 필드 씬의 인스턴스입니다. 필드 씬이 아니면 <c>null</c>입니다.</summary>
    /// <remarks>씬에 속하므로 씬 전환과 함께 사라집니다. 필드 밖에서는 존재하지 않는 것이 정상입니다.</remarks>
    public static FieldManager Instance => s_instance;

    /// <summary>탄흔·혈흔·임팩트 같은 일회성 시각 피드백을 내보내는 매니저입니다.</summary>
    public EffectManager EffectManager => m_effectManager != null
        ? m_effectManager
        : m_effectManager = GetComponentInChildren<EffectManager>(true);

    /// <summary>피격·사망·표면 충돌처럼 월드 위치에 남는 필드 one-shot 사운드 출력 매니저입니다.</summary>
    public AudioManager AudioManager => m_audioManager != null
        ? m_audioManager
        : m_audioManager = GetComponentInChildren<AudioManager>(true);

    /// <summary>적 진영 전반의 공용 설정과 제어를 소유하는 매니저입니다.</summary>
    public EnemyManager EnemyManager => m_enemyManager != null
        ? m_enemyManager
        : m_enemyManager = GetComponentInChildren<EnemyManager>(true);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    private void Reset()
    {
        CacheChildManagers();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheChildManagers();
    }
#endif

    /// <summary>
    /// 자식 오브젝트에 있는 매니저 참조를 채웁니다.
    /// </summary>
    /// <remarks>
    /// 비활성 오브젝트까지 훑습니다. 매니저를 잠시 꺼 두고 테스트하는 경우에도 참조가 끊기지 않아야 합니다.
    ///
    /// 없으면 만들지 않고 비워 둡니다. 예전에는 <c>AddComponent</c>로 보강했지만, 자식 오브젝트 구조에서는
    /// 그렇게 붙인 것이 씬에 저장되지 않아 실행할 때마다 기본값으로 되살아납니다. 그러면 인스펙터에서
    /// 조정한 예산이 조용히 무시되므로, 없으면 없는 대로 두고 <see cref="WarnMissingManagers"/>가 알립니다.
    /// </remarks>
    private void CacheChildManagers()
    {
        if (m_effectManager == null)
        {
            m_effectManager = GetComponentInChildren<EffectManager>(true);
        }

        if (m_audioManager == null)
        {
            m_audioManager = GetComponentInChildren<AudioManager>(true);
        }

        if (m_enemyManager == null)
        {
            m_enemyManager = GetComponentInChildren<EnemyManager>(true);
        }
    }

    /// <summary>빠진 자식 매니저를 한 번에 알립니다.</summary>
    /// <remarks>
    /// 이것들이 없으면 탄흔·사운드·시체 처리가 조용히 멈춥니다. 원인을 찾기 어려운 침묵이라 시작할 때 짚어 둡니다.
    /// </remarks>
    private void WarnMissingManagers()
    {
        if (m_effectManager == null)
        {
            Debug.LogWarning("[FieldManager] EffectManager 자식 오브젝트를 찾지 못했습니다. 탄흔과 임팩트 이펙트가 나오지 않습니다.", this);
        }

        if (m_audioManager == null)
        {
            Debug.LogWarning("[FieldManager] AudioManager 자식 오브젝트를 찾지 못했습니다. 필드 위치형 사운드가 나오지 않습니다.", this);
        }

        if (m_enemyManager == null)
        {
            Debug.LogWarning("[FieldManager] EnemyManager 자식 오브젝트를 찾지 못했습니다. 시체 처리 설정이 적용되지 않습니다.", this);
        }
    }

    private void Awake()
    {
        // 씬에 미리 배치된 것이 있으면 그쪽이 먼저 자리를 잡고, 뒤에 생긴 쪽이 물러납니다.
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;

        CacheChildManagers();
        WarnMissingManagers();

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
                    // 멤버별 상태는 스쿼드 전원에게 겁니다. 조작 멤버에게만 걸면 걸 때와 풀 때의
                    // 조작 멤버가 다를 경우 한쪽이 막힌 채 남습니다(다운 강제 전환은 UI 중에도 일어납니다).
                    m_squadManager.ApplyInputModeToSquad(false);

                    // 커서는 화면에 하나뿐이라 조작 멤버 쪽에서 한 번만 다룹니다.
                    m_cachedPlayerInputController.SetPlayerCursorMode(true);
                    break;

                case InputMode.Gameplay:
                    m_squadManager.ApplyInputModeToSquad(true);
                    m_cachedPlayerInputController.SetPlayerCursorMode(false);
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
            || m_cachedPlayerInputController == null
            || m_cachedThirdPersonController == null
            || m_cachedAimController == null)
        {
            m_cachedPlayerSquadMember = currentPlayer;
            m_cachedPlayerInputController = currentPlayer.GetComponent<PlayerInputController>();
            m_cachedThirdPersonController = currentPlayer.GetComponent<ThirdPersonController>();
            m_cachedAimController = currentPlayer.GetComponent<AimController>();
        }

        return m_cachedPlayerInputController != null
               && m_cachedThirdPersonController != null
               && m_cachedAimController != null;
    }
}
