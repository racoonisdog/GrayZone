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
    [Header("자원 정본")]
    [Tooltip("자원 종류별 현재 보유량입니다. 같은 종류는 런타임에 하나로 정규화됩니다.")]
    [SerializeField] private List<ResourceAmountState> resourceAmounts = new();
    [SerializeField] private List<ItemStorageEntry> itemStorageEntries = new();

    [Header("보유 캐릭터 및 장비 정본")]
    [Tooltip("Battle과 Shelter가 공통으로 복사해 사용하는 캐릭터 스냅샷 정본입니다.")]
    [FormerlySerializedAs("ownedCharacters")]
    [SerializeField] private List<CharacterSnapshotData> characters = new();

    [Header("셸터 정본")]
    [Tooltip("현재 출전 대상으로 선택된 캐릭터 런타임 ID 목록입니다. 최대 3명입니다.")]
    [FormerlySerializedAs("battleSquadNpcDefinitionIds")]
    [FormerlySerializedAs("playableSquadDefinitionIds")]
    [SerializeField] private List<string> playableSquadRuntimeIds = new();
    [Tooltip("셸터 시설에 배치된 캐릭터의 셸터 전용 상태입니다.")]
    [SerializeField] private List<ShelterCharacterAssignmentData> shelterCharacterAssignments = new();
    [Tooltip("시설별 해금 여부와 업그레이드 단계입니다.")]
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new();
    [SerializeField] private ManufacturingRuntimeData manufacturing = new();

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

    private ShelterSceneDataManager activeShelterSceneDataManager;

    /// <summary>현재 보유한 전체 캐릭터 수입니다.</summary>
    public int CharacterCount => characters?.Count ?? 0;
    public IReadOnlyList<CharacterSnapshotData> Characters => characters;
    public IReadOnlyList<ItemStorageEntry> ItemStorageEntries => itemStorageEntries;

    /// <summary>플레이어블 캐릭터와 비플레이어 NPC를 합한 전체 보유 수입니다.</summary>
    public int TotalOwnedCharacterCount => CharacterCount;

    /// <summary>현재 보유한 플레이어블 캐릭터 수입니다.</summary>
    public int PlayableCharacterCount => CharacterCount;

    /// <summary>현재 보유한 비플레이어 NPC 수입니다.</summary>
    public int NonPlayableNpcCount => 0;

    /// <summary>0~100 범위로 보정된 현재 셸터 안정도입니다.</summary>
    public int ShelterStability => Mathf.Clamp(shelterStability, 0, 100);

    /// <summary>현재 셸터 진행 일차입니다.</summary>
    public int CurrentDay => Mathf.Max(1, currentDay);

    /// <summary>현재 씬의 ShelterSceneDataManager가 등록되어 있는지 여부입니다.</summary>
    public bool HasActiveShelterSceneDataManager => activeShelterSceneDataManager != null;

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
    public void RegisterShelterSceneDataManager(ShelterSceneDataManager shelterDataManager)
    {
        if (shelterDataManager != null)
        {
            activeShelterSceneDataManager = shelterDataManager;
        }
    }

    /// <summary>지정한 셸터 씬 데이터 매니저가 현재 등록 대상이면 연결을 해제합니다.</summary>
    public void UnregisterShelterSceneDataManager(ShelterSceneDataManager shelterDataManager)
    {
        if (activeShelterSceneDataManager == shelterDataManager)
        {
            activeShelterSceneDataManager = null;
        }
    }

    /// <summary>현재 등록된 셸터 씬 작업 패킷을 전역 평탄 정본에 반영합니다.</summary>
    public bool SyncFromShelter()
    {
        if (activeShelterSceneDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] Active ShelterSceneDataManager is not registered.");
            return false;
        }

        return SyncFromShelter(activeShelterSceneDataManager);
    }

    /// <summary>지정한 셸터 씬 데이터 매니저의 작업 패킷을 전역 평탄 정본에 반영합니다.</summary>
    public bool SyncFromShelter(ShelterSceneDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterSceneDataManager is null.");
            return false;
        }

        ApplyShelterRuntimeSnapshot(shelterDataManager.CreateRuntimeSnapshot());
        return true;
    }

    /// <summary>BattleEntryData와 동일한 역할의 셸터 씬 진입 패킷을 생성합니다.</summary>
    public ShelterEntryData CreateShelterEntryData()
    {
        return new ShelterEntryData(CreateShelterRuntimeSnapshot());
    }

    /// <summary>현재 평탄 정본에서 셸터 씬이 사용할 독립 작업 패킷을 생성합니다.</summary>
    public ShelterRuntimeData CreateShelterRuntimeSnapshot()
    {
        EnsureRuntimeState();
        ShelterRuntimeData packet = new ShelterRuntimeData();
        packet.SetShelterStability(shelterStability);
        packet.ApplySavedState(currentDay, playableSquadRuntimeIds, facilityStates);
        packet.Manufacturing.CopyFrom(manufacturing);
        packet.SetItemStorageEntries(itemStorageEntries);

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource != null)
            {
                packet.Resources.SetAmount(resource.Type, resource.Amount);
            }
        }

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData snapshot = characters[i];
            if (snapshot != null && packet.TryAddCharacter(snapshot.Clone(), out ShelterMemberRuntimeData member))
            {
                ShelterCharacterAssignmentData assignment = FindShelterAssignment(member.RuntimeId);
                if (assignment != null && assignment.IsAssigned)
                    member.AssignToFacility(assignment.FacilityId, assignment.RoomId, assignment.Kind);
            }
        }

        return packet;
    }

    /// <summary>셸터 씬 작업 패킷을 깊은 복사해 전역 평탄 정본에 반영합니다.</summary>
    public void ApplyShelterRuntimeSnapshot(ShelterRuntimeData packet)
    {
        if (packet == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterRuntimeData is null.");
            return;
        }

        packet.EnsureRuntimeContainers();
        shelterStability = packet.ShelterStability;
        currentDay = packet.CurrentDay;
        playableSquadRuntimeIds = new List<string>(packet.BattleSquadRuntimeIds);
        facilityStates = CloneFacilityStates(packet.FacilityStates);
        manufacturing ??= new ManufacturingRuntimeData();
        manufacturing.CopyFrom(packet.Manufacturing);
        itemStorageEntries = CloneItemStorageEntries(packet.ItemStorageEntries);

        resourceAmounts = new List<ResourceAmountState>();
        foreach (KeyValuePair<CurrencyType, int> resource in packet.Resources.Amounts)
        {
            resourceAmounts.Add(new ResourceAmountState(resource.Key, resource.Value));
        }

        characters = new List<CharacterSnapshotData>();
        shelterCharacterAssignments = new List<ShelterCharacterAssignmentData>();
        for (int i = 0; i < packet.Characters.Count; i++)
        {
            ShelterMemberRuntimeData character = packet.Characters[i];
            if (character != null)
            {
                characters.Add(character.CreateSnapshot());
                if (character.IsAssignedToFacility)
                    shelterCharacterAssignments.Add(new ShelterCharacterAssignmentData(character));
            }
        }

        EnsureRuntimeState();
    }

    /// <summary>지정한 영속 캐릭터 ID의 캐릭터·총기 스냅샷을 깊은 복사하여 반환합니다.</summary>
    public bool TryGetCharacterSnapshot(string runtimeId, out CharacterSnapshotData snapshot)
    {
        snapshot = null;
        if (!TryGetCharacterIndex(runtimeId, out int index))
        {
            return false;
        }

        snapshot = characters[index].Clone();
        return true;
    }

    /// <summary>셸터 또는 배틀 씬에서 받은 캐릭터·총기 스냅샷을 전역 캐릭터 정본에 반영합니다.</summary>
    public bool TryApplyCharacterSnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            return false;
        }

        string id = string.IsNullOrWhiteSpace(snapshot.RuntimeId) ? snapshot.DefinitionId : snapshot.RuntimeId;
        if (!TryGetCharacterIndex(id, out int index))
        {
            Debug.LogWarning($"[GameDataManager] 캐릭터 스냅샷을 반영할 캐릭터를 찾지 못했습니다. definitionId={snapshot.DefinitionId}");
            return false;
        }

        CharacterSnapshotData normalizedSnapshot = snapshot.Clone();
        if (string.IsNullOrWhiteSpace(normalizedSnapshot.RuntimeId))
        {
            normalizedSnapshot.SetSceneIdentity(
                normalizedSnapshot.DefinitionId,
                normalizedSnapshot.CharacterId,
                normalizedSnapshot.DisplayName);
        }

        characters[index] = normalizedSnapshot;
        return true;
    }

    /// <summary>현재 평탄 정본에서 배틀 씬이 필요로 하는 출전 패킷을 생성합니다.</summary>
    public BattleEntryData CreateBattleEntryData(string battleId, string stageId, int randomSeed)
    {
        if (activeShelterSceneDataManager != null)
        {
            SyncFromShelter(activeShelterSceneDataManager);
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

        for (int i = 0; i < playableSquadRuntimeIds.Count; i++)
        {
            string runtimeId = playableSquadRuntimeIds[i];
            if (!TryGetCharacterIndex(runtimeId, out int characterIndex))
            {
                Debug.LogWarning($"[GameDataManager] 출전 스쿼드 캐릭터를 찾지 못했습니다. runtimeId={runtimeId}");
                continue;
            }

            CharacterSnapshotData characterSnapshot = characters[characterIndex].Clone();
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

        if (activeShelterSceneDataManager != null)
        {
            activeShelterSceneDataManager.ApplyRuntimeSnapshot(CreateShelterRuntimeSnapshot());
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
            shelterStability = ShelterStability
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

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData snapshot = characters[i];
            if (snapshot != null)
                saveData.characters.Add(snapshot.Clone());
        }

        return saveData;
    }

    private SaveData.ShelterSaveData CreateShelterSaveData()
    {
        SaveData.ShelterSaveData saveData = new SaveData.ShelterSaveData
        {
            currentDay = CurrentDay,
            battleSquadRuntimeIds = new List<string>(playableSquadRuntimeIds)
        };

        for (int i = 0; i < shelterCharacterAssignments.Count; i++)
        {
            ShelterCharacterAssignmentData assignment = shelterCharacterAssignments[i];
            if (assignment == null || !assignment.IsAssigned)
                continue;

            saveData.characterAssignments.Add(new SaveData.CharacterAssignmentSaveData
            {
                runtimeId = assignment.RuntimeId,
                facilityId = assignment.FacilityId,
                roomId = assignment.RoomId,
                kind = assignment.Kind
            });
        }

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
        resourceAmounts = new List<ResourceAmountState>();
        characters = new List<CharacterSnapshotData>();
        shelterCharacterAssignments = new List<ShelterCharacterAssignmentData>();

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

        if (saveData.characters != null && saveData.characters.Count > 0)
        {
            for (int i = 0; i < saveData.characters.Count; i++)
            {
                CharacterSnapshotData snapshot = saveData.characters[i];
                if (snapshot != null && !ContainsCharacter(snapshot.RuntimeId, snapshot.DefinitionId))
                    characters.Add(snapshot.Clone());
            }
        }
        else if (saveData.npcs != null)
        {
            for (int i = 0; i < saveData.npcs.Count; i++)
            {
                ShelterMemberRuntimeData legacyCharacter = LegacyNpcSaveDataMapper.ToRuntime(saveData.npcs[i]);
                if (legacyCharacter != null && !ContainsCharacter(legacyCharacter.RuntimeId, legacyCharacter.DefinitionId))
                {
                    characters.Add(legacyCharacter.CreateSnapshot());
                    if (legacyCharacter.IsAssignedToFacility)
                        shelterCharacterAssignments.Add(new ShelterCharacterAssignmentData(legacyCharacter));
                }
            }
        }
    }

    private void ApplyShelterSaveData(SaveData.ShelterSaveData saveData)
    {
        currentDay = Mathf.Max(1, saveData.currentDay);
        manufacturing = new ManufacturingRuntimeData();
        itemStorageEntries = new List<ItemStorageEntry>();
        IEnumerable<string> savedSquadIds = saveData.battleSquadRuntimeIds != null
            && saveData.battleSquadRuntimeIds.Count > 0
            ? saveData.battleSquadRuntimeIds
            : saveData.battleSquadNpcDefinitionIds;
        playableSquadRuntimeIds = NormalizeCharacterIds(
            savedSquadIds,
            ShelterRuntimeData.MaxBattleSquadSize);
        if (saveData.characterAssignments != null && saveData.characterAssignments.Count > 0)
        {
            shelterCharacterAssignments = new List<ShelterCharacterAssignmentData>();
            for (int i = 0; i < saveData.characterAssignments.Count; i++)
            {
                SaveData.CharacterAssignmentSaveData assignment = saveData.characterAssignments[i];
                if (assignment == null)
                    continue;
                ShelterCharacterAssignmentData runtimeAssignment = new ShelterCharacterAssignmentData(
                    assignment.runtimeId,
                    assignment.facilityId,
                    assignment.roomId,
                    assignment.kind);
                if (runtimeAssignment.IsAssigned)
                    shelterCharacterAssignments.Add(runtimeAssignment);
            }
        }
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

    private bool TryGetCharacterIndex(string id, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        string normalizedId = id.Trim();
        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData character = characters[i];
            if (character != null
                && (character.RuntimeId == normalizedId || character.DefinitionId == normalizedId))
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private bool ContainsCharacter(string runtimeId, string definitionId)
    {
        return TryGetCharacterIndex(
            string.IsNullOrWhiteSpace(runtimeId) ? definitionId : runtimeId,
            out _);
    }

    private ShelterCharacterAssignmentData FindShelterAssignment(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return null;
        return shelterCharacterAssignments.Find(assignment => assignment != null
            && assignment.RuntimeId == runtimeId.Trim());
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
        resourceAmounts ??= new List<ResourceAmountState>();
        itemStorageEntries ??= new List<ItemStorageEntry>();
        characters ??= new List<CharacterSnapshotData>();
        playableSquadRuntimeIds ??= new List<string>();
        shelterCharacterAssignments ??= new List<ShelterCharacterAssignmentData>();
        facilityStates ??= new List<FacilityRuntimeState>();
        manufacturing ??= new ManufacturingRuntimeData();
        lastBattleMemberResults ??= new List<BattleMemberResultData>();
        lastBattleAcquiredResources ??= new List<BattleResourceAmountData>();

        NormalizeResources();
        NormalizeItemStorageEntries();
        NormalizeOwnedCharacters();
        playableSquadRuntimeIds = NormalizeCharacterIds(
            playableSquadRuntimeIds,
            ShelterRuntimeData.MaxBattleSquadSize);
        NormalizePlayableSquadRuntimeIds();
        NormalizeShelterAssignments();
        facilityStates = CloneFacilityStates(facilityStates);
        manufacturing.EnsureValid();

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

    private void NormalizeItemStorageEntries()
    {
        List<ItemStorageEntry> normalized = new();
        Dictionary<string, int> entryIndexes = new(StringComparer.Ordinal);
        for (int i = 0; i < itemStorageEntries.Count; i++)
        {
            ItemStorageEntry entry = itemStorageEntries[i];
            if (entry == null)
                continue;

            entry.EnsureValid();
            if (!entry.IsValid)
                continue;

            if (!entryIndexes.TryGetValue(entry.ItemDefinitionId, out int existingIndex))
            {
                entryIndexes.Add(entry.ItemDefinitionId, normalized.Count);
                normalized.Add(entry.Clone());
                continue;
            }

            long combinedQuantity = (long)normalized[existingIndex].Quantity + entry.Quantity;
            normalized[existingIndex] = new ItemStorageEntry(
                entry.ItemDefinitionId,
                combinedQuantity > int.MaxValue ? int.MaxValue : (int)combinedQuantity);
        }

        itemStorageEntries = normalized;
    }

    private void NormalizeOwnedCharacters()
    {
        HashSet<string> runtimeIds = new();
        List<CharacterSnapshotData> normalized = new();
        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData character = characters[i];
            string runtimeId = character == null || string.IsNullOrWhiteSpace(character.RuntimeId)
                ? character?.DefinitionId
                : character.RuntimeId;
            if (character != null
                && !string.IsNullOrWhiteSpace(runtimeId)
                && runtimeIds.Add(runtimeId))
            {
                CharacterSnapshotData normalizedCharacter = character.Clone();
                if (string.IsNullOrWhiteSpace(normalizedCharacter.RuntimeId))
                {
                    normalizedCharacter.SetSceneIdentity(
                        runtimeId,
                        normalizedCharacter.CharacterId,
                        normalizedCharacter.DisplayName);
                }

                normalized.Add(normalizedCharacter);
            }
        }

        characters = normalized;
    }

    private void NormalizePlayableSquadRuntimeIds()
    {
        for (int i = 0; i < playableSquadRuntimeIds.Count; i++)
        {
            if (TryGetCharacterIndex(playableSquadRuntimeIds[i], out int characterIndex))
            {
                CharacterSnapshotData character = characters[characterIndex];
                playableSquadRuntimeIds[i] = string.IsNullOrWhiteSpace(character.RuntimeId)
                    ? character.DefinitionId
                    : character.RuntimeId;
            }
        }

        playableSquadRuntimeIds = NormalizeCharacterIds(
            playableSquadRuntimeIds,
            ShelterRuntimeData.MaxBattleSquadSize);
    }

    private void NormalizeShelterAssignments()
    {
        List<ShelterCharacterAssignmentData> normalized = new();
        HashSet<string> assignedRuntimeIds = new();
        for (int i = 0; i < shelterCharacterAssignments.Count; i++)
        {
            ShelterCharacterAssignmentData assignment = shelterCharacterAssignments[i];
            if (assignment == null
                || !assignment.IsAssigned
                || !TryGetCharacterIndex(assignment.RuntimeId, out int characterIndex))
            {
                continue;
            }

            CharacterSnapshotData character = characters[characterIndex];
            string runtimeId = string.IsNullOrWhiteSpace(character.RuntimeId)
                ? character.DefinitionId
                : character.RuntimeId;
            if (!assignedRuntimeIds.Add(runtimeId))
                continue;

            normalized.Add(new ShelterCharacterAssignmentData(
                runtimeId,
                assignment.FacilityId,
                assignment.RoomId,
                assignment.Kind));
        }

        shelterCharacterAssignments = normalized;
    }

    private static bool ShouldApplyAcquiredResources(BattleOutcome outcome)
    {
        return outcome == BattleOutcome.Success || outcome == BattleOutcome.Evacuated;
    }

    private static List<string> NormalizeCharacterIds(IEnumerable<string> source, int maximumCount)
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

    private static List<ItemStorageEntry> CloneItemStorageEntries(IEnumerable<ItemStorageEntry> source)
    {
        List<ItemStorageEntry> clone = new();
        if (source == null)
            return clone;

        foreach (ItemStorageEntry entry in source)
        {
            if (entry != null && entry.IsValid)
                clone.Add(entry.Clone());
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
