using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StaffSlot 아래의 헬퍼 슬롯(<see cref="MedicalStaffSlotView"/>)들을 관리하는 컨트롤러.
/// 슬롯 클릭 → 후보를 공용 패널(<see cref="NpcCandidateListPanel"/>)에 넘겨 표시 → 후보 클릭 → 헬퍼 배치.
/// 후보 필터: 부상상태가 Light 이상 Healthy 미만(= 경상만).
/// 슬롯 해금은 계층 순서(=배열 순서)와 <see cref="MedicalManager.HelperCapacity"/>로 판정한다.
/// </summary>
public class MedicalStaffSlotController : MonoBehaviour
{
    [Header("Slots")]
    [SerializeField] private MedicalStaffSlotView[] m_slots;

    [Header("Candidates")]
    [SerializeField] private MedicalManager m_medicalManager;
    [SerializeField] private CharacterManager m_characterManager;
    [SerializeField] private NpcCandidateListPanel m_candidateListPanel;

    private readonly List<NPCRuntimeData> m_candidates = new List<NPCRuntimeData>();

    private NpcCandidateListPanel m_boundPanel;
    private MedicalStaffSlotView m_pendingSlot;   // 배치 대상으로 클릭해 둔 빈 슬롯

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
        MedicalManager manager = CacheMedicalManager();
        if (manager != null)
        {
            manager.OnPatientSlotsChanged += RefreshSlots;
            manager.OnHelperReleased += HandleHelperReleased;
        }

        RefreshSlots();
    }

    private void OnDisable()
    {
        if (m_medicalManager != null)
        {
            m_medicalManager.OnPatientSlotsChanged -= RefreshSlots;
            m_medicalManager.OnHelperReleased -= HandleHelperReleased;
        }

        HideCandidateList();
    }

    private void OnDestroy()
    {
        if (m_boundPanel != null)
        {
            m_boundPanel.ClosedBy -= HandlePanelClosedBy;
            m_boundPanel = null;
        }
    }

    // 슬롯 해금/콜백 바인딩을 다시 계산한다. 점유 상태는 건드리지 않는다.
    private void RefreshSlots()
    {
        CacheChildViews();

        if (m_slots == null)
            return;

        MedicalManager manager = CacheMedicalManager();
        int unlockedSlotCount = manager != null ? manager.HelperCapacity : 0;

        for (int i = 0; i < m_slots.Length; i++)
        {
            MedicalStaffSlotView slotView = m_slots[i];
            if (slotView == null)
                continue;

            bool isUnlocked = i < unlockedSlotCount;
            slotView.Bind(isUnlocked, HandleSlotClicked);
        }
    }

    private void HandleSlotClicked(MedicalStaffSlotView slot)
    {
        MedicalManager manager = CacheMedicalManager();
        if (slot == null || manager == null)
            return;

        if (slot.HasHelper)
        {
            // 점유 슬롯 → 배치 취소 (별도 취소 버튼 없음)
            if (manager.TryReleaseHelper(slot.HelperId))
            {
                slot.ClearHelper();
                HideCandidateList();
            }
            return;
        }

        // 빈 슬롯 → 후보 목록 열기 (이 슬롯을 배치 대상으로 기억)
        m_pendingSlot = slot;
        RefreshCandidates();
    }

    private void RefreshCandidates()
    {
        m_candidates.Clear();

        MedicalManager manager = CacheMedicalManager();
        NpcCandidateListPanel panel = CacheCandidateListPanel();
        if (manager == null || panel == null)
            return;

        if (manager.CurrentHelperCount < manager.HelperCapacity &&
            TryGetCharacterManager(out CharacterManager characterManager))
        {
            foreach (NPCRuntimeData character in characterManager.Characters)
            {
                if (IsStaffCandidate(character))
                    m_candidates.Add(character);
            }
        }

        panel.Open(this, m_candidates, HandleCandidateClicked);
    }

    // 후보 조건: Light 이상 Healthy 미만 → enum 순서(Healthy<Light<Heavy<...)상 경상(LightInjury)만 해당.
    private bool IsStaffCandidate(NPCRuntimeData character)
    {
        if (character == null)
            return false;

        if (character.GetCurrentInjuryState() != NPCInjuryState.LightInjury)
            return false;

        return !character.GetIsAssignedToShelter();
    }

    private void HandleCandidateClicked(string definitionId)
    {
        MedicalManager manager = CacheMedicalManager();
        if (manager == null)
            return;

        if (manager.TryAssignHelper(definitionId))
        {
            if (m_pendingSlot != null)
                m_pendingSlot.SetHelper(definitionId);
            m_pendingSlot = null;

            HideCandidateList();
        }
        else
        {
            // 배치 실패(정원 초과 등) → 목록을 최신 상태로 유지
            RefreshCandidates();
        }
    }

    // 매니저 쪽에서 해제된 경우(외부 경로)에도 슬롯 표시를 맞춘다.
    private void HandleHelperReleased(NPCRuntimeData helper)
    {
        if (helper != null)
            ClearSlotByHelperId(helper.DefinitionId);
    }

    private void ClearSlotByHelperId(string definitionId)
    {
        if (m_slots == null || string.IsNullOrWhiteSpace(definitionId))
            return;

        for (int i = 0; i < m_slots.Length; i++)
        {
            MedicalStaffSlotView slot = m_slots[i];
            if (slot != null && slot.HasHelper && slot.HelperId == definitionId)
            {
                slot.ClearHelper();
                return;
            }
        }
    }

    private void HideCandidateList()
    {
        m_pendingSlot = null;
        m_candidates.Clear();

        // OnDisable 경로에서도 호출되므로 씬 검색 없이 캐시된 패널만 닫는다.
        if (m_candidateListPanel != null)
            m_candidateListPanel.Close(this);
    }

    // 패널이 닫히거나 다른 시설 UI가 패널을 가져갔을 때 로컬 상태를 정리한다.
    private void HandlePanelClosedBy(object requester)
    {
        if (!ReferenceEquals(requester, this))
            return;

        m_pendingSlot = null;
        m_candidates.Clear();
    }

    private NpcCandidateListPanel CacheCandidateListPanel()
    {
        if (m_candidateListPanel == null)
            m_candidateListPanel = FindFirstObjectByType<NpcCandidateListPanel>(FindObjectsInactive.Include);

        // 패널 참조가 바뀌면 ClosedBy 구독을 옮긴다.
        if (m_candidateListPanel != m_boundPanel)
        {
            if (m_boundPanel != null)
                m_boundPanel.ClosedBy -= HandlePanelClosedBy;

            m_boundPanel = m_candidateListPanel;

            if (m_boundPanel != null)
                m_boundPanel.ClosedBy += HandlePanelClosedBy;
        }

        return m_candidateListPanel;
    }

    private MedicalManager CacheMedicalManager()
    {
        if (m_medicalManager == null)
            m_medicalManager = FindFirstObjectByType<MedicalManager>();

        return m_medicalManager;
    }

    private bool TryGetCharacterManager(out CharacterManager manager)
    {
        if (m_characterManager == null)
            m_characterManager = CharacterManager.Instance;

        if (m_characterManager == null)
            m_characterManager = FindFirstObjectByType<CharacterManager>();

        manager = m_characterManager;
        if (manager != null)
            return true;

        Debug.LogWarning("[MedicalStaffSlotController] CharacterManager is not available.", this);
        return false;
    }

    private void CacheChildViews()
    {
        if (m_slots == null || m_slots.Length == 0)
            m_slots = GetComponentsInChildren<MedicalStaffSlotView>(true);
    }
}
