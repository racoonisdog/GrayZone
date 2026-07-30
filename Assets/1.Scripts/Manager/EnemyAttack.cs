using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 변이체의 근접 공격 시작 조건과 공간 판정을 처리하는 Module입니다.
/// </summary>
/// <remarks>
/// 이 모듈은 "때릴 수 있는가"와 "이 후보를 때려도 되는가"만 답합니다.
/// 언제 때릴지는 <see cref="AttackState"/>가, 무엇이 닿았는지는 손에 달린 판정 콜라이더가 정합니다.
/// 핵심 규칙은 현재 대상의 식별값이 피격 대상을 고정하지 않는다는 것입니다.
/// 대상은 공격 방향을 정하는 데만 쓰이고, 실제로 맞는 것은 판정 중에 콜라이더에 닿은 캐릭터입니다.
/// 그래서 대상이 피하면 빗나가고, 옆에 있던 다른 캐릭터가 대신 맞을 수 있습니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.9.1(공격 구간), §5.9.2(판정 범위와 피격 대상).
/// </remarks>
public class EnemyAttack : MonoBehaviour
{
    [Header("Start Condition")]
    [Tooltip("공격을 시작할 수 있는 대상과의 최대 거리입니다. 실제 판정 범위와는 별개입니다.")]
    [SerializeField] private float m_attackRange = 1.8f;

    [Tooltip("공격을 시작할 수 있는 정면 기준 허용 방향각입니다.")]
    [SerializeField] private float m_attackStartAngle = 70f;

    [Header("Impact")]
    [Tooltip("공격 판정이 적중했을 때 적용할 피해량입니다.")]
    [SerializeField] private int m_attackDamage = 1;

    [Tooltip("한 번의 공격으로 피해를 줄 수 있는 최대 캐릭터 수입니다.")]
    [SerializeField] private int m_attackMaxTargets = 1;

    [Tooltip("켜면 한 번 적중한 순간 그 공격을 끝냅니다. 양손 중 어느 쪽이 먼저 닿아도 피해는 정확히 한 번입니다. " +
             "끄면 판정 콜라이더마다 따로 판정해, 같은 캐릭터가 양손에 스치면 두 번 맞습니다.")]
    [SerializeField] private bool m_singleHitPerSwing = true;

    [Tooltip("공격 판정을 가로막는 고정 환경 장애물 레이어입니다. 다른 변이체는 포함하지 않습니다.")]
    [SerializeField] private LayerMask m_obstacleLayer;

    /// <summary>공격 주체(이 변이체)의 진영입니다.</summary>
    private Faction m_ownerFaction = Faction.Enemy;

    /// <summary>
    /// 이번 공격에서 이미 성립한 적중 기록입니다. 어느 판정 콜라이더가 누구를 때렸는지를 함께 기억합니다.
    /// </summary>
    /// <remarks>
    /// 캐릭터만 기억하면 손별 독립 판정을 표현할 수 없어 출처까지 같이 담습니다.
    /// 진입 시점에만 호출하더라도 좌우 콜라이더가 각각 발화하거나 대상이 빠져나갔다 다시 들어오면
    /// 같은 공격에서 여러 번 들어오기 때문에 이 기록이 필요합니다.
    /// </remarks>
    private readonly List<SwingHit> m_swingHits = new List<SwingHit>();

    /// <summary>실제로 사용할 판정 차단 레이어입니다. 지정이 없으면 기본값으로 채웁니다.</summary>
    private int m_resolvedObstacleMask;

    /// <summary>한 번의 공격에서 성립한 적중 하나입니다.</summary>
    private readonly struct SwingHit
    {
        /// <summary>피해를 발생시킨 판정 콜라이더입니다.</summary>
        public readonly Collider Source;

        /// <summary>피해를 입은 캐릭터입니다.</summary>
        public readonly SquadMemberController Member;

        public SwingHit(Collider source, SquadMemberController member)
        {
            Source = source;
            Member = member;
        }
    }

