using StarterAssets;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 스쿼드 멤버의 조작 주체, 생존 상태, 참조 컴포넌트 활성 상태를 관리하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 현재 멤버가 플레이어에게 직접 조작되는지, AI 추종 상태인지에 따라 입력, 캐릭터 컨트롤러,
/// 네비게이션 에이전트, 조준 컨트롤러, 추종 AI를 전환합니다.
/// </remarks>
[RequireComponent(typeof(Animator))]
public class SquadMemberController : MonoBehaviour
{
    /// <summary>
    /// 스쿼드 멤버의 전술 역할입니다.
    /// </summary>
    public enum SquadRole
    {
        /// <summary>지원 역할입니다.</summary>
        Support,

        /// <summary>의무병 역할입니다.</summary>
        Medic,

        /// <summary>공병 역할입니다.</summary>
        Engineer
    }

    [Foldout("Info Options")]
    [Tooltip("스쿼드 멤버 표시 이름입니다.")]
    [FormerlySerializedAs("memberName")]
    [SerializeField] private string m_memberName = "Member";

    [Tooltip("스쿼드 멤버의 역할입니다.")]
    [FormerlySerializedAs("role")]
    [SerializeField] private SquadRole m_role = SquadRole.Support;

    [Foldout("State Options")]
    [Tooltip("현재 플레이어가 직접 조작 중인지 여부입니다.")]
    [FormerlySerializedAs("isPlayerControlled")]
    [SerializeField] private bool m_isPlayerControlled;

    [Tooltip("현재 멤버가 생존 상태인지 여부입니다.")]
    [FormerlySerializedAs("isAlive")]
    [SerializeField] private bool m_isAlive = true;

    [Tooltip("현재 멤버가 다운 상태인지 여부입니다.")]
    [FormerlySerializedAs("isDown")]
    [SerializeField] private bool m_isDown;

    [Foldout("Reference Options")]
    [Tooltip("입력 값을 보관하는 Starter Assets 입력 컴포넌트입니다.")]
    [FormerlySerializedAs("starterAssetsInputs")]
    [SerializeField] private StarterAssetsInputs m_starterAssetsInputs;

    [Tooltip("직접 조작 시 사용하는 3인칭 컨트롤러입니다.")]
    [FormerlySerializedAs("thirdPersonController")]
    [SerializeField] private ThirdPersonController m_thirdPersonController;

    [Tooltip("조준, 사격, 재장전 상태를 제어하는 컴포넌트입니다.")]
    [FormerlySerializedAs("playerManager")]
    [SerializeField] private AimController m_aimController;

    [Tooltip("멤버가 장착한 무기 컨트롤러입니다.")]
    [FormerlySerializedAs("weaponController")]
    [SerializeField] private WeaponController m_weaponController;

    [Tooltip("멤버의 애니메이터입니다.")]
    [FormerlySerializedAs("animator")]
    [SerializeField] private Animator m_animator;

    [Tooltip("AI 추종 이동을 담당하는 컴포넌트입니다.")]
    [FormerlySerializedAs("followerAI")]
    [SerializeField] private SquadFollowerAI m_followerAI;

    [Tooltip("직접 조작 시 사용하는 PlayerInput 컴포넌트입니다.")]
    [FormerlySerializedAs("playerInput")]
    [SerializeField] private PlayerInput m_playerInput;

    [Tooltip("카메라가 따라갈 기준 Transform입니다. 비어 있으면 자기 Transform을 사용합니다.")]
    [FormerlySerializedAs("cameraTarget")]
    [SerializeField] private Transform m_cameraTarget;

    [Tooltip("AI 추종 이동 시 사용하는 NavMeshAgent입니다.")]
    [FormerlySerializedAs("navMeshAgent")]
    [SerializeField] private NavMeshAgent m_navMeshAgent;

    [Tooltip("직접 조작 시 사용하는 CharacterController입니다.")]
    [FormerlySerializedAs("characterController")]
    [SerializeField] private CharacterController m_characterController;

    /// <summary>스쿼드 멤버 표시 이름입니다.</summary>
    public string MemberName => m_memberName;

    /// <summary>스쿼드 멤버의 역할입니다.</summary>
    public SquadRole Role => m_role;

    /// <summary>현재 플레이어가 직접 조작 중인지 여부입니다.</summary>
    public bool IsPlayerControlled => m_isPlayerControlled;

    /// <summary>현재 멤버가 생존 상태인지 여부입니다.</summary>
    public bool IsAlive => m_isAlive;

    /// <summary>현재 멤버가 다운 상태인지 여부입니다.</summary>
    public bool IsDown => m_isDown;

    /// <summary>카메라가 따라갈 기준 Transform입니다.</summary>
    public Transform CameraTarget => m_cameraTarget != null ? m_cameraTarget : transform;

    /// <summary>
    /// Inspector에서 컴포넌트가 추가되거나 Reset될 때 현재 GameObject 기준으로 참조를 자동 탐색합니다.
    /// </summary>
    private void Reset()
    {
        AutoFindReferences();
    }

    /// <summary>
    /// 런타임 시작 시 필요한 참조를 캐싱하고 현재 상태에 맞게 조작 상태를 적용합니다.
    /// </summary>
    private void Awake()
    {
        AutoFindReferences();
        ApplyControlState();
    }

