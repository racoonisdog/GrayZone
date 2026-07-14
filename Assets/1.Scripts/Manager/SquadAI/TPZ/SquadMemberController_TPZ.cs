using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;
using UnityEngine.Serialization;

public class SquadMemberController_TPZ : MonoBehaviour
{
    public enum SquadRole
    {
        Support,
        Medic,
        Engineer
    }

    [Header("Info")]
    [SerializeField] private string memberName = "Member";
    [SerializeField] private SquadRole role = SquadRole.Support;

    [Header("State")]
    [FormerlySerializedAs("isPlayerControlled")]
    [SerializeField] private bool isPlayerSquadMember = false;
    [SerializeField] private bool isAlive = true;
    [SerializeField] private bool isDown = false;

    [Header("References")]
    [SerializeField] private StarterAssetsInputs_TPZ starterAssetsInputs;
    [SerializeField] private ThirdPersonController_TPZ thirdPersonController;
    [SerializeField] private PlayerManager_TPZ playerManager;
    [SerializeField] private WeaponController_TPZ weaponController;
    [SerializeField] private Animator animator;
    [SerializeField] private SquadFollowerAI_TPZ followerAI;
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private CharacterController characterController;

    public string MemberName => memberName;
    public SquadRole Role => role;

    /// <summary>현재 플레이어가 직접 조작하는 PlayerSquadMember인지 여부입니다.</summary>
    public bool IsPlayerSquadMember => isPlayerSquadMember;

    /// <summary>현재 스쿼드 AI가 조작하는 AiSquadMember인지 여부입니다.</summary>
    public bool IsAiSquadMember => !isPlayerSquadMember;
    public bool IsAlive => isAlive;
    public bool IsDown => isDown;
    public Transform CameraTarget => cameraTarget != null ? cameraTarget : transform;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        // �ʱ� ���� ������ SquadManager_TPZ�� ����
    }

    private void AutoFindReferences()
    {
        if (starterAssetsInputs == null)
            starterAssetsInputs = GetComponent<StarterAssetsInputs_TPZ>();

        if (thirdPersonController == null)
            thirdPersonController = GetComponent<ThirdPersonController_TPZ>();

        if (playerManager == null)
            playerManager = GetComponent<PlayerManager_TPZ>();

        if (weaponController == null)
            weaponController = GetComponentInChildren<WeaponController_TPZ>();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();

        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (followerAI == null)
            followerAI = GetComponent<SquadFollowerAI_TPZ>();

        if (cameraTarget == null)
        {
            Transform found = transform.Find("PlayerCameraRoot");
            if (found == null) found = transform.Find("CinemachineCameraTarget");
            if (found == null) found = transform.Find("CameraRoot");

            if (found != null)
                cameraTarget = found;
        }
    }

    /// <summary>
    /// 이 멤버의 현재 역할을 PlayerSquadMember 또는 AiSquadMember로 설정합니다.
    /// </summary>
    /// <param name="value">PlayerSquadMember로 설정하면 true입니다.</param>
    public void SetPlayerSquadMember(bool value)
    {
        isPlayerSquadMember = value;
        ApplyControlState();
    }

    public void SetAlive(bool value)
    {
        isAlive = value;

        if (!isAlive)
            isDown = false;

        ApplyControlState();
    }

    public void SetDown(bool value)
    {
        if (!isAlive && value) return;

        isDown = value;
        ApplyControlState();
    }

    public void ForceRefreshState()
    {
        ApplyControlState();
    }

    private void ApplyControlState()
    {
        bool allowDirectControl = isAlive && !isDown && isPlayerSquadMember;
        bool allowAiSquadMember = isAlive && !isDown && !isPlayerSquadMember;

        if (!allowDirectControl && playerManager != null)
        {
            playerManager.ForceStopAim();
        }

        if (starterAssetsInputs != null)
        {
            starterAssetsInputs.ResetInputState();
            starterAssetsInputs.enabled = allowDirectControl;
        }

        if (characterController != null)
        {
            characterController.enabled = allowDirectControl;
        }

        if (navMeshAgent != null)
        {
            if (allowAiSquadMember)
            {
                if (!navMeshAgent.enabled)
                    navMeshAgent.enabled = true;

                navMeshAgent.isStopped = false;
            }
            else
            {
                if (navMeshAgent.enabled)
                {
                    navMeshAgent.isStopped = true;
                    navMeshAgent.ResetPath();
                    navMeshAgent.enabled = false;
                }
            }
        }

        //if (thirdPersonController != null)
        //    thirdPersonController.enabled = allowDirectControl;

        if (playerManager != null)
            playerManager.enabled = allowDirectControl;

        if (weaponController != null)
            weaponController.enabled = true;

        if (followerAI != null)
            followerAI.enabled = allowAiSquadMember;

        // PlayerInput�� ��������
        if (playerInput != null)
        {
            if (allowDirectControl)
            {
                playerInput.enabled = true;
                playerInput.ActivateInput();
                playerInput.SwitchCurrentActionMap("Player");
            }
            else
            {
                playerInput.DeactivateInput();
                playerInput.enabled = false;
            }
        }
    }
}
