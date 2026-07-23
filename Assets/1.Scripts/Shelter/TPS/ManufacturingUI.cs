using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 제조 시설 UI를 열고 제조 슬롯, 레시피 선택, 헬퍼 UI를 매니저 상태로 투영합니다.
/// </summary>
public sealed class ManufacturingUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Manufacturing Slots")]
    [SerializeField] private ManufacturingSlotView[] m_slots;

    [Header("Recipe Selection")]
    [SerializeField] private CharacterCandidateListPanel m_candidateListPanel;
    [Tooltip("없으면 Default Order Quantity를 사용합니다.")]
    [SerializeField] private TMP_InputField m_quantityInput;
    [Min(1)]
    [SerializeField] private int m_defaultOrderQuantity = 1;
    [SerializeField] private TMP_Text m_noticeText;

    [Header("Staff")]
    [SerializeField] private ManufacturingStaffSlotController m_staffSlotController;

    private ManufacturingManager m_currentManager;
    private CharacterCandidateListPanel m_boundCandidateListPanel;
    private int m_pendingSlotIndex = -1;
    private bool m_isOpening;
    private bool m_isOpen;

    public bool IsOpen => m_isOpen;
    public event Action Closed;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        CacheChildViews();
        m_isOpen = m_root.activeSelf;
        if (m_hideOnAwake && !m_isOpening)
            Close();
    }

    private void Reset()
    {
        m_root = gameObject;
        CacheChildViews();
    }

    private void OnDisable()
    {
        UnbindManager();
        if (!m_isOpening)
            SetOpenState(false);
    }

    private void OnDestroy()
    {
        if (m_boundCandidateListPanel != null)
            m_boundCandidateListPanel.ClosedBy -= HandleCandidateListClosedBy;
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        CharacterCandidateListPanel panel = CacheCandidateListPanel();
        if (panel != null && panel.IsOpenFor(this))
            HideRecipeList();
        else
            Close();
    }

    public void Open(ManufacturingManager manager)
    {
        UnbindManager();
        m_currentManager = manager;
        if (m_currentManager != null)
            m_currentManager.StateChanged += Refresh;

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        CacheChildViews();
        m_staffSlotController?.SetManager(m_currentManager);
        HideRecipeList();
        ClearNotice();
        Refresh();
    }

    public void Close()
    {
        HideRecipeList();
        UnbindManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    public void Refresh()
    {
        CacheChildViews();
        if (m_slots == null)
            return;

        for (int i = 0; i < m_slots.Length; i++)
        {
            ManufacturingSlotView slot = m_slots[i];
            if (slot == null)
                continue;

            ManufacturingJobRuntimeData job = FindJob(i);
            ManufacturingRecipeDefinition recipe = null;
            if (job != null)
                m_currentManager?.TryGetRecipe(job.RecipeId, out recipe);

            slot.Bind(
                i,
                m_currentManager != null && m_currentManager.IsCraftingSlotUnlocked(i),
                job,
                recipe,
                m_currentManager != null ? m_currentManager.FinalProductivity : 0,
                HandleSlotClicked,
                HandleCancelClicked);
        }

        m_staffSlotController?.RefreshSlots();
    }

    private void HandleSlotClicked(int slotIndex)
    {
        if (m_currentManager == null
            || !m_currentManager.IsCraftingSlotUnlocked(slotIndex))
        {
            return;
        }

        m_pendingSlotIndex = slotIndex;
        CharacterCandidateListPanel panel = CacheCandidateListPanel();
        if (panel == null)
        {
            SetNotice("레시피 선택 패널을 찾을 수 없습니다.");
            return;
        }

        panel.OpenRecipes(
            this,
            m_currentManager.Recipes,
            m_currentManager.IsRecipeUnlocked,
            HandleRecipeSelected);
    }

    private void HandleRecipeSelected(string recipeId)
    {
        if (m_currentManager == null || m_pendingSlotIndex < 0)
            return;

        int quantity = ResolveOrderQuantity();
        if (m_currentManager.TryStartJob(m_pendingSlotIndex, recipeId, quantity))
        {
            HideRecipeList();
            ClearNotice();
            Refresh();
            return;
        }

        SetNotice("제작을 시작할 수 없습니다. 재료, 수량, 슬롯 상태를 확인하세요.");
    }

    private void HandleCancelClicked(int slotIndex)
    {
        if (m_currentManager != null && m_currentManager.TryCancelJob(slotIndex))
        {
            ClearNotice();
            Refresh();
        }
        else
        {
            SetNotice("제작 작업을 취소할 수 없습니다.");
        }
    }

    private ManufacturingJobRuntimeData FindJob(int slotIndex)
    {
        if (m_currentManager == null)
            return null;

        var jobs = m_currentManager.Jobs;
        for (int i = 0; i < jobs.Count; i++)
        {
            if (jobs[i] != null && jobs[i].SlotIndex == slotIndex)
                return jobs[i];
        }

        return null;
    }

    private int ResolveOrderQuantity()
    {
        int quantity = Mathf.Max(1, m_defaultOrderQuantity);
        if (m_quantityInput != null
            && int.TryParse(m_quantityInput.text, out int parsedQuantity))
        {
            quantity = parsedQuantity;
        }

        return m_currentManager != null
            ? Mathf.Clamp(quantity, 1, m_currentManager.MaxOrderQuantity)
            : quantity;
    }

    private void HideRecipeList()
    {
        m_pendingSlotIndex = -1;
        if (m_candidateListPanel != null)
            m_candidateListPanel.Close(this);
    }

    private void HandleCandidateListClosedBy(object requester)
    {
        if (ReferenceEquals(requester, this))
            m_pendingSlotIndex = -1;
    }

    private CharacterCandidateListPanel CacheCandidateListPanel()
    {
        if (m_candidateListPanel == null)
        {
            m_candidateListPanel =
                FindFirstObjectByType<CharacterCandidateListPanel>(FindObjectsInactive.Include);
        }

        if (m_candidateListPanel == m_boundCandidateListPanel)
            return m_candidateListPanel;

        if (m_boundCandidateListPanel != null)
            m_boundCandidateListPanel.ClosedBy -= HandleCandidateListClosedBy;

        m_boundCandidateListPanel = m_candidateListPanel;
        if (m_boundCandidateListPanel != null)
            m_boundCandidateListPanel.ClosedBy += HandleCandidateListClosedBy;

        return m_candidateListPanel;
    }

    private void CacheChildViews()
    {
        if (m_slots == null || m_slots.Length == 0)
            m_slots = GetComponentsInChildren<ManufacturingSlotView>(true);

        if (m_staffSlotController == null)
            m_staffSlotController = GetComponentInChildren<ManufacturingStaffSlotController>(true);

        CacheCandidateListPanel();
    }

    private void UnbindManager()
    {
        if (m_currentManager != null)
            m_currentManager.StateChanged -= Refresh;

        m_currentManager = null;
    }

    private void SetNotice(string message)
    {
        if (m_noticeText != null)
            m_noticeText.text = message;
        else
            Debug.LogWarning($"[ManufacturingUI] {message}", this);
    }

    private void ClearNotice()
    {
        if (m_noticeText != null)
            m_noticeText.text = string.Empty;
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
