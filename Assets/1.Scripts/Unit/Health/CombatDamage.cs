using UnityEngine;

/// <summary>
/// Shared faction-aware damage helpers for melee and hitscan attacks.
/// </summary>
public static class CombatDamage
{
    /// <summary>
    /// Result data returned by one resolved hit. Crosshair and kill feedback consume this.
    /// </summary>
    public readonly struct HitFeedback
    {
        /// <summary>Whether damage was actually applied.</summary>
        public readonly bool Applied;

        /// <summary>Whether the hit collider was marked as a headshot hitbox.</summary>
        public readonly bool Headshot;

        /// <summary>Whether this hit killed the target.</summary>
        public readonly bool Killed;

        public HitFeedback(bool applied, bool headshot, bool killed)
        {
            Applied = applied;
            Headshot = headshot;
            Killed = killed;
        }

        /// <summary>Empty result used when no damage was applied.</summary>
        public static HitFeedback None => new HitFeedback(false, false, false);
    }

    /// <summary>
    /// Returns whether the two factions are hostile under the current combat rules.
    /// </summary>
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
    /// Applies damage to an already resolved damageable target when faction and life-state checks pass.
    /// </summary>
    /// <param name="attackerObject">피해를 입힌 대상입니다. 피격자가 반격 대상을 알기 위해 그대로 전달합니다.</param>
    public static bool TryApplyDamage(IDamageable target, Faction attacker, int damage, GameObject attackerObject = null)
    {
        if (target == null || target.IsDead || damage <= 0)
        {
            return false;
        }

        if (!AreHostile(attacker, target.Faction))
        {
            return false;
        }

        return target.TakeDamage(damage, attackerObject);
    }

    /// <summary>
    /// 콜라이더의 부모에서 <see cref="IDamageable"/>을 찾아 지정한 피해를 그대로 적용합니다.
    /// </summary>
    /// <remarks>
    /// 약점 판정과 부위 배율을 사용하지 않는 경로입니다. 어디를 맞혔든 지정된 피해량 그대로 들어갑니다.
    /// 변이체의 근접 공격이 이 경로를 쓰며, 약점 개념이 없는 것은 의도된 규칙입니다.
    /// 약점 판정이 필요한 사격은 <see cref="ResolveHit"/>를 쓰십시오. 근접 공격을 그쪽으로 옮기면
    /// 변이체에게도 약점 배율이 생기므로 바꾸지 마십시오.
    /// </remarks>
    public static bool TryApplyDamage(Collider collider, Faction attacker, int damage, out IDamageable target, GameObject attackerObject = null)
    {
        target = null;

        if (collider == null || damage <= 0)
        {
            return false;
        }

        target = collider.GetComponentInParent<IDamageable>();
        return TryApplyDamage(target, attacker, damage, attackerObject);
    }

    /// <summary>
    /// Finds an <see cref="IDamageable"/> from a collider parent and applies damage when valid.
    /// </summary>
    public static bool TryApplyDamage(Collider collider, Faction attacker, int damage, GameObject attackerObject = null)
    {
        return TryApplyDamage(collider, attacker, damage, out _, attackerObject);
    }

    /// <summary>
    /// Resolves hitscan damage against a collider and returns hit feedback.
    /// </summary>
    /// <remarks>
    /// <see cref="Hitbox"/>가 붙은 콜라이더만 피격 대상입니다. 무엇이 맞을 수 있는지를 명시적으로 표시하게 해서,
    /// 이동용 캡슐이나 다른 용도의 콜라이더가 실수로 피해를 받는 것을 막습니다.
    /// <see cref="Hitbox"/>는 약점 여부만 표시하고, 배율은 무기가 넘깁니다.
    /// 근접 공격은 이 경로가 아니라 <see cref="TryApplyDamage(Collider, Faction, int)"/>를 쓰므로 이 규칙의 적용을 받지 않습니다.
    /// </remarks>
    /// <param name="allowHeadshot">
    /// 약점 판정을 사용할지 여부입니다. false면 약점 히트박스를 맞혀도 일반 피해로 처리하고 <see cref="HitFeedback.Headshot"/>도 false가 됩니다.
    /// 배율만 1로 두지 않고 표시까지 끄는 이유는, 피해는 안 늘었는데 조준선에 약점 표시만 뜨는 어긋남을 막기 위해서입니다.
    /// </param>
    public static HitFeedback ResolveHit(
        Collider collider,
        Faction attacker,
        int baseDamage,
        float headshotDamageMultiplier = 1.0f,
        bool allowHeadshot = true,
        GameObject attackerObject = null)
    {
        if (collider == null || baseDamage <= 0)
        {
            return HitFeedback.None;
        }

        // 피격 판정용으로 표시된 콜라이더가 아니면 사격이 통하지 않습니다.
        if (!collider.TryGetComponent(out Hitbox hitbox))
        {
            return HitFeedback.None;
        }

        IDamageable target = collider.GetComponentInParent<IDamageable>();
        if (target == null || target.IsDead || !AreHostile(attacker, target.Faction))
        {
            return HitFeedback.None;
        }

        bool headshot = allowHeadshot && hitbox.IsHeadshot;
        float multiplier = headshot ? Mathf.Max(0.0f, headshotDamageMultiplier) : 1.0f;
        int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * multiplier));

        if (!target.TakeDamage(damage, attackerObject))
        {
            return HitFeedback.None;
        }

        return new HitFeedback(true, headshot, target.IsDead);
    }
}
