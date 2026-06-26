using UnityEngine;
using UnityEngine.Serialization;
using VInspector;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Starter Assets 기반의 3인칭 캐릭터 이동, 회전, 점프, 중력, 애니메이션 파라미터를 제어하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 컨트롤러는 <see cref="CharacterController"/>와 <see cref="PlayerInputs"/>를 필수 참조로 사용합니다.
/// 필수 참조는 <c>Awake</c>에서 캐싱하고, 누락 시 컴포넌트를 비활성화하여 런타임 null 참조를 방지합니다.
/// </remarks>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputs))]
#if ENABLE_INPUT_SYSTEM
[RequireComponent(typeof(PlayerInput))]
#endif
public class ThirdPersonController : MonoBehaviour
{
    /// <summary>
    /// 멤버 전환 시 이어받을 이동 블렌드 상태입니다.
    /// </summary>
    public struct LocomotionCarryoverState
    {
        public bool HasState;
        public float Speed;
        public float AnimationBlend;
        public float MotionSpeed;
        public float TargetRotation;
        public float RotationVelocity;
        public float VerticalVelocity;
        public float JumpTimeoutDelta;
        public float FallTimeoutDelta;
        public bool Grounded;
        public bool Jump;
        public bool FreeFall;
    }


    /// <summary>이동 관련 설정값입니다.</summary>
    [Foldout("Move Options")]
    [Tooltip("캐릭터의 기본 이동 속도입니다. 단위는 m/s입니다.")]
    [FormerlySerializedAs("MoveSpeed")]
    [SerializeField] private float m_moveSpeed = 2.0f;

    [Tooltip("캐릭터의 전력질주 속도입니다. 단위는 m/s입니다.")]
    [FormerlySerializedAs("SprintSpeed")]
    [SerializeField] private float m_sprintSpeed = 5.335f;

    [Tooltip("캐릭터가 이동 방향을 바라보도록 회전하는 데 걸리는 보간 시간입니다.")]
    [Range(0.0f, 0.3f)]
    [FormerlySerializedAs("RotationSmoothTime")]
    [SerializeField] private float m_rotationSmoothTime = 0.12f;

    [Tooltip("가속과 감속 반응 속도입니다.")]
    [FormerlySerializedAs("SpeedChangeRate")]
    [SerializeField] private float m_speedChangeRate = 10.0f;



    /// <summary>점프와 중력 관련 설정값입니다.</summary>
    [Foldout("Jump Options")]
    [Tooltip("캐릭터가 점프할 수 있는 높이입니다.")]
    [FormerlySerializedAs("JumpHeight")]
    [SerializeField] private float m_jumpHeight = 1.2f;

    [Tooltip("캐릭터에 적용할 중력 값입니다. Unity 기본 중력은 -9.81입니다.")]
    [FormerlySerializedAs("Gravity")]
    [SerializeField] private float m_gravity = -15.0f;

    [Tooltip("다음 점프가 가능해지기까지 필요한 대기 시간입니다. 0이면 즉시 다시 점프할 수 있습니다.")]
    [FormerlySerializedAs("JumpTimeout")]
    [SerializeField] private float m_jumpTimeout = 0.50f;

    [Tooltip("낙하 상태로 전환되기 전까지의 대기 시간입니다. 계단 이동 같은 작은 단차 처리에 유용합니다.")]
    [FormerlySerializedAs("FallTimeout")]
    [SerializeField] private float m_fallTimeout = 0.15f;



    /// <summary>지면 감지 관련 설정값입니다.</summary>
    [Foldout("Ground Options")]
    [Tooltip("지면 감지 위치의 Y축 오프셋입니다. 울퉁불퉁한 지형에서 보정용으로 사용합니다.")]
    [FormerlySerializedAs("GroundedOffset")]
    [SerializeField] private float m_groundedOffset = -0.14f;

    [Tooltip("지면 감지 구체의 반지름입니다. CharacterController 반지름과 맞추는 것이 좋습니다.")]
    [FormerlySerializedAs("GroundedRadius")]
    [SerializeField] private float m_groundedRadius = 0.28f;