    /// <summary>
    /// 같은 GameObject 및 자식 오브젝트에서 필요한 참조를 자동으로 탐색합니다.
    /// </summary>
    private void AutoFindReferences()
    {
        if (m_starterAssetsInputs == null)
        {
            m_starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        }

        if (m_thirdPersonController == null)
        {
            m_thirdPersonController = GetComponent<ThirdPersonController>();
        }

        if (m_aimController == null)
        {
            m_aimController = GetComponent<AimController>();
        }

        if (m_weaponController == null)
        {
            m_weaponController = GetComponentInChildren<WeaponController>();
        }

        if (m_animator == null)
        {
            m_animator = GetComponent<Animator>();
        }

        if (m_playerInput == null)
        {
            m_playerInput = GetComponent<PlayerInput>();
        }

        if (m_navMeshAgent == null)
        {
            m_navMeshAgent = GetComponent<NavMeshAgent>();
        }

        if (m_characterController == null)
        {
            m_characterController = GetComponent<CharacterController>();
        }

        if (m_followerAI == null)
        {
            m_followerAI = GetComponent<SquadFollowerAI>();
        }

        if (m_cameraTarget == null)
        {
            m_cameraTarget = FindCameraTarget();
        }
    }

    /// <summary>
    /// 자주 사용되는 카메라 타겟 이름을 기준으로 카메라 기준 Transform을 찾습니다.
    /// </summary>
    /// <returns>찾은 카메라 타겟 Transform입니다. 없으면 null을 반환합니다.</returns>
    private Transform FindCameraTarget()
    {
        Transform found = transform.Find("PlayerCameraRoot");

        if (found == null)
        {
            found = transform.Find("CinemachineCameraTarget");
        }

        if (found == null)
        {
            found = transform.Find("CameraRoot");
        }

        return found;
    }

    /// <summary>
    /// 이 멤버가 플레이어 직접 조작 대상인지 설정합니다.
    /// </summary>
    /// <param name="value">직접 조작 대상이면 true입니다.</param>
    public void SetPlayerControlled(bool value)
    {
        m_isPlayerControlled = value;
        ApplyControlState();
    }

    /// <summary>
    /// 이 멤버의 생존 상태를 설정합니다.
    /// </summary>
    /// <param name="value">생존 상태이면 true입니다.</param>
    public void SetAlive(bool value)
    {
        m_isAlive = value;

        if (!m_isAlive)
        {
            m_isDown = false;
        }

        ApplyControlState();
    }

    /// <summary>
    /// 이 멤버의 다운 상태를 설정합니다.
    /// </summary>
    /// <param name="value">다운 상태이면 true입니다.</param>
    public void SetDown(bool value)
    {
        if (!m_isAlive && value)
        {
            return;
        }

        m_isDown = value;
        ApplyControlState();
    }

    /// <summary>
    /// 현재 저장된 상태값을 기준으로 컴포넌트 활성 상태를 다시 적용합니다.
    /// </summary>
    public void ForceRefreshState()
    {
        ApplyControlState();
    }

    /// <summary>
    /// 현재 생존, 다운, 직접 조작 상태에 따라 입력, 이동, 조준, 추종 AI 컴포넌트의 활성 상태를 적용합니다.
    /// </summary>
    private void ApplyControlState()
    {
        bool allowDirectControl = m_isAlive && !m_isDown && m_isPlayerControlled;
        bool allowAIControl = m_isAlive && !m_isDown && !m_isPlayerControlled;

        if (!allowDirectControl && m_aimController != null)
        {
            m_aimController.ForceStopAim();
        }

        if (m_starterAssetsInputs != null)
        {
            m_starterAssetsInputs.ResetInputState();
            m_starterAssetsInputs.enabled = allowDirectControl;
        }

        if (m_characterController != null)
        {
            m_characterController.enabled = allowDirectControl;
        }

        ApplyNavMeshAgentState(allowAIControl);

        if (m_thirdPersonController != null)
        {
            m_thirdPersonController.enabled = allowDirectControl;
        }

        if (m_aimController != null)
        {
            m_aimController.enabled = allowDirectControl;
        }

        if (m_weaponController != null)
        {
            m_weaponController.enabled = true;
        }

        if (m_followerAI != null)
        {
            m_followerAI.enabled = allowAIControl;
        }

        ApplyPlayerInputState(allowDirectControl);
    }

    /// <summary>
    /// AI 제어 가능 여부에 따라 NavMeshAgent 상태를 전환합니다.
    /// </summary>
    /// <param name="allowAIControl">AI 이동을 허용하면 true입니다.</param>
    private void ApplyNavMeshAgentState(bool allowAIControl)
    {
        if (m_navMeshAgent == null)
        {
            return;
        }

        if (allowAIControl)
        {
            if (!m_navMeshAgent.enabled)
            {
                m_navMeshAgent.enabled = true;
            }

            m_navMeshAgent.isStopped = false;
            return;
        }

        if (!m_navMeshAgent.enabled)
        {
            return;
        }

        m_navMeshAgent.isStopped = true;
        m_navMeshAgent.ResetPath();
        m_navMeshAgent.enabled = false;
    }

    /// <summary>
    /// 직접 조작 가능 여부에 따라 PlayerInput 활성 상태와 액션 맵을 전환합니다.
    /// </summary>
    /// <param name="allowDirectControl">직접 조작을 허용하면 true입니다.</param>
    private void ApplyPlayerInputState(bool allowDirectControl)
    {
        if (m_playerInput == null)
        {
            return;
        }

        if (allowDirectControl)
        {
            m_playerInput.enabled = true;
            m_playerInput.ActivateInput();
            m_playerInput.SwitchCurrentActionMap("Player");
            return;
        }

        m_playerInput.DeactivateInput();
        m_playerInput.enabled = false;
    }
}
