using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// AI가 조작하는 스쿼드 멤버의 행동을 판단하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// <see cref="PlayerInputController"/>의 대칭 짝입니다. 사람 입력이 그쪽으로 들어오듯 AI 판단이 이쪽으로 들어오며,
/// 조종 주체만 다르고 아래 계층(행동 판정과 실행)은 같은 것을 씁니다.
/// 어느 쪽이 활성인지는 <see cref="SquadMemberController"/>가 정합니다.
/// <para>
/// <b>목표 경계</b>: 이 컴포넌트는 "무엇을 할지"만 정하고 캐릭터를 직접 움직이지 않습니다.
/// 실제 이동·회전·애니메이션 실행은 <see cref="ThirdPersonController"/>가 담당합니다.
/// </para>
/// <para>
/// <b>현재는 예외</b>: 아직 이동 실행 통합 전이라 여기서 <see cref="NavMeshAgent"/>로 직접 이동하고
/// 회전과 애니메이터 파라미터까지 갱신합니다. 이 부분은 이동 통합 트랙에서 걷어낼 임시 코드이며,
/// 그때 NavMesh는 경로 계산 전용으로 남고 실행은 <see cref="ThirdPersonController"/>로 넘어갑니다.
/// </para>
/// 기획 용어로는 `추종`이 아니라 `동행`과 `합류`를 씁니다(공용 문서 `스쿼드 AI 시스템` §5, §12).
/// </remarks>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(SquadMemberController))]
public class SquadAIController : MonoBehaviour
{
    /// <summary>
    /// 멤버 전환 시 이어받을 AI 추종 상태입니다.
    /// </summary>
    public struct FollowCarryoverState
    {
        public bool HasState;
        public float NextUpdateDelay;
        public bool AgentEnabled;
        public bool IsStopped;
        public bool HasPath;
        public Vector3 Position;
        public Vector3 Destination;
        public Vector3 Velocity;
    }

    [Foldout("Follow Options")]
    [Tooltip("플레이어까지 경로상 거리가 이 값을 넘으면 합류를 시작하고 달립니다. 완료 거리와 충분히 벌려야 걷는 구간이 생깁니다.")]
    [FormerlySerializedAs("followDistance")]
    [SerializeField] private float m_joinStartDistance = 7.0f;

    [Tooltip("플레이어까지 경로상 거리가 이 값 이하로 들어오면 합류를 끝내고 달리기를 멈춥니다. 합류 목적지도 이 반경 안에서 고릅니다.")]
    [FormerlySerializedAs("stopDistance")]
    [SerializeField] private float m_joinCompleteDistance = 2.0f;

    [Tooltip("합류가 아닌 평상시 동행 속도를 플레이어 걷기 속도의 몇 배로 할지입니다. 1보다 커야 걷는 플레이어를 따라잡아 거리가 유지됩니다.")]
    [SerializeField] private float m_followSpeedMultiplier = 1.15f;

    [Tooltip("목표 위치를 다시 계산하는 주기입니다.")]
    [FormerlySerializedAs("updateInterval")]
    [SerializeField] private float m_updateInterval = 0.15f;

    [Tooltip("합류 목적지 후보를 플레이어 주위 몇 방향에서 뽑을지입니다. 고정 자리가 아니라 매번 가장 가까운 빈 자리를 고르기 위한 표본 수입니다.")]
    [SerializeField] private int m_destinationCandidateCount = 8;

    [Tooltip("합류 목적지가 다른 캐릭터와 이 거리 안이면 겹친 것으로 보고 후보에서 제외합니다.")]
    [SerializeField] private float m_destinationClearance = 0.9f;

    [Tooltip("동행 AI의 회피 우선순위 기준값입니다. 멤버 순번을 더해 서로 다른 값을 씁니다. 낮을수록 우선합니다.")]
    [SerializeField] private int m_baseAvoidancePriority = 50;

    [Foldout("Sight Options")]
    [Tooltip("이 AI가 적을 직접 확인할 수 있는 최대 거리입니다.")]
    [SerializeField] private float m_sightRange = 20.0f;

    [Tooltip("전방을 기준으로 하는 전체 시야각입니다. 변이체 쪽 시야각과 같은 방식으로 씁니다.")]
    [SerializeField] private float m_sightAngle = 120.0f;

    [Tooltip("시야 판정의 시작점입니다. 비어 있으면 발밑에서 눈 높이만큼 올린 지점을 씁니다.")]
    [SerializeField] private Transform m_eyePoint;

    [Tooltip("시야 판정 시작점의 기본 높이입니다. Eye Point가 비어 있을 때만 씁니다.")]
    [SerializeField] private float m_eyeHeight = 1.6f;

    [Foldout("Targeting Options")]
    [Tooltip("후보 전체를 다시 비교하는 주기입니다. 짧으면 대상이 자주 바뀌고, 길면 상황 변화에 늦게 반응합니다.")]
    [SerializeField] private float m_reevaluateInterval = 0.5f;

    [Tooltip("적이 안 보이게 돼도 현재 대상으로 붙잡아 두는 시간입니다. 이 동안은 마지막 확인 위치를 겨누되 쏘지는 않습니다.")]
    [SerializeField] private float m_targetHoldGrace = 5.0f;

    [Tooltip("거리 위협도의 최대값입니다. 적이 붙어 있을수록 이 값에 가까워집니다.")]
    [SerializeField] private float m_distanceThreatWeight = 10.0f;

    [Tooltip("거리 위협도가 0이 되는 거리입니다. 이보다 멀면 거리로는 점수를 얻지 못합니다.")]
    [SerializeField] private float m_distanceThreatFalloff = 30.0f;

    [Tooltip("현재 대상에게만 얹는 유지 보정입니다. 클수록 대상을 잘 바꾸지 않습니다.")]
    [SerializeField] private float m_currentTargetBonus = 3.0f;

    [Tooltip("받은 피해 1당 위협도 환산값입니다. 나를 때린 적을 우선하게 만드는 값입니다.")]
    [SerializeField] private float m_damageThreatPerPoint = 0.5f;

    [Tooltip("피해 위협도가 초당 줄어드는 양입니다. 맞은 지 오래되면 다시 거리 위주로 판단합니다.")]
    [SerializeField] private float m_damageThreatDecayPerSecond = 1.0f;

    [Tooltip("사격선 가림이 이 시간을 넘으면 지속적인 가림으로 보고 후보군을 낮춥니다. 짧으면 동료가 스칠 때마다 대상이 바뀝니다.")]
    [SerializeField] private float m_sustainedBlockDuration = 1.0f;

    [Tooltip("아군이 사격선에서 이 거리 안에 있으면 가린 것으로 봅니다. 크면 동료 근처만 가도 사격을 멈춥니다.")]
    [SerializeField] private float m_allyBlockRadius = 0.6f;

    [Tooltip("새 대상을 잡고 실제로 쏘기까지 기다리는 시간입니다. AI가 즉발로 반응하지 않게 만드는 값입니다.")]
    [SerializeField] private float m_reactionTime = 0.35f;

    [Foldout("Aim Options")]
    [Tooltip("대상을 향해 몸을 돌리는 속도입니다. 이동 방향 회전 속도와 따로 둡니다.")]
    [SerializeField] private float m_aimRotationSpeed = 12.0f;

    [Tooltip("대상 방향과 몸 방향의 각도 차가 이 값 이하이면 조준이 맞았다고 봅니다.")]
    [SerializeField] private float m_aimToleranceAngle = 8.0f;

    [Foldout("Fire Options")]
    [Tooltip("한 번 사격을 시작하면 이 시간 동안 계속 쏩니다. 무기의 점사 모드가 아니라 AI의 사격 제어입니다.")]
    [SerializeField] private float m_burstDuration = 0.8f;

    [Tooltip("사격 구간이 끝난 뒤 쉬는 시간입니다. 이 동안 조준은 유지하고 발사만 멈춥니다.")]
    [SerializeField] private float m_burstRestDuration = 0.5f;

    [Tooltip("겨눈 지점에서 좌우로 벌어지는 조준 오차입니다. AI가 기계처럼 정확하지 않게 만드는 값입니다.")]
    [SerializeField] private float m_aimErrorRadius = 0.35f;

    [Tooltip("탄창 잔탄이 이 값 이하일 때 전술 재장전을 검토합니다. 0이면 탄창이 완전히 빌 때만 재장전합니다.")]
    [SerializeField] private int m_tacticalReloadThreshold = 0;

    [Foldout("Combat Position Options")]
    [Tooltip("현재 대상이 이 거리 안으로 붙으면 뒤로 물러나며 사격합니다. 물러날 자리가 없으면 제자리에서 쏩니다.")]
    [SerializeField] private float m_keepAwayDistance = 3.0f;

    [Tooltip("한 번 물러날 때 목표로 삼는 거리입니다. 너무 크면 플레이어에게서 떨어져 곧 합류로 넘어갑니다.")]
    [SerializeField] private float m_retreatStep = 2.5f;

    [Tooltip("전투 위치를 다시 찾는 주기입니다. 짧으면 사격선이 잠깐 막힐 때마다 자리를 옮겨 산만해집니다.")]
    [SerializeField] private float m_combatPositionInterval = 0.4f;

    [Foldout("Move Options")]
    [Tooltip("이동 방향으로 회전하는 속도입니다.")]
    [FormerlySerializedAs("rotationSpeed")]
    [SerializeField] private float m_rotationSpeed = 10f;

    [Tooltip("걷기와 달리기 자세를 오가는 데 걸리는 시간입니다. ThirdPersonController의 같은 값과 맞춰야 조작 전환 시 자세가 튀지 않습니다.")]
    [SerializeField] private float m_moveStateBlendDuration = 0.2f;

    [Foldout("Debug")]
    [Tooltip("끄면 이 AI가 발사하지 않습니다. 대상 선정과 조준은 그대로 하므로 겨누기만 하고 쏘지 않습니다. 재장전도 함께 멈춥니다.")]
    [SerializeField] private bool m_debugAllowFiring = true;

    [Tooltip("이 동행 AI를 선택했을 때 합류 시작 거리와 합류 완료 거리를 리더 기준 원 두 개로 표시합니다. 리더가 없으면 그리지 않습니다.")]
    [SerializeField] private bool m_debugDrawJoinDistances = false;

