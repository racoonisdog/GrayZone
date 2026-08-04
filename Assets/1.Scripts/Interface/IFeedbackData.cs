/// <summary>
/// Unity Inspector에서 사운드, 이펙트, 데칼 등 표현 피드백을 할당하는 ScriptableObject 데이터임을 표시합니다.
/// </summary>
/// <remarks>
/// 이 데이터는 밸런스 CSV 대상이 아닙니다. 수치가 포함되더라도 이펙트 수명처럼 표현 재생을 위한 설정이며,
/// 게임플레이 밸런스는 <see cref="IBalanceTableData"/> 구현 SO가 소유합니다.
/// </remarks>
public interface IFeedbackData
{
}
