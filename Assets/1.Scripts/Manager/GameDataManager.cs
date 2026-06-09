using UnityEditor.Overlays;
using UnityEngine;

//ToDo : 런타임 데이터 게임에 맞게 연결하기
[System.Serializable]
public class RuntimeGameData
{
    public int playerLevel = 1;
    public int playerGold;
    public string lastStageId = string.Empty;
    public int currentDay = 1;
}

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("런타임 데이터")]
    [SerializeField] private RuntimeGameData runtimeData = new RuntimeGameData();

    public RuntimeGameData RuntimeData => runtimeData;

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

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
        return new RuntimeGameData
        {
            playerLevel = runtimeData.playerLevel,
            playerGold = runtimeData.playerGold,
            lastStageId = runtimeData.lastStageId ?? string.Empty,
            currentDay = Mathf.Max(1, runtimeData.currentDay)
        };
    }

    public SaveData CreateSaveData(string profileId)
    {
        SaveData saveData = new SaveData();
        saveData.profileId = string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId;
        saveData.progress.playerLevel = runtimeData.playerLevel;
        saveData.progress.playerGold = runtimeData.playerGold;
        saveData.progress.lastStageId = runtimeData.lastStageId ?? string.Empty;
        saveData.progress.currentDay = Mathf.Max(1, runtimeData.currentDay);
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

        runtimeData.playerLevel = Mathf.Max(1, saveData.progress.playerLevel);
        runtimeData.playerGold = Mathf.Max(0, saveData.progress.playerGold);
        runtimeData.lastStageId = saveData.progress.lastStageId ?? string.Empty;
        runtimeData.currentDay = Mathf.Max(1, saveData.progress.currentDay);
    }

    public void ApplySnapshot(RuntimeGameData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] Snapshot is null.");
            return;
        }

        runtimeData.playerLevel = Mathf.Max(1, snapshot.playerLevel);
        runtimeData.playerGold = Mathf.Max(0, snapshot.playerGold);
        runtimeData.lastStageId = snapshot.lastStageId ?? string.Empty;
        runtimeData.currentDay = Mathf.Max(1, snapshot.currentDay);
    }

    public void SetPlayerLevel(int level)
    {
        runtimeData.playerLevel = Mathf.Max(1, level);
    }

    public void SetPlayerGold(int gold)
    {
        runtimeData.playerGold = Mathf.Max(0, gold);
    }

    public void SetLastStageId(string stageId)
    {
        runtimeData.lastStageId = stageId ?? string.Empty;
    }

    public void SetCurrentDay(int day)
    {
        runtimeData.currentDay = Mathf.Max(1, day);
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