    private SquadMemberController m_memberController;
    private NavMeshAgent m_agent;
    private SquadManager m_squadManager;
    private Animator m_animator;

    private float m_nextUpdateTime;
    private bool m_hasRequiredReferences;

    // 합류 중인지입니다. 시작 거리에서 켜고 완료 거리에서 끄는 히스테리시스의 한쪽입니다(§6.2).
    // 하나의 거리로 판정하면 경계에서 합류 시작과 종료가 매 갱신마다 뒤집힙니다.
    private bool m_isJoining;

    // 마지막으로 확정한 동행 위치입니다. 갱신 주기 사이에도 도착 판정에 쓰므로 들고 있습니다.
    private Vector3 m_destination;
    private bool m_hasDestination;

    // 플레이어까지의 경로상 거리입니다. 직선거리를 쓰면 벽 하나 사이에 두고 "가깝다"고 오판합니다(§6.2).
    private float m_pathDistanceToLeader = -1.0f;

    // 경로 계산 버퍼입니다. 매번 새로 만들면 갱신마다 할당이 생깁니다.
    private NavMeshPath m_pathBuffer;

    // 리더가 바뀔 때만 다시 찾습니다. 속도 기준을 리더에게서 읽기 위한 캐시입니다.
    private Transform m_cachedLeaderTransform;
    private ThirdPersonController m_cachedLeaderController;

    // 지금 달려서 따라잡는 중인지입니다. 이동 속도와 자세(MoveState)가 같은 값을 봐야 어긋나지 않습니다.
    private bool m_isSprinting;

    // 애니메이터에 넣고 있는 MoveState 값입니다. 목표 단계로 보간합니다.
    private float m_moveState = MoveStateWalk;

    // 현재 대상 판단입니다. 이동과 별개 축이라 따로 둡니다(§9).
    private readonly SquadAITargeting m_targeting = new SquadAITargeting();

    // 이번 프레임에 무엇을 요청할지 정하는 판정입니다(§18.1). 실행 함수들은 이 결과만 봅니다.
    private readonly SquadAIDecision m_decision = new SquadAIDecision();

    // 마지막으로 확정한 행동 요청입니다. 실행과 진단이 같은 값을 봐야 어긋나지 않습니다.
    private SquadAIDecision.Result m_currentDecision;

    // 이번 프레임에 겨눌 지점입니다. 유예 중에는 마지막 확인 위치입니다(§8.5).
    private Vector3 m_aimPoint;
    private bool m_hasAimPoint;

    // 자동 사격의 유지·휴지 구간 상태입니다(§11.2). 무기의 점사 모드가 아니라 AI 사격 제어입니다.
    private bool m_isBursting;
    private float m_burstPhaseEndTime;

    // 이번 사격 구간에 쓸 조준 오차입니다. 발마다 흔들면 탄퍼짐과 겹쳐 과해집니다.
    private Vector3 m_aimErrorOffset;

    // 마지막으로 실제 발사한 시각입니다. 연사 중 반동 레이어가 발 사이마다 끊기지 않게 하는 데 씁니다.
    private float m_lastShotTime = -999.0f;

    // 사격 자세를 유지할 시간입니다. 발사 간격보다 넉넉해야 연사 중 반동이 이어집니다.
    private const float ShootingVisualHold = 0.25f;

    // LookY를 -1~1로 정규화할 때 쓰는 최대 상하 각도입니다. ThirdPersonController의 카메라 피치 범위와 맞춥니다.
    private const float MaxLookPitch = 70.0f;

    // 상체 조준 오프셋 파라미터의 감쇠 시간입니다. 즉시 바꾸면 상체가 튑니다.
    private const float LookDamp = 0.1f;

    // 상체 조준 리그를 소유한 컴포넌트입니다. AI에서는 꺼져 있고 공개 진입점만 부릅니다.
    private AimController m_aimController;

    // 전투 위치 상태입니다. 주기 사이에 자리가 흔들리지 않도록 들고 있습니다(§10.2).
    private Vector3 m_combatPosition;
    private bool m_hasCombatPosition;
    private float m_nextCombatPositionTime;

    /// <summary>전투 위치 후보를 몇 방향에서 뽑을지입니다.</summary>
    private const int CombatPositionCandidateCount = 7;

    /// <summary>후퇴 후보를 좌우로 벌리는 각도 간격입니다.</summary>
    private const float RetreatAngleStep = 30.0f;

    // 사격선 판정에 쓸 무기입니다. 사거리를 여기서 읽습니다.
    private Gun m_weapon;

    private static readonly int SpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");

    // 아래 파라미터는 ThirdPersonController가 조작 중인 멤버에게 채우는 것과 같은 묶음입니다.
    // AI 멤버는 ThirdPersonController가 꺼져 있어(SquadMemberController가 직접 조작 멤버만 켭니다)
    // 여기서 채우지 않으면 값이 초기값에 머물러 애니메이션이 엉킵니다.
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
    private static readonly int MoveStateHash = Animator.StringToHash("MoveState");
    private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
    private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int JumpHash = Animator.StringToHash("IsJump");
    private static readonly int FreeFallHash = Animator.StringToHash("IsFreeFall");

    // 조준 자세 파라미터입니다. 조작 멤버는 ThirdPersonController가 채우지만 AI 멤버는 그것도 꺼져 있어
    // 여기서 채워야 조준 포즈가 나옵니다.
    private static readonly int AimHash = Animator.StringToHash("IsAim");

    // 상체 조준 오프셋 파라미터입니다. 조작 멤버는 ThirdPersonController가 카메라 각도로 채웁니다.
    // AI 멤버에서 비워 두면 조작하던 시절의 플레이어 값이 그대로 남아 상체가 엉뚱하게 꺾입니다.
    private static readonly int LookXHash = Animator.StringToHash("LookX");
    private static readonly int LookYHash = Animator.StringToHash("LookY");

    /// <summary>이동 방향을 유효한 방향으로 볼 최소 수평 속력입니다.</summary>
    private const float MoveDirectionThreshold = 0.01f;

    /// <summary>MoveState 축의 걷기 단계 값입니다.</summary>
    private const float MoveStateWalk = 1.0f;

    /// <summary>MoveState 축의 달리기 단계 값입니다.</summary>
    /// <remarks><see cref="ThirdPersonController"/>와 같은 축을 씁니다(0=웅크림, 1=걷기, 2=달리기).</remarks>
    private const float MoveStateRun = 2.0f;

    /// <summary>이동 방향 파라미터의 감쇠 시간입니다.</summary>
    private const float MoveDirectionDamp = 0.1f;

    /// <summary>목적지 후보를 NavMesh 위로 끌어당길 때 허용하는 반경입니다.</summary>
    private const float DestinationSampleRadius = 1.5f;

    /// <summary>목적지 도착 판정의 최소 거리입니다.</summary>
    /// <remarks>실제 판정 거리는 <see cref="ResolveArrivalDistance"/>가 Agent 정지 거리와 비교해 정합니다.</remarks>
    private const float DestinationArrivalEpsilon = 0.6f;

    /// <summary>Agent 정지 거리 위에 더 얹는 도착 판정 여유입니다.</summary>
    /// <remarks>정지 직후 미세하게 남는 관성과 위치 오차를 흡수합니다.</remarks>
    private const float ArrivalStopDistanceMargin = 0.35f;

    /// <summary>후보 고리의 반지름을 합류 완료 거리의 몇 배로 잡을지입니다.</summary>
    /// <remarks>
    /// 완료 거리와 같게 두면 후보가 경계에 걸려 도착하자마자 다시 벗어난 것으로 판정될 수 있습니다.
    /// 안쪽으로 조금 당겨 두면 도착 후 판정이 안정됩니다.
    /// </remarks>
    private const float DestinationRadiusRatio = 0.75f;

    /// <summary>합류를 시작하는 경로상 거리입니다.</summary>
    public float JoinStartDistance => m_joinStartDistance;

    /// <summary>합류를 끝내는 경로상 거리입니다.</summary>
    public float JoinCompleteDistance => m_joinCompleteDistance;

    /// <summary>목표 위치 갱신 주기입니다.</summary>
    public float UpdateInterval => m_updateInterval;

    /// <summary>이동 방향으로 회전하는 속도입니다.</summary>
    public float RotationSpeed => m_rotationSpeed;

    /// <summary>지금 합류 중인지 여부입니다.</summary>
    /// <remarks>
    /// 진단용입니다. 합류는 <see cref="JoinStartDistance"/>에서 켜지고 <see cref="JoinCompleteDistance"/>에서
    /// 꺼집니다. 이 값이 자주 뒤집히면 두 거리가 너무 가깝다는 뜻입니다(공용 문서 §6.2).
    /// </remarks>
    public bool IsJoining => m_isJoining;

    /// <summary>마지막으로 확정한 합류 목적지입니다. 확정 전에는 자기 위치를 돌려줍니다.</summary>
    public Vector3 CurrentDestination => m_hasDestination ? m_destination : transform.position;

    /// <summary>플레이어까지 마지막으로 측정한 경로상 거리입니다. 경로가 없으면 <c>-1</c>입니다.</summary>
    public float PathDistanceToLeader => m_pathDistanceToLeader;

    /// <summary>이 AI의 현재 대상 판단입니다(§9).</summary>
    /// <remarks>
    /// 무엇을 아는지는 스쿼드가 공유하지만(<see cref="SquadEnemyIntel"/>), 그중 누구를 칠지는
    /// AI마다 따로 정합니다. 컴포넌트가 아니라 이 컨트롤러가 소유하는 일반 객체입니다.
    /// </remarks>
    public SquadAITargeting Targeting => m_targeting;

    /// <summary>이번 프레임에 확정된 행동 요청입니다(§18.1).</summary>
    /// <remarks>
    /// 실행 함수들이 보는 것과 같은 값입니다. 어떤 우선순위 단계가 이 행동을 정했는지는
    /// <see cref="SquadAIDecision.Result.Step"/>에 문서 번호로 들어 있습니다.
    /// </remarks>
    public SquadAIDecision.Result CurrentDecision => m_currentDecision;

