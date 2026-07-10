using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

/// <summary>
/// 의료 시설의 환자 치료, 헬퍼 배치, 시설 업그레이드 반영을 담당
/// </summary>
/// <remarks>
/// 시설 해금/레벨의 진실원천은 <see cref="FacilityManager"/>이며, 이 컴포넌트는 의료 시설의 표시와 치료 진행 상태를 관리
/// </remarks>
public class MedicalManager : MonoBehaviour, IFacilityUpgradeable, IInjuryThresholdModifier
{
    private const int MaxLevelIndex = 3; // 레벨 4단계 (인덱스 0,1,2,3)

    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Character Access")]
    [SerializeField] private CharacterManager characterManager;

    [Header("Level Visuals")]
    [SerializeField] private FacilityLevelVisuals levelVisuals;

    [Header("Upgrade Cost (레벨 i → i+1, 시설이 자기 비용을 소유)")]
    [SerializeField] private UpgradeCostTier[] upgradeCosts; // 길이 = 최대 업그레이드 횟수(MaxLevelIndex)

    [Header("Recovery")]
    [SerializeField] private int baseRecoveryPerDay = 5; // 일일 기본 회복 %
    [FormerlySerializedAs("staffHealBonuses")]
    [SerializeField] private HelperRecoveryBonus[] helperRecoveryBonuses = new HelperRecoveryBonus[]
    {
        new HelperRecoveryBonus { type = NPCType.Tanker, bonusPercent = 1 },
        new HelperRecoveryBonus { type = NPCType.Healer, bonusPercent = 2 },
        new HelperRecoveryBonus { type = NPCType.Dealer, bonusPercent = 1 }
    };

    private readonly List<MedicalTreatment> patientTreatments = new List<MedicalTreatment>();
    private readonly List<PatientStatus> patientStatuses = new List<PatientStatus>();
    private readonly List<NPCRuntimeData> helpers = new List<NPCRuntimeData>();
    private bool m_isUnlocked = true; // FacilityManager가 세이브 기준으로 덮어씀(의료시설 기본 해금)

    /// <summary>헬퍼가 새로 배치됐을 때 발생</summary>
    public event System.Action<NPCRuntimeData> OnHelperAssigned;

    /// <summary>헬퍼 배치가 해제됐을 때 발생</summary>
    public event System.Action<NPCRuntimeData> OnHelperReleased;

    /// <summary>환자 치료가 완료되어 슬롯에서 제거됐을 때 발생</summary>
    public event System.Action<NPCRuntimeData> OnPatientHealed;

    /// <summary>
    /// 환자 슬롯 표시를 다시 그려야 할 때 발생
    /// </summary>
    public event System.Action OnPatientSlotsChanged;

    /// <summary>현재 치료 중인 환자 표시 상태 목록</summary>
    public IReadOnlyList<PatientStatus> PatientStatuses
    {
        get
        {
            RefreshPatientStatuses();
            return patientStatuses;
        }
    }

    /// <summary>현재 치료 중인 환자 수</summary>
    public int CurrentPatientCount => patientTreatments.Count;

    /// <summary>현재 레벨에서 수용 가능한 최대 환자 수</summary>
    public int MaxPatientCount => PatientCapacity;

    /// <summary>현재 업그레이드 레벨 기준 환자 슬롯 수</summary>
    public int PatientCapacity => PatientSlotsForLevel(CurrentLevel);

    /// <summary>최대 업그레이드 레벨에서 가능한 환자 슬롯 수</summary>
    public int MaxPatientCapacity => PatientSlotsForLevel(MaxLevelIndex);

    /// <summary>현재 해금된 환자 슬롯 수</summary>
    public int UnlockedPatientSlotCount => PatientCapacity;

    /// <summary>아직 잠겨 있는 환자 슬롯 수</summary>
    public int LockedPatientSlotCount => MaxPatientCapacity - PatientCapacity;

