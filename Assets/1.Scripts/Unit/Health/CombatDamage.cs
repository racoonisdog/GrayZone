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
    /// 한 번의 피격 판정 결과입니다. 조준선 히트/헤드샷/킬 피드백에 사용합니다.
    /// </summary>
    public readonly struct HitFeedback
    {
        /// <summary>피해가 실제로 적용되었는지 여부입니다.</summary>
        public readonly bool Applied;

        /// <summary>헤드샷(머리 <see cref="Hitbox"/> 피격) 여부입니다.</summary>
        public readonly bool Headshot;

        /// <summary>이 피격으로 대상이 사망 상태로 진입했는지 여부입니다.</summary>
        public readonly bool Killed;

        public HitFeedback(bool applied, bool headshot, bool killed)
        {
            Applied = applied;
            Headshot = headshot;
            Killed = killed;
        }

        /// <summary>피해가 적용되지 않은 빈 결과입니다.</summary>
        public static HitFeedback None => new HitFeedback(false, false, false);
    }

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
    /// 이미 찾은 <see cref="IDamageable"/>에, 적대 진영이고 생존 중일 때만 피해를 적용합니다.
    /// </summary>
    /// <remarks>
    /// 콜라이더 기반 경로와 콜라이더 없이 대상을 직접 가진 경로(예: 비활성 콜라이더 타격)가
    /// 동일한 적대 판정과 피해 적용을 공유하도록 하는 단일 진입점입니다.
    /// </remarks>
    /// <param name="target">피해 대상입니다.</param>
    /// <param name="attacker">공격 측 진영입니다.</param>
    /// <param name="damage">적용할 피해량입니다.</param>
    /// <returns>피해가 실제로 적용되면 true입니다.</returns>
    public static bool TryApplyDamage(IDamageable target, Faction attacker, int damage)
    {
        if (target == null || target.IsDead || damage <= 0)
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
        return TryApplyDamage(target, attacker, damage);
    }

    /// <summary>
    /// 콜라이더에서 <see cref="IDamageable"/>을 찾아, 적대 진영이고 생존 중일 때만 피해를 적용합니다.
    /// </summary>
    public static bool TryApplyDamage(Collider collider, Faction attacker, int damage)
    {
        return TryApplyDamage(collider, attacker, damage, out _);
    }

    /// <summary>
    /// 히트스캔 피격을 해석해 피해를 적용하고, 헤드샷·킬 여부를 담은 피드백을 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 콜라이더에 <see cref="Hitbox"/>가 있으면 헤드샷 여부와 피해 배수를 반영합니다. 조준선 히트마커/킬 표시가
    /// 이 결과를 소비합니다. 근접 등 배수·부위 판정이 필요 없는 경로는 기존 <see cref="TryApplyDamage(Collider, Faction, int)"/>를 씁니다.
    /// </remarks>
    /// <param name="collider">피격 판정 대상 콜라이더입니다.</param>
    /// <param name="attacker">공격 측 진영입니다.</param>
    /// <param name="baseDamage">부위 배수 적용 전 기본 피해량입니다.</param>
    /// <returns>피해 적용 여부와 헤드샷·킬 여부를 담은 <see cref="HitFeedback"/>입니다.</returns>
    public static HitFeedback ResolveHit(Collider collider, Faction attacker, int baseDamage)
    {
        if (collider == null || baseDamage <= 0)
        {
            return HitFeedback.None;
        }

        IDamageable target = collider.GetComponentInParent<IDamageable>();
        if (target == null || target.IsDead || !AreHostile(attacker, target.Faction))
        {
            return HitFeedback.None;
        }

        bool hasHitbox = collider.TryGetComponent(out Hitbox hitbox);
        bool headshot = hasHitbox && hitbox.IsHeadshot;
        float multiplier = hasHitbox ? hitbox.DamageMultiplier : 1.0f;
        int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * multiplier));

        if (!target.TakeDamage(damage))
        {
            return HitFeedback.None;
        }

        return new HitFeedback(true, headshot, target.IsDead);
    }
}
