using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 타이틀 화면의 반복 배경 영상을 준비하고 재생합니다.
/// </summary>
/// <remarks>
/// 타이틀 로고와 메뉴 버튼의 표시 애니메이션은 각 오브젝트에 부착된
/// <see cref="CanvasGroupFader"/>가 독립적으로 담당합니다.
/// </remarks>
public sealed class TitleScreenController : MonoBehaviour
{
    private const string TitleMusicEventPath = "event:/Music/Title/Main_Music";

    [Header("References")]
    [Tooltip("검은 배경 대신 반복 재생할 타이틀 배경 VideoPlayer입니다.")]
    [SerializeField] private VideoPlayer m_backgroundVideo;

    [Tooltip("VideoPlayer의 RenderTexture를 전체 화면에 표시할 RawImage입니다.")]
    [SerializeField] private RawImage m_backgroundImage;

    [Header("Title Prototype 1")]
    [Tooltip("메뉴 글자 왼쪽에 표시할 선택 장식 텍스처입니다.")]
    [SerializeField] private Texture2D m_menuSelectionLeftTexture;

    [Tooltip("메뉴 글자 오른쪽에 표시할 선택 장식 텍스처입니다.")]
    [SerializeField] private Texture2D m_menuSelectionRightTexture;

    [Tooltip("Title Prototype 1의 세로 메뉴 버튼들입니다.")]
    [SerializeField] private Button[] m_menuButtons;

    [Tooltip("메뉴 글자와 좌우 선택 장식 사이에 유지할 동일한 간격입니다.")]
    [SerializeField, Min(0f)] private float m_menuSelectionTextGap = 12f;

    [Tooltip("선택 장식이 나타나고 사라지는 시간입니다.")]
    [SerializeField, Min(0f)] private float m_menuSelectionFadeDuration = 0.15f;

    private RenderTexture m_backgroundRenderTexture;
    private EventInstance m_titleMusic;
    private bool m_loggedMissingTitleMusic;

    private void Awake()
    {
        ConfigureBackgroundVideo();
        ConfigureMenuSelection();

        // 메뉴 버튼 동작(새 게임·불러오기·나가기)은 따로 둔 컴포넌트가 맡습니다. 씬에 미리 붙어 있으면 그것을 씁니다.
        if (GetComponent<TitleMenuActions>() == null)
        {
            gameObject.AddComponent<TitleMenuActions>();
        }
    }

    private void OnEnable()
    {
        PlayBackgroundVideo();
        PlayTitleMusic();
    }

    private void Start()
    {
        // FMOD가 다른 Awake/OnEnable 초기화 뒤 준비되는 경우를 위해 한 번 더 확인합니다.
        PlayTitleMusic();
    }

    private void OnDisable()
    {
        StopTitleMusic();
    }

    private void OnDestroy()
    {
        StopTitleMusic();
        ReleaseBackgroundTexture();
    }

    /// <summary>타이틀 전용 FMOD BGM 루프를 시작합니다.</summary>
    private void PlayTitleMusic()
    {
        if (m_titleMusic.isValid() || !RuntimeManager.IsInitialized)
        {
            return;
        }

        try
        {
            m_titleMusic = RuntimeManager.CreateInstance(TitleMusicEventPath);
            if (m_titleMusic.isValid())
            {
                m_titleMusic.start();
            }
        }
        catch (EventNotFoundException exception)
        {
            if (m_loggedMissingTitleMusic)
            {
                return;
            }

            m_loggedMissingTitleMusic = true;
            Debug.LogWarning(
                $"[TitleScreen] FMOD 타이틀 음악 이벤트를 찾지 못했습니다: {TitleMusicEventPath}\n{exception.Message}",
                this
            );
        }
    }

    /// <summary>타이틀 씬을 벗어날 때 루프 인스턴스를 정리합니다.</summary>
    private void StopTitleMusic()
    {
        if (!m_titleMusic.isValid())
        {
            return;
        }

        m_titleMusic.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        m_titleMusic.release();
        m_titleMusic.clearHandle();
    }

    /// <summary>배경 영상을 처음부터 반복 재생합니다.</summary>
    public void PlayBackgroundVideo()
    {
        if (m_backgroundVideo == null || m_backgroundVideo.clip == null)
        {
            Debug.LogWarning("[TitleScreen] 배경 VideoPlayer 또는 VideoClip이 연결되지 않았습니다.", this);
            return;
        }

        if (!m_backgroundVideo.isPlaying)
        {
            m_backgroundVideo.Play();
        }
    }

    private void ConfigureBackgroundVideo()
    {
        if (m_backgroundVideo == null || m_backgroundImage == null)
        {
            Debug.LogWarning("[TitleScreen] 배경 VideoPlayer 또는 RawImage가 연결되지 않았습니다.", this);
            return;
        }

        int width = m_backgroundVideo.clip != null ? Mathf.Max(16, (int)m_backgroundVideo.clip.width) : 1920;
        int height = m_backgroundVideo.clip != null ? Mathf.Max(16, (int)m_backgroundVideo.clip.height) : 1080;

        m_backgroundRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = "Title Background Video",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        m_backgroundRenderTexture.Create();

        m_backgroundVideo.playOnAwake = true;
        m_backgroundVideo.isLooping = true;
        m_backgroundVideo.waitForFirstFrame = true;
        m_backgroundVideo.skipOnDrop = true;
        m_backgroundVideo.audioOutputMode = VideoAudioOutputMode.None;
        m_backgroundVideo.renderMode = VideoRenderMode.RenderTexture;
        m_backgroundVideo.targetTexture = m_backgroundRenderTexture;
        m_backgroundImage.texture = m_backgroundRenderTexture;
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

        if (m_backgroundImage != null && m_backgroundImage.texture == m_backgroundRenderTexture)
        {
            m_backgroundImage.texture = null;
        }

        m_backgroundRenderTexture.Release();
        Destroy(m_backgroundRenderTexture);
        m_backgroundRenderTexture = null;
    }

    /// <summary>
    /// 메뉴 선택 표시를 붙입니다. 표시 로직은 다른 메뉴와 함께 쓰는 <see cref="MenuSelectionIndicator"/>에 있습니다.
    /// </summary>
    /// <remarks>
    /// 장식 텍스처와 버튼은 이 컴포넌트의 인스펙터 값을 그대로 넘깁니다. 씬 설정을 옮기지 않고 같은 동작을 유지하기 위해서입니다.
    /// </remarks>
    private void ConfigureMenuSelection()
    {
        if (m_menuSelectionLeftTexture == null
            || m_menuSelectionRightTexture == null
            || m_menuButtons == null
            || m_menuButtons.Length == 0)
        {
            return;
        }

        MenuSelectionIndicator indicator = gameObject.AddComponent<MenuSelectionIndicator>();
        indicator.Configure(
            m_menuSelectionLeftTexture,
            m_menuSelectionRightTexture,
            m_menuButtons,
            m_menuSelectionTextGap,
            m_menuSelectionFadeDuration);
    }
}
