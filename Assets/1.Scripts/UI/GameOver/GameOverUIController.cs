using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 작전 실패(게임오버) 오버레이입니다. 스쿼드 전멸이나 방어전 정문 파괴로 전투가 끝났을 때 표시합니다.
/// </summary>
/// <remarks>
/// 디자인 기준: Figma `Misson Fail Prototype 2_3`. 제목("작전 실패")과 "작전 중단. 퇴각합니다." 문구는 배경 이미지에 들어 있고,
/// 아래에 "마지막 저장 지점 부터"와 "타이틀로" 메뉴가 있습니다. 메뉴 선택 장식은 타이틀 화면과 같은
/// <see cref="MenuSelectionIndicator"/>가 그립니다.
///
/// 표시되는 동안 <see cref="Time.timeScale"/>을 0으로 멈춥니다. 시간이 멈춰 있으면 ESC 일시정지 메뉴도 열리지 않습니다.
/// 메뉴를 누르면 시간을 되돌린 뒤 씬을 옮깁니다.
///
/// 귀환 정산(Result UI)과 별개의 화면입니다. 기획 `전투 시스템` §5.9.2가 전멸을 정상 철수와
/// 다른 종료로 규정하므로 Result UI를 재사용하지 않고, 정산값(FinalizeField)도 만들지 않습니다.
/// 전멸 감지는 <see cref="SquadManager.OnSquadEliminated"/>, 상태 고정은 <see cref="CombatSceneDataManager"/>,
/// 화면 전환 결정은 <see cref="CombatSceneManager"/>가 맡습니다.
/// </remarks>
[DisallowMultipleComponent]
public class GameOverUIController : MonoBehaviour
{
    [Header("Background")]
    [Tooltip("선택 사항입니다. 배경 영상(예: Mission_fail.mp4)을 재생할 VideoPlayer입니다. 시간이 멈춰도 재생되도록 실제 시간으로 돌립니다.")]
    [SerializeField] private VideoPlayer m_backgroundVideo;

    [Tooltip("배경 영상을 전체 화면에 표시할 RawImage입니다. 영상이 없으면 이 RawImage의 텍스처를 그대로 씁니다.")]
    [SerializeField] private RawImage m_backgroundImage;

    [Tooltip("배경 영상을 이 시간(초)에서 멈추고 그 장면을 유지합니다. Mission_fail.mp4는 끝에서 검게 사라지므로 밝은 구간(약 0.8~2.7초)에서 멈춥니다. 0 이하이면 끝까지 재생합니다.")]
    [SerializeField, Min(0f)] private float m_backgroundHoldTime = 2.3f;

    [Header("Menu")]
    [Tooltip("마지막 저장을 불러와 셸터로 돌아가는 버튼입니다(\"마지막 저장 지점 부터\").")]
    [SerializeField] private Button m_retryFromSaveButton;

    [Tooltip("타이틀 화면으로 가는 버튼입니다(\"타이틀로\").")]
    [SerializeField] private Button m_titleButton;

    [Tooltip("메뉴 버튼 양옆에 선택 장식을 띄우는 컴포넌트입니다. 타이틀 화면과 같은 방식입니다.")]
    [SerializeField] private MenuSelectionIndicator m_selectionIndicator;

    [Tooltip("선택 사항입니다. 사유 문구를 따로 보여 줄 텍스트입니다. 디자인상 문구가 배경에 들어 있으면 비워 둡니다.")]
    [SerializeField] private TextMeshProUGUI m_messageText;

    [Header("Scenes")]
    [Tooltip("\"마지막 저장 지점 부터\"를 눌렀을 때 저장을 불러온 뒤 갈 씬 이름입니다. 빌드 설정에 있어야 합니다.")]
    [SerializeField] private string m_retrySceneName = "ShelterScene_Jung";

    [Tooltip("\"타이틀로\"를 눌렀을 때 갈 씬 이름입니다. 빌드 설정에 있어야 합니다.")]
    [SerializeField] private string m_titleSceneName = "TitleScene";

    [Header("Reset")]
    [Tooltip("켜면 \"타이틀로\"를 누를 때 게임 데이터·플레이어 설정을 가진 GameManager를 지우고 부트스트랩 씬부터 다시 시작합니다. 게임을 처음 켠 상태가 됩니다. 데모 종료 화면에서 씁니다.")]
    [SerializeField] private bool m_resetGameOnTitle;

    [Tooltip("초기화할 때 불러올 첫 씬 이름입니다. 이 씬이 새 GameManager를 만듭니다. 빌드 설정에 있어야 합니다.")]
    [SerializeField] private string m_bootSceneName = "BootstrapScene";

