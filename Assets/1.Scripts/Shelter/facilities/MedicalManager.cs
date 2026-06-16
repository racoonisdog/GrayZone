using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;

public class MedicalManager : MonoBehaviour
{
    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Patient Rows")]
    [FormerlySerializedAs("maxPatientSlots")]
    [SerializeField] private int maxPatientRows = 3;
    [SerializeField] private int cellsPerPatientRow = 3;
    [FormerlySerializedAs("startingUnlockedPatientSlots")]
    [SerializeField] private int startingUnlockedPatientRows = 1;
    [SerializeField] private int unlockedCellsPerUnlockedRow = 1;

    [Header("Staff")]
    [SerializeField] private int maxStaff = 1;

    [Header("Recovery")]
    [SerializeField] private int healDays = 5;
    [SerializeField] private StaffHealBonus[] staffHealBonuses = new StaffHealBonus[]
    {
        new StaffHealBonus { type = NPCType.Tanker,   daysReduction = 1 },
        new StaffHealBonus { type = NPCType.Detector, daysReduction = 1 },
        new StaffHealBonus { type = NPCType.Healer,   daysReduction = 2 },
        new StaffHealBonus { type = NPCType.Dealer,   daysReduction = 1 }
    };

    private readonly List<MedicalPatientRow> patientRows = new List<MedicalPatientRow>();
    private readonly List<MedicalPatientSlot> patientSlots = new List<MedicalPatientSlot>();
    private readonly List<NPCRuntimeData> assignedStaff = new List<NPCRuntimeData>();
    private IRecoveryComponent recovery = new RecoveryComponent();
    private StaffAssignment staffSlots;
    private int patientUpgrade = 0;

    public event System.Action<NPCRuntimeData> OnStaffAssigned;
    public event System.Action<NPCRuntimeData> OnStaffReleased;
    public event System.Action<NPCRuntimeData> OnPatientHealed;
    public event System.Action OnPatientSlotsChanged;