    [Tooltip("지면으로 판정할 레이어 마스크입니다.")]
    [FormerlySerializedAs("GroundLayers")]
    [SerializeField] private LayerMask m_groundLayers;



    /// <summary>카메라 회전 타겟과 상하 회전 제한 설정값입니다.</summary>
    [Foldout("Camera Options")]
    [Tooltip("Cinemachine Virtual Camera가 따라갈 카메라 타겟 오브젝트입니다.")]
    [FormerlySerializedAs("CinemachineCameraTarget")]
    [SerializeField] private GameObject m_cinemachineCameraTarget;

    [Tooltip("카메라를 위로 회전할 수 있는 최대 각도입니다.")]
    [FormerlySerializedAs("TopClamp")]
    [SerializeField] private float m_topClamp = 70.0f;

    [Tooltip("카메라를 아래로 회전할 수 있는 최대 각도입니다.")]
    [FormerlySerializedAs("BottomClamp")]
    [SerializeField] private float m_bottomClamp = -30.0f;

    [Tooltip("카메라 각도에 추가로 적용할 보정 각도입니다. 고정 카메라 튜닝에 사용할 수 있습니다.")]
    [FormerlySerializedAs("CameraAngleOverride")]
    [SerializeField] private float m_cameraAngleOverride = 0.0f;

    [Tooltip("카메라 회전 입력을 잠글지 여부입니다.")]
    [FormerlySerializedAs("LockCameraPosition")]
    [SerializeField] private bool m_lockCameraPosition = false;



    /// <summary>애니메이션 이벤트에서 재생할 캐릭터 오디오 설정값입니다.</summary>
    [Foldout("Audio Options")]
    [FormerlySerializedAs("LandingAudioClip")]
    [SerializeField] private AudioClip m_landingAudioClip;

    [FormerlySerializedAs("FootstepAudioClips")]
    [SerializeField] private AudioClip[] m_footstepAudioClips;

    [Range(0, 1)]
    [FormerlySerializedAs("FootstepAudioVolume")]
    [SerializeField] private float m_footstepAudioVolume = 0.5f;

    /// <summary>카메라 회전 보간에 사용하는 현재 yaw 값입니다.</summary>
    private float m_cinemachineTargetYaw;

    /// <summary>카메라 회전 보간에 사용하는 현재 pitch 값입니다.</summary>
    private float m_cinemachineTargetPitch;

    /// <summary>현재 프레임 이동에 사용할 수평 속도입니다.</summary>
    private float m_speed;

    /// <summary>Animator의 이동 블렌드 파라미터에 전달할 보간 속도값입니다.</summary>
    private float m_animationBlend;
    /// <summary>캐릭터가 바라봐야 할 목표 Y축 회전각입니다.</summary>
    private float m_targetRotation = 0.0f;

    /// <summary><see cref="Mathf.SmoothDampAngle"/>에서 사용하는 회전 속도 참조값입니다.</summary>
    private float m_rotationVelocity;

    /// <summary>현재 수직 속도입니다. 점프와 중력 계산에 사용합니다.</summary>
    private float m_verticalVelocity;

    /// <summary>수직 낙하 속도의 상한값입니다.</summary>
    private readonly float m_terminalVelocity = 53.0f;

    /// <summary>점프 재입력 제한 타이머입니다.</summary>
    private float m_jumpTimeoutDelta;

    /// <summary>낙하 애니메이션 전환 지연 타이머입니다.</summary>
    private float m_fallTimeoutDelta;

    /// <summary>Animator Speed 파라미터 해시입니다.</summary>
    private int m_animIDSpeed;

    /// <summary>Animator Grounded 파라미터 해시입니다.</summary>
    private int m_animIDGrounded;

    /// <summary>Animator Jump 파라미터 해시입니다.</summary>
    private int m_animIDJump;

    /// <summary>Animator FreeFall 파라미터 해시입니다.</summary>
    private int m_animIDFreeFall;

    /// <summary>Animator MotionSpeed 파라미터 해시입니다.</summary>
    private int m_animIDMotionSpeed;

