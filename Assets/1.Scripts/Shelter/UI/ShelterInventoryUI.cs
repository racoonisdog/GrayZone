using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 셸터 런타임 창고의 자원과 제작 아이템을 하나의 읽기 전용 그리드에 투영합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShelterInventoryUI : MonoBehaviour
{
    [Header("UI Ownership")]
    [SerializeField] private UIManager m_uiManager;
    [SerializeField] private GameObject m_viewRoot;
    [SerializeField] private Button m_openButton;
    [SerializeField] private Button m_closeButton;
    [SerializeField] private bool m_startClosed = true;

    [Header("Runtime Data")]
    [SerializeField] private ShelterSceneDataManager m_shelterDataManager;
    [SerializeField] private ItemDefinitionCatalog m_itemCatalog;
    [SerializeField] private ResourceDefinitionCatalog m_resourceCatalog;

    [Header("Grid")]
    [SerializeField] private RectTransform m_contentRoot;
    [SerializeField] private ShelterInventoryItemView m_itemPrefab;

    private readonly ShelterInventoryProjectionBuilder m_projectionBuilder =
        new();
    private readonly List<ShelterInventoryDisplayEntry> m_entries = new();
    private readonly Dictionary<string, ShelterInventoryItemView> m_rows =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> m_visibleKeys =
        new(StringComparer.Ordinal);

    private StorageFacility m_boundStorage;
    private bool m_hasStarted;
    private bool m_setupErrorLogged;
    private bool m_unresolvedItemLogged;

    public bool IsOpen => m_viewRoot != null && m_viewRoot.activeSelf;
    public int VisibleEntryCount => m_visibleKeys.Count;

    public event Action Closed;

    private void Awake()
    {
        CacheReferences();
        if (m_startClosed)
            SetViewActive(false);
    }

    private void OnEnable()
    {
        CacheReferences();
        AddButtonListeners();
        SubscribeStorage();

        if (m_hasStarted)
            Refresh();
    }

    private void Start()
    {
        m_hasStarted = true;
        SubscribeStorage();
        Refresh();
    }

    private void OnDisable()
    {
        RemoveButtonListeners();
        UnsubscribeStorage();
        SetViewActive(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.iKey.wasPressedThisFrame)
        {
            RequestToggle();
            return;
        }

        if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
            RequestClose();
    }

    /// <summary>UIManager가 차단형 UI 상태를 확보한 뒤 인벤토리를 엽니다.</summary>
    public bool Open()
    {
        if (!Refresh())
            return false;

        SetViewActive(true);
        if (m_contentRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(m_contentRoot);
        return true;
    }

    /// <summary>인벤토리 패널만 닫고 런타임 창고 데이터는 변경하지 않습니다.</summary>
    public void Close()
    {
        bool wasOpen = IsOpen;
        SetViewActive(false);

        if (wasOpen)
            Closed?.Invoke();
    }

    /// <summary>
    /// 현재 셸터 런타임 데이터를 다시 투영합니다.
    /// 새 stable key만 프리팹을 생성하고 기존 행은 수량만 재바인딩합니다.
    /// </summary>
    public bool Refresh()
    {
        CacheReferences();
        SubscribeStorage();
        if (!HasRequiredReferences())
        {
            LogSetupErrorOnce();
            return false;
        }

        bool allItemsResolved = m_projectionBuilder.FillEntries(
            m_boundStorage,
            m_itemCatalog,
            m_resourceCatalog,
            m_entries);

        SyncRows();

        if (!allItemsResolved && !m_unresolvedItemLogged)
        {
            Debug.LogWarning(
                $"[{nameof(ShelterInventoryUI)}] "
                + "At least one owned item ID is missing from ItemDefinitionCatalog.",
                this);
            m_unresolvedItemLogged = true;
        }
        else if (allItemsResolved)
        {
            m_unresolvedItemLogged = false;
        }

        return true;
    }

    private void SyncRows()
    {
        m_visibleKeys.Clear();

        for (int i = 0; i < m_entries.Count; i++)
        {
            ShelterInventoryDisplayEntry entry = m_entries[i];
            if (string.IsNullOrWhiteSpace(entry.StableKey)
                || !m_visibleKeys.Add(entry.StableKey))
            {
                continue;
            }

            if (!m_rows.TryGetValue(
                    entry.StableKey,
                    out ShelterInventoryItemView row)
                || row == null)
            {
                row = Instantiate(m_itemPrefab, m_contentRoot, false);
                row.name = BuildRowName(entry);
                m_rows[entry.StableKey] = row;
            }

            row.Bind(entry);
            row.transform.SetSiblingIndex(i);
        }

        foreach (KeyValuePair<string, ShelterInventoryItemView> pair in m_rows)
        {
            if (!m_visibleKeys.Contains(pair.Key) && pair.Value != null)
                pair.Value.Hide();
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(m_contentRoot);
    }

    private void RequestToggle()
    {
        CacheReferences();
        if (m_uiManager != null)
        {
            m_uiManager.TryToggleInventoryUI();
            return;
        }

        if (IsOpen)
            Close();
        else
            Open();
    }

    private void RequestClose()
    {
        CacheReferences();
        if (m_uiManager != null)
            m_uiManager.CloseInventoryUI();
        else
            Close();
    }

    private void HandleStorageChanged()
    {
        Refresh();
    }

    private void SubscribeStorage()
    {
        if (m_shelterDataManager == null)
            m_shelterDataManager = ShelterSceneDataManager.Instance;

        StorageFacility storage = m_shelterDataManager != null
            ? m_shelterDataManager.Storage
            : null;
        if (ReferenceEquals(m_boundStorage, storage))
            return;

        UnsubscribeStorage();
        m_boundStorage = storage;
        if (m_boundStorage == null)
            return;

        m_boundStorage.ResourcesChanged += HandleStorageChanged;
        m_boundStorage.ItemsChanged += HandleStorageChanged;
    }

    private void UnsubscribeStorage()
    {
        if (m_boundStorage == null)
            return;

        m_boundStorage.ResourcesChanged -= HandleStorageChanged;
        m_boundStorage.ItemsChanged -= HandleStorageChanged;
        m_boundStorage = null;
    }

    private void AddButtonListeners()
    {
        m_openButton?.onClick.RemoveListener(RequestToggle);
        m_openButton?.onClick.AddListener(RequestToggle);
        m_closeButton?.onClick.RemoveListener(RequestClose);
        m_closeButton?.onClick.AddListener(RequestClose);
    }

    private void RemoveButtonListeners()
    {
        m_openButton?.onClick.RemoveListener(RequestToggle);
        m_closeButton?.onClick.RemoveListener(RequestClose);
    }

    private void CacheReferences()
    {
        if (m_uiManager == null)
            m_uiManager = FindFirstObjectByType<UIManager>();

        if (m_shelterDataManager == null)
            m_shelterDataManager = ShelterSceneDataManager.Instance;

        if (m_openButton == null)
            m_openButton = GetComponent<Button>();

        if (m_viewRoot == null)
        {
            Transform view = transform.Find("ItemView");
            if (view != null)
                m_viewRoot = view.gameObject;
        }

        if (m_closeButton == null && m_viewRoot != null)
        {
            Transform close = m_viewRoot.transform.Find("close");
            if (close != null)
                m_closeButton = close.GetComponent<Button>();
        }

        if (m_contentRoot == null && m_viewRoot != null)
        {
            Transform content = m_viewRoot.transform.Find("Viewport/Content");
            if (content != null)
                m_contentRoot = content as RectTransform;
        }
    }

    private bool HasRequiredReferences()
    {
        return m_boundStorage != null
            && m_itemCatalog != null
            && m_resourceCatalog != null
            && m_contentRoot != null
            && m_itemPrefab != null;
    }

    private void SetViewActive(bool active)
    {
        if (m_viewRoot != null && m_viewRoot.activeSelf != active)
            m_viewRoot.SetActive(active);
    }

    private void LogSetupErrorOnce()
    {
        if (m_setupErrorLogged)
            return;

        Debug.LogError(
            $"[{nameof(ShelterInventoryUI)}] "
            + "Shelter data, catalogs, Content, and Inven_Item prefab must be assigned.",
            this);
        m_setupErrorLogged = true;
    }

    private static string BuildRowName(
        ShelterInventoryDisplayEntry entry)
    {
        string type = entry.Kind == ShelterInventoryDisplayEntryKind.Resource
            ? "Resource"
            : "Item";
        return $"Inven_Item_{type}_{entry.StableKey.Replace(':', '_')}";
    }
}
