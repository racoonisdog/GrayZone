using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 처치 상태입니다. 이동과 피격 판정을 즉시 걷어내고 사망 연출 후 오브젝트를 제거합니다.
/// </summary>
/// <remarks>
/// 충돌 제거를 연출보다 먼저, 즉시 하는 것이 중요합니다.
/// 사체가 길을 막으면 다른 변이체의 길찾기가 막히고, 피격 콜라이더가 남으면 시체가 총알을 먹습니다(§5.10.4).
/// 사망 애니메이션이나 래그돌은 이동을 막지 않는 시각 요소로만 남깁니다.
///
/// 연출은 두 단계입니다. 사망 애니메이션을 지정한 진행률까지 재생하고, 그 지점에서 래그돌 전환과
/// 시체 삭제 카운트를 함께 시작합니다. 진행률은 <see cref="EnemyCorpseSettings.Entry.RagdollAtNormalizedTime"/>이며
/// 1이면 클립이 끝난 뒤입니다.
///
/// 설정은 필드 공통이 아니라 <see cref="EnemyController.EnemyType"/>별로 고릅니다. 슬롯은 enum 멤버와
/// 1:1로 고정되어 있어 종류를 빼먹은 상태가 생기지 않고, 종류를 지정하지 않은 개체는 Unknown 슬롯을 씁니다.
///
/// 래그돌이 애니메이터를 즉시 꺼 버리므로 <b>전환 시점의 자세에서 물리가 이어받습니다.</b>
/// 1로 두면 래그돌은 쓰러진 뒤 지형에 맞춰 눕고 밀리는 반응만 맡고, 중간값으로 낮추면 쓰러지는 도중에
/// 물리가 이어받아 더 자연스러울 수 있습니다. 다만 0에 가까우면 선 자세에서 툭 떨어져 어색합니다.
///
/// 삭제 카운트 기점을 클립 종료가 아니라 래그돌 전환과 같이 둔 이유는, 그 지점이 연출이 시작되는
/// 지점이어서 "연출을 얼마나 보여 줄지"를 한 값으로 조절할 수 있기 때문입니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.10.4(사망 처리).
/// </remarks>
public class DeadState : EnemyStateBase
{
    /// <summary>
    /// 사망 애니메이션 종료를 기다리는 최대 시간(초)입니다.
    /// </summary>
    /// <remarks>
    /// 애니메이터에 사망 스테이트가 없거나 다른 스테이트에 갇히면 종료 판정이 영원히 오지 않아
    /// 시체가 사라지지 않습니다. 그 경우에도 시체 정리는 진행되도록 상한을 둡니다.
    /// </remarks>
    private const float MaxDeathAnimationWait = 5.0f;

    /// <summary>현재 시체를 시간 경과 후 삭제할지 여부입니다.</summary>
    private bool m_destroyCorpse;

    /// <summary>시체를 유지할 시간(초)입니다. 래그돌 전환 시점부터 셉니다.</summary>
    private float m_corpseLifetime;

    /// <summary>물리 골격이 준비된 경우 래그돌로 전환할지 여부입니다.</summary>
    private bool m_useRagdoll;

    /// <summary>래그돌로 넘길 사망 애니메이션 진행률(0~1)입니다.</summary>
    private float m_ragdollAtNormalizedTime;

    /// <summary>사망 애니메이션이 끝나 시체 단계로 넘어갔는지 여부입니다.</summary>
    private bool m_corpsePhaseStarted;

    /// <summary>오브젝트를 제거할 시각입니다. 시체 단계에 들어간 뒤에만 유효합니다.</summary>
    private float m_destroyTime;

    /// <summary>사망 애니메이션 종료를 더 기다리지 않고 시체 단계로 넘어갈 시각입니다.</summary>
    private float m_deathAnimationDeadline;

    /// <summary>처치 상태를 생성합니다.</summary>
    public DeadState(EnemyController controller) : base(controller) { }

