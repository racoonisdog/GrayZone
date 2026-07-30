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
    [Header("Balance Data")]
    [Tooltip("선택 사항인 적 밸런스 데이터입니다. 지정하면 아래 레거시 기본값보다 우선 적용됩니다.")]
    [FormerlySerializedAs("m_balance")]
    [SerializeField] private EnemyBalanceSO m_balanceSO;

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

    [Header("Detect")]
    /// <summary>대상을 처음 발견했을 때 경계 상태에 머무는 시간입니다.</summary>
    [SerializeField] private float alertDuration = 0.5f;

    /// <summary>시야에서 벗어난 뒤에도 대상의 실시간 위치를 계속 아는 시간입니다.</summary>
    [SerializeField] private float loseSightDelay = 2f;

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

    [Header("Dead")]
    /// <summary>사망 애니메이션 이후 Enemy 오브젝트를 제거하기까지 기다리는 시간입니다.</summary>
    [SerializeField] private float destroyDelay = 3f;

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

    // =========================
    // 최상위 상태 인스턴스 (상태 간 전이에 사용)
    // =========================
    /// <summary>비전투 배회 상태입니다.</summary>
    public WanderState Wander { get; private set; }

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

    /// <summary>사망 후 제거까지 대기 시간입니다.</summary>
    public float DestroyDelay => destroyDelay;

    /// <summary>현재 적용 대상으로 지정된 적 밸런스 데이터입니다.</summary>
    public EnemyBalanceSO Balance => m_balanceSO;

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
        alertDuration = balance.AlertDuration;
        loseSightDelay = balance.LoseSightDelay;
        targetReevaluateInterval = balance.TargetReevaluateInterval;
        targetSwitchPathDistanceDelta = balance.TargetSwitchPathDistanceDelta;
        attackDirectionLockTime = balance.AttackDirectionLockTime;
        attackImpactTime = balance.AttackImpactTime;
        attackRecoveryDuration = balance.AttackRecoveryDuration;
        hitStunDuration = balance.HitStunDuration;
        hitStunCooldown = balance.HitStunCooldown;
        destroyDelay = balance.DestroyDelay;

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
        if (animator != null && agent != null)
        {
            animator.SetFloat(AnimMoveSpeed, agent.velocity.magnitude);
        }
    }

    // =========================
    // 애니메이터 연동
    // 파라미터 이름을 여기 모아 두어 상태 클래스들이 같은 문자열을 각자 들고 있지 않게 합니다.
    // =========================
    private const string AnimMoveSpeed = "MoveSpeed";
    private const string AnimInAttackRange = "InAttackRange";
    private const string AnimAttack = "Attack";
    private const string AnimDead = "Dead";

    /// <summary>공격 애니메이션을 재생합니다.</summary>
    public void PlayAttackAnimation()
    {
        animator?.SetTrigger(AnimAttack);
    }

    /// <summary>사망 애니메이션을 재생합니다.</summary>
    public void PlayDeathAnimation()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(AnimInAttackRange, false);
        animator.SetTrigger(AnimDead);
    }

    /// <summary>공격 사거리 안에 있는지를 애니메이터에 전달합니다.</summary>
    /// <param name="inRange">사거리 안이면 true입니다.</param>
    public void SetInAttackRangeAnimation(bool inRange)
    {
        animator?.SetBool(AnimInAttackRange, inRange);
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
    public void ApplyAttackDamage()
    {
        if (m_current == Dead)
        {
            return;
        }

        Combat?.Attack?.NotifyAnimationImpact();
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
    }

    /// <summary>상태 인스턴스를 생성합니다.</summary>
    private void CreateStates()
    {
        Wander = new WanderState(this);
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
