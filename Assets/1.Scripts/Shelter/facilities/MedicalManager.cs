using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

public class MedicalManager : MonoBehaviour, IFacilityUpgradeable
{
    private const int MaxLevelIndex = 2; // 레벨 3단계 (인덱스 0,1,2)

    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Character Access")]
    [SerializeField] private CharacterManager characterManager;

    [Header("Capacity (레벨별 슬롯)")]
    [SerializeField] private int[] patientSlotsByLevel = { 1, 2, 3 };
    [SerializeField] private int[] helperSlotsByLevel = { 1, 1, 2 };

    [Header("Recovery")]
    [SerializeField] private int baseRecoveryPerDay = 5; // 일일 기본 회복 %
    [FormerlySerializedAs("staffHealBonuses")]
    [SerializeField] private HelperRecoveryBonus[] helperRecoveryBonuses = new HelperRecoveryBonus[]
    {
        new HelperRecoveryBonus { type = NPCType.Tanker, bonusPercent = 1 },
        new HelperRecoveryBonus { type = NPCType.Healer, bonusPercent = 2 },
        new HelperRecoveryBonus { type = NPCType.Dealer, bonusPercent = 1 }
    };

    private readonly List<MedicalTreatment> patientTreatments = new List<MedicalTreatment>();
    private readonly List<PatientStatus> patientStatuses = new List<PatientStatus>();
    private readonly List<NPCRuntimeData> helpers = new List<NPCRuntimeData>();
    private int patientCapacityLevel = 0;

    public event System.Action<NPCRuntimeData> OnHelperAssigned;
    public event System.Action<NPCRuntimeData> OnHelperReleased;
    public event System.Action<NPCRuntimeData> OnPatientHealed;
    /// <summary>
    /// MedicalUI.Refresh() 호출 함수 ( UI 갱신용 )
    /// </summary>
    public event System.Action OnPatientSlotsChanged;

    public IReadOnlyList<PatientStatus> PatientStatuses
    {
        get
        {
            RefreshPatientStatuses();
            return patientStatuses;
        }
    }
    public int CurrentPatientCount => patientTreatments.Count;
    public int MaxPatientCount => PatientCapacity;
    public int PatientCapacity => SlotsAtLevel(patientSlotsByLevel);
    public int MaxPatientCapacity => MaxSlots(patientSlotsByLevel);
    public int UnlockedPatientSlotCount => PatientCapacity;
    public int LockedPatientSlotCount => MaxPatientCapacity - PatientCapacity;
    public int CurrentHelperCount => helpers.Count;
    public int MaxHelperCount => HelperCapacity;
    public int HelperCapacity => SlotsAtLevel(helperSlotsByLevel);
    public int PatientUpgrade => patientCapacityLevel;
    public int UpgradeLevel => patientCapacityLevel;
    public int MaxUpgradeLevel => MaxLevelIndex;

