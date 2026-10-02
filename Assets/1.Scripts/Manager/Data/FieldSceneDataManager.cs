using UnityEngine;

/// <summary>
/// 필드 씬에서 발생한 런타임 결과값을 수집하고, 귀환 시점에 스냅샷으로 확정하는 씬 전용 데이터 매니저입니다.
/// </summary>
/// <remarks>
/// 처치 집계, 전멸 게임오버, 귀환 정산 같은 공통 동작은 모두 <see cref="CombatSceneDataManager"/>에 있습니다.
/// 필드 씬만의 데이터가 생기면 여기에 둡니다. 방어전 씬은 <see cref="DefenseSceneDataManager"/>가 같은 역할을 맡습니다.
/// 실행 순서 어트리뷰트는 상속에 기대지 않고 파생 클래스마다 직접 붙입니다.
/// </remarks>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class FieldSceneDataManager : CombatSceneDataManager
{
    /// <summary>현재 필드 씬의 인스턴스입니다. 필드 씬이 아니면 <c>null</c>입니다.</summary>
    /// <remarks>방어전 씬에서도 쓸 코드는 <see cref="CombatSceneDataManager.Instance"/>를 씁니다.</remarks>
    public static new FieldSceneDataManager Instance => CombatSceneDataManager.Instance as FieldSceneDataManager;
}
