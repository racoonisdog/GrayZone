using UnityEngine;

/// <summary>
/// 엔티티 단위 통합 밸런스 SO를 받을 수 있는 컴포넌트임을 표시합니다.
/// </summary>
/// <remarks>
/// 값이 정해지는 우선순위는 세 단계입니다.
/// <list type="number">
/// <item><description>프리팹/Inspector에 저작된 값 — 아무 SO도 없을 때 그대로 쓰입니다.</description></item>
/// <item><description>통합 SO — <see cref="SOBinder"/>가 이 인터페이스를 통해 주입합니다.</description></item>
/// <item><description>개별 SO — 컴포넌트가 자기 슬롯에 직접 물고 있는 SO입니다. 가장 강합니다.</description></item>
/// </list>
/// <para>
/// 통합과 개별이 섞이지 않도록 <b>건너뛰기</b>로 처리합니다. <see cref="HasOwnBalance"/>가 <c>true</c>인 컴포넌트에는
/// 통합 SO를 주입하지 않습니다. 덮어쓰기로 하면 Bind가 두 번 돌아 <see cref="IBalancePostProcess"/>도 두 번 돌게 되고,
/// 그러면 후처리가 멱등해야 한다는 제약이 새로 생깁니다.
/// </para>
/// <para>
/// 주입을 <see cref="BindManager"/> 직접 호출이 아니라 이 인터페이스로 돌리는 이유는 <b>실행 순서</b> 때문입니다.
/// 컴포넌트에 따라 대입 전에 프리팹 저작값을 갈무리해야 하는 것이 있어(예: <c>Gun</c>의 방사각 기준값),
/// 대입 순서는 그 컴포넌트 자신이 알고 있어야 합니다.
/// </para>
/// </remarks>
public interface ISharedBalanceReceiver
{
    /// <summary>자기 슬롯에 개별 밸런스 SO를 직접 물고 있는지 여부입니다.</summary>
    /// <remarks><c>true</c>면 통합 SO 주입을 건너뜁니다.</remarks>
    bool HasOwnBalance { get; }

    /// <summary>
    /// 통합 밸런스 SO의 값을 이 컴포넌트의 밸런스 필드에 대입합니다.
    /// </summary>
    /// <param name="balance">엔티티 단위 통합 밸런스 SO입니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다. 원본이 없으면 기본값입니다.</returns>
    /// <remarks>
    /// 개별 SO 슬롯은 건드리지 않습니다. 통합 SO는 컴포넌트 전용 SO와 타입이 다르기 때문에
    /// 그 슬롯에 담을 수 없고, 담아서도 안 됩니다. 슬롯이 비어 있다는 것 자체가 "개별 지정 없음"을 뜻합니다.
    /// </remarks>
    BalanceBindResult BindSharedBalance(ScriptableObject balance);
}