    /// <summary>공중 상태를 강제로 끊을 때 되돌아갈 기본 지상 이동 상태입니다.</summary>
    private static readonly int GroundedLocomotionStateHash = Animator.StringToHash("Base Layer.Idle Walk Run Blend");

#if ENABLE_INPUT_SYSTEM
    /// <summary>현재 입력 장치 판별에 사용하는 PlayerInput 컴포넌트입니다.</summary>
    private PlayerInput m_playerInput;
#endif
    /// <summary>캐릭터 애니메이션 파라미터를 갱신할 Animator 컴포넌트입니다.</summary>
    private Animator m_animator;

    /// <summary>실제 이동 충돌 처리를 수행하는 CharacterController 컴포넌트입니다.</summary>
    private CharacterController m_controller;

    /// <summary>Starter Assets 입력 상태를 보관하는 입력 컴포넌트입니다.</summary>
    private PlayerInputs m_input;

    /// <summary>MainCamera 태그로 찾은 기준 카메라 오브젝트입니다.</summary>
    private GameObject m_mainCamera;

    /// <summary>입력값을 유효한 입력으로 간주하기 위한 최소 제곱 크기 기준입니다.</summary>
    private const float Threshold = 0.01f;

    /// <summary>Animator 컴포넌트가 존재하는지 여부입니다.</summary>
    private bool m_hasAnimator;

    /// <summary>필수 참조 캐싱과 검증이 완료되었는지 여부입니다.</summary>
    private bool m_hasRequiredReferences;

    /// <summary>현재 캐릭터가 지면에 닿아 있는지 여부입니다.</summary>
    private bool m_grounded = true;

    /// <summary>조준 이동 상태 여부입니다. true이면 이동 중 몸 회전을 제한합니다.</summary>
    private bool m_isAimMove;

    /// <summary>재장전 상태 여부입니다. true이면 전력질주 대신 기본 이동 속도를 사용합니다.</summary>
    private bool m_isReload;

    /// <summary>기본 이동 속도입니다.</summary>
    public float MoveSpeed => m_moveSpeed;
    /// <summary>전력질주 이동 속도입니다.</summary>
    public float SprintSpeed => m_sprintSpeed;
    /// <summary>이동 방향을 바라보는 회전 보간 시간입니다.</summary>
    public float RotationSmoothTime => m_rotationSmoothTime;
    /// <summary>가속과 감속 반응 속도입니다.</summary>
    public float SpeedChangeRate => m_speedChangeRate;

    /// <summary>점프 높이입니다.</summary>
    public float JumpHeight => m_jumpHeight;
    /// <summary>캐릭터에 적용되는 중력 값입니다.</summary>
    public float Gravity => m_gravity;
    /// <summary>점프 재입력 제한 시간입니다.</summary>
    public float JumpTimeout => m_jumpTimeout;
    /// <summary>낙하 상태 전환 지연 시간입니다.</summary>
    public float FallTimeout => m_fallTimeout;

    /// <summary>현재 지면 접촉 상태입니다.</summary>
    public bool Grounded => m_grounded;
    /// <summary>지면 감지 위치의 Y축 오프셋입니다.</summary>
    public float GroundedOffset => m_groundedOffset;
    /// <summary>지면 감지 구체의 반지름입니다.</summary>
    public float GroundedRadius => m_groundedRadius;
    /// <summary>지면으로 판정할 레이어 마스크입니다.</summary>
    public LayerMask GroundLayers => m_groundLayers;

    /// <summary>Cinemachine 카메라가 따라갈 타겟 오브젝트입니다.</summary>
    public GameObject CinemachineCameraTarget => m_cinemachineCameraTarget;
    /// <summary>카메라 상단 회전 제한 각도입니다.</summary>
    public float TopClamp => m_topClamp;
    /// <summary>카메라 하단 회전 제한 각도입니다.</summary>
    public float BottomClamp => m_bottomClamp;
    /// <summary>카메라 회전에 추가 적용할 보정 각도입니다.</summary>
    public float CameraAngleOverride => m_cameraAngleOverride;
    /// <summary>카메라 회전 잠금 여부입니다.</summary>
    public bool LockCameraPosition => m_lockCameraPosition;

