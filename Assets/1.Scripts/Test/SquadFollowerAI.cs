using UnityEngine;
using UnityEngine.AI;

public class SquadFollowerAI : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private float followDistance = 2.5f;
    [SerializeField] private float stopDistance = 1.5f;
    [SerializeField] private float updateInterval = 0.15f;
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -2f);

    [Header("Move")]
    [SerializeField] private float rotationSpeed = 10f;

    private SquadMemberController memberController;
    private NavMeshAgent agent;
    private SquadManager squadManager;
    private Animator animator;

    private float nextUpdateTime;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        memberController = GetComponent<SquadMemberController>();
        agent = GetComponent<NavMeshAgent>();
        squadManager = FindFirstObjectByType<SquadManager>();
        animator = GetComponent<Animator>();

        if (agent != null)
        {
            agent.updateRotation = false;
        }
    }

    private void OnDisable()
    {
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        UpdateMoveAnimation(0f);
    }

    private void Update()
    {
        if (memberController == null) return;
        if (!memberController.IsAlive) return;
        if (memberController.IsDown) return;
        if (squadManager == null) return;
        if (squadManager.CurrentMember == null) return;
        if (agent == null) return;
        if (!agent.enabled) return;

        Transform leader = squadManager.CurrentMember.transform;

        if (leader == transform)
        {
            agent.isStopped = true;
            agent.ResetPath();
            UpdateMoveAnimation(0f);
            return;
        }

        if (Time.time >= nextUpdateTime)
        {
            nextUpdateTime = Time.time + updateInterval;
            UpdateFollowTarget(leader);
        }

        HandleRotation();
        HandleAnimation();
    }

    private void UpdateFollowTarget(Transform leader)
    {
        Vector3 targetPos = leader.position
                          - leader.forward * Mathf.Abs(followOffset.z)
                          + leader.right * followOffset.x;

        float distanceToLeader = Vector3.Distance(transform.position, leader.position);

        if (distanceToLeader > followDistance)
        {
            agent.isStopped = false;
            agent.SetDestination(targetPos);
        }
        else if (distanceToLeader <= stopDistance)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void HandleRotation()
    {
        if (agent == null) return;
        if (agent.isStopped) return;

        Vector3 velocity = agent.velocity;
        velocity.y = 0f;

        if (velocity.sqrMagnitude < 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );
    }

    private void HandleAnimation()
    {
        if (agent == null)
        {
            UpdateMoveAnimation(0f);
            return;
        }

        Vector3 velocity = agent.velocity;
        velocity.y = 0f;

        float speed = velocity.magnitude;
        UpdateMoveAnimation(speed);
    }

    private void UpdateMoveAnimation(float speed)
    {
        if (animator == null) return;
        animator.SetFloat(SpeedHash, speed);
    }
}