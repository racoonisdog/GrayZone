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
    /// 경직 같은 잠금이 풀린 뒤 이 상태의 이동을 다시 켭니다.
    /// </summary>
    /// <remarks>
    /// 잠금은 상태를 교체하지 않고 <c>StopMoving</c>으로 발만 멈추므로, 풀린 뒤 이동을 되살릴 곳이 필요합니다.
    /// <see cref="Enter"/>를 다시 부르면 될 것 같지만 그러면 진입 시점에만 정해야 하는 것까지 다시 잡습니다.
    /// 배회 기준점이 그 예로, 경직마다 기준점이 현재 위치로 밀려 배회 범위가 조금씩 이동합니다.
    ///
    /// 기본 구현은 속도만 되살립니다. 목적지는 각 상태의 <see cref="Tick"/>이 다시 정하므로 건드리지 않습니다.
    /// </remarks>
    public virtual void ResumeMovement()
    {
        NavMeshAgent agent = Controller.Agent;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = false;
    }

    /// <summary>
    /// 지정한 지점을 향해 각속도 상한 안에서 몸을 돌립니다.
    /// </summary>
    /// <param name="targetPosition">바라볼 월드 지점입니다. 높이 차이는 무시합니다.</param>
    /// <remarks>
    /// <b>비율 보간이 아니라 각속도 상한을 씁니다.</b> <c>Slerp(현재, 목표, 속도 * deltaTime)</c>은 남은 각도의
    /// 일정 <i>비율</i>씩 돌므로, 각도가 클수록 절대 회전 속도가 함께 커집니다. 180도를 돌아야 할 때
    /// 첫 프레임에만 20도 넘게 꺾여 고개가 튕기듯 돕니다. 경직에서 일어난 직후처럼 몸이 대상과 반대를
    /// 보고 있을 때 이 차이가 그대로 드러납니다.
    ///
    /// <see cref="Quaternion.RotateTowards"/>는 각도와 무관하게 초당 회전량이 일정해서, 멀리 돌아야 할수록
    /// 오래 걸릴 뿐 속도는 같습니다. 큰 각도에서도 자연스럽고 값이 곧 "초당 몇 도"라 튜닝 감각도 직관적입니다.
    ///
    /// 추격과 공격 준비가 이 회전을 공유합니다. 두 곳이 다른 방식을 쓰면 공격에 들어가는 순간 회전 속도가
    /// 바뀌어 자세가 튑니다.
    /// </remarks>
    protected void FaceTowards(Vector3 targetPosition)
    {
        Vector3 delta = targetPosition - Controller.transform.position;
        delta.y = 0.0f;

        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion desired = Quaternion.LookRotation(delta.normalized);
        Controller.transform.rotation = Quaternion.RotateTowards(
            Controller.transform.rotation,
            desired,
            Controller.RotationSpeed * Time.deltaTime);
    }

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