    public override void Enter()
    {
        Controller.PlayDeathFeedback();

        EnemyCorpseSettings settings = FieldManager.Instance != null
            ? FieldManager.Instance.EnemyCorpseSettings
            : null;

        if (settings == null)
        {
            m_destroyCorpse = false;
            m_corpseLifetime = 0.0f;
            m_useRagdoll = false;
            m_ragdollAtNormalizedTime = 1.0f;
            Debug.LogError(
                "[DeadState] FieldManager의 EnemyCorpseSettings를 찾지 못했습니다. 시체 처리 설정을 적용할 수 없습니다.",
                Controller);
        }
        else
        {
            // 이 개체의 종류에 해당하는 항목을 고릅니다. 목록에 없으면 기본 설정이 옵니다.
            EnemyCorpseSettings.Entry entry = settings.Resolve(Controller.EnemyType);

            m_destroyCorpse = entry.DestroyCorpse;
            m_corpseLifetime = entry.CorpseLifetime;
            m_useRagdoll = entry.UseRagdoll;
            m_ragdollAtNormalizedTime = entry.RagdollAtNormalizedTime;
        }

        m_corpsePhaseStarted = false;
        m_destroyTime = 0.0f;
        m_deathAnimationDeadline = Time.time + MaxDeathAnimationWait;

        DisableNavigation();

        // 공격 도중 죽으면 Off 이벤트가 오지 않습니다. 상태 표시까지 함께 내려 둡니다.
        Controller.Attack?.SetHitboxActive(false);

        // 교전 중이었다면 알고 있던 대상 정보를 정리합니다.
        Controller.Sensor?.ClearAllInfo();
        Controller.Sensor?.SetEngaged(false);

        // 래그돌 전환은 나중이지만 충돌 제거는 지금 해야 합니다.
        // 래그돌 골격이 있으면 그쪽 콜라이더는 남겨 두고 게임플레이 콜라이더만 걷어냅니다.
        if (!Controller.TryDisableGameplayColliders())
        {
            DisableColliders();
        }

        Controller.PlayDeathAnimation();
    }

    public override void Tick()
    {
        if (!m_corpsePhaseStarted)
        {
            // 래그돌 전환 시점은 설정된 진행률입니다. 1이면 클립이 끝난 뒤로 이전과 같습니다.
            // 래그돌을 쓰지 않는 개체는 클립 종료를 기준으로 삼습니다 - 넘길 것이 없으므로
            // 중간 시점에 시체 단계를 시작할 이유가 없고, 그러면 사망 자세가 덜 재생된 채 삭제 카운트가 돕니다.
            float threshold = m_useRagdoll ? m_ragdollAtNormalizedTime : 1.0f;

            if (Controller.IsDeathAnimationPast(threshold))
            {
                BeginCorpsePhase();
                return;
            }

            if (Time.time >= m_deathAnimationDeadline)
            {
                Debug.LogWarning(
                    "[DeadState] 사망 애니메이션 종료 판정이 오지 않아 대기 상한으로 시체 단계로 넘어갑니다. " +
                    "애니메이터에 Death 스테이트와 DoDeath 전이가 있는지 확인하십시오.",
                    Controller);
                BeginCorpsePhase();
            }

            return;
        }

        if (m_destroyCorpse && Time.time >= m_destroyTime)
        {
            Object.Destroy(Controller.gameObject);
        }
    }

    /// <summary>
    /// 사망 애니메이션이 끝난 뒤의 시체 단계로 넘어갑니다.
    /// </summary>
    /// <remarks>
    /// 래그돌 전환과 삭제 카운트 시작을 한 지점에 모아 둡니다.
    /// 래그돌을 껐거나 물리 골격이 없는 개체도 삭제 카운트 기점은 같아야 하므로, 카운트를 먼저 걸고
    /// 래그돌은 그 뒤에 시도합니다.
    /// </remarks>
    private void BeginCorpsePhase()
    {
        m_corpsePhaseStarted = true;
        m_destroyTime = Time.time + m_corpseLifetime;

        if (m_useRagdoll)
        {
            Controller.TryActivateRagdoll();
        }
    }

    /// <summary>
    /// 길찾기와 이동을 멈추고 다른 개체의 경로를 막지 않게 합니다.
    /// </summary>
    /// <remarks>
    /// NavMeshAgent는 비활성화만으로도 회피 대상에서 빠집니다.
    /// isStopped를 먼저 부르는 이유는 NavMesh 위에 없을 때 예외가 나지 않게 순서를 지키기 위해서입니다.
    /// </remarks>
    private void DisableNavigation()
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent == null)
        {
            return;
        }

        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        agent.enabled = false;
    }

    /// <summary>
    /// 이 변이체의 콜라이더를 모두 끕니다.
    /// </summary>
    /// <remarks>
    /// 피격 판정과 이동 충돌을 한 번에 걷어냅니다.
    /// 사망 이후 발사된 탄환이 사체에 막히지 않고 뒤의 살아 있는 대상까지 가야 하기 때문입니다.
    /// 래그돌 골격이 없는 개체 전용 경로입니다. 골격이 있으면
    /// <see cref="EnemyController.TryDisableGameplayColliders"/>가 래그돌용 콜라이더를 남기고 처리합니다.
    /// </remarks>
    private void DisableColliders()
    {
        Collider[] colliders = Controller.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }
}
