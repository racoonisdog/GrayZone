using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방어 거점(정문)의 체력과 경고를 화면 HUD에 표시합니다. Defense가 시작될 때 나타납니다.
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: 이 컴포넌트는 UI를 만들지 않고, 씬에 미리 배치된 자식을 이름으로
/// 찾아 값(게이지, 문구, 표시 여부)만 채웁니다.
///
/// 거점 위 월드 HP 바와는 표시하는 곳만 다르고 같은 <see cref="DefenseEventHealth"/>를 봅니다.
/// 월드 바는 <see cref="HealthSystemBase"/>가 직접 갱신하고, 화면 HUD는 여기서 갱신합니다.
/// 경고 문구는 <see cref="DefenseEventHealth"/>가 "언제 낼지"만 정하고 표시는 전부 이 컴포넌트가 합니다.
/// </remarks>
[DisallowMultipleComponent]
public class DefenseObjectiveHud : MonoBehaviour
{
    private const string PanelName = "Panel";
    private const string GaugeName = "Gauge";
    private const string LabelName = "Label";
    private const string WarningName = "Warning";

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("HUD 패널 루트입니다. 이 오브젝트를 켜고 끄는 것으로 표시를 전환합니다. 비어 있으면 자식 'Panel'을 찾습니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("거점 체력 게이지입니다. Image는 fillAmount로, Slider는 value로 채웁니다. 비어 있으면 자식 'Gauge'를 찾습니다.")]
    [SerializeField] private Image m_gaugeFill;

    [Tooltip("거점 이름 텍스트입니다. 비어 있으면 자식 'Label'을 찾습니다.")]
    [SerializeField] private TMP_Text m_labelText;

    [Tooltip("경고 문구 텍스트입니다. 비어 있으면 자식 'Warning'을 찾습니다.")]
    [SerializeField] private TMP_Text m_warningText;

    [Header("Source")]
    [Tooltip("표시할 거점 체력입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseEventHealth m_objective;

    [Tooltip("표시 시점을 맞출 라운드 매니저입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseManager m_defenseManager;

    [Header("Text")]
    [Tooltip("거점 이름입니다.")]
    [SerializeField] private string m_label = "정문";

    [Tooltip("경고 문구를 띄운 뒤 자동으로 지울 시간(초)입니다. 0 이하면 지우지 않습니다.")]
    [SerializeField] private float m_warningDuration = 3.0f;

    [Header("Behaviour")]
    [Tooltip("Defense가 시작될 때 자동으로 표시할지 여부입니다. 끄면 Show()를 직접 불러야 합니다.")]
    [SerializeField] private bool m_showOnDefenseStart = true;

    /// <summary>경고 문구를 지울 시각입니다. 0 이하면 예약 없음입니다.</summary>
    private float m_warningClearTime;

    /// <summary>거점이 파괴된 뒤인지 여부입니다. 파괴 문구는 자동으로 지우지 않습니다.</summary>
    private bool m_objectiveDestroyed;

    /// <summary>HUD가 현재 표시 중인지 여부입니다.</summary>
    public bool IsShown => m_panelRoot != null && m_panelRoot.activeSelf;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        ApplyLabel();
        ClearWarning();
        SetVisible(false);
    }

    private void OnEnable()
    {
        AutoFindReferences();
        Subscribe();
        RefreshGauge();

        // UI 루트가 꺼진 채 씬이 시작되면 OnEnable이 늦게 돌아 시작 이벤트를 놓칩니다.
        // 이벤트만 믿지 않고, 이미 시작된 상태면 여기서 바로 켭니다.
        if (m_showOnDefenseStart && m_defenseManager != null && m_defenseManager.IsGameStarted)
        {
            Show();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (m_objectiveDestroyed || m_warningClearTime <= 0.0f || Time.time < m_warningClearTime)
        {
            return;
        }

        m_warningClearTime = 0.0f;
        ClearWarning();
    }

    /// <summary>HUD를 표시합니다.</summary>
    public void Show()
    {
        AutoFindReferences();
        ApplyLabel();
        RefreshGauge();
        SetVisible(true);
    }

    /// <summary>HUD를 감춥니다.</summary>
    public void Hide()
    {
        SetVisible(false);
    }

    private void Subscribe()
    {
        if (m_defenseManager != null)
        {
            m_defenseManager.OnDefenseStarted -= HandleDefenseStarted;
            m_defenseManager.OnDefenseStarted += HandleDefenseStarted;
        }

        if (m_objective == null)
        {
            return;
        }

        m_objective.OnHPChanged -= HandleHpChanged;
        m_objective.OnHPChanged += HandleHpChanged;
        m_objective.OnWarningRaised -= HandleWarningRaised;
        m_objective.OnWarningRaised += HandleWarningRaised;
        m_objective.OnDestroyed -= HandleObjectiveDestroyed;
        m_objective.OnDestroyed += HandleObjectiveDestroyed;
    }

    private void Unsubscribe()
    {
        if (m_defenseManager != null)
        {
            m_defenseManager.OnDefenseStarted -= HandleDefenseStarted;
        }

        if (m_objective == null)
        {
            return;
        }

        m_objective.OnHPChanged -= HandleHpChanged;
        m_objective.OnWarningRaised -= HandleWarningRaised;
        m_objective.OnDestroyed -= HandleObjectiveDestroyed;
    }

    private void HandleDefenseStarted()
    {
        if (!m_showOnDefenseStart)
        {
            return;
        }

        m_objectiveDestroyed = false;
        ClearWarning();
        Show();
    }

    private void HandleHpChanged(int currentHp, int maxHp)
    {
        ApplyGauge(maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0.0f);
    }

    private void HandleWarningRaised(string message)
    {
        if (m_warningText == null)
        {
            return;
        }

        m_warningText.text = message;
        m_warningText.gameObject.SetActive(true);
        m_warningClearTime = m_warningDuration > 0.0f ? Time.time + m_warningDuration : 0.0f;
    }

    /// <summary>파괴되면 게이지를 비우고 마지막 문구를 남깁니다.</summary>
    /// <remarks>게임오버 화면이 뜨기 전까지 사유가 보여야 해서 자동 삭제를 걸지 않습니다.</remarks>
    private void HandleObjectiveDestroyed()
    {
        m_objectiveDestroyed = true;
        m_warningClearTime = 0.0f;
        ApplyGauge(0.0f);
    }

    private void RefreshGauge()
    {
        if (m_objective == null)
        {
            return;
        }

        int max = m_objective.MaxHP;
        ApplyGauge(max > 0 ? Mathf.Clamp01((float)m_objective.CurrentHP / max) : 0.0f);
    }

    private void ApplyGauge(float normalized)
    {
        if (m_gaugeFill != null)
        {
            m_gaugeFill.fillAmount = normalized;
        }
    }

    private void ApplyLabel()
    {
        if (m_labelText != null)
        {
            m_labelText.text = m_label;
        }
    }

    private void ClearWarning()
    {
        if (m_warningText == null)
        {
            return;
        }

        m_warningText.text = string.Empty;
        m_warningText.gameObject.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }

    private void AutoFindReferences()
    {
        if (m_objective == null)
        {
            m_objective = FindFirstObjectByType<DefenseEventHealth>(FindObjectsInactive.Include);
        }

        if (m_defenseManager == null)
        {
            m_defenseManager = FindFirstObjectByType<DefenseManager>(FindObjectsInactive.Include);
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

        if (m_gaugeFill == null)
        {
            Transform gauge = searchRoot.Find(GaugeName);
            if (gauge != null)
            {
                m_gaugeFill = gauge.GetComponent<Image>();
            }
        }

        if (m_labelText == null)
        {
            Transform label = searchRoot.Find(LabelName);
            if (label != null)
            {
                m_labelText = label.GetComponent<TMP_Text>();
            }
        }

        if (m_warningText == null)
        {
            Transform warning = searchRoot.Find(WarningName);
            if (warning != null)
            {
                m_warningText = warning.GetComponent<TMP_Text>();
            }
        }
    }
}
