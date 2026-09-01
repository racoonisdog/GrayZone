using UnityEngine;

[RequireComponent(typeof(Animator))]
public sealed class ShelterPatrolController : MonoBehaviour
{
    private enum PatrolState
    {
        Walking,
        Turning
    }

    private static readonly int TurnLeft180Hash = Animator.StringToHash("TurnLeft180");
    private static readonly int TurnRight180Hash = Animator.StringToHash("TurnRight180");
    private static readonly int TurnLeft180StateHash = Animator.StringToHash("TurnLeft180");
    private static readonly int TurnRight180StateHash = Animator.StringToHash("TurnRight180");

    private const int RequiredPointCount = 3;

    [Header("Route")]
    [SerializeField] private PatrolPoint[] m_patrolPoints;

    [Header("Movement")]
    [Min(0.01f)]
    [SerializeField] private float m_moveSpeed = 1.5f;

    [Min(0.001f)]
    [SerializeField] private float m_arrivalDistance = 0.05f;

    [Header("Grounding")]
    [SerializeField] private LayerMask m_groundLayers = Physics.DefaultRaycastLayers;

    [Min(0.01f)]
    [SerializeField] private float m_groundProbeHeight = 1.5f;

    [Min(0.01f)]
    [SerializeField] private float m_groundProbeDistance = 3f;

    [SerializeField] private float m_groundOffset;

    private Animator m_animator;
    private PatrolState m_state;
    private int m_currentPointIndex;
    private int m_direction;
    private int m_activeTurnStateHash;
    private bool m_enteredTurnState;
    private bool m_initialized;

    private void Awake()
    {
        m_animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        InitializePatrol();
    }

    private void Update()
    {
        if (!m_initialized)
        {
            return;
        }

        switch (m_state)
        {
            case PatrolState.Walking:
                UpdateWalking();
                break;
            case PatrolState.Turning:
                UpdateTurning();
                break;
        }
    }

    private void OnAnimatorMove()
    {
        if (m_initialized && m_state == PatrolState.Turning)
        {
            transform.rotation *= m_animator.deltaRotation;
        }
    }

    private void OnValidate()
    {
        m_moveSpeed = Mathf.Max(0.01f, m_moveSpeed);
        m_arrivalDistance = Mathf.Max(0.001f, m_arrivalDistance);
        m_groundProbeHeight = Mathf.Max(0.01f, m_groundProbeHeight);
        m_groundProbeDistance = Mathf.Max(0.01f, m_groundProbeDistance);
    }

    private void InitializePatrol()
    {
        m_initialized = HasValidRoute();
        if (!m_initialized)
        {
            return;
        }

        m_currentPointIndex = 0;
        m_direction = 1;
        m_activeTurnStateHash = 0;
        m_enteredTurnState = false;

        m_animator.ResetTrigger(TurnLeft180Hash);
        m_animator.ResetTrigger(TurnRight180Hash);

        Vector3 startPosition = m_patrolPoints[0].transform.position;
        SnapToGround(ref startPosition);
        transform.position = startPosition;
        FaceNextPoint();
        m_state = PatrolState.Walking;
    }

    private bool HasValidRoute()
    {
        if (m_patrolPoints == null || m_patrolPoints.Length != RequiredPointCount)
        {
            Debug.LogError($"{nameof(ShelterPatrolController)} requires exactly {RequiredPointCount} patrol points.", this);
            return false;
        }

        for (int i = 0; i < m_patrolPoints.Length; i++)
        {
            if (m_patrolPoints[i] == null)
            {
                Debug.LogError($"Patrol point at index {i} is not assigned.", this);
                return false;
            }
        }

        return true;
    }

    private void UpdateWalking()
    {
        int targetPointIndex = m_currentPointIndex + m_direction;
        Vector3 targetPosition = m_patrolPoints[targetPointIndex].transform.position;
        Vector2 currentPositionXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 targetPositionXZ = new Vector2(targetPosition.x, targetPosition.z);
        Vector2 nextPositionXZ = Vector2.MoveTowards(
            currentPositionXZ,
            targetPositionXZ,
            m_moveSpeed * Time.deltaTime);

        Vector3 nextPosition = new Vector3(nextPositionXZ.x, transform.position.y, nextPositionXZ.y);
        SnapToGround(ref nextPosition);
        transform.position = nextPosition;

        if ((nextPositionXZ - targetPositionXZ).sqrMagnitude > m_arrivalDistance * m_arrivalDistance)
        {
            return;
        }

        nextPosition.x = targetPosition.x;
        nextPosition.z = targetPosition.z;
        SnapToGround(ref nextPosition);
        transform.position = nextPosition;
        m_currentPointIndex = targetPointIndex;
        HandleArrival();
    }

    private void SnapToGround(ref Vector3 position)
    {
        Vector3 rayOrigin = position + Vector3.up * m_groundProbeHeight;
        float rayDistance = m_groundProbeHeight + m_groundProbeDistance;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                rayDistance,
                m_groundLayers,
                QueryTriggerInteraction.Ignore))
        {
            position.y = hit.point.y + m_groundOffset;
        }
    }

    private void HandleArrival()
    {
        if (!IsEndpoint(m_currentPointIndex))
        {
            return;
        }

        m_direction = m_currentPointIndex == 0 ? 1 : -1;
        PatrolTurnDirection turnDirection = m_patrolPoints[m_currentPointIndex].TurnDirection;

        if (turnDirection == PatrolTurnDirection.None)
        {
            FaceNextPoint();
            return;
        }

        StartTurning(turnDirection);
    }

    private void StartTurning(PatrolTurnDirection turnDirection)
    {
        m_state = PatrolState.Turning;
        m_enteredTurnState = false;
        m_animator.ResetTrigger(TurnLeft180Hash);
        m_animator.ResetTrigger(TurnRight180Hash);

        if (turnDirection == PatrolTurnDirection.Left180)
        {
            m_activeTurnStateHash = TurnLeft180StateHash;
            m_animator.SetTrigger(TurnLeft180Hash);
        }
        else
        {
            m_activeTurnStateHash = TurnRight180StateHash;
            m_animator.SetTrigger(TurnRight180Hash);
        }
    }

    private void UpdateTurning()
    {
        AnimatorStateInfo stateInfo = m_animator.GetCurrentAnimatorStateInfo(0);

        if (stateInfo.shortNameHash == m_activeTurnStateHash)
        {
            m_enteredTurnState = true;
            return;
        }

        if (!m_enteredTurnState || m_animator.IsInTransition(0))
        {
            return;
        }

        FaceNextPoint();
        m_state = PatrolState.Walking;
    }

    private void FaceNextPoint()
    {
        int targetPointIndex = m_currentPointIndex + m_direction;
        Vector3 direction = m_patrolPoints[targetPointIndex].transform.position - transform.position;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);

        if (direction.sqrMagnitude > Mathf.Epsilon)
        {
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    private bool IsEndpoint(int pointIndex)
    {
        return pointIndex == 0 || pointIndex == m_patrolPoints.Length - 1;
    }
}
