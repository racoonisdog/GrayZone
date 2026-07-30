using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제조 작업 확정 전 선택된 슬롯, 레시피, 요청 배치 수와 재료 견적 표시를 관리합니다.
/// 실제 자원 차감과 작업 생성은 이 View가 아니라 요청을 받은 <see cref="ManufacturingManager"/>가 담당합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ManufacturingCreateView : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private ManufacturingManager m_manager;
    [SerializeField] private ShelterSceneDataManager m_shelterDataManager;

    [Header("Lists")]
    [SerializeField] private ManufacturingRecipeListView m_recipeListView;
    [SerializeField] private ManufacturingMaterialListView m_materialListView;

    [Header("Result")]
    [SerializeField] private Image m_resultImage;
    [SerializeField] private TMP_Text m_totalResultQuantityText;

    [Header("Requested Batch Count")]
    [SerializeField] private TMP_Text m_requestedBatchCountText;
    [SerializeField] private Button m_decreaseTenButton;
    [SerializeField] private Button m_decreaseOneButton;
    [SerializeField] private Button m_increaseOneButton;
    [SerializeField] private Button m_increaseTenButton;

    [Header("Actions")]
    [SerializeField] private Button m_confirmButton;
    [SerializeField] private Button m_cancelButton;

    [Header("Initial State")]
    [SerializeField] private bool m_startClosed = true;

    private readonly List<ManufacturingMaterialQuoteLine> m_quoteLines = new();

    private int m_pendingSlotIndex = -1;
    private int m_requestedBatchCount = 1;
    private string m_selectedRecipeId = string.Empty;
    private bool m_canConfirm;
    private bool m_isSubscribed;
    private bool m_hasStarted;
    private bool m_openRequestedBeforeStart;
    private Sprite m_defaultResultSprite;

    public bool IsOpen => gameObject.activeSelf;
    public int PendingSlotIndex => m_pendingSlotIndex;
    public int RequestedBatchCount => m_requestedBatchCount;
    public string SelectedRecipeId => m_selectedRecipeId;
    public bool CanConfirm => m_canConfirm;

    /// <summary>
    /// 확정 버튼을 눌렀을 때 실제 제조 시작 권한을 가진 상위 UI에 요청합니다.
    /// </summary>
    public event Action<ManufacturingCreateRequest> ConfirmRequested;

    /// <summary>취소 또는 상위 흐름에 의해 CreateView가 닫혔을 때 발생합니다.</summary>
    public event Action Closed;

    private void Awake()
    {
        if (m_resultImage != null)
            m_defaultResultSprite = m_resultImage.sprite;

        Prewarm();
        ResetTransientState();
        RefreshPresentation();
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshPresentation();
    }

    private void Start()
    {
        m_hasStarted = true;
        if (m_startClosed && !m_openRequestedBeforeStart)
            CloseInternal(false);

        m_openRequestedBeforeStart = false;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>
    /// 레시피/재료 행을 한 번 생성해 캐시에 보관합니다.
    /// CreateView가 처음 닫힐 때도 이후 열기에서 Instantiate가 반복되지 않습니다.
    /// </summary>
    public bool Prewarm()
    {
        bool recipeReady = m_recipeListView != null && m_recipeListView.Prewarm();
        bool materialReady = m_materialListView != null && m_materialListView.Prewarm();
        return recipeReady && materialReady;
    }

    /// <summary>비어 있는 제조 슬롯을 대상으로 CreateView를 엽니다.</summary>
    public bool Open(int slotIndex)
    {
        if (m_manager == null
            || slotIndex < 0
            || slotIndex >= m_manager.CraftingSlotCount
            || !m_manager.IsCraftingSlotUnlocked(slotIndex))
        {
            return false;
        }

        m_pendingSlotIndex = slotIndex;
        m_selectedRecipeId = string.Empty;
        m_requestedBatchCount = 1;
        m_openRequestedBeforeStart = !m_hasStarted;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Subscribe();
        m_recipeListView?.RefreshRecipeStates();
        RefreshPresentation();
        return true;
    }

    /// <summary>
    /// 상위 UI가 제조 작업 시작에 성공한 뒤 CreateView를 닫을 때 사용합니다.
    /// </summary>
    public void CloseAfterConfirmed()
    {
        CloseInternal(true);
    }

    /// <summary>선택 중인 임시 상태만 버리고 CreateView를 닫습니다.</summary>
    public void Cancel()
    {
        if (!IsOpen)
            return;

        CloseInternal(true);
    }

    /// <summary>
    /// 상위 UI의 Escape 우선순위 처리에서 CreateView가 열려 있으면 먼저 닫습니다.
    /// </summary>
    public bool TryHandleEscape()
    {
        if (!IsOpen)
            return false;

        Cancel();
        return true;
    }

    /// <summary>현재 선택과 창고 상태를 기준으로 상세 표시를 다시 계산합니다.</summary>
    public void Refresh()
    {
        m_recipeListView?.RefreshRecipeStates();
        RefreshPresentation();
    }

    private void HandleRecipeSelected(string recipeId)
    {
        if (!TryResolveUnlockedRecipe(recipeId, out ManufacturingRecipeDefinition recipe))
            return;

        m_selectedRecipeId = recipe.RecipeId;
        m_requestedBatchCount = 1;
        RefreshPresentation();
    }

    private void DecreaseTen()
    {
        ChangeRequestedBatchCount(-10);
    }

    private void DecreaseOne()
    {
        ChangeRequestedBatchCount(-1);
    }

    private void IncreaseOne()
    {
        ChangeRequestedBatchCount(1);
    }

    private void IncreaseTen()
    {
        ChangeRequestedBatchCount(10);
    }

    private void ChangeRequestedBatchCount(int delta)
    {
        if (!HasSelectedRecipe())
            return;

        int maxOrderQuantity = Mathf.Max(1, m_manager.MaxOrderQuantity);
        int next = Mathf.Clamp(m_requestedBatchCount + delta, 1, maxOrderQuantity);
        if (next == m_requestedBatchCount)
            return;

        m_requestedBatchCount = next;
        RefreshPresentation();
    }

    private void HandleConfirmClicked()
    {
        if (!m_canConfirm)
            return;

        ConfirmRequested?.Invoke(new ManufacturingCreateRequest(
            m_pendingSlotIndex,
            m_selectedRecipeId,
            m_requestedBatchCount));
    }

    private void RefreshPresentation()
    {
        UpdateRequestedBatchCountText();

        if (!TryResolveUnlockedRecipe(
                m_selectedRecipeId,
                out ManufacturingRecipeDefinition recipe))
        {
            ClearSelectionPresentation();
            return;
        }

        bool resultQuantityResolved = SetResultPresentation(recipe);

        bool quoteResolved = m_manager.TryFillMaterialQuote(
            recipe.RecipeId,
            m_requestedBatchCount,
            m_quoteLines,
            out bool canAfford);
        bool materialsDisplayed = quoteResolved
            && m_materialListView != null
            && m_materialListView.Bind(m_quoteLines);

        m_canConfirm = m_pendingSlotIndex >= 0
            && m_manager.IsCraftingSlotUnlocked(m_pendingSlotIndex)
            && resultQuantityResolved
            && quoteResolved
            && materialsDisplayed
            && canAfford;
        UpdateButtonStates(true);
    }

    private void ClearSelectionPresentation()
    {
        m_selectedRecipeId = string.Empty;
        m_quoteLines.Clear();
        m_materialListView?.Clear();

        if (m_resultImage != null)
            m_resultImage.sprite = m_defaultResultSprite;

        if (m_totalResultQuantityText != null)
            m_totalResultQuantityText.text = string.Empty;

        m_canConfirm = false;
        UpdateButtonStates(false);
    }

    private bool SetResultPresentation(ManufacturingRecipeDefinition recipe)
    {
        if (m_resultImage != null)
            m_resultImage.sprite = recipe.Icon != null
                ? recipe.Icon
                : m_defaultResultSprite;

        if (m_totalResultQuantityText == null)
            return false;

        long totalResultQuantity =
            (long)m_requestedBatchCount * recipe.ResultQuantityPerBatch;
        bool isValid = totalResultQuantity > 0
            && totalResultQuantity <= int.MaxValue;
        m_totalResultQuantityText.text = isValid
            ? totalResultQuantity.ToString()
            : string.Empty;
        return isValid;
    }

    private void UpdateRequestedBatchCountText()
    {
        if (m_requestedBatchCountText != null)
            m_requestedBatchCountText.text = m_requestedBatchCount.ToString();
    }

    private void UpdateButtonStates(bool hasSelection)
    {
        int maxOrderQuantity = m_manager != null
            ? Mathf.Max(1, m_manager.MaxOrderQuantity)
            : 1;

        SetInteractable(
            m_decreaseTenButton,
            hasSelection && m_requestedBatchCount > 1);
        SetInteractable(
            m_decreaseOneButton,
            hasSelection && m_requestedBatchCount > 1);
        SetInteractable(
            m_increaseOneButton,
            hasSelection && m_requestedBatchCount < maxOrderQuantity);
        SetInteractable(
            m_increaseTenButton,
            hasSelection && m_requestedBatchCount < maxOrderQuantity);
        SetInteractable(m_confirmButton, m_canConfirm);
    }

    private bool TryResolveUnlockedRecipe(
        string recipeId,
        out ManufacturingRecipeDefinition recipe)
    {
        recipe = null;
        return m_manager != null
            && m_manager.TryGetRecipe(recipeId, out recipe)
            && m_manager.IsRecipeUnlocked(recipe);
    }

    private bool HasSelectedRecipe()
    {
        return TryResolveUnlockedRecipe(m_selectedRecipeId, out _);
    }

    private void CloseInternal(bool notifyClosed)
    {
        ResetTransientState();
        RefreshPresentation();

        if (gameObject.activeSelf)
            gameObject.SetActive(false);

        if (notifyClosed)
            Closed?.Invoke();
    }

    private void ResetTransientState()
    {
        m_pendingSlotIndex = -1;
        m_selectedRecipeId = string.Empty;
        m_requestedBatchCount = 1;
        m_canConfirm = false;
        m_quoteLines.Clear();
    }

    private void Subscribe()
    {
        if (m_isSubscribed)
            return;

        if (m_recipeListView != null)
            m_recipeListView.RecipeSelected += HandleRecipeSelected;
        if (m_manager != null)
            m_manager.StateChanged += Refresh;
        if (m_shelterDataManager != null)
            m_shelterDataManager.Storage.ResourcesChanged += RefreshPresentation;

        AddButtonListeners();
        m_isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!m_isSubscribed)
            return;

        if (m_recipeListView != null)
            m_recipeListView.RecipeSelected -= HandleRecipeSelected;
        if (m_manager != null)
            m_manager.StateChanged -= Refresh;
        if (m_shelterDataManager != null)
            m_shelterDataManager.Storage.ResourcesChanged -= RefreshPresentation;

        RemoveButtonListeners();
        m_isSubscribed = false;
    }

    private void AddButtonListeners()
    {
        m_decreaseTenButton?.onClick.AddListener(DecreaseTen);
        m_decreaseOneButton?.onClick.AddListener(DecreaseOne);
        m_increaseOneButton?.onClick.AddListener(IncreaseOne);
        m_increaseTenButton?.onClick.AddListener(IncreaseTen);
        m_confirmButton?.onClick.AddListener(HandleConfirmClicked);
        m_cancelButton?.onClick.AddListener(Cancel);
    }

    private void RemoveButtonListeners()
    {
        m_decreaseTenButton?.onClick.RemoveListener(DecreaseTen);
        m_decreaseOneButton?.onClick.RemoveListener(DecreaseOne);
        m_increaseOneButton?.onClick.RemoveListener(IncreaseOne);
        m_increaseTenButton?.onClick.RemoveListener(IncreaseTen);
        m_confirmButton?.onClick.RemoveListener(HandleConfirmClicked);
        m_cancelButton?.onClick.RemoveListener(Cancel);
    }

    private static void SetInteractable(Button button, bool interactable)
    {
        if (button != null)
            button.interactable = interactable;
    }
}

/// <summary>ManufacturingUI가 최종 검증에 사용할 CreateView 확정 요청입니다.</summary>
public readonly struct ManufacturingCreateRequest
{
    public int SlotIndex { get; }
    public string RecipeId { get; }
    public int RequestedBatchCount { get; }

    public ManufacturingCreateRequest(
        int slotIndex,
        string recipeId,
        int requestedBatchCount)
    {
        SlotIndex = slotIndex;
        RecipeId = recipeId ?? string.Empty;
        RequestedBatchCount = requestedBatchCount;
    }
}
