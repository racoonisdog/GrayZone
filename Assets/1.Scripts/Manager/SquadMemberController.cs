using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;

public class SquadMemberController : MonoBehaviour
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
    [SerializeField] private StarterAssetsInputs starterAssetsInputs;
    [SerializeField] private ThirdPersonController thirdPersonController;
    //[SerializeField] private PlayerManager playerManager;
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private Animator animator;
    [SerializeField] private SquadFollowerAI followerAI;
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
        // 초기 상태 적용은 SquadManager가 전담
    }

    private void AutoFindReferences()
    {
        if (starterAssetsInputs == null)
            starterAssetsInputs = GetComponent<StarterAssetsInputs>();

        if (thirdPersonController == null)
            thirdPersonController = GetComponent<ThirdPersonController>();

        //if (playerManager == null)
        //    playerManager = GetComponent<PlayerManager>();

        if (weaponController == null)
            weaponController = GetComponentInChildren<WeaponController>();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();

        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (followerAI == null)
            followerAI = GetComponent<SquadFollowerAI>();

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

        //if (!allowDirectControl && playerManager != null)
        //{
        //    playerManager.ForceStopAim();
        //}

//        //if (starterAssetsInputs != null)
        //{
        //    starterAssetsInputs.ResetInputState();
        //    starterAssetsInputs.enabled = allowDirectControl;
        //}

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

        //if (playerManager != null)
        //    playerManager.enabled = allowDirectControl;

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