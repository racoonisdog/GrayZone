using UnityEngine;

/// <summary>
/// 진영 기반 피해 판정과 적용을 한곳에 모은 공용 유틸입니다.
/// </summary>
/// <remarks>
/// 근접(<c>EnemyAttack</c>)과 히트스캔(<c>WeaponController</c>) 양쪽이 동일한
/// "콜라이더 → <see cref="IDamageable"/> → 적대 판정 → 피해" 경로를 공유합니다.
/// </remarks>
public static class CombatDamage
{
    /// <summary>
    /// 두 진영이 서로 적대 관계인지 확인합니다.
    /// </summary>
    /// <remarks>
    /// 현재 규칙: Player와 Enemy만 상호 적대입니다. 동일 진영(프렌들리 파이어)과
    /// <see cref="Faction.None"/>, 그리고 NPC(중립)는 적대가 아닙니다. NPC 교전 규칙은 추후 확정합니다.
    /// </remarks>
    public static bool AreHostile(Faction attacker, Faction target)
    {
        if (attacker == Faction.None || target == Faction.None)
        {
            return false;
        }

        return (attacker == Faction.Player && target == Faction.Enemy)
            || (attacker == Faction.Enemy && target == Faction.Player);
    }

    /// <summary>
    /// 콜라이더에서 <see cref="IDamageable"/>을 찾아, 적대 진영이고 생존 중일 때만 피해를 적용합니다.
    /// </summary>
    /// <param name="collider">피격 판정 대상 콜라이더입니다.</param>
    /// <param name="attacker">공격 측 진영입니다.</param>
    /// <param name="damage">적용할 피해량입니다.</param>
    /// <param name="target">찾은 피해 대상입니다. 없으면 null입니다.</param>
    /// <returns>피해가 실제로 적용되면 true입니다.</returns>
    public static bool TryApplyDamage(Collider collider, Faction attacker, int damage, out IDamageable target)
    {
        target = null;

        if (collider == null || damage <= 0)
        {
            return false;
        }

        target = collider.GetComponentInParent<IDamageable>();
        if (target == null || target.IsDead)
        {
            return false;
        }

        if (!AreHostile(attacker, target.Faction))
        {
            return false;
        }

        return target.TakeDamage(damage);
    }

    /// <summary>
    /// 콜라이더에서 <see cref="IDamageable"/>을 찾아, 적대 진영이고 생존 중일 때만 피해를 적용합니다.
    /// </summary>
    public static bool TryApplyDamage(Collider collider, Faction attacker, int damage)
    {
        return TryApplyDamage(collider, attacker, damage, out _);
    }
}
