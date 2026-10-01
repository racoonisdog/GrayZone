using UnityEngine;

/// <summary>
/// 주변의 폭발이나 화염에 휘말려 함께 터질 수 있는 대상입니다.
/// </summary>
/// <remarks>
/// 수류탄, 지뢰, 클레이모어 같은 폭발과 용숨결 같은 화염이 자기 범위 안에서 이 인터페이스를 찾아 부릅니다.
/// 실제로 터질지와 언제 터질지는 받는 쪽이 정합니다. 연쇄를 끄는 토글도 받는 쪽에 둡니다.
/// </remarks>
public interface IChainDetonatable
{
    /// <summary>
    /// 주변의 폭발·화염에 휘말렸음을 알립니다.
    /// </summary>
    /// <param name="source">휘말리게 한 폭발이나 화염을 일으킨 대상입니다. 모르면 <c>null</c>입니다.</param>
    /// <returns>이번 호출로 터지기 시작했으면 <c>true</c>입니다.</returns>
    bool TryChainDetonate(GameObject source);
}
