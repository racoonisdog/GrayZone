using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 씬 사이에서 유지되어야 하는 게임 런타임 정본을 평탄 필드로 소유하는 전역 데이터 매니저입니다.
/// 각 씬과 저장 시스템에는 소비 목적에 맞는 독립 패킷을 생성해 전달합니다.
/// </summary>
[DefaultExecutionOrder(-300)]
public class GameDataManager : MonoBehaviour
{
    /// <summary>현재 GameManager 자식에서 활성화된 전역 데이터 매니저 인스턴스입니다.</summary>
    public static GameDataManager Instance { get; private set; }

    [Header("공용 진행 정본")]
    [Tooltip("마지막으로 진행한 스테이지의 영속 ID입니다.")]
    [SerializeField] private string lastStageId = string.Empty;
    [Tooltip("현재 셸터 안정도입니다. 0~100 범위로 유지됩니다.")]
    [Range(0, 100)][SerializeField] private int shelterStability = 100;
    [Tooltip("현재 셸터 진행 일차입니다. 1 이상으로 유지됩니다.")]
    [Min(1)][SerializeField] private int currentDay = 1;
    [Tooltip("현재 보유한 플레이어블 캐릭터 수입니다.")]
    [Min(0)][SerializeField] private int playableCharacterCount;
    [Tooltip("현재 보유한 비플레이어 NPC 수입니다.")]
    [Min(0)][SerializeField] private int nonPlayableNpcCount;

    [Header("자원 정본")]
    [Tooltip("자원 종류별 현재 보유량입니다. 같은 종류는 런타임에 하나로 정규화됩니다.")]
    [SerializeField] private List<ResourceAmountState> resourceAmounts = new();

    [Header("보유 캐릭터 및 장비 정본")]
    [Tooltip("보유 캐릭터의 영속 상태와 캐릭터별 장착 총기 상태입니다.")]
    [SerializeField] private List<NPCRuntimeData> ownedCharacters = new();

    [Header("셸터 정본")]
    [Tooltip("현재 출전 대상으로 선택된 캐릭터 정의 ID 목록입니다. 최대 3명입니다.")]
    [FormerlySerializedAs("battleSquadNpcDefinitionIds")]
    [SerializeField] private List<string> playableSquadDefinitionIds = new();
    [Tooltip("시설별 해금 여부와 업그레이드 단계입니다.")]
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new();

    [Header("최근 배틀 정산 정본")]
    [Tooltip("마지막으로 정산 반영이 완료된 배틀 ID이며 중복 반영 방지 키로 사용합니다.")]
    [SerializeField] private string lastSettledBattleId = string.Empty;
    [Tooltip("최근 배틀이 진행된 스테이지 ID입니다.")]
    [SerializeField] private string lastBattleStageId = string.Empty;
    [Tooltip("최근 배틀의 최종 성공, 실패 또는 철수 결과입니다.")]
    [SerializeField] private BattleOutcome lastBattleOutcome;
    [Tooltip("최근 배틀이 종료된 직접적인 사유입니다.")]
    [SerializeField] private BattleEndReason lastBattleEndReason;
    [Tooltip("최근 배틀 종료 전에 임무 목표를 달성했는지 여부입니다.")]
    [SerializeField] private bool lastBattleMissionCompleted;
    [Tooltip("최근 배틀의 총 경과 시간입니다. 단위는 초입니다.")]
    [Min(0.0f)][SerializeField] private float lastBattleElapsedSeconds;
    [Tooltip("최근 배틀에서 스쿼드 전체가 확정한 적 처치 수입니다.")]
    [Min(0)][SerializeField] private int lastBattleTotalKillCount;
    [Tooltip("최근 배틀의 캐릭터별 최종 상태와 처치 결과입니다.")]
    [SerializeField] private List<BattleMemberResultData> lastBattleMemberResults = new();
    [Tooltip("최근 배틀에서 획득한 자원별 수량입니다.")]
    [SerializeField] private List<BattleResourceAmountData> lastBattleAcquiredResources = new();

