using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Scramble 시설 버튼 입력을 시설 로직에 전달하고 업그레이드 상태와 비용을 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrambleFacilityUI : MonoBehaviour
{
    [Serializable]
    private sealed class UpgradeRowView
    {
        [SerializeField] private TMP_Text m_requirementText;

        public void Cache(Transform uiRoot, string rowPath)
        {
            if (uiRoot == null || string.IsNullOrWhiteSpace(rowPath))
                return;

            Transform row = uiRoot.Find(rowPath);
            if (row == null)
                return;

            m_requirementText ??= row.Find("matter")?.GetComponent<TMP_Text>();
        }

        public void Refresh(string requirement)
        {
            SetText(m_requirementText, requirement);
        }
    }

    [Header("Facility")]
    [SerializeField] private ScrambleFacility m_facility;
    [SerializeField] private ResourceDefinitionCatalog m_resourceCatalog;

    [Header("Buttons")]
    [SerializeField] private Button m_battleSceneButton;
    [FormerlySerializedAs("m_shooter01UpgradeButton")]
    [SerializeField] private Button m_trapUpgradeButton;
    [SerializeField] private Button m_spikeUpgradeButton;
    [SerializeField] private Button m_explosiveUpgradeButton;
    [SerializeField] private Button m_shooterUpgradeButton;
    [SerializeField] private Button m_wireUpgradeButton;

    [Header("Upgrade Rows (Optional)")]
    [SerializeField] private UpgradeRowView m_trapRow = new();
    [SerializeField] private UpgradeRowView m_spikeRow = new();
    [SerializeField] private UpgradeRowView m_explosiveRow = new();
    [SerializeField] private UpgradeRowView m_shooterRow = new();
    [SerializeField] private UpgradeRowView m_wireRow = new();

    [Header("Notice (Optional)")]
    [SerializeField] private TMP_Text m_noticeText;

    public bool IsOpen => gameObject.activeSelf;
    public event Action Closed;

    private void Awake()
    {
        CacheFacility();
        CacheResourceCatalog();
        CacheUpgradeButtons();
        CacheUpgradeRows();
    }

    private void OnEnable()
    {
        CacheFacility();
        CacheResourceCatalog();
        CacheUpgradeButtons();
        CacheUpgradeRows();
        Bind();
        Refresh();
    }

    private void OnDisable()
    {
        Unbind();
    }

    public void Open(ScrambleFacility facility)
    {
        if (facility == null)
        {
            Debug.LogWarning("[ScrambleFacilityUI] ScrambleFacility is not available.", this);
            return;
        }

        Unbind();
        m_facility = facility;
        gameObject.SetActive(true);
        CacheResourceCatalog();
        CacheUpgradeButtons();
        CacheUpgradeRows();
        Bind();
        ClearNotice();
        Refresh();
    }

    public void Close()
    {
        bool wasOpen = gameObject.activeSelf;
        ClearNotice();
        gameObject.SetActive(false);

        if (wasOpen)
            Closed?.Invoke();
    }

    public bool TryHandleEscape()
    {
        if (!IsOpen)
            return false;

        Close();
        return true;
    }

    public void Refresh()
    {
        if (m_facility == null)
            return;

        CacheResourceCatalog();
        CacheUpgradeRows();

        if (m_battleSceneButton != null)
            m_battleSceneButton.interactable = true;
        if (m_trapUpgradeButton != null)
            m_trapUpgradeButton.interactable = m_facility.CanUpgrade(ScrambleUpgradeType.Trap);
        if (m_spikeUpgradeButton != null)
            m_spikeUpgradeButton.interactable = m_facility.CanUpgrade(ScrambleUpgradeType.Spike);
        if (m_explosiveUpgradeButton != null)
            m_explosiveUpgradeButton.interactable = m_facility.CanUpgrade(ScrambleUpgradeType.Explosive);
        if (m_shooterUpgradeButton != null)
            m_shooterUpgradeButton.interactable = m_facility.CanUpgrade(ScrambleUpgradeType.Shooter);
        if (m_wireUpgradeButton != null)
            m_wireUpgradeButton.interactable = m_facility.CanUpgrade(ScrambleUpgradeType.Wire);

        string resourceDisplayName = ResolveUpgradeResourceDisplayName();
        RefreshUpgradeRow(
            m_trapRow,
            ScrambleUpgradeType.Trap,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_spikeRow,
            ScrambleUpgradeType.Spike,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_explosiveRow,
            ScrambleUpgradeType.Explosive,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_shooterRow,
            ScrambleUpgradeType.Shooter,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_wireRow,
            ScrambleUpgradeType.Wire,
            resourceDisplayName);
    }

    private void HandleBattleSceneClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryLoadBattleScene())
            SetNotice("전투 씬으로 이동하지 못했습니다.");
    }

    private void HandleTrapUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeTrap())
            SetNotice("Trap을 업그레이드하지 못했습니다.");
    }

    private void HandleSpikeUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeSpike())
            SetNotice("Spike를 업그레이드하지 못했습니다.");
    }

    private void HandleExplosiveUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeExplosive())
            SetNotice("Explosive를 업그레이드하지 못했습니다.");
    }

    private void HandleShooterUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeShooter())
            SetNotice("Shooter를 업그레이드하지 못했습니다.");
    }

    private void HandleWireUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeWire())
            SetNotice("Wire를 업그레이드하지 못했습니다.");
    }

    private void HandleFacilityStateChanged()
    {
        ClearNotice();
        Refresh();
    }

    private void Bind()
    {
        Unbind();

        if (m_battleSceneButton != null)
            m_battleSceneButton.onClick.AddListener(HandleBattleSceneClicked);
        if (m_trapUpgradeButton != null)
            m_trapUpgradeButton.onClick.AddListener(HandleTrapUpgradeClicked);
        if (m_spikeUpgradeButton != null)
            m_spikeUpgradeButton.onClick.AddListener(HandleSpikeUpgradeClicked);
        if (m_explosiveUpgradeButton != null)
            m_explosiveUpgradeButton.onClick.AddListener(HandleExplosiveUpgradeClicked);
        if (m_shooterUpgradeButton != null)
            m_shooterUpgradeButton.onClick.AddListener(HandleShooterUpgradeClicked);
        if (m_wireUpgradeButton != null)
            m_wireUpgradeButton.onClick.AddListener(HandleWireUpgradeClicked);
        if (m_facility != null)
            m_facility.StateChanged += HandleFacilityStateChanged;
    }

    private void Unbind()
    {
        if (m_battleSceneButton != null)
            m_battleSceneButton.onClick.RemoveListener(HandleBattleSceneClicked);
        if (m_trapUpgradeButton != null)
            m_trapUpgradeButton.onClick.RemoveListener(HandleTrapUpgradeClicked);
        if (m_spikeUpgradeButton != null)
            m_spikeUpgradeButton.onClick.RemoveListener(HandleSpikeUpgradeClicked);
        if (m_explosiveUpgradeButton != null)
            m_explosiveUpgradeButton.onClick.RemoveListener(HandleExplosiveUpgradeClicked);
        if (m_shooterUpgradeButton != null)
            m_shooterUpgradeButton.onClick.RemoveListener(HandleShooterUpgradeClicked);
        if (m_wireUpgradeButton != null)
            m_wireUpgradeButton.onClick.RemoveListener(HandleWireUpgradeClicked);
        if (m_facility != null)
            m_facility.StateChanged -= HandleFacilityStateChanged;
    }

    private void CacheFacility()
    {
        if (m_facility == null)
            m_facility = FindFirstObjectByType<ScrambleFacility>();
    }

    private void CacheResourceCatalog()
    {
        if (m_resourceCatalog != null)
            return;

        ShelterInventoryUI inventoryUI =
            FindFirstObjectByType<ShelterInventoryUI>(FindObjectsInactive.Include);
        if (inventoryUI != null)
            m_resourceCatalog = inventoryUI.ResourceCatalog;
    }

    private void CacheUpgradeButtons()
    {
        m_trapUpgradeButton ??= FindUpgradeButton("TrapUpgrade/Trap_Botton");
        m_spikeUpgradeButton ??= FindUpgradeButton("SpikeUpgrade/Spike_Botton");
        m_explosiveUpgradeButton ??= FindUpgradeButton("ExplosiveUpgrade/Explosive_Botton");
        m_shooterUpgradeButton ??= FindUpgradeButton("ShooterUpgrade/Shooter_Botton");
        m_wireUpgradeButton ??= FindUpgradeButton("WireUpgrade/Wire_Botton");
    }

    private void CacheUpgradeRows()
    {
        m_trapRow ??= new UpgradeRowView();
        m_spikeRow ??= new UpgradeRowView();
        m_explosiveRow ??= new UpgradeRowView();
        m_shooterRow ??= new UpgradeRowView();
        m_wireRow ??= new UpgradeRowView();

        m_trapRow.Cache(transform, "TrapUpgrade");
        m_spikeRow.Cache(transform, "SpikeUpgrade");
        m_explosiveRow.Cache(transform, "ExplosiveUpgrade");
        m_shooterRow.Cache(transform, "ShooterUpgrade");
        m_wireRow.Cache(transform, "WireUpgrade");
    }

    private Button FindUpgradeButton(string relativePath)
    {
        Transform buttonTransform = transform.Find(relativePath);
        return buttonTransform != null
            ? buttonTransform.GetComponent<Button>()
            : null;
    }

    private string ResolveUpgradeResourceDisplayName()
    {
        string resourceId = m_facility.UpgradeResourceId;
        if (m_resourceCatalog != null
            && m_resourceCatalog.TryGetPresentation(
                resourceId,
                out ResourcePresentation presentation))
        {
            return string.IsNullOrWhiteSpace(presentation.DisplayName)
                ? resourceId
                : presentation.DisplayName;
        }

        return resourceId;
    }

    private void RefreshUpgradeRow(
        UpgradeRowView row,
        ScrambleUpgradeType upgradeType,
        string resourceDisplayName)
    {
        if (row == null)
            return;

        int currentLevel = m_facility.GetUpgradeLevel(upgradeType);
        int maxLevel = m_facility.GetMaxUpgradeLevel(upgradeType);
        string requirement = currentLevel >= maxLevel
            ? "업그레이드 완료"
            : $"{resourceDisplayName} x{m_facility.GetUpgradeCost(upgradeType)}";

        row.Refresh(requirement);
    }

    private void SetNotice(string message)
    {
        if (m_noticeText != null)
            m_noticeText.text = message;
        else
            Debug.LogWarning($"[ScrambleFacilityUI] {message}", this);
    }

    private void ClearNotice()
    {
        SetText(m_noticeText, string.Empty);
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value;
    }
}
