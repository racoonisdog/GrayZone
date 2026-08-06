using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 출격 후보 Scroll View와 세 개의 선택 슬롯을 표시하고 OperationCenter에 사용자 명령을 전달합니다.
/// </summary>
public sealed class OperationUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Selection")]
    [SerializeField] private CharacterCandidateListPanel m_candidateListPanel;
    [SerializeField] private OperationSquadSlotView[] m_slots =
        new OperationSquadSlotView[ShelterRuntimeData.MaxFieldSquadSize];

    [Header("Buttons")]
    [SerializeField] private Button m_selectButton;
    [SerializeField] private Button m_confirmButton;
    [SerializeField] private Button m_deployButton;
    [SerializeField] private Button m_cancelButton;

    [Header("Feedback (Optional)")]
    [Tooltip("선택 제한, 확정 결과, 출격 실패 원인을 표시할 선택형 안내 문구입니다.")]
    [SerializeField] private TMP_Text m_noticeText;

    private readonly List<ShelterMemberRuntimeData> m_candidates = new();
    private OperationCenter m_currentCenter;
    private bool m_isOpening;
    private bool m_isOpen;

    public bool IsOpen => m_isOpen;
    public event Action Closed;

    private void Awake()
    {
        CacheReferences();
        m_isOpen = m_root != null && m_root.activeSelf;

        if (m_hideOnAwake && !m_isOpening)
            Close();
    }

    private void Reset()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        BindButtons();
        BindSlots();
    }

    private void OnDisable()
    {
        UnbindButtons();
        UnbindSlots();
        CloseCandidateList();
        UnbindCenter(true);

        if (!m_isOpening)
            SetOpenState(false);
    }

    public void Open(OperationCenter center)
    {
        if (center == null)
        {
            Debug.LogWarning("[OperationUI] OperationCenter is not available.", this);
            return;
        }

        UnbindCenter(true);
        m_currentCenter = center;
        m_currentCenter.SelectionChanged += Refresh;
        m_currentCenter.BeginSelectionSession();

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        CacheReferences();
        BindButtons();
        BindSlots();
        CloseCandidateList();
        ClearNotice();
        Refresh();
    }

    /// <summary>출격하지 않고 UI를 닫고 현재 선택 세션을 폐기합니다.</summary>
    public void Close()
    {
        CloseCandidateList();
        UnbindCenter(true);
        ClearNotice();
        SetRootActive(false);
        SetOpenState(false);
    }

    /// <summary>ESC 입력 시 OperationUI 전체를 닫고 선택 데이터를 폐기합니다.</summary>
    public bool TryHandleEscape()
    {
        if (!m_isOpen)
            return false;

        Close();
        return true;
    }

    public void Refresh()
    {
        IReadOnlyList<string> selectedIds = m_currentCenter?.PendingSquadRuntimeIds;
        m_slots ??= Array.Empty<OperationSquadSlotView>();
        for (int i = 0; i < m_slots.Length; i++)
        {
            OperationSquadSlotView slot = m_slots[i];
            if (slot == null)
                continue;

            if (selectedIds != null
                && i < selectedIds.Count
                && m_currentCenter.TryGetCharacter(
                    selectedIds[i],
                    out ShelterMemberRuntimeData character))
            {
                slot.SetCharacter(character);
            }
            else
            {
                slot.ClearCharacter();
            }
        }

        if (m_confirmButton != null)
        {
            m_confirmButton.interactable = m_currentCenter != null
                && m_currentCenter.PendingCount == ShelterRuntimeData.MaxFieldSquadSize;
        }

        if (m_deployButton != null)
            m_deployButton.interactable = m_currentCenter != null && m_currentCenter.HasConfirmedSquad;
    }

    /// <summary>후보 Scroll View를 열고 클릭 토글을 시작합니다.</summary>
    public void OpenCandidateList()
    {
        if (m_currentCenter == null)
            return;

        CharacterCandidateListPanel panel = CacheCandidateListPanel();
        if (panel == null)
        {
            SetNotice("캐릭터 선택 목록을 찾을 수 없습니다.");
            return;
        }

        m_currentCenter.FillDeploymentCandidates(m_candidates);
        panel.Open(this, m_candidates, HandleCandidateClicked);
        ClearNotice();
    }

    /// <summary>정확히 세 명인 현재 선택을 세션 안에서 확정합니다.</summary>
    public void ConfirmSelection()
    {
        if (m_currentCenter == null || !m_currentCenter.TryConfirmSelection())
        {
            SetNotice("출격 캐릭터를 정확히 3명 선택해야 합니다.");
            Refresh();
            return;
        }

        CloseCandidateList();
        SetNotice("출격 캐릭터 3명을 확정했습니다.");
        Refresh();
    }

    /// <summary>확정 명단을 동기화하고 필드 씬으로 이동합니다.</summary>
    public void Deploy()
    {
        if (m_currentCenter == null || !m_currentCenter.TryDeploy())
            SetNotice("출격하지 못했습니다. 캐릭터 확정과 씬 설정을 확인하세요.");
    }

    public void Cancel()
    {
        Close();
    }

    /// <summary>지정 출격 슬롯의 임시 선택을 제거합니다.</summary>
    private void HandleSlotRemoveRequested(int slotIndex)
    {
        if (m_currentCenter != null && m_currentCenter.TryRemovePendingAt(slotIndex))
            ClearNotice();
    }

    private void HandleCandidateClicked(string runtimeId)
    {
        if (m_currentCenter == null)
            return;

        if (!m_currentCenter.TryTogglePendingCharacter(runtimeId))
        {
            SetNotice("출격 캐릭터는 최대 3명까지 선택할 수 있습니다.");
            return;
        }

        ClearNotice();
        Refresh();
    }

    private void BindButtons()
    {
        UnbindButtons();

        if (m_selectButton != null)
            m_selectButton.onClick.AddListener(OpenCandidateList);
        if (m_confirmButton != null)
            m_confirmButton.onClick.AddListener(ConfirmSelection);
        if (m_deployButton != null)
            m_deployButton.onClick.AddListener(Deploy);
        if (m_cancelButton != null)
            m_cancelButton.onClick.AddListener(Cancel);
    }

    private void BindSlots()
    {
        m_slots ??= Array.Empty<OperationSquadSlotView>();
        for (int i = 0; i < m_slots.Length; i++)
        {
            if (m_slots[i] != null)
                m_slots[i].Bind(i, HandleSlotRemoveRequested);
        }
    }

    private void UnbindSlots()
    {
        if (m_slots == null)
            return;

        for (int i = 0; i < m_slots.Length; i++)
        {
            if (m_slots[i] != null)
                m_slots[i].Unbind();
        }
    }

    private void UnbindButtons()
    {
        if (m_selectButton != null)
            m_selectButton.onClick.RemoveListener(OpenCandidateList);
        if (m_confirmButton != null)
            m_confirmButton.onClick.RemoveListener(ConfirmSelection);
        if (m_deployButton != null)
            m_deployButton.onClick.RemoveListener(Deploy);
        if (m_cancelButton != null)
            m_cancelButton.onClick.RemoveListener(Cancel);
    }

    private void CloseCandidateList()
    {
        if (m_candidateListPanel != null)
            m_candidateListPanel.Close(this);

        m_candidates.Clear();
    }

    private CharacterCandidateListPanel CacheCandidateListPanel()
    {
        if (m_candidateListPanel == null)
        {
            m_candidateListPanel =
                FindFirstObjectByType<CharacterCandidateListPanel>(FindObjectsInactive.Include);
        }

        return m_candidateListPanel;
    }

    private void CacheReferences()
    {
        if (m_root == null)
            m_root = gameObject;

        CacheCandidateListPanel();
        CacheSlots();
    }

    private void CacheSlots()
    {
        if (m_slots != null)
        {
            for (int i = 0; i < m_slots.Length; i++)
            {
                if (m_slots[i] != null)
                    return;
            }
        }

        m_slots = GetComponentsInChildren<OperationSquadSlotView>(true);
    }

    private void UnbindCenter(bool discardSelection)
    {
        if (m_currentCenter == null)
            return;

        m_currentCenter.SelectionChanged -= Refresh;
        if (discardSelection)
            m_currentCenter.CancelSelectionSession();

        m_currentCenter = null;
    }

    private void SetNotice(string message)
    {
        if (m_noticeText != null)
            m_noticeText.text = message;
        else if (!string.IsNullOrWhiteSpace(message))
            Debug.Log($"[OperationUI] {message}", this);
    }

    private void ClearNotice()
    {
        if (m_noticeText != null)
            m_noticeText.text = string.Empty;
    }

    private void SetRootActive(bool active)
    {
        if (m_root != null && m_root.activeSelf != active)
            m_root.SetActive(active);
    }

    private void SetOpenState(bool isOpen)
    {
        if (m_isOpen == isOpen)
            return;

        m_isOpen = isOpen;
        if (!m_isOpen)
            Closed?.Invoke();
    }
}