    private ShelterDataManager activeShelterDataManager;

    /// <summary>현재 보유한 전체 캐릭터 수입니다.</summary>
    public int OwnedCharacterCount => ownedCharacters?.Count ?? 0;

    /// <summary>플레이어블 캐릭터와 비플레이어 NPC를 합한 전체 보유 수입니다.</summary>
    public int TotalOwnedCharacterCount => PlayableCharacterCount + NonPlayableNpcCount;

    /// <summary>현재 보유한 플레이어블 캐릭터 수입니다.</summary>
    public int PlayableCharacterCount => Mathf.Max(0, playableCharacterCount);

    /// <summary>현재 보유한 비플레이어 NPC 수입니다.</summary>
    public int NonPlayableNpcCount => Mathf.Max(0, nonPlayableNpcCount);

    /// <summary>0~100 범위로 보정된 현재 셸터 안정도입니다.</summary>
    public int ShelterStability => Mathf.Clamp(shelterStability, 0, 100);

    /// <summary>현재 셸터 진행 일차입니다.</summary>
    public int CurrentDay => Mathf.Max(1, currentDay);

    /// <summary>현재 씬의 ShelterDataManager가 등록되어 있는지 여부입니다.</summary>
    public bool HasActiveShelterDataManager => activeShelterDataManager != null;

    /// <summary>현재 세션 또는 저장 데이터에 반영된 최근 배틀 결과가 있는지 여부입니다.</summary>
    public bool HasLastBattleResult => !string.IsNullOrWhiteSpace(lastSettledBattleId);

    /// <summary>마지막으로 정산 반영이 완료된 배틀 ID입니다.</summary>
    public string LastSettledBattleId => lastSettledBattleId ?? string.Empty;

