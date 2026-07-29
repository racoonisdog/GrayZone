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
/// 판정 전 경직으로 인한 공격 취소는 경직이 들어오는 슬라이스 4에서 추가합니다.
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

    public override void Enter()
    {
        m_startTime = Time.time;
        m_impactDone = false;
        m_impactTime = 0f;

        // 공격 중에는 이동하지 않습니다. 애니메이션의 짧은 전진은 슬라이스 2에서 다룹니다.
        Controller.StopMoving();

        Controller.Attack?.BeginSwing();
        Controller.SetInAttackRangeAnimation(true);
        Controller.PlayAttackAnimation();
    }

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

    public override void Exit()
    {
        Controller.SetInAttackRangeAnimation(false);
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
    /// TODO(판정 콜라이더): 이 지점에서 AttackPoint_L/_R을 켜고 끄는 배선이 아직 없습니다.
    /// 켜고 끄는 주체를 애니메이션 키프레임으로 할지 애니메이션 이벤트로 할지 미결이며, 정해지면 연결합니다.
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
        SquadMemberController target = Controller.Sensor != null ? Controller.Sensor.CurrentTarget : null;

        if (target != null && Controller.Attack != null && Controller.Attack.CanStartAttack(target))
        {
            // 같은 상태로 다시 들어가기 위해 진입 처리를 직접 호출합니다.
            // SetSubState는 같은 상태면 무시하므로 여기서는 쓸 수 없습니다.
            Enter();
            return;
        }

        Controller.Combat.SetSubState(Controller.Combat.Chase);
    }

    /// <summary>공격 준비 중 현재 대상을 향해 회전합니다.</summary>
    private void FaceCurrentTarget()
    {
        SquadMemberController target = Controller.Sensor != null ? Controller.Sensor.CurrentTarget : null;
        if (target == null)
        {
            return;
        }

        Vector3 delta = target.transform.position - Controller.transform.position;
        delta.y = 0f;

        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion desired = Quaternion.LookRotation(delta.normalized);
        Controller.transform.rotation = Quaternion.Slerp(
            Controller.transform.rotation,
            desired,
            Controller.RotationSpeed * Time.deltaTime);
    }
}
