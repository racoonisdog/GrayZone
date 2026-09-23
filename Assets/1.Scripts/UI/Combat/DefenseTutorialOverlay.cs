using TMPro;
using UnityEngine;

/// <summary>
/// Defense 시작 시 화면 오른쪽에 조작 안내를 띄우고, 닫기 입력으로 감춥니다.
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: 이 컴포넌트는 UI를 만들지 않고, 씬에 미리 배치된 자식을
/// 이름으로 찾아 값(본문 문자열, 표시 여부)만 채웁니다. 레이아웃은 씬에서 조정합니다.
///
/// 닫기 키는 <c>PlayerInput.inputactions</c>의 <c>TutorialClose</c> 액션이 정합니다(기본 E).
/// 기본값이 상호작용과 같은 키라, 닫을 때 <see cref="PlayerInputController.SuppressInteractUntilRelease"/>로
/// 상호작용을 막습니다. 안 막으면 튜토리얼을 닫은 그 입력이 그대로 구조 홀드로 이어집니다.
/// 액션 바인딩을 다른 키로 바꾸면 이 억제는 무해하게 비어 돌아갑니다.
/// </remarks>
[DisallowMultipleComponent]
public class DefenseTutorialOverlay : MonoBehaviour
{
    private const string PanelName = "Panel";
    private const string BodyName = "Body";
    private const string HintName = "Hint";

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("안내 패널 루트입니다. 이 오브젝트를 켜고 끄는 것으로 표시를 전환합니다. 비어 있으면 자식 'Panel'을 찾습니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("안내 본문 텍스트입니다. 비어 있으면 자식 'Body'를 찾습니다.")]
    [SerializeField] private TMP_Text m_bodyText;

    [Tooltip("닫기 안내 텍스트입니다. 비어 있으면 자식 'Hint'를 찾습니다.")]
    [SerializeField] private TMP_Text m_hintText;

    [Header("Defense")]
    [Tooltip("구독할 라운드 매니저입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseManager m_roundManager;

    [Header("Text")]
    [TextArea(3, 8)]
    [Tooltip("안내 본문입니다. 줄바꿈을 그대로 씁니다.")]
    [SerializeField] private string m_body =
        "감염체가 몰려옵니다.\n거점을 지키세요.\n\n라운드가 끝나면 잠시 휴식이 주어집니다.";

    [Tooltip("닫기 안내 문구입니다. 표시할 키 이름은 여기서 직접 적습니다.")]
    [SerializeField] private string m_hint = "[E] 3초 유지 — 방어전 시작";

    /// <summary>안내가 현재 표시 중인지 여부입니다.</summary>
    public bool IsShown => m_panelRoot != null && m_panelRoot.activeSelf;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        ApplyTexts();
        SetVisible(false);
    }

    private void OnEnable()
    {
        AutoFindReferences();
        Subscribe();
        RefreshVisibility();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>
    /// 방어전이 아직 시작되지 않았으면 안내를 띄우고, 시작된 뒤면 감춥니다.
    /// </summary>
    /// <remarks>
    /// 안내는 "시작하는 방법"을 알려주는 것이라 시작 전에 떠 있어야 합니다. 시작되면 역할이 끝나므로 내립니다.
    ///
    /// 시작 이벤트만 믿지 않고 여기서 현재 상태도 함께 봅니다. UI 루트가 꺼진 채 씬이 시작되면
    /// <c>OnEnable</c>이 늦게 돌아 그 사이의 시작 이벤트를 놓치기 때문입니다.
    /// </remarks>
    private void RefreshVisibility()
    {
        bool started = m_roundManager != null && m_roundManager.IsGameStarted;
        if (started)
        {
            Hide();
            return;
        }

        Show();
    }

    /// <summary>안내를 표시합니다.</summary>
    public void Show()
    {
        AutoFindReferences();
        ApplyTexts();
        SetVisible(true);
    }

    /// <summary>안내를 감춥니다.</summary>
    public void Hide()
    {
        SetVisible(false);
    }

    private void Subscribe()
    {
        if (m_roundManager == null)
        {
            return;
        }

        m_roundManager.OnDefenseStarted -= HandleDefenseStarted;
        m_roundManager.OnDefenseStarted += HandleDefenseStarted;
    }

    private void Unsubscribe()
    {
        if (m_roundManager != null)
        {
            m_roundManager.OnDefenseStarted -= HandleDefenseStarted;
        }
    }

    /// <summary>방어전이 시작되면 안내를 내립니다. 시작 방법을 알려주는 안내라 역할이 끝납니다.</summary>
    private void HandleDefenseStarted()
    {
        Hide();
    }

    private void AutoFindReferences()
    {
        if (m_roundManager == null)
        {
            m_roundManager = FindFirstObjectByType<DefenseManager>();
        }

        if (m_panelRoot == null)
        {
            Transform panel = transform.Find(PanelName);
            if (panel != null)
            {
                m_panelRoot = panel.gameObject;
            }
        }

        Transform searchRoot = m_panelRoot != null ? m_panelRoot.transform : transform;

        if (m_bodyText == null)
        {
            Transform body = searchRoot.Find(BodyName);
            if (body != null)
            {
                m_bodyText = body.GetComponent<TMP_Text>();
            }
        }

        if (m_hintText == null)
        {
            Transform hint = searchRoot.Find(HintName);
            if (hint != null)
            {
                m_hintText = hint.GetComponent<TMP_Text>();
            }
        }
    }

    private void ApplyTexts()
    {
        if (m_bodyText != null)
        {
            m_bodyText.text = m_body;
        }

        if (m_hintText != null)
        {
            m_hintText.text = m_hint;
        }
    }

    private void SetVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }
}
