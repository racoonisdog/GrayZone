using System.IO;
using UnityEditor.Overlays;
using UnityEngine;

public class GameSaveManager : MonoBehaviour
{
    public static GameSaveManager Instance { get; private set; }

    [Header("저장 설정")]
    [SerializeField] private string defaultProfileId = SaveFilePaths.DefaultProfileId;
    [SerializeField] private bool prettyPrint = true;

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

    public bool SaveGame()
    {
        bool gameSaved = SaveGameData(defaultProfileId);
        bool settingSaved = SaveSettingData();
        return gameSaved && settingSaved;
    }

    public bool LoadGame()
    {
        bool gameLoaded = LoadGameData(defaultProfileId);
        bool settingLoaded = LoadSettingData();
        return gameLoaded && settingLoaded;
    }

    public bool SaveGameData(string profileId)
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        string resolvedProfileId = ResolveProfileId(profileId);
        SaveData saveData = GameDataManager.Instance.CreateSaveData(resolvedProfileId);
        saveData.schemaVersion = SaveData.CurrentSchemaVersion;
        string path = GetGameSavePath(resolvedProfileId);
        return TryWriteJsonFile(path, saveData, "game save");
    }

    public bool LoadGameData(string profileId)
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        string path = GetGameSavePath(ResolveProfileId(profileId));
        if (!TryReadJsonFile(path, out SaveData saveData, "game save"))
        {
            return false;
        }

        saveData.schemaVersion = saveData.schemaVersion <= 0 ? SaveData.CurrentSchemaVersion : saveData.schemaVersion;
        GameDataManager.Instance.ApplySaveData(saveData);
        return true;
    }

    public bool SaveSettingData()
    {
        if (GameSettingManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameSettingManager.Instance is null.");
            return false;
        }

        SettingData settingData = GameSettingManager.Instance.CreateSettingData();
        settingData.schemaVersion = SettingData.CurrentSchemaVersion;
        string path = GetSettingPath();
        return TryWriteJsonFile(path, settingData, "setting data");
    }

    public bool LoadSettingData()
    {
        if (GameSettingManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameSettingManager.Instance is null.");
            return false;
        }

        string path = GetSettingPath();
        if (!TryReadJsonFile(path, out SettingData settingData, "setting data"))
        {
            return false;
        }

        settingData.schemaVersion = settingData.schemaVersion <= 0 ? SettingData.CurrentSchemaVersion : settingData.schemaVersion;
        GameSettingManager.Instance.ApplySettingData(settingData);
        return true;
    }

    public string GetGameSavePath(string profileId)
    {
        SaveFilePaths.EnsureSaveDirectory();
        return SaveFilePaths.GetGameSavePath(profileId);
    }

    public string GetSettingPath()
    {
        SaveFilePaths.EnsureSaveDirectory();
        return SaveFilePaths.GetSettingPath();
    }

    private bool TryWriteJsonFile<T>(string path, T data, string label)
    {
        try
        {
            string json = JsonUtility.ToJson(data, prettyPrint);
            File.WriteAllText(path, json);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameSaveManager] Failed to write {label}. Path: {path}. Error: {ex.Message}");
            return false;
        }
    }

    private bool TryReadJsonFile<T>(string path, out T data, string label) where T : class
    {
        data = null;

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[GameSaveManager] {label} file not found. Path: {path}");
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning($"[GameSaveManager] {label} file is empty. Path: {path}");
                return false;
            }

            data = JsonUtility.FromJson<T>(json);
            if (data == null)
            {
                Debug.LogWarning($"[GameSaveManager] Failed to parse {label}. Path: {path}");
                return false;
            }

            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameSaveManager] Failed to read {label}. Path: {path}. Error: {ex.Message}");
            return false;
        }
    }

    private string ResolveProfileId(string profileId)
    {
        return string.IsNullOrWhiteSpace(profileId) ? defaultProfileId : profileId;
    }

    private bool TryRejectDuplicateOrInvalidRoot()
    {
        GameManager rootManager = GetComponentInParent<GameManager>();
        if (rootManager == null)
        {
            Debug.LogWarning("[GameSaveManager] Parent GameManager not found. Destroying duplicate/orphan instance.");
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