    /// <summary>중복 인스턴스를 거부하고 모든 평탄 정본 필드를 정규화합니다.</summary>
    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        EnsureRuntimeState();
        Instance = this;
    }

    /// <summary>현재 인스턴스가 파괴될 때 전역 접근자를 해제합니다.</summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>현재 셸터 씬의 작업 데이터 매니저를 동기화 대상으로 등록합니다.</summary>
    public void RegisterShelterDataManager(ShelterDataManager shelterDataManager)
    {
        if (shelterDataManager != null)
        {
            activeShelterDataManager = shelterDataManager;
        }
    }

    /// <summary>지정한 셸터 씬 데이터 매니저가 현재 등록 대상이면 연결을 해제합니다.</summary>
    public void UnregisterShelterDataManager(ShelterDataManager shelterDataManager)
    {
        if (activeShelterDataManager == shelterDataManager)
        {
            activeShelterDataManager = null;
        }
    }

    /// <summary>현재 등록된 셸터 씬의 기존 공용·셸터 작업 패킷을 전역 평탄 정본에 반영합니다.</summary>
    public bool SyncFromShelter()
    {
        if (activeShelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] Active ShelterDataManager is not registered.");
            return false;
        }

        return SyncFromShelter(activeShelterDataManager);
    }

    /// <summary>지정한 셸터 씬 데이터 매니저의 기존 공용·셸터 작업 패킷을 전역 평탄 정본에 반영합니다.</summary>
    public bool SyncFromShelter(ShelterDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterDataManager is null.");
            return false;
        }

        ApplySharedSnapshot(shelterDataManager.CreateSharedSnapshot());
        ApplyShelterSnapshot(shelterDataManager.CreateShelterSnapshot());
        return true;
    }

    /// <summary>현재 평탄 정본에서 셸터의 기존 공용 작업 패킷을 생성합니다.</summary>
    public SharedRuntimeData CreateSharedSnapshot()
    {
        EnsureRuntimeState();
        SharedRuntimeData packet = new SharedRuntimeData();
        packet.SetLastStageId(lastStageId);
        packet.SetShelterStability(shelterStability);
        packet.SetOwnedCharacterCounts(playableCharacterCount, nonPlayableNpcCount);

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource != null)
            {
                packet.Resources.SetAmount(resource.Type, resource.Amount);
            }
        }

        for (int i = 0; i < ownedCharacters.Count; i++)
        {
            if (ownedCharacters[i] != null)
            {
                packet.NpcRoster.Add(ownedCharacters[i].Clone());
            }
        }

        return packet;
    }

    /// <summary>현재 평탄 정본에서 셸터의 기존 전용 작업 패킷을 생성합니다.</summary>
    public ShelterRuntimeData CreateShelterSnapshot()
    {
        EnsureRuntimeState();
        ShelterRuntimeData packet = new ShelterRuntimeData();
        packet.ApplySavedState(currentDay, playableSquadDefinitionIds, facilityStates);
        return packet;
    }

    /// <summary>셸터의 기존 공용 작업 패킷을 깊은 복사해 전역 평탄 정본에 반영합니다.</summary>
    public void ApplySharedSnapshot(SharedRuntimeData packet)
    {
        if (packet == null)
        {
            Debug.LogWarning("[GameDataManager] SharedRuntimeData is null.");
            return;
        }

        packet.EnsureRuntimeContainers();
        lastStageId = packet.LastStageId;
        shelterStability = packet.ShelterStability;
        playableCharacterCount = packet.PlayableCharacterCount;
        nonPlayableNpcCount = packet.NonPlayableNpcCount;

        resourceAmounts = new List<ResourceAmountState>();
        foreach (KeyValuePair<CurrencyType, int> resource in packet.Resources.Amounts)
        {
            resourceAmounts.Add(new ResourceAmountState(resource.Key, resource.Value));
        }

        ownedCharacters = new List<NPCRuntimeData>();
        for (int i = 0; i < packet.NpcRoster.All.Count; i++)
        {
            NPCRuntimeData character = packet.NpcRoster.All[i];
            if (character != null)
            {
                ownedCharacters.Add(character.Clone());
            }
        }

        EnsureRuntimeState();
    }

    /// <summary>셸터의 기존 전용 작업 패킷을 깊은 복사해 전역 평탄 정본에 반영합니다.</summary>
    public void ApplyShelterSnapshot(ShelterRuntimeData packet)
    {
        if (packet == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterRuntimeData is null.");
            return;
        }

        packet.EnsureRuntimeContainers();
        currentDay = packet.CurrentDay;
        playableSquadDefinitionIds = new List<string>(packet.BattleSquadNpcDefinitionIds);
        facilityStates = CloneFacilityStates(packet.FacilityStates);
        EnsureRuntimeState();
    }

    /// <summary>지정한 영속 캐릭터 ID의 캐릭터·총기 스냅샷을 깊은 복사하여 반환합니다.</summary>
    public bool TryGetCharacterSnapshot(string definitionId, out CharacterSnapshotData snapshot)
    {
        snapshot = null;
        if (!TryGetOwnedCharacter(definitionId, out NPCRuntimeData npc))
        {
            return false;
        }

        snapshot = npc.Snapshot;
        return true;
    }

    /// <summary>셸터 또는 배틀 씬에서 받은 캐릭터·총기 스냅샷을 전역 캐릭터 정본에 반영합니다.</summary>
    public bool TryApplyCharacterSnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            return false;
        }

        if (!TryGetOwnedCharacter(snapshot.DefinitionId, out NPCRuntimeData npc))
        {
            Debug.LogWarning($"[GameDataManager] 캐릭터 스냅샷을 반영할 캐릭터를 찾지 못했습니다. definitionId={snapshot.DefinitionId}");
            return false;
        }

        return npc.ApplySnapshot(snapshot);
    }

    /// <summary>현재 평탄 정본에서 배틀 씬이 필요로 하는 출전 패킷을 생성합니다.</summary>
    public BattleEntryData CreateBattleEntryData(string battleId, string stageId, int randomSeed)
    {
        if (activeShelterDataManager != null)
        {
            SyncFromShelter(activeShelterDataManager);
        }

        EnsureRuntimeState();
        string resolvedBattleId = string.IsNullOrWhiteSpace(battleId)
            ? Guid.NewGuid().ToString("N")
            : battleId.Trim();
        string resolvedStageId = string.IsNullOrWhiteSpace(stageId)
            ? lastStageId
            : stageId.Trim();

        BattleEntryData entryData = new BattleEntryData(
            resolvedBattleId,
            resolvedStageId,
            randomSeed,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource != null)
            {
                entryData.AddStartingResource(resource.Type, resource.Amount);
            }
        }

        for (int i = 0; i < playableSquadDefinitionIds.Count; i++)
        {
            string definitionId = playableSquadDefinitionIds[i];
            if (!TryGetOwnedCharacter(definitionId, out NPCRuntimeData character))
            {
                Debug.LogWarning($"[GameDataManager] 출전 스쿼드 캐릭터를 찾지 못했습니다. definitionId={definitionId}");
                continue;
            }

            CharacterSnapshotData characterSnapshot = character.Snapshot;
            characterSnapshot.SetPlayerSquadMember(i == 0);
            entryData.AddMember(new BattleMemberEntryData(characterSnapshot));
        }

        return entryData;
    }

    /// <summary>
    /// 확정된 배틀 결과를 전역 평탄 정본에 한 번만 반영합니다.
    /// 성공 또는 탈출일 때만 획득 자원을 보존하며, 캐릭터 최종 상태는 모든 결과에 반영합니다.
    /// </summary>
    public bool ApplyBattleResult(BattleResultData resultData)
    {
        if (resultData == null || string.IsNullOrWhiteSpace(resultData.BattleId))
        {
            Debug.LogWarning("[GameDataManager] 유효한 BattleResultData가 필요합니다.");
            return false;
        }

        string battleId = resultData.BattleId.Trim();
        if (battleId == LastSettledBattleId)
        {
            return true;
        }

        if (ShouldApplyAcquiredResources(resultData.Outcome))
        {
            for (int i = 0; i < resultData.AcquiredResources.Count; i++)
            {
                BattleResourceAmountData resource = resultData.AcquiredResources[i];
                if (resource != null)
                {
                    AddResource(resource.Type, resource.Amount);
                }
            }
        }

        for (int i = 0; i < resultData.Members.Count; i++)
        {
            ApplyBattleMemberResult(resultData.Members[i]);
        }

        if (!string.IsNullOrWhiteSpace(resultData.StageId))
        {
            lastStageId = resultData.StageId.Trim();
        }

        ApplyLastBattleResult(resultData);

        if (activeShelterDataManager != null)
        {
            activeShelterDataManager.ApplySharedSnapshot(CreateSharedSnapshot());
        }

        return true;
    }

    /// <summary>최근 배틀 결과의 평탄 필드를 독립된 결과 패킷으로 조립해 반환합니다.</summary>
    public BattleResultData CreateLastBattleResultSnapshot()
    {
        if (!HasLastBattleResult)
        {
            return null;
        }

        return new BattleResultData(
            lastSettledBattleId,
            lastBattleStageId,
            lastBattleOutcome,
            lastBattleEndReason,
            lastBattleMissionCompleted,
            lastBattleElapsedSeconds,
            lastBattleTotalKillCount,
            lastBattleMemberResults,
            lastBattleAcquiredResources);
    }

    /// <summary>현재 전역 평탄 정본을 지정한 프로필의 독립된 저장 패킷으로 변환합니다.</summary>
    /// <remarks>활성 셸터 작업본은 호출 전에 <see cref="SyncFromShelter()"/>로 정본에 먼저 반영해야 합니다.</remarks>
    public SaveData CreateSaveData(string profileId)
    {
        EnsureRuntimeState();
        SaveData saveData = new SaveData
        {
            profileId = string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId.Trim(),
            shared = CreateSharedSaveData(),
            shelter = CreateShelterSaveData(),
            lastBattleResult = CreateLastBattleResultSnapshot()
        };

        saveData.MarkSavedNow();
        return saveData;
    }

    /// <summary>불러온 저장 패킷의 영속값을 전역 평탄 정본에 적용합니다.</summary>
    public void ApplySaveData(SaveData saveData)
    {
        if (saveData == null)
        {
            Debug.LogWarning("[GameDataManager] SaveData is null.");
            return;
        }

        ApplySharedSaveData(saveData.shared ?? new SaveData.SharedSaveData());
        ApplyShelterSaveData(saveData.shelter ?? new SaveData.ShelterSaveData());

        if (saveData.lastBattleResult != null
            && !string.IsNullOrWhiteSpace(saveData.lastBattleResult.BattleId))
        {
            ApplyLastBattleResult(saveData.lastBattleResult);
        }
        else
        {
            ClearLastBattleResult();
        }

        EnsureRuntimeState();
    }

    private void ApplyLastBattleResult(BattleResultData resultData)
    {
        lastSettledBattleId = resultData.BattleId.Trim();
        lastBattleStageId = resultData.StageId;
        lastBattleOutcome = resultData.Outcome;
        lastBattleEndReason = resultData.EndReason;
        lastBattleMissionCompleted = resultData.MissionCompleted;
        lastBattleElapsedSeconds = resultData.ElapsedSeconds;
        lastBattleTotalKillCount = resultData.TotalKillCount;
        lastBattleMemberResults = new List<BattleMemberResultData>();
        lastBattleAcquiredResources = new List<BattleResourceAmountData>();

        for (int i = 0; i < resultData.Members.Count; i++)
        {
            if (resultData.Members[i] != null)
            {
                lastBattleMemberResults.Add(resultData.Members[i].Clone());
            }
        }

        for (int i = 0; i < resultData.AcquiredResources.Count; i++)
        {
            if (resultData.AcquiredResources[i] != null)
            {
                lastBattleAcquiredResources.Add(resultData.AcquiredResources[i].Clone());
            }
        }
    }

    private void ClearLastBattleResult()
    {
        lastSettledBattleId = string.Empty;
        lastBattleStageId = string.Empty;
        lastBattleOutcome = default;
        lastBattleEndReason = BattleEndReason.None;
        lastBattleMissionCompleted = false;
        lastBattleElapsedSeconds = 0.0f;
        lastBattleTotalKillCount = 0;
        lastBattleMemberResults = new List<BattleMemberResultData>();
        lastBattleAcquiredResources = new List<BattleResourceAmountData>();
    }

    private SaveData.SharedSaveData CreateSharedSaveData()
    {
        SaveData.SharedSaveData saveData = new SaveData.SharedSaveData
        {
            lastStageId = lastStageId,
            shelterStability = ShelterStability,
            playableCharacterCount = PlayableCharacterCount,
            nonPlayableNpcCount = NonPlayableNpcCount
        };

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource != null)
            {
                saveData.resources.Add(new SaveData.ResourceAmountData
                {
                    type = resource.Type,
                    amount = resource.Amount
                });
            }
        }

        for (int i = 0; i < ownedCharacters.Count; i++)
        {
            SaveData.NpcSaveData npcSaveData = NpcSaveDataMapper.FromRuntime(ownedCharacters[i]);
            if (npcSaveData != null)
            {
                saveData.npcs.Add(npcSaveData);
            }
        }

        return saveData;
    }

    private SaveData.ShelterSaveData CreateShelterSaveData()
    {
        SaveData.ShelterSaveData saveData = new SaveData.ShelterSaveData
        {
            currentDay = CurrentDay,
            battleSquadNpcDefinitionIds = new List<string>(playableSquadDefinitionIds)
        };

        for (int i = 0; i < facilityStates.Count; i++)
        {
            SaveData.FacilitySaveData facilitySaveData = FacilitySaveDataMapper.FromRuntime(facilityStates[i]);
            if (facilitySaveData != null)
            {
                saveData.facilities.Add(facilitySaveData);
            }
        }

        return saveData;
    }

    private void ApplySharedSaveData(SaveData.SharedSaveData saveData)
    {
        lastStageId = saveData.lastStageId?.Trim() ?? string.Empty;
        shelterStability = Mathf.Clamp(saveData.shelterStability, 0, 100);
        playableCharacterCount = Mathf.Max(0, saveData.playableCharacterCount);
        nonPlayableNpcCount = Mathf.Max(0, saveData.nonPlayableNpcCount);
        resourceAmounts = new List<ResourceAmountState>();
        ownedCharacters = new List<NPCRuntimeData>();

        if (saveData.resources != null)
        {
            for (int i = 0; i < saveData.resources.Count; i++)
            {
                SaveData.ResourceAmountData resource = saveData.resources[i];
                if (resource != null)
                {
                    SetResourceAmount(resource.type, resource.amount);
                }
            }
        }

        if (saveData.npcs != null)
        {
            for (int i = 0; i < saveData.npcs.Count; i++)
            {
                NPCRuntimeData runtimeCharacter = NpcSaveDataMapper.ToRuntime(saveData.npcs[i]);
                if (runtimeCharacter != null && !ContainsOwnedCharacter(runtimeCharacter.DefinitionId))
                {
                    ownedCharacters.Add(runtimeCharacter);
                }
            }
        }
    }

    private void ApplyShelterSaveData(SaveData.ShelterSaveData saveData)
    {
        currentDay = Mathf.Max(1, saveData.currentDay);
        playableSquadDefinitionIds = NormalizeDefinitionIds(
            saveData.battleSquadNpcDefinitionIds,
            ShelterRuntimeData.MaxBattleSquadSize);
        facilityStates = new List<FacilityRuntimeState>();

        if (saveData.facilities == null)
        {
            return;
        }

        for (int i = 0; i < saveData.facilities.Count; i++)
        {
            FacilityRuntimeState state = FacilitySaveDataMapper.ToRuntime(saveData.facilities[i]);
            if (state != null)
            {
                facilityStates.Add(state);
            }
        }
    }

    private void ApplyBattleMemberResult(BattleMemberResultData memberResult)
    {
        CharacterSnapshotData snapshot = memberResult?.Snapshot;
        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            TryApplyCharacterSnapshot(snapshot);
        }
    }

    private bool TryGetOwnedCharacter(string definitionId, out NPCRuntimeData runtimeData)
    {
        runtimeData = null;
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return false;
        }

        string normalizedId = definitionId.Trim();
        for (int i = 0; i < ownedCharacters.Count; i++)
        {
            if (ownedCharacters[i] != null && ownedCharacters[i].DefinitionId == normalizedId)
            {
                runtimeData = ownedCharacters[i];
                return true;
            }
        }

        return false;
    }

    private bool ContainsOwnedCharacter(string definitionId)
    {
        return TryGetOwnedCharacter(definitionId, out _);
    }

    private int GetResourceAmount(CurrencyType type)
    {
        ResourceAmountState state = FindResource(type);
        return state?.Amount ?? 0;
    }

    private void SetResourceAmount(CurrencyType type, int amount)
    {
        ResourceAmountState state = FindResource(type);
        if (state == null)
        {
            resourceAmounts.Add(new ResourceAmountState(type, amount));
            return;
        }

        state.SetAmount(amount);
    }

    private void AddResource(CurrencyType type, int amount)
    {
        if (amount > 0)
        {
            SetResourceAmount(type, GetResourceAmount(type) + amount);
        }
    }

    private ResourceAmountState FindResource(CurrencyType type)
    {
        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            if (resourceAmounts[i] != null && resourceAmounts[i].Type == type)
            {
                return resourceAmounts[i];
            }
        }

        return null;
    }

    private void EnsureRuntimeState()
    {
        lastStageId ??= string.Empty;
        shelterStability = Mathf.Clamp(shelterStability, 0, 100);
        currentDay = Mathf.Max(1, currentDay);
        playableCharacterCount = Mathf.Max(0, playableCharacterCount);
        nonPlayableNpcCount = Mathf.Max(0, nonPlayableNpcCount);
        resourceAmounts ??= new List<ResourceAmountState>();
        ownedCharacters ??= new List<NPCRuntimeData>();
        playableSquadDefinitionIds ??= new List<string>();
        facilityStates ??= new List<FacilityRuntimeState>();
        lastBattleMemberResults ??= new List<BattleMemberResultData>();
        lastBattleAcquiredResources ??= new List<BattleResourceAmountData>();

        NormalizeResources();
        NormalizeOwnedCharacters();
        playableSquadDefinitionIds = NormalizeDefinitionIds(
            playableSquadDefinitionIds,
            ShelterRuntimeData.MaxBattleSquadSize);
        facilityStates = CloneFacilityStates(facilityStates);

        if (!HasLastBattleResult)
        {
            ClearLastBattleResult();
        }
    }

    private void NormalizeResources()
    {
        List<ResourceAmountState> normalized = new();
        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState source = resourceAmounts[i];
            if (source == null)
            {
                continue;
            }

            ResourceAmountState existing = null;
            for (int j = 0; j < normalized.Count; j++)
            {
                if (normalized[j].Type == source.Type)
                {
                    existing = normalized[j];
                    break;
                }
            }

            if (existing == null)
            {
                normalized.Add(source.Clone());
            }
            else
            {
                existing.SetAmount(existing.Amount + source.Amount);
            }
        }

        resourceAmounts = normalized;
    }

    private void NormalizeOwnedCharacters()
    {
        HashSet<string> definitionIds = new();
        List<NPCRuntimeData> normalized = new();
        for (int i = 0; i < ownedCharacters.Count; i++)
        {
            NPCRuntimeData character = ownedCharacters[i];
            if (character != null
                && !string.IsNullOrWhiteSpace(character.DefinitionId)
                && definitionIds.Add(character.DefinitionId))
            {
                normalized.Add(character);
            }
        }

        ownedCharacters = normalized;
    }

    private static bool ShouldApplyAcquiredResources(BattleOutcome outcome)
    {
        return outcome == BattleOutcome.Success || outcome == BattleOutcome.Evacuated;
    }

    private static List<string> NormalizeDefinitionIds(IEnumerable<string> source, int maximumCount)
    {
        List<string> normalized = new();
        if (source == null)
        {
            return normalized;
        }

        foreach (string definitionId in source)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                continue;
            }

            string normalizedId = definitionId.Trim();
            if (!normalized.Contains(normalizedId))
            {
                normalized.Add(normalizedId);
            }

            if (normalized.Count >= maximumCount)
            {
                break;
            }
        }

        return normalized;
    }

    private static List<FacilityRuntimeState> CloneFacilityStates(IEnumerable<FacilityRuntimeState> source)
    {
        List<FacilityRuntimeState> clone = new();
        if (source == null)
        {
            return clone;
        }

        HashSet<string> facilityIds = new();
        foreach (FacilityRuntimeState state in source)
        {
            if (state == null)
            {
                continue;
            }

            state.EnsureValid();
            if (!string.IsNullOrWhiteSpace(state.facilityId) && facilityIds.Add(state.facilityId))
            {
                clone.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
            }
        }

        return clone;
    }

    private bool TryRejectDuplicateOrInvalidRoot()
    {
        GameManager rootManager = GetComponentInParent<GameManager>();
        if (rootManager == null)
        {
            Debug.LogWarning("[GameDataManager] Parent GameManager not found. Destroying duplicate/orphan instance.");
            Destroy(gameObject);
            return true;
        }

        if (GameManager.Instance != null && rootManager != GameManager.Instance)
        {
            Destroy(gameObject);
            return true;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