    public IReadOnlyList<MedicalPatientRow> PatientRows => patientRows;
    public IReadOnlyList<MedicalPatientSlot> PatientSlots => patientSlots;
    public int CurrentPatientCount => GetCurrentPatientCount();
    public int MaxPatientCount => UnlockedPatientSlotCount;
    public int PatientRowCount => patientRows.Count;
    public int PatientSlotCount => patientSlots.Count;
    public int UnlockedPatientRowCount => GetUnlockedPatientRowCount();
    public int LockedPatientRowCount => PatientRowCount - UnlockedPatientRowCount;
    public int UnlockedPatientSlotCount => GetUnlockedPatientSlotCount();
    public int LockedPatientSlotCount => PatientSlotCount - UnlockedPatientSlotCount;
    public int CurrentStaffCount => assignedStaff.Count;
    public int MaxStaffCount => staffSlots != null ? staffSlots.MaxPeople : maxStaff;
    public int PatientUpgrade => patientUpgrade;

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
        InitializePatientSlots();
        staffSlots = new StaffAssignment(maxStaff);
    }

    private void OnValidate()
    {
        maxPatientRows = Mathf.Max(1, maxPatientRows);
        cellsPerPatientRow = Mathf.Max(1, cellsPerPatientRow);
        startingUnlockedPatientRows = Mathf.Clamp(startingUnlockedPatientRows, 0, maxPatientRows);
        unlockedCellsPerUnlockedRow = Mathf.Clamp(unlockedCellsPerUnlockedRow, 1, cellsPerPatientRow);
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
        int previousUnlockedSlots = UnlockedPatientSlotCount;
        patientUpgrade = Mathf.Clamp(saved, 0, GetMaxPatientUpgrade());
        ApplyPatientSlotUnlocks();

        if (patientSlots.Count > 0 && UnlockedPatientSlotCount != previousUnlockedSlots)
            NotifyPatientSlotsChanged();
    }

    public bool TryAssignPatient(NPCRuntimeData target)
    {
        if (target == null) return false;
        if (FindPatientSlot(target) != null) return true;

        MedicalPatientSlot slot = FindAvailablePatientSlot();
        if (slot == null) return false;
        if (!target.AssignToShelter(FacilityId, roomId)) return false;

        if (!slot.TryAssign(target, GetEffectiveHealDays()))
        {
            target.ReleaseFromShelter();
            return false;
        }

        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleasePatient(NPCRuntimeData target)
    {
        MedicalPatientSlot slot = FindPatientSlot(target);
        if (slot == null) return false;

        NPCRuntimeData releasedPatient = slot.Release();
        releasedPatient.ReleaseFromShelter();
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool UpgradePatientCapacity(int amount)
    {
        if (amount <= 0) return false;

        int previousUnlockedSlots = UnlockedPatientSlotCount;
        patientUpgrade = Mathf.Clamp(patientUpgrade + amount, 0, GetMaxPatientUpgrade());
        ApplyPatientSlotUnlocks();
        bool upgraded = UnlockedPatientSlotCount > previousUnlockedSlots;
        if (upgraded)
            NotifyPatientSlotsChanged();

        return upgraded;
    }

    public int GetPatientHealDaysRemaining(NPCRuntimeData patient)
    {
        MedicalPatientSlot slot = FindPatientSlot(patient);
        return slot != null ? slot.RemainingDays : 0;
    }

    public bool TryAssignStaff(NPCRuntimeData staff)
    {
        if (staff == null) return false;
        if (assignedStaff.Contains(staff)) return true;
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
        for (int i = 0; i < patientSlots.Count; i++)
        {
            MedicalPatientSlot slot = patientSlots[i];
            if (!slot.IsOccupied)
                continue;

            slot.ReduceRemainingDays(elapsedDays);
            changed = true;

            if (slot.IsOccupied && slot.RemainingDays <= 0)
                CompleteHealing(slot);
        }

        if (changed)
            NotifyPatientSlotsChanged();
    }

    private void CompleteHealing(MedicalPatientSlot slot)
    {
        NPCRuntimeData patient = slot.Release();
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
        for (int i = 0; i < patientSlots.Count; i++)
        {
            patientSlots[i].AdjustRemainingDays(delta);
        }
    }

    private void InitializePatientSlots()
    {
        patientRows.Clear();
        patientSlots.Clear();

        int unlockedRows = GetTargetUnlockedPatientRowCount();
        for (int rowIndex = 0; rowIndex < maxPatientRows; rowIndex++)
        {
            int unlockedCells = rowIndex < unlockedRows ? unlockedCellsPerUnlockedRow : 0;
            int firstSlotIndex = patientSlots.Count;
            MedicalPatientRow row = new MedicalPatientRow(rowIndex, cellsPerPatientRow, unlockedCells, firstSlotIndex);

            patientRows.Add(row);
            for (int cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                patientSlots.Add(row.Cells[cellIndex]);
            }
        }
    }

    private void ApplyPatientSlotUnlocks()
    {
        if (patientRows.Count == 0)
            return;

        int unlockedRows = GetTargetUnlockedPatientRowCount();
        for (int i = 0; i < patientRows.Count; i++)
        {
            int unlockedCells = i < unlockedRows ? unlockedCellsPerUnlockedRow : 0;
            patientRows[i].SetUnlockedCellCount(unlockedCells);
        }
    }

    private MedicalPatientSlot FindAvailablePatientSlot()
    {
        for (int i = 0; i < patientRows.Count; i++)
        {
            MedicalPatientSlot slot = patientRows[i].FindAvailableSlot();
            if (slot != null)
                return slot;
        }

        return null;
    }

    private MedicalPatientSlot FindPatientSlot(NPCRuntimeData patient)
    {
        if (patient == null)
            return null;

        for (int i = 0; i < patientSlots.Count; i++)
        {
            if (patientSlots[i].Patient == patient)
                return patientSlots[i];
        }

        return null;
    }

    private int GetCurrentPatientCount()
    {
        int count = 0;
        for (int i = 0; i < patientSlots.Count; i++)
        {
            if (patientSlots[i].IsOccupied)
                count++;
        }

        return count;
    }

    private int GetUnlockedPatientSlotCount()
    {
        int count = 0;
        for (int i = 0; i < patientSlots.Count; i++)
        {
            if (patientSlots[i].IsUnlocked)
                count++;
        }

        return count;
    }

    private int GetUnlockedPatientRowCount()
    {
        int count = 0;
        for (int i = 0; i < patientRows.Count; i++)
        {
            if (patientRows[i].IsUnlocked)
                count++;
        }

        return count;
    }

    private int GetTargetUnlockedPatientRowCount()
    {
        return Mathf.Clamp(startingUnlockedPatientRows + patientUpgrade, 0, maxPatientRows);
    }

    private int GetMaxPatientUpgrade()
    {
        return Mathf.Max(0, maxPatientRows - startingUnlockedPatientRows);
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
