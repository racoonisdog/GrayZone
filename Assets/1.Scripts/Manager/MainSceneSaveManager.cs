using UnityEngine;
using UnityEngine.SceneManagement;

public class MainSceneSaveManager : MonoBehaviour
{
    private const string ShelterSceneName = "ShelterScene_Jung";

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
            // 실제 게임 흐름: ShelterSceneManager.Awake에서 복사한 기존 GameDataManager 데이터를 유지한다.
            // Shelter 복귀 데이터가 초기화되는 문제를 점검할 때는 이 플래그가 false인지 먼저 확인할 것.
            return;
        }

        // 테스트 흐름: Shelter 단독 실행/반복 테스트를 위해 DefaultSaveData를 의도적으로 다시 적용한다.
        // 따라서 이 플래그가 true인 동안 Scene 복귀 전 데이터가 덮이는 것은 미수정 버그가 아니라 예상 동작이다.
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

        GameDataManager.Instance.InitializeCharacters(defaultSaveData.CreateStartingCharacterSnapshots());
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
        if (StartNewGame())
        {
            SceneManager.LoadScene(ShelterSceneName);
        }
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
