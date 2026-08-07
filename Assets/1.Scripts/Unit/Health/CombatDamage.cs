using UnityEngine;

/// <summary>
/// 근접과 사격이 공통으로 쓰는, 진영을 아는 피해 적용 헬퍼입니다.
/// </summary>
public static class CombatDamage
{
    /// <summary>
    /// 한 번의 명중 판정이 끝난 뒤 돌려주는 결과입니다. 크로스헤어와 처치 피드백이 이 값을 씁니다.
    /// </summary>
    public readonly struct HitFeedback
    {
        /// <summary>피해가 실제로 들어갔는지 여부입니다.</summary>
        public readonly bool Applied;

        /// <summary>맞은 콜라이더가 약점 히트박스로 표시되어 있었는지 여부입니다.</summary>
        public readonly bool Headshot;

        /// <summary>이 명중으로 대상이 죽었는지 여부입니다.</summary>
        public readonly bool Killed;

        /// <summary>한 번의 명중 결과를 세 가지 판정으로 묶습니다.</summary>
        /// <param name="applied">피해가 실제로 들어갔는지 여부입니다.</param>
        /// <param name="headshot">약점 히트박스에 맞았는지 여부입니다.</param>
        /// <param name="killed">이 명중으로 대상이 죽었는지 여부입니다.</param>
        public HitFeedback(bool applied, bool headshot, bool killed)
        {
            Applied = applied;
            Headshot = headshot;
            Killed = killed;
        }

        /// <summary>피해가 들어가지 않았을 때 쓰는 빈 결과입니다.</summary>
        public static HitFeedback None => new HitFeedback(false, false, false);
    }

    /// <summary>
    /// 현재 전투 규칙에서 두 진영이 서로 적대 관계인지 판정합니다.
    /// </summary>
    /// <param name="attacker">피해를 주는 쪽의 진영입니다.</param>
    /// <param name="target">피해를 받는 쪽의 진영입니다.</param>
    /// <returns>적대 관계이면 <c>true</c>입니다. 한쪽이라도 진영이 없으면 <c>false</c>입니다.</returns>
    /// <remarks>
    /// 이 판정이 피해 적용과 탄 차단의 공통 기준입니다. 두 곳이 다른 기준을 쓰면
    /// "피해는 안 들어가는데 탄은 막히는" 상태가 생깁니다.
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
    /// 이 콜라이더가 탄을 막는지 판정합니다.
    /// </summary>
    /// <param name="collider">사격 경로에서 맞은 콜라이더입니다.</param>
    /// <param name="attacker">사격자의 진영입니다.</param>
    /// <param name="allyPassThrough">아군의 몸을 통과시킬지 여부입니다.</param>
    /// <returns>탄이 여기서 멈춰야 하면 true입니다.</returns>
    /// <remarks>
    /// 판정 기준을 <see cref="AreHostile"/>과 같은 것으로 두어야 "피해는 안 들어가는데 탄은 막히는"
    /// 어긋남이 생기지 않습니다. 그 어긋남이 곧 팀원이 길을 막고 선 몸빵 상황입니다.
    ///
    /// 죽은 유닛은 진영과 무관하게 통과시킵니다. 사망 후 남는 콜라이더는 래그돌 골격이고, 그것이 탄을 막으면
    /// 시체가 쌓인 자리가 통째로 엄폐물이 됩니다. 조준점이 시체에 걸려 뒤의 적을 못 겨누는 문제도 같이 옵니다.
    ///
    /// 유닛이 아닌 것(레벨 지오메트리)은 항상 막습니다.
    /// </remarks>
    public static bool BlocksShot(Collider collider, Faction attacker, bool allyPassThrough)
    {
        if (collider == null)
        {
            return false;
        }

        IDamageable unit = collider.GetComponentInParent<IDamageable>();

        if (unit == null)
        {
            return true;
        }

        if (unit.IsDead)
        {
            return false;
        }

        return !allyPassThrough || AreHostile(attacker, unit.Faction);
    }

