using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MedicalUI : MonoBehaviour
{
    [System.Serializable]
    private sealed class MedicalSlotRow
    {
        [SerializeField] private Image[] m_slotImages;

        public void Render(MedicalPatientRow row, Sprite unlockedSprite, Sprite lockedSprite)
        {
            if (m_slotImages == null)
                return;

            IReadOnlyList<MedicalPatientSlot> cells = row != null ? row.Cells : null;

            for (int i = 0; i < m_slotImages.Length; i++)
            {
                Image targetImage = m_slotImages[i];
                if (targetImage == null)
                    continue;

                MedicalPatientSlot cell = cells != null && i < cells.Count ? cells[i] : null;
                Sprite slotSprite = cell != null && cell.IsUnlocked ? unlockedSprite : lockedSprite;
                ApplySlotSprite(targetImage, slotSprite);
            }
        }
    }

    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Slot Images")]
    [SerializeField] private Sprite m_unlockedSlotSprite;
    [SerializeField] private Sprite m_lockedSlotSprite;

    [Header("Slot Rows")]
    [SerializeField] private MedicalSlotRow[] m_slotRows;

    [Header("Flat Slot Images")]
    [SerializeField] private Image[] m_slotImages;

    private MedicalManager m_currentManager;
    private bool m_isOpening;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        if (m_hideOnAwake && !m_isOpening)
            Close();
    }

    private void OnDisable()
    {
        UnbindManager();
    }

    public void Open(FacilityInteractionPoint interactionPoint)
    {
        UnbindManager();

        m_currentManager = null;

        if (interactionPoint != null)
            interactionPoint.TryGetFacility(out m_currentManager);

        if (m_currentManager != null)
            m_currentManager.OnPatientSlotsChanged += Refresh;

        m_isOpening = true;
        SetRootActive(true);
        m_isOpening = false;

        Refresh();
    }

    public void Close()
    {
        UnbindManager();
        SetRootActive(false);
    }

    public void Refresh()
    {
        IReadOnlyList<MedicalPatientRow> rows = m_currentManager != null
            ? m_currentManager.PatientRows
            : null;
        IReadOnlyList<MedicalPatientSlot> slots = m_currentManager != null
            ? m_currentManager.PatientSlots
            : null;

        if (m_slotRows != null && m_slotRows.Length > 0)
        {
            RefreshSlotRows(rows);
            return;
        }

        RefreshFlatSlots(slots);
    }

    private void RefreshSlotRows(IReadOnlyList<MedicalPatientRow> rows)
    {
        for (int i = 0; i < m_slotRows.Length; i++)
        {
            if (m_slotRows[i] == null)
                continue;

            MedicalPatientRow row = rows != null && i < rows.Count ? rows[i] : null;
            m_slotRows[i].Render(row, m_unlockedSlotSprite, m_lockedSlotSprite);
        }
    }

    private void RefreshFlatSlots(IReadOnlyList<MedicalPatientSlot> slots)
    {
        if (m_slotImages == null)
            return;

        for (int i = 0; i < m_slotImages.Length; i++)
        {
            Image targetImage = m_slotImages[i];
            if (targetImage == null)
                continue;

            MedicalPatientSlot slot = slots != null && i < slots.Count ? slots[i] : null;
            bool isUnlocked = slot != null && slot.IsUnlocked;
            ApplySlotImage(targetImage, isUnlocked);
        }
    }

    private void ApplySlotImage(Image targetImage, bool isUnlocked)
    {
        Sprite slotSprite = isUnlocked ? m_unlockedSlotSprite : m_lockedSlotSprite;
        ApplySlotSprite(targetImage, slotSprite);
    }

    private static void ApplySlotSprite(Image targetImage, Sprite slotSprite)
    {
        if (targetImage == null || slotSprite == null)
            return;

        targetImage.sprite = slotSprite;
        targetImage.enabled = true;
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
}
