using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 의료 시설 UI의 열기/닫기, 환자 슬롯 표시, 환자/헬퍼 후보 목록을 조율
/// </summary>
public class MedicalUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Patient Slots")]
    [SerializeField] private MedicalPatientSlotView[] m_patientSlots;

    [Header("Treatment Candidates")]
    [SerializeField] private CharacterManager m_characterManager;
    [SerializeField] private CharacterCandidateListPanel m_candidateListPanel;

    private readonly List<ShelterMemberRuntimeData> m_treatmentCandidates = new List<ShelterMemberRuntimeData>();

    private MedicalManager m_currentManager;
    private CharacterManager m_boundCharacterManager;
    private CharacterCandidateListPanel m_boundCandidateListPanel;
    private MedicalPatientSlotView m_pendingSlot;   // 배치 대상으로 클릭해 둔 빈 슬롯
    private bool m_isOpening;
    private bool m_isOpen;
    private bool m_isTreatmentCandidateListOpen;
    private bool m_helperMode;   // 후보 목록 모드: false=환자, true=헬퍼

    /// <summary>의료 UI가 현재 열린 상태인지 여부</summary>
    public bool IsOpen => m_isOpen;

    /// <summary>의료 UI가 닫힐 때 발생</summary>
    public event Action Closed;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        CacheChildViews();
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
        if (m_boundCandidateListPanel != null)
        {
            m_boundCandidateListPanel.ClosedBy -= HandleCandidateListClosedBy;
            m_boundCandidateListPanel = null;
        }
    }

    private void Update()
    {
        if (Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // 후보 목록이 열려 있으면 목록만 먼저 닫는다.
            if (m_isTreatmentCandidateListOpen)
                HideTreatmentCandidateList();
            else
                Close();
        }
    }


    /// <summary>
    /// 지정한 의료 매니저와 바인딩하고 의료 UI를 열기.
    /// </summary>
    /// <param name="manager">UI가 표시하고 조작할 의료 시설 매니저</param>
    public void Open(MedicalManager manager)
    {
        UnbindManager();

        m_currentManager = manager;
        if (m_currentManager != null)
        {
            m_currentManager.OnPatientSlotsChanged += Refresh;
            m_currentManager.OnPatientHealed += HandlePatientHealed;
        }

        BindCharacterManager(CacheCharacterManager());

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        CacheChildViews();
        HideTreatmentCandidateList();
        SyncSlotsFromManager();
        Refresh();
    }

    /// <summary>
    /// 의료 UI를 닫고 연결된 매니저/캐릭터 이벤트 구독을 해제
    /// </summary>
    public void Close()
    {
        HideTreatmentCandidateList();
        UnbindManager();
        UnbindCharacterManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    /// <summary>
    /// 환자 슬롯과 후보 목록 표시 갱신
    /// </summary>
    public void Refresh()
    {
        CacheChildViews();
        RefreshPatientSlots();
        ApplyPatientStatusesToSlots();

        if (m_isTreatmentCandidateListOpen)
            RefreshTreatmentCandidates();
        else
            ClearTreatmentCandidateList();
    }

    private void CacheChildViews()
    {
        if (m_patientSlots == null || m_patientSlots.Length == 0)
            m_patientSlots = GetComponentsInChildren<MedicalPatientSlotView>(true);
    }

    private void RefreshPatientSlots()
    {
        if (m_patientSlots == null)
            return;

        int unlockedSlotCount = m_currentManager != null
            ? m_currentManager.PatientCapacity
            : 0;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slotView = m_patientSlots[i];
            if (slotView == null)
                continue;

            bool isUnlocked = i < unlockedSlotCount;
            slotView.Bind(isUnlocked, HandleSlotClicked);
        }
    }

    // 각 점유 슬롯에 게이지/부상상태/남은일수 반영. 게이지 바는 레벨2+(UpgradeLevel>=1)에서만 표시.
    private void ApplyPatientStatusesToSlots()
    {
        if (m_patientSlots == null || m_currentManager == null)
            return;

        IReadOnlyList<PatientStatus> statuses = m_currentManager.PatientStatuses;
        //Todo : 1로 바꾸기
        bool showGauge = m_currentManager.UpgradeLevel >= 0;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slot = m_patientSlots[i];
            if (slot == null || !slot.HasPatient)
                continue;

            for (int j = 0; j < statuses.Count; j++)
            {
                PatientStatus status = statuses[j];
                if (status.Patient != null && status.Patient.RuntimeId == slot.PatientRuntimeId)
                {
                    slot.ApplyStatus(status, showGauge);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 헬퍼 배치 모드로 후보 목록을 열기.
    /// </summary>
    public void BeginHelperAssignment()
    {
        if (m_currentManager == null)
            return;

        m_helperMode = true;
        m_pendingSlot = null;
        ShowTreatmentCandidateList();
        RefreshTreatmentCandidates();
    }

    // UI를 열 때 1회: 현재 치료 중인 환자를 슬롯에 초기 배치한다.
    // 이후의 위치는 배치/취소/완치 이벤트로 증분 유지되며, Refresh는 점유를 재배치하지 않는다.
    private void SyncSlotsFromManager()
    {
        if (m_patientSlots == null)
            return;

        for (int i = 0; i < m_patientSlots.Length; i++)
            if (m_patientSlots[i] != null)
                m_patientSlots[i].ClearPatient();

        if (m_currentManager == null)
            return;

        // 환자 수 <= 수용량이므로 앞 슬롯부터 채우면 모두 해금 슬롯 범위 안에 들어간다.
        IReadOnlyList<PatientStatus> statuses = m_currentManager.PatientStatuses;
        for (int i = 0; i < statuses.Count && i < m_patientSlots.Length; i++)
        {
            ShelterMemberRuntimeData patient = statuses[i].Patient;
            if (patient != null && m_patientSlots[i] != null)
                m_patientSlots[i].SetPatient(patient.RuntimeId, patient.DefinitionId);
        }
    }

    private void HandlePatientHealed(ShelterMemberRuntimeData patient)
    {
        if (patient != null)
            ClearSlotByPatientId(patient.RuntimeId);
    }

    private void ClearSlotByPatientId(string runtimeId)
    {
        if (m_patientSlots == null || string.IsNullOrWhiteSpace(runtimeId))
            return;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slot = m_patientSlots[i];
            if (slot != null && slot.HasPatient && slot.PatientRuntimeId == runtimeId)
            {
                slot.ClearPatient();
                return;
            }
        }
    }

    private void RefreshTreatmentCandidates()
    {
        ClearTreatmentCandidateList();

        CharacterCandidateListPanel panel = CacheCandidateListPanel();
        if (m_currentManager == null || panel == null)
            return;

        if (m_helperMode)
        {
            if (m_currentManager.CurrentHelperCount < m_currentManager.HelperCapacity)
                m_currentManager.FillHelperCandidates(m_treatmentCandidates);
        }
        else
        {
            if (m_currentManager.CurrentPatientCount < m_currentManager.PatientCapacity)
                m_currentManager.FillPatientCandidates(m_treatmentCandidates);
        }

        // 정원이 가득 차면 빈 목록을 넘겨 빈 패널을 표시(기존 동작과 동일).
        panel.Open(this, m_treatmentCandidates, HandleTreatmentCandidateClicked);
    }

    // 로컬 후보 버퍼만 비운다. 패널의 행 제거는 패널 소유권 규칙(Open/Close)에 맡긴다.
    private void ClearTreatmentCandidateList()
    {
        m_treatmentCandidates.Clear();
    }

    private void HandleSlotClicked(MedicalPatientSlotView slot)
    {
        if (slot == null || m_currentManager == null)
            return;

        if (slot.HasPatient)
        {
            // 점유 슬롯 → 배치 취소
            if (m_currentManager.TryReleasePatient(slot.PatientRuntimeId))
            {
                slot.ClearPatient();
                HideTreatmentCandidateList();
            }
            return;
        }

        // 빈 슬롯 → 환자 후보 목록 열기 (이 슬롯을 배치 대상으로 기억)
        m_helperMode = false;
        m_pendingSlot = slot;
        ShowTreatmentCandidateList();
        RefreshTreatmentCandidates();
    }

    private void HandleTreatmentCandidateClicked(string runtimeId)
    {
        if (m_currentManager == null)
            return;

        bool wasCandidateListOpen = m_isTreatmentCandidateListOpen;
        m_isTreatmentCandidateListOpen = false;

        ShelterMemberRuntimeData selected = m_treatmentCandidates.Find(
            character => character != null && character.RuntimeId == runtimeId);
        bool assigned = m_helperMode
            ? m_currentManager.TryAssignHelper(runtimeId)
            : m_currentManager.TryAssignPatient(runtimeId);

        if (assigned)
        {
            // 환자 모드에서만 클릭해 둔 빈 슬롯에 고정 배치. 헬퍼 전용 슬롯 UI는 에디터 배선 필요.
            if (!m_helperMode && m_pendingSlot != null && selected != null)
                m_pendingSlot.SetPatient(selected.RuntimeId, selected.DefinitionId);
            m_pendingSlot = null;

            HideTreatmentCandidateList();
            RefreshPatientSlots();
            ApplyPatientStatusesToSlots();
        }
        else
        {
            m_isTreatmentCandidateListOpen = wasCandidateListOpen;

            if (m_isTreatmentCandidateListOpen)
                RefreshTreatmentCandidates();
        }
    }

    private void ShowTreatmentCandidateList()
    {
        // 실제 패널 표시는 RefreshTreatmentCandidates의 Open에서 일어난다.
        m_isTreatmentCandidateListOpen = true;
    }

    private void HideTreatmentCandidateList()
    {
        m_isTreatmentCandidateListOpen = false;
        m_helperMode = false;
        m_pendingSlot = null;
        ClearTreatmentCandidateList();

        // OnDisable/Close 경로에서도 호출되므로 씬 검색 없이 캐시된 패널만 닫는다.
        if (m_candidateListPanel != null)
            m_candidateListPanel.Close(this);
    }

    // 패널이 닫히거나 다른 시설 UI가 패널을 가져갔을 때 로컬 상태를 정리한다.
    private void HandleCandidateListClosedBy(object requester)
    {
        if (!ReferenceEquals(requester, this))
            return;

        m_isTreatmentCandidateListOpen = false;
        m_helperMode = false;
        m_pendingSlot = null;
        m_treatmentCandidates.Clear();
    }

    private CharacterCandidateListPanel CacheCandidateListPanel()
    {
        if (m_candidateListPanel == null)
            m_candidateListPanel = FindFirstObjectByType<CharacterCandidateListPanel>(FindObjectsInactive.Include);

        // 패널 참조가 바뀌면 ClosedBy 구독을 옮긴다.
        if (m_candidateListPanel != m_boundCandidateListPanel)
        {
            if (m_boundCandidateListPanel != null)
                m_boundCandidateListPanel.ClosedBy -= HandleCandidateListClosedBy;

            m_boundCandidateListPanel = m_candidateListPanel;

            if (m_boundCandidateListPanel != null)
                m_boundCandidateListPanel.ClosedBy += HandleCandidateListClosedBy;
        }

        return m_candidateListPanel;
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
        {
            m_currentManager.OnPatientSlotsChanged -= Refresh;
            m_currentManager.OnPatientHealed -= HandlePatientHealed;
        }

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
