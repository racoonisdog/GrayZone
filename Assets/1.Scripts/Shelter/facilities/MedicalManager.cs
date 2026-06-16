using UnityEngine;
using System.Collections.Generic;

public class MedicalManager : MonoBehaviour, IFacilityService
{
    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Assignment")]
    [SerializeField] private int maxPatients = 2;

    [Header("Recovery")]
    [SerializeField] private int healAmount = 30;
    [SerializeField] private int reviveHpPercent = 30;

    [Header("Cost")]
    [SerializeField] private CurrencyType treatmentCostType = CurrencyType.Medicine;
    [SerializeField] private int treatmentCostAmount = 1;

    private readonly RecoveryComponent recovery = new RecoveryComponent();
    private readonly List<NPCRuntimeData> assignedPatients = new List<NPCRuntimeData>();
    private StaffAssignment patientAssignment;

    public string FacilityId
    {
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;

            return fallbackFacilityId;
        }
    }

    public CostBundle Cost => new CostBundle(
        new CurrencyCost(treatmentCostType, treatmentCostAmount));

    private void Awake()
    {
        patientAssignment = new StaffAssignment(maxPatients);
    }

    public bool TryAssignPatient(NPCRuntimeData target)
    {
        if (target == null)
            return false;

        if (assignedPatients.Contains(target))
            return true;

        if (!patientAssignment.CanAssign(assignedPatients.Count, 1))
            return false;

        if (!target.AssignToShelter(FacilityId, roomId))
            return false;

        assignedPatients.Add(target);
        return true;
    }

    public bool TryReleasePatient(NPCRuntimeData target)
    {
        if (target == null || !assignedPatients.Remove(target))
            return false;

        target.ReleaseFromShelter();
        return true;
    }

    public bool CanExecute(ResourceStorage storage)
    {
        if (storage == null)
            return false;

        foreach (CurrencyCost cost in Cost.Costs)
        {
            if (!storage.CanSpend(cost))
                return false;
        }

        return true;
    }

    public bool TryExecute(ResourceStorage storage)
    {
        if (!CanExecute(storage))
            return false;

        foreach (CurrencyCost cost in Cost.Costs)
            storage.TrySpend(cost);

        return true;
    }

    public bool TryTreat(NPCRuntimeData target, ResourceStorage storage)
    {
        if (target == null || !CanExecute(storage))
            return false;

        bool recovered = target.IsDead
            ? recovery.Revive(target, reviveHpPercent)
            : recovery.Heal(target, healAmount);

        return recovered && TryExecute(storage);
    }
}