    /// <summary>착지 애니메이션 이벤트에서 재생할 오디오 클립입니다.</summary>
    public AudioClip LandingAudioClip => m_landingAudioClip;
    /// <summary>발걸음 애니메이션 이벤트에서 임의 선택할 오디오 클립 배열입니다.</summary>
    public AudioClip[] FootstepAudioClips => m_footstepAudioClips;
    /// <summary>발걸음과 착지 효과음 재생 볼륨입니다.</summary>
    public float FootstepAudioVolume => m_footstepAudioVolume;

    /// <summary>현재 조준 이동 상태인지 여부입니다.</summary>
    public bool IsAimMove => m_isAimMove;
    /// <summary>현재 재장전 상태인지 여부입니다.</summary>
    public bool IsReload => m_isReload;

    /// <summary>현재 입력 장치가 마우스 기반인지 여부입니다.</summary>
    private bool IsCurrentDeviceMouse
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return m_playerInput != null && m_playerInput.currentControlScheme == "KeyboardMouse";
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// 기본 이동 속도를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetMoveSpeed(float value) => m_moveSpeed = Mathf.Max(0.0f, value);
    /// <summary>
    /// 전력질주 속도를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetSprintSpeed(float value) => m_sprintSpeed = Mathf.Max(0.0f, value);
    /// <summary>
    /// 회전 보간 시간을 설정합니다. 0에서 0.3 사이로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetRotationSmoothTime(float value) => m_rotationSmoothTime = Mathf.Clamp(value, 0.0f, 0.3f);
    /// <summary>
    /// 가속과 감속 반응 속도를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetSpeedChangeRate(float value) => m_speedChangeRate = Mathf.Max(0.0f, value);

    /// <summary>
    /// 점프 높이를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetJumpHeight(float value) => m_jumpHeight = Mathf.Max(0.0f, value);
    /// <summary>
    /// 중력 값을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGravity(float value) => m_gravity = value;
    /// <summary>
    /// 점프 재입력 제한 시간을 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetJumpTimeout(float value) => m_jumpTimeout = Mathf.Max(0.0f, value);
    /// <summary>
    /// 낙하 상태 전환 지연 시간을 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetFallTimeout(float value) => m_fallTimeout = Mathf.Max(0.0f, value);

    /// <summary>
    /// 지면 감지 위치의 Y축 오프셋을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGroundedOffset(float value) => m_groundedOffset = value;
    /// <summary>
    /// 지면 감지 구체의 반지름을 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGroundedRadius(float value) => m_groundedRadius = Mathf.Max(0.0f, value);
    /// <summary>
    /// 지면으로 판정할 레이어 마스크를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGroundLayers(LayerMask value) => m_groundLayers = value;

    /// <summary>
    /// Cinemachine 카메라 타겟 오브젝트를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetCinemachineCameraTarget(GameObject value) => m_cinemachineCameraTarget = value;

    /// <summary>
    /// 카메라 타겟의 월드 회전과 내부 yaw/pitch 값을 지정한 회전에 맞춥니다.
    /// </summary>
    /// <param name="worldRotation">적용할 월드 회전입니다.</param>
    public void SyncCameraTargetRotation(Quaternion worldRotation)
    {
        if (m_cinemachineCameraTarget == null)
        {
            return;
        }

        Vector3 eulerAngles = worldRotation.eulerAngles;
        float pitch = eulerAngles.x - m_cameraAngleOverride;

        if (pitch > 180.0f)
        {
            pitch -= 360.0f;
        }

        m_cinemachineTargetYaw = eulerAngles.y;
        m_cinemachineTargetPitch = ClampAngle(pitch, m_bottomClamp, m_topClamp);

        m_cinemachineCameraTarget.transform.rotation = Quaternion.Euler(
            m_cinemachineTargetPitch + m_cameraAngleOverride,
            m_cinemachineTargetYaw,
            0.0f);
    }
    /// <summary>
    /// 카메라 상단 회전 제한 각도를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetTopClamp(float value) => m_topClamp = value;
    /// <summary>
    /// 카메라 하단 회전 제한 각도를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetBottomClamp(float value) => m_bottomClamp = value;
    /// <summary>
    /// 카메라 회전 보정 각도를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetCameraAngleOverride(float value) => m_cameraAngleOverride = value;
    /// <summary>
    /// 카메라 회전 잠금 여부를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetLockCameraPosition(bool value) => m_lockCameraPosition = value;