    /// <summary>
    /// 이번 공격에서 더 이상 판정이 필요 없는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 스윙당 1회 고정 모드에서 한 번 맞은 뒤 true가 됩니다.
    /// 판정 콜라이더는 이 값이 true면 다음 공격 전까지 스스로 꺼져야 합니다.
    /// 콜라이더를 이 모듈이 직접 들고 있지 않으므로, 끄는 행위는 콜라이더 쪽 책임으로 둡니다.
    /// </remarks>
    public bool IsSwingConsumed => m_singleHitPerSwing && m_swingHits.Count > 0;

    /// <summary>
    /// 적 밸런스 데이터에서 공격 시작 조건과 판정 수치를 적용합니다.
    /// </summary>
    /// <param name="balance">적용할 순수 수치 밸런스 데이터입니다.</param>
    public void ApplyBalance(EnemyBalanceSO balance)
    {
        if (balance == null)
        {
            return;
        }

        m_attackRange = balance.AttackRange;
        m_attackStartAngle = balance.AttackStartAngle;
        m_attackDamage = balance.AttackDamage;
        m_attackMaxTargets = balance.AttackMaxTargets;
    }

    private void Awake()
    {
        // 공격 주체의 진영을 소유 HealthSystemBase에서 가져옵니다. 없으면 Enemy로 가정합니다.
        HealthSystemBase ownerHealth = GetComponentInParent<HealthSystemBase>();
        m_ownerFaction = ownerHealth != null ? ownerHealth.Faction : Faction.Enemy;

        m_resolvedObstacleMask = EnemyLayers.ResolveObstacleMask(m_obstacleLayer, this, "공격 판정 차단");
    }