    [Header("Behaviour")]
    [Tooltip("켜면 화면이 떠 있는 동안 시간을 멈춥니다.")]
    [SerializeField] private bool m_pauseTimeWhileShown = true;

    [Tooltip("스쿼드 전멸로 실패했을 때의 사유 문구입니다. 사유 텍스트가 있을 때만 씁니다.")]
    [SerializeField] private string m_squadEliminatedMessage = "스쿼드 전원이 전투에서 이탈했습니다.";

    private float m_timeScaleBeforeShow = 1.0f;
    private bool m_pausedTime;
    private bool m_isLeaving;
    private RenderTexture m_backgroundRenderTexture;

    /// <summary>메뉴를 눌러 씬을 떠나기 직전에 발생합니다. 인자는 갈 씬 이름입니다.</summary>
    public event Action<string> OnConfirmed;

    /// <summary>게임오버 화면이 현재 표시 중인지 여부입니다.</summary>
    public bool IsShown => gameObject.activeSelf;

    private void Awake()
    {
        if (m_retryFromSaveButton != null)
        {
            m_retryFromSaveButton.onClick.AddListener(RetryFromLastSave);
        }

        if (m_titleButton != null)
        {
            m_titleButton.onClick.AddListener(GoToTitle);
        }
    }

    private void Update()
    {
        // 시간이 멈춰 있어도 Update는 돕니다. 영상이 밝은 구간에 닿으면 멈춰 그 장면을 유지합니다.
        if (m_backgroundVideo != null
            && m_backgroundHoldTime > 0f
            && m_backgroundVideo.isPlaying
            && m_backgroundVideo.time >= m_backgroundHoldTime)
        {
            m_backgroundVideo.Pause();
        }
    }

    private void OnDisable()
    {
        // 화면이 꺼질 때 멈춘 시간이 남지 않게 합니다.
        RestoreTime();
        MissionOverlayVisibility.Unregister(this);

        if (m_backgroundVideo != null)
        {
            m_backgroundVideo.Stop();
        }
    }

    private void OnDestroy()
    {
        ReleaseBackgroundTexture();
    }

    /// <summary>배경 영상을 처음부터 실제 시간 기준으로 재생합니다. 시간이 멈춘 동안에도 흐릅니다.</summary>
    private void PlayBackgroundVideo()
    {
        if (m_backgroundImage == null)
        {
            return;
        }

        if (m_backgroundVideo == null || m_backgroundVideo.clip == null)
        {
            // 텍스처가 없는 RawImage는 흰색 사각형으로 그려져 화면 전체가 하얗게 덮입니다.
            // 영상 참조가 끊겼으면(예: 영상 에셋 GUID가 바뀜) 끄고 뒤의 검은 배경을 보이게 합니다.
            if (m_backgroundImage.texture == null)
            {
                m_backgroundImage.enabled = false;
                Debug.LogWarning("[GameOverUIController] 배경 영상 클립이 연결되지 않아 배경 이미지를 끕니다. VideoPlayer의 Video Clip을 확인하세요.", this);
            }

            return;
        }

        m_backgroundImage.enabled = true;

        if (m_backgroundRenderTexture == null)
        {
            int width = Mathf.Max(16, (int)m_backgroundVideo.clip.width);
            int height = Mathf.Max(16, (int)m_backgroundVideo.clip.height);
            m_backgroundRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Game Over Background Video",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_backgroundRenderTexture.Create();
        }

        m_backgroundVideo.playOnAwake = false;
        m_backgroundVideo.isLooping = false;
        m_backgroundVideo.waitForFirstFrame = true;
        m_backgroundVideo.audioOutputMode = VideoAudioOutputMode.None;
        m_backgroundVideo.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        m_backgroundVideo.renderMode = VideoRenderMode.RenderTexture;
        m_backgroundVideo.targetTexture = m_backgroundRenderTexture;
        m_backgroundImage.texture = m_backgroundRenderTexture;

        m_backgroundVideo.Stop();
        m_backgroundVideo.time = 0.0;
        m_backgroundVideo.Play();
    }

    private void ReleaseBackgroundTexture()
    {
        if (m_backgroundRenderTexture == null)
        {
            return;
        }

        if (m_backgroundVideo != null && m_backgroundVideo.targetTexture == m_backgroundRenderTexture)
        {
            m_backgroundVideo.targetTexture = null;
        }

        m_backgroundRenderTexture.Release();
        Destroy(m_backgroundRenderTexture);
        m_backgroundRenderTexture = null;
    }

