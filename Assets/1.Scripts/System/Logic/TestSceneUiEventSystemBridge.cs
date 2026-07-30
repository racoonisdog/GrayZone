using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Title 기반 공용 UI Runtime이 만들어지기 전까지, 테스트 씬의 결과 UI 입력과 셸터 전환을 중계합니다.
/// </summary>
/// <remarks>
/// 필드 GameManager의 자식 EventSystem만 사용합니다. 결과 UI를 표시할 때는 Input System 모듈을 보장하고,
/// 셸터 전환 직전에는 대상 씬 EventSystem과 겹치지 않도록 해당 오브젝트만 비활성화합니다.
/// GameManager와 GameDataManager는 비활성화하지 않습니다.
/// TODO(Title UI Runtime): Title 기반 공용 GameManager/EventSystem이 도입되면 이 임시 브리지를 제거하고,
/// EventSystem 소유권과 입력 모듈 설정을 해당 공용 Runtime으로 통합합니다.
/// TODO(Shelter test scene): TEst의 씬 로컬 UI EventSystem은 셸터 담당 영역입니다.
/// 이 브리지는 전환 직전 필드 EventSystem만 끄며, TEst 내부 중복 구성은 수정하지 않습니다.
/// TODO(PlayerTest): PlayerTest에는 GameManager 자식 EventSystem이 없으므로 이 브리지 적용 대상이 아닙니다.
/// PlayerTest까지 결과 UI 클릭을 시험하려면 별도 UI Runtime 구성이 필요합니다.
/// </remarks>
public static class TestSceneUiEventSystemBridge
{
    private static EventSystem s_fieldEventSystem;

    /// <summary>
    /// 결과 UI가 클릭 입력을 받을 수 있도록 필드 GameManager 자식 EventSystem을 활성화합니다.
    /// </summary>
    public static void EnableForResultUI()
    {
        EventSystem eventSystem = GetOrCacheFieldEventSystem();
        if (eventSystem == null)
        {
            return;
        }

        EnsureInputModule(eventSystem);
        if (!eventSystem.gameObject.activeSelf)
        {
            eventSystem.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 셸터 씬 전환 직전에 필드 EventSystem만 비활성화합니다.
    /// </summary>
    public static void DisableBeforeShelterTransition()
    {
        EventSystem eventSystem = GetOrCacheFieldEventSystem();
        if (eventSystem != null && eventSystem.gameObject.activeSelf)
        {
            eventSystem.gameObject.SetActive(false);
        }
    }

    private static EventSystem GetOrCacheFieldEventSystem()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            return null;
        }

        if (s_fieldEventSystem != null
            && s_fieldEventSystem.transform.IsChildOf(gameManager.transform))
        {
            return s_fieldEventSystem;
        }

        s_fieldEventSystem = gameManager.GetComponentInChildren<EventSystem>(true);
        return s_fieldEventSystem;
    }

    private static void EnsureInputModule(EventSystem eventSystem)
    {
        if (eventSystem.GetComponent<InputSystemUIInputModule>() != null
            || eventSystem.GetComponent<BaseInputModule>() != null)
        {
            return;
        }

        eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
    }
}
