using UnityEngine;
using System;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VInspector;

#if UNITY_EDITOR
using UnityEditor;
#endif

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
    /// 멤버 전환 시 새 조작 멤버에 이어줄 얕은 입력 상태입니다.
    /// </summary>
    public struct SwitchCarryoverState
    {
        public bool HasInput;
        public Vector2 Move;
        public bool Jump;
        public bool Sprint;
        public bool Aim;
        public bool Shoot;
        public bool AnalogMovement;
        public ThirdPersonController.LocomotionCarryoverState Locomotion;
    }

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
    [Tooltip("현재 PlayerSquadMember 역할인지 여부입니다. false이면 AiSquadMember 역할입니다.")]
    [FormerlySerializedAs("isPlayerControlled")]
    [FormerlySerializedAs("m_isPlayerControlled")]
    [SerializeField] private bool m_isPlayerSquadMember;

    [Tooltip("현재 멤버가 생존 상태인지 여부입니다.")]
    [FormerlySerializedAs("isAlive")]
    [SerializeField] private bool m_isAlive = true;

    [Tooltip("현재 멤버가 다운 상태인지 여부입니다.")]
    [FormerlySerializedAs("isDown")]
    [SerializeField] private bool m_isDown;

    private bool m_isInteractionLocked;
    private Collider m_reviveDetectionCollider;
    private RagdollController m_ragdollController;

    [Foldout("Reference Options")]
    [Tooltip("입력 값을 보관하는 플레이어 입력 컴포넌트입니다.")]
    [FormerlySerializedAs("starterAssetsInputs")]
    [FormerlySerializedAs("m_starterAssetsInputs")]
    [SerializeField] private PlayerInputs m_playerInputs;

    [Tooltip("직접 조작 시 사용하는 3인칭 컨트롤러입니다.")]
    [FormerlySerializedAs("thirdPersonController")]
    [SerializeField] private ThirdPersonController m_thirdPersonController;

    [Tooltip("조준, 사격, 재장전 상태를 제어하는 컴포넌트입니다.")]
    [FormerlySerializedAs("playerManager")]
    [SerializeField] private AimController m_aimController;

    [Tooltip("멤버가 장착한 무기 컨트롤러입니다.")]
    [FormerlySerializedAs("weaponController")]
    [SerializeField] private Gun m_weaponController;

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

    [Tooltip("AI 조작 중 CharacterController 대신 활성화할 물리 충돌용 CapsuleCollider입니다.")]
    [SerializeField] private CapsuleCollider m_aiCollisionCollider;

    [Tooltip("이 멤버의 체력 컴포넌트입니다. 사망/부활 시 생존 플래그를 동기화합니다.")]
    [SerializeField] private PlayerHealth m_playerHealth;

    /// <summary>스쿼드 멤버 표시 이름입니다.</summary>
    public string MemberName => m_memberName;

    /// <summary>스쿼드 멤버의 역할입니다.</summary>
    public SquadRole Role => m_role;

    /// <summary>현재 플레이어가 직접 조작하는 PlayerSquadMember인지 여부입니다.</summary>
    public bool IsPlayerSquadMember => m_isPlayerSquadMember;

    /// <summary>현재 스쿼드 AI가 조작하는 AiSquadMember인지 여부입니다.</summary>
    public bool IsAiSquadMember => !m_isPlayerSquadMember;

    /// <summary>현재 멤버가 생존 상태인지 여부입니다.</summary>
    public bool IsAlive => m_isAlive;

    /// <summary>현재 멤버가 다운 상태인지 여부입니다.</summary>
    public bool IsDown => m_isDown;

    /// <summary>카메라가 따라갈 기준 Transform입니다.</summary>
    public Transform CameraTarget => m_cameraTarget != null ? m_cameraTarget : transform;

    /// <summary>이 멤버가 생존 상태에서 사망(전투 이탈) 상태로 바뀔 때 발생합니다.</summary>
    public event Action<SquadMemberController> OnMemberDied;

    /// <summary>이 멤버가 다운(빈사) 상태로 진입할 때 발생합니다.</summary>
    public event Action<SquadMemberController> OnMemberDowned;

    /// <summary>이 멤버의 직접 조작 역할이 PlayerSquadMember와 AiSquadMember 사이에서 변경될 때 발생합니다.</summary>
    public event Action<bool> OnPlayerSquadMemberChanged;

    private static readonly int DownHash = Animator.StringToHash("IsDown");
    private static readonly int DeathHash = Animator.StringToHash("IsDead");
    private static readonly int DoDeathHash = Animator.StringToHash("DoDeath");
    private static readonly int InteractionHash = Animator.StringToHash("IsInteraction");
    private static readonly int ReviveHash = Animator.StringToHash("IsRevive");
    private static readonly int StandingHash = Animator.StringToHash("IsStanding");
    private static readonly int RootHash = Animator.StringToHash("IsRoot");
    private static readonly int DownStateHash = Animator.StringToHash("Base Layer.Down");
    private static readonly int GroundedLocomotionStateHash = Animator.StringToHash("Base Layer.Idle Walk Run Blend");

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
        EnsureDownedAllyInteractable();
        EnsureReviveDetectionCollider();
        ApplyControlState();
    }

    /// <summary>
    /// 체력 컴포넌트의 사망/부활 이벤트를 구독해 생존 플래그를 동기화합니다.
    /// </summary>
    private void OnEnable()
    {
        AutoFindReferences();

        if (m_playerHealth == null)
        {
            return;
        }

        m_playerHealth.OnDown += HandleHealthDowned;
        m_playerHealth.OnDeath += HandleHealthDeath;
        m_playerHealth.OnRevive += HandleHealthRevive;
        m_playerHealth.OnDebugInstantRevive += HandleDebugInstantRevive;
    }

    private void OnDisable()
    {
        if (m_playerHealth == null)
        {
            return;
        }

        m_playerHealth.OnDown -= HandleHealthDowned;
        m_playerHealth.OnDeath -= HandleHealthDeath;
        m_playerHealth.OnRevive -= HandleHealthRevive;
        m_playerHealth.OnDebugInstantRevive -= HandleDebugInstantRevive;
    }

    /// <summary>
    /// HP가 0에 도달하면 다운(빈사) 상태로 전환합니다.
    /// </summary>
    /// <remarks>
    /// 1차 프로토타입 기준: HP 0은 사망이 아니라 다운이며, 다운 중에는 이동/조준/사격이 제한됩니다.
    /// 전투 이탈(사망 확정)은 구조 가능 시간이 끝났을 때 <see cref="HandleHealthDeath"/>로 처리합니다(후속 작업).
    /// </remarks>
    private void HandleHealthDowned()
    {
        SetDown(true);
    }

    /// <summary>
    /// 특수 사망 조건(예: 구조 실패)으로 체력 컴포넌트가 사망하면 전투 이탈(사망) 상태로 전환합니다.
    /// </summary>
    private void HandleHealthDeath()
    {
        SetAlive(false);
    }

    /// <summary>
    /// 부활 시 생존 플래그를 복구합니다.
    /// </summary>
    private void HandleHealthRevive()
    {
        SetAlive(true);
        SetDown(false);
    }

    /// <summary>
    /// 트레이너/Inspector에서 즉시 부활했을 때 다운 애니메이션을 바로 로코모션으로 복귀시킵니다.
    /// </summary>
    /// <remarks>
    /// 일반 구조는 <see cref="DownedAllyInteractable"/>가 기립 모션을 끝낸 뒤
    /// <see cref="CompleteAssistedStandingAnimator"/>를 호출한다. 여기서 같은 호출을 일반
    /// <see cref="HealthSystemBase.OnRevive"/>에 넣으면 기립 모션을 건너뛰므로, 디버그 전용
    /// 이벤트로만 분리한다.
    /// </remarks>
    private void HandleDebugInstantRevive()
    {
        if (!m_isAlive || m_isDown)
        {
            return;
        }

        CompleteAssistedStandingAnimator();
    }

    /// <summary>
    /// 같은 GameObject 및 자식 오브젝트에서 필요한 참조를 자동으로 탐색합니다.
    /// </summary>
    private void AutoFindReferences()
    {
        if (m_playerInputs == null)
        {
            m_playerInputs = GetComponent<PlayerInputs>();
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
            m_weaponController = GetComponentInChildren<Gun>();
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

        if (m_aiCollisionCollider == null)
        {
            m_aiCollisionCollider = GetComponent<CapsuleCollider>();
        }

        if (m_playerHealth == null)
        {
            m_playerHealth = GetComponent<PlayerHealth>();
        }

        if (m_ragdollController == null)
        {
            m_ragdollController = GetComponent<RagdollController>();
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
    /// 이 멤버의 현재 역할을 PlayerSquadMember 또는 AiSquadMember로 설정합니다.
    /// </summary>
    /// <param name="value">직접 조작 대상이면 true입니다.</param>
    public void SetPlayerSquadMember(bool value)
    {
        ApplyRoleSetup(value, clearInteractionLock: !value, isInitialSetup: false);
    }

    /// <summary>
    /// 새 플레이어블 프리팹과 필드 입장 시 사용할 PlayerSquadMember 초기 역할 프리셋을 적용합니다.
    /// </summary>
    /// <remarks>
    /// 생존/다운/체력 상태는 건드리지 않고, 직접 조작에 필요한 입력·이동·조준 컴포넌트만 Player 역할에 맞춥니다.
    /// Edit Mode에서는 Input/Navigation 런타임 API를 호출하지 않고 직렬화되는 활성 상태만 갱신합니다.
    /// </remarks>
    public void ApplyPlayerInitialSetup()
    {
        ApplyRoleSetup(true, clearInteractionLock: true, isInitialSetup: true);
    }

    /// <summary>
    /// 새 플레이어블 프리팹과 필드 입장 시 사용할 AiSquadMember 초기 역할 프리셋을 적용합니다.
    /// </summary>
    /// <remarks>
    /// 생존/다운/체력 상태는 건드리지 않고, AI 이동·충돌 컴포넌트만 AI 역할에 맞춥니다.
    /// </remarks>
    public void ApplyAiInitialSetup()
    {
        ApplyRoleSetup(false, clearInteractionLock: true, isInitialSetup: true);
    }

    /// <summary>
    /// 역할 값과 역할별 컴포넌트 활성 상태를 한 진입점에서 적용합니다.
    /// </summary>
    private void ApplyRoleSetup(bool value, bool clearInteractionLock, bool isInitialSetup)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && isInitialSetup)
        {
            Undo.RecordObject(this, value ? "Apply Player Initial Setup" : "Apply AI Initial Setup");
        }
#endif

        AutoFindReferences();

#if UNITY_EDITOR
        if (!Application.isPlaying && isInitialSetup)
        {
            RecordRoleSetupComponentUndo(value ? "Apply Player Initial Setup" : "Apply AI Initial Setup");
        }
#endif

        bool wasPlayerSquadMember = m_isPlayerSquadMember;
        m_isPlayerSquadMember = value;
        if (clearInteractionLock || !m_isPlayerSquadMember)
        {
            m_isInteractionLocked = false;
        }
        ApplyRoleControlState();

        if (wasPlayerSquadMember != m_isPlayerSquadMember)
        {
            OnPlayerSquadMemberChanged?.Invoke(m_isPlayerSquadMember);
        }
    }

    /// <summary>
    /// 이 멤버의 생존 상태를 설정합니다.
    /// </summary>
    /// <param name="value">생존 상태이면 true입니다.</param>
    public void SetAlive(bool value)
    {
        bool wasAlive = m_isAlive;

        if (!wasAlive && value)
        {
            m_ragdollController?.DeactivateRagdoll();
        }

        m_isAlive = value;

        if (!m_isAlive)
        {
            m_isDown = false;
            m_isInteractionLocked = false;
        }

        ApplyControlState();
        UpdateDownDeathAnimator();

        if (wasAlive && !m_isAlive)
        {
            // 물리 골격이 없을 때만 기존 사망 애니메이션으로 폴백합니다.
            if ((m_ragdollController == null || !m_ragdollController.TryActivateRagdoll())
                && m_animator != null)
            {
                m_animator.SetTrigger(DoDeathHash);
            }

            OnMemberDied?.Invoke(this);
        }
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

        bool wasDown = m_isDown;
        m_isDown = value;
        if (m_isDown)
        {
            m_isInteractionLocked = false;
        }
        UpdateReviveDetectionCollider();
        ApplyControlState();
        UpdateDownDeathAnimator();

        if (!wasDown && m_isDown)
        {
            OnMemberDowned?.Invoke(this);
        }
    }

    private void EnsureDownedAllyInteractable()
    {
        if (m_playerHealth == null)
        {
            return;
        }

        if (GetComponent<DownedAllyInteractable>() == null)
        {
            gameObject.AddComponent<DownedAllyInteractable>();
        }
    }

    /// <summary>
    /// 다운된 아군을 상호작용 탐지(OverlapSphere)로 찾을 수 있도록, 다운 중에만 켜지는 전용 트리거 콜라이더를 준비합니다.
    /// </summary>
    /// <remarks>
    /// 비조작 팔로워는 CharacterController가 꺼져 있어(NavMeshAgent 구동) 유일한 콜라이더가 비활성이며, 이 경우
    /// <see cref="Physics.OverlapSphere"/>가 대상을 반환하지 못합니다. 다운 상태에서만 켜는 트리거 콜라이더로 이 사각을 메웁니다.
    /// </remarks>
    private void EnsureReviveDetectionCollider()
    {
        if (m_reviveDetectionCollider != null)
        {
            return;
        }

        SphereCollider detection = gameObject.AddComponent<SphereCollider>();
        detection.isTrigger = true;
        detection.radius = 0.6f;
        detection.center = new Vector3(0.0f, 1.0f, 0.0f);
        m_reviveDetectionCollider = detection;
        UpdateReviveDetectionCollider();
    }

    private void UpdateReviveDetectionCollider()
    {
        if (m_reviveDetectionCollider != null)
        {
            m_reviveDetectionCollider.enabled = m_isDown;
        }
    }

    public void SetInteractionLocked(bool value)
    {
        if (m_isInteractionLocked == value)
        {
            if (m_isInteractionLocked)
            {
                SuppressNonInteractionInputs();
            }

            return;
        }

        m_isInteractionLocked = value;
        ApplyControlState();

        if (m_isInteractionLocked)
        {
            SuppressNonInteractionInputs();
        }
    }

    public void RefreshInteractionLock()
    {
        if (!m_isInteractionLocked)
        {
            return;
        }

        SuppressNonInteractionInputs();
    }

    public void SetReviveInteractionAnimator(bool active)
    {
        if (m_animator == null)
        {
            return;
        }

        m_animator.SetBool(InteractionHash, active);
        m_animator.SetBool(ReviveHash, active);
        m_animator.SetBool(RootHash, false);
    }

    public void SetAssistedStandingAnimator(bool active)
    {
        if (m_animator == null)
        {
            return;
        }

        m_animator.SetBool(StandingHash, active);

        if (active)
        {
            // 기립 전이가 'Any State→Down'(IsDown)과 Down 상태에 막히지 않도록 Down 파라미터를 선제적으로 내린다.
            // 다운 로직 상태(m_isDown)는 유지되며(구조 완료 시 SetDown(false)로 정리) 애니메이터 파라미터만 먼저 반영한다.
            m_animator.SetBool(DownHash, false);
        }
        else if (m_isAlive && m_isDown)
        {
            // 구조 취소: 다시 다운 포즈로 되돌린다.
            m_animator.SetBool(DownHash, true);
            m_animator.Play(DownStateHash, 0, 0.0f);
            m_animator.Update(0.0f);
        }
    }

    public void CompleteAssistedStandingAnimator()
    {
        if (m_animator == null)
        {
            return;
        }

        m_animator.SetBool(StandingHash, false);
        m_animator.SetBool(DownHash, false);
        m_animator.SetBool(DeathHash, false);
        m_animator.SetBool(InteractionHash, false);
        m_animator.SetBool(ReviveHash, false);
        m_animator.SetBool(RootHash, false);
        m_animator.Play(GroundedLocomotionStateHash, 0, 0.0f);
        m_animator.Update(0.0f);
    }

    /// <summary>
    /// 현재 생존/다운 상태를 애니메이터 파라미터(Down/Death)에 반영합니다.
    /// </summary>
    /// <remarks>
    /// 사망이면 Death=true·Down=false, 생존 중 다운이면 Down=true·Death=false, 그 외엔 둘 다 false입니다.
    /// </remarks>
    private void UpdateDownDeathAnimator()
    {
        bool isDead = !m_isAlive;
        bool isDown = !isDead && m_isDown;

        // 다운/사망 시 상체 조준 IK와 무기 레이어를 완전히 해제해 목표(다운/사망) 모션이 IK에 의해 깨지지 않게 합니다.
        if ((isDead || isDown) && m_aimController != null)
        {
            m_aimController.ReleaseCombatVisuals();
        }

        if (m_animator == null)
        {
            return;
        }

        m_animator.SetBool(DeathHash, isDead);
        m_animator.SetBool(DownHash, isDown);
    }

    /// <summary>
    /// 현재 저장된 상태값을 기준으로 컴포넌트 활성 상태를 다시 적용합니다.
    /// </summary>
    public void ForceRefreshState()
    {
        ApplyControlState();
    }

    /// <summary>
    /// 직접 조작 전환 직전에 유지할 입력 상태를 캡처합니다.
    /// </summary>
    /// <returns>새 조작 멤버에 적용할 입력 상태입니다.</returns>
    public SwitchCarryoverState CaptureSwitchCarryoverState()
    {
        if (m_playerInputs == null)
        {
            return default;
        }

        SwitchCarryoverState state = new()
        {
            HasInput = true,
            Move = m_playerInputs.Move,
            Jump = m_playerInputs.Jump,
            Sprint = m_playerInputs.Sprint,
            Aim = m_playerInputs.Aim,
            Shoot = m_playerInputs.Shoot,
            AnalogMovement = m_playerInputs.AnalogMovement,
        };

        if (m_thirdPersonController != null)
        {
            state.Locomotion = m_thirdPersonController.CaptureLocomotionCarryoverState();
        }

        return state;
    }

    /// <summary>
    /// 전환 직전 캡처한 얕은 입력 상태를 현재 조작 멤버에 적용합니다.
    /// </summary>
    /// <param name="state">적용할 전환 입력 상태입니다.</param>
    public void ApplySwitchCarryoverState(SwitchCarryoverState state)
    {
        if (!state.HasInput)
        {
            return;
        }

        // 조준(ADS)은 유지하지만, 전력질주 중인 힙파이어는 sprint가 우선이므로 전환에 넘기지 않습니다.
        bool sprintCancelsHipfire = state.Sprint && !state.Aim;
        bool keepShoot = sprintCancelsHipfire ? false : state.Shoot;
        // 전투 자세(백뷰)는 조준이거나 유지 가능한 사격(힙파이어) 중이면 유지합니다.
        bool inCombat = state.Aim || keepShoot;

        if (m_playerInputs != null)
        {
            m_playerInputs.MoveInput(state.Move);
            m_playerInputs.JumpInput(state.Jump);
            m_playerInputs.SprintInput(state.Sprint);
            m_playerInputs.AimInput(state.Aim);
            m_playerInputs.ShootInput(keepShoot);
            m_playerInputs.SetAnalogMovement(state.AnalogMovement);
        }

        if (m_thirdPersonController != null)
        {
            m_thirdPersonController.SetCombatStance(inCombat);
            m_thirdPersonController.ApplyLocomotionCarryoverState(state.Locomotion);
        }

        if (m_aimController != null)
        {
            m_aimController.ApplySwitchCarryoverState(state.Aim, keepShoot);
        }
    }

    /// <summary>
    /// 현재 AI 추종 상태를 전환 유지용으로 캡처합니다.
    /// </summary>
    /// <returns>AI 추종 상태입니다.</returns>
    public SquadFollowerAI.FollowCarryoverState CaptureFollowCarryoverState()
    {
        if (m_followerAI == null)
        {
            return default;
        }

        return m_followerAI.CaptureFollowCarryoverState();
    }

    /// <summary>
    /// 전환 직전 캡처한 AI 추종 상태를 현재 멤버에 적용합니다.
    /// </summary>
    /// <param name="state">적용할 AI 추종 상태입니다.</param>
    public void ApplyFollowCarryoverState(SquadFollowerAI.FollowCarryoverState state)
    {
        Vector3 groundReferencePosition = state.HasState ? state.Position : transform.position;
        SnapToNavMeshGround(groundReferencePosition);

        if (m_thirdPersonController != null)
        {
            m_thirdPersonController.ClearAirborneCarryoverState();
        }

        if (m_followerAI == null)
        {
            return;
        }

        m_followerAI.ApplyFollowCarryoverState(state);
    }

    /// <summary>
    /// AI 제어로 전환된 멤버를 현재 위치 근처의 NavMesh 지면으로 보정합니다.
    /// </summary>
    /// <param name="referencePosition">지면 보정 기준 위치입니다.</param>
    private void SnapToNavMeshGround(Vector3 referencePosition)
    {
        if (m_navMeshAgent == null)
        {
            return;
        }

        if (!NavMesh.SamplePosition(referencePosition, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
        {
            return;
        }

        bool wasCharacterControllerEnabled = m_characterController != null && m_characterController.enabled;
        bool wasAgentEnabled = m_navMeshAgent.enabled;

        if (wasCharacterControllerEnabled)
        {
            m_characterController.enabled = false;
        }

        if (wasAgentEnabled)
        {
            m_navMeshAgent.enabled = false;
        }

        transform.position = hit.position;

        if (wasAgentEnabled)
        {
            m_navMeshAgent.enabled = true;

            if (m_navMeshAgent.isOnNavMesh)
            {
                m_navMeshAgent.Warp(hit.position);
            }
        }

        if (wasCharacterControllerEnabled)
        {
            m_characterController.enabled = true;
        }
    }

    /// <summary>
    /// 현재 멤버가 장착한 무기의 탄약 UI를 현재 탄약 상태로 갱신합니다.
    /// </summary>
    public void RefreshWeaponUI()
    {
        if (m_weaponController == null)
        {
            m_weaponController = GetComponentInChildren<Gun>();
        }

        if (m_weaponController != null)
        {
            m_weaponController.UpdateBulletUI();
        }
    }

    /// <summary>
    /// 카메라 타겟 회전을 지정한 월드 회전으로 동기화합니다.
    /// </summary>
    /// <param name="worldRotation">적용할 월드 회전입니다.</param>
    public void SyncCameraTargetRotation(Quaternion worldRotation)
    {
        if (m_thirdPersonController != null)
        {
            m_thirdPersonController.SyncCameraTargetRotation(worldRotation);
            return;
        }

        if (m_cameraTarget != null)
        {
            m_cameraTarget.rotation = worldRotation;
        }
    }

    /// <summary>
    /// 사망한 멤버에 남아 있을 수 있는 직접 조작 입력과 카메라 회전 상태를 정리합니다.
    /// </summary>
    public void ClearDeadControlState()
    {
        if (m_playerInputs != null)
        {
            m_playerInputs.ResetInputState();
            m_playerInputs.enabled = false;
        }

        if (m_playerInput != null)
        {
            m_playerInput.DeactivateInput();
            m_playerInput.enabled = false;
        }

        if (m_aimController != null)
        {
            m_aimController.ForceStopAim();
            m_aimController.enabled = false;
        }

        if (m_thirdPersonController != null)
        {
            m_thirdPersonController.SyncCameraTargetRotation(transform.rotation);
            m_thirdPersonController.enabled = false;
            return;
        }

        if (m_cameraTarget != null)
        {
            m_cameraTarget.rotation = transform.rotation;
        }
    }

    /// <summary>
    /// 현재 생존, 다운, 직접 조작 상태에 따라 입력, 이동, 조준, 추종 AI 컴포넌트의 활성 상태를 적용합니다.
    /// </summary>
    private void ApplyControlState()
    {
        bool allowPlayerInput = m_isAlive && !m_isDown && m_isPlayerSquadMember;
        bool allowDirectControl = allowPlayerInput && !m_isInteractionLocked;
        bool allowAiSquadMember = m_isAlive && !m_isDown && !m_isPlayerSquadMember;

        if (!allowDirectControl && m_aimController != null)
        {
            m_aimController.ForceStopAim();
        }

        if (m_playerInputs != null)
        {
            if (allowPlayerInput)
            {
                if (!allowDirectControl)
                {
                    m_playerInputs.ResetNonInteractionInputState();
                }
            }
            else
            {
                m_playerInputs.ResetInputState();
            }

            m_playerInputs.enabled = allowPlayerInput;
        }

        if (m_characterController != null)
        {
            m_characterController.enabled = allowDirectControl;
        }

        if (m_aiCollisionCollider != null)
        {
            m_aiCollisionCollider.enabled = allowAiSquadMember;
        }

        ApplyNavMeshAgentState(allowAiSquadMember);

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

        if (allowDirectControl
            && m_thirdPersonController != null
            && m_thirdPersonController.IsReload
            && m_weaponController != null
            && !m_weaponController.IsReloading)
        {
            m_thirdPersonController.SetReload(false);

            if (m_aimController != null)
            {
                m_aimController.ApplySwitchCarryoverState(false, false);
            }
        }

        if (m_followerAI != null)
        {
            m_followerAI.enabled = allowAiSquadMember;
        }

        ApplyPlayerInputState(allowPlayerInput);
    }

    /// <summary>
    /// Play Mode와 Edit Mode에서 각각 안전한 방식으로 역할별 활성 상태를 반영합니다.
    /// </summary>
    private void ApplyRoleControlState()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            ApplyEditorRoleControlState();
            MarkRoleSetupComponentsDirty();
            return;
        }
#endif

        ApplyControlState();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector 프리셋 버튼용 활성 상태 반영입니다. Input/NaviMesh의 런타임 API는 호출하지 않습니다.
    /// </summary>
    private void ApplyEditorRoleControlState()
    {
        bool allowPlayerInput = m_isAlive && !m_isDown && m_isPlayerSquadMember;
        bool allowDirectControl = allowPlayerInput && !m_isInteractionLocked;
        bool allowAiSquadMember = m_isAlive && !m_isDown && !m_isPlayerSquadMember;

        SetBehaviourEnabled(m_playerInputs, allowPlayerInput);
        SetColliderEnabled(m_characterController, allowDirectControl);
        SetColliderEnabled(m_aiCollisionCollider, allowAiSquadMember);
        SetBehaviourEnabled(m_navMeshAgent, allowAiSquadMember);
        SetBehaviourEnabled(m_thirdPersonController, allowDirectControl);
        SetBehaviourEnabled(m_aimController, allowDirectControl);
        SetBehaviourEnabled(m_weaponController, true);
        SetBehaviourEnabled(m_followerAI, allowAiSquadMember);
        SetBehaviourEnabled(m_playerInput, allowPlayerInput);
    }

    private void RecordRoleSetupComponentUndo(string actionName)
    {
        RecordUndo(m_playerInputs, actionName);
        RecordUndo(m_characterController, actionName);
        RecordUndo(m_aiCollisionCollider, actionName);
        RecordUndo(m_navMeshAgent, actionName);
        RecordUndo(m_thirdPersonController, actionName);
        RecordUndo(m_aimController, actionName);
        RecordUndo(m_weaponController, actionName);
        RecordUndo(m_followerAI, actionName);
        RecordUndo(m_playerInput, actionName);
    }

    private void MarkRoleSetupComponentsDirty()
    {
        MarkDirty(this);
        MarkDirty(m_playerInputs);
        MarkDirty(m_characterController);
        MarkDirty(m_aiCollisionCollider);
        MarkDirty(m_navMeshAgent);
        MarkDirty(m_thirdPersonController);
        MarkDirty(m_aimController);
        MarkDirty(m_weaponController);
        MarkDirty(m_followerAI);
        MarkDirty(m_playerInput);
    }

    private static void SetBehaviourEnabled(Behaviour component, bool value)
    {
        if (component != null)
        {
            component.enabled = value;
        }
    }

    private static void SetColliderEnabled(Collider component, bool value)
    {
        if (component != null)
        {
            component.enabled = value;
        }
    }

    private static void RecordUndo(UnityEngine.Object target, string actionName)
    {
        if (target != null)
        {
            Undo.RecordObject(target, actionName);
        }
    }

    private static void MarkDirty(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        EditorUtility.SetDirty(target);

        if (PrefabUtility.IsPartOfPrefabInstance(target))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
#endif

    private void SuppressNonInteractionInputs()
    {
        if (m_playerInputs != null)
        {
            m_playerInputs.ResetNonInteractionInputState();
        }

        if (m_aimController != null)
        {
            m_aimController.ForceStopAim();
        }
    }

    /// <summary>
    /// AI 제어 가능 여부에 따라 NavMeshAgent 상태를 전환합니다.
    /// </summary>
    /// <param name="allowAiSquadMember">AiSquadMember 이동을 허용하면 true입니다.</param>
    private void ApplyNavMeshAgentState(bool allowAiSquadMember)
    {
        if (m_navMeshAgent == null)
        {
            return;
        }

        if (allowAiSquadMember)
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
            // 이미 "Player" 맵으로 활성 상태면 재설정하지 않는다.
            // SwitchCurrentActionMap/ActivateInput은 맵을 껐다 켜므로, 이미 눌린 채인 버튼(예: 구조 홀드 중 Interaction)의
            // 홀드 상태가 소실되어 다음 프레임에 IsPressed()가 false가 되고 홀드가 끊긴다. 상태가 이미 맞으면 건너뛴다.
            bool alreadyActive = m_playerInput.enabled
                && m_playerInput.inputIsActive
                && m_playerInput.currentActionMap != null
                && m_playerInput.currentActionMap.name == "Player";
            if (alreadyActive)
            {
                return;
            }

            m_playerInput.enabled = true;
            m_playerInput.ActivateInput();
            m_playerInput.SwitchCurrentActionMap("Player");
            return;
        }

        m_playerInput.DeactivateInput();
        m_playerInput.enabled = false;
    }
}
