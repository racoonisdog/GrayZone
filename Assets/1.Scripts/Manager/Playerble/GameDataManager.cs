using System.Collections.Generic;
using System;
using UnityEngine;

/// <summary>
/// 씬 사이에서 유지되는 공용·셸터 런타임 데이터와 배틀 결과 반영 경계를 소유하는 전역 데이터 매니저입니다.
/// </summary>
[DefaultExecutionOrder(-300)]
public class GameDataManager : MonoBehaviour
{
    /// <summary>현재 GameManager 자식에서 활성화된 전역 데이터 매니저 인스턴스입니다.</summary>
    public static GameDataManager Instance { get; private set; }

    [Header("Shared Runtime Baseline Data")]
    [SerializeField] private SharedRuntimeData sharedData = new SharedRuntimeData();

    [Header("Scene Runtime Backup Data")]
    [SerializeField] private ShelterRuntimeData shelterData = new ShelterRuntimeData();

    [Header("Battle Runtime Backup Data")]
    [SerializeField] private BattleResultData lastBattleResult;

    [SerializeField] private List<string> appliedBattleIds = new();

    private ShelterDataManager activeShelterDataManager;

    private SharedRuntimeData SharedData
    {
        get
        {
            EnsureSharedData();
            return sharedData;
        }
    }

    private ShelterRuntimeData ShelterData
    {
        get
        {
            EnsureShelterData();
            return shelterData;
        }
    }

    /// <summary>현재 공용 로스터에 등록된 NPC 수입니다.</summary>
    public int RosterCount => SharedData.RosterCount;

    /// <summary>플레이어블 캐릭터와 비플레이어 NPC를 합한 전체 보유 캐릭터 수입니다.</summary>
    public int TotalOwnedCharacterCount => SharedData.TotalOwnedCharacterCount;

    /// <summary>현재 보유한 플레이어블 캐릭터 수입니다.</summary>
    public int PlayableCharacterCount => SharedData.PlayableCharacterCount;

    /// <summary>현재 보유한 비플레이어 NPC 수입니다.</summary>
    public int NonPlayableNpcCount => SharedData.NonPlayableNpcCount;

    /// <summary>0~100 범위로 보정된 현재 셸터 안정도입니다.</summary>
    public int ShelterStability => Mathf.Clamp(SharedData.ShelterStability, 0, 100);

    /// <summary>현재 셸터 진행 일차입니다.</summary>
    public int CurrentDay => ShelterData.CurrentDay;

    /// <summary>현재 씬의 ShelterDataManager가 등록되어 있는지 여부입니다.</summary>
    public bool HasActiveShelterDataManager => activeShelterDataManager != null;

    /// <summary>현재 세션에서 반영한 마지막 배틀 결과가 있는지 여부입니다.</summary>
    public bool HasLastBattleResult => lastBattleResult != null;

    /// <summary>중복 인스턴스를 거부하고 런타임 데이터 컨테이너를 준비합니다.</summary>
    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        EnsureSharedData();
        EnsureShelterData();
        appliedBattleIds ??= new List<string>();
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
        if (shelterDataManager == null)
            return;

