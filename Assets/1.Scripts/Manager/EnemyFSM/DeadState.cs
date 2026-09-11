using UnityEngine;
using UnityEngine.AI;

/// <summary>
    /// 처치 상태입니다. 이동과 피격 판정을 즉시 걷어내고 래그돌로 넘긴 뒤 시체 유지 시간을 처리합니다.
/// </summary>
/// <remarks>
/// 충돌 제거를 연출보다 먼저, 즉시 하는 것이 중요합니다.
/// 사체가 길을 막으면 다른 변이체의 길찾기가 막히고, 피격 콜라이더가 남으면 시체가 총알을 먹습니다(§5.10.4).
///
/// 정상 경로에서는 사망 애니메이션을 쓰지 않습니다. 사망 판정과 동시에 래그돌로 넘기고, 쓰러지는 모양은
/// 그때까지의 자세와 속도, 그리고 죽인 타격의 임펄스가 결정합니다. 사망 클립을 쓰면 클립 수만큼의 정해진
/// 방향으로만 쓰러져 모두 똑같이 죽는 문제가 있었고, 클립을 여러 개 넣어도 그 n가지로 뻔해지는 것은 같습니다.
///
/// 다만 래그돌을 쓸 수 없는 개체는 사망 클립으로 대체합니다. 그러지 않으면 마지막 자세 그대로 굳어
/// 죽은 것으로 보이지 않습니다. 물리 골격이 없는 경우는 프리팹 구성 누락이므로 경고도 함께 남깁니다.
///
/// 그래서 이 상태는 대기 단계가 없습니다. <see cref="Enter"/>에서 전환을 끝내고 <see cref="Tick"/>은
/// 삭제 시점만 셉니다. 사망 처리가 같은 프레임에 끝나므로, 죽인 쪽은 <see cref="Enter"/>가 반환된 뒤
/// 이미 활성화된 래그돌에 임펄스를 줄 수 있습니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.10.4(사망 처리).
/// </remarks>
public class DeadState : EnemyStateBase
{
    /// <summary>현재 시체를 시간 경과 후 삭제할지 여부입니다.</summary>
    private bool m_destroyCorpse;

    /// <summary>오브젝트를 제거할 시각입니다.</summary>
    private float m_destroyTime;

    /// <summary>처치 상태를 생성합니다.</summary>
    public DeadState(EnemyController controller) : base(controller) { }

    /// <summary>이동과 피격 판정을 즉시 걷어내고 래그돌로 넘긴 뒤 시체 제거 시각을 잡습니다.</summary>
    /// <remarks>
    /// 충돌 제거를 연출보다 먼저 하는 것이 중요합니다. 사체가 길을 막으면 다른 변이체의 길찾기가 막히고,
    /// 피격 콜라이더가 남으면 시체가 총알을 먹습니다. 사망 처리가 이 안에서 같은 프레임에 끝납니다.
    /// </remarks>
    public override void Enter()
    {
        Controller.PlayDeathFeedback();

        EnemyManager settings = FieldManager.Instance != null
            ? FieldManager.Instance.EnemyManager
            : null;

        bool useRagdoll;

        if (settings == null)
        {
            m_destroyCorpse = false;
            m_destroyTime = 0.0f;
            useRagdoll = false;
            Debug.LogError(
                "[DeadState] FieldManager의 EnemyCorpseSettings를 찾지 못했습니다. 시체 처리 설정을 적용할 수 없습니다.",
                Controller);
        }
        else
        {
            // 이 개체의 종류에 해당하는 슬롯을 고릅니다.
            EnemyManager.Entry entry = settings.Resolve(Controller.EnemyType);

            m_destroyCorpse = entry.DestroyCorpse;
            m_destroyTime = Time.time + entry.CorpseLifetime;
            useRagdoll = entry.UseRagdoll;

            // 사망 연출 세기는 맞는 쪽 정책입니다. 임펄스는 쏜 쪽이 곧 보내므로 값만 먼저 넣어 둡니다.
            Controller.SetRagdollMinimumHitImpulse(entry.DeathKnockbackImpulse);
        }

        DisableNavigation();

        // 공격 도중 죽으면 Off 이벤트가 오지 않습니다. 상태 표시까지 함께 내려 둡니다.
        Controller.Attack?.SetHitboxActive(false);

        // 교전 중이었다면 알고 있던 대상 정보를 정리합니다.
        Controller.Sensor?.ClearAllInfo();
        Controller.Sensor?.SetEngaged(false);

        // 래그돌로 넘어가면 게임플레이 콜라이더 정리는 그쪽이 함께 처리합니다.
        if (useRagdoll && Controller.TryActivateRagdoll())
        {
            return;
        }

        // 여기부터는 래그돌을 쓰지 못하는 경로입니다. 콜라이더를 직접 걷어내고 사망 클립으로 대체합니다.
        // 클립까지 없으면 마지막 자세 그대로 굳어 죽은 것으로 보이지 않습니다.
        if (!Controller.TryDisableGameplayColliders())
        {
            DisableColliders();
        }

        // 설정에서 끈 것은 의도된 선택이므로 조용히 넘어가고, 골격이 없는 것만 알립니다.
        // 프리팹에 Joint 골격을 넣지 않았다는 뜻이라 구성 누락에 해당합니다.
        if (useRagdoll && !Controller.HasRagdollSkeleton)
        {
            Debug.LogWarning(
                $"[DeadState] '{Controller.name}'에 래그돌 물리 골격이 없어 사망 클립으로 대체합니다. " +
                "프리팹에 Joint 골격과 RagdollController가 있는지 확인하십시오.",
                Controller);
        }

        Controller.PlayDeathAnimation();
    }

    /// <summary>시체 유지 시각까지 남은 시간만 셉니다.</summary>
    /// <remarks>이 상태에는 대기 단계가 없습니다. 전환은 <see cref="Enter"/>에서 이미 끝나 있습니다.</remarks>
    public override void Tick()
    {
        if (m_destroyCorpse && Time.time >= m_destroyTime)
        {
            // 풀링 적은 자신의 스포너가 먼저 비활성 풀로 회수합니다. 처리할 풀이 없는 일반 배치 적만 기존처럼 파괴합니다.
            if (Controller.TryHandleCorpseLifetimeElapsed())
            {
                return;
            }

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
