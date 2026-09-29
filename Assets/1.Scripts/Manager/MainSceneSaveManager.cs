using UnityEngine;

public class MainSceneSaveManager : MonoBehaviour
{
    public static MainSceneSaveManager Instance { get; private set; }

    [Header("New Game")]
    [SerializeField] private DefaultSaveData defaultSaveData;

    public DefaultSaveData DefaultSaveData => defaultSaveData;

    private void Awake()
    {
        if (TryRejectDuplicate())
        {
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[MainSceneSaveManager] GameDataManager.Instance is null.");
            return;
        }

        if (!GameDataManager.Instance.UseDefaultSaveDataOnShelterStart)
        {
            return;
        }

        // Temporary test hook for launching the Shelter scene directly.
        StartNewGame();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool StartNewGame()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[MainSceneSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        SaveData newGameSaveData = CreateNewGameSaveData();
        if (newGameSaveData == null)
        {
            return false;
        }

        GameDataManager.Instance.ApplySaveData(newGameSaveData);

        // Temporary test-scene sync. Scene managers should copy from GameDataManager on scene load later.
        if (ShelterSceneDataManager.Instance != null)
        {
            ShelterSceneDataManager.Instance.InitializeFromGameData();
        }

        return true;
    }

    public void OnStartNewGameButtonClicked()
    {
        StartNewGame();
    }

    public SaveData CreateNewGameSaveData()
    {
        if (defaultSaveData == null)
        {
            Debug.LogWarning("[MainSceneSaveManager] DefaultSaveData is not assigned.", this);
            return null;
        }

        return defaultSaveData.CreateSaveData();
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
