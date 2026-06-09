using System.Collections.Generic;
using UnityEditor.Overlays;
using UnityEngine;

[System.Serializable]
public class RuntimeGameData
{
    public string lastStageId = string.Empty;
    public int currentDay = 1;
    public string controlledNpcRuntimeId = string.Empty;
    public List<string> squadNpcRuntimeIds = new List<string>();

    private ResourceStorage resources;
    private NpcRoster npcRoster;

    public ResourceStorage Resources
    {
        get
        {
            resources ??= new ResourceStorage();
            return resources;
        }
    }

    public NpcRoster NpcRoster
    {
        get
        {
            npcRoster ??= new NpcRoster();
            return npcRoster;
        }
    }

    public void EnsureRuntimeContainers()
    {
        lastStageId ??= string.Empty;
        controlledNpcRuntimeId ??= string.Empty;
        squadNpcRuntimeIds ??= new List<string>();
        currentDay = Mathf.Max(1, currentDay);

        _ = Resources;
        _ = NpcRoster;
    }
}

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("Runtime Data")]
    [SerializeField] private RuntimeGameData runtimeData = new RuntimeGameData();

    public RuntimeGameData RuntimeData
    {
        get
        {
            EnsureRuntimeData();
            return runtimeData;
        }
    }

    public ResourceStorage Resources => RuntimeData.Resources;
    public NpcRoster NpcRoster => RuntimeData.NpcRoster;
    public IReadOnlyList<string> SquadNpcRuntimeIds => RuntimeData.squadNpcRuntimeIds;
    public string ControlledNpcRuntimeId => RuntimeData.controlledNpcRuntimeId ?? string.Empty;
    public int CurrentDay => Mathf.Max(1, RuntimeData.currentDay);

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        EnsureRuntimeData();
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public RuntimeGameData Snapshot()
    {
        RuntimeGameData snapshot = new RuntimeGameData
        {
            lastStageId = RuntimeData.lastStageId ?? string.Empty,
            currentDay = CurrentDay,
            controlledNpcRuntimeId = RuntimeData.controlledNpcRuntimeId ?? string.Empty,
            squadNpcRuntimeIds = new List<string>(RuntimeData.squadNpcRuntimeIds)
        };

        foreach (NPCRuntimeData npc in NpcRoster.All)
        {
            snapshot.NpcRoster.Add(npc);
        }

        return snapshot;
    }

    public SaveData CreateSaveData(string profileId)
    {
        SaveData saveData = new SaveData();
        saveData.profileId = string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId;
        saveData.progress.lastStageId = RuntimeData.lastStageId ?? string.Empty;
        saveData.progress.currentDay = CurrentDay;
        saveData.MarkSavedNow();
        return saveData;
    }

    public void ApplySaveData(SaveData saveData)
    {
        if (saveData == null)
        {
            Debug.LogWarning("[GameDataManager] SaveData is null.");
            return;
        }

        if (saveData.progress == null)
        {
            Debug.LogWarning("[GameDataManager] SaveData.progress is null.");
            return;
        }

        RuntimeData.lastStageId = saveData.progress.lastStageId ?? string.Empty;
        RuntimeData.currentDay = Mathf.Max(1, saveData.progress.currentDay);
    }

    public void ApplySnapshot(RuntimeGameData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] Snapshot is null.");
            return;
        }

        snapshot.EnsureRuntimeContainers();

        RuntimeData.lastStageId = snapshot.lastStageId ?? string.Empty;
        RuntimeData.currentDay = Mathf.Max(1, snapshot.currentDay);
        RuntimeData.controlledNpcRuntimeId = snapshot.controlledNpcRuntimeId ?? string.Empty;
        RuntimeData.squadNpcRuntimeIds = new List<string>(snapshot.squadNpcRuntimeIds);

        NpcRoster.Clear();
        foreach (NPCRuntimeData npc in snapshot.NpcRoster.All)
        {
            NpcRoster.Add(npc);
        }
    }

    public bool TryRecruitNpc(NPCChar npcData, out NPCRuntimeData runtimeNpc)
    {
        return NpcRoster.TryAdd(npcData, out runtimeNpc);
    }

    public bool TryRecruitNpc(NPCChar npcData, string runtimeId, out NPCRuntimeData runtimeNpc)
    {
        return NpcRoster.TryAdd(npcData, runtimeId, out runtimeNpc);
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

        RemoveNpcReferences(runtimeId);
        return true;
    }

    public bool TrySetControlledNpc(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId) || !NpcRoster.Contains(runtimeId))
        {
            return false;
        }

        RuntimeData.controlledNpcRuntimeId = runtimeId.Trim();
        return true;
    }

    public void ClearControlledNpc()
    {
        RuntimeData.controlledNpcRuntimeId = string.Empty;
    }

    public bool TryAddSquadNpc(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId) || !NpcRoster.Contains(runtimeId))
        {
            return false;
        }

        string trimmedRuntimeId = runtimeId.Trim();
        if (RuntimeData.squadNpcRuntimeIds.Contains(trimmedRuntimeId))
        {
            return true;
        }

        RuntimeData.squadNpcRuntimeIds.Add(trimmedRuntimeId);
        return true;
    }

    public bool TryRemoveSquadNpc(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            return false;
        }

        return RuntimeData.squadNpcRuntimeIds.Remove(runtimeId.Trim());
    }

    public void SetLastStageId(string stageId)
    {
        RuntimeData.lastStageId = stageId ?? string.Empty;
    }

    public void SetCurrentDay(int day)
    {
        RuntimeData.currentDay = Mathf.Max(1, day);
    }

    private void EnsureRuntimeData()
    {
        runtimeData ??= new RuntimeGameData();
        runtimeData.EnsureRuntimeContainers();
    }

    private void RemoveNpcReferences(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            return;
        }

        string trimmedRuntimeId = runtimeId.Trim();
        if (RuntimeData.controlledNpcRuntimeId == trimmedRuntimeId)
        {
            RuntimeData.controlledNpcRuntimeId = string.Empty;
        }

        RuntimeData.squadNpcRuntimeIds.RemoveAll(id => id == trimmedRuntimeId);
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
