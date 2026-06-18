using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MedicalUI : MonoBehaviour
{
    [System.Serializable]
    private sealed class MedicalSlotRow
    {
        [SerializeField] private Image[] m_slotImages;

        public int SlotCount => m_slotImages != null ? m_slotImages.Length : 0;

        public void Render(int unlockedSlotCount, int firstSlotIndex, Sprite unlockedSprite, Sprite lockedSprite)
        {
            if (m_slotImages == null)
                return;

            for (int i = 0; i < m_slotImages.Length; i++)
            {
                Image targetImage = m_slotImages[i];
                if (targetImage == null)
                    continue;

                int slotIndex = firstSlotIndex + i;
                bool isUnlocked = slotIndex < unlockedSlotCount;
                Sprite slotSprite = isUnlocked ? unlockedSprite : lockedSprite;
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
    private bool m_isOpen;

    public bool IsOpen => m_isOpen;

    public event Action Closed;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        m_isOpen = m_root != null && m_root.activeSelf;

        if (m_hideOnAwake && !m_isOpening)
            Close();
    }

    private void OnDisable()
    {
        UnbindManager();

        if (!m_isOpening)
            SetOpenState(false);
    }


    private void Update()
    {
        if (Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }


    public void Open(MedicalManager manager)
    {
        UnbindManager();

        m_currentManager = manager;
        if (m_currentManager != null)
            m_currentManager.OnPatientSlotsChanged += Refresh;

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        Refresh();
    }

    public void Close()
    {
        UnbindManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    public void Refresh()
    {
        int unlockedSlotCount = m_currentManager != null
            ? m_currentManager.PatientCapacity
            : 0;

        if (m_slotRows != null && m_slotRows.Length > 0)
        {
            RefreshSlotRows(unlockedSlotCount);
            return;
        }

        RefreshFlatSlots(unlockedSlotCount);
    }

    private void RefreshSlotRows(int unlockedSlotCount)
    {
        int firstSlotIndex = 0;
        for (int i = 0; i < m_slotRows.Length; i++)
        {
            if (m_slotRows[i] == null)
                continue;

            m_slotRows[i].Render(unlockedSlotCount, firstSlotIndex, m_unlockedSlotSprite, m_lockedSlotSprite);
            firstSlotIndex += m_slotRows[i].SlotCount;
        }
    }

    private void RefreshFlatSlots(int unlockedSlotCount)
    {
        if (m_slotImages == null)
            return;

        for (int i = 0; i < m_slotImages.Length; i++)
        {
            Image targetImage = m_slotImages[i];
            if (targetImage == null)
                continue;

            bool isUnlocked = i < unlockedSlotCount;
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

    private void SetOpenState(bool isOpen)
    {
        if (m_isOpen == isOpen)
            return;

        m_isOpen = isOpen;

        if (!m_isOpen)
            Closed?.Invoke();
    }
}