    /// <summary>
    /// 착지 효과음 클립을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetLandingAudioClip(AudioClip value) => m_landingAudioClip = value;
    /// <summary>
    /// 발걸음 효과음 클립 배열을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetFootstepAudioClips(AudioClip[] value) => m_footstepAudioClips = value;
    /// <summary>
    /// 효과음 볼륨을 설정합니다. 0에서 1 사이로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetFootstepAudioVolume(float value) => m_footstepAudioVolume = Mathf.Clamp01(value);

    /// <summary>
    /// 조준 이동 상태를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetAimMove(bool value) => m_isAimMove = value;
    /// <summary>
    /// 재장전 상태를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetReload(bool value) => m_isReload = value;

    /// <summary>
    /// 현재 이동 블렌드 상태를 전환 유지용으로 캡처합니다.
    /// </summary>
    /// <returns>이동 블렌드 상태입니다.</returns>
    public LocomotionCarryoverState CaptureLocomotionCarryoverState()
    {
        float motionSpeed = 1.0f;

        if (m_input != null)
        {
            motionSpeed = m_input.analogMovement ? m_input.move.magnitude : 1.0f;
        }

        bool jump = false;
        bool freeFall = false;

        if (m_animator == null)
        {
            m_hasAnimator = TryGetComponent(out m_animator);
        }
        else
        {
            m_hasAnimator = true;
        }

        AssignAnimationIDs();

        if (m_hasAnimator)
        {
            jump = m_animator.GetBool(m_animIDJump);
            freeFall = m_animator.GetBool(m_animIDFreeFall);
        }

        bool grounded = m_grounded && m_verticalVelocity <= 0.0f && !jump && !freeFall;

        return new LocomotionCarryoverState
        {
            HasState = true,
            Speed = m_speed,
            AnimationBlend = m_animationBlend,
            MotionSpeed = motionSpeed,
            TargetRotation = m_targetRotation,
            RotationVelocity = m_rotationVelocity,
            VerticalVelocity = m_verticalVelocity,
            JumpTimeoutDelta = m_jumpTimeoutDelta,
            FallTimeoutDelta = m_fallTimeoutDelta,
            Grounded = grounded,
            Jump = jump,
            FreeFall = freeFall,
        };
    }

    /// <summary>
    /// 전환 직전 캡처한 이동 블렌드 상태를 현재 컨트롤러와 Animator에 적용합니다.
    /// </summary>
    /// <param name="state">적용할 이동 블렌드 상태입니다.</param>
    public void ApplyLocomotionCarryoverState(LocomotionCarryoverState state)
    {
        if (!state.HasState)
        {
            return;
        }

        AssignAnimationIDs();

        m_speed = Mathf.Max(0.0f, state.Speed);
        m_animationBlend = Mathf.Max(0.0f, state.AnimationBlend);
        m_targetRotation = state.TargetRotation;
        m_rotationVelocity = state.RotationVelocity;
        m_verticalVelocity = state.VerticalVelocity;
        m_jumpTimeoutDelta = state.JumpTimeoutDelta;
        m_fallTimeoutDelta = state.FallTimeoutDelta;
        m_grounded = state.Grounded;

        if (m_animator == null)
        {
            m_hasAnimator = TryGetComponent(out m_animator);
        }
        else
        {
            m_hasAnimator = true;
        }

        if (m_hasAnimator)
        {
            m_animator.SetFloat(m_animIDSpeed, m_animationBlend);
            m_animator.SetFloat(m_animIDMotionSpeed, state.MotionSpeed);
            m_animator.SetBool(m_animIDGrounded, state.Grounded);
            m_animator.SetBool(m_animIDJump, state.Jump);
            m_animator.SetBool(m_animIDFreeFall, state.FreeFall);
        }
    }

