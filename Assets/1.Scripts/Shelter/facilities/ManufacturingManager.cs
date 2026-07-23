using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 시설의 공용 해금/업그레이드 연결과 제조 전용 작업 흐름을 담당합니다.
/// </summary>
/// <remarks>
/// 시설 해금과 레벨의 진실원천은 <see cref="FacilityManager"/>입니다.
/// 제조 작업과 창고 상태는 각각 <see cref="ShelterSceneDataManager.Manufacturing"/>과
/// <see cref="ShelterSceneDataManager.Storage"/>를 직접 사용하며 이 컴포넌트가 복제본을 소유하지 않습니다.
/// </remarks>
public sealed class ManufacturingManager : MonoBehaviour, IFacilityUpgradeable
{
    private const int MaxLevelIndex = 3;
    private const int TotalCraftingSlotCount = 4;
    private const int TotalHelperSlotCount = 2;

    private static readonly ManufacturingJobRuntimeData[] EmptyJobs =
        Array.Empty<ManufacturingJobRuntimeData>();

    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "manufacturing_center";
    [SerializeField] private string roomId = "manufacturing_room";

    [Header("Scene Dependencies")]
    [SerializeField] private ShelterSceneDataManager shelterDataManager;
    [SerializeField] private CharacterManager characterManager;

    [Header("Level Visuals")]
    [SerializeField] private FacilityLevelVisuals levelVisuals;

    [Header("Upgrade Cost (레벨 i → i+1)")]
    [SerializeField] private UpgradeCostTier[] upgradeCosts;

    [Header("Recipes")]
    [SerializeField] private ManufacturingRecipeDefinition[] recipes =
        Array.Empty<ManufacturingRecipeDefinition>();
    [Min(1)]
    [SerializeField] private int maxOrderQuantity = 99;

    [Header("Productivity")]
    [Min(1)]
    [SerializeField] private int baseProductivity = 5;
    [Min(0)]
    [SerializeField] private int level3ProductivityBonus = 5;
    [SerializeField] private HelperProductivityBonus[] helperProductivityBonuses =
    {
        new HelperProductivityBonus { type = NPCType.Tanker, bonus = 2 },
        new HelperProductivityBonus { type = NPCType.Healer, bonus = 1 },
        new HelperProductivityBonus { type = NPCType.Dealer, bonus = 1 }
    };

    [Header("Helper Slots")]
    [Tooltip("두 번째 헬퍼 슬롯이 열리는 플레이어 표시 레벨입니다.")]
    [Range(1, 4)]
    [SerializeField] private int secondHelperSlotUnlockLevel = 3;

    private bool m_isUnlocked = true;

    /// <summary>작업 생성, 진행, 취소 또는 시설 상태가 바뀌었을 때 발생합니다.</summary>
    public event Action StateChanged;

    /// <summary>제조 작업 목록 또는 작업 진행량이 바뀌었을 때 발생합니다.</summary>
    public event Action JobsChanged;

    /// <summary>헬퍼 배치 상태 또는 최종 제작력이 바뀌었을 때 발생합니다.</summary>
    public event Action HelpersChanged;

