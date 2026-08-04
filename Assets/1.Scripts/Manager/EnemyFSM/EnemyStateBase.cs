using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 엄브렐라 HFSM에서 모든 Enemy 상태가 공유하는 추상 베이스입니다.
/// </summary>
/// <remarks>
/// 상태는 MonoBehaviour가 아니므로 코루틴 대신 <see cref="Tick"/>에서 타이머를 사용합니다.
/// 튜닝 수치·컴포넌트 접근은 <see cref="Controller"/>를 통해 읽습니다(상태는 수치를 소유하지 않음).
/// 설계 근거: privateDoc ENEMY_SYSTEM_DESIGN.md §5.
/// </remarks>
public abstract class EnemyStateBase
{
    /// <summary>이 상태가 속한 Enemy 컨트롤러입니다. 컴포넌트·헬퍼·전이에 사용합니다.</summary>
    protected readonly EnemyController Controller;

    /// <summary>다음 배회 목적지를 고를 수 있는 시각입니다. 배회 이동 방식을 쓰는 상태가 공유합니다.</summary>
    private float m_nextRepathTime;

    /// <summary>상태를 소유 컨트롤러에 묶습니다.</summary>
    /// <param name="controller">이 상태를 구동하는 <see cref="EnemyController"/>입니다.</param>
    protected EnemyStateBase(EnemyController controller)
    {
        Controller = controller;
    }

    /// <summary>상태에 진입할 때 1회 호출됩니다.</summary>
    public virtual void Enter() { }

    /// <summary>매 프레임 호출되는 상태 갱신 지점입니다.</summary>
    public virtual void Tick() { }

    /// <summary>상태에서 빠져나갈 때 1회 호출됩니다.</summary>
    public virtual void Exit() { }

    /// <summary>
    /// 기준점 주변을 배회하는 이동을 진행합니다.
    /// </summary>
    /// <param name="anchor">배회의 기준이 되는 지점입니다.</param>
    /// <param name="radius">기준점에서 목적지를 고를 반경입니다.</param>
    /// <remarks>
    /// 배회와 소음 수색이 이 이동 방식을 공유합니다. 공용 문서 §5.8.3이 "소음 수색과 교전 수색은
    /// 배회 이동 방식을 공유하지만 서로 다른 상태로 구분한다"고 정하므로, 상태는 나누고 이동만 여기 둡니다.
    /// 상태를 합치면 종료 결과(배회 복귀 / 교전 종료)와 감지 보호 규칙이 서로 달라 구분할 수 없습니다.
    ///
    /// 목적지 갱신 시각을 베이스가 들고 있으므로 상태마다 같은 타이머를 다시 만들지 않습니다.
    /// </remarks>
    protected void TickWanderMovement(Vector3 anchor, float radius)
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent == null || !agent.isOnNavMesh || agent.pathPending)
        {
            return;
        }

        bool arrived = agent.remainingDistance <= agent.stoppingDistance + 0.2f;
        if (arrived || Time.time >= m_nextRepathTime)
        {
            PickWanderDestination(anchor, radius);
        }
    }

    /// <summary>
    /// 기준점 주변 NavMesh 위에서 새 목적지를 골라 이동을 지시합니다.
    /// </summary>
    /// <param name="anchor">배회의 기준이 되는 지점입니다.</param>
    /// <param name="radius">기준점에서 목적지를 고를 반경입니다.</param>
    protected void PickWanderDestination(Vector3 anchor, float radius)
    {
        m_nextRepathTime = Time.time + Controller.WanderInterval;

        Vector3 candidate = anchor + Random.insideUnitSphere * radius;

        // 높이는 자기 현재 높이를 씁니다. 구체에서 뽑은 y를 그대로 쓰면 지형 위나 아래를 가리킵니다.
        candidate.y = Controller.transform.position.y;

        Vector3 destination = NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas)
            ? hit.position
            : Controller.transform.position;

        Controller.MoveTo(destination);
    }
}
