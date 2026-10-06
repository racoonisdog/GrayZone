using System.IO;
using UnityEngine;

/// <summary>GameDataManager가 생성한 저장 패킷을 JSON 파일로 기록하고 다시 불러오는 저장 입출력 매니저입니다.</summary>
public class GameSaveManager : MonoBehaviour
{
    private const string AutoProfileId = "autosave";

    public static GameSaveManager Instance { get; private set; }

    [Header("Save Settings")]
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

    /// <summary>현재 전역 정본을 게임오버 복구용 Auto 슬롯에 저장합니다.</summary>
    public bool SaveAutoGame()
    {
        return SaveGameData(AutoProfileId);
    }

    /// <summary>게임오버 복구용 Auto 슬롯을 전역 정본에 적용합니다.</summary>
    public bool LoadAutoGame()
    {
        return LoadGameData(AutoProfileId);
    }

    /// <summary>게임오버 복구용 Auto 슬롯 파일이 존재하는지 확인합니다.</summary>
    public bool HasAutoSave()
    {
        return File.Exists(SaveFilePaths.GetGameSavePath(AutoProfileId));
    }

    /* 기존 수동 저장 진입점은 현재 사용처가 없어 비활성화합니다.
    public bool SaveGame()
    {
        bool gameSaved = SaveGameData(SaveFilePaths.DefaultProfileId);
        bool settingSaved = SaveSettingData();
        return gameSaved && settingSaved;
    }

    public bool LoadGame()
    {
        bool gameLoaded = LoadGameData(SaveFilePaths.DefaultProfileId);
        bool settingLoaded = LoadSettingData();
        return gameLoaded && settingLoaded;
    }
    */

    private bool SaveGameData(string profileId)
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        if (GameDataManager.Instance.HasActiveShelterSceneDataManager
            && !GameDataManager.Instance.SyncFromShelter())
        {
            Debug.LogWarning("[GameSaveManager] 저장 전에 셸터 작업 데이터를 전역 정본에 반영하지 못했습니다.");
            return false;
        }

        SaveData saveData = GameDataManager.Instance.CreateSaveData();
        saveData.schemaVersion = SaveData.CurrentSchemaVersion;
        string path = GetGameSavePath(profileId);
        return TryWriteJsonFile(path, saveData, "game save");
    }

    private bool LoadGameData(string profileId)
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        string path = GetGameSavePath(profileId);
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

    /* 사용자 설정 저장/로드는 GameSettingManager가 직접 담당하므로 비활성화합니다.
    public bool SaveSettingData()
    {
        if (GameSettingManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameSettingManager.Instance is null.");
            return false;
        }

        return GameSettingManager.Instance.SaveSettings();
    }

    public bool LoadSettingData()
    {
        if (GameSettingManager.Instance == null)
        {
            Debug.LogWarning("[GameSaveManager] GameSettingManager.Instance is null.");
            return false;
        }

        return GameSettingManager.Instance.LoadSettings();
    }
    */

    /// <summary>지정한 프로필의 게임 저장 파일 절대 경로를 반환합니다.</summary>
    public string GetGameSavePath(string profileId)
    {
        SaveFilePaths.EnsureSaveDirectory();
        return SaveFilePaths.GetGameSavePath(profileId);
    }

    /* 사용자 설정 경로는 GameSettingManager가 직접 사용하므로 비활성화합니다.
    public string GetSettingPath()
    {
        SaveFilePaths.EnsureSaveDirectory();
        return SaveFilePaths.GetSettingPath();
    }
    */

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