    public string FacilityId
    {
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;

            return fallbackFacilityId;
        }
    }

    /// <summary>내부 0 기반 시설 레벨입니다. UI 표시 레벨은 <see cref="DisplayLevel"/>을 사용합니다.</summary>
    public int UpgradeLevel => CurrentLevel;
    public int MaxUpgradeLevel => MaxLevelIndex;
    public int DisplayLevel => CurrentLevel + 1;
    public bool IsUnlocked => m_isUnlocked;
    public int CraftingSlotCount => TotalCraftingSlotCount;
    public int HelperSlotCount => TotalHelperSlotCount;
    public int UnlockedCraftingSlotCount => Mathf.Clamp(DisplayLevel, 1, TotalCraftingSlotCount);
    public int HelperCapacity => HelperSlotsForLevel(CurrentLevel);
    public int CurrentHelperCount => CountAssignedHelpers();
    public int MaxOrderQuantity => maxOrderQuantity;
    public int FinalProductivity => CalculateFinalProductivity();
    public IReadOnlyList<ManufacturingRecipeDefinition> Recipes =>
        recipes ?? Array.Empty<ManufacturingRecipeDefinition>();
    public IReadOnlyList<ManufacturingJobRuntimeData> Jobs =>
        TryGetManufacturingData(out ManufacturingRuntimeData runtimeData)
            ? runtimeData.Jobs
            : EmptyJobs;

    private int CurrentLevel =>
        FacilityManager.Instance != null
            ? FacilityManager.Instance.GetUpgradeLevel(FacilityId)
            : 0;

    private void Awake()
    {
        CacheDependencies();
    }

    private void OnValidate()
    {
        fallbackFacilityId = fallbackFacilityId?.Trim() ?? string.Empty;
        roomId = roomId?.Trim() ?? string.Empty;
        maxOrderQuantity = Mathf.Max(1, maxOrderQuantity);
        baseProductivity = Mathf.Max(1, baseProductivity);
        level3ProductivityBonus = Mathf.Max(0, level3ProductivityBonus);
        secondHelperSlotUnlockLevel = Mathf.Clamp(secondHelperSlotUnlockLevel, 1, 4);
        recipes ??= Array.Empty<ManufacturingRecipeDefinition>();
        helperProductivityBonuses ??= Array.Empty<HelperProductivityBonus>();
    }

    private void Start()
    {
        CacheDependencies();

        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced += OnDayAdvanced;

        if (characterManager != null)
            characterManager.CharactersChanged += OnCharactersChanged;

        // 등록 즉시 FacilityManager가 셸터 런타임의 해금/레벨 상태를 적용합니다.
        FacilityManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced -= OnDayAdvanced;

        if (characterManager != null)
            characterManager.CharactersChanged -= OnCharactersChanged;

        FacilityManager.Instance?.Unregister(this);
    }

    public void ApplyUpgradeLevel(int level)
    {
        RefreshLevelVisuals();
        NotifyStateChanged();
    }

    public void ApplyUnlockState(bool isUnlocked)
    {
        m_isUnlocked = isUnlocked;
        RefreshLevelVisuals();
        NotifyStateChanged();
    }

    public CostBundle GetUpgradeCost(int currentLevel)
    {
        if (upgradeCosts == null || currentLevel < 0 || currentLevel >= upgradeCosts.Length)
            return new CostBundle();

        UpgradeCostTier tier = upgradeCosts[currentLevel];
        UpgradeCostEntry[] entries = tier?.entries;
        if (entries == null || entries.Length == 0)
            return new CostBundle();

        CurrencyCost[] costs = new CurrencyCost[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            UpgradeCostEntry entry = entries[i];
            costs[i] = entry != null
                ? new CurrencyCost(entry.type, entry.amount)
                : new CurrencyCost(default, 0);
        }

        return new CostBundle(costs);
    }

    public bool AreUpgradeRequirementsMet(int currentLevel) => true;

    public IReadOnlyList<FacilityFeatureLine> GetUpgradeFeatureLines(int currentLevel)
    {
        List<FacilityFeatureLine> lines = new();
        int nextLevel = currentLevel + 1;
        if (nextLevel > MaxLevelIndex)
            return lines;

        int currentCraftingSlots = CraftingSlotsForLevel(currentLevel);
        int nextCraftingSlots = CraftingSlotsForLevel(nextLevel);
        if (currentCraftingSlots != nextCraftingSlots)
        {
            lines.Add(new FacilityFeatureLine(
                "제조 슬롯",
                $"{currentCraftingSlots} → {nextCraftingSlots}"));
        }

        int currentHelperSlots = HelperSlotsForLevel(currentLevel);
        int nextHelperSlots = HelperSlotsForLevel(nextLevel);
        if (currentHelperSlots != nextHelperSlots)
        {
            lines.Add(new FacilityFeatureLine(
                "헬퍼 슬롯",
                $"{currentHelperSlots} → {nextHelperSlots}"));
        }

        int currentLevelBonus = LevelProductivityBonus(currentLevel);
        int nextLevelBonus = LevelProductivityBonus(nextLevel);
        if (currentLevelBonus != nextLevelBonus)
        {
            lines.Add(new FacilityFeatureLine(
                "기본 제작력",
                $"{baseProductivity + currentLevelBonus} → {baseProductivity + nextLevelBonus}"));
        }

        return lines;
    }

    /// <summary>제조 슬롯이 현재 시설 레벨에서 사용 가능한지 반환합니다.</summary>
    public bool IsCraftingSlotUnlocked(int slotIndex)
    {
        return m_isUnlocked
            && slotIndex >= 0
            && slotIndex < CraftingSlotsForLevel(CurrentLevel);
    }

    /// <summary>헬퍼 슬롯이 현재 시설 레벨에서 사용 가능한지 반환합니다.</summary>
    public bool IsHelperSlotUnlocked(int slotIndex)
    {
        return m_isUnlocked
            && slotIndex >= 0
            && slotIndex < HelperSlotsForLevel(CurrentLevel);
    }

    /// <summary>레시피가 현재 시설 레벨에서 사용 가능한지 반환합니다.</summary>
    public bool IsRecipeUnlocked(ManufacturingRecipeDefinition recipe)
    {
        return m_isUnlocked
            && recipe != null
            && recipe.RequiredFacilityLevel <= DisplayLevel;
    }

    public bool TryGetRecipe(string recipeId, out ManufacturingRecipeDefinition recipe)
    {
        recipe = null;
        string normalizedId = NormalizeId(recipeId);
        if (string.IsNullOrEmpty(normalizedId) || recipes == null)
            return false;

        for (int i = 0; i < recipes.Length; i++)
        {
            ManufacturingRecipeDefinition candidate = recipes[i];
            if (candidate != null
                && string.Equals(candidate.RecipeId, normalizedId, StringComparison.Ordinal))
            {
                recipe = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 전체 재료를 선차감하고 비어 있는 슬롯에 제조 작업을 생성합니다.
    /// </summary>
    public bool TryStartJob(int slotIndex, string recipeId, int quantity)
    {
        if (!IsCraftingSlotUnlocked(slotIndex)
            || quantity < 1
            || quantity > maxOrderQuantity
            || !TryGetRecipe(recipeId, out ManufacturingRecipeDefinition recipe)
            || !IsRecipeUnlocked(recipe)
            || string.IsNullOrWhiteSpace(recipe.ResultItemDefinitionId))
        {
            return false;
        }

        if (!TryGetContext(
                out ShelterSceneDataManager dataManager,
                out StorageFacility storage,
                out ManufacturingRuntimeData runtimeData))
        {
            return false;
        }

        if (runtimeData.TryGetJob(slotIndex, out _))
            return false;

        CostBundle unitCost = recipe.BuildUnitCost();
        if (!TryBuildQuantityCost(unitCost, quantity, out CostBundle totalCost))
            return false;

        ManufacturingJobRuntimeData job = new(
            slotIndex,
            recipe.RecipeId,
            recipe.ResultItemDefinitionId,
            quantity,
            recipe.UnitWork,
            unitCost.Costs);

        if (!job.IsValid || !storage.TrySpendResources(totalCost))
            return false;

        if (!runtimeData.TryAddJob(job))
        {
            if (!storage.TryAddResources(totalCost))
            {
                Debug.LogError(
                    "[ManufacturingManager] Failed to rollback resources after job creation failed.",
                    this);
            }

            return false;
        }

        dataManager.MarkDirty();
        NotifyJobsChanged();
        return true;
    }

    /// <summary>
    /// 진행 중인 작업을 취소하고 아직 완성되지 않은 수량분의 시작 당시 비용을 환불합니다.
    /// </summary>
    public bool TryCancelJob(int slotIndex)
    {
        if (!TryGetContext(
                out ShelterSceneDataManager dataManager,
                out StorageFacility storage,
                out ManufacturingRuntimeData runtimeData)
            || !runtimeData.TryGetJob(slotIndex, out ManufacturingJobRuntimeData job))
        {
            return false;
        }

        if (!TryBuildRefundCost(job, out CostBundle refund)
            || !storage.TryAddResources(refund)
            || !runtimeData.RemoveJob(slotIndex))
        {
            return false;
        }

        dataManager.MarkDirty();
        NotifyJobsChanged();
        return true;
    }

    /// <summary>현재 제조 헬퍼 후보를 호출자가 제공한 목록에 채웁니다.</summary>
    public void FillHelperCandidates(List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (CanAssignHelper(character))
                results.Add(character);
        }
    }

    /// <summary>현재 제조실에 배치된 헬퍼를 호출자가 제공한 목록에 채웁니다.</summary>
    public void FillAssignedHelpers(List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (IsAssignedHelper(character))
                results.Add(character);
        }
    }

    public bool CanAssignHelper(ShelterMemberRuntimeData character)
    {
        if (!m_isUnlocked
            || character == null
            || CurrentHelperCount >= HelperCapacity
            || character.IsDead
            || character.IsAssignedToFacility)
        {
            return false;
        }

        return character.InjuryState == PlayerInjuryState.Normal
            || character.InjuryState == PlayerInjuryState.Minor;
    }

    public bool TryAssignHelper(string runtimeId)
    {
        if (!TryGetCharacterManager(out CharacterManager manager)
            || !manager.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            return false;
        }

        if (IsAssignedHelper(character))
            return true;

        if (!CanAssignHelper(character))
            return false;

        return manager.TryAssignToFacility(
            runtimeId,
            FacilityId,
            roomId,
            CharacterAssignmentFilter.AvailableAlive,
            FacilityAssignmentKind.Staff,
            out _);
    }

    public bool TryReleaseHelper(string runtimeId)
    {
        if (!TryGetCharacterManager(out CharacterManager manager)
            || !manager.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character)
            || !IsAssignedHelper(character))
        {
            return false;
        }

        return manager.TryReleaseFromFacility(runtimeId, out _);
    }

    private void OnDayAdvanced(int previousDay, int nextDay)
    {
        if (!m_isUnlocked
            || !TryGetContext(
                out ShelterSceneDataManager dataManager,
                out StorageFacility storage,
                out ManufacturingRuntimeData runtimeData))
        {
            return;
        }

        int productivity = FinalProductivity;
        IReadOnlyList<ManufacturingJobRuntimeData> jobs = runtimeData.Jobs;
        bool changed = false;

        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            ManufacturingJobRuntimeData job = jobs[i];
            if (job.IsComplete)
            {
                runtimeData.RemoveJob(job.SlotIndex);
                changed = true;
                continue;
            }

            int newCompletedQuantity = PredictNewCompletedQuantity(job, productivity);
            if (newCompletedQuantity > 0
                && storage.GetItemQuantity(job.ResultItemDefinitionId)
                    > int.MaxValue - newCompletedQuantity)
            {
                Debug.LogError(
                    $"[ManufacturingManager] Item quantity overflow for '{job.ResultItemDefinitionId}'.",
                    this);
                continue;
            }

            int processedWorkBefore = job.ProcessedWork;
            int actualCompletedQuantity = job.ApplyWork(productivity);
            if (job.ProcessedWork == processedWorkBefore)
                continue;

            changed = true;
            if (actualCompletedQuantity <= 0 && !job.IsComplete)
                continue;

            if (actualCompletedQuantity > 0
                && !storage.TryAddItem(job.ResultItemDefinitionId, actualCompletedQuantity))
            {
                Debug.LogError(
                    $"[ManufacturingManager] Failed to store completed item '{job.ResultItemDefinitionId}'.",
                    this);
                continue;
            }

            if (job.IsComplete)
                runtimeData.RemoveJob(job.SlotIndex);
        }

        if (!changed)
            return;

        dataManager.MarkDirty();
        NotifyJobsChanged();
    }

    private void OnCharactersChanged()
    {
        HelpersChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private int CalculateFinalProductivity()
    {
        int total = baseProductivity + LevelProductivityBonus(CurrentLevel);
        if (!TryGetCharacterManager(out CharacterManager manager))
            return Mathf.Max(1, total);

        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (IsAssignedHelper(character))
                total += GetHelperProductivityBonus(character.Type);
        }

        return Mathf.Max(1, total);
    }

    private int CountAssignedHelpers()
    {
        if (!TryGetCharacterManager(out CharacterManager manager))
            return 0;

        int count = 0;
        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (IsAssignedHelper(character))
                count++;
        }

        return count;
    }

    private bool IsAssignedHelper(ShelterMemberRuntimeData character)
    {
        return character != null
            && character.AssignmentKind == FacilityAssignmentKind.Staff
            && string.Equals(
                character.AssignedFacilityId,
                FacilityId,
                StringComparison.Ordinal);
    }

    private int GetHelperProductivityBonus(NPCType type)
    {
        if (helperProductivityBonuses == null)
            return 0;

        for (int i = 0; i < helperProductivityBonuses.Length; i++)
        {
            if (helperProductivityBonuses[i].type == type)
                return Mathf.Max(0, helperProductivityBonuses[i].bonus);
        }

        return 0;
    }

    private int CraftingSlotsForLevel(int level)
    {
        return Mathf.Clamp(level + 1, 1, TotalCraftingSlotCount);
    }

    private int HelperSlotsForLevel(int level)
    {
        int displayLevel = Mathf.Clamp(level + 1, 1, 4);
        return displayLevel >= secondHelperSlotUnlockLevel ? 2 : 1;
    }

    private int LevelProductivityBonus(int level)
    {
        return level >= 2 ? level3ProductivityBonus : 0;
    }

    private static int PredictNewCompletedQuantity(
        ManufacturingJobRuntimeData job,
        int workAmount)
    {
        if (job == null || !job.IsValid || workAmount <= 0 || job.IsComplete)
            return 0;

        int completedBefore = job.CompletedQuantity;
        int appliedWork = Math.Min(workAmount, job.RemainingWork);
        int completedAfter = Math.Min(
            job.RequestedQuantity,
            (job.ProcessedWork + appliedWork) / job.UnitWorkSnapshot);
        return completedAfter - completedBefore;
    }

    private static bool TryBuildQuantityCost(
        CostBundle unitCost,
        int quantity,
        out CostBundle totalCost)
    {
        totalCost = new CostBundle();
        if (quantity <= 0)
            return false;

        if (unitCost == null || unitCost.IsFree)
            return true;

        Dictionary<CurrencyType, int> totals = new();
        foreach (CurrencyCost cost in unitCost.Costs)
        {
            if (!TryAccumulateCost(totals, cost.Type, cost.Amount, quantity))
                return false;
        }

        totalCost = CreateCostBundle(totals);
        return true;
    }

    private static bool TryBuildRefundCost(
        ManufacturingJobRuntimeData job,
        out CostBundle refund)
    {
        refund = new CostBundle();
        if (job == null || !job.IsValid)
            return false;

        Dictionary<CurrencyType, int> totals = new();
        foreach (ManufacturingMaterialCostSnapshot cost in job.UnitCostSnapshots)
        {
            if (cost != null
                && !TryAccumulateCost(
                    totals,
                    cost.Type,
                    cost.UnitAmount,
                    job.RemainingQuantity))
            {
                return false;
            }
        }

        refund = CreateCostBundle(totals);
        return true;
    }

    private static bool TryAccumulateCost(
        Dictionary<CurrencyType, int> totals,
        CurrencyType type,
        int unitAmount,
        int quantity)
    {
        if (unitAmount <= 0 || quantity <= 0)
            return true;

        long added = (long)unitAmount * quantity;
        totals.TryGetValue(type, out int current);
        long total = current + added;
        if (total > int.MaxValue)
            return false;

        totals[type] = (int)total;
        return true;
    }

    private static CostBundle CreateCostBundle(Dictionary<CurrencyType, int> totals)
    {
        CurrencyCost[] costs = new CurrencyCost[totals.Count];
        int index = 0;
        foreach (KeyValuePair<CurrencyType, int> total in totals)
            costs[index++] = new CurrencyCost(total.Key, total.Value);

        return new CostBundle(costs);
    }

    private void RefreshLevelVisuals()
    {
        if (levelVisuals == null)
            return;

        if (m_isUnlocked)
            levelVisuals.ShowLevel(CurrentLevel);
        else
            levelVisuals.HideAll();
    }

    private void NotifyJobsChanged()
    {
        JobsChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private void NotifyStateChanged()
    {
        HelpersChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private bool TryGetContext(
        out ShelterSceneDataManager dataManager,
        out StorageFacility storage,
        out ManufacturingRuntimeData runtimeData)
    {
        dataManager = CacheShelterDataManager();
        storage = dataManager?.Storage;
        runtimeData = dataManager?.Manufacturing;
        if (dataManager != null && storage != null && runtimeData != null)
            return true;

        Debug.LogWarning("[ManufacturingManager] Shelter manufacturing context is not available.", this);
        return false;
    }

    private bool TryGetManufacturingData(out ManufacturingRuntimeData runtimeData)
    {
        ShelterSceneDataManager dataManager = CacheShelterDataManager();
        runtimeData = dataManager?.Manufacturing;
        return runtimeData != null;
    }

    private bool TryGetCharacterManager(out CharacterManager manager)
    {
        manager = CacheCharacterManager();
        return manager != null;
    }

    private void CacheDependencies()
    {
        CacheShelterDataManager();
        CacheCharacterManager();
    }

    private ShelterSceneDataManager CacheShelterDataManager()
    {
        if (shelterDataManager == null)
            shelterDataManager = ShelterSceneDataManager.Instance;

        if (shelterDataManager == null)
            shelterDataManager = FindFirstObjectByType<ShelterSceneDataManager>();

        return shelterDataManager;
    }

    private CharacterManager CacheCharacterManager()
    {
        if (characterManager == null)
            characterManager = CharacterManager.Instance;

        if (characterManager == null)
            characterManager = FindFirstObjectByType<CharacterManager>();

        return characterManager;
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    [Serializable]
    private sealed class UpgradeCostEntry
    {
        public CurrencyType type = default;
        [Min(0)] public int amount = 0;
    }

    [Serializable]
    private sealed class UpgradeCostTier
    {
        public UpgradeCostEntry[] entries = Array.Empty<UpgradeCostEntry>();
    }

    [Serializable]
    private struct HelperProductivityBonus
    {
        public NPCType type;
        [Min(0)] public int bonus;
    }
}