    /// <summary>
    /// AI 제어로 전환될 때 플레이어 조작 중 남은 점프/낙하 상태를 정리합니다.
    /// </summary>
    public void ClearAirborneCarryoverState()
    {
        AssignAnimationIDs();

        m_verticalVelocity = -2.0f;
        m_jumpTimeoutDelta = m_jumpTimeout;
        m_fallTimeoutDelta = m_fallTimeout;
        m_grounded = true;

        if (m_input != null)
        {
            m_input.jump = false;
        }

        if (m_animator == null)
        {
            m_hasAnimator = TryGetComponent(out m_animator);
        }
        else
        {
            m_hasAnimator = true;
        }

        if (m_hasAnimator)
        {
            m_animator.SetFloat(m_animIDSpeed, 0.0f);
            m_animator.SetFloat(m_animIDMotionSpeed, 1.0f);
            m_animator.SetBool(m_animIDGrounded, true);
            m_animator.SetBool(m_animIDJump, false);
            m_animator.SetBool(m_animIDFreeFall, false);
            m_animator.Play(GroundedLocomotionStateHash, 0, 0.0f);
            m_animator.Update(0.0f);
        }
    }

    /// <summary>
    /// 필수 컴포넌트와 외부 참조를 캐싱하고 누락 여부를 검증합니다.
    /// </summary>
    private void Awake()
    {
        CacheRequiredReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        m_hasRequiredReferences = true;
    }

    /// <summary>
    /// 카메라 초기 회전값, Animator 파라미터 해시, 점프/낙하 타이머 초기값을 설정합니다.
    /// </summary>
    private void Start()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        if (m_cinemachineCameraTarget != null)
        {
            m_cinemachineTargetYaw = m_cinemachineCameraTarget.transform.rotation.eulerAngles.y;
        }
        else
        {
            Debug.LogWarning("[ThirdPersonController] CinemachineCameraTarget이 비어 있습니다. 카메라 회전 타겟 갱신은 생략됩니다.", this);
        }

        AssignAnimationIDs();

