using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스쿼드 전멸로 필드가 끝났을 때 표시하는 게임오버 화면입니다.
/// </summary>
/// <remarks>
/// <see cref="ResultUIController"/>와 같은 방식입니다: 이 컴포넌트는 아무 UI도 만들지 않고,
/// 씬에 미리 배치된 자식(Title/Message/ConfirmButton)을 이름으로 찾아 값만 채웁니다.
/// 탐색은 계층 전체를 재귀적으로 훑으므로 정리용 상위 그룹 밑으로 옮겨도 계속 동작합니다.
///
/// 귀환 정산(Result UI)과 별개의 화면입니다. 기획 `전투 시스템` §5.9.2가 전멸을 정상 철수와
/// 다른 종료로 규정하고, PR-005 완료 기준이 "귀환 정산 화면 없이" 게임오버를 요구하므로
/// Result UI를 재사용하지 않습니다. 이 경로는 <c>FinalizeField</c>를 부르지 않아 결과값도 만들지 않습니다.
///
/// 이 컴포넌트는 표시만 담당합니다. 전멸 감지는 <see cref="SquadManager.OnSquadEliminated"/>가,
/// 상태 고정은 <see cref="FieldSceneDataManager"/>가, 화면 전환 결정은 <see cref="FieldManager"/>가 맡습니다.
/// </remarks>
[DisallowMultipleComponent]
public class GameOverUIController : MonoBehaviour
{
    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("게임오버 제목을 표시하는 텍스트입니다. 비어 있으면 자식 'Title'을 찾습니다.")]
    [SerializeField] private TextMeshProUGUI m_titleText;

    [Tooltip("게임오버 사유를 표시하는 텍스트입니다. 비어 있으면 자식 'Message'를 찾습니다.")]
    [SerializeField] private TextMeshProUGUI m_messageText;

    [Tooltip("게임오버를 확인하는 버튼입니다. 비어 있으면 자식 'ConfirmButton'을 찾습니다.")]
    [SerializeField] private Button m_confirmButton;

    [Header("Text")]
    [Tooltip("게임오버 화면의 제목입니다.")]
    [SerializeField] private string m_title = "작전 실패";

    [Tooltip("전원 전투 이탈로 게임오버가 된 사유 문구입니다.")]
    [SerializeField] private string m_squadEliminatedMessage = "스쿼드 전원이 전투에서 이탈했습니다.";

    /// <summary>확인 버튼을 눌렀을 때 발생합니다.</summary>
    /// <remarks>
    /// 이 컴포넌트는 씬을 전환하지 않습니다. 게임오버 후 이동 대상이 확정되면 구독자가 처리합니다.
    /// </remarks>
    public event Action OnConfirmed;

    /// <summary>게임오버 화면이 현재 표시 중인지 여부입니다.</summary>
    public bool IsShown => gameObject.activeSelf;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
    }

    /// <summary>
    /// 스쿼드 전멸 사유로 게임오버 화면을 표시합니다.
    /// </summary>
    /// <remarks>
    /// 입력 모드 전환은 이 메서드가 하지 않습니다. 호출자가 게임플레이 입력을 먼저 차단해야 합니다.
    /// </remarks>
    public void ShowSquadEliminated()
    {
        Show(m_squadEliminatedMessage);
    }

    /// <summary>
    /// 지정한 사유 문구로 게임오버 화면을 표시합니다.
    /// </summary>
    /// <param name="message">표시할 사유 문구입니다. 비어 있으면 기존 문구를 유지합니다.</param>
    public void Show(string message)
    {
        AutoFindReferences();

        if (m_titleText != null)
        {
            m_titleText.text = m_title;
        }

        if (m_messageText != null && !string.IsNullOrEmpty(message))
        {
            m_messageText.text = message;
        }

        gameObject.SetActive(true);

        // 필드 EventSystem을 켜 두지 않으면 확인 버튼 클릭이 들어오지 않습니다.
        // 메서드 이름은 Result UI 기준이지만 동작은 오버레이 공용입니다.
        TestSceneUiEventSystemBridge.EnableForResultUI();
    }

    /// <summary>게임오버 화면을 숨깁니다.</summary>
    /// <remarks>
    /// 인게임 HUD는 여기서 끄지 않습니다. 조준선 패널이 이 캔버스보다 아래에 있어
    /// UI 레이어가 가립니다. 자세한 배경은 <see cref="ResultUIController.Hide"/>에 적어 두었습니다.
    /// </remarks>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// ConfirmButton의 Inspector OnClick에서 호출하는 확인 진입점입니다.
    /// </summary>
    /// <remarks>
    /// 게임오버 후 이동 대상(셸터 / 타이틀)이 확정되지 않아 씬 전환을 하지 않습니다.
    /// TODO(PR-005): 이동 대상이 정해지면 구독자 쪽에서 전환을 연결합니다.
    /// </remarks>
    public void Confirm()
    {
        OnConfirmed?.Invoke();
    }

    /// <summary>
    /// 씬에 미리 배치된 자식들을 이름으로 찾아 참조를 채웁니다. 이미 할당된 참조는 덮어쓰지 않습니다.
    /// </summary>
    private void AutoFindReferences()
    {
        if (m_titleText == null)
        {
            Transform title = FindDeep(transform, "Title");
            if (title != null)
            {
                m_titleText = title.GetComponent<TextMeshProUGUI>();
            }
        }

        if (m_messageText == null)
        {
            Transform message = FindDeep(transform, "Message");
            if (message != null)
            {
                m_messageText = message.GetComponent<TextMeshProUGUI>();
            }
        }

        if (m_confirmButton == null)
        {
            Transform button = FindDeep(transform, "ConfirmButton");
            if (button != null)
            {
                m_confirmButton = button.GetComponent<Button>();
            }
        }
    }

    /// <summary>지정한 이름의 자손 Transform을 하위 계층 전체에서 재귀적으로 찾습니다(직계 자식 한정 아님).</summary>
    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name)
            {
                return child;
            }

            Transform found = FindDeep(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
