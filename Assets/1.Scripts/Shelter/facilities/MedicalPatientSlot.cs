using UnityEngine;

public sealed class MedicalPatientSlot
{
    public int SlotIndex { get; }
    public int RowIndex { get; }
    public int CellIndex { get; }
    public bool IsUnlocked { get; private set; }
    public NPCRuntimeData Patient { get; private set; }
    public int RemainingDays { get; private set; }
    public bool IsOccupied => Patient != null;

    public MedicalPatientSlot(int slotIndex, bool isUnlocked)
        : this(slotIndex, 0, slotIndex, isUnlocked)
    {
    }

    public MedicalPatientSlot(int slotIndex, int rowIndex, int cellIndex, bool isUnlocked)
    {
        SlotIndex = slotIndex;
        RowIndex = rowIndex;
        CellIndex = cellIndex;
        IsUnlocked = isUnlocked;
    }

    internal void SetUnlocked(bool isUnlocked)
    {
        IsUnlocked = isUnlocked;
    }

    internal bool TryAssign(NPCRuntimeData patient, int remainingDays)
    {
        if (!IsUnlocked || IsOccupied || patient == null)
            return false;

        Patient = patient;
        RemainingDays = Mathf.Max(1, remainingDays);
        return true;
    }

    internal NPCRuntimeData Release()
    {
        NPCRuntimeData releasedPatient = Patient;
        Patient = null;
        RemainingDays = 0;
        return releasedPatient;
    }

    internal void ReduceRemainingDays(int days)
    {
        if (!IsOccupied)
            return;

        RemainingDays = Mathf.Max(0, RemainingDays - Mathf.Max(1, days));
    }

    internal void AdjustRemainingDays(int delta)
    {
        if (!IsOccupied)
            return;

        RemainingDays = Mathf.Max(1, RemainingDays + delta);
    }
}
