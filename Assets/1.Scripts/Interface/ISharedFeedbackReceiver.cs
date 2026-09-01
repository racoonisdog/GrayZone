using UnityEngine;

/// <summary>
/// 엔티티 단위 통합 피드백 SO를 받을 수 있는 컴포넌트임을 표시합니다.
/// </summary>
/// <remarks>
/// <see cref="ISharedBalanceReceiver"/>와 우선순위·주입 규칙이 완전히 같습니다.
/// 프리팹/Inspector 값 &lt; 통합 SO &lt; 개별 SO 순이고, 개별 SO를 물고 있으면 통합 주입을 건너뜁니다.
/// <para>
/// 다른 점은 대입되는 값이 수치가 아니라 에셋 참조라는 것뿐입니다. 참조를 SO 통째로 물지 않고
/// <see cref="FeedbackFieldAttribute"/>가 붙은 개별 필드로 받는 이유는, SO를 통째로 들고 다니면
/// 같은 타입의 리소스 여러 개를 구분할 수 없기 때문입니다. 필드 이름으로 매칭하면
/// <c>m_muzzleEffectPrefab</c>과 <c>m_tracerEffectPrefab</c>이 둘 다 <c>GameObject</c>여도 정확히 갈립니다.
/// </para>
/// </remarks>
public interface ISharedFeedbackReceiver
{
    /// <summary>자기 슬롯에 개별 피드백 SO를 직접 물고 있는지 여부입니다.</summary>
    /// <remarks><c>true</c>면 통합 SO 주입을 건너뜁니다.</remarks>
    bool HasOwnFeedback { get; }

    /// <summary>
    /// 통합 피드백 SO의 리소스 참조를 이 컴포넌트의 피드백 필드에 대입합니다.
    /// </summary>
    /// <param name="feedback">엔티티 단위 통합 피드백 SO입니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다. 원본이 없으면 기본값입니다.</returns>
    BalanceBindResult BindSharedFeedback(ScriptableObject feedback);
}
