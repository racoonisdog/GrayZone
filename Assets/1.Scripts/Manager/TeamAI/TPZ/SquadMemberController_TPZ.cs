using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;

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
    [SerializeField] private bool isPlayerControlled = false;
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
    public bool IsPlayerControlled => isPlayerControlled;
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
        // 초기 상태 적용은 SquadManager_TPZ가 전담
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

    public void SetPlayerControlled(bool value)
    {
        isPlayerControlled = value;
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
        bool allowDirectControl = isAlive && !isDown && isPlayerControlled;
        bool allowAIControl = isAlive && !isDown && !isPlayerControlled;

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
            if (allowAIControl)
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

        if (thirdPersonController != null)
            thirdPersonController.enabled = allowDirectControl;

        if (playerManager != null)
            playerManager.enabled = allowDirectControl;

        if (weaponController != null)
            weaponController.enabled = true;

        if (followerAI != null)
            followerAI.enabled = allowAIControl;

        // PlayerInput는 마지막에
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