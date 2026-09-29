using UnityEngine;

/// <summary>
/// 특정 적 종류만 가진 특수 능력의 기반 컴포넌트입니다.
/// </summary>
/// <remarks>
/// <see cref="EnemyController"/>와 같은 GameObject에 붙이면 컨트롤러의 Awake에서 수집됩니다.
/// 교전 상태가 다음 행동을 고르는 지점마다 붙은 능력에게 붙은 순서대로 묻고, 처음으로 상태를 돌려준 능력이
/// 흐름을 가져갑니다. 아무 능력도 상태를 돌려주지 않으면 공통 흐름(추격)을 따릅니다.
///
/// 분기점은 실제로 쓰는 능력이 생길 때만 추가합니다. 쓰는 곳 없이 미리 만든 분기점은
/// 이름과 인자가 실제 요구와 어긋나기 쉽습니다.
///
/// 능력이 돌려주는 상태는 <see cref="CombatState"/>의 하위 상태로 실행됩니다. 행동이 끝나면
/// 그 상태가 스스로 <see cref="CombatState.SetSubState"/>로 다음 하위 상태를 지정해야 합니다.
/// </remarks>
[RequireComponent(typeof(EnemyController))]
public abstract class EnemyAbility : MonoBehaviour
{
    /// <summary>이 능력을 가진 적입니다. <see cref="Initialize"/> 전에는 null입니다.</summary>
    protected EnemyController Owner { get; private set; }

    /// <summary>능력을 소유한 적을 연결하고 능력 전용 상태를 준비합니다.</summary>
    /// <param name="owner">이 능력을 수집한 적입니다.</param>
    /// <remarks><see cref="EnemyController"/>가 Awake에서 상태를 만들기 전에 한 번 부릅니다.</remarks>
    public void Initialize(EnemyController owner)
    {
        Owner = owner;
        OnInitialized();
    }

    /// <summary>소유자가 연결된 직후 능력 전용 상태 등을 만듭니다.</summary>
    protected virtual void OnInitialized() { }

    /// <summary>적 밸런스 데이터에서 이 능력이 쓰는 수치를 가져옵니다.</summary>
    /// <param name="balance">적용할 밸런스 데이터입니다. null이 들어오지 않습니다.</param>
    public virtual void ApplyBalance(EnemyBalanceSO balance) { }

    /// <summary>교전에 진입할 때 첫 하위 상태를 가져갈 기회입니다.</summary>
    /// <returns>실행할 하위 상태입니다. 가져가지 않으면 null입니다.</returns>
    public virtual EnemyStateBase OnCombatEnter() => null;

    /// <summary>
    /// 이 능력이 <see cref="CombatState.BeginLeadInAttack"/>로 요청한 선행 공격 1회가 끝났을 때 이어갈 하위 상태를 고릅니다.
    /// </summary>
    /// <returns>이어갈 하위 상태입니다. null이면 추격으로 갑니다.</returns>
    public virtual EnemyStateBase OnLeadInAttackFinished() => null;

    /// <summary>경직으로 현재 하위 행동이 끊기기 직전에 알립니다.</summary>
    /// <param name="interruptedSub">끊기는 하위 상태입니다.</param>
    /// <param name="wasOwnLeadInAttack">끊기는 행동이 이 능력이 요청한 선행 공격인지 여부입니다.</param>
    /// <remarks>
    /// 하위 상태가 바뀌기 전에 불리므로 끊긴 상태의 진행도를 여기서 읽을 수 있습니다.
    /// 경직 뒤에 이어갈 행동이 있으면 기록해 두고 <see cref="OnStaggerEnded"/>에서 돌려줍니다.
    /// </remarks>
    public virtual void OnStaggered(EnemyStateBase interruptedSub, bool wasOwnLeadInAttack) { }

    /// <summary>경직이 끝났을 때 이어갈 하위 상태를 가져갈 기회입니다.</summary>
    /// <returns>이어갈 하위 상태입니다. null이면 경직 전 행동의 이동만 되살립니다.</returns>
    /// <remarks>앞 능력이 이미 상태를 돌려줬어도 모든 능력에 불리므로, 기록은 여기서 비웁니다.</remarks>
    public virtual EnemyStateBase OnStaggerEnded() => null;

    /// <summary>교전이 끝날 때 교전 단위 기록을 초기화합니다.</summary>
    /// <remarks>풀 재사용도 교전 종료를 거치므로 이곳에서 함께 정리됩니다.</remarks>
    public virtual void OnCombatExit() { }
}
