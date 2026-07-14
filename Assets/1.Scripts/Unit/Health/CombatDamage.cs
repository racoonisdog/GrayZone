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
    /// Finds an <see cref="IDamageable"/> from a collider parent and applies damage when valid.
    /// </summary>
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
    /// Finds an <see cref="IDamageable"/> from a collider parent and applies damage when valid.
    /// </summary>
    public static bool TryApplyDamage(Collider collider, Faction attacker, int damage)
    {
        return TryApplyDamage(collider, attacker, damage, out _);
    }

    /// <summary>
    /// Resolves hitscan damage against a collider and returns hit feedback.
    /// </summary>
    /// <remarks>
    /// A <see cref="Hitbox"/> only marks whether the hit is a headshot. The weapon supplies
    /// the headshot damage multiplier for this calculation.
    /// </remarks>
    public static HitFeedback ResolveHit(
        Collider collider,
        Faction attacker,
        int baseDamage,
        float headshotDamageMultiplier = 1.0f)
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

        bool headshot = collider.TryGetComponent(out Hitbox hitbox) && hitbox.IsHeadshot;
        float multiplier = headshot ? Mathf.Max(0.0f, headshotDamageMultiplier) : 1.0f;
        int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * multiplier));

        if (!target.TakeDamage(damage))
        {
            return HitFeedback.None;
        }

        return new HitFeedback(true, headshot, target.IsDead);
    }
}