    public string FacilityId
    {
        //fallbackFacilityId 나중에 통일 필요( 방지용 ID임 이건 )
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;
            return fallbackFacilityId;
        }
    }

    private void Awake()
    {
        CacheCharacterManager();
    }

    private void OnValidate()
    {
        patientCapacityLevel = Mathf.Clamp(patientCapacityLevel, 0, MaxLevelIndex);
        baseRecoveryPerDay = Mathf.Max(1, baseRecoveryPerDay);
    }

    private void Start()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced += OnDayAdvanced;
    }

    private void OnDestroy()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced -= OnDayAdvanced;
    }

    public void LoadPatientUpgrade(int saved)
    {
        int previousCapacity = PatientCapacity;
        //레벨복원
        patientCapacityLevel = Mathf.Clamp(saved, 0, GetMaxPatientCapacityLevel());

        //복원했을때 수치가 다르면 갱신
        if (PatientCapacity != previousCapacity)
            NotifyPatientSlotsChanged();
    }

    public void ApplyUpgradeLevel(int level)
    {
        LoadPatientUpgrade(level);
    }

    public void FillPatientCandidates(List<NPCRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (NPCRuntimeData character in manager.Characters)
        {
            if (CanAssignPatient(character))
                results.Add(character);
        }
    }

    public bool CanAssignPatient(NPCRuntimeData character)
    {
        if (character == null)
            return false;

        if (patientTreatments.Count >= PatientCapacity)
            return false;

        if (FindPatientSlotIndex(character) >= 0)
            return false;

        // 완치(Healthy)면 치료 불필요 (enum 기준).
        if (character.GetCurrentInjuryState() == NPCInjuryState.Healthy)
            return false;

        return !character.GetIsAssignedToShelter();
    }

    public bool TryAssignPatient(NPCRuntimeData target)
    {
        return target != null && TryAssignPatient(target.DefinitionId);
    }

    public bool TryAssignPatient(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return false;
        //몇번째 슬롯에 있는지(슬롯에 없는 id면 -1 return)
        if (FindPatientSlotIndex(definitionId) >= 0) return true;

        //목록에 있는 숫자가 최대치 보다 높을경우 오류상태
        if (patientTreatments.Count >= PatientCapacity) return false;

        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        //셸터 데이터에서 NPC를 관리하는 CharacterManager로 부터 데이터를 가져오는 함수
        if (!manager.TryGetCharacter(definitionId, out NPCRuntimeData target))
            return false;

        if (!CanAssignPatient(target))
            return false;

        if (!manager.TryAssignToFacility(
                definitionId,
                FacilityId,
                roomId,
                CharacterAssignmentFilter.AvailableAlive,
                FacilityAssignmentKind.Patient,
                out target,
                out _))
        {
            return false;
        }

        patientTreatments.Add(new MedicalTreatment(target, GetDailyRecovery()));
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleasePatient(NPCRuntimeData target)
    {
        return target != null && TryReleasePatient(target.DefinitionId);
    }

    public bool TryReleasePatient(string definitionId)
    {
        int slotIndex = FindPatientSlotIndex(definitionId);
        if (slotIndex < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        MedicalTreatment treatment = patientTreatments[slotIndex];
        if (!manager.TryReleaseFromFacility(treatment.Patient.DefinitionId, out _))
            return false;

        // 중도 해제 → 현재 게이지 기준으로 부상상태 갱신
        manager.TryRefreshInjuryState(treatment.Patient.DefinitionId, out _);
        patientTreatments.RemoveAt(slotIndex);
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool UpgradePatientCapacity(int amount)
    {
        if (amount <= 0) return false;

        int previousCapacity = PatientCapacity;
        patientCapacityLevel = Mathf.Clamp(patientCapacityLevel + amount, 0, GetMaxPatientCapacityLevel());
        bool upgraded = PatientCapacity > previousCapacity;
        if (upgraded)
            NotifyPatientSlotsChanged();

        return upgraded;
    }

    public int GetPatientHealDaysRemaining(NPCRuntimeData patient)
    {
        int slotIndex = FindPatientSlotIndex(patient);
        return slotIndex >= 0 ? patientTreatments[slotIndex].RemainingDays : 0;
    }

    public void FillHelperCandidates(List<NPCRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (NPCRuntimeData character in manager.Characters)
        {
            if (CanAssignHelper(character))
                results.Add(character);
        }
    }

    public bool CanAssignHelper(NPCRuntimeData character)
    {
        if (character == null)
            return false;

        if (helpers.Count >= HelperCapacity)
            return false;

        if (FindHelperIndex(character) >= 0)
            return false;

        // 도우미는 건강 또는 경상만 가능 (중상·위독 제외).
        NPCInjuryState state = character.GetCurrentInjuryState();
        if (state != NPCInjuryState.Healthy && state != NPCInjuryState.LightInjury)
            return false;

        return !character.GetIsAssignedToShelter();
    }

    public bool TryAssignHelper(NPCRuntimeData target)
    {
        return target != null && TryAssignHelper(target.DefinitionId);
    }

    public bool TryAssignHelper(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return false;
        if (FindHelperIndex(definitionId) >= 0) return true;

        if (helpers.Count >= HelperCapacity) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        if (!manager.TryGetCharacter(definitionId, out NPCRuntimeData target))
            return false;

        if (!CanAssignHelper(target))
            return false;

        if (!manager.TryAssignToFacility(
                definitionId,
                FacilityId,
                roomId,
                CharacterAssignmentFilter.AvailableAlive,
                FacilityAssignmentKind.Staff,
                out target,
                out _))
        {
            return false;
        }

        helpers.Add(target);
        RecalculateAllTreatmentPlans();
        OnHelperAssigned?.Invoke(target);
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleaseHelper(NPCRuntimeData target)
    {
        return target != null && TryReleaseHelper(target.DefinitionId);
    }

    public bool TryReleaseHelper(string definitionId)
    {
        int index = FindHelperIndex(definitionId);
        if (index < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        NPCRuntimeData helper = helpers[index];
        if (!manager.TryReleaseFromFacility(helper.DefinitionId, out _))
            return false;

        helpers.RemoveAt(index);
        RecalculateAllTreatmentPlans();
        OnHelperReleased?.Invoke(helper);
        NotifyPatientSlotsChanged();
        return true;
    }

    private int FindHelperIndex(NPCRuntimeData helper)
    {
        return helper == null ? -1 : FindHelperIndex(helper.DefinitionId);
    }

    private int FindHelperIndex(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return -1;

        string normalizedDefinitionId = definitionId.Trim();
        for (int i = 0; i < helpers.Count; i++)
        {
            NPCRuntimeData helper = helpers[i];
            if (helper != null && helper.DefinitionId == normalizedDefinitionId)
                return i;
        }

        return -1;
    }

    private void OnDayAdvanced(int prev, int next)
    {
        if (patientTreatments.Count == 0)
            return;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        // 다중일 스킵은 현재 스코프 밖 — 하루 단위로만 진행한다.
        bool changed = false;
        for (int i = patientTreatments.Count - 1; i >= 0; i--)
        {
            MedicalTreatment treatment = patientTreatments[i];
            NPCRuntimeData patient = treatment.Patient;

            float amount = treatment.ConsumeDailyRecovery();
            manager.TrySetInjuryGauge(patient.DefinitionId, patient.InjuryGauge + amount, out _);
            changed = true;

            // 완치 판정은 게이지 기준(진실원천). 아이템 등 치료 외 경로로 게이지가 차도 즉시 완치된다.
            if (patient.InjuryGauge >= patient.MaxInjuryGauge)
                CompleteHealing(i);
        }

        if (changed)
            NotifyPatientSlotsChanged();
    }

    private void CompleteHealing(int slotIndex)
    {
        MedicalTreatment treatment = patientTreatments[slotIndex];
        NPCRuntimeData patient = treatment.Patient;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        if (!manager.TryCompleteRecovery(patient.DefinitionId, out _))
            return;

        patientTreatments.RemoveAt(slotIndex);
        OnPatientHealed?.Invoke(patient);
    }

    // 일일 회복량 = 기본 + 배치된 헬퍼들의 타입별 보너스 합 (게이지 %/일).
    private float GetDailyRecovery()
    {
        int bonus = 0;
        foreach (NPCRuntimeData helper in helpers)
            bonus += GetHelperBonus(helper.Type);
        return Mathf.Max(1, baseRecoveryPerDay + bonus);
    }

    private int GetHelperBonus(NPCType type)
    {
        foreach (var b in helperRecoveryBonuses)
            if (b.type == type) return b.bonusPercent;
        return 0;
    }

    // 헬퍼 배치/해제로 회복률이 바뀌면 활성 환자 계획을 현재 게이지 기준으로 재산출한다.
    private void RecalculateAllTreatmentPlans()
    {
        float daily = GetDailyRecovery();
        for (int i = 0; i < patientTreatments.Count; i++)
            patientTreatments[i].Recalculate(daily);
    }

    private int FindPatientSlotIndex(NPCRuntimeData patient)
    {
        if (patient == null)
            return -1;

        return FindPatientSlotIndex(patient.DefinitionId);
    }

    private int FindPatientSlotIndex(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return -1;

        string normalizedDefinitionId = definitionId.Trim();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            if (treatment.Patient != null && treatment.Patient.DefinitionId == normalizedDefinitionId)
                return i;
        }

        return -1;
    }

    private bool TryGetCharacterManager(out CharacterManager manager)
    {
        manager = CacheCharacterManager();
        if (manager != null)
            return true;

        Debug.LogWarning("[MedicalManager] CharacterManager is not available.", this);
        return false;
    }

    private CharacterManager CacheCharacterManager()
    {
        if (characterManager == null)
            characterManager = CharacterManager.Instance;

        if (characterManager == null)
            characterManager = FindFirstObjectByType<CharacterManager>();

        return characterManager;
    }

    private int SlotsAtLevel(int[] table)
    {
        if (table == null || table.Length == 0)
            return 0;

        int index = Mathf.Clamp(patientCapacityLevel, 0, table.Length - 1);
        return Mathf.Max(0, table[index]);
    }

    private int MaxSlots(int[] table)
    {
        return (table == null || table.Length == 0) ? 0 : Mathf.Max(0, table[table.Length - 1]);
    }

    private int GetMaxPatientCapacityLevel()
    {
        return MaxLevelIndex;
    }

    private void NotifyPatientSlotsChanged()
    {
        RefreshPatientStatuses();
        OnPatientSlotsChanged?.Invoke();
    }

    /// <summary>
    /// 외부 노출용 투영본인 patientStatuses 삭제 및 재생성( 원본에서 매번 재생성 함 )
    /// </summary>
    private void RefreshPatientStatuses()
    {
        patientStatuses.Clear();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            patientStatuses.Add(new PatientStatus(treatment.Patient, treatment.RemainingDays, treatment.TotalDays));
        }
    }

    private sealed class MedicalTreatment
    {
        public NPCRuntimeData Patient { get; }
        public int RemainingDays { get; private set; }
        public int TotalDays { get; private set; }

        private readonly float maxGauge;
        private float dailyRecovery;
        private float nextRecoveryAmount;

        public MedicalTreatment(NPCRuntimeData patient, float dailyRecovery)
        {
            Patient = patient;
            maxGauge = patient.MaxInjuryGauge;
            Recalculate(dailyRecovery);
        }

        // 현재 게이지 기준으로 치료 계획을 (재)산출한다. 계산은 TreatmentDurationCalculator에 위임한다.
        public void Recalculate(float daily)
        {
            TreatmentPlan plan = TreatmentDurationCalculator.Calculate(Patient.InjuryGauge, maxGauge, daily);
            dailyRecovery = plan.DailyRecovery;
            TotalDays = plan.TotalDays;
            RemainingDays = plan.TotalDays;
            nextRecoveryAmount = plan.FirstTickRecovery;
        }

        // 이번 날 회복량을 반환하고 남은 일수/다음 회복량을 진행시킨다.
        public float ConsumeDailyRecovery()
        {
            float amount = nextRecoveryAmount;
            nextRecoveryAmount = dailyRecovery;
            RemainingDays = Mathf.Max(0, RemainingDays - 1);
            return amount;
        }
    }
}


public readonly struct PatientStatus
{
    public PatientStatus(NPCRuntimeData patient, int remainingDays, int totalDays)
    {
        Patient = patient;
        RemainingDays = remainingDays;
        TotalDays = totalDays;
    }

    public NPCRuntimeData Patient { get; }
    public int RemainingDays { get; }
    public int TotalDays { get; }

    // UI 게이지/상태 표시용 (게이지가 진실원천).
    public float InjuryGauge => Patient != null ? Patient.InjuryGauge : 0f;
    public float MaxInjuryGauge => Patient != null ? Patient.MaxInjuryGauge : 1f;
    public float GaugeNormalized => MaxInjuryGauge > 0f ? Mathf.Clamp01(InjuryGauge / MaxInjuryGauge) : 0f;
    public NPCInjuryState InjuryState => Patient != null ? Patient.GetCurrentInjuryState() : NPCInjuryState.Healthy;
    public string DisplayName => Patient != null
        ? (Patient.NPCData != null ? Patient.NPCData.name : Patient.DefinitionId)
        : string.Empty;
}


// 헬퍼 타입별 일일 회복 보너스(%)
[System.Serializable]
public struct HelperRecoveryBonus
{
    public NPCType type;
    [FormerlySerializedAs("daysReduction")]
    public int bonusPercent;
}
