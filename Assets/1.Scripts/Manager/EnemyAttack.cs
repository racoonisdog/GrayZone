using UnityEngine;

/// <summary>
/// Enemy의 공격 가능 여부, 공격 쿨다운, 근접 피해 판정을 처리하는 Module입니다.
/// </summary>
public class EnemyAttack : MonoBehaviour
{
    [Header("Attack")]
    /// <summary>공격을 시작할 수 있는 대상과의 최대 거리입니다.</summary>
    [SerializeField] private float m_attackRange = 1.8f;

    /// <summary>한 번 공격한 뒤 다음 공격까지 기다리는 시간입니다.</summary>
    [SerializeField] private float m_attackCooldown = 1.2f;

    /// <summary>공격 애니메이션 중 EnemyController의 상태 전환을 잠그는 시간입니다.</summary>
    [SerializeField] private float m_attackLockDuration = 0.9f;

    /// <summary>공격 판정이 성공했을 때 대상에게 적용할 피해량입니다.</summary>
    [SerializeField] private int m_attackDamage = 1;

    /// <summary>근접 공격 판정 구체의 중심점입니다. 비어 있으면 Enemy 전방 위치를 사용합니다.</summary>
    [SerializeField] private Transform m_attackPoint;

    /// <summary>근접 공격 판정 구체의 반지름입니다.</summary>
    [SerializeField] private float m_attackRadius = 1.0f;

    /// <summary>다음 공격이 가능해지는 시각입니다.</summary>
    private float m_nextAttackTime;

    /// <summary>공격 주체(이 Enemy)의 진영입니다. 소유 HealthSystemBase에서 가져옵니다.</summary>
    private Faction m_ownerFaction = Faction.Enemy;

    /// <summary>공격 애니메이션 중 상태 전환을 잠그는 시간입니다.</summary>
    public float AttackLockDuration => m_attackLockDuration;

    private void Awake()
    {
        // 공격 주체의 진영을 소유 HealthSystemBase에서 가져옵니다. 없으면 Enemy로 가정합니다.
        HealthSystemBase ownerHealth = GetComponentInParent<HealthSystemBase>();
        m_ownerFaction = ownerHealth != null ? ownerHealth.Faction : Faction.Enemy;
    }

    /// <summary>현재 공격 쿨다운이 끝났는지 여부입니다.</summary>
    public bool IsCooldownComplete => Time.time >= m_nextAttackTime;

    /// <summary>
    /// 지정한 대상이 공격 거리 안에 있는지 확인합니다.
    /// </summary>
    public bool IsTargetInRange(Vector3 origin, SquadMemberController target)
    {
        if (!IsTargetValid(target))
        {
            return false;
        }

        return Vector3.Distance(origin, target.transform.position) <= m_attackRange;
    }

    /// <summary>
    /// 현재 쿨다운과 대상 상태 기준으로 공격을 시작할 수 있는지 확인합니다.
    /// </summary>
    public bool CanAttack(Vector3 origin, SquadMemberController target)
    {
        return IsCooldownComplete && IsTargetInRange(origin, target);
    }

    /// <summary>
    /// 공격 쿨다운을 시작합니다.
    /// </summary>
    public void StartCooldown()
    {
        m_nextAttackTime = Time.time + Mathf.Max(0.0f, m_attackCooldown);
    }

    /// <summary>
    /// 현재 공격 대상에게 우선 피해를 적용하고, 실패하면 공격 판정 범위 안의 첫 유효 스쿼드 멤버에게 피해를 적용합니다.
    /// </summary>
    /// <param name="target">EnemyController가 현재 추적 중인 공격 대상입니다.</param>
    /// <returns>피해 적용 대상이 있으면 true입니다.</returns>
    public bool ApplyAttackDamage(SquadMemberController target)
    {
        if (TryApplyDamageToTarget(target))
        {
            return true;
        }

        return ApplyAttackDamage();
    }

    /// <summary>
    /// 공격 범위 안의 첫 유효 스쿼드 멤버에게 피해를 적용합니다.
    /// </summary>
    /// <returns>피해 적용 대상이 있으면 true입니다.</returns>
    public bool ApplyAttackDamage()
    {
        Vector3 point = GetAttackPoint();
        Collider[] hits = Physics.OverlapSphere(point, m_attackRadius);

        for (int i = 0; i < hits.Length; i++)
        {
            // 다운/사망 스쿼드 멤버는 공격 대상에서 제외합니다(기존 동작 유지).
            SquadMemberController member = hits[i].GetComponentInParent<SquadMemberController>();
            if (!IsTargetValid(member))
            {
                continue;
            }

            // 진영 판정과 피해 적용은 공용 경로로 처리합니다.
            if (CombatDamage.TryApplyDamage(hits[i], m_ownerFaction, m_attackDamage))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 콜라이더 활성 여부와 무관하게 현재 공격 대상의 체력에 직접 피해를 적용합니다.
    /// </summary>
    private bool TryApplyDamageToTarget(SquadMemberController target)
    {
        if (!IsTargetValid(target) || !IsTargetInRange(transform.position, target))
        {
            return false;
        }

        PlayerHealth health = target.GetComponent<PlayerHealth>();
        if (health == null)
        {
            health = target.GetComponentInChildren<PlayerHealth>();
        }

        // 적대 판정과 생존/피해 적용은 공용 경로로 처리합니다(null·사망 대상은 내부에서 걸러집니다).
        return CombatDamage.TryApplyDamage(health, m_ownerFaction, m_attackDamage);
    }

    /// <summary>
    /// 공격 판정에 사용할 중심 위치를 반환합니다.
    /// </summary>
    private Vector3 GetAttackPoint()
    {
        return m_attackPoint != null
            ? m_attackPoint.position
            : transform.position + transform.forward + Vector3.up;
    }

    /// <summary>
    /// 공격 대상으로 사용할 수 있는 살아 있는 스쿼드 멤버인지 확인합니다.
    /// </summary>
    private static bool IsTargetValid(SquadMemberController target)
    {
        return target != null && target.IsAlive && !target.IsDown;
    }

    /// <summary>
    /// 선택된 Enemy의 공격 판정 반경을 Scene 뷰에서 확인하기 위한 Gizmo입니다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GetAttackPoint(), m_attackRadius);
    }
}
