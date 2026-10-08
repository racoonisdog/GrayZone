using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 감염체 이동 애니메이션의 한 주기 안에서 두 번의 접지 시점을 찾아 FMOD 발소리를 재생합니다.
/// </summary>
/// <remarks>
/// Enemy 이동 클립은 NavMesh 속도에 따라 Blend Tree 안에서 바뀌므로 고정 타이머를 쓰지 않습니다.
/// 현재 <c>Move</c> 상태의 정규화 시간을 따라가야 감속·가속 중에도 소리가 애니메이션 주기에 붙습니다.
/// 표면과 신발 파라미터는 플레이어 발소리 이벤트와 같은 규약을 재사용합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyController))]
public sealed class EnemyFootstepEmitter : MonoBehaviour
{
    private const string WalkEventPath = "event:/Character/Player/Footstep";
    private const string RunEventPath = "event:/Character/Player/Footstep_Run";
    private const string FootwearParameter = "Footwear";
    private const string SurfaceParameter = "Surface";
    private const float MinimumMoveSpeed = 0.1f;
    private const float MaximumTrackedFrameAdvance = 0.75f;

    private static readonly int MoveStateHash = Animator.StringToHash("Move");
    private static bool s_loggedMissingEvent;

    [Header("Timing")]
    [Tooltip("이동 애니메이션 한 주기에서 첫 번째 발이 닿는 정규화 시점입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_firstFootPhase = 0.08f;

    [Tooltip("이동 애니메이션 한 주기에서 두 번째 발이 닿는 정규화 시점입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_secondFootPhase = 0.58f;

    [Tooltip("Always Run이 아닌 적이 이 속도 이상으로 움직이면 Run 발소리를 사용합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_runSpeedThreshold = 2.2f;

    [Header("Ground")]
    [Tooltip("적 루트 위에서 아래로 표면을 찾기 시작하는 높이입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_groundProbeHeight = 0.6f;

    [Tooltip("표면을 찾기 위해 아래로 검사할 거리입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_groundProbeDistance = 2.0f;

    [Header("Mix")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_volume = 1.0f;

    private readonly RaycastHit[] m_groundHits = new RaycastHit[8];
    private EnemyController m_controller;
    private NavMeshAgent m_agent;
    private Animator m_animator;
    private bool m_trackingMoveCycle;
    private float m_previousNormalizedTime;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        ResetCycleTracking();
    }

    private void OnDisable()
    {
        ResetCycleTracking();
    }

    private void LateUpdate()
    {
        CacheReferences();

        if (m_animator == null
            || !m_animator.enabled
            || m_agent == null
            || !m_agent.enabled)
        {
            ResetCycleTracking();
            return;
        }

        float moveSpeed = m_agent.velocity.magnitude;
        if (moveSpeed < MinimumMoveSpeed || !TryGetMoveState(out AnimatorStateInfo moveState))
        {
            ResetCycleTracking();
            return;
        }

        float normalizedTime = moveState.normalizedTime;
        if (!m_trackingMoveCycle
            || normalizedTime < m_previousNormalizedTime
            || normalizedTime - m_previousNormalizedTime > MaximumTrackedFrameAdvance)
        {
            m_trackingMoveCycle = true;
            m_previousNormalizedTime = normalizedTime;
            return;
        }

        bool crossedContact = CrossedPhase(
                m_previousNormalizedTime,
                normalizedTime,
                m_firstFootPhase)
            || CrossedPhase(
                m_previousNormalizedTime,
                normalizedTime,
                m_secondFootPhase);

        m_previousNormalizedTime = normalizedTime;

        if (crossedContact)
        {
            PlayFootstep(moveSpeed);
        }
    }

    /// <summary>현재 또는 전환 대상이 이동 상태인지 확인합니다.</summary>
    private bool TryGetMoveState(out AnimatorStateInfo moveState)
    {
        moveState = m_animator.GetCurrentAnimatorStateInfo(0);
        if (moveState.shortNameHash == MoveStateHash)
        {
            return true;
        }

        if (!m_animator.IsInTransition(0))
        {
            return false;
        }

        moveState = m_animator.GetNextAnimatorStateInfo(0);
        return moveState.shortNameHash == MoveStateHash;
    }

    /// <summary>이전 프레임과 현재 프레임 사이에 지정 접지 시점을 지났는지 확인합니다.</summary>
    private static bool CrossedPhase(float previous, float current, float phase)
    {
        int previousContactIndex = Mathf.FloorToInt(previous - phase);
        int currentContactIndex = Mathf.FloorToInt(current - phase);
        return currentContactIndex > previousContactIndex;
    }

    private void PlayFootstep(float moveSpeed)
    {
        if (!FMODUnity.RuntimeManager.IsInitialized)
        {
            return;
        }

        SurfaceMaterialType surface = ResolveSurface();
        if (surface == SurfaceMaterialType.Unknown)
        {
            return;
        }

        bool useRun = (m_controller.AlwaysRun && !m_controller.IsForcedToWalk)
            || moveSpeed >= m_runSpeedThreshold;
        string eventPath = useRun ? RunEventPath : WalkEventPath;

        try
        {
            FMOD.Studio.EventInstance instance = FMODUnity.RuntimeManager.CreateInstance(eventPath);
            if (!instance.isValid())
            {
                return;
            }

            FMODUnity.RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
            instance.setParameterByNameWithLabel(FootwearParameter, FootwearType.Barefoot.ToString());
            instance.setParameterByNameWithLabel(SurfaceParameter, surface.ToString());
            instance.setVolume(m_volume);
            instance.start();
            instance.release();
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            if (s_loggedMissingEvent)
            {
                return;
            }

            Debug.LogWarning(
                $"[EnemyFootstepEmitter] FMOD 발소리 이벤트를 찾지 못했습니다: {exception.Message}",
                this);
            s_loggedMissingEvent = true;
        }
    }

    /// <summary>자기 콜라이더를 제외하고 가장 가까운 발밑 SurfaceMaterialTag를 찾습니다.</summary>
    private SurfaceMaterialType ResolveSurface()
    {
        Vector3 origin = transform.position + Vector3.up * m_groundProbeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            m_groundHits,
            m_groundProbeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        Collider nearestCollider = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider candidate = m_groundHits[i].collider;
            if (candidate == null || candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            if (m_groundHits[i].distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = m_groundHits[i].distance;
            nearestCollider = candidate;
        }

        return SurfaceMaterialTag.Resolve(nearestCollider);
    }

    private void CacheReferences()
    {
        if (m_controller == null)
        {
            m_controller = GetComponent<EnemyController>();
        }

        if (m_controller != null)
        {
            m_agent = m_controller.Agent;
            m_animator = m_controller.Animator;
        }

        if (m_animator == null)
        {
            m_animator = GetComponentInChildren<Animator>(true);
        }
    }

    private void ResetCycleTracking()
    {
        m_trackingMoveCycle = false;
        m_previousNormalizedTime = 0.0f;
    }
}
