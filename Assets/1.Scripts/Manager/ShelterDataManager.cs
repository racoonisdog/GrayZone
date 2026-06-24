using System;
using System.Collections.Generic;
using UnityEngine;

public class ShelterDataManager : MonoBehaviour
{
    public static ShelterDataManager Instance { get; private set; }

    [Header("Shelter Working Data")]
    [SerializeField] private ShelterRuntimeData shelterData = new ShelterRuntimeData();
    [SerializeField] private bool copySharedDataFromGameDataManagerOnAwake = true;

    public event Action<ShelterRuntimeData> ShelterDataChanged;

    public ShelterRuntimeData ShelterData
    {
        get
        {
            EnsureShelterData();
            return shelterData;
        }
    }

    public SharedRuntimeData SharedData => ShelterData.SharedData;
    public ResourceStorage Resources => ShelterData.Resources;
    public NpcRoster NpcRoster => ShelterData.NpcRoster;
    public int RosterCount => ShelterData.RosterCount;
    public int TotalOwnedCharacterCount => ShelterData.TotalOwnedCharacterCount;
    public int PlayableCharacterCount => ShelterData.PlayableCharacterCount;
    public int NonPlayableNpcCount => ShelterData.NonPlayableNpcCount;
    public IReadOnlyList<string> BattleSquadNpcRuntimeIds => ShelterData.BattleSquadNpcRuntimeIds;
    public int CurrentDay => Mathf.Max(1, ShelterData.currentDay);
    public int ShelterStability => ShelterData.ShelterStability;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureShelterData();

        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.RegisterShelterDataManager(this);

            if (copySharedDataFromGameDataManagerOnAwake)
            {
                CopySharedDataFromGameDataManager();
            }
        }
        else
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
        }
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.UnregisterShelterDataManager(this);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void CopySharedDataFromGameDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return;
        }

        ApplySharedSnapshot(GameDataManager.Instance.CreateSnapshot());
    }

    public void CopyFromDataManager()
    {
        CopySharedDataFromGameDataManager();
    }

    public bool PushToDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return false;
        }

        return GameDataManager.Instance.SyncFromShelter(this);
    }

    public ShelterRuntimeData CreateSnapshot()
    {
        return ShelterData.Clone();
    }

    public SharedRuntimeData CreateSharedSnapshot()
    {
        return SharedData.Clone();
    }

    public void ApplySnapshot(ShelterRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[ShelterDataManager] Snapshot is null.");
            return;
        }

        ShelterData.CopyFrom(snapshot);
        NotifyShelterDataChanged();
    }

    public void ApplySharedSnapshot(SharedRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[ShelterDataManager] Shared snapshot is null.");
            return;
        }

        ShelterData.CopySharedFrom(snapshot);
        NotifyShelterDataChanged();
    }

    public FacilityRuntimeState GetOrCreateFacilityState(string facilityId, bool isUnlockedByDefault)
    {
        FacilityRuntimeState state = ShelterData.GetOrCreateFacilityState(facilityId, isUnlockedByDefault);
        NotifyShelterDataChanged();
        return state;
    }

    public bool TryRecruitNpc(NPCChar npcData, out NPCRuntimeData runtimeNpc)
    {
        bool result = NpcRoster.TryAdd(npcData, out runtimeNpc);
        if (result)
        {
            SharedData.RefreshCountsFromRosterAsPlayable();
            NotifyShelterDataChanged();
        }

        return result;
    }

    public bool TryGetNpc(string runtimeId, out NPCRuntimeData runtimeNpc)
    {
        return NpcRoster.TryGet(runtimeId, out runtimeNpc);
    }

    public bool TryRemoveNpc(string runtimeId)
    {
        if (!NpcRoster.Remove(runtimeId))
        {
            return false;
        }

        ShelterData.RemoveNpcReferences(runtimeId);
        SharedData.RefreshCountsFromRosterAsPlayable();
        NotifyShelterDataChanged();
        return true;
    }

    public bool TrySetBattleSquad(IEnumerable<string> runtimeIds)
    {
        bool result = ShelterData.TrySetBattleSquad(runtimeIds);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    public void ClearBattleSquad()
    {
        ShelterData.ClearBattleSquad();
        NotifyShelterDataChanged();
    }

    public void SetOwnedCharacterCounts(int playableCount, int nonPlayableNpcCount)
    {
        SharedData.SetOwnedCharacterCounts(playableCount, nonPlayableNpcCount);
        NotifyShelterDataChanged();
    }

    public void RefreshCountsFromRosterAsPlayable()
    {
        SharedData.RefreshCountsFromRosterAsPlayable();
        NotifyShelterDataChanged();
    }

    public void SetLastStageId(string stageId)
    {
        SharedData.lastStageId = stageId ?? string.Empty;
        NotifyShelterDataChanged();
    }

    public void SetCurrentDay(int day)
    {
        ShelterData.currentDay = Mathf.Max(1, day);
        NotifyShelterDataChanged();
    }

    public void SetShelterStability(int stability)
    {
        SharedData.shelterStability = Mathf.Clamp(stability, 0, 100);
        NotifyShelterDataChanged();
    }

    public void MarkDirty()
    {
        NotifyShelterDataChanged();
    }

    private void NotifyShelterDataChanged()
    {
        ShelterDataChanged?.Invoke(CreateSnapshot());
    }

    private void EnsureShelterData()
    {
        shelterData ??= new ShelterRuntimeData();
        shelterData.EnsureRuntimeContainers();
    }
}