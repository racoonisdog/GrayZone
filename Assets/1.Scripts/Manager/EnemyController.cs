using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy의 탐색, 경계, 추적, 공격, 피격, 사망 상태 전환을 제어하는 메인 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 대상 탐지는 <see cref="EnemyTargetSensor"/>, 공격 판정은 <see cref="EnemyAttack"/>, 체력 처리는 <see cref="EnemyHealth"/>에 위임합니다.
/// </remarks>
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyTargetSensor))]
[RequireComponent(typeof(EnemyAttack))]
public class EnemyController : MonoBehaviour
{
    /// <summary>
    /// Enemy가 현재 수행 중인 행동 상태입니다.
    /// </summary>
    public enum EnemyState
    {
        /// <summary>스폰 지점 주변을 배회하는 상태입니다.</summary>
        Wander,

        /// <summary>대상을 발견한 직후 잠시 바라보며 반응하는 상태입니다.</summary>
        Alert,

        /// <summary>현재 대상을 추적하는 상태입니다.</summary>
        Chase,

        /// <summary>공격 애니메이션과 피해 판정을 처리하는 상태입니다.</summary>
        Attack,

        /// <summary>피격 경직을 처리하는 상태입니다.</summary>
        Hit,

        /// <summary>사망 후 정리 루틴을 처리하는 상태입니다.</summary>
        Dead
    }

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

    /// <summary>대상을 마지막으로 본 뒤 추적을 포기하기까지의 지연 시간입니다.</summary>
    [SerializeField] private float loseSightDelay = 2f;

    [Header("Hit")]
    /// <summary>피격 상태에서 이동과 상태 전환을 잠그는 시간입니다.</summary>
    [SerializeField] private float hitStunDuration = 0.35f;

    /// <summary>연속 피격 시 Hit 상태를 다시 시작할 수 있는 최소 간격입니다.</summary>
    [SerializeField] private float hitStunCooldown = 0.2f;

    [Header("Dead")]
    /// <summary>사망 애니메이션 이후 Enemy 오브젝트를 제거하기까지 기다리는 시간입니다.</summary>
    [SerializeField] private float destroyDelay = 3f;

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

    /// <summary>배회 기준점으로 사용하는 최초 활성 위치입니다.</summary>
    private Vector3 spawnPosition;

    /// <summary>현재 NavMeshAgent에 지정된 배회 목적지입니다.</summary>
    private Vector3 currentWanderPoint;

    /// <summary>다음 배회 목적지 갱신 가능 시각입니다.</summary>
    private float nextWanderTime;

    /// <summary>다음 피격 경직 진입 가능 시각입니다.</summary>
    private float nextHitTime;

    /// <summary>대상을 마지막으로 시야 안에서 확인한 시각입니다.</summary>
    private float lastSeenTime;

    /// <summary>공격, 경계, 피격처럼 짧은 루틴 동안 상태 전환을 막는 플래그입니다.</summary>
    private bool isStateLocked;

    /// <summary>사망 루틴이 시작되었는지 여부입니다.</summary>
    private bool isDead;

    /// <summary>시야 밖에서 피해를 받아도 추적 상태로 전환해야 하는지 여부입니다.</summary>
    private bool isProvokedByDamage;

    /// <summary>공격 코루틴이 이미 진행 중인지 여부입니다.</summary>
    private bool isAttacking;

    /// <summary>현재 Enemy 상태입니다.</summary>
    private EnemyState currentState = EnemyState.Wander;

    /// <summary>외부에서 읽을 수 있는 현재 Enemy 상태입니다.</summary>
    public EnemyState CurrentState => currentState;

    /// <summary>현재 체력입니다. 체력 컴포넌트가 없으면 0을 반환합니다.</summary>
    public int CurrentHP => enemyHealth != null ? enemyHealth.CurrentHP : 0;

    /// <summary>현재 유효한 추적 대상 스쿼드 멤버입니다.</summary>
    private SquadMemberController CurrentTarget => targetSensor != null ? targetSensor.CurrentTarget : null;

    /// <summary>현재 유효한 추적 대상의 Transform입니다.</summary>
    private Transform CurrentTargetTransform => targetSensor != null ? targetSensor.CurrentTargetTransform : null;

    /// <summary>
    /// 필수 컴포넌트 참조를 캐싱합니다.
    /// </summary>
    private void Awake()
    {
        CacheReferences();
    }

    /// <summary>
    /// 활성화될 때 체력 이벤트를 구독합니다.
    /// </summary>
    private void OnEnable()
    {
        CacheReferences();

        if (enemyHealth == null)
        {
            return;
        }

        enemyHealth.OnDamaged += HandleDamaged;
        enemyHealth.OnDied += HandleDied;
    }