    /// <summary>
    /// 스쿼드 전멸 사유로 게임오버 화면을 표시합니다.
    /// </summary>
    /// <remarks>
    /// 입력 모드 전환은 이 메서드가 하지 않습니다. 호출자가 게임플레이 입력을 먼저 차단해야 합니다.
    /// </remarks>
    public void ShowSquadEliminated()
    {
        Show(m_squadEliminatedMessage);
    }

    /// <summary>
    /// 지정한 사유 문구로 게임오버 화면을 표시하고 시간을 멈춥니다.
    /// </summary>
    /// <param name="message">사유 텍스트가 있을 때 표시할 문구입니다. 비어 있으면 기존 문구를 유지합니다.</param>
    public void Show(string message)
    {
        if (m_messageText != null && !string.IsNullOrEmpty(message))
        {
            m_messageText.text = message;
        }

        m_isLeaving = false;
        gameObject.SetActive(true);
        MissionOverlayVisibility.Register(this);
        m_selectionIndicator?.ResetSelection();
        PlayBackgroundVideo();

        if (m_pauseTimeWhileShown && !m_pausedTime)
        {
            m_timeScaleBeforeShow = Time.timeScale > 0.0f ? Time.timeScale : 1.0f;
            Time.timeScale = 0.0f;
            m_pausedTime = true;
        }

        // 필드 EventSystem을 켜 두지 않으면 버튼 클릭이 들어오지 않습니다.
        // 메서드 이름은 Result UI 기준이지만 동작은 오버레이 공용입니다.
        TestSceneUiEventSystemBridge.EnableForResultUI();
    }

    /// <summary>게임오버 화면을 숨기고 멈춘 시간을 되돌립니다.</summary>
    /// <remarks>
    /// 인게임 HUD는 여기서 끄지 않습니다. 조준선 패널이 이 캔버스보다 아래에 있어
    /// UI 레이어가 가립니다. 자세한 배경은 <see cref="ResultUIController.Hide"/>에 적어 두었습니다.
    /// </remarks>
    public void Hide()
    {
        RestoreTime();
        gameObject.SetActive(false);
    }

    /// <summary>"마지막 저장 지점 부터": 마지막 저장을 불러온 뒤 셸터 씬으로 갑니다.</summary>
    /// <remarks>저장을 불러오지 못해도 씬은 옮깁니다. 이 경우 지금 메모리의 진행 상태로 셸터에 들어갑니다.</remarks>
    public void RetryFromLastSave()
    {
        if (m_isLeaving)
        {
            return;
        }

        GameSaveManager saveManager = FindFirstObjectByType<GameSaveManager>(FindObjectsInactive.Include);
        if (saveManager == null || !saveManager.LoadAutoGame())
        {
            Debug.LogWarning("[GameOverUIController] 마지막 저장을 불러오지 못했습니다. 현재 진행 상태로 셸터에 들어갑니다.", this);
        }

        LeaveTo(m_retrySceneName);
    }

    /// <summary>"타이틀로": 타이틀 씬으로 갑니다.</summary>
    public void GoToTitle()
    {
        if (m_isLeaving)
        {
            return;
        }

        if (m_resetGameOnTitle && Application.CanStreamedLevelBeLoaded(m_bootSceneName))
        {
            // 설정은 파일에 남으므로 먼저 기본값으로 되돌려 저장합니다. 새 GameManager가 이 파일을 읽습니다.
            GameSettingManager.Instance?.ResetToDefaultsAndSave();

            // 씬을 넘어 유지되던 GameManager(게임 데이터·설정)를 지우고 부트스트랩부터 다시 시작합니다.
            // Destroy는 이번 프레임 끝에 처리되고 씬 로드는 다음 프레임에 일어나므로, 부트스트랩의 새 GameManager가 남습니다.
            if (GameManager.Instance != null)
            {
                Destroy(GameManager.Instance.gameObject);
            }

            LeaveTo(m_bootSceneName);
            return;
        }

        LeaveTo(m_titleSceneName);
    }

    private void LeaveTo(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[GameOverUIController] 이동할 씬 이름이 비어 있습니다.", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[GameOverUIController] '{sceneName}' 씬이 빌드 설정에 없어 이동할 수 없습니다.", this);
            return;
        }

        m_isLeaving = true;
        OnConfirmed?.Invoke(sceneName);
        RestoreTime();
        SceneManager.LoadScene(sceneName);
    }

    private void RestoreTime()
    {
        if (!m_pausedTime)
        {
            return;
        }

        Time.timeScale = m_timeScaleBeforeShow;
        m_pausedTime = false;
    }
}
