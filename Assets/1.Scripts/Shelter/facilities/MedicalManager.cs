using UnityEngine;
using System.Collections.Generic;

public class MedicalManager : MonoBehaviour, IFacilityUpgradeable
{
    private const int BasePatientCapacity = 2;
    private const int FirstUpgradePatientCapacity = 4;
    private const int FullPatientCapacity = 9;
    private const int MaxPatientCapacityLevel = 2;

    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Staff")]
    [SerializeField] private int maxStaff = 1;

    [Header("Recovery")]
    //ToDo : 플레이어의 부상 상태에 따른 회복 시간 로직 설정하기
    [SerializeField] private int healDays = 5;
    [SerializeField] private StaffHealBonus[] staffHealBonuses = new StaffHealBonus[]
    {
        new StaffHealBonus { type = NPCType.Tanker,   daysReduction = 1 },
        new StaffHealBonus { type = NPCType.Healer,   daysReduction = 2 },
        new StaffHealBonus { type = NPCType.Dealer,   daysReduction = 1 }
    };

    private readonly List<MedicalTreatment> patientTreatments = new List<MedicalTreatment>(FullPatientCapacity);
    private readonly List<NPCRuntimeData> assignedStaff = new List<NPCRuntimeData>();
    private IRecoveryComponent recovery = new RecoveryComponent();
    private StaffAssignment staffSlots;
    private int patientCapacityLevel = 0;

    public event System.Action<NPCRuntimeData> OnStaffAssigned;
    public event System.Action<NPCRuntimeData> OnStaffReleased;
    public event System.Action<NPCRuntimeData> OnPatientHealed;
    public event System.Action OnPatientSlotsChanged;

    public IReadOnlyList<MedicalTreatment> PatientTreatments => patientTreatments;
    public int CurrentPatientCount => patientTreatments.Count;
    public int MaxPatientCount => PatientCapacity;
    public int PatientCapacity => GetPatientCapacity();
    public int MaxPatientCapacity => FullPatientCapacity;
    public int UnlockedPatientSlotCount => PatientCapacity;
    public int LockedPatientSlotCount => FullPatientCapacity - PatientCapacity;
    public int CurrentStaffCount => assignedStaff.Count;
    public int MaxStaffCount => staffSlots != null ? staffSlots.MaxPeople : maxStaff;
    public int PatientUpgrade => patientCapacityLevel;
    public int UpgradeLevel => patientCapacityLevel;
    public int MaxUpgradeLevel => GetMaxPatientCapacityLevel();

    public string FacilityId
    {
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;
            return fallbackFacilityId;
        }
    }

    private void Awake()
    {
        staffSlots = new StaffAssignment(maxStaff);
    }

    private void OnValidate()
    {
        patientCapacityLevel = Mathf.Clamp(patientCapacityLevel, 0, GetMaxPatientCapacityLevel());
        maxStaff = Mathf.Max(0, maxStaff);
        healDays = Mathf.Max(1, healDays);
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
        patientCapacityLevel = Mathf.Clamp(saved, 0, GetMaxPatientCapacityLevel());

        if (PatientCapacity != previousCapacity)
            NotifyPatientSlotsChanged();
    }

    public void ApplyUpgradeLevel(int level)
    {
        LoadPatientUpgrade(level);
    }

    public bool TryAssignPatient(NPCRuntimeData target)
    {
        if (target == null) return false;
        if (FindPatientSlotIndex(target) >= 0) return true;

        if (patientTreatments.Count >= PatientCapacity) return false;
        if (!target.AssignToShelter(FacilityId, roomId)) return false;

        patientTreatments.Add(new MedicalTreatment(target, GetEffectiveHealDays()));
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleasePatient(NPCRuntimeData target)
    {
        int slotIndex = FindPatientSlotIndex(target);
        if (slotIndex < 0) return false;

        MedicalTreatment treatment = patientTreatments[slotIndex];
        patientTreatments.RemoveAt(slotIndex);
        treatment.Patient.ReleaseFromShelter();
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

    public bool TryAssignStaff(NPCRuntimeData staff)
    {
        if (staff == null) return false;
        if (assignedStaff.Contains(staff)) return true;
        if (staffSlots == null) staffSlots = new StaffAssignment(maxStaff);
        if (!staffSlots.CanAssign(assignedStaff.Count, 1)) return false;

        assignedStaff.Add(staff);
        ApplyHealDayDelta(staff.NPCData.Type, subtract: true);
        OnStaffAssigned?.Invoke(staff);
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleaseStaff(NPCRuntimeData staff)
    {
        if (staff == null || !assignedStaff.Remove(staff)) return false;
        ApplyHealDayDelta(staff.NPCData.Type, subtract: false);
        OnStaffReleased?.Invoke(staff);
        NotifyPatientSlotsChanged();
        return true;
    }

    public void SetRecoveryComponent(IRecoveryComponent recoveryComponent)
    {
        recovery = recoveryComponent ?? new RecoveryComponent();
    }

    private void OnDayAdvanced(int prev, int next)
    {
        int elapsedDays = Mathf.Max(1, next - prev);
        bool changed = false;
        for (int i = patientTreatments.Count - 1; i >= 0; i--)
        {
            MedicalTreatment treatment = patientTreatments[i];
            treatment.ReduceRemainingDays(elapsedDays);
            changed = true;

            if (treatment.RemainingDays <= 0)
                CompleteHealing(i);
        }

        if (changed)
            NotifyPatientSlotsChanged();
    }

    private void CompleteHealing(int slotIndex)
    {
        MedicalTreatment treatment = patientTreatments[slotIndex];
        patientTreatments.RemoveAt(slotIndex);
        NPCRuntimeData patient = treatment.Patient;
        recovery.CompleteShelterRecovery(patient);
        patient.ReleaseFromShelter();
        OnPatientHealed?.Invoke(patient);
    }

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

        int delta = subtract ? -bonus : bonus;
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            patientTreatments[i].AdjustRemainingDays(delta);
        }
    }

    private int FindPatientSlotIndex(NPCRuntimeData patient)
    {
        if (patient == null)
            return -1;

        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            if (treatment.Patient == patient)
                return i;
        }

        return -1;
    }

    private int GetPatientCapacity()
    {
        switch (patientCapacityLevel)
        {
            case 0:
                return BasePatientCapacity;
            case 1:
                return FirstUpgradePatientCapacity;
            default:
                return FullPatientCapacity;
        }
    }

    private int GetMaxPatientCapacityLevel()
    {
        return MaxPatientCapacityLevel;
    }

    private void NotifyPatientSlotsChanged()
    {
        OnPatientSlotsChanged?.Invoke();
    }

}


[System.Serializable]
public struct StaffHealBonus
{
    public NPCType type;
    public int daysReduction;
}

public sealed class MedicalTreatment
{
    public NPCRuntimeData Patient { get; }
    public int RemainingDays { get; private set; }

    public MedicalTreatment(NPCRuntimeData patient, int remainingDays)
    {
        Patient = patient;
        RemainingDays = Mathf.Max(1, remainingDays);
    }

    public void ReduceRemainingDays(int days)
    {
        RemainingDays = Mathf.Max(0, RemainingDays - Mathf.Max(1, days));
    }

    public void AdjustRemainingDays(int delta)
    {
        RemainingDays = Mathf.Max(1, RemainingDays + delta);
    }
}
