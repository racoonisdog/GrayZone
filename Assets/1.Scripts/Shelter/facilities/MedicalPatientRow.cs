using System.Collections.Generic;

public sealed class MedicalPatientRow
{
    private readonly List<MedicalPatientSlot> cells = new List<MedicalPatientSlot>();

    public int RowIndex { get; }
    public IReadOnlyList<MedicalPatientSlot> Cells => cells;
    public int CellCount => cells.Count;
    public int UnlockedCellCount => GetUnlockedCellCount();
    public bool IsUnlocked => UnlockedCellCount > 0;

    public MedicalPatientRow(int rowIndex, int cellCount, int unlockedCellCount, int firstSlotIndex)
    {
        RowIndex = rowIndex;

        for (int i = 0; i < cellCount; i++)
        {
            bool isUnlocked = i < unlockedCellCount;
            cells.Add(new MedicalPatientSlot(firstSlotIndex + i, rowIndex, i, isUnlocked));
        }
    }

    internal void SetUnlockedCellCount(int unlockedCellCount)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            cells[i].SetUnlocked(i < unlockedCellCount);
        }
    }

    internal MedicalPatientSlot FindAvailableSlot()
    {
        for (int i = 0; i < cells.Count; i++)
        {
            MedicalPatientSlot cell = cells[i];
            if (cell.IsUnlocked && !cell.IsOccupied)
                return cell;
        }

        return null;
    }

    private int GetUnlockedCellCount()
    {
        int count = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].IsUnlocked)
                count++;
        }

        return count;
    }
}
