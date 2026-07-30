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
public class FieldManager : MonoBehaviour, IInputModeController
{
    private static FieldManager s_instance;

    [Tooltip("현재 직접 조작 중인 스쿼드 멤버를 제공하는 필드 스쿼드 매니저입니다. 비어 있으면 Awake에서 한 번 탐색합니다.")]
    [SerializeField] private SquadManager m_squadManager;

    private InputMode m_currentInputMode = InputMode.Gameplay;
    private SquadMemberController m_cachedPlayerSquadMember;
    private PlayerInputs m_cachedPlayerInputs;
    private ThirdPersonController m_cachedThirdPersonController;
    private AimController m_cachedAimController;

    /// <summary>현재 필드 씬의 인스턴스입니다. 필드 씬이 아니면 <c>null</c>입니다.</summary>
    /// <remarks>씬에 속하므로 씬 전환과 함께 사라집니다. 필드 밖에서는 존재하지 않는 것이 정상입니다.</remarks>
    public static FieldManager Instance => s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
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

        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
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