        m_jumpTimeoutDelta = m_jumpTimeout;
        m_fallTimeoutDelta = m_fallTimeout;
    }

    /// <summary>
    /// 매 프레임 점프/중력, 지면 감지, 이동 처리를 수행합니다.
    /// </summary>
    private void Update()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        m_hasAnimator = TryGetComponent(out m_animator);

        JumpAndGravity();
        GroundedCheck();
        Move();
    }

    /// <summary>
    /// 모든 Update 처리 이후 카메라 회전을 갱신합니다.
    /// </summary>
    private void LateUpdate()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        CameraRotation();
    }

    /// <summary>
    /// 현재 GameObject와 씬에서 필요한 참조를 캐싱합니다.
    /// </summary>
    private void CacheRequiredReferences()
    {
        if (m_mainCamera == null)
        {
            m_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
        }

        m_hasAnimator = TryGetComponent(out m_animator);
        m_controller = GetComponent<CharacterController>();
        m_input = GetComponent<PlayerInputs>();
#if ENABLE_INPUT_SYSTEM
        m_playerInput = GetComponent<PlayerInput>();
#else
        Debug.LogError("Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it", this);
#endif
    }

    /// <summary>
    /// 캐싱된 필수 참조가 모두 유효한지 검사합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 존재하면 true, 하나라도 누락되면 false입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_mainCamera == null)
        {
            Debug.LogError("[ThirdPersonController] MainCamera 태그를 가진 카메라를 찾지 못했습니다. 씬의 메인 카메라에 MainCamera 태그를 지정하세요.", this);
            isValid = false;
        }

        if (m_controller == null)
        {
            Debug.LogError("[ThirdPersonController] CharacterController 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        if (m_input == null)
        {
            Debug.LogError("[ThirdPersonController] PlayerInputs 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }
#if ENABLE_INPUT_SYSTEM
        if (m_playerInput == null)
        {
            Debug.LogError("[ThirdPersonController] PlayerInput 컴포넌트가 없습니다.", this);
            isValid = false;
        }
#endif

        return isValid;
    }

    /// <summary>
    /// Animator 파라미터 이름을 해시값으로 변환해 캐싱합니다.
    /// </summary>
    private void AssignAnimationIDs()
    {
        m_animIDSpeed = Animator.StringToHash("Speed");
        m_animIDGrounded = Animator.StringToHash("Grounded");
        m_animIDJump = Animator.StringToHash("Jump");
        m_animIDFreeFall = Animator.StringToHash("FreeFall");
        m_animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
    }

    /// <summary>
    /// 캐릭터 발밑에 지면이 있는지 구체 검사로 확인하고 Animator에 반영합니다.
    /// </summary>
    private void GroundedCheck()
    {
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - m_groundedOffset,
            transform.position.z);
        m_grounded = Physics.CheckSphere(spherePosition, m_groundedRadius, m_groundLayers,
            QueryTriggerInteraction.Ignore);

        if (m_hasAnimator)
        {
            m_animator.SetBool(m_animIDGrounded, m_grounded);
        }
    }

    /// <summary>
    /// 입력값을 바탕으로 카메라 타겟의 yaw/pitch 회전을 갱신합니다.
    /// </summary>
    private void CameraRotation()
    {
        if (m_input.look.sqrMagnitude >= Threshold && !m_lockCameraPosition)
        {
            float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

            m_cinemachineTargetYaw += m_input.look.x * deltaTimeMultiplier;
            m_cinemachineTargetPitch += m_input.look.y * deltaTimeMultiplier;
        }

        m_cinemachineTargetYaw = ClampAngle(m_cinemachineTargetYaw, float.MinValue, float.MaxValue);
        m_cinemachineTargetPitch = ClampAngle(m_cinemachineTargetPitch, m_bottomClamp, m_topClamp);

        if (m_cinemachineCameraTarget != null)
        {
            m_cinemachineCameraTarget.transform.rotation = Quaternion.Euler(
                m_cinemachineTargetPitch + m_cameraAngleOverride,
                m_cinemachineTargetYaw,
                0.0f);
        }
    }

    /// <summary>
    /// 입력 상태, 전력질주 상태, 조준/재장전 상태를 반영해 캐릭터를 이동 및 회전시킵니다.
    /// </summary>
    private void Move()
    {
        float targetSpeed = m_input.sprint ? m_sprintSpeed : m_moveSpeed;

        if (m_isAimMove || m_isReload)
        {
            targetSpeed = m_moveSpeed;
        }

        if (m_input.move == Vector2.zero)
        {
            targetSpeed = 0.0f;
        }

        float currentHorizontalSpeed = new Vector3(m_controller.velocity.x, 0.0f, m_controller.velocity.z).magnitude;

        float speedOffset = 0.1f;
        float inputMagnitude = m_input.analogMovement ? m_input.move.magnitude : 1f;

        if (currentHorizontalSpeed < targetSpeed - speedOffset ||
            currentHorizontalSpeed > targetSpeed + speedOffset)
        {
            m_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                Time.deltaTime * m_speedChangeRate);

            m_speed = Mathf.Round(m_speed * 1000f) / 1000f;
        }
        else
        {
            m_speed = targetSpeed;
        }

        m_animationBlend = Mathf.Lerp(m_animationBlend, targetSpeed, Time.deltaTime * m_speedChangeRate);
        if (m_animationBlend < 0.01f)
        {
            m_animationBlend = 0f;
        }

        Vector3 inputDirection = new Vector3(m_input.move.x, 0.0f, m_input.move.y).normalized;

        if (m_input.move != Vector2.zero)
        {
            m_targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                               m_mainCamera.transform.eulerAngles.y;
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, m_targetRotation, ref m_rotationVelocity,
                m_rotationSmoothTime);

            if (!m_isAimMove)
            {
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }
        }

        Vector3 targetDirection = Quaternion.Euler(0.0f, m_targetRotation, 0.0f) * Vector3.forward;

        m_controller.Move(targetDirection.normalized * (m_speed * Time.deltaTime) +
                          new Vector3(0.0f, m_verticalVelocity, 0.0f) * Time.deltaTime);

        if (m_hasAnimator)
        {
            m_animator.SetFloat(m_animIDSpeed, m_animationBlend);
            m_animator.SetFloat(m_animIDMotionSpeed, inputMagnitude);
        }
    }

    /// <summary>
    /// 지면 상태에 따라 점프, 낙하, 중력, 관련 애니메이션 파라미터를 갱신합니다.
    /// </summary>
    private void JumpAndGravity()
    {
        if (m_grounded)
        {
            m_fallTimeoutDelta = m_fallTimeout;

            if (m_hasAnimator)
            {
                m_animator.SetBool(m_animIDJump, false);
                m_animator.SetBool(m_animIDFreeFall, false);
            }

            if (m_verticalVelocity < 0.0f)
            {
                m_verticalVelocity = -2f;
            }

            if (m_input.jump && m_jumpTimeoutDelta <= 0.0f)
            {
                m_verticalVelocity = Mathf.Sqrt(m_jumpHeight * -2f * m_gravity);

                if (m_hasAnimator)
                {
                    m_animator.SetBool(m_animIDJump, true);
                }
            }

            if (m_jumpTimeoutDelta >= 0.0f)
            {
                m_jumpTimeoutDelta -= Time.deltaTime;
            }
        }
        else
        {
            m_jumpTimeoutDelta = m_jumpTimeout;

            if (m_fallTimeoutDelta >= 0.0f)
            {
                m_fallTimeoutDelta -= Time.deltaTime;
            }
            else
            {
                if (m_hasAnimator)
                {
                    m_animator.SetBool(m_animIDFreeFall, true);
                }
            }

            m_input.jump = false;
        }

        if (m_verticalVelocity < m_terminalVelocity)
        {
            m_verticalVelocity += m_gravity * Time.deltaTime;
        }
    }

    /// <summary>
    /// 각도를 -360도에서 360도 범위로 정규화한 뒤 지정 범위로 제한합니다.
    /// </summary>
    /// <param name="lfAngle">제한할 각도입니다.</param>
    /// <param name="lfMin">최소 각도입니다.</param>
    /// <param name="lfMax">최대 각도입니다.</param>
    /// <returns>지정 범위로 제한된 각도입니다.</returns>
    private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
    {
        if (lfAngle < -360f) lfAngle += 360f;
        if (lfAngle > 360f) lfAngle -= 360f;
        return Mathf.Clamp(lfAngle, lfMin, lfMax);
    }

    /// <summary>
    /// 에디터에서 선택되었을 때 지면 감지 구체를 기즈모로 표시합니다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
        Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

        Gizmos.color = m_grounded ? transparentGreen : transparentRed;

        Gizmos.DrawSphere(
            new Vector3(transform.position.x, transform.position.y - m_groundedOffset, transform.position.z),
            m_groundedRadius);
    }

    /// <summary>
    /// 발걸음 애니메이션 이벤트에서 임의의 발소리 클립을 재생합니다.
    /// </summary>
    /// <param name="animationEvent">애니메이션 이벤트 정보입니다.</param>
    private void OnFootstep(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight > 0.5f)
        {
            if (m_footstepAudioClips != null && m_footstepAudioClips.Length > 0)
            {
                int index = Random.Range(0, m_footstepAudioClips.Length);
                AudioSource.PlayClipAtPoint(m_footstepAudioClips[index], transform.TransformPoint(m_controller.center), m_footstepAudioVolume);
            }
        }
    }

    /// <summary>
    /// 착지 애니메이션 이벤트에서 착지 효과음을 재생합니다.
    /// </summary>
    /// <param name="animationEvent">애니메이션 이벤트 정보입니다.</param>
    private void OnLand(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight > 0.5f && m_landingAudioClip != null)
        {
            AudioSource.PlayClipAtPoint(m_landingAudioClip, transform.TransformPoint(m_controller.center), m_footstepAudioVolume);
        }
    }
}
