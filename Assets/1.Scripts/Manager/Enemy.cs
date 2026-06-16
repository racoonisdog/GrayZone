using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public class Enemy : MonoBehaviour
{
    public enum EnemyState
    {
        Wander,
        Alert,
        Chase,
        Attack,
        Hit,
        Dead
    }

    [Header("UI")]
    [SerializeField] private Slider HPBar;

    [Header("Target")]
    [SerializeField] private LayerMask obstacleLayer;
    [SerializeField] private Transform eyePoint;
    [SerializeField] private float targetRefreshInterval = 0.25f;

    [Header("Move")]
    [SerializeField] private float wanderRadius = 8f;
    [SerializeField] private float wanderInterval = 3f;
    [SerializeField] private float wanderSpeed = 1.2f;
    [SerializeField] private float chaseSpeed = 3.2f;
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Detect")]
    [SerializeField] private float sightRange = 12f;
    [SerializeField] private float sightAngle = 120f;
    [SerializeField] private float alertDuration = 0.5f;
    [SerializeField] private float loseSightDelay = 2f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 1.8f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private float attackLockDuration = 0.9f;
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float attackRadius = 1.0f;

    [Header("Hit")]
    [SerializeField] private float hitStunDuration = 0.35f;
    [SerializeField] private float hitStunCooldown = 0.2f;

    [Header("HP")]
    [SerializeField] private float maxHP = 10f;
    [SerializeField] private float destroyDelay = 3f;

    private NavMeshAgent agent;
    private Animator animator;
    private Transform targetPlayer;
    private SquadMemberController_TPZ targetMember;

    private Vector3 spawnPosition;
    private Vector3 currentWanderPoint;

    private float currentHP;
    private float nextWanderTime;
    private float nextAttackTime;
    private float nextHitTime;
    private float lastSeenTime;
    private float nextTargetRefreshTime;

    private bool isStateLocked;
    private bool isDead;
    private bool isProvokedByDamage;
    private bool isAttacking;

    private EnemyState currentState = EnemyState.Wander;

    public EnemyState CurrentState => currentState;
    public float CurrentHP => currentHP;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        spawnPosition = transform.position;
        currentHP = maxHP;

        RefreshTarget(force: true);
        EnterWanderState();
        UpdateHPBar();
    }

    private void Update()
    {
        if (isDead)
            return;

        RefreshTarget();

        UpdateHPBar();
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

    private void RefreshTarget(bool force = false)
    {
        if (!force && Time.time < nextTargetRefreshTime)
            return;

        nextTargetRefreshTime = Time.time + targetRefreshInterval;
        FindBestTarget();
    }

    private void FindBestTarget()
    {
        SquadMemberController_TPZ[] members = FindObjectsByType<SquadMemberController_TPZ>(FindObjectsSortMode.None);

        float closestDistance = Mathf.Infinity;
        SquadMemberController_TPZ closestMember = null;

        for (int i = 0; i < members.Length; i++)
        {
            SquadMemberController_TPZ member = members[i];
            if (member == null) continue;
            if (!member.IsAlive) continue;
            if (member.IsDown) continue;

            float distance = Vector3.Distance(transform.position, member.transform.position);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestMember = member;
            }
        }

        targetMember = closestMember;
        targetPlayer = targetMember != null ? targetMember.transform : null;
    }

    private void UpdateHPBar()
    {
        if (HPBar != null)
        {
            HPBar.value = currentHP / maxHP;
        }
    }

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

    private void UpdateWanderState()
    {
        if (CanSeePlayer())
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
    private void EnterAlertState()
    {
        if (isDead) return;

        currentState = EnemyState.Alert;
        isStateLocked = true;
        isAttacking = false;

        agent.isStopped = true;
        StartCoroutine(AlertRoutine());
    }

    private IEnumerator AlertRoutine()
    {
        float timer = 0f;

        while (timer < alertDuration)
        {
            RefreshTarget(force: true);

            if (targetPlayer != null)
            {
                RotateTowards(targetPlayer.position);
            }

            timer += Time.deltaTime;
            yield return null;
        }

        isStateLocked = false;

        if (CanSeePlayer())
        {
            EnterChaseState();
        }
        else
        {
            EnterWanderState();
        }
    }

    private void UpdateAlertState()
    {
        if (targetPlayer != null)
        {
            RotateTowards(targetPlayer.position);
        }
    }

    // =========================
    // Chase
    // =========================
    private void EnterChaseState()
    {
        if (isDead) return;

        currentState = EnemyState.Chase;
        isAttacking = false;

        if (animator != null)
        {
            if (targetPlayer != null)
            {
                float distance = Vector3.Distance(transform.position, targetPlayer.position);
                animator.SetBool("InAttackRange", distance <= attackRange);
            }
            else
            {
                animator.SetBool("InAttackRange", false);
            }
        }

        agent.isStopped = false;
        agent.speed = chaseSpeed;

        if (targetPlayer != null)
        {
            agent.SetDestination(targetPlayer.position);
            lastSeenTime = Time.time;
        }
    }

    private void UpdateChaseState()
    {
        if (targetPlayer == null || targetMember == null || !targetMember.IsAlive || targetMember.IsDown)
        {
            if (animator != null)
            {
                animator.SetBool("InAttackRange", false);
            }

            RefreshTarget(force: true);

            if (targetPlayer == null)
            {
                EnterWanderState();
                return;
            }
        }

        if (CanSeePlayer())
        {
            lastSeenTime = Time.time;
            agent.SetDestination(targetPlayer.position);
        }

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        bool inAttackRange = distance <= attackRange;

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
    private bool CanAttack()
    {
        if (isDead) return false;
        if (isStateLocked) return false;
        if (Time.time < nextAttackTime) return false;
        if (targetPlayer == null) return false;
        if (targetMember == null) return false;
        if (!targetMember.IsAlive || targetMember.IsDown) return false;

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        return distance <= attackRange;
    }

    private IEnumerator AttackRoutine()
    {
        if (isAttacking || isDead)
            yield break;

        isAttacking = true;
        currentState = EnemyState.Attack;
        isStateLocked = true;

        agent.isStopped = true;
        nextAttackTime = Time.time + attackCooldown;

        if (targetPlayer != null)
        {
            RotateTowards(targetPlayer.position);
        }

        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }

        yield return new WaitForSeconds(attackLockDuration);

        isStateLocked = false;
        isAttacking = false;

        if (isDead)
            yield break;

        RefreshTarget(force: true);

        if (targetPlayer == null)
        {
            EnterWanderState();
            yield break;
        }

        float distance = Vector3.Distance(transform.position, targetPlayer.position);

        if (distance > attackRange)
        {
            EnterChaseState();
        }
    }

    private void UpdateAttackState()
    {
        if (targetPlayer == null || targetMember == null || !targetMember.IsAlive || targetMember.IsDown)
        {
            if (animator != null)
            {
                animator.SetBool("InAttackRange", false);
            }

            RefreshTarget(force: true);

            if (targetPlayer == null)
            {
                EnterWanderState();
                return;
            }
        }

        RotateTowards(targetPlayer.position);

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        bool inAttackRange = distance <= attackRange;

        if (animator != null)
        {
            animator.SetBool("InAttackRange", inAttackRange);
        }

        if (!inAttackRange)
        {
            EnterChaseState();
            return;
        }

        if (!isAttacking && !isStateLocked && Time.time >= nextAttackTime)
        {
            StartCoroutine(AttackRoutine());
        }
    }

    // 애니메이션 이벤트에서 호출
    public void ApplyAttackDamage()
    {
        if (isDead) return;

        Vector3 point = attackPoint != null
            ? attackPoint.position
            : transform.position + transform.forward * 1.0f + Vector3.up * 1.0f;

        Collider[] hits = Physics.OverlapSphere(point, attackRadius);

        for (int i = 0; i < hits.Length; i++)
        {
            SquadMemberController_TPZ member = hits[i].GetComponentInParent<SquadMemberController_TPZ>();
            if (member == null) continue;
            if (!member.IsAlive) continue;
            if (member.IsDown) continue;

            PlayerHealth_TPZ playerHealth = hits[i].GetComponentInParent<PlayerHealth_TPZ>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(attackDamage);
            }

            break;
        }
    }

    // =========================
    // Hit
    // =========================
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

    private IEnumerator HitRoutine()
    {
        yield return new WaitForSeconds(hitStunDuration);

        isStateLocked = false;

        if (isDead)
            yield break;

        RefreshTarget(force: true);

        if (isProvokedByDamage && targetPlayer != null)
        {
            EnterChaseState();
            yield break;
        }

        if (targetPlayer != null && CanSeePlayer())
        {
            EnterChaseState();
        }
        else
        {
            EnterWanderState();
        }
    }

    private void UpdateHitState()
    {
        if (targetPlayer != null)
        {
            RotateTowards(targetPlayer.position);
        }
    }

    // =========================
    // Dead
    // =========================
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

    private IEnumerator DeadRoutine()
    {
        yield return new WaitForSeconds(destroyDelay);
        Destroy(gameObject);
    }

    // =========================
    // Damage
    // =========================
    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (isDead)
            return;

        RefreshTarget(force: true);

        currentHP -= damage;
        currentHP = Mathf.Max(currentHP, 0f);

        UpdateHPBar();

        if (currentHP <= 0f)
        {
            EnterDeadState();
            return;
        }

        isProvokedByDamage = true;
        lastSeenTime = Time.time;

        if (Time.time >= nextHitTime)
        {
            nextHitTime = Time.time + hitStunCooldown;
            EnterHitState();
        }
        else
        {
            if (targetPlayer != null)
            {
                EnterChaseState();
            }
        }
    }

    // =========================
    // Sight
    // =========================
    private bool CanSeePlayer()
    {
        if (targetPlayer == null)
            return false;

        if (targetMember == null)
            return false;

        if (!targetMember.IsAlive || targetMember.IsDown)
            return false;

        Vector3 origin = eyePoint != null
            ? eyePoint.position
            : transform.position + Vector3.up * 1.5f;

        Vector3 target = targetPlayer.position + Vector3.up * 1.0f;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (distance > sightRange)
            return false;

        float angle = Vector3.Angle(transform.forward, direction.normalized);
        if (angle > sightAngle * 0.5f)
            return false;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, sightRange, ~0))
        {
            SquadMemberController_TPZ member = hit.transform.GetComponentInParent<SquadMemberController_TPZ>();
            if (member != null && member == targetMember)
            {
                return true;
            }

            if (((1 << hit.collider.gameObject.layer) & obstacleLayer) != 0)
            {
                return false;
            }
        }

        return false;
    }

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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);

        Vector3 point = attackPoint != null
            ? attackPoint.position
            : transform.position + transform.forward * 1.0f + Vector3.up * 1.0f;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(point, attackRadius);
    }
}