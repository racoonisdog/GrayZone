using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 헬퍼 슬롯을 매니저 상태로 투영하고 공용 캐릭터 후보 패널로 배치/해제를 전달합니다.
/// </summary>
public sealed class ManufacturingStaffSlotController : MonoBehaviour
{
    [SerializeField] private ManufacturingStaffSlotView[] m_slots;
    [SerializeField] private ManufacturingManager m_manufacturingManager;
    [SerializeField] private CharacterCandidateListPanel m_candidateListPanel;

    private readonly List<ShelterMemberRuntimeData> m_candidates = new();
    private readonly List<ShelterMemberRuntimeData> m_assignedHelpers = new();

    private CharacterCandidateListPanel m_boundPanel;
    private ManufacturingManager m_boundManager;
    private ManufacturingStaffSlotView m_pendingSlot;

    private void Awake()
    {
        CacheChildViews();
    }

    private void Reset()
    {
        CacheChildViews();
    }

    private void OnEnable()
    {
        BindManager(CacheManufacturingManager());
        RefreshSlots();
    }

    private void OnDisable()
    {
        UnbindManager();
        HideCandidateList();
    }

    private void OnDestroy()
    {
        if (m_boundPanel != null)
            m_boundPanel.ClosedBy -= HandlePanelClosedBy;
    }

    public void SetManager(ManufacturingManager manager)
    {
        m_manufacturingManager = manager;
        if (!isActiveAndEnabled)
            return;

        BindManager(manager);
        RefreshSlots();
    }

    public void RefreshSlots()
    {
        CacheChildViews();
        ManufacturingManager manager = CacheManufacturingManager();

        m_assignedHelpers.Clear();
        manager?.FillAssignedHelpers(m_assignedHelpers);

        for (int i = 0; i < m_slots.Length; i++)
        {
            ManufacturingStaffSlotView slot = m_slots[i];
            if (slot == null)
                continue;

            bool isUnlocked = manager != null && manager.IsHelperSlotUnlocked(i);
            if (i < m_assignedHelpers.Count && m_assignedHelpers[i] != null)
            {
                ShelterMemberRuntimeData helper = m_assignedHelpers[i];
                slot.SetHelper(helper.RuntimeId, helper.DefinitionId);
            }
            else
            {
                slot.ClearHelper();
            }

            slot.Bind(isUnlocked, HandleSlotClicked, HandleCancelClicked);
        }
    }

    private void HandleSlotClicked(ManufacturingStaffSlotView slot)
    {
        ManufacturingManager manager = CacheManufacturingManager();
        if (slot == null || manager == null || slot.HasHelper)
            return;

        m_pendingSlot = slot;
        RefreshCandidates();
    }

    private void HandleCancelClicked(ManufacturingStaffSlotView slot)
    {
        ManufacturingManager manager = CacheManufacturingManager();
        if (slot == null || manager == null || !slot.HasHelper)
            return;

        if (manager.TryReleaseHelper(slot.HelperRuntimeId))
            HideCandidateList();
    }

    private void RefreshCandidates()
    {
        m_candidates.Clear();

        ManufacturingManager manager = CacheManufacturingManager();
        CharacterCandidateListPanel panel = CacheCandidateListPanel();
        if (manager == null || panel == null)
            return;

        if (manager.CurrentHelperCount < manager.HelperCapacity)
            manager.FillHelperCandidates(m_candidates);

        panel.Open(this, m_candidates, HandleCandidateClicked);
    }

    private void HandleCandidateClicked(string runtimeId)
    {
        ManufacturingManager manager = CacheManufacturingManager();
        if (manager == null)
            return;

        if (manager.TryAssignHelper(runtimeId))
        {
            m_pendingSlot = null;
            HideCandidateList();
            RefreshSlots();
        }
        else
        {
            RefreshCandidates();
        }
    }

    private void HandleHelpersChanged()
    {
        RefreshSlots();
    }

    private void BindManager(ManufacturingManager manager)
    {
        if (m_boundManager == manager)
            return;

        UnbindManager();
        m_boundManager = manager;
        if (m_boundManager != null)
            m_boundManager.HelpersChanged += HandleHelpersChanged;
    }

    private void UnbindManager()
    {
        if (m_boundManager != null)
            m_boundManager.HelpersChanged -= HandleHelpersChanged;

        m_boundManager = null;
    }

    private void HideCandidateList()
    {
        m_pendingSlot = null;
        m_candidates.Clear();
        if (m_candidateListPanel != null)
            m_candidateListPanel.Close(this);
    }

    private void HandlePanelClosedBy(object requester)
    {
        if (!ReferenceEquals(requester, this))
            return;

        m_pendingSlot = null;
        m_candidates.Clear();
    }

    private CharacterCandidateListPanel CacheCandidateListPanel()
    {
        if (m_candidateListPanel == null)
        {
            m_candidateListPanel =
                FindFirstObjectByType<CharacterCandidateListPanel>(FindObjectsInactive.Include);
        }

        if (m_candidateListPanel == m_boundPanel)
            return m_candidateListPanel;

        if (m_boundPanel != null)
            m_boundPanel.ClosedBy -= HandlePanelClosedBy;

        m_boundPanel = m_candidateListPanel;
        if (m_boundPanel != null)
            m_boundPanel.ClosedBy += HandlePanelClosedBy;

        return m_candidateListPanel;
    }

    private ManufacturingManager CacheManufacturingManager()
    {
        if (m_manufacturingManager == null)
            m_manufacturingManager = FindFirstObjectByType<ManufacturingManager>();

        return m_manufacturingManager;
    }

    private void CacheChildViews()
    {
        if (m_slots == null || m_slots.Length == 0)
            m_slots = GetComponentsInChildren<ManufacturingStaffSlotView>(true);
    }
}
