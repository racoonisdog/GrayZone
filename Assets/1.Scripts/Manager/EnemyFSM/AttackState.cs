using UnityEngine;

/// <summary>
/// 교전 하위 상태: 공격 준비 → 방향 고정 → 공간 판정 → 후딜레이 구간을 처리합니다.
/// </summary>
/// <remarks>
/// 구간을 나누는 이유는 플레이어에게 회피할 여지를 주기 위해서입니다.
/// 방향이 고정된 뒤에는 변이체가 대상을 따라 돌지 않으므로, 옆으로 빠지면 공격이 빗나갑니다.
/// 판정은 대상 식별값이 아니라 그 순간의 공간에 대해 이루어집니다. 실제 피격 대상 결정은 <see cref="EnemyAttack"/>가 합니다.
/// 판정 시점은 애니메이션 이벤트가 있으면 그것을, 없으면 자체 타이머를 씁니다.
/// 어느 쪽이 오든 한 번의 공격에서 판정은 한 번만 일어납니다.
/// 후딜레이가 곧 공격 간격이며 별도의 비가시적 쿨다운은 두지 않습니다(§5.9.1).
/// 경직으로 인한 공격 취소는 이 상태가 아니라 <see cref="EnemyController"/>의 경직 처리가 담당합니다.
/// 경직은 상태를 교체하지 않고 위에 얹히므로, 취소는 하위 상태를 추격으로 되돌리는 것으로 이루어지고
/// 뒷정리는 이 상태의 <see cref="Exit"/>가 그대로 맡습니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.9(공격 행동).
/// </remarks>
public class AttackState : EnemyStateBase
{
    /// <summary>이번 공격이 시작된 시각입니다.</summary>
    private float m_startTime;

    /// <summary>이번 공격에서 이미 판정을 수행했는지 여부입니다.</summary>
    private bool m_impactDone;

    /// <summary>판정이 끝난 시각입니다. 후딜레이 계산에 씁니다.</summary>
    private float m_impactTime;

    /// <summary>공격 상태를 생성합니다.</summary>
    public AttackState(EnemyController controller) : base(controller) { }

    /// <summary>
    /// 이번 공격이 하울링 전 공격인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 하울링 전 공격은 B-1 한 번으로 끝내고 B-2까지 연계하지 않으며, 끝나면 하울링으로 갑니다
    /// (콘텐츠 §7.2). 그래서 보통의 공격과 종료 처리가 다릅니다.
    /// </remarks>
    private bool m_isPreHowlAttack;

    /// <summary>지금 진행 중인 공격이 하울링 전 공격인지 여부입니다.</summary>
    /// <remarks>
    /// 경직으로 공격이 끊겼을 때 하울링으로 이어갈지(§5.5.2) 판단하려면 <b>Exit 전에</b> 읽어야 합니다.
    /// </remarks>
    public bool IsPreHowlAttack => m_isPreHowlAttack;

    /// <summary>
    /// 다음 진입을 하울링 전 공격으로 표시합니다.
    /// </summary>
    /// <remarks><see cref="CombatState"/>가 하위 상태를 바꾸기 직전에 부릅니다.</remarks>
    public void BeginAsPreHowlAttack()
    {
        m_isPreHowlAttack = true;
    }

    /// <summary>공격 애니메이션을 시작하고 방향 고정·판정·후딜레이 시점을 잡습니다.</summary>
    /// <remarks>구간을 나누는 이유는 플레이어가 회피할 여지를 주기 위해서입니다.</remarks>
    public override void Enter()
    {
        Controller.PlayAttackFeedback();

        m_startTime = Time.time;
        m_impactDone = false;
        m_impactTime = 0f;

        // 공격 중에는 이동하지 않습니다. 애니메이션의 짧은 전진은 슬라이스 2에서 다룹니다.
        Controller.StopMoving();

        m_comboStep = 1;

        Controller.Attack?.BeginSwing();
        Controller.SetInAttackRangeAnimation(true);
        Controller.PlayAttackAnimation(m_comboStep);
    }

    /// <summary>
    /// 지금 몇 번째 공격인지입니다. 1이 빠른 횡타, 2가 이어지는 어퍼컷입니다.
    /// </summary>
    /// <remarks>
    /// 두 공격은 하나의 취소 불가능한 연타가 아닙니다. 앞 공격의 후딜레이가 정상적으로 끝났을 때만
    /// 다음 공격으로 이어지며, 도중에 끊기면 이어지지 않습니다.
    /// 설계 근거: 공용 `변이체 잡몹 1 콘텐츠` §8.3(패턴 B-2 진입 판단).
    /// </remarks>
    private int m_comboStep;

    /// <summary>이어지는 공격까지 포함한 한 교전에서의 최대 공격 수입니다.</summary>
    private const int MaxComboStep = 2;

