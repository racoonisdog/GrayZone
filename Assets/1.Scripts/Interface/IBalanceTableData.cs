/// <summary>
/// 모든 직렬화 필드가 기획 테이블 입출력 대상인 순수 밸런스 ScriptableObject를 표시합니다.
/// </summary>
/// <remarks>
/// 구현 타입은 Unity 오브젝트 및 미디어 참조를 소유하지 않고, 테이블에서 편집할 식별자·열거형·수치와
/// 그 값들로만 구성된 중첩 데이터만 직렬화해야 합니다. CSV 도구가 이 경계를 검사합니다.
/// </remarks>
public interface IBalanceTableData
{
}
