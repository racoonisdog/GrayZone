/// <summary>
/// 필드 씬의 생명주기와 게임플레이 입력 모드 전환을 조율하는 씬 루트 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 하위 매니저 연결, 입력 모드 전환, 게임오버 화면 표시는 모두 <see cref="CombatSceneManager"/>에 있습니다.
/// 필드 씬만의 흐름이 생기면 여기에 둡니다. 방어전 씬은 <see cref="DefenseManager"/>가 같은 역할을 맡습니다.
/// </remarks>
public class FieldManager : CombatSceneManager
{
    /// <summary>현재 필드 씬의 인스턴스입니다. 필드 씬이 아니면 <c>null</c>입니다.</summary>
    /// <remarks>방어전 씬에서도 쓸 코드는 <see cref="CombatSceneManager.Instance"/>를 씁니다.</remarks>
    public static new FieldManager Instance => CombatSceneManager.Instance as FieldManager;
}