    /// <summary>
    /// 비활성화될 때 체력 이벤트 구독을 해제합니다.
    /// </summary>
    private void OnDisable()
    {
        if (enemyHealth == null)
        {
            return;
        }

        enemyHealth.OnDamaged -= HandleDamaged;
        enemyHealth.OnDied -= HandleDied;
    }

    /// <summary>
    /// 스폰 위치를 기록하고 초기 대상을 찾은 뒤 배회 상태로 진입합니다.
    /// </summary>
    private void Start()
    {
        CacheReferences();

        spawnPosition = transform.position;

        RefreshTarget(force: true);
        EnterWanderState();
    }

    /// <summary>
    /// 현재 상태에 맞는 Enemy 행동 루프를 갱신합니다.
    /// </summary>
    private void Update()
    {
        if (isDead)
            return;

        RefreshTarget();

        UpdateAnimatorMoveSpeed();

        switch (currentState)
        {
            case EnemyState.Wander:
                UpdateWanderState();
                break;

            case EnemyState.Alert:
                UpdateAlertState();
                break;

            case EnemyState.Chase:
                UpdateChaseState();
                break;

            case EnemyState.Attack:
                UpdateAttackState();
                break;

            case EnemyState.Hit:
                UpdateHitState();
                break;
        }
    }

    /// <summary>
    /// 대상 센서가 추적 대상을 다시 평가하도록 요청합니다.
    /// </summary>
    /// <param name="force">true이면 센서의 갱신 주기를 무시하고 즉시 갱신합니다.</param>
    private void RefreshTarget(bool force = false)
    {
        if (targetSensor == null)
        {
            return;
        }

        targetSensor.RefreshTarget(force);
    }

    /// <summary>
    /// Enemy 동작에 필요한 컴포넌트를 찾고 없으면 보조 모듈을 추가합니다.
    /// </summary>
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

    /// <summary>
    /// NavMeshAgent 속도를 Animator MoveSpeed 파라미터로 전달합니다.
    /// </summary>
    private void UpdateAnimatorMoveSpeed()
    {
        if (animator != null && agent != null)
        {
            animator.SetFloat("MoveSpeed", agent.velocity.magnitude);
        }
    }

    // =========================
    // Wander
    // =========================
    /// <summary>
    /// 배회 상태로 진입하고 새로운 배회 목적지를 설정합니다.
    /// </summary>
    private void EnterWanderState()
    {
        if (isDead) return;

        currentState = EnemyState.Wander;
        isStateLocked = false;
        isProvokedByDamage = false;
        isAttacking = false;

        if (animator != null)
        {
            animator.SetBool("InAttackRange", false);
        }

        agent.isStopped = false;
        agent.speed = wanderSpeed;

        SetNewWanderDestination();
        nextWanderTime = Time.time + wanderInterval;
    }

    /// <summary>
    /// 배회 중 대상 발견 여부와 목적지 갱신 시점을 확인합니다.
    /// </summary>
    private void UpdateWanderState()
    {
        if (CanSeeTarget())
        {
            EnterAlertState();
            return;
        }

        if (!agent.pathPending)
        {
            if (agent.remainingDistance <= agent.stoppingDistance + 0.2f || Time.time >= nextWanderTime)
            {
                SetNewWanderDestination();
                nextWanderTime = Time.time + wanderInterval;
            }
        }
    }

