using UnityEngine;

/// <summary>
/// 유닛이 아니지만 총에 맞으면 반응하는 소품입니다.
/// </summary>
/// <remarks>
/// 사격은 유닛(<see cref="IDamageable"/>)에만 피해를 넣고, 그 밖의 콜라이더는 탄이 멈추는 지형으로 봅니다.
/// 탄이 멈춘 지형 콜라이더의 부모에서 이 인터페이스를 찾아 알립니다. 드럼통처럼 맞으면 터지는 소품이 씁니다.
/// 반응할 콜라이더는 탄을 막는(트리거가 아닌) 콜라이더여야 하고, 플레이어 사격에서 빠지는 Trap 레이어에 두면 안 됩니다.
/// </remarks>
public interface IShotReactive
{
    /// <summary>
    /// 탄에 맞았음을 알립니다.
    /// </summary>
    /// <param name="hitPoint">탄이 맞은 월드 좌표입니다.</param>
    /// <param name="damage">이 탄의 피해량입니다. 거리·관통 감쇠가 적용된 값이며, 쓸지 말지는 받는 쪽이 정합니다.</param>
    /// <param name="attacker">쏜 대상입니다. 모르면 <c>null</c>입니다.</param>
    void OnShotHit(Vector3 hitPoint, int damage, GameObject attacker);
}
