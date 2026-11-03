using UnityEngine;

/// <summary>
/// 방어전 클리어 횟수로 셸터 NPC의 행동을 나눕니다.
/// 분기 1(클리어 횟수 &lt; 순찰 시작 횟수)에는 제자리 대기, 분기 2에는 <see cref="ShelterPatrolController"/> 순찰을 사용합니다.
/// </summary>
/// <remarks>
/// 애니메이션 분기는 Animator Base Layer의 Bool 파라미터(기본 <c>IsPatrol</c>)로 나눕니다.
/// false면 Idle 상태, true면 WalkFwd(순찰) 상태로 전환되도록 컨트롤러에 전이를 둡니다.
/// 순찰 컴포넌트의 OnEnable(시작 지점으로 이동)보다 먼저 분기를 정하도록 실행 순서를 앞당깁니다.
/// 말풍선 대사 분기는 같은 오브젝트의 <see cref="NpcSpeechBubble"/>이 대사 표로 따로 처리합니다.
/// </remarks>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class ShelterNpcStageBehavior : MonoBehaviour
{
    [Header("Stage")]
    [Tooltip("이 클리어 횟수 이상이면 순찰(분기 2), 미만이면 대기(분기 1). 대사 표의 MinClearCount와 같은 기준")]
    [Min(0)]
    [SerializeField] private int m_patrolMinClearCount = 1;

    [Header("Animator")]
    [Tooltip("순찰 여부를 넘길 Animator Bool 파라미터 이름")]
    [SerializeField] private string m_patrolParameter = "IsPatrol";

    [Header("Idle (분기 1)")]
    [Tooltip("대기 위치/방향. 비우면 씬에 배치한 위치 그대로")]
    [SerializeField] private Transform m_idleAnchor;
    [Tooltip("대기 중 루트 모션 적용 여부. Idle 클립이 미세하게 미끄러지면 해제")]
    [SerializeField] private bool m_idleApplyRootMotion;

    [Header("Patrol (분기 2)")]
    [Tooltip("순찰 담당 컴포넌트. 비우면 같은 오브젝트에서 자동 탐색")]
    [SerializeField] private ShelterPatrolController m_patrolController;

    private Animator m_animator;
    private int m_patrolParameterHash;
    private bool m_hasPatrolParameter;
    private bool m_patrolApplyRootMotion;
    private Vector3 m_placedPosition;
    private Quaternion m_placedRotation;
    private int m_appliedClearCount = -1;
    private bool? m_isPatrolling;

    /// <summary>현재 순찰 분기(분기 2)인지 여부입니다.</summary>
    public bool IsPatrolling => m_isPatrolling == true;

    private void Reset()
    {
        m_patrolController = GetComponent<ShelterPatrolController>();
    }

    private void Awake()
    {
        m_animator = GetComponent<Animator>();
        if (m_patrolController == null)
        {
            m_patrolController = GetComponent<ShelterPatrolController>();
        }

        m_patrolParameterHash = Animator.StringToHash(m_patrolParameter);
        m_hasPatrolParameter = HasBoolParameter(m_patrolParameterHash);
        if (!m_hasPatrolParameter)
        {
            Debug.LogWarning($"[ShelterNpcStageBehavior] {name}: Animator에 Bool 파라미터 '{m_patrolParameter}'가 없습니다.", this);
        }

        m_patrolApplyRootMotion = m_animator.applyRootMotion;

        // 순찰 OnEnable이 시작 지점으로 옮기기 전의 배치 위치를 대기 위치로 기억
        m_placedPosition = transform.position;
        m_placedRotation = transform.rotation;

        // 첫 판정 전까지 순찰이 먼저 돌지 않도록 막아 둠
        if (m_patrolController != null)
        {
            m_patrolController.enabled = false;
        }
    }

    private void OnEnable()
    {
        m_appliedClearCount = -1;
        m_isPatrolling = null;
        Refresh();
    }

    // 클리어 횟수는 귀환 정산·디버그 토글(F12)로만 바뀌므로 값이 달라질 때만 분기를 다시 적용
    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        GameDataManager gameData = GameDataManager.Instance;
        int clearCount = gameData != null ? gameData.DefenseClearCount : 0;
        if (clearCount == m_appliedClearCount)
        {
            return;
        }

        m_appliedClearCount = clearCount;
        bool shouldPatrol = clearCount >= m_patrolMinClearCount;
        if (m_isPatrolling == shouldPatrol)
        {
            return;
        }

        m_isPatrolling = shouldPatrol;
        if (shouldPatrol)
        {
            EnterPatrol();
        }
        else
        {
            EnterIdle();
        }
    }

    private void EnterIdle()
    {
        if (m_patrolController != null)
        {
            m_patrolController.enabled = false;
        }

        SetPatrolParameter(false);
        m_animator.applyRootMotion = m_idleApplyRootMotion;

        if (m_idleAnchor != null)
        {
            transform.SetPositionAndRotation(m_idleAnchor.position, m_idleAnchor.rotation);
        }
        else
        {
            transform.SetPositionAndRotation(m_placedPosition, m_placedRotation);
        }
    }

    private void EnterPatrol()
    {
        SetPatrolParameter(true);
        m_animator.applyRootMotion = m_patrolApplyRootMotion;

        if (m_patrolController == null)
        {
            Debug.LogWarning($"[ShelterNpcStageBehavior] {name}: 순찰 분기인데 ShelterPatrolController가 없습니다.", this);
            return;
        }

        m_patrolController.enabled = true;
    }

    private void SetPatrolParameter(bool value)
    {
        if (m_hasPatrolParameter)
        {
            m_animator.SetBool(m_patrolParameterHash, value);
        }
    }

    private bool HasBoolParameter(int hash)
    {
        if (m_animator.runtimeAnimatorController == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in m_animator.parameters)
        {
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Bool)
            {
                return true;
            }
        }

        return false;
    }
}
