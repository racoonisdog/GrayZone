using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 셸터 씬의 주요 매니저 참조를 연결하고 UI 상태에 따라 플레이어 조작 잠금을 적용하는 씬 조립 루트
/// </summary>
[DefaultExecutionOrder(-100)]
public class ShelterSceneManager : MonoBehaviour
{
    /// <summary>현재 셸터 씬 매니저 싱글톤 인스턴스</summary>
    public static ShelterSceneManager Instance { get; private set; }

    [Header("Managers")]
    [FormerlySerializedAs("m_ShelterDataManager")]
    [SerializeField] private ShelterSceneDataManager m_ShelterSceneDataManager;
    [SerializeField] private UIManager m_UIManager;
    [SerializeField] private MedicalManager m_MedicalManager;
    [SerializeField] private bool m_copyDataFromGameDataManagerOnAwake = true;


    // ToDo : 캐릭터 컨트롤러 매니저 크게 렙핑 필요
    [Header("Player Control")]
    [SerializeField] private PlayerMove m_PlayerMove;
    [SerializeField] private CameraLook m_CameraLook;
    [SerializeField] private bool m_autoFindReferences = true;

    /// <summary>셸터 씬의 작업 데이터 매니저 참조</summary>
    public ShelterSceneDataManager ShelterSceneDataManager => m_ShelterSceneDataManager;

    /// <summary>셸터 씬 UI 매니저 참조</summary>
    public UIManager UIManager => m_UIManager;

    /// <summary>셸터 씬 의료 시설 매니저 참조</summary>
    public MedicalManager MedicalManager => m_MedicalManager;

    /// <summary>UI 등으로 플레이어 이동/시점 조작이 잠겨 있는지 여부</summary>
    public bool IsPlayerControlLocked { get; private set; }

    private void Reset()
    {
        CacheSceneReferences();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (m_autoFindReferences)
            CacheSceneReferences();

        if (m_copyDataFromGameDataManagerOnAwake)
            CopyShelterDataFromGameDataManager();
    }

    private void OnEnable()
    {
        if (Instance != this)
            return;

        if (m_UIManager != null)
            m_UIManager.ActiveUIChanged += HandleActiveUIChanged;

        ApplyPlayerControlLock(m_UIManager != null && ShouldLockPlayerControl(m_UIManager.ActiveUI));
    }

    private void OnDisable()
    {
        if (m_UIManager != null)
            m_UIManager.ActiveUIChanged -= HandleActiveUIChanged;

        if (Instance == this)
            ApplyPlayerControlLock(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 글로벌 게임 데이터 매니저의 현재 스냅샷을 셸터 작업 데이터로 복사
    /// </summary>
    public void CopyShelterDataFromGameDataManager()
    {
        if (m_ShelterSceneDataManager == null)
            m_ShelterSceneDataManager = ShelterSceneDataManager.Instance;

        if (m_ShelterSceneDataManager == null)
        {
            Debug.LogWarning("[ShelterSceneManager] ShelterSceneDataManager is not available.", this);
            return;
        }

        m_ShelterSceneDataManager.InitializeFromGameData();
    }

    private void HandleActiveUIChanged(ShelterUIType activeUI)
    {
        ApplyPlayerControlLock(ShouldLockPlayerControl(activeUI));
    }

    private bool ShouldLockPlayerControl(ShelterUIType activeUI)
    {
        return activeUI != ShelterUIType.None;
    }

    private void ApplyPlayerControlLock(bool locked)
    {
        IsPlayerControlLocked = locked;

        if (m_PlayerMove != null)
            m_PlayerMove.SetMoveLocked(locked);

        if (m_CameraLook != null)
            m_CameraLook.SetLookLocked(locked);
    }

    private void CacheSceneReferences()
    {
        if (m_ShelterSceneDataManager == null)
            m_ShelterSceneDataManager = FindFirstObjectByType<ShelterSceneDataManager>();

        if (m_UIManager == null)
            m_UIManager = FindFirstObjectByType<UIManager>();

        if (m_PlayerMove == null)
            m_PlayerMove = FindFirstObjectByType<PlayerMove>();

        if (m_CameraLook == null)
            m_CameraLook = FindFirstObjectByType<CameraLook>();
    }
}