    /// <summary>현재 배치된 헬퍼 수</summary>
    public int CurrentHelperCount => helpers.Count;

    /// <summary>현재 레벨에서 배치 가능한 최대 헬퍼 수</summary>
    public int MaxHelperCount => HelperCapacity;

    /// <summary>현재 업그레이드 레벨 기준 헬퍼 슬롯 수</summary>
    public int HelperCapacity => HelperSlotsForLevel(CurrentLevel);

    /// <summary>환자 슬롯 업그레이드 레벨</summary>
    public int PatientUpgrade => CurrentLevel;

    /// <summary>의료 시설 업그레이드 레벨</summary>
    public int UpgradeLevel => CurrentLevel;

    /// <summary>의료 시설의 최대 업그레이드 레벨</summary>
    public int MaxUpgradeLevel => MaxLevelIndex;

    // 레벨은 FacilityManager(진실원천)에서 읽는다 — MedicalManager는 캐시하지 않는다.
    private int CurrentLevel =>
        FacilityManager.Instance != null ? FacilityManager.Instance.GetUpgradeLevel(FacilityId) : 0;

    /// <summary>시설 정의 또는 대체값으로 결정한 의료 시설 ID</summary>
    public string FacilityId
    {
        //fallbackFacilityId 나중에 통일 필요( 방지용 ID임 이건 )
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;
            return fallbackFacilityId;
        }
    }

    private void Awake()
    {
        CacheCharacterManager();
    }

    private void OnValidate()
    {
        baseRecoveryPerDay = Mathf.Max(1, baseRecoveryPerDay);
    }

    private void Start()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced += OnDayAdvanced;

        // 등록 즉시 FacilityManager가 세이브 기준 해금/레벨을 이 시설에 반영한다.
        FacilityManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced -= OnDayAdvanced;

        FacilityManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// <see cref="FacilityManager"/>가 레벨 변경 후 호출하는 시설 반영 콜백
    /// </summary>
    /// <param name="level">새 업그레이드 레벨. 실제 값은 <see cref="FacilityManager"/> 기준</param>
    public void ApplyUpgradeLevel(int level)
    {
        RefreshLevelVisuals();
        NotifyPatientSlotsChanged();
    }

    /// <summary>
    /// <see cref="FacilityManager"/>가 저장 데이터에서 복원한 시설 해금 상태를 반영
    /// </summary>
    /// <param name="isUnlocked">시설 해금 여부</param>
    public void ApplyUnlockState(bool isUnlocked)
    {
        m_isUnlocked = isUnlocked;
        RefreshLevelVisuals();
    }

    // 현재 해금/레벨 상태를 건물 비주얼에 반영한다(잠금이면 전부 숨김).
    private void RefreshLevelVisuals()
    {
        if (levelVisuals == null)
            return;

        if (m_isUnlocked)
            levelVisuals.ShowLevel(CurrentLevel);
        else
            levelVisuals.HideAll();
    }

    /// <summary>
    /// 현재 레벨에서 다음 레벨로 올릴 때 필요한 비용을 반환
    /// </summary>
    /// <param name="currentLevel">비용을 조회할 현재 업그레이드 레벨</param>
    /// <returns>해당 레벨의 업그레이드 비용 묶음 범위 밖이면 무료 묶음</returns>
    public CostBundle GetUpgradeCost(int currentLevel)
    {
        if (upgradeCosts == null || currentLevel < 0 || currentLevel >= upgradeCosts.Length)
            return new CostBundle();

        UpgradeCostEntry[] entries = upgradeCosts[currentLevel].entries;
        if (entries == null || entries.Length == 0)
            return new CostBundle();

        CurrencyCost[] costs = new CurrencyCost[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            costs[i] = new CurrencyCost(entries[i].type, entries[i].amount);

        return new CostBundle(costs);
    }

    /// <summary>
    /// 자원 이외의 업그레이드 조건을 검사
    /// </summary>
    /// <param name="currentLevel">검사할 현재 업그레이드 레벨</param>
    /// <returns>현재는 별도 조건이 없으므로 항상 <c>true</c></returns>
    public bool AreUpgradeRequirementsMet(int currentLevel) => true;

    // ── 부상 임계 완화 (IInjuryThresholdModifier) ──
    // 경계별 임계 완화 기여(레벨 기준). FacilityManager가 집계해 NPC 부상상태 평가 함수에 넘긴다.
    // 값 계약: 정상/경상/중상 상한을 각각 델타만큼 완화. 잠김이면 None.
    public InjuryThresholdRelief GetThresholdRelief()
        => m_isUnlocked ? InjuryThresholdReliefForLevel(CurrentLevel) : InjuryThresholdRelief.None;

    private static InjuryThresholdRelief InjuryThresholdReliefForLevel(int level) => level switch
    {
        // Lv.4(index 3): 정상/경상/중상 상한 각각 완화 — 플레이스홀더, 경계별로 다르게 조정 가능
        3 => new InjuryThresholdRelief(10, 10, 10),
        _ => InjuryThresholdRelief.None,
    };

    /// <summary>
    /// 치료 대상 NPC 후보 채우기
    /// </summary>
    /// <param name="results">후보 결과 목록. 호출 시 기존 내용 비움</param>
    public void FillPatientCandidates(List<NPCRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (NPCRuntimeData character in manager.Characters)
        {
            if (CanAssignPatient(character))
                results.Add(character);
        }
    }

    /// <summary>
    /// 지정한 NPC가 환자로 배치 가능한지 검사
    /// </summary>
    /// <param name="character">검사할 NPC 런타임 데이터</param>
    /// <returns>환자로 배치 가능하면 <c>true</c></returns>
    public bool CanAssignPatient(NPCRuntimeData character)
    {
        if (character == null)
            return false;

        if (patientTreatments.Count >= PatientCapacity)
            return false;

        if (FindPatientSlotIndex(character) >= 0)
            return false;

        // 완치(Healthy)면 치료 불필요 (enum 기준).
        if (character.GetCurrentInjuryState() == NPCInjuryState.Healthy)
            return false;

        return !character.GetIsAssignedToShelter();
    }

    /// <summary>
    /// 지정한 NPC를 환자로 배치
    /// </summary>
    /// <param name="target">배치할 NPC 런타임 데이터</param>
    /// <returns>배치에 성공했거나 이미 배치되어 있으면 <c>true</c></returns>
    public bool TryAssignPatient(NPCRuntimeData target)
    {
        return target != null && TryAssignPatient(target.DefinitionId);
    }

    /// <summary>
    /// NPC 식별자로 환자를 배치
    /// </summary>
    /// <param name="definitionId">배치할 NPC 정의 ID</param>
    /// <returns>배치에 성공했거나 이미 배치되어 있으면 <c>true</c></returns>
    public bool TryAssignPatient(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return false;
        //몇번째 슬롯에 있는지(슬롯에 없는 id면 -1 return)
        if (FindPatientSlotIndex(definitionId) >= 0) return true;

        //목록에 있는 숫자가 최대치 보다 높을경우 오류상태
        if (patientTreatments.Count >= PatientCapacity) return false;

        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        //셸터 데이터에서 NPC를 관리하는 CharacterManager로 부터 데이터를 가져오는 함수
        if (!manager.TryGetCharacter(definitionId, out NPCRuntimeData target))
            return false;

        if (!CanAssignPatient(target))
            return false;

        if (!manager.TryAssignToFacility(
                definitionId,
                FacilityId,
                roomId,
                CharacterAssignmentFilter.AvailableAlive,
                FacilityAssignmentKind.Patient,
                out target,
                out _))
        {
            return false;
        }

        patientTreatments.Add(new MedicalTreatment(target, GetDailyRecovery()));
        NotifyPatientSlotsChanged();
        return true;
    }

    /// <summary>
    /// 지정한 NPC의 환자 배치를 해제
    /// </summary>
    /// <param name="target">해제할 NPC 런타임 데이터</param>
    /// <returns>실제로 해제됐으면 <c>true</c></returns>
    public bool TryReleasePatient(NPCRuntimeData target)
    {
        return target != null && TryReleasePatient(target.DefinitionId);
    }

    /// <summary>
    /// NPC 식별자로 환자 배치를 해제
    /// </summary>
    /// <param name="definitionId">해제할 NPC 정의 ID</param>
    /// <returns>실제로 해제됐으면 <c>true</c></returns>
    public bool TryReleasePatient(string definitionId)
    {
        int slotIndex = FindPatientSlotIndex(definitionId);
        if (slotIndex < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        MedicalTreatment treatment = patientTreatments[slotIndex];
        if (!manager.TryReleaseFromFacility(treatment.Patient.DefinitionId, out _))
            return false;

        // 중도 해제 → 현재 게이지 기준으로 부상상태 갱신
        manager.TryRefreshInjuryState(treatment.Patient.DefinitionId, out _);
        patientTreatments.RemoveAt(slotIndex);
        NotifyPatientSlotsChanged();
        return true;
    }

    /// <summary>
    /// 지정한 환자의 남은 치료 일수를 반환
    /// </summary>
    /// <param name="patient">조회할 환자 NPC</param>
    /// <returns>치료 중이면 남은 일수, 치료 중이 아니면 0</returns>
    public int GetPatientHealDaysRemaining(NPCRuntimeData patient)
    {
        int slotIndex = FindPatientSlotIndex(patient);
        return slotIndex >= 0 ? patientTreatments[slotIndex].RemainingDays : 0;
    }

    /// <summary>
    /// 의료 헬퍼 NPC 후보 채우기
    /// </summary>
    /// <param name="results">후보 결과 목록. 호출 시 기존 내용 비움</param>
    public void FillHelperCandidates(List<NPCRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (NPCRuntimeData character in manager.Characters)
        {
            if (CanAssignHelper(character))
                results.Add(character);
        }
    }

    /// <summary>
    /// 지정한 NPC가 의료 헬퍼로 배치 가능한지 검사
    /// </summary>
    /// <param name="character">검사할 NPC 런타임 데이터</param>
    /// <returns>헬퍼로 배치 가능하면 <c>true</c></returns>
    public bool CanAssignHelper(NPCRuntimeData character)
    {
        if (character == null)
            return false;

        if (helpers.Count >= HelperCapacity)
            return false;

        if (FindHelperIndex(character) >= 0)
            return false;

        // 도우미는 건강 또는 경상만 가능 (중상·위독 제외).
        NPCInjuryState state = character.GetCurrentInjuryState();
        if (state != NPCInjuryState.Healthy && state != NPCInjuryState.LightInjury)
            return false;

        return !character.GetIsAssignedToShelter();
    }

    /// <summary>
    /// 지정한 NPC를 의료 헬퍼로 배치
    /// </summary>
    /// <param name="target">배치할 NPC 런타임 데이터</param>
    /// <returns>배치에 성공했거나 이미 배치되어 있으면 <c>true</c></returns>
    public bool TryAssignHelper(NPCRuntimeData target)
    {
        return target != null && TryAssignHelper(target.DefinitionId);
    }

    /// <summary>
    /// NPC 식별자로 의료 헬퍼를 배치
    /// </summary>
    /// <param name="definitionId">배치할 NPC 정의 ID</param>
    /// <returns>배치에 성공했거나 이미 배치되어 있으면 <c>true</c></returns>
    public bool TryAssignHelper(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return false;
        if (FindHelperIndex(definitionId) >= 0) return true;

        if (helpers.Count >= HelperCapacity) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        if (!manager.TryGetCharacter(definitionId, out NPCRuntimeData target))
            return false;

        if (!CanAssignHelper(target))
            return false;

        if (!manager.TryAssignToFacility(
                definitionId,
                FacilityId,
                roomId,
                CharacterAssignmentFilter.AvailableAlive,
                FacilityAssignmentKind.Staff,
                out target,
                out _))
        {
            return false;
        }

        helpers.Add(target);
        RecalculateAllTreatmentPlans();
        OnHelperAssigned?.Invoke(target);
        NotifyPatientSlotsChanged();
        return true;
    }

    /// <summary>
    /// 지정한 NPC의 헬퍼 배치를 해제
    /// </summary>
    /// <param name="target">해제할 NPC 런타임 데이터</param>
    /// <returns>실제로 해제됐으면 <c>true</c></returns>
    public bool TryReleaseHelper(NPCRuntimeData target)
    {
        return target != null && TryReleaseHelper(target.DefinitionId);
    }

    /// <summary>
    /// NPC 식별자로 헬퍼 배치를 해제
    /// </summary>
    /// <param name="definitionId">해제할 NPC 정의 ID</param>
    /// <returns>실제로 해제됐으면 <c>true</c></returns>
    public bool TryReleaseHelper(string definitionId)
    {
        int index = FindHelperIndex(definitionId);
        if (index < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        NPCRuntimeData helper = helpers[index];
        if (!manager.TryReleaseFromFacility(helper.DefinitionId, out _))
            return false;

        helpers.RemoveAt(index);
        RecalculateAllTreatmentPlans();
        OnHelperReleased?.Invoke(helper);
        NotifyPatientSlotsChanged();
        return true;
    }

    private int FindHelperIndex(NPCRuntimeData helper)
    {
        return helper == null ? -1 : FindHelperIndex(helper.DefinitionId);
    }

    private int FindHelperIndex(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return -1;

        string normalizedDefinitionId = definitionId.Trim();
        for (int i = 0; i < helpers.Count; i++)
        {
            NPCRuntimeData helper = helpers[i];
            if (helper != null && helper.DefinitionId == normalizedDefinitionId)
                return i;
        }

        return -1;
    }

    private void OnDayAdvanced(int prev, int next)
    {
        if (patientTreatments.Count == 0)
            return;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        // 다중일 스킵은 현재 스코프 밖 — 하루 단위로만 진행한다.
        bool changed = false;
        for (int i = patientTreatments.Count - 1; i >= 0; i--)
        {
            MedicalTreatment treatment = patientTreatments[i];
            NPCRuntimeData patient = treatment.Patient;

            float amount = treatment.ConsumeDailyRecovery();
            manager.TrySetInjuryGauge(patient.DefinitionId, patient.InjuryGauge + amount, out _);
            // [임시] 게이지 세터가 파생 부상상태(enum)를 자동 갱신하지 않아, 매일 게이지 반영 후 상태를 명시 재산출한다.
            //  → 추후 NPC 체력 컴포넌트가 게이지 변경 시 상태를 함께 갱신하면 이 호출은 제거.
            manager.TryRefreshInjuryState(patient.DefinitionId, out _);
            changed = true;

            // 완치 판정은 게이지 기준(진실원천). 아이템 등 치료 외 경로로 게이지가 차도 즉시 완치된다.
            if (patient.InjuryGauge >= patient.MaxInjuryGauge)
                CompleteHealing(i);
        }

        if (changed)
            NotifyPatientSlotsChanged();
    }

    private void CompleteHealing(int slotIndex)
    {
        MedicalTreatment treatment = patientTreatments[slotIndex];
        NPCRuntimeData patient = treatment.Patient;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        if (!manager.TryCompleteRecovery(patient.DefinitionId, out _))
            return;

        patientTreatments.RemoveAt(slotIndex);
        OnPatientHealed?.Invoke(patient);
    }

    // 일일 회복량 = 기본 + 배치된 헬퍼들의 타입별 보너스 합 (게이지 %/일).
    private float GetDailyRecovery()
    {
        int bonus = 0;
        foreach (NPCRuntimeData helper in helpers)
            bonus += GetHelperBonus(helper.Type);
        return Mathf.Max(1, baseRecoveryPerDay + bonus);
    }

    private int GetHelperBonus(NPCType type)
    {
        foreach (var b in helperRecoveryBonuses)
            if (b.type == type) return b.bonusPercent;
        return 0;
    }

    // 헬퍼 배치/해제로 회복률이 바뀌면 활성 환자 계획을 현재 게이지 기준으로 재산출한다.
    private void RecalculateAllTreatmentPlans()
    {
        float daily = GetDailyRecovery();
        for (int i = 0; i < patientTreatments.Count; i++)
            patientTreatments[i].Recalculate(daily);
    }

    private int FindPatientSlotIndex(NPCRuntimeData patient)
    {
        if (patient == null)
            return -1;

        return FindPatientSlotIndex(patient.DefinitionId);
    }

    private int FindPatientSlotIndex(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return -1;

        string normalizedDefinitionId = definitionId.Trim();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            if (treatment.Patient != null && treatment.Patient.DefinitionId == normalizedDefinitionId)
                return i;
        }

        return -1;
    }

    private bool TryGetCharacterManager(out CharacterManager manager)
    {
        manager = CacheCharacterManager();
        if (manager != null)
            return true;

        Debug.LogWarning("[MedicalManager] CharacterManager is not available.", this);
        return false;
    }

    private CharacterManager CacheCharacterManager()
    {
        if (characterManager == null)
            characterManager = CharacterManager.Instance;

        if (characterManager == null)
            characterManager = FindFirstObjectByType<CharacterManager>();

        return characterManager;
    }

    // 레벨별 효과는 시설 특수 정보이므로 코드에 하드코딩(절대값). 확장 = case 추가 + MaxLevelIndex.
    private static int PatientSlotsForLevel(int level) => level switch
    {
        0 => 1,
        1 => 2,
        2 => 3,
        3 => 4,
        _ => 1
    };

    private static int HelperSlotsForLevel(int level) => level switch
    {
        0 => 1,
        1 => 1,
        2 => 2,
        3 => 2,
        _ => 1
    };

    // 다음 레벨이 제공하는 기능 표시 줄들. 값이 바뀌는 항목만 노출(shown=actual — 슬롯 함수에서 파생).
    public System.Collections.Generic.IReadOnlyList<FacilityFeatureLine> GetUpgradeFeatureLines(int currentLevel)
    {
        var lines = new System.Collections.Generic.List<FacilityFeatureLine>();
        int next = currentLevel + 1;
        if (next > MaxLevelIndex)
            return lines;

        if (PatientSlotsForLevel(next) != PatientSlotsForLevel(currentLevel))
            lines.Add(new FacilityFeatureLine("환자 슬롯",
                $"{PatientSlotsForLevel(currentLevel)} → {PatientSlotsForLevel(next)}"));

        if (HelperSlotsForLevel(next) != HelperSlotsForLevel(currentLevel))
            lines.Add(new FacilityFeatureLine("헬퍼 슬롯",
                $"{HelperSlotsForLevel(currentLevel)} → {HelperSlotsForLevel(next)}"));

        return lines;
    }

    private void NotifyPatientSlotsChanged()
    {
        RefreshPatientStatuses();
        OnPatientSlotsChanged?.Invoke();
    }

    /// <summary>
    /// 외부 노출용 투영본인 patientStatuses 삭제 및 재생성( 원본에서 매번 재생성 함 )
    /// </summary>
    private void RefreshPatientStatuses()
    {
        patientStatuses.Clear();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            patientStatuses.Add(new PatientStatus(treatment.Patient, treatment.RemainingDays, treatment.TotalDays));
        }
    }

    // 업그레이드 비용 데이터(인스펙터 편집용). 한 단계(레벨 i→i+1)에서 요구하는 자원들.
    [System.Serializable]
    private struct UpgradeCostEntry
    {
        public CurrencyType type;
        public int amount;
    }

    [System.Serializable]
    private struct UpgradeCostTier
    {
        public UpgradeCostEntry[] entries;
    }

    private sealed class MedicalTreatment
    {
        public NPCRuntimeData Patient { get; }
        public int RemainingDays { get; private set; }
        public int TotalDays { get; private set; }

        private readonly float maxGauge;
        private float dailyRecovery;
        private float nextRecoveryAmount;

        public MedicalTreatment(NPCRuntimeData patient, float dailyRecovery)
        {
            Patient = patient;
            maxGauge = patient.MaxInjuryGauge;
            Recalculate(dailyRecovery);
        }

        // 현재 게이지 기준으로 치료 계획을 (재)산출한다. 계산은 TreatmentDurationCalculator에 위임한다.
        public void Recalculate(float daily)
        {
            TreatmentPlan plan = TreatmentDurationCalculator.Calculate(Patient.InjuryGauge, maxGauge, daily);
            dailyRecovery = plan.DailyRecovery;
            TotalDays = plan.TotalDays;
            RemainingDays = plan.TotalDays;
            nextRecoveryAmount = plan.FirstTickRecovery;
        }

        // 이번 날 회복량을 반환하고 남은 일수/다음 회복량을 진행시킨다.
        public float ConsumeDailyRecovery()
        {
            float amount = nextRecoveryAmount;
            nextRecoveryAmount = dailyRecovery;
            RemainingDays = Mathf.Max(0, RemainingDays - 1);
            return amount;
        }
    }
}


