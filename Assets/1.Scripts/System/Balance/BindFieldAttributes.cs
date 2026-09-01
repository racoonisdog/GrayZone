using System;
using UnityEngine.Scripting;

/// <summary>
/// 데이터 SO에서 값을 받을 런타임 필드임을 선언하는 특성들의 공통 기반입니다.
/// </summary>
/// <remarks>
/// 원본과 대상은 <b>필드 이름</b>으로 연결됩니다. <see cref="BindManager"/>가 대상 스크립트를 기준으로 순회하며
/// 이 특성이 붙은 필드만 원본에서 찾아오므로, 원본 SO에만 있는 시트 식별 필드는 자동으로 무시됩니다.
/// <para>
/// <b>이름 규칙</b>: 통합 SO 하나가 한 엔티티의 여러 컴포넌트 값을 함께 담기 때문에, SO 쪽 필드 이름에는
/// 기본적으로 <c>{대상 타입 이름}_</c> 접두사가 붙습니다. 컴포넌트가 늘어도 이름이 겹치지 않게 하기 위해서입니다.
/// 여러 컴포넌트가 같은 값 하나를 공유해야 할 때만 <see cref="Shared"/>로 접두사를 뺍니다.
/// </para>
/// <para>
/// 값의 범위는 이 특성이 아니라 <see cref="ClampAttribute"/>가 선언합니다. 둘을 나눈 이유는
/// 경계가 SO 주입에만 필요한 것이 아니기 때문입니다. SO를 쓰지 않는 필드도 Inspector 입력을 제한해야 하고,
/// 그 경계는 SO를 쓰게 되더라도 그대로 유효합니다. 그래서 경계는 독립 선언으로 두고 여기서는
/// "이 필드는 SO에서 값을 받는다"만 표시합니다. 둘을 함께 붙이면 Inspector 입력과 SO 주입이 같은 범위를 따릅니다.
/// </para>
/// </remarks>
[Preserve]
public abstract class BindFieldAttribute : Attribute
{
    /// <summary>여러 컴포넌트가 원본 SO의 같은 필드 하나를 함께 받는지 여부입니다.</summary>
    /// <remarks>
    /// 기본값 <c>false</c>는 "이 컴포넌트 전용"이며, SO에는 <c>{대상 타입 이름}_{필드 이름}</c>으로 생성됩니다.
    /// <c>true</c>면 접두사 없이 <c>{필드 이름}</c>으로 생성되어, 같은 이름을 선언한 다른 컴포넌트와 같은 값을 받습니다.
    /// <para>
    /// 공유는 시트에 값을 한 번만 적으면 되는 대신 한쪽만 다르게 조정하는 것이 불가능해집니다.
    /// 나중에 갈라야 할 가능성이 조금이라도 있으면 전용으로 두는 편이 되돌리기 쉽습니다.
    /// </para>
    /// </remarks>
    public bool Shared { get; set; }
}

/// <summary>
/// 밸런스 수치를 받을 런타임 필드임을 선언합니다.
/// </summary>
/// <remarks>
/// 데이터 원본 SO에는 이 특성을 붙이지 않습니다. SO는 이 특성이 붙은 스크립트에서 생성하므로
/// 이름이 어긋날 여지가 없습니다.
/// <para>
/// <b>붙일 수 있는 값의 범위(중요)</b>: 이 특성은 <b>상수 또는 첫 초기화 기본값</b>에만 붙입니다.
/// 플레이 중 변하는 값(현재 탄약, 현재 체력, 부품으로 증감한 실효 수치 등)은 SO에 두지 않습니다.
/// 그런 값의 소유자는 GameDataManager와 각 씬 데이터 매니저, 세이브 매니저이며,
/// 씬을 옮길 때 SO에서 다시 읽으면 진행 상황이 덮여 사라집니다.
/// </para>
/// <para>
/// 최대 체력·최대 탄창처럼 "기본값은 상수인데 런타임에 보정될 수 있는" 값은 <b>SO가 첫 초기화만</b> 담당합니다.
/// 바인딩은 대상이 새로 생성될 때마다(Awake) 실행되므로, 그 뒤에 데이터 매니저가 저장된 실효값을 반드시 다시 적용해야 합니다.
/// 예: <c>Gun.m_maxBullet</c>은 이 특성으로 기본값을 받고, 실효값은 스냅샷 복원이 덮어씁니다.
/// </para>
/// </remarks>
[Preserve]
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class BalanceFieldAttribute : BindFieldAttribute
{
}

/// <summary>
/// 사운드·이펙트 등 표현 리소스 참조를 받을 런타임 필드임을 선언합니다.
/// </summary>
/// <remarks>
/// 밸런스와 같은 규칙(필드 이름 매칭, 접두사, <see cref="BindFieldAttribute.Shared"/>)을 그대로 따릅니다.
/// 다른 점은 원본이 <see cref="IFeedbackData"/> 구현 SO라는 것과, 대입되는 값이 수치가 아니라 에셋 참조라는 것뿐입니다.
/// <para>
/// 참조를 개별 필드로 받는 이유는, SO를 통째로 물고 다니면 같은 타입의 리소스 여러 개를 구분할 수 없기 때문입니다.
/// 필드 이름으로 매칭하면 <c>m_muzzleEffectPrefab</c>과 <c>m_tracerEffectPrefab</c>이 둘 다
/// <c>GameObject</c>여도 정확히 갈립니다.
/// </para>
/// <para>
/// 이 특성이 붙은 필드는 밸런스 CSV 대상이 아닙니다. 시트로 나가는 것은 <see cref="IBalanceTableData"/>
/// 구현 SO뿐이며, 표현 리소스는 Unity 에셋 참조라 시트에 적을 수 있는 값이 아닙니다.
/// </para>
/// </remarks>
[Preserve]
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FeedbackFieldAttribute : BindFieldAttribute
{
}
