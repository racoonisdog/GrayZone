using UnityEngine;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("Shared Runtime Baseline Data")]
    [SerializeField] private SharedRuntimeData sharedData = new SharedRuntimeData();

    private ShelterDataManager activeShelterDataManager;
    //ToDo : 읽어올 BattleDataManager 등록하기

    private SharedRuntimeData SharedData
    {
        get
        {
            EnsureSharedData();
            return sharedData;
        }
    }

    public int RosterCount => SharedData.RosterCount;
    public int TotalOwnedCharacterCount => SharedData.TotalOwnedCharacterCount;
    public int PlayableCharacterCount => SharedData.PlayableCharacterCount;
    public int NonPlayableNpcCount => SharedData.NonPlayableNpcCount;
    public int ShelterStability => Mathf.Clamp(SharedData.shelterStability, 0, 100);
    public bool HasActiveShelterDataManager => activeShelterDataManager != null;

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        EnsureSharedData();
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void RegisterShelterDataManager(ShelterDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
            return;

        activeShelterDataManager = shelterDataManager;
    }

    public void UnregisterShelterDataManager(ShelterDataManager shelterDataManager)
    {
        if (activeShelterDataManager == shelterDataManager)
        {
            activeShelterDataManager = null;
        }
    }

    public bool SyncFromShelter()
    {
        if (activeShelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] Active ShelterDataManager is not registered.");
            return false;
        }

        return SyncFromShelter(activeShelterDataManager);
    }

    public bool SyncFromShelter(ShelterDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterDataManager is null.");
            return false;
        }

        ApplySnapshot(shelterDataManager.CreateSharedSnapshot());
        return true;
    }

    public SharedRuntimeData CreateSnapshot()
    {
        return SharedData.Clone();
    }

    public SharedRuntimeData Snapshot()
    {
        return CreateSnapshot();
    }

    public SaveData CreateSaveData(string profileId)
    {
        SaveData saveData = new SaveData();
        saveData.profileId = string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId;
        saveData.progress.lastStageId = SharedData.lastStageId ?? string.Empty;
        saveData.progress.currentDay = activeShelterDataManager != null ? activeShelterDataManager.CurrentDay : 1;
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

        SharedData.lastStageId = saveData.progress.lastStageId ?? string.Empty;
    }

    public void ApplySnapshot(SharedRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] Snapshot is null.");
            return;
        }

        SharedData.CopyFrom(snapshot);
    }

    private void EnsureSharedData()
    {
        sharedData ??= new SharedRuntimeData();
        sharedData.EnsureRuntimeContainers();
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
