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
}
