using UnityEngine;

/// <summary>
/// 잠자기 상호작용으로 셸터 날짜를 진행시키는 테스트용 상호작용 컴포넌트
/// </summary>
public class SleepInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private int daysToAdvance = 1;

    /// <summary>탭 상호작용이므로 홀드 시간이 없음</summary>
    public float HoldDuration => 0f;

    /// <summary>
    /// 이 컴포넌트가 활성 상태일 때만 상호작용을 허용
    /// </summary>
    /// <param name="interactor">상호작용을 시도한 오브젝트</param>
    /// <returns>상호작용 가능하면 <c>true</c></returns>
    public bool CanInteract(GameObject interactor) => isActiveAndEnabled;

    /// <summary>상호작용 프롬프트 텍스트</summary>
    public string GetPrompt() => "Sleep";

    /// <summary>
    /// 날짜 매니저에 지정 일수만큼 날짜 진행을 요청
    /// </summary>
    /// <param name="interactor">상호작용을 실행한 오브젝트</param>
    public void Interact(GameObject interactor)
    {
        // ToDo: 확인 팝업 후 호출로 교체 (현재는 테스트용 즉시 진행)
        if (GameDateManager.Instance == null)
        {
            Debug.LogWarning("[SleepInteractable] GameDateManager.Instance is null.", this);
            return;
        }

        GameDateManager.Instance.AdvanceDay(daysToAdvance);
    }
}