    /// <summary>
    /// 콜라이더의 부모에서 <see cref="IStaggerable"/>을 찾아 경직력을 누적시킵니다.
    /// </summary>
    /// <param name="collider">명중한 콜라이더입니다.</param>
    /// <param name="staggerPower">이번 공격이 실어 보내는 경직력입니다. 0 이하면 아무것도 하지 않습니다.</param>
    /// <returns>이번 누적으로 경직이 발동했으면 true입니다.</returns>
    /// <remarks>
    /// <b>피해와 분리된 경로입니다.</b> <see cref="IDamageable.TakeDamage"/>는 HP만 다루고, 경직은 여기를 지납니다.
    /// 두 관심사를 한 메서드에 합치면 경직이 없는 대상(플레이어·다운 아군)의 피해 경로에까지
    /// 경직 개념이 새어 들어갑니다.
    ///
    /// 대상이 <see cref="IStaggerable"/>을 구현하지 않았으면 <b>조용히 무시</b>합니다. 이것이
    /// "경직은 감염체 전용"을 호출부 분기 없이 성립시키는 방식입니다. 무기는 자기가 때린 것이
    /// 경직될 수 있는 존재인지 몰라도 됩니다.
    ///
    /// 진영 검사를 다시 하지 않는 이유는 피해가 이미 들어간 뒤에만 부르기 때문입니다.
    /// 피해가 성립했다면 적대 관계는 이미 확인된 것입니다.
    /// </remarks>
    public static bool TryApplyStagger(Collider collider, float staggerPower)
    {
        if (collider == null || staggerPower <= 0.0f)
        {
            return false;
        }

        IStaggerable staggerable = collider.GetComponentInParent<IStaggerable>();
        return staggerable != null && staggerable.ApplyStagger(staggerPower);
    }

    /// <summary>
    /// 이미 찾아 둔 피격 대상에 진영·생존 검사를 통과할 때만 피해를 적용합니다.
    /// </summary>
    /// <param name="target">피해를 받을 대상입니다.</param>
    /// <param name="attacker">피해를 주는 쪽의 진영입니다.</param>
    /// <param name="damage">적용할 피해량입니다. 0 이하면 아무것도 하지 않습니다.</param>
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
    /// 콜라이더의 부모에서 <see cref="IDamageable"/>을 찾아 유효할 때 피해를 적용합니다.
    /// </summary>
    /// <returns>피해가 실제로 들어갔으면 <c>true</c>입니다.</returns>
    /// <remarks>찾은 대상을 돌려받을 필요가 없을 때 쓰는 간편 오버로드입니다.</remarks>
    public static bool TryApplyDamage(Collider collider, Faction attacker, int damage, GameObject attackerObject = null)
    {
        return TryApplyDamage(collider, attacker, damage, out _, attackerObject);
    }

    /// <summary>
    /// 콜라이더를 상대로 사격 피해를 판정하고 명중 결과를 돌려줍니다.
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
    /// <param name="staggerPower">
    /// 이번 명중이 대상의 경직력 누적에 더할 값(무기의 저지력)입니다. 기본값 0은 경직을 일으키지 않습니다.
    /// 대상이 <see cref="IStaggerable"/>이 아니면 무시됩니다.
    /// </param>
    public static HitFeedback ResolveHit(
        Collider collider,
        Faction attacker,
        int baseDamage,
        float headshotDamageMultiplier = 1.0f,
        bool allowHeadshot = true,
        GameObject attackerObject = null,
        float staggerPower = 0.0f)
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

        // 경직은 피해가 성립한 뒤에만 누적합니다. 막히거나 빗나간 사격이 경직을 쌓으면
        // "안 맞았는데 비틀거리는" 상태가 됩니다.
        TryApplyStagger(collider, staggerPower);

        return new HitFeedback(true, headshot, target.IsDead);
    }
}
