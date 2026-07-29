using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 처치 상태입니다. 이동과 피격 판정을 즉시 걷어내고 사망 연출 후 오브젝트를 제거합니다.
/// </summary>
/// <remarks>
/// 충돌 제거를 연출보다 먼저, 즉시 하는 것이 중요합니다.
/// 사체가 길을 막으면 다른 변이체의 길찾기가 막히고, 피격 콜라이더가 남으면 시체가 총알을 먹습니다(§5.10.4).
/// 사망 애니메이션이나 래그돌은 이동을 막지 않는 시각 요소로만 남깁니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.10.4(사망 처리).
/// </remarks>
public class DeadState : EnemyStateBase
{
    /// <summary>오브젝트를 제거할 시각입니다.</summary>
    private float m_destroyTime;

    /// <summary>처치 상태를 생성합니다.</summary>
    public DeadState(EnemyController controller) : base(controller) { }

    public override void Enter()
    {
        m_destroyTime = Time.time + Controller.DestroyDelay;

        DisableNavigation();
        DisableColliders();

        // 교전 중이었다면 알고 있던 대상 정보를 정리합니다.
        Controller.Sensor?.ClearAllInfo();
        Controller.Sensor?.SetEngaged(false);

        Controller.PlayDeathAnimation();
    }

    public override void Tick()
    {
        if (Time.time >= m_destroyTime)
        {
            Object.Destroy(Controller.gameObject);
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
    /// 래그돌을 쓰게 되면 여기서 래그돌용 콜라이더는 남기도록 나눠야 합니다.
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
