using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>
/// Enemy 행동을 엄브렐라 HFSM으로 오케스트레이션하는 호스트입니다.
/// </summary>
/// <remarks>
/// 상태 전이는 <see cref="TransitionTo"/>로 처리하고, 이산 자극(피격/사망)은 이벤트로 전이를 요청합니다(하이브리드 트리거).
/// 감지·공격·체력은 각각 <see cref="EnemyTargetSensor"/>, <see cref="EnemyAttack"/>, <see cref="EnemyHealth"/>에 위임합니다.
/// 밸런스 수치는 <see cref="EnemyBalanceSO"/>가 있으면 <see cref="ApplyBalance"/>로 각 모듈에 주입하고, 없으면 직렬화 기본값을 유지합니다.
/// 슬라이스 1(뼈대): 상태 패턴 배선·전이·엄브렐라 구조만. 각 상태의 실제 행동(Tick 본문)·타이머·전이 조건·<see cref="MoveTo"/> 구현은 후속 슬라이스.
/// 설계 근거: privateDoc ENEMY_SYSTEM_DESIGN.md §5~6(행동), §1(밸런스).
/// </remarks>
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyTargetSensor))]
[RequireComponent(typeof(EnemyAttack))]
public class EnemyController : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("이 감염체의 종류입니다. 시체 처리처럼 종류별로 다른 설정을 고를 때의 키로 씁니다. 밸런스 수치와는 무관합니다.")]
    [SerializeField] private EnemyType m_enemyType = EnemyType.Howler;

    [Header("Balance Data")]
    [Tooltip("선택 사항인 적 밸런스 데이터입니다. 지정하면 아래 레거시 기본값보다 우선 적용됩니다.")]
    [FormerlySerializedAs("m_balance")]
    [SerializeField] private EnemyBalanceSO m_balanceSO;

    [Header("Feedback Data")]
    [Tooltip("이 감염체 타입이 사용할 피격 표현, 혈흔, 행동 사운드 피드백 데이터입니다.")]
    [FormerlySerializedAs("m_feedbackProfile")]
    [SerializeField] private EnemyFeedbackSO m_feedback;

    [Header("Move")]
    /// <summary>스폰 지점을 기준으로 배회 목적지를 고를 반경입니다.</summary>
    [SerializeField] private float wanderRadius = 8f;

    /// <summary>배회 목적지를 다시 고르는 기본 주기입니다.</summary>
    [SerializeField] private float wanderInterval = 3f;

    /// <summary>배회 상태에서 사용할 NavMeshAgent 이동 속도입니다.</summary>
    [SerializeField] private float wanderSpeed = 1.2f;

    /// <summary>추적 상태에서 사용할 NavMeshAgent 이동 속도입니다.</summary>
    [SerializeField] private float chaseSpeed = 3.2f;

    /// <summary>대상 방향으로 회전할 때 사용하는 보간 속도입니다.</summary>
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Idle Variation")]
    /// <summary>대기 동작 혼합 비율의 최솟값입니다.</summary>
    [SerializeField] private float idleTypeMin = 0f;

    /// <summary>대기 동작 혼합 비율의 최댓값입니다.</summary>
    [SerializeField] private float idleTypeMax = 1f;

    /// <summary>이 개체가 쓰는 대기 동작 혼합 비율입니다. 초기화 때 한 번 뽑아 바뀌지 않습니다.</summary>
    public float IdleType { get; private set; }

    [Header("Detect")]
    /// <summary>대상을 처음 발견했을 때 경계 상태에 머무는 시간입니다.</summary>
    [SerializeField] private float alertDuration = 0.5f;

    /// <summary>시야에서 벗어난 뒤에도 대상의 실시간 위치를 계속 아는 시간입니다.</summary>
    [SerializeField] private float loseSightDelay = 2f;

    [Header("Noise")]
    /// <summary>소음 위치로 이동할 때의 속도입니다.</summary>
    [SerializeField] private float noiseChaseSpeed = 2f;

    /// <summary>소음 위치에 도착했다고 볼 거리입니다.</summary>
    [SerializeField] private float noiseArriveDistance = 1.5f;

    /// <summary>소음 위치 도착 후 주변을 수색하는 전체 시간입니다.</summary>
    [SerializeField] private float noiseSearchDuration = 8f;

    /// <summary>소음 수색 중 배회할 반경입니다.</summary>
    [SerializeField] private float noiseSearchRadius = 5f;

    [Header("Howl")]
    /// <summary>하울링이 전달되는 고정 반경입니다.</summary>
    [SerializeField] private float howlRadius = 25f;

    /// <summary>하울링 시작 후 전파가 확정되는 시점입니다.</summary>
    [SerializeField] private float howlBroadcastTime = 1.2f;

    /// <summary>하울링 행동 전체 길이입니다.</summary>
    [SerializeField] private float howlDuration = 3f;

    [Header("Target")]
    /// <summary>현재 대상을 다시 고를지 판단하는 주기입니다. 이 주기가 곧 대상의 최소 유지 시간입니다.</summary>
    [SerializeField] private float targetReevaluateInterval = 1f;

    /// <summary>새 후보가 현재 대상보다 이만큼 더 가까워야 대상을 바꿉니다.</summary>
    [SerializeField] private float targetSwitchPathDistanceDelta = 2f;

    [Header("Hit")]
    /// <summary>피격 상태에서 이동과 상태 전환을 잠그는 시간입니다.</summary>
    [SerializeField] private float hitStunDuration = 0.35f;

    /// <summary>연속 피격 시 Hit 상태를 다시 시작할 수 있는 최소 간격입니다.</summary>
    [SerializeField] private float hitStunCooldown = 0.2f;

    [Header("Attack Timing")]
    /// <summary>공격 시작 후 방향을 확정하는 시점입니다. 이후에는 대상을 따라 회전하지 않습니다.</summary>
    [SerializeField] private float attackDirectionLockTime = 0.25f;

    /// <summary>공격 시작 후 공간 판정을 수행하는 시점입니다.</summary>
    [SerializeField] private float attackImpactTime = 0.45f;

    /// <summary>판정 후 다음 행동까지의 후딜레이입니다. 이 값이 곧 공격 간격입니다.</summary>
    [SerializeField] private float attackRecoveryDuration = 0.75f;

    // =========================
    // 컴포넌트 참조
    // =========================
    /// <summary>Enemy의 NavMesh 이동을 담당하는 컴포넌트입니다.</summary>
    private NavMeshAgent agent;

    /// <summary>Enemy 애니메이션 파라미터와 트리거를 제어하는 컴포넌트입니다.</summary>
    private Animator animator;

    /// <summary>피해와 사망 이벤트를 제공하는 체력 컴포넌트입니다.</summary>
    private EnemyHealth enemyHealth;

    /// <summary>현재 추적할 스쿼드 멤버를 찾고 시야를 판정하는 센서입니다.</summary>
    private EnemyTargetSensor targetSensor;

    /// <summary>공격 가능 여부, 쿨다운, 실제 피해 적용을 담당하는 공격 모듈입니다.</summary>
    private EnemyAttack enemyAttack;

    /// <summary>선택 사항인 물리 래그돌 전환 컴포넌트입니다.</summary>
    private RagdollController ragdollController;

    /// <summary>행동·피격·사망 피드백의 출력 컴포넌트입니다.</summary>
    private EnemyFeedbackEmitter feedbackEmitter;

    // =========================
    // 최상위 상태 인스턴스 (상태 간 전이에 사용)
    // =========================
    /// <summary>비전투 배회 상태입니다.</summary>
    public WanderState Wander { get; private set; }

    /// <summary>비전투 소음 경계(두리번) 상태입니다.</summary>
    public AlertState Alert { get; private set; }

    /// <summary>비전투 소음 추적 상태입니다.</summary>
    public NoiseChaseState NoiseChase { get; private set; }

    /// <summary>비전투 소음 수색 상태입니다.</summary>
    public NoiseSearchState NoiseSearch { get; private set; }

    /// <summary>교전 엄브렐라 상태입니다.</summary>
    public CombatState Combat { get; private set; }

    /// <summary>처치 상태입니다.</summary>
    public DeadState Dead { get; private set; }
    // 각성 준비(AwakenState)·경직(HitState)은 각각 슬라이스 2·4에서 추가.

    /// <summary>현재 활성 상태입니다.</summary>
    private EnemyStateBase m_current;

    /// <summary>현재 활성 상태입니다.</summary>
    public EnemyStateBase Current => m_current;

    // =========================
    // 상태가 읽어 쓰는 접근자 (튜닝 수치는 컨트롤러가 소유, 상태는 읽기만)
    // =========================
    /// <summary>NavMesh 이동 컴포넌트입니다.</summary>
    public NavMeshAgent Agent => agent;

    /// <summary>애니메이터입니다.</summary>
    public Animator Animator => animator;

    /// <summary>체력 컴포넌트입니다.</summary>
    public EnemyHealth Health => enemyHealth;

    /// <summary>대상 감지 센서입니다.</summary>
    public EnemyTargetSensor Sensor => targetSensor;

    /// <summary>공격 모듈입니다.</summary>
    public EnemyAttack Attack => enemyAttack;

    /// <summary>
    /// 물리 골격이 준비되어 있으면 현재 자세에서 래그돌로 전환합니다.
    /// </summary>
    /// <returns>래그돌을 활성화했으면 true이고, 구성이 없으면 false입니다.</returns>
    public bool TryActivateRagdoll()
    {
        return ragdollController != null && ragdollController.TryActivateRagdoll();
    }

    /// <summary>
    /// 래그돌 골격이 있으면 래그돌용 콜라이더는 남기고 게임플레이 콜라이더만 걷어냅니다.
    /// </summary>
    /// <returns>래그돌 컴포넌트가 처리했으면 true이고, 없으면 false입니다.</returns>
    /// <remarks>
    /// 사망 직후에는 아직 래그돌로 넘기지 않아도 피격·이동 충돌은 즉시 사라져야 합니다(§5.10.4).
    /// 어느 콜라이더가 래그돌 소속인지는 <see cref="RagdollController"/>만 알고 있으므로 그쪽에 맡깁니다.
    /// false가 돌아오면 호출자가 콜라이더를 통째로 끄면 됩니다.
    /// </remarks>
    public bool TryDisableGameplayColliders()
    {
        if (ragdollController == null)
        {
            return false;
        }

        ragdollController.DisableGameplayColliders();
        return true;
    }

    /// <summary>배회 상태 진입 피드백을 출력합니다.</summary>
    public void PlayIdleFeedback() => EnsureFeedbackEmitter()?.PlayIdle(m_feedback);

    /// <summary>교전 진입 경계 피드백을 출력합니다.</summary>
    public void PlayAlertFeedback() => EnsureFeedbackEmitter()?.PlayAlert(m_feedback);

    /// <summary>추적 상태 진입 피드백을 출력합니다.</summary>
    public void PlayChaseFeedback() => EnsureFeedbackEmitter()?.PlayChase(m_feedback);

    /// <summary>공격 시작 피드백을 출력합니다.</summary>
    public void PlayAttackFeedback() => EnsureFeedbackEmitter()?.PlayAttack(m_feedback);

    /// <summary>실제 피격 위치에 데이터 기반 피격 이펙트·사운드·혈흔을 출력합니다.</summary>
    public void PlayHitFeedback(Vector3 point, Vector3 normal, Transform hitTransform) =>
        EnsureFeedbackEmitter()?.PlayHit(m_feedback, point, normal, hitTransform);

    /// <summary>사망 위치에 데이터 기반 사망 사운드를 출력합니다.</summary>
    public void PlayDeathFeedback() => EnsureFeedbackEmitter()?.PlayDeath(m_feedback);

    /// <summary>배회 목적지 반경입니다.</summary>
    public float WanderRadius => wanderRadius;

    /// <summary>배회 목적지 갱신 주기입니다.</summary>
    public float WanderInterval => wanderInterval;

    /// <summary>배회 이동 속도입니다.</summary>
    public float WanderSpeed => wanderSpeed;

    /// <summary>추적 이동 속도입니다.</summary>
    public float ChaseSpeed => chaseSpeed;

    /// <summary>대상 방향 회전 보간 속도입니다.</summary>
    public float RotationSpeed => rotationSpeed;

    /// <summary>소음 위치로 이동할 때의 속도입니다.</summary>
    public float NoiseChaseSpeed => noiseChaseSpeed;

    /// <summary>소음 위치에 도착했다고 볼 거리입니다.</summary>
    public float NoiseArriveDistance => noiseArriveDistance;

    /// <summary>소음 수색 전체 시간입니다.</summary>
    public float NoiseSearchDuration => noiseSearchDuration;

    /// <summary>소음 수색 중 배회할 반경입니다.</summary>
    public float NoiseSearchRadius => noiseSearchRadius;

    /// <summary>하울링이 전달되는 고정 반경입니다.</summary>
    public float HowlRadius => howlRadius;

    /// <summary>하울링 전파가 확정되는 시점입니다.</summary>
    public float HowlBroadcastTime => howlBroadcastTime;

    /// <summary>하울링 행동 전체 길이입니다. 전파 시점보다 짧아지지 않습니다.</summary>
    public float HowlDuration => Mathf.Max(howlBroadcastTime, howlDuration);

    /// <summary>각성 준비(경계) 시간입니다.</summary>
    public float AlertDuration => alertDuration;

    /// <summary>시야에서 벗어난 뒤 실시간 위치를 계속 아는 시간입니다.</summary>
    public float LoseSightDelay => loseSightDelay;

    /// <summary>현재 대상 재평가 주기입니다.</summary>
    public float TargetReevaluateInterval => targetReevaluateInterval;

    /// <summary>대상 교체에 필요한 경로 거리 차이입니다.</summary>
    public float TargetSwitchPathDistanceDelta => targetSwitchPathDistanceDelta;

    /// <summary>피격 경직 지속 시간입니다.</summary>
    public float HitStunDuration => hitStunDuration;

    /// <summary>연속 피격 경직 재진입 최소 간격입니다.</summary>
    public float HitStunCooldown => hitStunCooldown;

    /// <summary>공격 시작 기준 방향 고정 시점입니다.</summary>
    public float AttackDirectionLockTime => attackDirectionLockTime;

    /// <summary>공격 시작 기준 판정 시점입니다.</summary>
    public float AttackImpactTime => Mathf.Max(attackDirectionLockTime, attackImpactTime);

    /// <summary>판정 후 후딜레이이며 곧 공격 간격입니다.</summary>
    public float AttackRecoveryDuration => attackRecoveryDuration;

    /// <summary>이 감염체의 종류입니다. 종류별 설정을 고를 때의 키입니다.</summary>
    public EnemyType EnemyType => m_enemyType;

    /// <summary>현재 적용 대상으로 지정된 적 밸런스 데이터입니다.</summary>
    public EnemyBalanceSO Balance => m_balanceSO;

    /// <summary>현재 이 감염체 타입에 지정된 피드백 데이터입니다.</summary>
    public EnemyFeedbackSO Feedback => m_feedback;

    /// <summary>현재 HP입니다. 체력 컴포넌트가 없으면 0을 반환합니다.</summary>
    public int CurrentHP => enemyHealth != null ? enemyHealth.CurrentHP : 0;

    /// <summary>현재 유효한 추적 대상 스쿼드 멤버입니다.</summary>
    public SquadMemberController CurrentTarget => targetSensor != null ? targetSensor.CurrentTarget : null;

    /// <summary>필수 컴포넌트를 캐싱하고 상태 인스턴스를 생성한 뒤 밸런스를 적용합니다.</summary>
    private void Awake()
    {
        CacheReferences();
        CreateStates();
        ApplyBalance(m_balanceSO);
    }

    /// <summary>
    /// 지정한 적 밸런스 데이터를 컨트롤러와 하위 감지·공격·체력 모듈에 적용합니다.
    /// </summary>
    /// <remarks>
    /// null이면 기존 컴포넌트 직렬화 기본값을 유지합니다. 최초 Awake에서는 체력 Start 초기화 전에
    /// 최대 HP만 교체하며, 런타임 재적용 시 현재 HP를 강제로 회복하지 않습니다.
    /// </remarks>
    /// <param name="balance">적용할 순수 수치 밸런스 데이터입니다.</param>
    public void ApplyBalance(EnemyBalanceSO balance)
    {
        if (balance == null)
        {
            return;
        }

        m_balanceSO = balance;

        wanderRadius = balance.WanderRadius;
        wanderInterval = balance.WanderInterval;
        wanderSpeed = balance.WanderSpeed;
        chaseSpeed = balance.ChaseSpeed;
        rotationSpeed = balance.RotationSpeed;
        idleTypeMin = balance.IdleTypeMin;
        idleTypeMax = balance.IdleTypeMax;
        noiseChaseSpeed = balance.NoiseChaseSpeed;
        noiseArriveDistance = balance.NoiseArriveDistance;
        noiseSearchDuration = balance.NoiseSearchDuration;
        noiseSearchRadius = balance.NoiseSearchRadius;
        howlRadius = balance.HowlRadius;
        howlBroadcastTime = balance.HowlBroadcastTime;
        howlDuration = balance.HowlDuration;
        alertDuration = balance.AlertDuration;
        loseSightDelay = balance.LoseSightDelay;
        targetReevaluateInterval = balance.TargetReevaluateInterval;
        targetSwitchPathDistanceDelta = balance.TargetSwitchPathDistanceDelta;
        attackDirectionLockTime = balance.AttackDirectionLockTime;
        attackImpactTime = balance.AttackImpactTime;
        attackRecoveryDuration = balance.AttackRecoveryDuration;
        hitStunDuration = balance.HitStunDuration;
        hitStunCooldown = balance.HitStunCooldown;
        targetSensor?.ApplyBalance(balance);
        enemyAttack?.ApplyBalance(balance);
        enemyHealth?.SetMaxHP(balance.MaxHp);
    }

    /// <summary>활성화될 때 체력 이벤트를 구독합니다.</summary>
    private void OnEnable()
    {
        CacheReferences();

        if (enemyHealth == null)
        {
            return;
        }

        enemyHealth.OnDamaged += HandleDamaged;
        enemyHealth.OnDeath += HandleDied;
    }

    /// <summary>비활성화될 때 체력 이벤트 구독을 해제합니다.</summary>
    private void OnDisable()
    {
        if (enemyHealth == null)
        {
            return;
        }

        enemyHealth.OnDamaged -= HandleDamaged;
        enemyHealth.OnDeath -= HandleDied;
    }

    /// <summary>초기 상태로 진입합니다.</summary>
    private void Start()
    {
        RollIdleType();

        // TODO(슬라이스 2): 휴면/배회 배치 구분, 스폰 기준점 기록.
        TransitionTo(Wander);
    }

    /// <summary>이동 애니메이션을 갱신하고 현재 상태를 Tick합니다.</summary>
    private void Update()
    {
        UpdateLocomotionAnimator();
        m_current?.Tick();
    }

    /// <summary>NavMeshAgent 속도를 애니메이터 MoveSpeed 파라미터로 전달합니다.</summary>
    private void UpdateLocomotionAnimator()
    {
        if (animator == null || agent == null)
        {
            return;
        }

        Vector3 velocity = agent.velocity;
        float speed = velocity.magnitude;

        animator.SetFloat(AnimMoveSpeed, speed);

        // 이동 방향을 자기 기준으로 바꿔 2D 블렌드 축에 넣습니다.
        // 월드 방향을 그대로 쓰면 몸이 어디를 보든 같은 값이 되어 옆걸음과 앞걸음을 구분하지 못합니다.
        if (speed <= 0.01f)
        {
            animator.SetFloat(AnimMoveX, 0f);
            animator.SetFloat(AnimMoveY, 0f);
            return;
        }

        Vector3 local = transform.InverseTransformDirection(velocity / speed);
        animator.SetFloat(AnimMoveX, local.x);
        animator.SetFloat(AnimMoveY, local.z);
    }

    // =========================
    // 애니메이터 연동
    // 파라미터 이름을 여기 모아 두어 상태 클래스들이 같은 문자열을 각자 들고 있지 않게 합니다.
    // =========================
    private static readonly int AnimMoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int AnimInAttackRange = Animator.StringToHash("InAttackRange");
    private static readonly int AnimAttack = Animator.StringToHash("DoAttack");

    // 사망 관련 애니메이터 파라미터는 두지 않습니다. 사망은 애니메이터를 끄고 래그돌이 이어받으므로
    // 재생할 클립도, 종료를 기다릴 스테이트도 없습니다(DeadState 참고).

    /// <summary>공격 중인지 여부입니다. 공격 스테이트를 유지하는 조건입니다.</summary>
    private static readonly int AnimIsAttack = Animator.StringToHash("IsAttack");

    /// <summary>몇 번째 공격인지입니다. 1이 첫 공격, 2가 이어지는 공격입니다.</summary>
    private static readonly int AnimAttackCombo = Animator.StringToHash("AttackCombo");

    /// <summary>어느 손으로 치는지입니다. 0이 왼손, 1이 오른손입니다.</summary>
    private static readonly int AnimAttackSide = Animator.StringToHash("AttackSide");

    /// <summary>이동 방향의 좌우 성분입니다. 2D 이동 블렌드의 X축입니다.</summary>
    private static readonly int AnimMoveX = Animator.StringToHash("MoveX");

    /// <summary>이동 방향의 앞뒤 성분입니다. 2D 이동 블렌드의 Y축입니다.</summary>
    private static readonly int AnimMoveY = Animator.StringToHash("MoveY");

    /// <summary>대기 동작 혼합 비율입니다. 개체마다 한 번 뽑아 고정합니다.</summary>
    private static readonly int AnimIdleType = Animator.StringToHash("IdleType");

    /// <summary>애니메이터를 기본 상태로 되돌리는 트리거입니다.</summary>
    private static readonly int AnimReset = Animator.StringToHash("DoReset");

    /// <summary>소음 경계(두리번) 중인지 여부입니다. 클립은 `WW_LookAround`를 씁니다.</summary>
    /// <remarks>
    /// 트리거가 아니라 bool입니다. 두리번은 지속 상태이고 나가는 길이 셋(추적/배회/교전)이라
    /// 트리거로는 "지금 경계 중인가"를 되읽을 수 없습니다.
    ///
    /// 이름을 `IsAlert`가 아니라 `IsLookAround`로 맞췄습니다. 애니메이터의 스테이트·클립·진입 트리거가
    /// 모두 `LookAround` 어휘를 쓰므로, 코드만 다른 이름을 쓰면 파라미터가 조용히 어긋납니다.
    /// </remarks>
    private static readonly int AnimIsAlert = Animator.StringToHash("IsLookAround");

    /// <summary>두리번을 시작하는 트리거입니다.</summary>
    private static readonly int AnimLookAround = Animator.StringToHash("DoLookAround");

    /// <summary>소음 인지 게이지의 진행도입니다. 두리번 강도 블렌드에 쓰는 선택 파라미터입니다.</summary>
    private static readonly int AnimAlertLevel = Animator.StringToHash("AlertLevel");

    /// <summary>하울링을 시작하는 트리거입니다. 클립은 `WW_Howl`을 씁니다.</summary>
    private static readonly int AnimHowl = Animator.StringToHash("DoHowl");

    /// <summary>하울링 스테이트를 유지하는 조건입니다.</summary>
    private static readonly int AnimIsHowl = Animator.StringToHash("IsHowl");

    /// <summary>
    /// 애니메이터를 기본 상태로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// 어떤 상태에 걸려 빠져나오지 못할 때 쓰는 탈출구입니다.
    /// 지금은 부르는 곳이 없습니다. 필요한 시점이 정해지면 그때 연결합니다.
    /// </remarks>
    public void ResetAnimation()
    {
        animator?.SetTrigger(AnimReset);
    }

    /// <summary>공격 애니메이션을 재생합니다.</summary>
    /// <param name="comboStep">1이면 첫 공격, 2면 이어지는 두 번째 공격입니다.</param>
    public void PlayAttackAnimation(int comboStep = 1)
    {
        if (animator == null)
        {
            return;
        }

        // 같은 동작만 반복되면 단조로워 보이므로 휘두르는 손을 매번 새로 고릅니다.
        // 중간값을 넣으면 좌우 동작이 섞여 어색해지므로 0이나 1만 넣습니다.
        animator.SetFloat(AnimAttackSide, Random.value < 0.5f ? 0f : 1f);

        animator.SetBool(AnimIsAttack, true);
        animator.SetInteger(AnimAttackCombo, comboStep);
        animator.SetTrigger(AnimAttack);
    }

    /// <summary>
    /// 공격 상태에서 빠져나왔음을 애니메이터에 알립니다.
    /// </summary>
    /// <remarks>공격 스테이트는 이 값이 false가 되어야 이동으로 돌아갑니다.</remarks>
    public void EndAttackAnimation()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(AnimIsAttack, false);
        animator.SetInteger(AnimAttackCombo, 0);
    }

    /// <summary>공격 사거리 안에 있는지를 애니메이터에 전달합니다.</summary>
    /// <param name="inRange">사거리 안이면 true입니다.</param>
    public void SetInAttackRangeAnimation(bool inRange)
    {
        animator?.SetBool(AnimInAttackRange, inRange);
    }

    /// <summary>
    /// 소음 경계(두리번) 상태 여부를 애니메이터에 전달합니다.
    /// </summary>
    /// <param name="alert">경계 중이면 true입니다.</param>
    /// <remarks>
    /// <b>파라미터가 없어도 동작해야 합니다.</b> 두리번 클립과 애니메이터 배선은 별도로 진행 중이며,
    /// 없는 파라미터에 값을 넣으면 유니티가 매 호출마다 경고를 찍어 콘솔이 묻힙니다.
    /// 배선이 끝나면 이 확인은 통과하므로 코드를 되돌릴 필요가 없습니다.
    /// </remarks>
    public void SetAlertAnimation(bool alert)
    {
        if (animator == null || !HasAnimatorParameter(AnimIsAlert))
        {
            return;
        }

        animator.SetBool(AnimIsAlert, alert);

        // 진입은 트리거가 함께 있어야 성립합니다. 애니메이터 조건이 DoLookAround AND IsLookAround입니다.
        // bool만 세우면 스테이트에 들어가지 못하고, 그러면 두리번 자세가 아예 나오지 않습니다.
        if (alert && HasAnimatorParameter(AnimLookAround))
        {
            animator.SetTrigger(AnimLookAround);
        }

        // 두리번 강도 블렌드는 선택 사항입니다. 파라미터를 두지 않아도 경계 자체는 동작합니다.
        if (HasAnimatorParameter(AnimAlertLevel))
        {
            animator.SetFloat(AnimAlertLevel, targetSensor != null ? targetSensor.NoiseAwareness01 : 0f);
        }
    }

    /// <summary>
    /// 하울링 애니메이션을 시작합니다.
    /// </summary>
    /// <remarks>
    /// `DoHowl`과 `IsHowl`은 애니메이터에 이미 선언돼 있으나 지금까지 코드가 부르지 않았습니다.
    /// 파라미터가 없어도 동작해야 하므로 존재를 확인합니다 - 전파 자체는 타이머로 진행되며
    /// 애니메이션은 표현입니다.
    /// </remarks>
    public void PlayHowlAnimation()
    {
        if (animator == null)
        {
            return;
        }

        if (HasAnimatorParameter(AnimIsHowl))
        {
            animator.SetBool(AnimIsHowl, true);
        }

        if (HasAnimatorParameter(AnimHowl))
        {
            animator.SetTrigger(AnimHowl);
        }
    }

    /// <summary>하울링 상태에서 빠져나왔음을 애니메이터에 알립니다.</summary>
    public void EndHowlAnimation()
    {
        if (animator == null || !HasAnimatorParameter(AnimIsHowl))
        {
            return;
        }

        animator.SetBool(AnimIsHowl, false);
    }

    /// <summary>
    /// 클립의 하울링 전파 이벤트를 상태로 넘깁니다.
    /// </summary>
    /// <remarks>
    /// 애니메이션 이벤트가 이 이름으로 이 컴포넌트를 부릅니다. 문서 확정본의 이벤트 이름은
    /// `HowlBroadcast`이며, 클립에 이벤트를 넣을 때 이 메서드를 가리키게 합니다.
    /// 하울링 중이 아닐 때 들어오면 무시합니다.
    /// </remarks>
    public void HowlBroadcast()
    {
        if (m_current == Combat && Combat.CurrentSub == Combat.Howl)
        {
            Combat.Howl.NotifyAnimationBroadcast();
        }
    }

    /// <summary>
    /// 하울링 전파 결과를 진단용으로 남깁니다.
    /// </summary>
    /// <param name="appliedCount">실제로 하울링을 적용한 주변 변이체 수입니다.</param>
    /// <param name="memberCount">위치를 제공한 스쿼드 캐릭터 수입니다.</param>
    /// <remarks>
    /// 하울링은 눈에 보이는 결과가 "주변 개체가 몰려온다"뿐이라 전파가 실제로 됐는지 판단하기 어렵습니다.
    /// 반경 밖이어서 0인 것과 배선이 끊겨 0인 것을 구분하려면 숫자가 필요합니다.
    /// </remarks>
    public void NotifyHowlBroadcast(int appliedCount, int memberCount)
    {
        LastHowlAppliedCount = appliedCount;
        LastHowlMemberCount = memberCount;
    }

    /// <summary>가장 최근 하울링이 적용된 주변 변이체 수입니다. 진단용입니다.</summary>
    public int LastHowlAppliedCount { get; private set; } = -1;

    /// <summary>가장 최근 하울링이 위치를 제공한 스쿼드 캐릭터 수입니다. 진단용입니다.</summary>
    public int LastHowlMemberCount { get; private set; } = -1;

    /// <summary>지정한 파라미터가 현재 애니메이터에 선언되어 있는지 확인합니다.</summary>
    /// <remarks>
    /// 매 프레임 부르는 경로가 아니므로 순회 비용은 문제가 되지 않습니다.
    /// 상태 진입·이탈에서만 호출합니다.
    /// </remarks>
    private bool HasAnimatorParameter(int nameHash)
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == nameHash)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>현재 상태를 빠져나가고 지정한 상태로 진입합니다.</summary>
    /// <param name="next">전이할 상태입니다.</param>
    public void TransitionTo(EnemyStateBase next)
    {
        if (next == null || next == m_current)
        {
            return;
        }

        m_current?.Exit();
        m_current = next;
        m_current.Enter();
    }

    /// <summary>공격 애니메이션 이벤트에서 호출되어 판정 시점을 알립니다.</summary>
    /// <remarks>
    /// 이제 여기서 피해를 넣지 않습니다. 실제 적중은 손에 달린 판정 콜라이더가 결정합니다.
    /// 이름이 하는 일과 어긋나 보이지만, 기존 클립의 애니메이션 이벤트가 이 이름으로 묶여 있어 그대로 둡니다.
    /// 클립을 새로 만들 때 이벤트 이름을 정리하면 그때 함께 바꿉니다.
    /// 상태는 이 통보와 자체 타이머 중 먼저 오는 것을 판정 시점으로 삼고 한 번의 공격에서 한 번만 처리합니다.
    /// </remarks>
    /// <summary>공격 클립의 애니메이션 이벤트에서 호출되어 판정 콜라이더를 켭니다.</summary>
    /// <remarks>
    /// 이 시점부터 손에 달린 판정 콜라이더가 닿은 것에 피해를 줍니다.
    /// 판정 시점 통보도 함께 처리해 상태가 후딜레이로 넘어갈 수 있게 합니다.
    /// </remarks>
    public void OnAttackHitboxOn()
    {
        if (m_current == Dead)
        {
            return;
        }

        // Attack은 판정을 담당하는 모듈이고, Combat.Attack은 공격 상태입니다. 이름이 같으니 주의합니다.
        Attack?.SetHitboxActive(true);
        Combat?.Attack?.NotifyAnimationImpact();
    }

    /// <summary>공격 클립의 애니메이션 이벤트에서 호출되어 판정 콜라이더를 끕니다.</summary>
    /// <remarks>
    /// 죽은 상태에서도 반드시 꺼야 합니다. 켜진 채로 남으면 다음 공격 전까지 스치는 것마다 피해가 들어갑니다.
    /// 클립이 중간에 끊겨 이 이벤트가 오지 않는 경우는 <see cref="AttackState"/>가 종료 시 다시 끕니다.
    /// </remarks>
    public void OnAttackHitboxOff()
    {
        Attack?.SetHitboxActive(false);
    }

    public void ApplyAttackDamage()
    {
        if (m_current == Dead)
        {
            return;
        }

        Combat?.Attack?.NotifyAnimationImpact();
    }

    /// <summary>
    /// 이 개체가 쓸 대기 동작 혼합 비율을 한 번 뽑아 고정합니다.
    /// </summary>
    /// <remarks>
    /// 개체마다 대기 자세가 조금씩 달라 보이게 하려는 것이라 매번 새로 뽑지 않습니다.
    /// 계속 흔들리면 대기 중에 몸이 미세하게 떨리는 것처럼 보입니다.
    ///
    /// 범위는 밸런스 SO가 소유하는 상수이고, 뽑힌 값은 이 개체의 런타임 상태입니다.
    /// Awake가 아니라 Start에서 뽑는 것은 ApplyBalance가 Awake에서 범위를 채우기 때문입니다.
    /// </remarks>
    private void RollIdleType()
    {
        IdleType = Random.Range(idleTypeMin, idleTypeMax);
        animator?.SetFloat(AnimIdleType, IdleType);
    }

    /// <summary>지정한 월드 좌표로 NavMesh 이동을 지시합니다.</summary>
    /// <param name="worldPosition">이동 목적지 월드 좌표입니다.</param>
    /// <remarks>이동 속도는 호출 상태가 <c>Agent.speed</c>로 미리 설정합니다(배회/추격 등).</remarks>
    public void MoveTo(Vector3 worldPosition)
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(worldPosition);
    }

    /// <summary>이동을 멈춥니다.</summary>
    public void StopMoving()
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = true;
    }

    /// <summary>Enemy 동작에 필요한 컴포넌트를 찾고 없으면 보조 모듈을 추가합니다.</summary>
    private void CacheReferences()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (enemyHealth == null)
        {
            enemyHealth = GetComponent<EnemyHealth>();
        }

        if (targetSensor == null && !TryGetComponent(out targetSensor))
        {
            targetSensor = gameObject.AddComponent<EnemyTargetSensor>();
        }

        if (enemyAttack == null && !TryGetComponent(out enemyAttack))
        {
            enemyAttack = gameObject.AddComponent<EnemyAttack>();
        }

        if (ragdollController == null)
        {
            ragdollController = GetComponent<RagdollController>();
        }

        if (m_feedback != null)
        {
            EnsureFeedbackEmitter();
        }
    }

    /// <summary>Feedback SO가 있을 때만 감염체 피드백 emitter를 런타임에 준비합니다.</summary>
    private EnemyFeedbackEmitter EnsureFeedbackEmitter()
    {
        if (m_feedback == null)
        {
            return null;
        }

        if (feedbackEmitter == null && !TryGetComponent(out feedbackEmitter))
        {
            feedbackEmitter = gameObject.AddComponent<EnemyFeedbackEmitter>();
        }

        return feedbackEmitter;
    }

    /// <summary>상태 인스턴스를 생성합니다.</summary>
    private void CreateStates()
    {
        Wander = new WanderState(this);
        Alert = new AlertState(this);
        NoiseChase = new NoiseChaseState(this);
        NoiseSearch = new NoiseSearchState(this);
        Combat = new CombatState(this);
        Dead = new DeadState(this);
    }

    /// <summary>피해를 받으면 공격자를 인식하고 교전으로 전이합니다.</summary>
    /// <param name="damage">이번에 적용된 피해량입니다.</param>
    /// <param name="attacker">피해를 입힌 대상입니다. 공격자를 알 수 없는 경로면 null입니다.</param>
    /// <remarks>
    /// 직접 피격은 비전투 감지 보호(§5.6)를 무시합니다. 시야·접촉·소음은 AI 동료를 감지 대상에서 빼지만,
    /// 실제로 맞았다면 누가 쐈든 알아채야 합니다. 오사로 맞은 변이체가 쏜 쪽을 쫓아오는 규칙이 여기서 성립합니다.
    /// TODO(후속): 경직 처리.
    /// </remarks>
    private void HandleDamaged(int damage, GameObject attacker)
    {
        if (m_current == Dead)
        {
            return;
        }

        if (attacker != null && targetSensor != null)
        {
            SquadMemberController member = attacker.GetComponentInParent<SquadMemberController>();
            if (member != null)
            {
                targetSensor.NotifyDamagedBy(member);
            }
        }

        TransitionTo(Combat);
    }

    /// <summary>사망 이벤트를 받아 처치 상태로 전이합니다.</summary>
    private void HandleDied()
    {
        TransitionTo(Dead);
    }
}
