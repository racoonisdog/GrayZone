using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 의료시설 UI의 치료 후보, 환자 슬롯, 일괄 치료 흐름을 조정합니다.
/// </summary>
public class MedicalUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Treatment Candidates")]
    [SerializeField] private TestButton[] m_treatmentCandidateViews;
    [SerializeField] private TextMeshProUGUI m_noTreatmentTargetText;

    [Header("Patient Slots")]
    [SerializeField] private MedicalPatientSlotView[] m_patientSlots;
    [SerializeField] private Button m_healButton;

    [Header("Character Access")]
    [SerializeField] private CharacterManager m_characterManager;

    private readonly List<ShelterMemberRuntimeData> m_treatmentCandidates = new();

    private MedicalManager m_currentManager;
    private CharacterManager m_boundCharacterManager;
    private bool m_isOpening;
    private bool m_isOpen;

    public bool IsOpen => m_isOpen;

    public event Action Closed;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        CacheChildViews();
        BindHealButton();
        m_isOpen = m_root != null && m_root.activeSelf;

        if (m_hideOnAwake && !m_isOpening)
            Close();
    }

    private void Reset()
    {
        m_root = gameObject;
        CacheChildViews();
    }

    private void OnDisable()
    {
        UnbindManager();
        UnbindCharacterManager();

        if (!m_isOpening)
            SetOpenState(false);
    }

    private void OnDestroy()
    {
        if (m_healButton != null)
            m_healButton.onClick.RemoveListener(HandleHealClicked);
    }

    public bool TryHandleEscape()
    {
        if (!m_isOpen)
            return false;

        Close();
        return true;
    }

    public void Open(MedicalManager manager)
    {
        UnbindManager();

        m_currentManager = manager;
        if (m_currentManager != null)
            m_currentManager.OnPatientSlotsChanged += Refresh;

        BindCharacterManager(CacheCharacterManager());

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        CacheChildViews();
        BindHealButton();
        m_currentManager?.RestoreRuntimeState();
        Refresh();
    }

    public void Close()
    {
        ClearTreatmentCandidateViews();
        UnbindManager();
        UnbindCharacterManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    public void Refresh()
    {
        CacheChildViews();
        RefreshPatientSlots();
        RefreshTreatmentCandidates();

        if (m_healButton != null)
        {
            m_healButton.interactable = m_currentManager != null
                && m_currentManager.CurrentPatientCount > 0;
        }
    }

    private void CacheChildViews()
    {
        if (m_patientSlots == null || m_patientSlots.Length == 0)
            m_patientSlots = GetComponentsInChildren<MedicalPatientSlotView>(true);

        if (m_treatmentCandidateViews == null || m_treatmentCandidateViews.Length == 0)
            m_treatmentCandidateViews = GetComponentsInChildren<TestButton>(true);
    }

    private void BindHealButton()
    {
        if (m_healButton == null)
            return;

        m_healButton.onClick.RemoveListener(HandleHealClicked);
        m_healButton.onClick.AddListener(HandleHealClicked);
    }

    private void RefreshPatientSlots()
    {
        if (m_patientSlots == null)
            return;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slotView = m_patientSlots[i];
            if (slotView == null)
                continue;

            bool isUnlocked = m_currentManager != null
                && m_currentManager.IsPatientSlotUnlocked(i);
            bool isAvailable = m_currentManager != null
                && m_currentManager.IsPatientSlotAvailable(i);
            slotView.Bind(i, isUnlocked, isAvailable, HandlePatientSlotClicked);
            slotView.ClearPatient();
        }

        if (m_currentManager == null)
            return;

        IReadOnlyList<PatientStatus> statuses = m_currentManager.PatientStatuses;
        for (int i = 0; i < statuses.Count; i++)
        {
            PatientStatus status = statuses[i];
            int slotIndex = status.SlotIndex;
            if (status.Patient == null
                || slotIndex < 0
                || slotIndex >= m_patientSlots.Length
                || m_patientSlots[slotIndex] == null)
            {
                continue;
            }

            MedicalPatientSlotView slotView = m_patientSlots[slotIndex];
            slotView.SetPatient(status.Patient.RuntimeId, status.Patient.DefinitionId);
            slotView.ApplyStatus(status, false);
        }
    }

    private void RefreshTreatmentCandidates()
    {
        m_treatmentCandidates.Clear();
        if (m_currentManager != null)
            m_currentManager.FillPatientCandidates(m_treatmentCandidates);

        int visibleCount = Mathf.Min(
            m_treatmentCandidates.Count,
            m_treatmentCandidateViews?.Length ?? 0);

        for (int i = 0; i < visibleCount; i++)
        {
            ShelterMemberRuntimeData character = m_treatmentCandidates[i];
            bool isInteractable = character != null
                && !m_currentManager.IsPatientAssigned(character.RuntimeId);
            m_treatmentCandidateViews[i]?.Bind(
                character,
                isInteractable,
                HandleTreatmentCandidateClicked);
        }

        if (m_treatmentCandidateViews != null)
        {
            for (int i = visibleCount; i < m_treatmentCandidateViews.Length; i++)
                m_treatmentCandidateViews[i]?.Clear();
        }

        if (m_noTreatmentTargetText != null)
            m_noTreatmentTargetText.gameObject.SetActive(m_treatmentCandidates.Count == 0);
    }

    private void ClearTreatmentCandidateViews()
    {
        m_treatmentCandidates.Clear();
        if (m_treatmentCandidateViews == null)
            return;

        for (int i = 0; i < m_treatmentCandidateViews.Length; i++)
            m_treatmentCandidateViews[i]?.Clear();
    }

    private void HandleTreatmentCandidateClicked(string runtimeId)
    {
        if (m_currentManager == null || string.IsNullOrWhiteSpace(runtimeId))
            return;

        m_currentManager.TryAssignPatient(runtimeId);
        Refresh();
    }

    private void HandlePatientSlotClicked(MedicalPatientSlotView slot)
    {
        if (m_currentManager == null || slot == null || !slot.HasPatient)
            return;

        m_currentManager.TryReleasePatientAtSlot(slot.SlotIndex);
        Refresh();
    }

    private void HandleHealClicked()
    {
        if (m_currentManager == null)
            return;

        m_currentManager.TryHealAssignedPatients();
        Refresh();
    }

    private CharacterManager CacheCharacterManager()
    {
        if (m_characterManager == null)
            m_characterManager = CharacterManager.Instance;

        if (m_characterManager == null)
            m_characterManager = FindFirstObjectByType<CharacterManager>();

        return m_characterManager;
    }

    private void BindCharacterManager(CharacterManager manager)
    {
        if (m_boundCharacterManager == manager)
            return;

        UnbindCharacterManager();
        m_boundCharacterManager = manager;
        if (m_boundCharacterManager == null)
            return;

        m_boundCharacterManager.CharacterChanged += HandleCharacterChanged;
        m_boundCharacterManager.CharactersChanged += HandleCharactersChanged;
    }

    private void UnbindCharacterManager()
    {
        if (m_boundCharacterManager != null)
        {
            m_boundCharacterManager.CharacterChanged -= HandleCharacterChanged;
            m_boundCharacterManager.CharactersChanged -= HandleCharactersChanged;
        }

        m_boundCharacterManager = null;
    }

    private void HandleCharacterChanged(ShelterMemberRuntimeData character)
    {
        if (m_isOpen)
            Refresh();
    }

    private void HandleCharactersChanged()
    {
        if (m_isOpen)
            Refresh();
    }

    private void UnbindManager()
    {
        if (m_currentManager != null)
            m_currentManager.OnPatientSlotsChanged -= Refresh;

        m_currentManager = null;
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
