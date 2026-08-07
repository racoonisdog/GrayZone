using System.IO;
using UnityEngine;

/// <summary>GameDataManager가 생성한 저장 패킷을 JSON 파일로 기록하고 다시 불러오는 저장 입출력 매니저입니다.</summary>
public class GameSaveManager : MonoBehaviour
{
    public static GameSaveManager Instance { get; private set; }

    [Header("Save Settings")]
    [Tooltip("프로필 ID가 지정되지 않았을 때 사용할 기본 저장 프로필 ID입니다.")]
    [SerializeField] private string defaultProfileId = SaveFilePaths.DefaultProfileId;
    [Tooltip("JSON 저장 파일을 사람이 읽기 쉬운 들여쓰기 형식으로 기록할지 여부입니다.")]
    [SerializeField] private bool prettyPrint = true;

    private void Awake()
    {
        if (TryRejectDuplicate())
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

    /// <summary>기본 프로필의 게임 데이터와 사용자 설정을 함께 저장합니다.</summary>
    public bool SaveGame()
    {
        bool gameSaved = SaveGameData(defaultProfileId);
        bool settingSaved = SaveSettingData();
        return gameSaved && settingSaved;
    }

    /// <summary>기본 프로필의 게임 데이터와 사용자 설정을 함께 불러옵니다.</summary>
    public bool LoadGame()
    {
        bool gameLoaded = LoadGameData(defaultProfileId);
        bool settingLoaded = LoadSettingData();
        return gameLoaded && settingLoaded;
    }

    /// <summary>지정한 프로필의 전역 정본을 독립된 저장 패킷으로 만들어 JSON 파일에 기록합니다.</summary>
    public bool SaveGameData(string profileId)
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        string resolvedProfileId = ResolveProfileId(profileId);
        if (GameDataManager.Instance.HasActiveShelterSceneDataManager
            && !GameDataManager.Instance.SyncFromShelter())
        {
            Debug.LogWarning("[GameSaveManager] 저장 전에 셸터 작업 데이터를 전역 정본에 반영하지 못했습니다.");
            return false;
        }

        SaveData saveData = GameDataManager.Instance.CreateSaveData(resolvedProfileId);
        saveData.schemaVersion = SaveData.CurrentSchemaVersion;
        string path = GetGameSavePath(resolvedProfileId);
        return TryWriteJsonFile(path, saveData, "game save");
    }

    /// <summary>지정한 프로필의 JSON 저장 파일을 읽어 GameDataManager의 평탄 정본에 적용합니다.</summary>
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

        if (saveData.schemaVersion != SaveData.CurrentSchemaVersion)
        {
            Debug.LogWarning(
                $"[GameSaveManager] 지원하지 않는 게임 저장 버전입니다. "
                + $"현재={SaveData.CurrentSchemaVersion}, "
                + $"파일={saveData.schemaVersion}. 새 게임을 시작하세요.");
            return false;
        }

        GameDataManager.Instance.ApplySaveData(saveData);
        if (ShelterSceneDataManager.Instance != null)
        {
            ShelterSceneDataManager.Instance.InitializeFromGameData();
        }

        return true;
    }

    /// <summary>현재 사용자 설정을 별도 JSON 파일에 저장합니다.</summary>
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

    /// <summary>사용자 설정 JSON 파일을 읽어 GameSettingManager에 적용합니다.</summary>
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

    /// <summary>지정한 프로필의 게임 저장 파일 절대 경로를 반환합니다.</summary>
    public string GetGameSavePath(string profileId)
    {
        SaveFilePaths.EnsureSaveDirectory();
        return SaveFilePaths.GetGameSavePath(profileId);
    }

    /// <summary>사용자 설정 저장 파일 절대 경로를 반환합니다.</summary>
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

    private bool TryRejectDuplicate()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
