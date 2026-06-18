using UnityEngine;

public class ShelterSceneManager : MonoBehaviour
{
    public static ShelterSceneManager Instance { get; private set; }

    [Header("매니저")]
    [SerializeField] private UIManager m_UIManager;
    //ToDo : 셸터 전반을 관리하는 매니저 만들기, MedicalManager를 여기서 가지고 있는거는 임시 테스트용
    [SerializeField] private MedicalManager m_MedicalManager;


    //ToDo : PlayerControlManager로 묶어서 관리하도록 확장 하기
    [Header("Player Control")]
    [SerializeField] private PlayerMove m_PlayerMove;
    [SerializeField] private CameraLook m_CameraLook;
    [SerializeField] private bool m_autoFindReferences = true;

    public UIManager UIManager => m_UIManager;
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
        if (m_UIManager == null)
            m_UIManager = FindFirstObjectByType<UIManager>();

        if (m_PlayerMove == null)
            m_PlayerMove = FindFirstObjectByType<PlayerMove>();

        if (m_CameraLook == null)
            m_CameraLook = FindFirstObjectByType<CameraLook>();
    }
}