    /// <summary>구간을 진행시키며 방향 고정과 공간 판정을 각 시점에 한 번씩 수행합니다.</summary>
    /// <remarks>
    /// 방향이 고정된 뒤에는 대상을 따라 돌지 않으므로 옆으로 빠지면 빗나갑니다.
    /// 판정 시점은 애니메이션 이벤트가 있으면 그것을 쓰고 없으면 자체 타이머를 씁니다.
    /// 어느 쪽이 오든 한 번의 공격에서 판정은 한 번만 일어납니다.
    /// </remarks>
    public override void Tick()
    {
        float elapsed = Time.time - m_startTime;

        if (!m_impactDone)
        {
            // 방향 고정 시점 전까지만 대상을 따라 회전합니다.
            if (elapsed < Controller.AttackDirectionLockTime)
            {
                FaceCurrentTarget();
            }

            // 애니메이션 이벤트가 아직 오지 않았다면 타이머로 판정합니다.
            if (elapsed >= Controller.AttackImpactTime)
            {
                DoImpact();
            }

            return;
        }

        if (Time.time - m_impactTime >= Controller.AttackRecoveryDuration)
        {
            FinishSwing();
        }
    }

    /// <summary>공격 구간 상태와 판정 소모 표시를 초기화합니다.</summary>
    public override void Exit()
    {
        Controller.SetInAttackRangeAnimation(false);

        // 공격 스테이트는 IsAttack이 false가 되어야 이동으로 돌아갑니다.
        Controller.EndAttackAnimation();

        // 판정 콜라이더는 클립의 Off 이벤트가 끄는 것이 정상 경로입니다.
        // 다만 클립이 중간에 끊기면 그 이벤트가 오지 않으므로, 상태를 벗어날 때 반드시 다시 끕니다.
        Controller.Attack?.SetHitboxActive(false);

        // 하울링 전 공격 표시는 이 공격에만 유효합니다. 정상 종료(FinishSwing)가 아니라 경직 등으로 끊겨서
        // 나가면 표시가 남아, 다음 평범한 공격이 하울링 전 공격으로 오인되어 끝나고 하울링으로 빠집니다.
        m_isPreHowlAttack = false;
    }

    /// <summary>
    /// 애니메이션 이벤트가 알려 준 판정 시점을 처리합니다.
    /// </summary>
    /// <remarks>이미 타이머로 판정했다면 무시합니다. 한 번의 공격에서 판정은 한 번뿐입니다.</remarks>
    public void NotifyAnimationImpact()
    {
        if (m_impactDone)
        {
            return;
        }

        DoImpact();
    }

    /// <summary>판정 구간이 끝난 것으로 표시하고 후딜레이로 넘어갑니다.</summary>
    /// <remarks>
    /// 피해를 여기서 넣지 않습니다. 실제 적중은 손에 달린 판정 콜라이더가 켜져 있는 동안 닿은 것으로 결정되며,
    /// 그 콜라이더가 <see cref="EnemyAttack.TryApplyDamageTo"/>를 부릅니다.
    /// 콜라이더를 켜고 끄는 주체는 공격 클립의 애니메이션 이벤트로 정했습니다.
    /// 클립이 <c>OnAttackHitboxOn</c>과 <c>OnAttackHitboxOff</c>를 부르고, 그 둘이
    /// <see cref="EnemyAttack.SetHitboxActive"/>까지 이어집니다.
    /// </remarks>
    private void DoImpact()
    {
        m_impactDone = true;
        m_impactTime = Time.time;
    }

    /// <summary>
    /// 후딜레이까지 끝났을 때 다음 행동을 고릅니다.
    /// </summary>
    /// <remarks>아직 칠 수 있으면 다시 공격하고, 아니면 추격으로 돌아갑니다.</remarks>
    private void FinishSwing()
    {
        // 하울링 전 공격은 B-1 한 번으로 끝내고 B-2까지 연계하지 않습니다(콘텐츠 §7.2).
        // 대상을 다시 칠 수 있는지와 무관하게 하울링으로 넘어갑니다.
        if (m_isPreHowlAttack)
        {
            m_isPreHowlAttack = false;
            Controller.Combat.ContinueToHowlAfterPreAttack();
            return;
        }

        SquadMemberController target = Controller.Sensor != null ? Controller.Sensor.CurrentTarget : null;
        bool canAttackAgain = target != null && Controller.Attack != null && Controller.Attack.CanStartAttack(target);

        if (canAttackAgain && m_comboStep < MaxComboStep)
        {
            // 앞 공격의 후딜레이가 정상적으로 끝났으므로 이어지는 공격으로 넘어갑니다.
            m_comboStep++;
            m_startTime = Time.time;
            m_impactDone = false;
            m_impactTime = 0f;

            Controller.Attack?.BeginSwing();
            Controller.PlayAttackAnimation(m_comboStep);
            return;
        }

        if (canAttackAgain)
        {
            // 이어지는 공격까지 마쳤으면 연계를 풀고 첫 공격부터 다시 시작합니다.
            // SetSubState는 같은 상태면 무시하므로 진입 처리를 직접 호출합니다.
            Enter();
            return;
        }

        Controller.Combat.SetSubState(Controller.Combat.Chase);
    }

    /// <summary>공격 준비 중 현재 대상을 향해 회전합니다.</summary>
    /// <remarks>회전 방식은 추격과 공유합니다(<see cref="EnemyStateBase.FaceTowards"/>).</remarks>
    private void FaceCurrentTarget()
    {
        SquadMemberController target = Controller.Sensor != null ? Controller.Sensor.CurrentTarget : null;
        if (target == null)
        {
            return;
        }

        FaceTowards(target.transform.position);
    }
}
