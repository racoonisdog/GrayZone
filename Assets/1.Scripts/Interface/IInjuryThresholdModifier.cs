/// <summary>
/// 부상 상태 임계값(경상·중상·치명상 범위)을 완화하는 시설이 구현하는 계약입니다.
/// </summary>
/// <remarks>
/// 셸터 측은 이 기여값만 노출하고, NPC 부상상태 평가 함수(NPC 로직 소유)는 그 값을 인자로 받아 씁니다.
/// 값 계약: 경계별 완화 델타 묶음(<see cref="InjuryThresholdRelief"/>). 없으면 <see cref="InjuryThresholdRelief.None"/>.
/// </remarks>
public interface IInjuryThresholdModifier
{
    /// <summary>이 시설이 현재 상태에서 제공하는 경계별 부상 임계 완화값입니다(없으면 None).</summary>
    InjuryThresholdRelief GetThresholdRelief();
}