    /// <summary>이번 프레임에 겨누고 있는 지점입니다. 대상이 없으면 false입니다.</summary>
    /// <param name="point">겨누는 지점입니다. 유예 중에는 마지막 확인 위치입니다.</param>
    /// <returns>겨누는 중이면 true입니다.</returns>
    public bool TryGetCurrentAimPoint(out Vector3 point)
    {
        point = m_aimPoint;
        return m_hasAimPoint;
    }

    /// <summary>대상 방향과 몸 방향의 각도 차입니다. 겨누는 중이 아니면 -1입니다.</summary>
    /// <remarks>조준이 실제로 맞았는지 확인하는 진단값입니다.</remarks>
    public float AimAngleError
    {
        get
        {
            if (!m_hasAimPoint)
            {
                return -1.0f;
            }

            Vector3 delta = m_aimPoint - transform.position;
            delta.y = 0.0f;
            return delta.sqrMagnitude < 0.0001f ? 0.0f : Vector3.Angle(transform.forward, delta.normalized);
        }
    }

    /// <summary>조준이 허용 오차 안에 들어왔는지 여부입니다.</summary>
    public bool IsAimAligned => m_hasAimPoint && AimAngleError <= m_aimToleranceAngle;

    /// <summary>지금 사격 유지 구간인지 여부입니다. 휴지 구간이면 false입니다(§11.2).</summary>
    public bool IsBursting => m_isBursting;

    /// <summary>현재 대상이 이 거리 안으로 붙으면 물러납니다(§10.3).</summary>
    public float KeepAwayDistance => m_keepAwayDistance;

    /// <summary>지금 전투 위치로 이동 중인지 여부입니다.</summary>
    /// <remarks>진단용입니다. 거리 벌리기나 사격선 확보로 자리를 옮기는 중이면 true입니다.</remarks>
    public bool IsRepositioning => m_hasCombatPosition;

    /// <summary>이 AI의 발사 허용 여부입니다.</summary>
    /// <remarks>끄면 발사와 재장전만 멈추고 대상 선정·조준은 그대로 돕니다.</remarks>
    public bool AllowFiring
    {
        get => m_debugAllowFiring;
        set => m_debugAllowFiring = value;
    }

    /// <summary>이 AI가 적을 직접 확인할 수 있는 최대 거리입니다.</summary>
    public float SightRange => m_sightRange;

    /// <summary>이 AI의 전체 시야각입니다.</summary>
    public float SightAngle => m_sightAngle;

    /// <summary>
    /// 이 AI가 조작하는 캐릭터의 시야로 해당 지점을 볼 수 있는지 판정합니다(§8.3).
    /// </summary>
    /// <param name="point">확인할 지점입니다.</param>
    /// <param name="obstacleMask">시야를 가로막는 고정 환경 장애물 레이어입니다.</param>
    /// <returns>거리·시야각·차폐를 모두 통과하면 true입니다.</returns>
    /// <remarks>
    /// 판정 주체가 여기인 이유는 문서가 "AI는 <b>자신이 현재 조작하는 캐릭터</b>의 시점, 시야각과
    /// 시야 거리를 기준으로 확인한다"고 규정하기 때문입니다. 스쿼드 공용 정보는 결과만 받습니다.
    /// <para>
    /// 싼 검사부터 합니다. Raycast는 거리와 각도를 통과한 뒤에만 나갑니다.
    /// 변이체 쪽 <c>EnemyTargetSensor.CanSee</c>와 같은 순서·같은 방식이며, 전용 인식 콜라이더 대신
    /// 한 점을 겨누는 것도 동일합니다(기획 확정).
    /// </para>
    /// <para>
    /// 무엇을 볼지는 부르는 쪽이 정합니다. 이 함수는 "교전 중인 적만" 같은 §8.1 제약을 알지 못하므로
    /// 후보를 걸러 넘겨야 합니다.
    /// </para>
    /// </remarks>
    public bool CanSeePoint(Vector3 point, int obstacleMask)
    {
        Vector3 origin = m_eyePoint != null
            ? m_eyePoint.position
            : transform.position + Vector3.up * m_eyeHeight;

        Vector3 delta = point - origin;

        if (delta.sqrMagnitude > m_sightRange * m_sightRange)
        {
            return false;
        }

        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        Vector3 direction = delta / distance;
        if (Vector3.Angle(transform.forward, direction) > m_sightAngle * 0.5f)
        {
            return false;
        }

        return !Physics.Raycast(origin, direction, distance, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 현재 AI 추종 상태를 전환 유지용으로 캡처합니다.
    /// </summary>
    /// <returns>AI 추종 상태입니다.</returns>
    public FollowCarryoverState CaptureFollowCarryoverState()
    {
        if (m_agent == null)
        {
            return default;
        }

        bool canReadAgentPath = m_agent.enabled && m_agent.isOnNavMesh;

        return new FollowCarryoverState
        {
            HasState = true,
            NextUpdateDelay = Mathf.Max(0.0f, m_nextUpdateTime - Time.time),
            AgentEnabled = m_agent.enabled,
            IsStopped = !m_agent.enabled || m_agent.isStopped,
            HasPath = canReadAgentPath && m_agent.hasPath,
            Position = transform.position,
            Destination = canReadAgentPath ? m_agent.destination : transform.position,
            Velocity = m_agent.enabled ? m_agent.velocity : Vector3.zero,
        };
    }

    /// <summary>
    /// 전환 직전 캡처한 AI 추종 상태를 현재 멤버에 적용합니다.
    /// </summary>
    /// <param name="state">적용할 AI 추종 상태입니다.</param>
    public void ApplyFollowCarryoverState(FollowCarryoverState state)
    {
        if (!state.HasState || m_agent == null || !m_agent.enabled)
        {
            return;
        }

        m_nextUpdateTime = Time.time + state.NextUpdateDelay;

        if (state.IsStopped || !state.AgentEnabled)
        {
            StopAgent();
            return;
        }

        m_agent.isStopped = false;

        if (state.HasPath && m_agent.isOnNavMesh)
        {
            Vector3 destination = state.Destination;
            Vector3 velocity = state.Velocity;
            velocity.y = 0.0f;

            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                destination = hit.position;
            }

            m_agent.SetDestination(destination);
            m_agent.velocity = velocity;
        }
    }

    /// <summary>
    /// 필요한 참조를 캐싱하고 NavMeshAgent 회전 갱신 방식을 초기화합니다.
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
        m_agent.updateRotation = false;

        ApplyTargetingSettings();
    }

    /// <summary>
    /// Inspector 값을 대상 판단에 반영합니다.
    /// </summary>
    /// <remarks>
    /// 판단 규칙과 수치를 분리해 두었으므로 값만 옮겨 담습니다. 인스펙터에서 바꾼 값을 Play Mode에서
    /// 바로 보려면 <see cref="OnValidate"/>가 다시 부릅니다.
    /// </remarks>
    private void ApplyTargetingSettings()
    {
        m_targeting.ReevaluateInterval = m_reevaluateInterval;
        m_targeting.TargetHoldGrace = m_targetHoldGrace;
        m_targeting.DistanceThreatWeight = m_distanceThreatWeight;
        m_targeting.DistanceThreatFalloff = m_distanceThreatFalloff;
        m_targeting.CurrentTargetBonus = m_currentTargetBonus;
        m_targeting.DamageThreatPerPoint = m_damageThreatPerPoint;
        m_targeting.DamageThreatDecayPerSecond = m_damageThreatDecayPerSecond;
        m_targeting.SustainedBlockDuration = m_sustainedBlockDuration;
        m_targeting.AllyBlockRadius = m_allyBlockRadius;
        m_targeting.ReactionTime = m_reactionTime;
    }

    private void OnValidate()
    {
        ApplyTargetingSettings();
    }

    /// <summary>
    /// 이 AI가 받은 피해를 공격자별 위협도에 누적합니다(§4.3).
    /// </summary>
    /// <param name="attacker">피해를 준 적입니다.</param>
    /// <param name="damage">실제로 적용된 피해량입니다.</param>
    public void NotifyDamagedBy(EnemyController attacker, int damage)
    {
        m_targeting.NotifyDamagedBy(attacker, damage);
    }

    /// <summary>
    /// 현재 대상 판단을 갱신합니다.
    /// </summary>
    /// <remarks>
    /// 합류 중에는 대상 판단을 멈춥니다. §9.5가 "합류가 시작되면 현재 대상 정보는 유지하되 조준과
    /// 사격 요청을 종료한다"고 규정하므로, 판단은 남기고 실행만 끊는 것이 맞습니다. 여기서는
    /// <see cref="SquadAITargeting.CanFireAtCurrentTarget"/>이 자연히 false가 되도록 갱신만 건너뜁니다.
    /// </remarks>
    private void UpdateTargeting()
    {
        SquadManager manager = m_squadManager;
        if (manager == null)
        {
            return;
        }

        var context = new SquadAITargeting.Context
        {
            Intel = manager.EnemyIntel,
            EyePosition = m_eyePoint != null ? m_eyePoint.position : transform.position + Vector3.up * m_eyeHeight,
            BodyPosition = transform.position,
            WeaponRange = m_weapon != null ? m_weapon.HitscanRange : m_sightRange,
            ObstacleMask = manager.IntelObstacleMask,
            Squad = manager.SquadMembers,
            Self = m_memberController,
            CanEngage = m_memberController != null
                        && m_memberController.IsAlive
                        && !m_memberController.IsDown,
            IsJoining = m_isJoining,
        };

        m_targeting.Tick(in context);

        // 합류 중에는 대상 정보를 남긴 채 조준만 끕니다(§18.2 "합류 시작 중 조준 또는 연속 사격 ->
        // 실행 요청 즉시 종료 후 합류"). 조준을 남기면 몸이 적을 보느라 합류 방향으로 돌지 않습니다.
        m_hasAimPoint = !m_isJoining
                        && m_targeting.CurrentTarget != null
                        && m_targeting.TryGetAimPoint(manager.EnemyIntel, out m_aimPoint);
    }

