using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 셸터 첫 진입 컷신 영상을 전체 화면으로 한 번 재생하고, 끝나면 완료 콜백을 부릅니다.
/// </summary>
/// <remarks>
/// 재생할지는 <see cref="ShelterFlowController"/>가 정합니다. 이 컴포넌트는 재생, 스킵, 입력 차단만 맡습니다.
/// 재생하는 동안 플레이어 입력 게이트를 닫고 일시정지 메뉴(Escape)를 끕니다.
/// Time.timeScale은 건드리지 않습니다. timeScale이 0이면 VideoPlayer가 Unscaled 모드여도 첫 프레임에서 멈추기 때문입니다.
/// GIF는 Unity가 바로 재생하지 못하므로 mp4(H.264)나 webm으로 바꿔서 연결합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class ShelterIntroCutscene : MonoBehaviour
{
    [Header("References")]
    [Tooltip("컷신 영상을 재생할 VideoPlayer입니다. Video Clip을 여기에 연결합니다.")]
    [SerializeField] private VideoPlayer m_videoPlayer;

    [Tooltip("영상 RenderTexture를 전체 화면에 표시할 RawImage입니다.")]
    [SerializeField] private RawImage m_videoImage;

    [Tooltip("컷신 UI 전체의 루트입니다. 재생할 때만 켭니다. 비우면 RawImage의 GameObject를 사용합니다.")]
    [SerializeField] private GameObject m_root;

    [Tooltip("선택 사항입니다. 스킵 안내 문구 등 스킵할 수 있을 때만 보여 줄 오브젝트입니다.")]
    [SerializeField] private GameObject m_skipHint;

    [Header("Playback")]
    [Tooltip("재생하는 동안 Escape 일시정지 메뉴를 막습니다.")]
    [SerializeField] private bool m_blockPauseMenuWhilePlaying = true;

    [Tooltip("켜면 영상 소리를 출력합니다. 끄면 무음으로 재생합니다.")]
    [SerializeField] private bool m_playAudio = true;

    [Header("Skip")]
    [Tooltip("스킵을 허용합니다.")]
    [SerializeField] private bool m_allowSkip = true;

    [Tooltip("재생을 시작하고 이 시간(실제 시간, 초)이 지나야 스킵할 수 있습니다.")]
    [SerializeField, Min(0f)] private float m_skipAvailableAfter = 1.0f;

    [Tooltip("영상 준비가 이 시간(실제 시간, 초) 안에 끝나지 않으면 컷신을 건너뜁니다.")]
    [SerializeField, Min(1f)] private float m_prepareTimeout = 5.0f;

    private RenderTexture m_renderTexture;
    private Action m_onFinished;
    private Coroutine m_playRoutine;
    private readonly System.Collections.Generic.List<PlayerInputController> m_gatedInputs = new();
    private readonly System.Collections.Generic.List<GameplayPauseMenuController> m_disabledPauseMenus = new();
    private readonly System.Collections.Generic.List<(PlayerMove move, bool wasLocked)> m_lockedMoves = new();
    private bool m_isPlaying;
    private bool m_finishRequested;

    /// <summary>컷신이 재생 중인지 여부입니다.</summary>
    public bool IsPlaying => m_isPlaying;

    /// <summary>재생할 영상이 연결되어 있는지 여부입니다.</summary>
    public bool HasClip => m_videoPlayer != null && m_videoPlayer.clip != null;

    private GameObject Root => m_root != null ? m_root : (m_videoImage != null ? m_videoImage.gameObject : null);

    private void Awake()
    {
        SetVisible(false);

        if (m_videoPlayer != null)
        {
            m_videoPlayer.playOnAwake = false;
            m_videoPlayer.Stop();
        }
    }

    private void OnDisable()
    {
        // 재생 중에 꺼지거나 씬을 떠나도 시간과 입력이 멈춘 채로 남지 않게 합니다.
        if (m_isPlaying)
        {
            Finish(invokeCallback: false);
        }
    }

    private void OnDestroy()
    {
        ReleaseRenderTexture();
    }

    /// <summary>컷신을 처음부터 재생합니다. 끝나거나 스킵되면 <paramref name="onFinished"/>를 한 번 부릅니다.</summary>
    /// <remarks>영상이 연결되지 않았거나 준비에 실패하면 바로 <paramref name="onFinished"/>를 부릅니다.</remarks>
    public void Play(Action onFinished)
    {
        if (m_isPlaying)
        {
            Debug.LogWarning("[ShelterIntroCutscene] 이미 재생 중입니다.", this);
            return;
        }

        if (!HasClip || m_videoImage == null)
        {
            Debug.LogWarning("[ShelterIntroCutscene] VideoPlayer, Video Clip 또는 RawImage가 연결되지 않아 컷신을 건너뜁니다.", this);
            onFinished?.Invoke();
            return;
        }

        m_onFinished = onFinished;
        m_playRoutine = StartCoroutine(PlayRoutine());
    }

    /// <summary>재생 중인 컷신을 바로 끝냅니다.</summary>
    public void Skip()
    {
        if (m_isPlaying)
        {
            m_finishRequested = true;
        }
    }

    private IEnumerator PlayRoutine()
    {
        m_isPlaying = true;
        m_finishRequested = false;
        BeginBlockingGameplay();
        PrepareRenderTarget();
        SetVisible(true);
        SetSkipHintVisible(false);

        m_videoPlayer.loopPointReached += HandleLoopPointReached;
        m_videoPlayer.errorReceived += HandleErrorReceived;
        m_videoPlayer.Prepare();

        // 씬 로딩 직후에는 한 프레임이 몇 초씩 걸릴 수 있습니다. 실제 경과 시간으로 재면 준비가 끝나기 전에
        // 타임아웃이 나 버리므로, 프레임마다 최대 0.1초까지만 더해서 멈춘 프레임이 타임아웃을 다 쓰지 않게 합니다.
        float prepareElapsed = 0f;
        while (!m_videoPlayer.isPrepared && !m_finishRequested)
        {
            prepareElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (prepareElapsed > m_prepareTimeout)
            {
                Debug.LogWarning("[ShelterIntroCutscene] 영상 준비 시간이 초과되어 컷신을 건너뜁니다.", this);
                m_finishRequested = true;
                break;
            }

            yield return null;
        }

        if (!m_finishRequested)
        {
            m_videoPlayer.Play();
        }

        float playStart = Time.unscaledTime;
        while (!m_finishRequested)
        {
            bool canSkip = m_allowSkip && Time.unscaledTime - playStart >= m_skipAvailableAfter;
            SetSkipHintVisible(canSkip);

            if (canSkip && WasSkipPressed())
            {
                m_finishRequested = true;
                break;
            }

            yield return null;
        }

        // 스킵 키(Escape 등)를 누른 프레임에 일시정지 메뉴를 다시 켜면 같은 입력으로 열릴 수 있어 한 프레임 넘깁니다.
        yield return null;

        m_playRoutine = null;
        Finish(invokeCallback: true);
    }

    private void Finish(bool invokeCallback)
    {
        if (m_playRoutine != null)
        {
            StopCoroutine(m_playRoutine);
            m_playRoutine = null;
        }

        if (m_videoPlayer != null)
        {
            m_videoPlayer.loopPointReached -= HandleLoopPointReached;
            m_videoPlayer.errorReceived -= HandleErrorReceived;
            m_videoPlayer.Stop();
        }

        SetSkipHintVisible(false);
        SetVisible(false);
        EndBlockingGameplay();
        ReleaseRenderTexture();
        m_isPlaying = false;

        Action callback = m_onFinished;
        m_onFinished = null;
        if (invokeCallback)
        {
            callback?.Invoke();
        }
    }

    private void HandleLoopPointReached(VideoPlayer source)
    {
        m_finishRequested = true;
    }

    private void HandleErrorReceived(VideoPlayer source, string message)
    {
        Debug.LogWarning($"[ShelterIntroCutscene] 영상 재생 오류로 컷신을 건너뜁니다: {message}", this);
        m_finishRequested = true;
    }

    private void PrepareRenderTarget()
    {
        VideoClip clip = m_videoPlayer.clip;
        int width = Mathf.Max(16, (int)clip.width);
        int height = Mathf.Max(16, (int)clip.height);

        if (m_renderTexture == null || m_renderTexture.width != width || m_renderTexture.height != height)
        {
            ReleaseRenderTexture();
            m_renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Shelter Intro Cutscene",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            m_renderTexture.Create();
        }

        // 첫 프레임이 나오기 전에 이전 내용이나 쓰레기 값이 비치지 않게 검게 비웁니다.
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = m_renderTexture;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = previous;

        m_videoPlayer.isLooping = false;
        m_videoPlayer.waitForFirstFrame = true;
        m_videoPlayer.skipOnDrop = true;
        m_videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        m_videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        m_videoPlayer.targetTexture = m_renderTexture;
        m_videoPlayer.audioOutputMode = m_playAudio ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.None;
        m_videoImage.texture = m_renderTexture;
    }

    private void ReleaseRenderTexture()
    {
        if (m_renderTexture == null)
        {
            return;
        }

        if (m_videoPlayer != null && m_videoPlayer.targetTexture == m_renderTexture)
        {
            m_videoPlayer.targetTexture = null;
        }

        if (m_videoImage != null && m_videoImage.texture == m_renderTexture)
        {
            m_videoImage.texture = null;
        }

        m_renderTexture.Release();
        Destroy(m_renderTexture);
        m_renderTexture = null;
    }

    private void BeginBlockingGameplay()
    {
        // 셸터에는 SquadManager가 없을 수 있어, 씬의 플레이어 입력을 직접 닫습니다.
        m_gatedInputs.Clear();
        PlayerInputController[] inputs = FindObjectsByType<PlayerInputController>(FindObjectsSortMode.None);
        for (int i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] == null)
                continue;

            inputs[i].SetInputGate(false);
            m_gatedInputs.Add(inputs[i]);
        }

        // 셸터 캐릭터의 PlayerMove는 키보드를 직접 읽어 입력 게이트를 거치지 않습니다.
        // 잠그지 않으면 스킵용 Space가 점프로도 들어가므로, 원래 잠금 상태를 기억해 두고 잠급니다.
        m_lockedMoves.Clear();
        PlayerMove[] moves = FindObjectsByType<PlayerMove>(FindObjectsSortMode.None);
        for (int i = 0; i < moves.Length; i++)
        {
            if (moves[i] == null)
                continue;

            m_lockedMoves.Add((moves[i], moves[i].IsMoveLocked));
            moves[i].SetMoveLocked(true);
        }

        m_disabledPauseMenus.Clear();
        if (m_blockPauseMenuWhilePlaying)
        {
            GameplayPauseMenuController[] pauseMenus = FindObjectsByType<GameplayPauseMenuController>(FindObjectsSortMode.None);
            for (int i = 0; i < pauseMenus.Length; i++)
            {
                if (pauseMenus[i] == null || !pauseMenus[i].enabled)
                    continue;

                pauseMenus[i].enabled = false;
                m_disabledPauseMenus.Add(pauseMenus[i]);
            }
        }
    }

    private void EndBlockingGameplay()
    {
        for (int i = 0; i < m_disabledPauseMenus.Count; i++)
        {
            if (m_disabledPauseMenus[i] != null)
                m_disabledPauseMenus[i].enabled = true;
        }

        m_disabledPauseMenus.Clear();

        for (int i = 0; i < m_gatedInputs.Count; i++)
        {
            if (m_gatedInputs[i] != null)
                m_gatedInputs[i].SetInputGate(true);
        }

        m_gatedInputs.Clear();

        for (int i = 0; i < m_lockedMoves.Count; i++)
        {
            if (m_lockedMoves[i].move != null)
                m_lockedMoves[i].move.SetMoveLocked(m_lockedMoves[i].wasLocked);
        }

        m_lockedMoves.Clear();
    }

    private void SetVisible(bool visible)
    {
        GameObject root = Root;
        if (root != null && root != gameObject)
        {
            root.SetActive(visible);
        }
    }

    private void SetSkipHintVisible(bool visible)
    {
        if (m_skipHint != null && m_skipHint.activeSelf != visible)
        {
            m_skipHint.SetActive(visible);
        }
    }

    private static bool WasSkipPressed()
    {
        // 스킵은 스페이스바로만 허용합니다.
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
    }
}
