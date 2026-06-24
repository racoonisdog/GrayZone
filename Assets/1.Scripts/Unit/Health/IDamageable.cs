/// <summary>
/// 진영을 가지고 피해를 받을 수 있는 대상입니다.
/// </summary>
/// <remarks>
/// 공격 측(<c>WeaponController</c>, <c>EnemyAttack</c> 등)이 구체 타입
/// (<c>PlayerHealth</c>/<c>EnemyHealth</c>)을 몰라도 진영 판정과 피해 적용을
/// 한 경로로 처리할 수 있게 하는 공통 계약입니다. <see cref="HealthSystemBase"/>가 구현합니다.
/// </remarks>
public interface IDamageable
{
    /// <summary>이 대상의 진영입니다.</summary>
    Faction Faction { get; }

    /// <summary>이 대상이 사망 상태인지 여부입니다.</summary>
    bool IsDead { get; }

    /// <summary>
    /// 피해를 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다.
    /// </summary>
    bool TakeDamage(int amount);
}
