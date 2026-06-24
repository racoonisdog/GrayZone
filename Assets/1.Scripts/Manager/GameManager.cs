using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("하위 매니저")]
    [SerializeField] private GameDataManager gameDataManager;
    [SerializeField] private GameSettingManager gameSettingManager;

    public GameDataManager DataManager => gameDataManager;
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

        if (gameSettingManager == null)
        {
            gameSettingManager = GetComponentInChildren<GameSettingManager>(true);
        }

        if (gameDataManager == null || gameSettingManager == null)
        {
            Debug.LogWarning("[GameManager] DataManager or SettingManager is missing.");
        }
    }
}
