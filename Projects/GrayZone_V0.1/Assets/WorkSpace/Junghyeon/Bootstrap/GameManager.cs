using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("하위 매니저")]
    [SerializeField] private GameDataManager gameDataManager;
    [SerializeField] private GameSaveManager gameSaveManager;
    [SerializeField] private GameSettingManager gameSettingManager;

    public GameDataManager DataManager => gameDataManager;
    public GameSaveManager SaveManager => gameSaveManager;
    public GameSettingManager SettingManager => gameSettingManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        CacheChildManagers();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }


    private void CacheChildManagers()
    {
        if (gameDataManager == null)
        {
            gameDataManager = GetComponentInChildren<GameDataManager>(true);
        }

        if (gameSaveManager == null)
        {
            gameSaveManager = GetComponentInChildren<GameSaveManager>(true);
        }

        if (gameSettingManager == null)
        {
            gameSettingManager = GetComponentInChildren<GameSettingManager>(true);
        }

        if (gameDataManager == null || gameSaveManager == null || gameSettingManager == null)
        {
            Debug.LogWarning("[GameManager] One or more child managers are missing.");
        }
    }
}
