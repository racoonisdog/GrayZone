/// <summary>
/// 부상 상태 임계값(경상·중상·치명상 범위)을 완화하는 기능이 구현하는 계약입니다.
/// </summary>
/// <remarks>
/// NPC 부상상태 평가 함수는 구체적인 시설 매니저 대신 이 계약을 통해 완화값을 받습니다.
/// 값 계약: 경계별 완화 델타 묶음(<see cref="InjuryThresholdRelief"/>). 없으면 <see cref="InjuryThresholdRelief.None"/>.
/// </remarks>
public interface IInjuryThresholdModifier
{
    /// <summary>현재 상태에서 제공하는 경계별 부상 임계 완화값입니다(없으면 None).</summary>
    InjuryThresholdRelief GetInjuryThresholdRelief();
}