    /// <summary>
    /// 스폰 위치 주변의 NavMesh 위에서 새 배회 목적지를 선택합니다.
    /// </summary>
    private void SetNewWanderDestination()
    {
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += spawnPosition;
        randomDirection.y = transform.position.y;

        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            currentWanderPoint = hit.position;
            agent.SetDestination(currentWanderPoint);
        }
        else
        {
            currentWanderPoint = transform.position;
            agent.SetDestination(currentWanderPoint);
        }
    }

    // =========================
    // Alert
    // =========================
    /// <summary>
    /// 경계 상태로 진입해 이동을 멈추고 대상을 바라보는 루틴을 시작합니다.
    /// </summary>
    private void EnterAlertState()
    {
        if (isDead) return;

        currentState = EnemyState.Alert;
        isStateLocked = true;
        isAttacking = false;

        agent.isStopped = true;
        StartCoroutine(AlertRoutine());
    }

    /// <summary>
    /// 경계 시간 동안 대상을 바라본 뒤 시야 여부에 따라 추적 또는 배회로 전환합니다.
    /// </summary>
    private IEnumerator AlertRoutine()
    {
        float timer = 0f;

        while (timer < alertDuration)
        {
            RefreshTarget(force: true);

            Transform targetTransform = CurrentTargetTransform;
            if (targetTransform != null)
            {
                RotateTowards(targetTransform.position);
            }

            timer += Time.deltaTime;
            yield return null;
        }

        isStateLocked = false;

        if (CanSeeTarget())
        {
            EnterChaseState();
        }
        else
        {
            EnterWanderState();
        }
    }

    /// <summary>
    /// 경계 상태가 유지되는 동안 현재 대상을 바라봅니다.
    /// </summary>
    private void UpdateAlertState()
    {
        Transform targetTransform = CurrentTargetTransform;
        if (targetTransform != null)
        {
            RotateTowards(targetTransform.position);
        }
    }

    // =========================
    // Chase
    // =========================
    /// <summary>
    /// 추적 상태로 진입하고 현재 대상 위치를 NavMeshAgent 목적지로 설정합니다.
    /// </summary>
    private void EnterChaseState()
    {
        if (isDead) return;

        currentState = EnemyState.Chase;
        isAttacking = false;

        SquadMemberController target = CurrentTarget;
        Transform targetTransform = CurrentTargetTransform;
        bool inAttackRange = enemyAttack != null && enemyAttack.IsTargetInRange(transform.position, target);

        if (animator != null)
        {
            animator.SetBool("InAttackRange", inAttackRange);
        }

        agent.isStopped = false;
        agent.speed = chaseSpeed;

        if (targetTransform != null)
        {
            agent.SetDestination(targetTransform.position);
            lastSeenTime = Time.time;
        }
    }

    /// <summary>
    /// 대상 위치, 시야 유지, 공격 거리 진입 여부를 갱신합니다.
    /// </summary>
    private void UpdateChaseState()
    {
        SquadMemberController target = CurrentTarget;
        Transform targetTransform = CurrentTargetTransform;

        if (target == null || targetTransform == null)
        {
            if (animator != null)
            {
                animator.SetBool("InAttackRange", false);
            }

            RefreshTarget(force: true);
            target = CurrentTarget;
            targetTransform = CurrentTargetTransform;

            if (target == null || targetTransform == null)
            {
                EnterWanderState();
                return;
            }
        }

        if (CanSeeTarget())
        {
            lastSeenTime = Time.time;
            agent.SetDestination(targetTransform.position);
        }

        bool inAttackRange = enemyAttack != null && enemyAttack.IsTargetInRange(transform.position, target);

        if (animator != null)
        {
            animator.SetBool("InAttackRange", inAttackRange);
        }

        if (inAttackRange)
        {
            if (CanAttack())
            {
                StartCoroutine(AttackRoutine());
            }
            return;
        }

        if (Time.time - lastSeenTime > loseSightDelay)
        {
            EnterWanderState();
        }
    }

    // =========================
    // Attack
    // =========================
    /// <summary>
    /// 현재 상태와 공격 모듈 기준으로 공격을 시작할 수 있는지 확인합니다.
    /// </summary>
    private bool CanAttack()
    {
        if (isDead) return false;
        if (isStateLocked) return false;

        return enemyAttack != null && enemyAttack.CanAttack(transform.position, CurrentTarget);
    }

    /// <summary>
    /// 공격 상태를 잠그고 공격 애니메이션 트리거와 쿨다운을 처리합니다.
    /// </summary>
    private IEnumerator AttackRoutine()
    {
        if (isAttacking || isDead)
            yield break;

        isAttacking = true;
        currentState = EnemyState.Attack;
        isStateLocked = true;

        agent.isStopped = true;
        enemyAttack?.StartCooldown();

        Transform targetTransform = CurrentTargetTransform;
        if (targetTransform != null)
        {
            RotateTowards(targetTransform.position);
        }

        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }

        float lockDuration = enemyAttack != null ? enemyAttack.AttackLockDuration : 0.0f;
        yield return new WaitForSeconds(lockDuration);

        isStateLocked = false;
        isAttacking = false;

        if (isDead)
            yield break;

        RefreshTarget(force: true);
        targetTransform = CurrentTargetTransform;

        if (targetTransform == null)
        {
            EnterWanderState();
            yield break;
        }

        if (enemyAttack == null || !enemyAttack.IsTargetInRange(transform.position, CurrentTarget))
        {
            EnterChaseState();
        }
    }

    /// <summary>
    /// 공격 상태에서 대상 이탈, 재공격 가능 여부, 추적 전환 여부를 갱신합니다.
    /// </summary>
    private void UpdateAttackState()
    {
        SquadMemberController target = CurrentTarget;
        Transform targetTransform = CurrentTargetTransform;

        if (target == null || targetTransform == null)
        {
            if (animator != null)
            {
                animator.SetBool("InAttackRange", false);
            }

            RefreshTarget(force: true);
            target = CurrentTarget;
            targetTransform = CurrentTargetTransform;

            if (target == null || targetTransform == null)
            {
                EnterWanderState();
                return;
            }
        }

        RotateTowards(targetTransform.position);

        bool inAttackRange = enemyAttack != null && enemyAttack.IsTargetInRange(transform.position, target);

        if (animator != null)
        {
            animator.SetBool("InAttackRange", inAttackRange);
        }

        if (!inAttackRange)
        {
            EnterChaseState();
            return;
        }

        if (!isAttacking && !isStateLocked && enemyAttack != null && enemyAttack.IsCooldownComplete)
        {
            StartCoroutine(AttackRoutine());
        }
    }

    /// <summary>
    /// 공격 애니메이션 이벤트에서 호출되어 실제 피해 판정을 수행합니다.
    /// </summary>
    public void ApplyAttackDamage()
    {
        if (isDead) return;

        enemyAttack?.ApplyAttackDamage(CurrentTarget);
    }

    // =========================
    // Hit
    // =========================
    /// <summary>
    /// 피격 상태로 진입하고 경직 애니메이션과 복귀 루틴을 시작합니다.
    /// </summary>
    private void EnterHitState()
    {
        if (isDead) return;

        currentState = EnemyState.Hit;
        isStateLocked = true;
        isAttacking = false;
        agent.isStopped = true;

        if (animator != null)
        {
            animator.SetTrigger("Hit");
        }

        StartCoroutine(HitRoutine());
    }

    /// <summary>
    /// 피격 경직이 끝난 뒤 도발 여부와 시야 상태에 따라 다음 상태를 결정합니다.
    /// </summary>
    private IEnumerator HitRoutine()
    {
        yield return new WaitForSeconds(hitStunDuration);

        isStateLocked = false;

        if (isDead)
            yield break;

        RefreshTarget(force: true);

        Transform targetTransform = CurrentTargetTransform;
        if (isProvokedByDamage && targetTransform != null)
        {
            EnterChaseState();
            yield break;
        }

        if (targetTransform != null && CanSeeTarget())
        {
            EnterChaseState();
        }
        else
        {
            EnterWanderState();
        }
    }

    /// <summary>
    /// 피격 상태가 유지되는 동안 현재 대상을 바라봅니다.
    /// </summary>
    private void UpdateHitState()
    {
        Transform targetTransform = CurrentTargetTransform;
        if (targetTransform != null)
        {
            RotateTowards(targetTransform.position);
        }
    }

    // =========================
    // Dead
    // =========================
    /// <summary>
    /// 사망 상태로 진입하고 이동, 충돌, 공격 판정을 정리합니다.
    /// </summary>
    private void EnterDeadState()
    {
        if (isDead) return;

        isDead = true;
        currentState = EnemyState.Dead;
        isStateLocked = true;
        isAttacking = false;

        if (animator != null)
        {
            animator.SetBool("InAttackRange", false);
            animator.SetTrigger("Dead");
        }

        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        StartCoroutine(DeadRoutine());
    }

    /// <summary>
    /// 사망 연출 시간을 기다린 뒤 Enemy 오브젝트를 제거합니다.
    /// </summary>
    private IEnumerator DeadRoutine()
    {
        yield return new WaitForSeconds(destroyDelay);
        Destroy(gameObject);
    }

    /// <summary>
    /// 피격 이벤트를 받아 도발 상태를 기록하고 Hit 또는 Chase 상태로 전환합니다.
    /// </summary>
    /// <param name="damage">이번에 적용된 피해량입니다.</param>
    private void HandleDamaged(int damage)
    {
        if (isDead || enemyHealth == null || enemyHealth.CurrentHP <= 0)
        {
            return;
        }

        RefreshTarget(force: true);

        isProvokedByDamage = true;
        lastSeenTime = Time.time;

        if (Time.time >= nextHitTime)
        {
            nextHitTime = Time.time + hitStunCooldown;
            EnterHitState();
        }
        else
        {
            if (CurrentTargetTransform != null)
            {
                EnterChaseState();
            }
        }
    }

    /// <summary>
    /// 사망 이벤트를 받아 Dead 상태로 전환합니다.
    /// </summary>
    private void HandleDied()
    {
        EnterDeadState();
    }

    // =========================
    // Sight
    // =========================
    /// <summary>
    /// 현재 대상이 EnemyTargetSensor 기준으로 보이는지 확인합니다.
    /// </summary>
    private bool CanSeeTarget()
    {
        return targetSensor != null && targetSensor.CanSeeCurrentTarget();
    }

    /// <summary>
    /// 수평 방향만 사용해 지정 위치를 향하도록 Enemy를 회전시킵니다.
    /// </summary>
    /// <param name="targetPosition">바라볼 월드 좌표입니다.</param>
    private void RotateTowards(Vector3 targetPosition)
    {
        Vector3 lookDirection = targetPosition - transform.position;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );
    }

}
