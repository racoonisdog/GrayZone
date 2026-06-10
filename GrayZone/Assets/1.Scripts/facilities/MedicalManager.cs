using UnityEngine;
using System.Collections.Generic;

public class MedicalManager : MonoBehaviour, IFacilityService
{
    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Patient")]
    [SerializeField] private int baseMaxPatients = 1;

    [Header("Staff")]
    [SerializeField] private int maxStaff = 1;

    [Header("Recovery")]
    [SerializeField] private int healAmount = 30;
    [SerializeField] private int healDays = 5;
    [SerializeField] private int reviveHpPercent = 30;
    [SerializeField] private StaffHealBonus[] staffHealBonuses = new StaffHealBonus[]
    {
        new StaffHealBonus { type = NPCType.Tanker,   daysReduction = 1 },
        new StaffHealBonus { type = NPCType.Detector, daysReduction = 1 },
        new StaffHealBonus { type = NPCType.Healer,   daysReduction = 2 },
        new StaffHealBonus { type = NPCType.Dealer,   daysReduction = 1 }
    };

    [Header("Cost")]
    [SerializeField] private CurrencyType treatmentCostType = CurrencyType.Medicine;
    [SerializeField] private int treatmentCostAmount = 1;

    private readonly RecoveryComponent recovery = new RecoveryComponent();
    private readonly List<NPCRuntimeData> assignedPatients = new List<NPCRuntimeData>();
    private readonly List<NPCRuntimeData> assignedStaff = new List<NPCRuntimeData>();
    private readonly Dictionary<NPCRuntimeData, int> patientHealDays = new Dictionary<NPCRuntimeData, int>();
    private StaffAssignment staffSlots;
    private int patientUpgrade = 0;

    public event System.Action<NPCRuntimeData> OnStaffAssigned;
    public event System.Action<NPCRuntimeData> OnStaffReleased;
    public event System.Action<NPCRuntimeData> OnPatientHealed;

    // UI용
    public int CurrentPatientCount => assignedPatients.Count;
    public int MaxPatientCount => baseMaxPatients + patientUpgrade;
    public int CurrentStaffCount => assignedStaff.Count;
    public int MaxStaffCount => staffSlots.MaxPeople;

    // 업그레이드 저장/로드
    public int PatientUpgrade => patientUpgrade;
    public void LoadPatientUpgrade(int saved) => patientUpgrade = Mathf.Max(0, saved);

    public string FacilityId
    {
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;
            return fallbackFacilityId;
        }
    }

    public CostBundle Cost => new CostBundle(new CurrencyCost(treatmentCostType, treatmentCostAmount));

    private void Awake()
    {
        staffSlots = new StaffAssignment(maxStaff);
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

    // --- 환자 ---

    public bool TryAssignPatient(NPCRuntimeData target)
    {
        if (target == null) return false;
        if (assignedPatients.Contains(target)) return true;
        if (assignedPatients.Count >= MaxPatientCount) return false;
        if (!target.AssignToShelter(FacilityId, roomId)) return false;

        assignedPatients.Add(target);
        patientHealDays[target] = GetEffectiveHealDays();
        return true;
    }

    public bool TryReleasePatient(NPCRuntimeData target)
    {
        if (target == null || !assignedPatients.Remove(target)) return false;
        patientHealDays.Remove(target);
        target.ReleaseFromShelter();
        return true;
    }

    public bool UpgradePatientCapacity(int amount)
    {
        if (amount <= 0) return false;
        patientUpgrade += amount;
        return true;
    }

    public int GetPatientHealDaysRemaining(NPCRuntimeData patient)
    {
        return patientHealDays.TryGetValue(patient, out int days) ? days : 0;
    }

    // --- 스태프 ---

    public bool TryAssignStaff(NPCRuntimeData staff)
    {
        if (staff == null) return false;
        if (assignedStaff.Contains(staff)) return true;
        if (!staffSlots.CanAssign(assignedStaff.Count, 1)) return false;

        assignedStaff.Add(staff);
        ApplyHealDayDelta(staff.NPCData.Type, subtract: true);
        OnStaffAssigned?.Invoke(staff);
        return true;
    }

    public bool TryReleaseStaff(NPCRuntimeData staff)
    {
        if (staff == null || !assignedStaff.Remove(staff)) return false;
        ApplyHealDayDelta(staff.NPCData.Type, subtract: false);
        OnStaffReleased?.Invoke(staff);
        return true;
    }

    // --- 날짜 기반 치료 ---

    private void OnDayAdvanced(int prev, int next)
    {
        var keys = new List<NPCRuntimeData>(patientHealDays.Keys);
        foreach (var patient in keys)
        {
            patientHealDays[patient]--;
            if (patientHealDays[patient] <= 0)
                CompleteHealing(patient);
        }
    }

    private void CompleteHealing(NPCRuntimeData patient)
    {
        patient.SetCurrentHp(patient.MaxHp);
        patientHealDays.Remove(patient);
        assignedPatients.Remove(patient);
        patient.ReleaseFromShelter();
        OnPatientHealed?.Invoke(patient);
    }

    // --- 치료 (즉시) ---

    public bool TryTreat(NPCRuntimeData target, ResourceStorage storage)
    {
        if (target == null || !CanExecute(storage)) return false;

        bool recovered = target.IsDead
            ? recovery.Revive(target, reviveHpPercent)
            : recovery.Heal(target, healAmount);

        return recovered && TryExecute(storage);
    }

    // --- IFacilityService ---

    public bool CanExecute(ResourceStorage storage)
    {
        if (storage == null) return false;
        foreach (CurrencyCost cost in Cost.Costs)
        {
            if (!storage.CanSpend(cost)) return false;
        }
        return true;
    }

    public bool TryExecute(ResourceStorage storage)
    {
        if (!CanExecute(storage)) return false;
        foreach (CurrencyCost cost in Cost.Costs)
            storage.TrySpend(cost);
        return true;
    }

    // --- helpers ---

    private int GetEffectiveHealDays()
    {
        int reduction = 0;
        foreach (var staff in assignedStaff)
            reduction += GetHealBonus(staff.NPCData.Type);
        return Mathf.Max(1, healDays - reduction);
    }

    private int GetHealBonus(NPCType type)
    {
        foreach (var bonus in staffHealBonuses)
            if (bonus.type == type) return bonus.daysReduction;
        return 0;
    }

    private void ApplyHealDayDelta(NPCType type, bool subtract)
    {
        int bonus = GetHealBonus(type);
        if (bonus <= 0) return;

        var keys = new List<NPCRuntimeData>(patientHealDays.Keys);
        foreach (var patient in keys)
        {
            int days = patientHealDays[patient];
            patientHealDays[patient] = subtract ? Mathf.Max(1, days - bonus) : days + bonus;
        }
    }
}

[System.Serializable]
public struct StaffHealBonus
{
    public NPCType type;
    public int daysReduction;
}