        activeShelterDataManager = shelterDataManager;
    }

    /// <summary>지정한 셸터 씬 데이터 매니저가 현재 등록 대상이면 연결을 해제합니다.</summary>
    public void UnregisterShelterDataManager(ShelterDataManager shelterDataManager)
    {
        if (activeShelterDataManager == shelterDataManager)
        {
            activeShelterDataManager = null;
        }
    }

    /// <summary>현재 등록된 ShelterDataManager의 공용·셸터 작업 데이터를 전역 데이터로 복사합니다.</summary>
    public bool SyncFromShelter()
    {
        if (activeShelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] Active ShelterDataManager is not registered.");
            return false;
        }

        return SyncFromShelter(activeShelterDataManager);
    }

    /// <summary>지정한 ShelterDataManager의 공용·셸터 작업 데이터를 전역 데이터로 복사합니다.</summary>
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

    /// <summary>현재 공용 런타임 데이터의 깊은 복사본을 반환합니다.</summary>
    public SharedRuntimeData CreateSharedSnapshot()
    {
        return SharedData.Clone();
    }

    /// <summary>현재 셸터 전용 런타임 데이터의 깊은 복사본을 반환합니다.</summary>
    public ShelterRuntimeData CreateShelterSnapshot()
    {
        return ShelterData.Clone();
    }

    /// <summary>
    /// 현재 영속 런타임 데이터에서 배틀 씬으로 넘길 출전 스냅샷을 생성합니다.
    /// 셸터 NPC의 회복형 부상 게이지는 배틀의 누적 부상 게이지 방향으로 변환합니다.
    /// </summary>
    public BattleEntryData CreateBattleEntryData(string battleId, string stageId, int randomSeed)
    {
        SharedRuntimeData sharedSnapshot = activeShelterDataManager != null
            ? activeShelterDataManager.CreateSharedSnapshot()
            : SharedData.Clone();
        ShelterRuntimeData shelterSnapshot = activeShelterDataManager != null
            ? activeShelterDataManager.CreateShelterSnapshot()
            : ShelterData.Clone();

        string resolvedBattleId = string.IsNullOrWhiteSpace(battleId)
            ? Guid.NewGuid().ToString("N")
            : battleId.Trim();
        string resolvedStageId = string.IsNullOrWhiteSpace(stageId)
            ? sharedSnapshot.LastStageId
            : stageId.Trim();

        BattleEntryData entryData = new BattleEntryData(
            resolvedBattleId,
            resolvedStageId,
            randomSeed,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        foreach (KeyValuePair<CurrencyType, int> resource in sharedSnapshot.Resources.Amounts)
        {
            entryData.AddStartingResource(resource.Key, resource.Value);
        }

        IReadOnlyList<string> squadDefinitionIds = shelterSnapshot.BattleSquadNpcDefinitionIds;
        for (int i = 0; i < squadDefinitionIds.Count; i++)
        {
            string definitionId = squadDefinitionIds[i];
            if (!sharedSnapshot.NpcRoster.TryGet(definitionId, out NPCRuntimeData npc))
            {
                Debug.LogWarning($"[GameDataManager] 출전 스쿼드 NPC를 로스터에서 찾지 못했습니다. definitionId={definitionId}");
                continue;
            }

            float maxInjuryGauge = npc.MaxInjuryGauge;
            float battleInjuryGauge = Mathf.Clamp(maxInjuryGauge - npc.InjuryGauge, 0.0f, maxInjuryGauge);
            string displayName = npc.NPCData != null && !string.IsNullOrWhiteSpace(npc.NPCData.name)
                ? npc.NPCData.name
                : npc.DefinitionId;

            entryData.AddMember(new BattleMemberEntryData(
                npc.DefinitionId,
                string.Empty,
                PlayableCharacterId.Unknown,
                displayName,
                npc.GetCurrentHp(),
                npc.MaxHp,
                battleInjuryGauge,
                maxInjuryGauge,
                ConvertToPlayerInjuryState(npc.CurrentInjuryState),
                string.Empty,
                0,
                0,
                i == 0));
        }

        return entryData;
    }

    /// <summary>
    /// 확정된 배틀 결과를 공용 영속 런타임 데이터에 한 번만 반영합니다.
    /// 성공 또는 탈출일 때만 획득 자원을 보존하며, 멤버 HP·부상 결과는 모든 종료 결과에 반영합니다.
    /// </summary>
    public bool ApplyBattleResult(BattleResultData resultData)
    {
        if (resultData == null)
        {
            Debug.LogWarning("[GameDataManager] BattleResultData is null.");
            return false;
        }

        appliedBattleIds ??= new List<string>();
        string battleId = resultData.BattleId;
        if (string.IsNullOrWhiteSpace(battleId))
        {
            Debug.LogWarning("[GameDataManager] BattleResultData.BattleId is empty.");
            return false;
        }

        if (appliedBattleIds.Contains(battleId))
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
                    SharedData.Resources.Add(resource.Type, resource.Amount);
                }
            }
        }

        for (int i = 0; i < resultData.Members.Count; i++)
        {
            ApplyBattleMemberResult(resultData.Members[i]);
        }

        if (!string.IsNullOrWhiteSpace(resultData.StageId))
        {
            SharedData.SetLastStageId(resultData.StageId);
        }

        lastBattleResult = resultData.Clone();
        appliedBattleIds.Add(battleId);

        if (activeShelterDataManager != null)
        {
            activeShelterDataManager.ApplySharedSnapshot(SharedData.Clone());
        }

        return true;
    }

    /// <summary>마지막으로 반영한 배틀 결과의 깊은 복사본을 반환합니다.</summary>
    public BattleResultData CreateLastBattleResultSnapshot()
    {
        return lastBattleResult?.Clone();
    }

    /// <summary>현재 전역 런타임 데이터를 지정한 프로필의 저장 데이터 구조로 변환합니다.</summary>
    public SaveData CreateSaveData(string profileId)
    {
        SaveData saveData = new SaveData
        {
            profileId = string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId
        };

        SharedRuntimeData sharedSnapshot = activeShelterDataManager != null ? activeShelterDataManager.CreateSharedSnapshot() : SharedData.Clone();
        ShelterRuntimeData shelterSnapshot = activeShelterDataManager != null ? activeShelterDataManager.CreateShelterSnapshot() : ShelterData.Clone();
        saveData.shared = CreateSharedSaveData(sharedSnapshot);
        saveData.shelter = CreateShelterSaveData(shelterSnapshot);
        saveData.MarkSavedNow();
        return saveData;
    }

    /// <summary>불러온 저장 데이터를 현재 공용·셸터 런타임 데이터에 적용합니다.</summary>
    public void ApplySaveData(SaveData saveData)
    {
        if (saveData == null)
        {
            Debug.LogWarning("[GameDataManager] SaveData is null.");
            return;
        }

        ApplySharedSaveData(saveData.shared ?? new SaveData.SharedSaveData());
        ApplyShelterSaveData(saveData.shelter ?? new SaveData.ShelterSaveData());
    }

    /// <summary>지정한 공용 런타임 스냅샷을 현재 전역 공용 데이터에 복사합니다.</summary>
    public void ApplySharedSnapshot(SharedRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] Snapshot is null.");
            return;
        }

        SharedData.CopyFrom(snapshot);
    }

    /// <summary>지정한 셸터 런타임 스냅샷을 현재 전역 셸터 데이터에 복사합니다.</summary>
    public void ApplyShelterSnapshot(ShelterRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] Shelter snapshot is null.");
            return;
        }

        ShelterData.CopyFrom(snapshot);
    }

    /// <summary>공용 런타임 데이터와 내부 컬렉션이 항상 사용 가능한 상태인지 보장합니다.</summary>
    private void EnsureSharedData()
    {
        sharedData ??= new SharedRuntimeData();
        sharedData.EnsureRuntimeContainers();
    }

    /// <summary>셸터 런타임 데이터와 내부 컬렉션이 항상 사용 가능한 상태인지 보장합니다.</summary>
    private void EnsureShelterData()
    {
        shelterData ??= new ShelterRuntimeData();
        shelterData.EnsureRuntimeContainers();
    }

    /// <summary>셸터 NPC 부상 단계를 배틀 플레이어 부상 단계로 대응시킵니다.</summary>
    private static PlayerInjuryState ConvertToPlayerInjuryState(NPCInjuryState state)
    {
        return state switch
        {
            NPCInjuryState.Healthy => PlayerInjuryState.Normal,
            NPCInjuryState.LightInjury => PlayerInjuryState.Minor,
            NPCInjuryState.HeavyInjury => PlayerInjuryState.Serious,
            NPCInjuryState.NearDeath => PlayerInjuryState.Critical,
            NPCInjuryState.Dead => PlayerInjuryState.Critical,
            _ => PlayerInjuryState.Normal
        };
    }

    /// <summary>종료 결과에 따라 이번 배틀에서 획득한 자원을 셸터에 보존할지 결정합니다.</summary>
    private static bool ShouldApplyAcquiredResources(BattleOutcome outcome)
    {
        return outcome == BattleOutcome.Success || outcome == BattleOutcome.Evacuated;
    }

    /// <summary>스쿼드원 최종 HP와 부상 게이지를 대응하는 셸터 NPC 런타임 데이터에 반영합니다.</summary>
    private void ApplyBattleMemberResult(BattleMemberResultData memberResult)
    {
        BattleMemberRuntimeData member = memberResult?.RuntimeData;
        BattleMemberEntryData entry = member?.EntryData;
        string definitionId = entry?.DefinitionId ?? string.Empty;
        if (member == null || string.IsNullOrWhiteSpace(definitionId))
        {
            return;
        }

        if (!SharedData.NpcRoster.TryGet(definitionId, out NPCRuntimeData npc))
        {
            Debug.LogWarning($"[GameDataManager] 배틀 결과를 반영할 NPC를 로스터에서 찾지 못했습니다. definitionId={definitionId}");
            return;
        }

        float hpRatio = member.MaxHp > 0
            ? Mathf.Clamp01((float)member.CurrentHp / member.MaxHp)
            : 0.0f;
        int shelterHp = member.IsCombatOut
            ? 0
            : Mathf.RoundToInt(npc.MaxHp * hpRatio);

        float battleInjuryRatio = member.MaxInjuryGauge > 0.0f
            ? Mathf.Clamp01(member.InjuryGauge / member.MaxInjuryGauge)
            : 0.0f;
        float shelterRecoveryGauge = npc.MaxInjuryGauge * (1.0f - battleInjuryRatio);

        npc.SetCurrentHp(shelterHp);
        npc.SetInjuryGauge(shelterRecoveryGauge);
        npc.RefreshInjuryStateFromGauge();
    }

    /// <summary>공용 런타임 데이터를 파일 저장용 공용 데이터 구조로 변환합니다.</summary>
    private SaveData.SharedSaveData CreateSharedSaveData(SharedRuntimeData source)
    {
        source.EnsureRuntimeContainers();

        SaveData.SharedSaveData saveData = new SaveData.SharedSaveData
        {
            lastStageId = source.LastStageId,
            shelterStability = source.ShelterStability,
            playableCharacterCount = source.PlayableCharacterCount,
            nonPlayableNpcCount = source.NonPlayableNpcCount
        };

        foreach (KeyValuePair<CurrencyType, int> resource in source.Resources.Amounts)
        {
            saveData.resources.Add(new SaveData.ResourceAmountData
            {
                type = resource.Key,
                amount = Mathf.Max(0, resource.Value)
            });
        }

        foreach (NPCRuntimeData npc in source.NpcRoster.All)
        {
            SaveData.NpcSaveData npcSaveData = NpcSaveDataMapper.FromRuntime(npc);
            if (npcSaveData == null)
                continue;

            saveData.npcs.Add(npcSaveData);
        }

        return saveData;
    }

    /// <summary>셸터 런타임 데이터를 파일 저장용 셸터 데이터 구조로 변환합니다.</summary>
    private SaveData.ShelterSaveData CreateShelterSaveData(ShelterRuntimeData source)
    {
        source.EnsureRuntimeContainers();

        SaveData.ShelterSaveData saveData = new SaveData.ShelterSaveData
        {
            currentDay = source.CurrentDay,
            battleSquadNpcDefinitionIds = new List<string>(source.BattleSquadNpcDefinitionIds)
        };

        foreach (FacilityRuntimeState state in source.FacilityStates)
        {
            SaveData.FacilitySaveData facilitySaveData = FacilitySaveDataMapper.FromRuntime(state);
            if (facilitySaveData == null)
                continue;

            saveData.facilities.Add(facilitySaveData);
        }

        return saveData;
    }

    /// <summary>파일에서 읽은 공용 저장 데이터를 현재 공용 런타임 데이터에 적용합니다.</summary>
    private void ApplySharedSaveData(SaveData.SharedSaveData saveData)
    {
        SharedData.SetLastStageId(saveData.lastStageId);
        SharedData.SetShelterStability(saveData.shelterStability);
        SharedData.SetOwnedCharacterCounts(saveData.playableCharacterCount, saveData.nonPlayableNpcCount);

        SharedData.Resources.Clear();
        if (saveData.resources != null)
        {
            foreach (SaveData.ResourceAmountData resource in saveData.resources)
            {
                if (resource == null)
                    continue;

                SharedData.Resources.SetAmount(resource.type, resource.amount);
            }
        }

        SharedData.NpcRoster.Clear();
        if (saveData.npcs != null)
        {
            foreach (SaveData.NpcSaveData npc in saveData.npcs)
            {
                if (npc == null)
                    continue;

                NPCRuntimeData runtimeNpc = NpcSaveDataMapper.ToRuntime(npc);
                if (runtimeNpc != null)
                {
                    SharedData.NpcRoster.Add(runtimeNpc);
                }
            }
        }
    }

    /// <summary>파일에서 읽은 셸터 저장 데이터를 현재 셸터 런타임 데이터에 적용합니다.</summary>
    private void ApplyShelterSaveData(SaveData.ShelterSaveData saveData)
    {
        List<FacilityRuntimeState> facilityStates = new List<FacilityRuntimeState>();
        if (saveData.facilities != null)
        {
            foreach (SaveData.FacilitySaveData facility in saveData.facilities)
            {
                if (facility == null)
                    continue;

                FacilityRuntimeState runtimeState = FacilitySaveDataMapper.ToRuntime(facility);
                if (runtimeState != null)
                {
                    facilityStates.Add(runtimeState);
                }
            }
        }

        ShelterData.ApplySavedState(saveData.currentDay, saveData.battleSquadNpcDefinitionIds, facilityStates);
    }

    /// <summary>GameManager 소속이 아닌 중복 또는 고아 인스턴스를 제거할지 판정합니다.</summary>
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
