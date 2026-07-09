using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 현재 조작 중인 스쿼드 리더를 따라 이동하는 AI 추종 컴포넌트입니다.
/// </summary>
/// <remarks>
/// NavMeshAgent를 사용하여 리더 뒤쪽의 지정된 오프셋 위치를 따라가며,
/// 이동 속도에 따라 애니메이터의 Speed 파라미터를 갱신합니다.
/// </remarks>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(SquadMemberController))]
public class SquadFollowerAI : MonoBehaviour
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
    [Tooltip("리더와 이 거리보다 멀어지면 추종을 시작합니다.")]
    [FormerlySerializedAs("followDistance")]
    [SerializeField] private float m_followDistance = 2.5f;

    [Tooltip("리더와 이 거리 이하가 되면 이동을 멈춥니다.")]
    [FormerlySerializedAs("stopDistance")]
    [SerializeField] private float m_stopDistance = 1.5f;

    [Tooltip("목표 위치를 다시 계산하는 주기입니다.")]
    [FormerlySerializedAs("updateInterval")]
    [SerializeField] private float m_updateInterval = 0.15f;

    [Tooltip("리더 기준 추종 위치 오프셋입니다.")]
    [FormerlySerializedAs("followOffset")]
    [SerializeField] private Vector3 m_followOffset = new Vector3(0f, 0f, -2f);

    [Foldout("Move Options")]
    [Tooltip("이동 방향으로 회전하는 속도입니다.")]
    [FormerlySerializedAs("rotationSpeed")]
    [SerializeField] private float m_rotationSpeed = 10f;

    private SquadMemberController m_memberController;
    private NavMeshAgent m_agent;
    private SquadManager m_squadManager;
    private Animator m_animator;

    private float m_nextUpdateTime;
    private bool m_hasRequiredReferences;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");

    /// <summary>리더 추종 시작 거리입니다.</summary>
    public float FollowDistance => m_followDistance;

    /// <summary>리더 추종 정지 거리입니다.</summary>
    public float StopDistance => m_stopDistance;

    /// <summary>목표 위치 갱신 주기입니다.</summary>
    public float UpdateInterval => m_updateInterval;

    /// <summary>리더 기준 추종 오프셋입니다.</summary>
    public Vector3 FollowOffset => m_followOffset;

    /// <summary>이동 방향으로 회전하는 속도입니다.</summary>
    public float RotationSpeed => m_rotationSpeed;

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
    }

    /// <summary>
    /// 비활성화될 때 남아 있는 경로와 이동 애니메이션 값을 정리합니다.
    /// </summary>
    private void OnDisable()
    {
        StopAgent();
        UpdateMoveAnimation(0f);
    }

    /// <summary>
    /// 리더 위치를 주기적으로 갱신하고, 이동 방향 회전과 애니메이션 파라미터를 처리합니다.
    /// </summary>
    private void Update()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        if (!CanFollow(out Transform leader))
        {
            UpdateMoveAnimation(0f);
            return;
        }

        if (leader == transform)
        {
            StopAgent();
            UpdateMoveAnimation(0f);
            return;
        }

        if (Time.time >= m_nextUpdateTime)
        {
            m_nextUpdateTime = Time.time + m_updateInterval;
            UpdateFollowTarget(leader);
        }

        HandleRotation();
        HandleAnimation();
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
            Debug.LogError("[SquadFollowerAI] SquadMemberController 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        if (m_agent == null)
        {
            Debug.LogError("[SquadFollowerAI] NavMeshAgent 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        if (m_squadManager == null)
        {
            Debug.LogError("[SquadFollowerAI] 씬에서 SquadManager를 찾지 못했습니다.", this);
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

        if (m_squadManager == null || m_squadManager.CurrentMember == null)
        {
            return false;
        }

        if (m_agent == null || !m_agent.enabled)
        {
            return false;
        }

        leader = m_squadManager.CurrentMember.transform;
        return leader != null;
    }

    /// <summary>
    /// 리더 위치와 오프셋을 기준으로 NavMeshAgent의 목적지를 갱신합니다.
    /// </summary>
    /// <param name="leader">따라갈 리더 Transform입니다.</param>
    private void UpdateFollowTarget(Transform leader)
    {
        Vector3 targetPosition = leader.position
                               - leader.forward * Mathf.Abs(m_followOffset.z)
                               + leader.right * m_followOffset.x;

        float distanceToLeader = Vector3.Distance(transform.position, leader.position);

        if (distanceToLeader > m_followDistance)
        {
            m_agent.isStopped = false;
            m_agent.SetDestination(targetPosition);
        }
        else if (distanceToLeader <= m_stopDistance)
        {
            StopAgent();
        }
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
            UpdateMoveAnimation(0f);
            return;
        }

        Vector3 velocity = m_agent.velocity;
        velocity.y = 0f;

        UpdateMoveAnimation(velocity.magnitude);
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
    /// 애니메이터의 Speed 파라미터를 갱신합니다.
    /// </summary>
    /// <param name="speed">현재 수평 이동 속도입니다.</param>
    private void UpdateMoveAnimation(float speed)
    {
        if (m_animator == null)
        {
            return;
        }

        m_animator.SetFloat(SpeedHash, speed);
        m_animator.SetFloat(MotionSpeedHash, 1.0f);
    }

    /// <summary>
    /// 리더 추종 시작 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 추종 시작 거리입니다.</param>
    public void SetFollowDistance(float value)
    {
        m_followDistance = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 리더 추종 정지 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 추종 정지 거리입니다.</param>
    public void SetStopDistance(float value)
    {
        m_stopDistance = Mathf.Max(0.0f, value);
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
    /// 리더 기준 추종 오프셋을 설정합니다.
    /// </summary>
    /// <param name="value">새 추종 오프셋입니다.</param>
    public void SetFollowOffset(Vector3 value)
    {
        m_followOffset = value;
    }

    /// <summary>
    /// 이동 방향 회전 속도를 설정합니다.
    /// </summary>
    /// <param name="value">새 회전 속도입니다.</param>
    public void SetRotationSpeed(float value)
    {
        m_rotationSpeed = Mathf.Max(0.0f, value);
    }
}