    /// <summary>
    /// 대상을 향해 몸을 돌립니다(§11.2).
    /// </summary>
    /// <returns>조준을 수행했으면 true입니다. 그러면 이동 방향 회전을 건너뜁니다.</returns>
    /// <remarks>
    /// <b>유예 중에도 돕니다.</b> §8.5가 "마지막 확인 위치를 향한 조준과 방향 유지는 가능하지만
    /// 개인 사격선이 없으면 발사하지 않는다"고 규정하므로, 겨누는 것과 쏘는 것은 다른 판정입니다.
    /// 발사 가부는 <see cref="SquadAITargeting.CanFireAtCurrentTarget"/>이 따로 들고 있습니다.
    /// <para>
    /// 조준이 이동 방향 회전을 이깁니다. 둘 다 몸을 돌리므로 한쪽만 이겨야 하고, 전투 중에는 적을
    /// 보는 쪽이 맞습니다. §10.3의 "이동 중에도 조준과 사격을 계속한다"와도 맞습니다.
    /// </para>
    /// <para>
    /// <b>현재는 몸통 회전까지만입니다.</b> 조준 자세(IsAim)와 상체 리그는 <see cref="AimController"/>가
    /// 담당하는데 AI 멤버에서는 꺼져 있습니다. 이동 실행 통합에서 그 컴포넌트를 켜게 되면 그때 연결합니다.
    /// </para>
    /// </remarks>
    private bool HandleAimRotation()
    {
        if (!m_currentDecision.Aim || !m_hasAimPoint)
        {
            return false;
        }

        Vector3 delta = m_aimPoint - transform.position;
        delta.y = 0.0f;

        if (delta.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Quaternion targetRotation = Quaternion.LookRotation(delta.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * m_aimRotationSpeed);

        return true;
    }

    /// <summary>
    /// 사격 가능하면 실제로 무기를 쏩니다(§11.2).
    /// </summary>
    /// <remarks>
    /// <b>플레이어와 같은 경로를 씁니다.</b> <see cref="Gun.TryLayShoot"/>가 히트스캔·탄퍼짐·피해·피드백과
    /// 탄약 소비를 모두 처리하므로, AI만 다른 경로로 쏘면 §11.1의 "동일한 무기는 조작 주체와 관계없이
    /// 실제 성능을 동일하게 적용한다"가 깨집니다.
    /// <para>
    /// <b>유지·휴지 구간</b>: 조건이 맞아도 계속 쏘지 않고 <see cref="m_burstDuration"/> 만큼 쏜 뒤
    /// <see cref="m_burstRestDuration"/> 만큼 쉽니다. 문서가 이것을 무기의 점사 모드가 아니라
    /// AI 사격 제어로 규정합니다. 쉬는 동안에도 조준은 유지합니다.
    /// </para>
    /// <para>
    /// <b>조준 오차</b>는 사격 구간이 시작될 때 한 번 뽑아 그 구간 내내 씁니다. 발마다 새로 흔들면
    /// 무기 자체의 탄퍼짐과 겹쳐 과하게 흩어집니다. AI와 플레이어의 차이는 여기서 만듭니다(§11.1).
    /// </para>
    /// </remarks>
    private void HandleFiring()
    {
        if (m_weapon == null)
        {
            return;
        }

        // 발사 요청은 판정이 이미 끝냈습니다(§18.1 5). 사격을 막아도 대상 선정과 조준은 그대로 두는데,
        // 겨누기와 쏘기는 다른 판정이며(§8.5) 조준까지 끄면 동료가 적을 등지고 서 있어
        // 무엇을 노리는지 볼 수 없기 때문입니다.
        //
        // 조준 정렬만 여기서 봅니다. 이번 프레임의 몸 회전 결과에 의존하므로 판정 시점에는 아직
        // 확정되지 않은 값입니다. 사격선이 막히거나 조준이 어긋나면 즉시 멈춥니다(§10.1, §11.2).
        if (!m_currentDecision.Fire || !IsAimAligned)
        {
            m_isBursting = false;
            return;
        }

        UpdateBurstPhase();

        if (!m_isBursting)
        {
            return;
        }

        Vector3 muzzle = m_weapon.FirePos != null
            ? m_weapon.FirePos.position
            : transform.position + Vector3.up * m_eyeHeight;

        Vector3 aim = m_aimPoint + Vector3.up + m_aimErrorOffset;
        Vector3 delta = aim - muzzle;

        if (delta.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float distance = delta.magnitude;
        Vector3 direction = delta / distance;

        var shot = new Gun.HitscanShotInfo
        {
            IsValid = true,
            HasHit = false,
            IsObstructed = false,
            Origin = muzzle,
            Direction = direction,
            AimPoint = aim,
            EndPoint = muzzle + direction * Mathf.Min(distance, m_weapon.HitscanRange),
            FrameCount = Time.frameCount,
        };

        // AI는 ADS를 쓰지 않으므로 힙파이어 기준 탄퍼짐으로 넘깁니다.
        if (m_weapon.TryLayShoot(shot, false, out _))
        {
            m_lastShotTime = Time.time;
        }
    }

    /// <summary>
    /// 전투 자세(조준 포즈와 상체 리그)를 적용합니다.
    /// </summary>
    /// <remarks>
    /// 두 곳을 함께 건드려야 조준처럼 보입니다. <c>IsAim</c>은 Base Layer의 조준 트리를 고르고,
    /// <see cref="AimController.ApplyAiCombatStance"/>가 상체 조준 리그와 손 IK를 올립니다.
    /// 둘 다 원래는 조작 멤버에서 <see cref="ThirdPersonController"/>와 <see cref="AimController"/>가
    /// 하는 일인데, AI 멤버에서는 두 컴포넌트가 모두 꺼져 있어 여기서 대신 부릅니다.
    /// <para>
    /// 사격 직후 잠깐은 사격 중으로 봅니다. 발사 간격 사이마다 반동 레이어가 0으로 떨어지면 연사 중에
    /// 반동이 끊겨 보이기 때문입니다.
    /// </para>
    /// </remarks>
    private void HandleCombatStance()
    {
        bool inCombat = m_hasAimPoint;
        bool shooting = inCombat && Time.time - m_lastShotTime <= ShootingVisualHold;

        if (m_animator != null)
        {
            m_animator.SetBool(AimHash, inCombat);

            // 상체 조준 오프셋을 AI 기준으로 채웁니다. 이 값을 건드리지 않으면 조작하던 시절
            // 플레이어의 카메라 각도가 그대로 남아 상체가 엉뚱한 쪽으로 꺾입니다.
            // AI는 몸 전체를 대상 쪽으로 돌리므로 좌우 오프셋은 0이고, 상하만 높이 차로 냅니다.
            float lookY = 0.0f;
            if (inCombat)
            {
                Vector3 eye = m_eyePoint != null ? m_eyePoint.position : transform.position + Vector3.up * m_eyeHeight;
                Vector3 delta = m_aimPoint + Vector3.up - eye;
                float flat = new Vector2(delta.x, delta.z).magnitude;
                if (flat > 0.01f)
                {
                    lookY = Mathf.Clamp(Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg / MaxLookPitch, -1.0f, 1.0f);
                }
            }

            m_animator.SetFloat(LookXHash, 0.0f, LookDamp, Time.deltaTime);
            m_animator.SetFloat(LookYHash, lookY, LookDamp, Time.deltaTime);
        }

        if (m_aimController != null)
        {
            // 겨눌 지점을 함께 넘겨 상체 IK 타겟이 AI 기준으로 움직이게 합니다.
            m_aimController.ApplyAiCombatStance(inCombat, shooting);
        }
    }

    /// <summary>사격 유지 구간과 휴지 구간을 번갈아 갱신합니다(§11.2).</summary>
    private void UpdateBurstPhase()
    {
        if (Time.time < m_burstPhaseEndTime)
        {
            return;
        }

        if (m_isBursting)
        {
            m_isBursting = false;
            m_burstPhaseEndTime = Time.time + Mathf.Max(0.0f, m_burstRestDuration);
            return;
        }

        m_isBursting = true;
        m_burstPhaseEndTime = Time.time + Mathf.Max(0.05f, m_burstDuration);
        m_aimErrorOffset = ResolveAimError();
    }

    /// <summary>
    /// 확정된 재장전 요청을 시작합니다(§12.1).
    /// </summary>
    /// <remarks>
    /// 빈 탄창인지 전술 재장전인지, 지금 해도 되는지는 <see cref="SquadAIDecision"/>이 §18.1 순서로
    /// 이미 정했습니다(빈 탄창은 3번, 전술은 6번). 여기서는 그 요청을 수행만 합니다.
    /// <para>
    /// 시작한 재장전은 중간에 끊지 않습니다(§12.2 "새로운 적이 사격 가능한 상태가 되어도 완료한다").
    /// <see cref="Gun"/>이 <c>Invoke</c>로 완료를 예약하므로 여기서 아무것도 하지 않으면 그대로 완료됩니다.
    /// </para>
    /// </remarks>
    private void HandleReload()
    {
        // 무엇을 재장전할지는 판정이 이미 끝냈습니다(§18.1 3과 6). 여기서는 요청만 수행합니다.
        // 진행 중인 재장전은 판정이 None을 돌려주므로 자연히 끊기지 않습니다(§12.2).
        if (m_currentDecision.Reload == SquadAIReloadIntent.None || m_weapon == null)
        {
            return;
        }

        m_weapon.StartReload();
    }

    /// <summary>이번 사격 구간에 쓸 조준 오차를 뽑습니다.</summary>
    /// <returns>겨눈 지점에 더할 오프셋입니다.</returns>
    private Vector3 ResolveAimError()
    {
        if (m_aimErrorRadius <= 0.0f)
        {
            return Vector3.zero;
        }

        Vector2 circle = Random.insideUnitCircle * m_aimErrorRadius;
        return transform.right * circle.x + Vector3.up * circle.y;
    }

    /// <summary>
    /// 비활성화될 때 남아 있는 경로와 이동 애니메이션 값을 정리합니다.
    /// </summary>
    private void OnDisable()
    {
        StopAgent();
        UpdateMoveAnimation(Vector3.zero);
        ClearFollowState();
    }

    /// <summary>
    /// 동행 위치와 정지 상태를 지웁니다.
    /// </summary>
    /// <remarks>
    /// 조작권을 넘겨받거나 다시 AI가 될 때 옛 위치를 들고 있으면, 그 사이 리더가 움직였는데도
    /// "이미 도착했다"고 판단해 제자리에 서 있게 됩니다.
    /// </remarks>
    private void ClearFollowState()
    {
        m_isJoining = false;
        m_hasDestination = false;
        m_hasCombatPosition = false;
        m_pathDistanceToLeader = -1.0f;
        m_isSprinting = false;
        m_moveState = MoveStateWalk;
        m_cachedLeaderTransform = null;
        m_cachedLeaderController = null;

        // 대상 판단도 함께 지웁니다. 이 컴포넌트가 꺼지면 Tick이 멈추므로, 그냥 두면 마지막 판단이
        // 그대로 얼어붙습니다. 다시 AI가 됐을 때 옛 대상을 이미 조준 중인 것처럼 보이고,
        // 그 사이 대상이 죽거나 교전이 끝났어도 "쏠 수 있다"가 남습니다(실측으로 발견).
        // 문서도 AI 슬롯이 비활성화되면 판단 정보를 제거하라고 규정합니다(§9.5).
        m_targeting.Clear();

        // 마지막 행동 요청도 비웁니다. 남겨 두면 다시 AI가 된 첫 프레임에 옛 요청이 한 번 실행됩니다.
        m_currentDecision = default;

        // 전투 자세도 내립니다. 그러지 않으면 조준 리그가 올라간 채로 남아, 조작 멤버가 됐을 때
        // AimController가 자기 상태에서 시작하지 못하고 어긋난 자세를 물려받습니다.
        m_hasAimPoint = false;
        m_isBursting = false;
        if (m_aimController != null)
        {
            m_aimController.ApplyAiCombatStance(false, false);
        }

        if (m_animator != null)
        {
            m_animator.SetBool(AimHash, false);

            // 상체 오프셋도 0으로 되돌립니다. 남겨 두면 조작 멤버가 됐을 때 AimController가
            // 이 값에서 시작해 첫 프레임에 상체가 튑니다.
            m_animator.SetFloat(LookXHash, 0.0f);
            m_animator.SetFloat(LookYHash, 0.0f);
        }
    }

    /// <summary>
    /// 정보를 갱신하고, 이번 프레임의 행동 요청을 정한 뒤, 그 요청을 실행합니다.
    /// </summary>
    /// <remarks>
    /// 세 구간이 이 순서로 고정입니다.
    /// <para>
    /// 1. <b>정보 갱신</b> - 대상 판단(§9)과 이동 축 상태(경로상 거리, 합류 히스테리시스).
    ///    무엇을 할지는 아직 정하지 않습니다.
    /// 2. <b>판정</b> - <see cref="SquadAIDecision"/>이 §18.1의 우선순위 7단계를 위에서부터 확인해
    ///    이번 프레임의 요청을 확정합니다. 우선순위는 오직 그 안에만 있습니다.
    /// 3. <b>실행</b> - 아래 함수들은 요청을 수행만 하고 "할지 말지"를 다시 판단하지 않습니다.
    /// </para>
    /// <para>
    /// 이동만 갱신 주기를 따릅니다. 목적지를 매 프레임 다시 고르면 경로 계산 비용이 크고 발이 떨립니다
    /// (§6.3). 조준·사격·재장전은 매 프레임 돕니다.
    /// </para>
    /// </remarks>
    private void Update()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        // --- 1. 정보 갱신 ---

        // 대상 판단은 이동과 별개 축이라 이동을 못 하는 상황에서도 계속 돕니다.
        // 다만 다운·전투 이탈은 판단 자체를 멈춰야 하므로 Context.CanEngage가 걸러 냅니다(§9.5).
        UpdateTargeting();

        bool canFollow = CanFollow(out Transform leader);

        // 자기 자신이 리더인 경우는 갈 곳이 없는 것이지 행동 제한이 아니지만, 결과가 같으므로
        // 같은 분기로 접습니다. 다만 이쪽은 명시적으로 세워야 합니다.
        bool selfIsLeader = canFollow && leader == transform;
        bool canAct = canFollow && !selfIsLeader;

        bool movementDue = canAct && Time.time >= m_nextUpdateTime;
        if (movementDue)
        {
            m_nextUpdateTime = Time.time + m_updateInterval;
            movementDue = UpdateJoinState(leader);
        }

        // --- 2. 판정 ---

        m_currentDecision = m_decision.Resolve(BuildDecisionContext(canAct, selfIsLeader));

        // --- 3. 실행 ---

        if (m_currentDecision.Kind == SquadAIActionKind.Restricted)
        {
            if (m_currentDecision.HoldPosition)
            {
                StopAgent();
            }

            UpdateMoveAnimation(Vector3.zero);
            return;
        }

        if (movementDue)
        {
            ExecuteMovement(leader);
        }

        // 조준이 이동 방향 회전을 이깁니다. 둘 다 몸을 돌리므로 한쪽만 이겨야 하고,
        // 전투 중에는 적을 보는 쪽이 맞습니다(§10.3 "이동 중에도 조준과 사격을 계속한다").
        if (!HandleAimRotation())
        {
            HandleRotation();
        }

        HandleFiring();
        HandleReload();
        HandleCombatStance();
        HandleAnimation();
    }

    /// <summary>
    /// 판정에 넘길 현재 사정을 모읍니다.
    /// </summary>
    /// <param name="canAct">지금 행동 요청을 낼 수 있는 상태인지입니다.</param>
    /// <param name="holdPosition">활동 제한 상태에서 이동을 멈춰 세워야 하는지입니다.</param>
    /// <returns>판정에 넘길 사정입니다.</returns>
    /// <remarks>
    /// 컴포넌트를 뒤지는 일은 여기서 끝냅니다. <see cref="SquadAIDecision"/>은 규칙만 들고 있어야
    /// 우선순위가 한눈에 읽힙니다. <see cref="SquadAITargeting"/>과 같은 방식입니다.
    /// </remarks>
    private SquadAIDecision.Context BuildDecisionContext(bool canAct, bool holdPosition)
    {
        return new SquadAIDecision.Context
        {
            CanAct = canAct,
            HoldPosition = holdPosition,

            // §16 자동 구조는 미구현입니다. 판정에 자리는 있고 입력이 아직 없습니다.
            RescueRequested = false,

            HasWeapon = m_weapon != null,
            IsReloading = m_weapon != null && m_weapon.IsReloading,
            CanStartReload = m_weapon != null && m_weapon.CanReload,
            CurrentBullet = m_weapon != null ? m_weapon.CurrentBullet : 0,
            TacticalReloadThreshold = m_tacticalReloadThreshold,
            FiringEnabled = m_debugAllowFiring,

            IsJoining = m_isJoining,
            HasTarget = m_targeting.CurrentTarget != null,
            HasAimPoint = m_hasAimPoint,
            CanFireAtTarget = m_targeting.CanFireAtCurrentTarget,
        };
    }

    /// <summary>
    /// 필요한 컴포넌트와 매니저 참조를 캐싱합니다.
    /// </summary>
    private void CacheRequiredReferences()
    {
        m_memberController = GetComponent<SquadMemberController>();
        m_agent = GetComponent<NavMeshAgent>();
        m_squadManager = FindFirstObjectByType<SquadManager>();
        m_animator = GetComponent<Animator>();
        m_weapon = GetComponentInChildren<Gun>();
        m_aimController = GetComponent<AimController>();
    }

    /// <summary>
    /// 필수 참조가 누락되었는지 확인합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 true입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_memberController == null)
        {
            Debug.LogError("[SquadAIController] SquadMemberController 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        if (m_agent == null)
        {
            Debug.LogError("[SquadAIController] NavMeshAgent 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        if (m_squadManager == null)
        {
            Debug.LogError("[SquadAIController] 씬에서 SquadManager를 찾지 못했습니다.", this);
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// 현재 추종 가능한 상태인지 확인하고 리더 Transform을 반환합니다.
    /// </summary>
    /// <param name="leader">현재 조작 중인 리더 Transform입니다.</param>
    /// <returns>추종 가능한 상태이면 true입니다.</returns>
    private bool CanFollow(out Transform leader)
    {
        leader = null;

        if (m_memberController == null || !m_memberController.IsAlive || m_memberController.IsDown)
        {
            return false;
        }

        if (m_squadManager == null || m_squadManager.PlayerSquadMember == null)
        {
            return false;
        }

        if (m_agent == null || !m_agent.enabled)
        {
            return false;
        }

        leader = m_squadManager.PlayerSquadMember.transform;
        return leader != null;
    }

    /// <summary>
    /// 플레이어까지의 경로상 거리를 재고 합류 여부를 갱신합니다. 이동은 실행하지 않습니다.
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <returns>이동할 수 있는 상태이면 true입니다. 경로가 없으면 false입니다.</returns>
    /// <remarks>
    /// 공용 문서 `스쿼드 AI 시스템` v0.2 §6 기준입니다. <b>직선거리를 쓰지 않습니다</b>(§6.2) - 벽 하나를
    /// 사이에 두면 직선으로는 가까워도 실제로는 멀리 돌아야 하므로 판정에 경로상 거리를 씁니다.
    /// <para>
    /// 여기서 갱신한 합류 여부를 <see cref="SquadAIDecision"/>이 §18.1 4번 입력으로 읽습니다. 그래서
    /// 이 함수는 판정보다 먼저 돌아야 하고, 판정과 실행 사이에 끼어들면 안 됩니다.
    /// </para>
    /// </remarks>
    private bool UpdateJoinState(Transform leader)
    {
        ApplyAvoidancePriority(ResolveMemberOrder());

        m_pathDistanceToLeader = CalculatePathDistance(transform.position, leader.position);

        // 경로가 아예 없으면 갈 방법이 없으므로 제자리를 지킵니다. 직선으로 밀어붙이면 벽에 붙어 비빕니다.
        if (m_pathDistanceToLeader < 0.0f)
        {
            StopAgent();
            m_isJoining = false;
            ApplyFollowSpeed(leader);
            return false;
        }

        // 합류는 시작 거리에서 켜고 완료 거리에서 끕니다(§6.2). 한 거리로 판정하면 경계에서 뒤집힙니다.
        //
        // 완료 조건에 "목적지에 도착했는가"를 함께 보는 이유(실측): 목적지 반경은 직선거리로 잡고
        // 완료 판정은 경로상 거리로 하기 때문에, 목적지에 이미 서 있는데도 경로상으로는 완료 거리를
        // 살짝 넘어 합류가 안 풀리는 구간이 생깁니다. §6.1의 "합류 완료 거리 안의 유효 위치에 있으면
        // 위치 조정을 멈춘다"가 이 경우의 기준이므로 도착 여부를 함께 봅니다.
        bool arrivedAtDestination = m_hasDestination
                                    && FlatDistance(transform.position, m_destination) <= ResolveArrivalDistance();

        if (m_isJoining)
        {
            if (m_pathDistanceToLeader <= m_joinCompleteDistance || arrivedAtDestination)
            {
                m_isJoining = false;
            }
        }
        else if (m_pathDistanceToLeader > m_joinStartDistance)
        {
            m_isJoining = true;
        }

        return true;
    }

    /// <summary>
    /// 확정된 행동 요청에 맞춰 이동만 실행합니다.
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <remarks>
    /// 여기서는 "어느 행동인가"를 다시 판단하지 않습니다. 그것은 <see cref="SquadAIDecision"/>이 §18.1
    /// 순서대로 이미 정했습니다. 이 함수가 하는 판단은 <b>그 행동을 이룰 자리가 실제로 있는가</b>뿐입니다.
    /// <para>
    /// 전투 위치를 찾지 못하면 동행 경로로 흘러내립니다. §10.2가 "조건을 충족하는 위치가 없으면 무리하게
    /// 이동하지 않고 동행 행동을 유지한다"고 규정하므로, 자리가 없을 때 제자리에서 쏘는 것이 맞습니다.
    /// </para>
    /// </remarks>
    private void ExecuteMovement(Transform leader)
    {
        SquadAIActionKind kind = m_currentDecision.Kind;

        if (kind == SquadAIActionKind.Combat && TryResolveCombatPosition(leader, out Vector3 combatPosition))
        {
            m_destination = combatPosition;
            m_hasDestination = true;

            // 전투 중 이동은 걷기입니다. 달리면 조준이 흔들리고, §10.3도 개인적인 회피를 위한
            // 달리기 반복을 금지합니다. ApplyFollowSpeed가 m_isSprinting을 합류 여부로 다시 쓰므로
            // 그 뒤에 걷기 속도를 덮어씁니다.
            ApplyFollowSpeed(leader);
            m_isSprinting = false;

            ThirdPersonController leaderController = ResolveLeaderController(leader);
            if (leaderController != null)
            {
                m_agent.speed = leaderController.MoveSpeed * Mathf.Max(1.0f, m_followSpeedMultiplier);
            }

            m_agent.isStopped = false;
            m_agent.SetDestination(combatPosition);
            return;
        }

        // 완료 반경 안에 이미 서 있고 자리가 유효하면 위치를 조정하지 않습니다(§6.1).
        // 합류 중에는 아직 도착하지 않은 것이므로 이 분기를 타지 않습니다.
        if (kind != SquadAIActionKind.Join
            && m_pathDistanceToLeader <= m_joinCompleteDistance
            && IsPositionClear(transform.position))
        {
            StopAgent();
            m_hasDestination = false;
            ApplyFollowSpeed(leader);
            return;
        }

        if (!TryResolveDestination(leader, out Vector3 destination))
        {
            StopAgent();
            ApplyFollowSpeed(leader);
            return;
        }

        m_destination = destination;
        m_hasDestination = true;

        ApplyFollowSpeed(leader);

        m_agent.isStopped = false;
        m_agent.SetDestination(destination);
    }

    /// <summary>
    /// 전투 중 자리를 옮겨야 하는지 판단하고, 옮긴다면 목적지를 정합니다(§10.2, §10.3).
    /// </summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">확정된 전투 위치입니다.</param>
    /// <returns>자리를 옮겨야 하면 true입니다.</returns>
    /// <remarks>
    /// <b>기본은 안 움직이는 것입니다</b>(§10.2 "현재 위치에서 사격할 수 있으면 불필요하게 이동하지 않는다").
    /// 두 경우에만 움직입니다.
    /// <para>
    /// 1. <b>너무 붙었을 때</b>(§10.3) — 근접 변이체가 <see cref="m_keepAwayDistance"/> 안으로 들어오면
    ///    적 반대 방향으로 물러납니다. 물러나면서도 조준과 사격은 계속합니다.
    /// 2. <b>사격선이 지속적으로 막혔을 때</b>(§10.2) — 뚫리는 자리를 찾습니다. 일시적인 가림으로는
    ///    움직이지 않습니다. 동료가 잠깐 스칠 때마다 자리를 옮기면 산만해집니다.
    /// </para>
    /// <para>
    /// <b>자리가 없으면 그냥 서서 쏩니다</b>(§10.2 "조건을 충족하는 위치가 없으면 무리하게 이동하지 않고
    /// 동행 행동을 유지한다"). 뒤가 벽이면 벽에 비비지 않고 제자리에서 사격하는 것이 맞습니다.
    /// </para>
    /// <para>
    /// 합류가 필요한 상황은 호출자가 먼저 걸러 냅니다. 합류가 전투 위치 조정보다 우선입니다(§18.1).
    /// </para>
    /// </remarks>
    private bool TryResolveCombatPosition(Transform leader, out Vector3 destination)
    {
        destination = transform.position;

        EnemyController target = m_targeting.CurrentTarget;
        if (target == null || !m_hasAimPoint)
        {
            return false;
        }

        if (Time.time < m_nextCombatPositionTime)
        {
            // 주기 사이에는 앞서 고른 자리를 유지합니다. 매번 다시 고르면 목적지가 미세하게 흔들립니다.
            if (m_hasCombatPosition)
            {
                destination = m_combatPosition;
                return true;
            }

            return false;
        }

        m_nextCombatPositionTime = Time.time + Mathf.Max(0.05f, m_combatPositionInterval);

        Vector3 enemyPos = target.transform.position;
        float distanceToEnemy = FlatDistance(transform.position, enemyPos);

        bool tooClose = distanceToEnemy < m_keepAwayDistance;
        bool blockedTooLong = !m_targeting.CanFireAtCurrentTarget && m_targeting.IsFiringLineBlockedLong;

        if (!tooClose && !blockedTooLong)
        {
            m_hasCombatPosition = false;
            return false;
        }

        if (TrySelectCombatPosition(leader, target, tooClose, out Vector3 picked))
        {
            m_combatPosition = picked;
            m_hasCombatPosition = true;
            destination = picked;
            return true;
        }

        // 갈 자리가 없습니다. 억지로 움직이지 않고 제자리에서 쏩니다.
        m_hasCombatPosition = false;
        return false;
    }

    /// <summary>
    /// 전투 위치 후보 중 조건을 만족하는 가장 가까운 자리를 고릅니다(§10.2).
    /// </summary>
    /// <param name="leader">플레이어 조작 캐릭터입니다. 합류 완료 반경의 기준입니다.</param>
    /// <param name="target">현재 대상입니다.</param>
    /// <param name="tooClose">거리를 벌려야 하는 상황인지 여부입니다.</param>
    /// <param name="destination">선택된 자리입니다.</param>
    /// <returns>쓸 만한 자리를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// 후보는 <b>플레이어 주변 합류 완료 반경 안</b>으로 제한합니다(§10.2). 그래야 적을 피하다가
    /// 플레이어에게서 떨어져 곧바로 합류로 넘어가는 일이 없습니다.
    /// 거리를 벌리는 경우에는 적에게서 더 멀어지는 자리만 후보로 인정합니다.
    /// </remarks>
    private bool TrySelectCombatPosition(Transform leader, EnemyController target, bool tooClose, out Vector3 destination)
    {
        destination = transform.position;

        Vector3 enemyPos = target.transform.position;
        Vector3 away = transform.position - enemyPos;
        away.y = 0.0f;

        if (away.sqrMagnitude < 0.0001f)
        {
            away = -transform.forward;
        }

        away.Normalize();

        float currentDistance = FlatDistance(transform.position, enemyPos);
        float leaderRadius = Mathf.Max(0.1f, m_joinCompleteDistance);

        bool found = false;
        float bestPathDistance = float.MaxValue;

        // 정면 후퇴부터 좌우로 벌려 가며 훑습니다. 뒤가 막혔으면 비스듬히라도 빠지는 편이 자연스럽습니다.
        for (int i = 0; i < CombatPositionCandidateCount; i++)
        {
            float angle = ResolveRetreatAngle(i);
            Vector3 dir = Quaternion.Euler(0.0f, angle, 0.0f) * away;
            Vector3 candidate = transform.position + dir * m_retreatStep;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, DestinationSampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            // 플레이어에게서 너무 떨어지면 전투 위치가 아니라 이탈입니다.
            if (FlatDistance(hit.position, leader.position) > leaderRadius)
            {
                continue;
            }

            if (!IsPositionClear(hit.position))
            {
                continue;
            }

            if (tooClose && FlatDistance(hit.position, enemyPos) <= currentDistance)
            {
                // 물러나려는 것이므로 더 가까워지는 자리는 의미가 없습니다.
                continue;
            }

            if (!HasFiringLineFrom(hit.position, target))
            {
                continue;
            }

            float pathDistance = CalculatePathDistance(transform.position, hit.position);
            if (pathDistance < 0.0f || pathDistance >= bestPathDistance)
            {
                continue;
            }

            bestPathDistance = pathDistance;
            destination = hit.position;
            found = true;
        }

        return found;
    }

    /// <summary>후퇴 후보 각도를 정면부터 좌우로 번갈아 벌려 돌려줍니다.</summary>
    /// <param name="index">후보 순번입니다.</param>
    /// <returns>후퇴 방향에 더할 각도입니다.</returns>
    private static float ResolveRetreatAngle(int index)
    {
        int step = (index + 1) / 2;
        float sign = index % 2 == 0 ? 1.0f : -1.0f;
        return sign * step * RetreatAngleStep;
    }

    /// <summary>
    /// 지정한 자리에서 대상에게 사격선이 열리는지 확인합니다(§10.2).
    /// </summary>
    /// <param name="from">확인할 자리입니다.</param>
    /// <param name="target">현재 대상입니다.</param>
    /// <returns>환경 장애물에 막히지 않으면 true입니다.</returns>
    /// <remarks>
    /// 아군 가림은 보지 않습니다. 아군도 함께 움직이므로 이동이 끝날 때쯤이면 판정이 이미 달라져 있고,
    /// 그것까지 맞추려 들면 서로를 피해 다니느라 자리가 정해지지 않습니다.
    /// </remarks>
    private bool HasFiringLineFrom(Vector3 from, EnemyController target)
    {
        Vector3 eye = from + Vector3.up * m_eyeHeight;
        Vector3 delta = target.transform.position + Vector3.up - eye;
        float distance = delta.magnitude;

        if (distance <= 0.0001f)
        {
            return true;
        }

        int mask = m_squadManager != null ? m_squadManager.IntelObstacleMask : 0;
        return !Physics.Raycast(eye, delta / distance, distance, mask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 합류 목적지를 고릅니다. 기존 목적지가 아직 쓸 만하면 그대로 유지합니다.
    /// </summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">확정된 목적지입니다.</param>
    /// <returns>쓸 수 있는 목적지를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// §6.3: "기존 목적지가 계속 합류 완료 거리 안에 있고 접근 가능하면 다시 선택하지 않는다."
    /// 이 유지 규칙이 고정 자리 없이도 목적지가 안정되게 만드는 부분입니다.
    /// </remarks>
    private bool TryResolveDestination(Transform leader, out Vector3 destination)
    {
        if (m_hasDestination && IsDestinationStillUsable(leader, m_destination))
        {
            destination = m_destination;
            return true;
        }

        return TrySelectNearestFreePosition(leader, out destination);
    }

    /// <summary>기존 목적지를 계속 쓸 수 있는지 확인합니다.</summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">확인할 목적지입니다.</param>
    /// <returns>완료 반경 안이고 접근 가능하며 비어 있으면 true입니다.</returns>
    private bool IsDestinationStillUsable(Transform leader, Vector3 destination)
    {
        Vector3 toLeader = destination - leader.position;
        toLeader.y = 0.0f;
        if (toLeader.sqrMagnitude > m_joinCompleteDistance * m_joinCompleteDistance)
        {
            return false;
        }

        if (!IsPositionClear(destination))
        {
            return false;
        }

        return CalculatePathDistance(transform.position, destination) >= 0.0f;
    }

    /// <summary>
    /// 플레이어 주변 완료 반경 안에서 가장 짧게 갈 수 있는 빈 자리를 고릅니다(§6.3).
    /// </summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">선택된 목적지입니다.</param>
    /// <returns>후보를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// 후보 각도의 시작점을 멤버 순번으로 어긋나게 둡니다. 같은 순서로 훑으면 두 멤버가 같은 후보를
    /// 먼저 만나 같은 자리를 노립니다. 고정 자리를 주는 것이 아니라 탐색 순서만 다르게 하는 것이라
    /// §6.1의 `슬롯별 고정 위치` 금지에 걸리지 않습니다.
    /// </remarks>
    private bool TrySelectNearestFreePosition(Transform leader, out Vector3 destination)
    {
        destination = leader.position;

        int candidateCount = Mathf.Max(1, m_destinationCandidateCount);
        float radius = Mathf.Max(0.1f, m_joinCompleteDistance * DestinationRadiusRatio);
        float angleStep = 360.0f / candidateCount;
        float angleOffset = angleStep * (ResolveMemberOrder() / (float)Mathf.Max(1, candidateCount));

        bool found = false;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < candidateCount; i++)
        {
            float angle = angleOffset + angleStep * i;
            Vector3 offset = Quaternion.Euler(0.0f, angle, 0.0f) * (Vector3.forward * radius);
            Vector3 candidate = leader.position + offset;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, DestinationSampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            if (!IsPositionClear(hit.position))
            {
                continue;
            }

            float pathDistance = CalculatePathDistance(transform.position, hit.position);
            if (pathDistance < 0.0f || pathDistance >= bestDistance)
            {
                continue;
            }

            bestDistance = pathDistance;
            destination = hit.position;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// 목적지에 도착했다고 볼 거리를 구합니다.
    /// </summary>
    /// <returns>도착 판정에 쓸 수평 거리입니다.</returns>
    /// <remarks>
    /// <b>Agent의 <c>stoppingDistance</c>를 반드시 넘겨야 합니다(실측).</b> Agent는 목적지보다
    /// <c>stoppingDistance</c>만큼 앞에서 멈추므로, 그보다 작은 값으로 도착을 판정하면 영원히 도착하지
    /// 않습니다. 그러면 합류가 풀리지 않아 동료가 목적지 근처에서 계속 달리는 상태로 남습니다.
    /// 합류 완료 거리를 좁게 잡을수록 이 함정에 걸리기 쉬워 고정값 대신 Agent 설정에서 끌어옵니다.
    /// </remarks>
    private float ResolveArrivalDistance()
    {
        float agentStopDistance = m_agent != null ? m_agent.stoppingDistance : 0.0f;
        return Mathf.Max(DestinationArrivalEpsilon, agentStopDistance + ArrivalStopDistanceMargin);
    }

    /// <summary>높이를 무시한 두 지점 사이의 거리입니다.</summary>
    /// <param name="a">첫 번째 지점입니다.</param>
    /// <param name="b">두 번째 지점입니다.</param>
    /// <returns>수평 거리입니다.</returns>
    /// <remarks>
    /// 캐릭터 발밑과 NavMesh 표본 높이가 조금씩 다르므로, 도착 판정에 y를 넣으면 그 차이만큼
    /// 항상 멀다고 나옵니다.
    /// </remarks>
    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0.0f;
        return delta.magnitude;
    }

    /// <summary>그 자리가 다른 캐릭터와 겹치지 않는지 확인합니다.</summary>
    /// <param name="position">확인할 위치입니다.</param>
    /// <returns>비어 있으면 true입니다.</returns>
    /// <remarks>
    /// 자기 자신과 플레이어 조작 캐릭터는 제외합니다. 플레이어를 넣으면 완료 반경 안 후보가 전부
    /// 막혀 버립니다. 목적지 선택에만 쓰며, 이동 중 실제 충돌은 Agent 회피가 처리합니다.
    /// </remarks>
    private bool IsPositionClear(Vector3 position)
    {
        var members = m_squadManager.SquadMembers;
        if (members == null)
        {
            return true;
        }

        float clearanceSqr = m_destinationClearance * m_destinationClearance;
        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null || member == m_memberController || member.IsPlayerSquadMember)
            {
                continue;
            }

            if (!member.IsAlive || member.IsDown)
            {
                continue;
            }

            Vector3 delta = member.transform.position - position;
            delta.y = 0.0f;
            if (delta.sqrMagnitude < clearanceSqr)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 두 지점 사이의 실제 이동 경로 길이를 잽니다.
    /// </summary>
    /// <param name="from">출발 지점입니다.</param>
    /// <param name="to">도착 지점입니다.</param>
    /// <returns>경로상 거리이며, 완전한 경로가 없으면 <c>-1</c>입니다.</returns>
    /// <remarks>
    /// <c>PathComplete</c>가 아닌 경로는 목적지에 닿지 못한다는 뜻이므로 거리로 인정하지 않습니다.
    /// 이것이 §6의 길막 처리이기도 합니다. 닿을 수 없는 자리는 후보에서 빠지고 다른 자리가 선택됩니다.
    /// 갱신 주기마다 후보 수만큼 계산하므로 주기를 너무 짧게 두지 마십시오.
    /// </remarks>
    private float CalculatePathDistance(Vector3 from, Vector3 to)
    {
        m_pathBuffer ??= new NavMeshPath();

        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, m_pathBuffer))
        {
            return -1.0f;
        }

        if (m_pathBuffer.status != NavMeshPathStatus.PathComplete)
        {
            return -1.0f;
        }

        Vector3[] corners = m_pathBuffer.corners;
        if (corners.Length < 2)
        {
            return 0.0f;
        }

        float total = 0.0f;
        for (int i = 1; i < corners.Length; i++)
        {
            total += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return total;
    }

    /// <summary>
    /// 스쿼드 목록에서 이 멤버가 몇 번째 AI인지 셉니다.
    /// </summary>
    /// <returns>0부터 시작하는 순번입니다.</returns>
    /// <remarks>
    /// 회피 우선순위와 후보 탐색 시작 각도를 서로 어긋나게 하는 데만 씁니다. 이 순번으로 자리를
    /// 고정하지는 않습니다. 플레이어 조작 멤버를 건너뛰고 세므로 조작권이 바뀌어도 순번이 이어집니다.
    /// </remarks>
    private int ResolveMemberOrder()
    {
        var members = m_squadManager.SquadMembers;
        if (members == null)
        {
            return 0;
        }

        int order = 0;
        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null || member.IsPlayerSquadMember)
            {
                continue;
            }

            if (member == m_memberController)
            {
                return order;
            }

            order++;
        }

        return 0;
    }

    /// <summary>
    /// 멤버 순번에 따라 서로 다른 회피 우선순위를 부여합니다.
    /// </summary>
    /// <param name="order">이 멤버의 AI 순번입니다.</param>
    /// <remarks>
    /// <b>실측으로 확인한 문제</b>: 동행 AI 두 명의 <c>avoidancePriority</c>가 기본값 50으로 같으면
    /// <c>HighQualityObstacleAvoidance</c>가 서로를 대칭으로 피하려다 교착에 빠집니다.
    /// 속도를 6으로 올려도 실제 속력이 0과 2.7 사이를 오가며 4초에 4m밖에 못 갔습니다.
    /// 우선순위를 갈라 놓으면 한쪽이 양보해 교착이 풀립니다. Unity는 값이 낮을수록 우선합니다.
    /// </remarks>
    private void ApplyAvoidancePriority(int order)
    {
        int priority = Mathf.Clamp(m_baseAvoidancePriority + order, 0, 99);
        if (m_agent.avoidancePriority != priority)
        {
            m_agent.avoidancePriority = priority;
        }
    }

    /// <summary>
    /// 합류 중인지에 따라 걷기와 달리기 속도를 고릅니다.
    /// </summary>
    /// <param name="leader">속도 기준을 읽어올 플레이어 조작 캐릭터입니다.</param>
    /// <remarks>
    /// §6.1은 "플레이어의 달리기 상태를 그대로 복사하지 않는다", §6.2는 "합류 시작 조건을 충족하면
    /// 달리기를 사용한다"고 규정합니다. 그래서 리더가 달리는지가 아니라 <b>내가 합류 중인지</b>로 정합니다.
    /// 속도값 자체는 리더의 <see cref="ThirdPersonController"/>에서 읽어 플레이어와 어긋나지 않게 합니다.
    /// <para>
    /// 멈추는 경로에서도 이 함수를 부릅니다(실측). 그러지 않으면 합류를 끝낸 뒤에도 Agent에 달리기 속도가
    /// 남아, 다음에 다시 움직이기 시작하는 첫 프레임이 달리기 속도로 출발합니다.
    /// </para>
    /// </remarks>
    private void ApplyFollowSpeed(Transform leader)
    {
        m_isSprinting = m_isJoining;

        ThirdPersonController leaderController = ResolveLeaderController(leader);
        if (leaderController == null)
        {
            // 리더에서 속도를 읽지 못하면 프리팹에 설정된 Agent 속도를 그대로 씁니다.
            return;
        }

        // 평상시 동행은 플레이어 걷기보다 조금 빠릅니다. 같은 속도로 두면 플레이어가 걷기만 해도
        // 격차가 계속 벌어져 합류(달리기)가 상시로 켜지고, 결과적으로 동료가 늘 뛰어다닙니다(실측).
        // 달리기 상태를 복사하는 것이 아니라 걷기 속도에 여유를 주는 것이라 §6.1에 어긋나지 않습니다.
        m_agent.speed = m_isSprinting
            ? leaderController.SprintSpeed
            : leaderController.MoveSpeed * Mathf.Max(1.0f, m_followSpeedMultiplier);
    }

    /// <summary>리더의 이동 컨트롤러를 캐시와 함께 가져옵니다.</summary>
    /// <param name="leader">현재 리더 Transform입니다.</param>
    /// <returns>리더의 <see cref="ThirdPersonController"/>이며 없으면 null입니다.</returns>
    private ThirdPersonController ResolveLeaderController(Transform leader)
    {
        if (m_cachedLeaderTransform == leader)
        {
            return m_cachedLeaderController;
        }

        m_cachedLeaderTransform = leader;
        m_cachedLeaderController = leader != null ? leader.GetComponent<ThirdPersonController>() : null;
        return m_cachedLeaderController;
    }

    /// <summary>
    /// NavMeshAgent 이동 속도를 기준으로 캐릭터가 이동 방향을 바라보도록 회전합니다.
    /// </summary>
    private void HandleRotation()
    {
        if (m_agent == null || m_agent.isStopped)
        {
            return;
        }

        Vector3 velocity = m_agent.velocity;
        velocity.y = 0f;

        if (velocity.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * m_rotationSpeed);
    }

    /// <summary>
    /// 현재 이동 속도를 애니메이터의 이동 파라미터에 반영합니다.
    /// </summary>
    private void HandleAnimation()
    {
        if (m_agent == null)
        {
            UpdateMoveAnimation(Vector3.zero);
            return;
        }

        Vector3 velocity = m_agent.velocity;
        velocity.y = 0f;

        UpdateMoveAnimation(velocity);
    }

    /// <summary>
    /// NavMeshAgent 이동을 정지하고 현재 경로를 초기화합니다.
    /// </summary>
    private void StopAgent()
    {
        if (m_agent == null || !m_agent.enabled)
        {
            return;
        }

        m_agent.isStopped = true;
        m_agent.ResetPath();
    }

    /// <summary>
    /// AI 멤버의 이동 애니메이터 파라미터를 갱신합니다.
    /// </summary>
    /// <param name="velocity">현재 수평 이동 속도 벡터입니다.</param>
    /// <remarks>
    /// 조작 중인 멤버는 <see cref="ThirdPersonController"/>가 이 묶음을 채우지만, AI 멤버는 그 컴포넌트가
    /// 꺼져 있습니다(<see cref="SquadMemberController"/>가 직접 조작 멤버만 켭니다). 그래서 여기서 같은
    /// 파라미터를 채워야 합니다. 채우지 않으면 이렇게 어긋납니다.
    ///
    /// - <c>MoveState</c>가 0에 머물러 웅크림 트리가 재생됩니다(0은 웅크림 단계입니다).
    /// - <c>MoveX</c>/<c>MoveZ</c>가 0에 머물러 방향 블렌드의 중심, 즉 정지 클립만 나옵니다.
    /// - <c>IsGrounded</c>가 false로 남아, 조작하던 중 점프한 멤버를 전환으로 넘기면 <c>JumpStart</c>에서
    ///   나오지 못합니다. 나가는 조건이 <c>IsGrounded &amp;&amp; !IsJump</c>이기 때문입니다.
    ///
    /// AI 멤버는 NavMeshAgent로 지면을 따라 움직이므로 접지는 항상 true, 점프와 낙하는 항상 false입니다.
    /// AI가 점프하게 되면 그때 이 값들을 실제 상태로 바꿔야 합니다.
    /// </remarks>
    private void UpdateMoveAnimation(Vector3 velocity)
    {
        if (m_animator == null)
        {
            return;
        }

        float speed = velocity.magnitude;
        bool moving = speed > MoveDirectionThreshold;

        m_animator.SetFloat(SpeedHash, speed);

        // 동행 속도 배수만큼 재생 속도를 올려 발이 미끄러지지 않게 합니다.
        // 걷기 클립은 플레이어의 걷기 속도에 맞춰져 있는데 AI는 그보다 배수만큼 빠르게 이동하므로,
        // 재생 속도를 그대로 두면 이동 거리와 보폭이 어긋납니다. 합류(달리기) 중에는 달리기 클립과
        // 달리기 속도가 짝이 맞으므로 배수를 적용하지 않습니다.
        float motionSpeed = !m_isSprinting && moving
            ? Mathf.Max(1.0f, m_followSpeedMultiplier)
            : 1.0f;
        m_animator.SetFloat(MotionSpeedHash, motionSpeed);

        // 방향은 몸 기준으로 바꿔 넣습니다. 월드 방향을 그대로 쓰면 몸이 어디를 보든 같은 값이 됩니다.
        Vector3 local = moving
            ? transform.InverseTransformDirection(velocity / speed)
            : Vector3.zero;

        m_animator.SetFloat(MoveXHash, local.x, MoveDirectionDamp, Time.deltaTime);
        m_animator.SetFloat(MoveZHash, local.z, MoveDirectionDamp, Time.deltaTime);

        m_animator.SetBool(IsMoveHash, moving);

        // 걷기와 달리기 사이를 즉시 바꾸면 발걸음이 튑니다. ThirdPersonController와 같은 방식으로 보간합니다.
        // 멈춰 있을 때는 걷기 자세로 돌려놓아야 다음 출발이 달리기 포즈에서 시작하지 않습니다.
        float targetMoveState = moving && m_isSprinting ? MoveStateRun : MoveStateWalk;
        m_moveState = m_moveStateBlendDuration <= 0.0f
            ? targetMoveState
            : Mathf.MoveTowards(m_moveState, targetMoveState, Time.deltaTime / m_moveStateBlendDuration);

        m_animator.SetFloat(MoveStateHash, m_moveState);

        m_animator.SetBool(GroundedHash, true);
        m_animator.SetBool(JumpHash, false);
        m_animator.SetBool(FreeFallHash, false);
    }

    /// <summary>
    /// 합류를 시작하는 경로상 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 합류 시작 거리입니다.</param>
    public void SetJoinStartDistance(float value)
    {
        m_joinStartDistance = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 합류를 끝내는 경로상 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 합류 완료 거리입니다.</param>
    public void SetJoinCompleteDistance(float value)
    {
        m_joinCompleteDistance = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 목표 위치 갱신 주기를 설정합니다.
    /// </summary>
    /// <param name="value">새 갱신 주기입니다.</param>
    public void SetUpdateInterval(float value)
    {
        m_updateInterval = Mathf.Max(0.01f, value);
    }

    /// <summary>
    /// 이동 방향 회전 속도를 설정합니다.
    /// </summary>
    /// <param name="value">새 회전 속도입니다.</param>
    public void SetRotationSpeed(float value)
    {
        m_rotationSpeed = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 선택했을 때 합류 시작·완료 거리를 리더 기준 원 두 개로 그립니다.
    /// </summary>
    /// <remarks>
    /// 두 값은 히스테리시스 쌍입니다. 바깥 원을 넘으면 합류를 시작하고 안쪽 원에 들어오면 끝납니다.
    /// 숫자 두 개만 보면 그 사이 간격이 충분한지 알 수 없고, 좁으면 경계에서 합류가 껐다 켜졌다 합니다.
    ///
    /// <b>리더 기준으로 그립니다.</b> 판정 자체가 "리더까지 얼마나 먼가"이므로 이 AI를 중심에 두면 방향이 반대가 됩니다.
    /// 리더는 런타임에 찾으므로 Edit Mode에서는 그려지지 않습니다. 정지 상태의 배치보다 따라다니는 중의
    /// 간격이 봐야 할 대상이라 실질적인 제약은 아닙니다.
    ///
    /// 원은 실제 판정과 한 가지 다릅니다. 판정은 <b>경로상 거리</b>를 쓰는데(§6.2) 원은 직선거리라,
    /// 벽 너머에서는 원 안이어도 합류가 끝나지 않습니다. 그 차이를 보는 것이 이 표시의 쓸모이기도 합니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawJoinDistances || m_squadManager == null || m_squadManager.PlayerSquadMember == null)
        {
            return;
        }

        Vector3 leaderPosition = m_squadManager.PlayerSquadMember.transform.position;

        // 바깥 원: 이 밖으로 나가면 합류를 시작합니다.
        Gizmos.color = new Color(1.0f, 0.75f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(leaderPosition, m_joinStartDistance);

        // 안쪽 원: 여기 들어오면 합류가 끝납니다.
        Gizmos.color = new Color(0.3f, 1.0f, 0.45f, 0.8f);
        Gizmos.DrawWireSphere(leaderPosition, m_joinCompleteDistance);

        // 지금 이 AI가 리더와 얼마나 떨어져 있는지. 직선이지만 어느 원 사이인지 읽는 데 씁니다.
        Gizmos.color = m_isJoining
            ? new Color(1.0f, 0.5f, 0.2f, 0.9f)
            : new Color(0.5f, 0.5f, 0.5f, 0.5f);
        Gizmos.DrawLine(transform.position, leaderPosition);
    }
}