using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using VInspector;

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
public class EnemyController : MonoBehaviour, IKnockbackReceiver
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
    [Tooltip("스폰 지점을 기준으로 배회 목적지를 고를 반경(m)입니다.")]
    [SerializeField] private float wanderRadius = 8f;

    [Tooltip("배회 목적지를 다시 고르는 기본 주기(초)입니다.")]
    [SerializeField] private float wanderInterval = 3f;

    [Tooltip("배회 상태에서 사용할 NavMeshAgent 이동 속도(m/s)입니다.")]
    [SerializeField] private float wanderSpeed = 1.2f;

    [Tooltip("추적 상태에서 사용할 NavMeshAgent 이동 속도(m/s)입니다.")]
    [SerializeField] private float chaseSpeed = 3.2f;

    [Tooltip("대상을 향해 몸을 돌리는 최대 각속도(도/초)입니다. 200이면 180도 도는 데 약 0.9초가 걸립니다. 값이 클수록 고개가 튕기듯 돌아갑니다.")]
    [SerializeField] private float rotationSpeed = 200f;

    [Header("Idle Variation")]
    [Tooltip("대기 동작 혼합 비율의 최솟값입니다. 개체마다 이 범위에서 한 번 뽑아 고정합니다.")]
    [SerializeField] private float idleTypeMin = 0f;

    [Tooltip("대기 동작 혼합 비율의 최댓값입니다. 최솟값과 같으면 모든 개체가 같은 대기 동작을 씁니다.")]
    [SerializeField] private float idleTypeMax = 1f;

    /// <summary>이 개체가 쓰는 대기 동작 혼합 비율입니다. 초기화 때 한 번 뽑아 바뀌지 않습니다.</summary>
    public float IdleType { get; private set; }

    [Header("Detect")]
    [Tooltip("대상을 처음 발견했을 때 경계 상태에 머무는 시간(초)입니다.")]
    [SerializeField] private float alertDuration = 0.5f;

    [Tooltip("시야에서 벗어난 뒤에도 대상의 실시간 위치를 계속 아는 시간(초)입니다. 끝나면 마지막 확인 위치만 남습니다.")]
    [SerializeField] private float loseSightDelay = 2f;

    [Header("Noise")]
    [Tooltip("소음 위치로 이동할 때의 속도(m/s)입니다.")]
    [SerializeField] private float noiseChaseSpeed = 2f;

    [Tooltip("소음 위치에 도착했다고 볼 거리(m)입니다.")]
    [SerializeField] private float noiseArriveDistance = 1.5f;

    [Tooltip("소음 위치 도착 후 주변을 수색하는 전체 시간(초)입니다.")]
    [SerializeField] private float noiseSearchDuration = 8f;

    [Tooltip("소음 수색 중 배회할 반경(m)입니다.")]
    [SerializeField] private float noiseSearchRadius = 5f;

    [Header("Howl")]
    [Tooltip("하울링이 전달되는 고정 반경(m)입니다. 벽이나 엄폐물은 판정에 쓰지 않습니다.")]
    [SerializeField] private float howlRadius = 25f;

    [Tooltip("하울링 시작 후 전파가 확정되는 시점(초)입니다. 이 시점 전에 사망하면 전파가 취소됩니다.")]
    [SerializeField] private float howlBroadcastTime = 1.2f;

    [Tooltip("하울링 행동 전체 길이(초)입니다. 끝나면 다음 행동을 고릅니다.")]
    [SerializeField] private float howlDuration = 3f;

    [Header("Target")]
    [Tooltip("현재 대상을 다시 고를지 판단하는 주기(초)입니다. 이 주기가 곧 대상의 최소 유지 시간이 됩니다.")]
    [SerializeField] private float targetReevaluateInterval = 1f;

    [Tooltip("새 후보가 현재 대상보다 이만큼(m) 더 가까워야 대상을 바꿉니다. 경계에서 대상이 떨리는 것을 막습니다.")]
    [SerializeField] private float targetSwitchPathDistanceDelta = 2f;

    [Header("Knockback")]
    [Tooltip("넉백 충격량을 속도로 바꿀 때 쓰는 유효 질량(kg)입니다. 속도 = 충격량 / 이 값이므로, 크게 하면 같은 충격량에 덜 밀립니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_knockbackMass = 60.0f;

    [Tooltip("넉백 속도가 초당 줄어드는 비율입니다. 4면 약 0.25초 만에 거의 멈춥니다. 클수록 짧고 탁 밀립니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_knockbackDamping = 8.0f;

    [Tooltip("넉백으로 낼 수 있는 최대 속도(m/s)입니다. 강한 무기로도 순간이동처럼 날아가지 않게 막습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_maxKnockbackSpeed = 6.0f;

    [Header("Hit")]
    [Tooltip("피격 상태에서 이동과 상태 전환을 잠그는 시간(초)입니다.")]
    [SerializeField] private float hitStunDuration = 0.35f;

    [Tooltip("연속 피격 시 피격 상태를 다시 시작할 수 있는 최소 간격(초)입니다.")]
    [SerializeField] private float hitStunCooldown = 0.2f;

    [Header("Attack Timing")]
    [Tooltip("공격 시작 후 이 시점(초)에 방향을 확정합니다. 이후에는 대상을 따라 회전하지 않습니다.")]
    [SerializeField] private float attackDirectionLockTime = 0.25f;

    [Tooltip("공격 시작 후 이 시점(초)에 공간 판정을 수행합니다. 방향 고정 시점보다 뒤여야 합니다.")]
    [SerializeField] private float attackImpactTime = 0.45f;

    [Tooltip("판정 후 다음 행동까지의 후딜레이(초)입니다. 이 값이 곧 공격 간격이며 별도 쿨다운은 두지 않습니다.")]
    [SerializeField] private float attackRecoveryDuration = 0.75f;

    [Foldout("Debug")]
    [Tooltip("이 개체를 선택했을 때 하울링 전파 반경을 Scene 뷰에 원으로 표시합니다. 벽은 판정에 쓰지 않으므로 원 안이면 그대로 전달됩니다.")]
    [SerializeField] private bool m_debugDrawHowlRadius = true;

    [Tooltip("이 개체를 선택했을 때 배회 반경과 소음 수색 반경을 Scene 뷰에 표시합니다. 배회는 스폰 지점, 수색은 현재 위치가 기준입니다.")]
    [SerializeField] private bool m_debugDrawWanderRadius = false;

    [Tooltip("경직이 끝날 때 잠금 시간과 루트 모션이 실제로 옮긴 거리를 콘솔에 남깁니다. 클립이 의도한 후퇴량과 어긋나는지 확인할 때 켭니다.")]
    [SerializeField] private bool m_debugLogStagger = false;

    // =========================
    // 컴포넌트 참조
    // =========================
    /// <summary>Enemy의 NavMesh 이동을 담당하는 컴포넌트입니다.</summary>
    private NavMeshAgent agent;

    /// <summary>남아 있는 넉백 속도입니다. 매 프레임 감쇠하며 0이 되면 넉백이 끝납니다.</summary>
    private Vector3 m_knockbackVelocity;

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
    // 각성 준비(AwakenState)는 슬라이스 2에서 추가.
    // 경직은 상태가 아니라 상태 위에 얹히는 잠금이라 여기 목록에 없습니다(§5.7). HandleStaggered 참고.

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
    /// 이 개체에 Joint로 연결된 물리 골격이 준비되어 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 래그돌 전환 실패가 "골격이 없어서"인지 "설정에서 껐거나 이미 켜져 있어서"인지 가르는 데 씁니다.
    /// 골격이 없는 것은 프리팹 구성 누락이라 경고할 일이고, 나머지는 정상 경로입니다.
    /// </remarks>
    public bool HasRagdollSkeleton => ragdollController != null && ragdollController.IsConfigured;

    /// <summary>
    /// 이 개체가 피격으로 밀려날 때 적용할 충격량 하한을 래그돌에 알립니다.
    /// </summary>
    /// <param name="minimumImpulse">충격량 하한(N·s)입니다.</param>
    /// <remarks>
    /// 하한은 종류별 시체 설정이 정하고, 실제 적용은 쏜 쪽이 <see cref="RagdollController.ApplyHitImpulse"/>로
    /// 합니다. 사망 시점에 한 번 넣어 두면 그 뒤에 도착하는 임펄스가 이 값을 하한으로 씁니다.
    /// </remarks>
    public void SetRagdollMinimumHitImpulse(float minimumImpulse)
    {
        ragdollController?.SetMinimumHitImpulse(minimumImpulse);
    }

    /// <summary>
    /// 피격 방향으로 이 개체를 밀어냅니다.
    /// </summary>
    /// <param name="direction">공격자에서 피격 지점으로 향하는 방향입니다.</param>
    /// <param name="hitPoint">피격 지점의 월드 좌표입니다.</param>
    /// <param name="impulse">충격량(N·s)입니다.</param>
    /// <param name="hitBone">맞은 부위의 리지드바디입니다. 알 수 없으면 null입니다.</param>
    /// <returns>넉백을 적용했으면 true입니다.</returns>
    /// <remarks>
    /// <b>넉백의 기본 대상은 살아 있는 개체입니다.</b> 사망 후 래그돌이 받는 충격은 같은 값을 쓰는 부가
    /// 효과이며, 이 함수가 상태를 보고 두 경로 중 하나를 고릅니다. 때리는 쪽은 어느 쪽인지 몰라도 됩니다.
    ///
    /// 살아 있는 경우 충격량을 유효 질량으로 나눠 속도로 바꿉니다. 뼈 하나가 아니라 몸 전체가 밀리므로
    /// 래그돌 뼈 질량이 아니라 <see cref="m_knockbackMass"/>를 씁니다.
    /// 수평 성분만 씁니다. 살아 있는 개체는 NavMesh 위에 붙어 있어 위아래 성분이 의미가 없고,
    /// 아래로 밀면 지면에 박히려는 변위가 되어 이동이 끊깁니다.
    /// </remarks>
    public bool ApplyKnockback(Vector3 direction, Vector3 hitPoint, float impulse, Rigidbody hitBone)
    {
        // 사망 후에는 래그돌이 물리로 받습니다. 부가 효과 경로입니다.
        // 같은 유효 질량을 넘겨 무기 값 하나가 두 경로에서 같은 속도를 내게 합니다.
        if (m_current == Dead)
        {
            return ragdollController != null
                && ragdollController.ApplyHitImpulse(
                    direction, hitPoint, impulse, hitBone, m_knockbackMass);
        }

        if (impulse <= 0.0f)
        {
            return false;
        }

        Vector3 horizontal = new Vector3(direction.x, 0.0f, direction.z);
        if (horizontal.sqrMagnitude <= Mathf.Epsilon)
        {
            return false;
        }

        float speed = Mathf.Min(impulse / m_knockbackMass, m_maxKnockbackSpeed);
        if (speed <= 0.0f)
        {
            return false;
        }

        // 연속 피격은 더해집니다. 다만 상한을 넘지 않게 잘라 연사에 끝없이 가속하지 않도록 합니다.
        m_knockbackVelocity += horizontal.normalized * speed;
        if (m_knockbackVelocity.magnitude > m_maxKnockbackSpeed)
        {
            m_knockbackVelocity = m_knockbackVelocity.normalized * m_maxKnockbackSpeed;
        }

        return true;
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
        enemyHealth?.ApplyBalance(balance);
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
        enemyHealth.OnStaggered += HandleStaggered;
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
        enemyHealth.OnStaggered -= HandleStaggered;

        // 잠긴 채로 꺼지면 다시 켜졌을 때 남은 시간만큼 굳어 있습니다. 개체를 재사용(풀링)할 때 드러나는 문제라
        // 여기서 확실히 풀어 둡니다.
        m_isStaggered = false;
        RestoreAgentPositionOwnership();
    }

    /// <summary>초기 상태로 진입합니다.</summary>
    private void Start()
    {
        RollIdleType();

        // TODO(슬라이스 2): 휴면/배회 배치 구분, 스폰 기준점 기록.
        TransitionTo(Wander);
    }

    /// <summary>이동 애니메이션을 갱신하고, 경직 중이 아니면 현재 상태를 Tick합니다.</summary>
    /// <remarks>
    /// 경직은 상태를 교체하지 않고 <b>상태 위에 얹히는 잠금</b>입니다(§5.7). 그래서 여기서 Tick만 건너뜁니다.
    /// 상태 자체는 그대로 남아 있으므로 경직이 풀리면 하던 행동이 이어지며, 복귀 대상을 따로 기억할 필요가 없습니다.
    /// 넉백이 상태를 취소하지 않고 변위만 더하는 것과 같은 층위입니다.
    ///
    /// <b>감지는 경직 중에도 계속 돕니다.</b> 경직은 행동을 끊는 것이지 눈을 감기는 것이 아닙니다.
    /// 멈추면 대상 정보가 그 시간만큼 낡고, 경직 시간(<see cref="HitStunDuration"/>)이 시야 유지 시간
    /// (<c>LoseSightDelay</c>)보다 길면 풀리는 순간 "그동안 못 봤다"로 판정되어 대상을 잃고 배회로 돌아갑니다.
    /// 눈앞에 서 있는 상대를 경직 한 번에 놓치는 셈이라, 감지만은 잠금 밖에 둡니다.
    /// </remarks>
    private void Update()
    {
        UpdateLocomotionAnimator();
        UpdateKnockback();

        if (UpdateStagger())
        {
            targetSensor?.UpdatePerception();
            return;
        }

        // 잠금은 풀렸지만 이탈 블렌드가 남아 있으면 위치는 아직 루트 모션이 쥐고 있습니다.
        // 판단은 이미 재개된 상태이므로 아래 Tick은 그대로 돌립니다.
        UpdateStaggerHandover();

        m_current?.Tick();
    }

    /// <summary>
    /// 남아 있는 넉백 속도만큼 이 개체를 밀어내고 속도를 줄입니다.
    /// </summary>
    /// <remarks>
    /// 살아 있는 개체는 뼈가 kinematic이고 이동을 NavMeshAgent가 소유하므로 물리 힘으로 밀 수 없습니다.
    /// 그래서 <see cref="NavMeshAgent.Move"/>로 변위를 더합니다. Agent가 NavMesh 경계와 높이를 맞춰 주므로
    /// Transform을 직접 움직일 때 생기는 벽 관통과 공중 부양을 피할 수 있습니다.
    ///
    /// 상태 머신의 이동 지시를 취소하지 않고 그 위에 변위를 더합니다. 밀리는 동안에도 추격을 계속하는 편이
    /// "밀렸지만 다시 달려든다"는 모습에 맞고, 경직으로 행동을 끊는 것은 별도 규칙(<see cref="HandleStaggered"/>)의 몫입니다.
    /// 경직 중에도 이 변위는 계속 더해집니다. 넉백과 경직 루트 모션은 둘 다 밀어내는 값이라 그대로 합산됩니다.
    ///
    /// 지수 감쇠로 줄입니다. 일정량씩 빼면 초기 속도에 따라 지속 시간이 달라져 무기마다 감이 흔들립니다.
    /// </remarks>
    private void UpdateKnockback()
    {
        if (m_knockbackVelocity.sqrMagnitude <= 0.0001f)
        {
            m_knockbackVelocity = Vector3.zero;
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(m_knockbackVelocity * Time.deltaTime);
        }

        m_knockbackVelocity *= Mathf.Exp(-m_knockbackDamping * Time.deltaTime);
    }

    // =========================
    // 경직 (오버레이 인터럽트)
    // 별도 상태가 아니라 하위 행동 위에 얹히는 잠금입니다(§5.7).
    // 누적·한계치·면역은 EnemyHealth가 소유하고, 여기서는 발동 이후의 잠금과 연출만 다룹니다.
    // =========================

    /// <summary>지금 경직 중인지 여부입니다.</summary>
    private bool m_isStaggered;

    /// <summary>경직 잠금이 풀리는 시각입니다.</summary>
    private float m_staggerEndTime;

    /// <summary>이번 경직에서 애니메이터의 실제 클립 길이를 이미 반영했는지 여부입니다.</summary>
    private bool m_staggerLengthResolved;

    /// <summary>
    /// 위치를 루트 모션이 쥐고 있는 구간인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// <b>행동 잠금보다 길게 유지됩니다.</b> 잠금이 풀리는 순간 위치를 Agent에 돌려주면, 애니메이터가 아직
    /// 경직에서 빠져나오는 이탈 블렌드(0.25초) 중인데 몸은 Agent가 끌고 가 위치가 끊겨 보입니다.
    /// 블렌드가 끝날 때까지 루트 모션이 위치를 계속 쥐고 있어야 일어서는 동작과 달리기가 이어집니다.
    /// </remarks>
    private bool m_staggerRootMotionActive;

    /// <summary>이탈 블렌드가 끝나지 않아도 위치를 Agent에 돌려줄 한계 시각입니다.</summary>
    /// <remarks>
    /// 애니메이터 배선이 없거나 다른 상태로 튀어 블렌드 종료를 감지하지 못하면 위치 소유권이 영구히
    /// 루트 모션에 남습니다. 그러면 Agent 이동이 화면에 반영되지 않아 제자리에서 미끄러지는 모습이 됩니다.
    /// </remarks>
    private float m_staggerHandoverDeadline;

    /// <summary>이탈 블렌드를 기다려 줄 최대 시간(초)입니다.</summary>
    /// <remarks>애니메이터의 이탈 전이(0.25초)보다 넉넉하게 둡니다.</remarks>
    private const float StaggerHandoverTimeout = 0.6f;

    /// <summary>직전 프레임의 위치입니다. 루트 모션이 위치를 쥔 구간에서 실제 속도를 재는 데 씁니다.</summary>
    private Vector3 m_lastLocomotionPosition;

    /// <summary>경직이 시작된 위치입니다. 루트 모션이 실제로 얼마나 옮겼는지 확인하는 데 씁니다.</summary>
    private Vector3 m_staggerStartPosition;

    /// <summary>경직 스테이트의 짧은 이름 해시입니다.</summary>
    /// <remarks>
    /// 잠금 시간을 애니메이터의 실제 재생 길이에 맞추려면 지금 어느 스테이트인지 알아야 합니다.
    /// 파라미터 이름(DoStagger/IsStagger)과 같은 어휘를 쓰므로 배선이 갈라질 여지가 적습니다.
    /// </remarks>
    private static readonly int StaggerStateHash = Animator.StringToHash("Stagger");

    /// <summary>지금 경직으로 행동이 잠겨 있는지 여부입니다.</summary>
    public bool IsStaggered => m_isStaggered;

    /// <summary>
    /// 경직 발동을 받아 행동을 잠그고 경직 연출을 시작합니다.
    /// </summary>
    /// <remarks>
    /// 진행 중이던 공격은 여기서 취소합니다(§5.7). 경직은 아무것도 못 하는 상태이므로
    /// 판정 전이든 후딜레이 중이든 그 공격은 이어지지 않습니다. 하위 상태를 추격으로 되돌리면
    /// <see cref="AttackState.Exit"/>가 판정 콜라이더와 공격 애니메이터 플래그를 정리해 줍니다.
    ///
    /// 하울링도 같은 이유로 취소됩니다. 하울링 시도 기회는 성공·취소와 무관하게 소모되므로(§5.5.1)
    /// 취소해도 규칙이 깨지지 않습니다. 취소하지 않으면 경직 클립이 하울링 위에 얹혔다가
    /// 하울링 도중으로 되돌아가 자세가 어긋납니다.
    /// </remarks>
    private void HandleStaggered()
    {
        if (m_current == Dead)
        {
            return;
        }

        // 공격·하울링 같은 진행 중인 행동을 먼저 정리한 뒤 잠급니다.
        // 순서를 뒤집으면 정리 과정이 다시 이동을 지시해 잠금 직후에 움직입니다.
        //
        // 무엇이 끊겼는지에 따라 경직 이후가 달라지므로(하울링 전 공격이 끊기면 경직 후 하울링, §5.5.2)
        // 취소 판단은 교전 상태가 소유합니다.
        if (m_current == Combat && Combat != null)
        {
            Combat.CancelForStagger();
        }

        StopMoving();

        // 경직 동안 위치의 주인을 루트 모션으로 넘깁니다. Agent가 계속 자기 내부 위치를 transform에 쓰면
        // 밀려난 만큼을 매 프레임 조금씩 되돌려, 결국 클립이 끝난 자리가 아닌 어중간한 지점에 서게 됩니다.
        // 끝날 때 Warp로 Agent를 최종 위치에 맞춰 다시 넘겨줍니다.
        if (agent != null && agent.enabled)
        {
            agent.updatePosition = false;
        }

        m_isStaggered = true;
        m_staggerRootMotionActive = true;
        m_staggerLengthResolved = false;
        m_staggerStartPosition = transform.position;

        // 실제 속도 측정의 기준점입니다. 초기화하지 않으면 첫 프레임 변위가 예전 위치와의 차이로 잡혀
        // 이동 블렌드가 한 프레임 튑니다.
        m_lastLocomotionPosition = transform.position;

        // 애니메이터 스테이트에 들어가기 전까지 쓰는 임시값입니다. 들어간 뒤에는 실제 재생 길이로 다시 잡습니다.
        m_staggerEndTime = Time.time + HitStunDuration;

        // 경직 동안에는 만료 시계를 세웁니다. 행동은 잠기지만 누가 쐈는지까지 잊을 이유는 없습니다.
        // 잠금이 추적 유지 시간보다 길면, 대상을 볼 수 없는 각도에서 맞았을 때 일어나는 순간 대상이 사라져
        // 아무 일도 없었다는 듯 배회로 돌아갑니다.
        targetSensor?.ExtendTrackingHold(HitStunDuration);

        PlayStaggerAnimation();
    }

    /// <summary>
    /// 경직 잠금을 진행시키고, 아직 잠겨 있으면 true를 돌려줍니다.
    /// </summary>
    /// <returns>경직 중이라 이번 프레임의 상태 Tick을 건너뛰어야 하면 true입니다.</returns>
    /// <remarks>
    /// 잠금 시간은 <b>애니메이터가 실제로 재생하는 길이</b>를 따릅니다. 밸런스의 <see cref="HitStunDuration"/>은
    /// 스테이트에 들어가기 전(전이 중)과 애니메이터가 없는 개체를 위한 초기값일 뿐입니다.
    ///
    /// 숫자를 직접 맞춰 두지 않는 이유는 둘이 조용히 어긋나기 때문입니다. 잠금이 짧으면 클립이 중간에 끊겨
    /// 일어나다 만 자세로 복귀하고, 길면 다 일어난 채로 굳어 있습니다. 클립을 교체하거나 스테이트 속도를
    /// 바꿔도 이 방식은 그대로 따라옵니다.
    /// </remarks>
    private bool UpdateStagger()
    {
        if (!m_isStaggered)
        {
            return false;
        }

        ResolveStaggerLengthFromAnimator();

        if (Time.time < m_staggerEndTime)
        {
            return true;
        }

        EndStagger();
        return false;
    }

    /// <summary>
    /// 애니메이터가 경직 스테이트에 들어왔으면 남은 재생 시간으로 잠금 종료 시각을 다시 잡습니다.
    /// </summary>
    /// <remarks>
    /// 진입 전이가 끝나야 스테이트 정보를 읽을 수 있으므로 매 프레임 확인하다가 한 번만 반영합니다.
    /// <c>length</c>는 스테이트 속도가 반영된 실제 재생 길이이며, 남은 시간은 진행도를 빼서 구합니다.
    /// </remarks>
    private void ResolveStaggerLengthFromAnimator()
    {
        if (m_staggerLengthResolved || animator == null)
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != StaggerStateHash)
        {
            return;
        }

        m_staggerLengthResolved = true;

        float remaining = state.length * Mathf.Max(0.0f, 1.0f - state.normalizedTime);
        m_staggerEndTime = Time.time + remaining;
    }

    /// <summary>경직 잠금을 풀고 면역 윈도우를 시작시킵니다.</summary>
    /// <remarks>
    /// 경직 전에 세워 둔 경로는 밀려나기 전 위치를 기준으로 계산된 것이라 그대로 두면 밀려난 만큼을
    /// 되돌아가는 이동이 먼저 나옵니다. 경로를 비워 두면 다음 Tick에서 지금 위치를 기준으로 다시 세웁니다.
    /// </remarks>
    private void EndStagger()
    {
        if (!m_isStaggered)
        {
            return;
        }

        m_isStaggered = false;

        if (m_debugLogStagger)
        {
            Vector3 moved = transform.position - m_staggerStartPosition;
            moved.y = 0.0f;

            // 뒤로 밀렸는지 확인하려면 몸이 보는 방향 기준으로 봐야 합니다. 월드 좌표로는 회전에 따라 부호가 뒤집힙니다.
            float backward = -Vector3.Dot(moved, transform.forward);

            Debug.Log(
                $"[Stagger] 종료 | 후퇴 {backward:F2}m (총 이동 {moved.magnitude:F2}m) " +
                $"| 클립길이반영={m_staggerLengthResolved}",
                this);
        }

        EndStaggerAnimation();

        // 위치 소유권은 여기서 돌려주지 않습니다. 애니메이터가 아직 경직에서 빠져나오는 중이므로,
        // 이탈 블렌드가 끝날 때까지 루트 모션이 위치를 계속 쥐고 있어야 동작이 이어집니다.
        // 인계는 UpdateStaggerHandover가 처리합니다.
        m_staggerHandoverDeadline = Time.time + StaggerHandoverTimeout;

        // 면역 윈도우는 경직이 끝난 시점부터 재므로, 끝났다는 사실을 누적 소유자에게 알려야 합니다.
        enemyHealth?.NotifyStaggerEnded();

        // 잠금이 멈춘 이동을 다시 켭니다. 경직 진입에서 StopMoving을 불렀으므로 여기서 켜 주지 않으면
        // 상태는 그대로인데 발만 멈춘 채로 남습니다.
        // 교전은 하위 상태와 하울링 이어가기까지 함께 판단해야 하므로 교전 상태가 직접 처리합니다(§5.5.2).
        if (m_current == Combat && Combat != null)
        {
            Combat.ResumeAfterStagger();
            return;
        }

        // 배회·소음 추적처럼 하위 상태가 없는 경로입니다. 진입 처리 전체가 아니라 이동만 되살립니다.
        m_current?.ResumeMovement();
    }

    /// <summary>
    /// 남아 있는 루트 모션 변위를 경직 중에만 실제 이동으로 옮깁니다.
    /// </summary>
    /// <remarks>
    /// 경직 클립은 제자리 애니메이션이 아니라 몸이 뒤로 밀려나는 이동을 품고 있습니다(실측 후퇴 약 1.25m).
    /// 그 변위를 버리면 화면에서는 밀려나는데 실제 위치는 그대로여서, 발이 미끄러지고
    /// 사거리 판정도 밀리지 않은 위치를 기준으로 남습니다.
    ///
    /// <b>경직 동안에는 Agent가 아니라 루트 모션이 위치의 주인입니다.</b> <c>updatePosition</c>을 켠 채로 두면
    /// Agent가 매 프레임 <c>transform.position</c>을 자기 내부 위치로 덮어써서, 밀려난 만큼이 조금씩 되돌아가
    /// 클립이 끝난 자리가 아닌 어중간한 지점에 서게 됩니다. 그래서 <see cref="HandleStaggered"/>에서
    /// <c>updatePosition</c>을 끄고, <see cref="EndStagger"/>에서 <c>Warp</c>로 맞춰 돌려줍니다.
    ///
    /// 목표 위치는 <see cref="Animator.rootPosition"/>(루트 모션이 가려는 절대 위치)을 그대로 씁니다.
    /// 프레임 변위를 누적하는 것보다 어긋남이 쌓이지 않습니다.
    ///
    /// <see cref="NavMesh.SamplePosition"/>으로 한 번 보정하는 것은 벽을 뚫고 밀려나거나 바닥 밖으로
    /// 나가는 것을 막기 위해서입니다. 밀리는 거리가 1.25m라 좁은 통로에서는 실제로 벽에 닿습니다.
    ///
    /// <b>이 구간 밖에서는 변위를 버립니다.</b> 이동 클립에도 루트 모션이 들어 있어 그대로 소비하면
    /// NavMeshAgent가 시키는 이동 위에 애니메이션 이동이 겹쳐 두 배로 움직입니다.
    /// 이동의 주인은 여전히 Agent이고, 경직과 그 이탈 블렌드만 예외입니다.
    ///
    /// 구간이 잠금보다 긴 이유는 <see cref="UpdateStaggerHandover"/>에 있습니다. 이탈 블렌드 동안에는
    /// 경직 자세와 이동 자세가 섞이므로, 그 섞인 결과의 루트 모션을 그대로 따라가야 위치가 이어집니다.
    ///
    /// 회전(<see cref="Animator.deltaRotation"/>)은 일부러 쓰지 않습니다. 이 클립의 루트 회전은
    /// 시작과 끝이 같아 순 회전이 0이고(흔들렸다가 제자리로 돌아옴), 몸이 휘청이는 모습은 이미 뼈 애니메이션이
    /// 표현합니다. 굳이 소비하면 Agent의 회전 갱신과 주인이 겹칩니다.
    /// </remarks>
    private void OnAnimatorMove()
    {
        if (!m_staggerRootMotionActive || animator == null)
        {
            return;
        }

        // 밀려난 지점을 NavMesh 위로 당깁니다. 당길 수 없으면 아예 옮기지 않습니다.
        //
        // NavMesh 밖으로 한 발이라도 나가면 그 지점에서 출발하는 경로 계산이 전부 실패하고
        // (EnemyTargetSensor.GetPathDistance가 PathComplete만 인정) 유효 대상이 사라집니다.
        // 그러면 추격이 갈 곳을 잃고 제자리에 굳습니다. 밀리다 마는 것이 굳는 것보다 낫습니다.
        if (!NavMesh.SamplePosition(
                animator.rootPosition, out NavMeshHit hit, StaggerNavMeshSampleRadius, NavMesh.AllAreas))
        {
            return;
        }

        transform.position = hit.position;
    }

    /// <summary>경직 루트 모션 위치를 NavMesh 위로 당길 때 허용하는 반경(m)입니다.</summary>
    /// <remarks>
    /// 후퇴량이 1.25m라 좁은 통로나 바닥 경계에서는 실제로 NavMesh를 벗어납니다.
    /// 벽 쪽으로 스냅되는 것이 NavMesh 밖에 서는 것보다 낫기 때문에 후퇴량보다 넉넉하게 둡니다.
    /// </remarks>
    private const float StaggerNavMeshSampleRadius = 2.0f;

    /// <summary>이동 속도를 애니메이터 MoveSpeed 파라미터로 전달합니다.</summary>
    /// <remarks>
    /// 보통은 <see cref="NavMeshAgent.velocity"/>를 씁니다. 다만 경직과 그 이탈 블렌드 동안에는 위치의 주인이
    /// 루트 모션이라 Agent 속도가 실제 이동과 다릅니다. Agent는 내부 경로만 진행하고 있어서, 그 값을 쓰면
    /// 일어서는 중인데 이동 블렌드가 예전 방향으로 전력 질주를 가리켜 자세가 어긋납니다.
    /// 그 구간에서는 실제 변위로 속도를 재서 넣습니다.
    /// </remarks>
    private void UpdateLocomotionAnimator()
    {
        if (animator == null || agent == null)
        {
            return;
        }

        Vector3 velocity = m_staggerRootMotionActive
            ? (transform.position - m_lastLocomotionPosition) / Mathf.Max(Time.deltaTime, 0.0001f)
            : agent.velocity;

        m_lastLocomotionPosition = transform.position;

        float speed = velocity.magnitude;

        animator.SetFloat(AnimMoveSpeed, speed);

        // 이동 방향을 자기 기준으로 바꿔 2D 블렌드 축에 넣습니다.
        // 월드 방향을 그대로 쓰면 몸이 어디를 보든 같은 값이 되어 옆걸음과 앞걸음을 구분하지 못합니다.
        if (speed <= 0.01f)
        {
            animator.SetFloat(AnimMoveX, 0f);
            animator.SetFloat(AnimMoveZ, 0f);
            return;
        }

        Vector3 local = transform.InverseTransformDirection(velocity / speed);
        animator.SetFloat(AnimMoveX, local.x);
        animator.SetFloat(AnimMoveZ, local.z);
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

    /// <summary>이동 방향의 앞뒤 성분입니다. 2D 이동 블렌드의 세로축이며, 값은 몸 기준 z 성분입니다.</summary>
    /// <remarks>이름이 MoveY였을 때도 담고 있던 값은 z였습니다. 플레이어와 규약을 맞추며 이름을 정정했습니다.</remarks>
    private static readonly int AnimMoveZ = Animator.StringToHash("MoveZ");

    /// <summary>대기 동작 혼합 비율입니다. 개체마다 한 번 뽑아 고정합니다.</summary>
    private static readonly int AnimIdleType = Animator.StringToHash("IdleType");

    /// <summary>애니메이터를 기본 상태로 되돌리는 트리거입니다.</summary>
    private static readonly int AnimReset = Animator.StringToHash("DoReset");

    /// <summary>사망 클립 트리거입니다. 래그돌을 쓸 수 없는 개체의 폴백 연출에만 씁니다.</summary>
    private static readonly int AnimDead = Animator.StringToHash("DoDeath");

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

    /// <summary>경직을 시작하는 트리거입니다. 클립은 `WW_Stagger_Large01`을 씁니다.</summary>
    private static readonly int AnimStagger = Animator.StringToHash("DoStagger");

    /// <summary>경직 스테이트를 유지하는 조건입니다.</summary>
    /// <remarks>
    /// 경직 스테이트의 탈출 전이가 이 값이 false가 되는 것을 조건으로 하고 종료 시점(Exit Time)을 쓰지 않으므로,
    /// 클립을 언제까지 재생할지는 전적으로 코드가 정합니다. 잠금 시간과 클립 길이를 맞춰야 하는 이유가 이것입니다.
    /// </remarks>
    private static readonly int AnimIsStagger = Animator.StringToHash("IsStagger");

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
    /// 사망 클립을 재생합니다. 래그돌을 쓸 수 없는 개체의 폴백 연출입니다.
    /// </summary>
    /// <remarks>
    /// 정상 경로는 래그돌입니다(<see cref="DeadState"/> 주석 참고 — 클립을 쓰면 정해진 방향으로만 쓰러져
    /// 모두 똑같이 죽습니다). 이 함수는 물리 골격이 없는 개체가 마지막 자세로 굳어 버리는 것만 막습니다.
    ///
    /// 사거리 표시를 함께 내리는 이유는, 공격 사거리 안에서 죽으면 그 값이 참인 채로 남아
    /// 사망 클립 위로 공격 대기 자세가 섞이기 때문입니다.
    /// </remarks>
    public void PlayDeathAnimation()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(AnimInAttackRange, false);
        animator.SetBool(AnimIsAttack, false);
        animator.SetTrigger(AnimDead);
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

    /// <summary>경직 애니메이션을 시작합니다.</summary>
    /// <remarks>
    /// 하울링·두리번과 같은 규약입니다. 진입은 트리거, 유지는 bool이 담당합니다.
    /// bool만 세우면 스테이트에 들어가지 못하고, 트리거만 쏘면 탈출 조건이 즉시 성립해 한 프레임 만에 빠져나옵니다.
    ///
    /// <b>파라미터가 없어도 동작해야 합니다.</b> 애니메이터 배선이 없는 개체라도 경직 잠금 자체는 성립해야 하며,
    /// 없는 파라미터에 값을 넣으면 유니티가 매 호출마다 경고를 찍어 콘솔이 묻힙니다.
    /// </remarks>
    public void PlayStaggerAnimation()
    {
        if (animator == null)
        {
            Debug.LogWarning("[Stagger] Animator가 없어 경직 연출을 재생하지 못했습니다. 잠금은 그대로 걸립니다.", this);
            return;
        }

        bool hasBool = HasAnimatorParameter(AnimIsStagger);
        bool hasTrigger = HasAnimatorParameter(AnimStagger);

        if (hasBool)
        {
            animator.SetBool(AnimIsStagger, true);
        }

        if (hasTrigger)
        {
            animator.SetTrigger(AnimStagger);
        }

        // 둘 중 하나라도 없으면 스테이트로 들어가지 못합니다. 잠금만 걸리고 자세는 그대로라
        // "경직이 안 걸린다"로 보이므로, 원인을 파라미터 쪽으로 특정할 수 있게 남깁니다.
        if (!hasBool || !hasTrigger)
        {
            Debug.LogWarning(
                $"[Stagger] 애니메이터 파라미터 누락 (IsStagger={hasBool}, DoStagger={hasTrigger}) - " +
                $"컨트롤러 '{(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "없음")}'",
                this);
        }
    }

    /// <summary>경직 상태에서 빠져나왔음을 애니메이터에 알립니다.</summary>
    public void EndStaggerAnimation()
    {
        if (animator == null || !HasAnimatorParameter(AnimIsStagger))
        {
            return;
        }

        animator.SetBool(AnimIsStagger, false);
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

    /// <summary>공격 판정 시점에 실제 피해를 적용합니다.</summary>
    /// <remarks>
    /// 애니메이션 이벤트에서도 호출되므로 사망 상태에서 들어올 수 있어 먼저 걸러냅니다.
    /// 한 번의 공격에서 판정은 한 번만 일어나야 하므로 소모 여부는 공격 상태가 관리합니다.
    /// </remarks>
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
    ///
    /// 경직은 여기서 다루지 않습니다. 피해와 경직은 서로 다른 값으로 판정되며(피해 0인 경직도, 경직 0인 피해도 성립),
    /// 경직력 누적은 <see cref="EnemyHealth.ApplyStagger"/>가 따로 받습니다. 발동하면 <see cref="HandleStaggered"/>로 옵니다.
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

    /// <summary>
    /// 선택했을 때 반경 계열 수치를 Scene 뷰에 그립니다.
    /// </summary>
    /// <remarks>
    /// 반경은 숫자만 봐서는 넓이가 잡히지 않아 튜닝이 감으로 흐릅니다. 특히 하울링은 한 마리가 얼마나 많은
    /// 동료를 끌어들이는지를 결정하는데, 그 결과는 전파가 일어난 뒤에야 드러나서 값만으로는 판단하기 어렵습니다.
    ///
    /// 시야 반경·각도는 <see cref="EnemyTargetSensor"/>가 이미 그리므로 여기서 중복해서 그리지 않습니다.
    /// 공격 시작 거리도 <see cref="EnemyAttack"/>가 소유합니다. 각 수치를 소유한 컴포넌트가 자기 것을 그립니다.
    ///
    /// <see cref="OnDrawGizmos"/>가 아니라 선택 시에만 그립니다. 씬에 개체가 14마리 넘게 있어 상시로 그리면
    /// 원이 겹쳐 아무것도 못 읽습니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (m_debugDrawHowlRadius)
        {
            // 하울링은 벽을 통과합니다. 원 안이면 그대로 전달되므로 차폐를 반영한 표시를 하지 않습니다.
            Gizmos.color = new Color(1.0f, 0.55f, 0.0f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, howlRadius);
        }

        if (!m_debugDrawWanderRadius)
        {
            return;
        }

        // 배회 기준점은 배회에 진입한 시점의 위치입니다(WanderState). Edit Mode에서는 배치 위치가 곧 기준점이지만,
        // Play 중에는 교전 이탈 지점이 기준이 되어 현재 위치와 다를 수 있습니다.
        Gizmos.color = new Color(0.3f, 0.8f, 1.0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, wanderRadius);

        Gizmos.color = new Color(0.6f, 0.4f, 1.0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, noiseSearchRadius);
    }

    /// <summary>사망 이벤트를 받아 처치 상태로 전이합니다.</summary>
    /// <remarks>
    /// 경직 중에 죽을 수 있으므로 잠금을 먼저 풉니다. 잠금이 남아 있으면 <see cref="DeadState"/>의 Tick이
    /// 남은 경직 시간만큼 돌지 않아 시체 정리가 그만큼 늦어집니다.
    /// 애니메이터 플래그는 되돌리지 않습니다. 사망은 애니메이터를 끄고 래그돌이 이어받으므로 의미가 없습니다.
    /// </remarks>
    private void HandleDied()
    {
        m_isStaggered = false;
        RestoreAgentPositionOwnership();

        TransitionTo(Dead);
    }

    /// <summary>
    /// 경직 중 넘겼던 위치 소유권을 Agent에 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 경직이 정상 종료되지 않는 경로(사망·비활성화)를 위한 것입니다. 여기서 되돌리지 않으면
    /// <c>updatePosition</c>이 꺼진 채로 남아 이후 Agent 이동이 화면에 전혀 반영되지 않습니다.
    /// 제자리에서 미끄러지듯 애니메이션만 도는 증상으로 나타납니다.
    /// </remarks>
    private void RestoreAgentPositionOwnership()
    {
        m_staggerRootMotionActive = false;

        if (agent == null || !agent.enabled || agent.updatePosition)
        {
            return;
        }

        // 루트 모션이 옮겨 놓은 최종 위치로 Agent를 통째로 이동시킨 뒤 소유권을 돌려줍니다.
        // Warp는 내부 위치와 경로 상태를 함께 맞춰 주므로 nextPosition만 대입할 때 남는 어긋남이 없습니다.
        agent.Warp(transform.position);
        agent.updatePosition = true;

        // 그래도 NavMesh 밖이면 되돌립니다. 밖에 서 있으면 경로 계산이 전부 실패해 유효 대상을 잃고,
        // 추격이 갈 곳 없이 제자리에 굳습니다(경직을 두 번 당한 개체가 일어나서 멈춰 있던 증상).
        if (!agent.isOnNavMesh
            && NavMesh.SamplePosition(
                transform.position, out NavMeshHit recovery, StaggerNavMeshSampleRadius, NavMesh.AllAreas))
        {
            agent.Warp(recovery.position);
        }

        if (agent.isOnNavMesh)
        {
            // 경직 전 경로는 밀려나기 전 위치 기준이라 그대로 두면 밀려난 만큼 되돌아가는 이동이 먼저 나옵니다.
            agent.ResetPath();
        }
        else if (m_debugLogStagger)
        {
            Debug.LogWarning("[Stagger] 경직 후 NavMesh 밖에 남았습니다. 추격이 멈출 수 있습니다.", this);
        }
    }

    /// <summary>
    /// 경직 이탈 블렌드가 끝났으면 위치 소유권을 Agent에 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 잠금이 풀린 뒤에도 애니메이터는 이탈 전이(0.25초) 동안 경직 자세에서 빠져나오는 중입니다.
    /// 그 구간의 루트 모션까지 위치에 반영해야 일어서는 동작과 이후 이동이 이어집니다.
    /// 블렌드 중에 Agent가 위치를 끌고 가면 몸이 자세와 어긋나 순간이동처럼 보입니다.
    ///
    /// 블렌드 중에도 <b>행동은 이미 재개된 상태</b>입니다. 잠금은 위치가 아니라 판단을 막는 것이므로,
    /// 이 구간에 추격이 경로를 세워 두면 인계 직후 곧바로 달릴 수 있습니다.
    ///
    /// 감지 실패를 대비해 시간 제한을 둡니다. 애니메이터 배선이 없으면 블렌드 종료를 알 수 없고,
    /// 그대로 두면 Agent 이동이 영구히 화면에 반영되지 않습니다.
    /// </remarks>
    private void UpdateStaggerHandover()
    {
        if (!m_staggerRootMotionActive || m_isStaggered)
        {
            return;
        }

        if (animator != null && Time.time < m_staggerHandoverDeadline)
        {
            // 전이 중이면 아직 경직 자세가 섞여 있습니다.
            if (animator.IsInTransition(0))
            {
                return;
            }

            // 전이가 없더라도 아직 경직 스테이트면 기다립니다.
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == StaggerStateHash)
            {
                return;
            }
        }

        RestoreAgentPositionOwnership();
    }
}
