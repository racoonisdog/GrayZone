using UnityEngine;
using VInspector;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Managers")]
    [SerializeField] private GameDataManager gameDataManager;
    [SerializeField] private GameSettingManager gameSettingManager;

    [Foldout("Debug")]
    [Tooltip("켜면 디버그·치트 기능을 사용할 수 있는 상태로 게임을 시작합니다. " +
             "실제 활성화는 Editor 또는 Development Build에서만 이루어집니다.")]
    [SerializeField] private bool developMode = true;

    public GameDataManager DataManager => gameDataManager;
    public GameSettingManager SettingManager => gameSettingManager;

    /// <summary>
    /// 개발 모드 사용 여부입니다. 게임 수명 동안 이 값이 디버그 기능의 소유자입니다.
    /// </summary>
    /// <remarks>
    /// 런타임에 껐다 켜면 트레이너를 포함한 디버그 기능이 즉시 따라옵니다.
    /// 실제 기능 활성화 조건은 <see cref="GameDevMode.DebugFeaturesEnabled"/>가 실행 환경까지 함께 판단합니다.
    /// </remarks>
    public bool DevelopMode
    {
        get => developMode;
        set
        {
            developMode = value;
            GameDevMode.SetDevelopMode(value);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 개발 모드는 게임 전체 수명을 따르므로 전역 매니저가 소유합니다.
        // 이전에는 트레이너가 자기 활성 상태로 이 값을 켰는데, 그러면 트레이너를 만들지 여부를
        // 판단하는 쪽이 트레이너가 켜 줄 값을 봐야 하는 순환이 생깁니다.
        GameDevMode.SetDevelopMode(developMode);

        CacheChildManagers();
    }

    /// <summary>Inspector에서 개발 모드를 바꿨을 때 실행 중에도 즉시 반영합니다.</summary>
    private void OnValidate()
    {
        if (Application.isPlaying && Instance == this)
        {
            GameDevMode.SetDevelopMode(developMode);
        }
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