    /// <summary>
    /// 지정한 대상에게 공격을 시작할 수 있는지 확인합니다.
    /// </summary>
    /// <param name="target">현재 대상입니다.</param>
    /// <returns>시작 거리와 허용 방향각을 모두 충족하면 true입니다.</returns>
    /// <remarks>
    /// 시작 조건은 실제 판정 범위와 독립된 값입니다. 공격을 시작한 뒤 대상이 범위를 벗어나도 취소하지 않으며,
    /// 그 경우 빗나간 공격이 됩니다(§5.9.1).
    /// </remarks>
    public bool CanStartAttack(SquadMemberController target)
    {
        if (!IsAliveMember(target))
        {
            return false;
        }

        Vector3 delta = target.transform.position - transform.position;
        delta.y = 0f;

        if (delta.sqrMagnitude > m_attackRange * m_attackRange)
        {
            return false;
        }

        if (delta.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Vector3.Angle(transform.forward, delta.normalized) <= m_attackStartAngle * 0.5f;
    }

    /// <summary>
    /// 새로운 공격 행동을 시작합니다.
    /// </summary>
    /// <remarks>
    /// 적중 기록을 비웁니다. 이 시점부터 판정 콜라이더가 다시 피해를 낼 수 있습니다.
    /// 스윙당 1회 고정 모드에서 꺼 두었던 판정 콜라이더를 다시 켜도 되는 시점이기도 합니다.
    /// </remarks>
    public void BeginSwing()
    {
        m_swingHits.Clear();
    }

    /// <summary>
    /// 판정 콜라이더에 닿은 후보 하나에 대해 피해를 적용할지 판단하고 적용합니다.
    /// </summary>
    /// <param name="source">피해를 낼 판정 콜라이더입니다. 좌우 손을 구분하는 데 씁니다.</param>
    /// <param name="other">판정 콜라이더에 닿은 상대 콜라이더입니다.</param>
    /// <returns>실제로 피해를 입혔으면 true입니다.</returns>
    /// <remarks>
    /// 현재 대상을 인자로 받지 않는 것이 의도입니다. 닿은 것이 곧 피격 후보이며,
    /// 처음 겨눈 대상이 빠져나갔다면 맞지 않습니다(§5.9.2).
    /// 진입 시점에만 부르더라도 같은 공격에서 중복 적중은 생깁니다.
    /// 좌우 콜라이더가 각각 발화하고, 대상이 빠져나갔다 다시 들어와도 또 발화하기 때문입니다.
    /// 그 중복을 어떻게 취급할지는 스윙당 1회 고정 토글이 정합니다.
    /// 다른 변이체는 피해 대상이 아니고 판정을 가리지도 않습니다. 고정 환경 장애물만 판정을 막습니다.
    /// </remarks>
    public bool TryApplyDamageTo(Collider source, Collider other)
    {
        if (source == null || other == null || IsSwingConsumed)
        {
            return false;
        }

        SquadMemberController member = other.GetComponentInParent<SquadMemberController>();
        if (!IsAliveMember(member) || IsAlreadyHit(source, member))
        {
            return false;
        }

        if (CountDistinctMembersHit() >= m_attackMaxTargets && !IsMemberAlreadyCounted(member))
        {
            return false;
        }

        if (IsBlockedByObstacle(source.bounds.center, other))
        {
            return false;
        }

        // 진영 판정과 실제 피해 적용은 공용 경로로 처리합니다.
        // 맞은 쪽이 누구에게 맞았는지 알 수 있도록 이 변이체를 함께 넘깁니다.
        if (!CombatDamage.TryApplyDamage(other, m_ownerFaction, m_attackDamage, gameObject))
        {
            return false;
        }

        m_swingHits.Add(new SwingHit(source, member));
        return true;
    }

    /// <summary>
    /// 이 조합이 이번 공격에서 이미 적중했는지 확인합니다.
    /// </summary>
    /// <remarks>
    /// 스윙당 1회 고정이면 출처와 무관하게 같은 캐릭터를 다시 때리지 않습니다.
    /// 손별 독립이면 같은 손이 같은 캐릭터를 다시 때리는 것만 막고, 반대 손은 따로 판정합니다.
    /// </remarks>
    private bool IsAlreadyHit(Collider source, SquadMemberController member)
    {
        for (int i = 0; i < m_swingHits.Count; i++)
        {
            SwingHit hit = m_swingHits[i];
            if (hit.Member != member)
            {
                continue;
            }

            if (m_singleHitPerSwing || hit.Source == source)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>이번 공격에서 피해를 입은 서로 다른 캐릭터 수입니다.</summary>
    /// <remarks>다중 대상 수는 적중 횟수가 아니라 캐릭터 수로 셉니다. 양손에 맞아도 한 명입니다.</remarks>
    private int CountDistinctMembersHit()
    {
        int count = 0;
        for (int i = 0; i < m_swingHits.Count; i++)
        {
            if (!IsMemberCountedBefore(i))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>같은 캐릭터가 앞선 기록에 이미 있는지 확인합니다.</summary>
    private bool IsMemberCountedBefore(int index)
    {
        for (int i = 0; i < index; i++)
        {
            if (m_swingHits[i].Member == m_swingHits[index].Member)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>이 캐릭터가 이번 공격에서 이미 한 번이라도 맞았는지 확인합니다.</summary>
    /// <remarks>이미 센 캐릭터라면 다중 대상 수를 넘겼더라도 반대 손의 추가 적중은 허용합니다.</remarks>
    private bool IsMemberAlreadyCounted(SquadMemberController member)
    {
        for (int i = 0; i < m_swingHits.Count; i++)
        {
            if (m_swingHits[i].Member == member)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 판정 지점과 대상 사이를 고정 환경 장애물이 가로막는지 확인합니다.
    /// </summary>
    /// <remarks>벽 너머로 겹친 캐릭터가 맞지 않게 하려는 것입니다(§5.9.2).</remarks>
    private bool IsBlockedByObstacle(Vector3 origin, Collider targetCollider)
    {
        Vector3 target = targetCollider.bounds.center;
        Vector3 delta = target - origin;
        float distance = delta.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        return Physics.Raycast(origin, delta / distance, distance, m_resolvedObstacleMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>공격 대상으로 삼을 수 있는 살아 있는 스쿼드 캐릭터인지 확인합니다.</summary>
    private static bool IsAliveMember(SquadMemberController member)
    {
        return member != null && member.IsAlive && !member.IsDown;
    }

    /// <summary>
    /// 공격 시작 거리를 Scene 뷰에서 확인하기 위한 Gizmo입니다.
    /// </summary>
    /// <remarks>
    /// 판정 범위는 손에 달린 판정 콜라이더가 직접 그려 주므로 여기서는 시작 조건만 표시합니다.
    /// 둘은 별개의 값이라 같이 보면 오히려 헷갈립니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, m_attackRange);
    }
}