public readonly struct PatientStatus
{
    /// <summary>
    /// 의료 UI에 표시할 환자 상태 투영본을 생성
    /// </summary>
    /// <param name="patient">표시할 환자 NPC</param>
    /// <param name="remainingDays">남은 치료 일수</param>
    /// <param name="totalDays">총 치료 일수</param>
    public PatientStatus(NPCRuntimeData patient, int remainingDays, int totalDays)
    {
        Patient = patient;
        RemainingDays = remainingDays;
        TotalDays = totalDays;
    }

    /// <summary>표시 대상 환자 NPC</summary>
    public NPCRuntimeData Patient { get; }

    /// <summary>남은 치료 일수</summary>
    public int RemainingDays { get; }

    /// <summary>처음 산출된 총 치료 일수</summary>
    public int TotalDays { get; }

    /// <summary>현재 환자 부상 게이지</summary>
    public float InjuryGauge => Patient != null ? Patient.InjuryGauge : 0f;

    /// <summary>환자 부상 게이지의 최대값</summary>
    public float MaxInjuryGauge => Patient != null ? Patient.MaxInjuryGauge : 1f;

    /// <summary>UI 게이지 표시용 0~1 정규화 값</summary>
    public float GaugeNormalized => MaxInjuryGauge > 0f ? Mathf.Clamp01(InjuryGauge / MaxInjuryGauge) : 0f;

    /// <summary>현재 환자 부상 상태</summary>
    public NPCInjuryState InjuryState => Patient != null ? Patient.GetCurrentInjuryState() : NPCInjuryState.Healthy;

    /// <summary>UI에 표시할 환자 이름</summary>
    public string DisplayName => Patient != null
        ? (Patient.NPCData != null ? Patient.NPCData.name : Patient.DefinitionId)
        : string.Empty;
}


/// <summary>
/// NPC 타입별 의료 헬퍼 일일 회복 보너스 설정
/// </summary>
[System.Serializable]
public struct HelperRecoveryBonus
{
    /// <summary>보너스를 적용할 NPC 타입</summary>
    public NPCType type;

    /// <summary>일일 회복량에 더할 보너스 수치</summary>
    [FormerlySerializedAs("daysReduction")]
    public int bonusPercent;
}
