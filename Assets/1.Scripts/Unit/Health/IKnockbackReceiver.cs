using UnityEngine;

/// <summary>
/// 피격으로 밀려날 수 있는 대상입니다.
/// </summary>
/// <remarks>
/// 넉백의 기본 대상은 <b>살아 있는 개체</b>입니다. 사망 후 래그돌이 받는 충격은 같은 값을 쓰는 부가 효과이며,
/// 어느 쪽으로 처리할지는 받는 쪽이 자기 상태를 보고 정합니다.
/// 때리는 쪽(총기·근접)은 방향과 세기만 알려 주고 대상이 살아 있는지 죽었는지 몰라도 됩니다.
///
/// <see cref="IDamageable"/>과 분리한 이유는 피해와 넉백이 독립적이기 때문입니다.
/// 피해 0인 밀어내기나 넉백 0인 피해가 모두 성립합니다.
/// </remarks>
public interface IKnockbackReceiver
{
    /// <summary>
    /// 피격 방향으로 이 대상을 밀어냅니다.
    /// </summary>
    /// <param name="direction">공격자에서 피격 지점으로 향하는 방향입니다. 정규화되지 않아도 됩니다.</param>
    /// <param name="hitPoint">피격 지점의 월드 좌표입니다. 부위를 특정하는 데 씁니다.</param>
    /// <param name="impulse">충격량(N·s)입니다. 받는 쪽이 자기 하한과 비교해 조정할 수 있습니다.</param>
    /// <param name="hitBone">맞은 부위의 리지드바디입니다. 알 수 없으면 null입니다.</param>
    /// <returns>넉백을 적용했으면 true입니다.</returns>
    bool ApplyKnockback(Vector3 direction, Vector3 hitPoint, float impulse, Rigidbody hitBone);
}
