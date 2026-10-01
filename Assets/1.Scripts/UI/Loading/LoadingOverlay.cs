using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬과 구역이 전환되는 동안 화면을 덮고 게임 시간과 플레이어 입력을 잠급니다.
/// </summary>
/// <remarks>
/// 애니메이션은 <see cref="Time.unscaledDeltaTime"/>을 사용하므로
/// <see cref="Time.timeScale"/>이 0인 동안에도 계속 재생됩니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class LoadingOverlay : MonoBehaviour
{
    public static LoadingOverlay Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CanvasGroup m_rootCanvasGroup;
    [SerializeField] private CanvasGroup m_breathingCanvasGroup;
    [SerializeField] private RectTransform m_breathingTarget;
    [SerializeField] private TMP_Text m_loadingLabel;

    [Header("Loading Text")]
    [Tooltip("점 애니메이션 앞에 표시할 문구입니다.")]
    [SerializeField] private string m_baseLabel = "구역 이동 중";

    [Tooltip("기본 문구와 점 사이의 간격입니다.")]
    [SerializeField] private string m_dotSeparator = "";

    [Tooltip("0개부터 이 개수까지 점을 순환합니다. 3이면 GIF와 같은 4단계입니다.")]
    [SerializeField, Min(1)] private int m_maxDotCount = 3;

    [Tooltip("점이 다음 단계로 넘어가는 간격입니다. 원본 4프레임 GIF는 1초입니다.")]
    [SerializeField, Min(0.05f)] private float m_dotStepInterval = 1f;

    [Header("Breathing")]
    [Tooltip("숨쉬기 효과 한 주기의 길이입니다.")]
    [SerializeField, Min(0.1f)] private float m_breathCycleDuration = 2.4f;

    [SerializeField, Range(0f, 1f)] private float m_breathMinAlpha = 0.82f;
    [SerializeField, Range(0f, 1f)] private float m_breathMaxAlpha = 1f;
    [SerializeField, Min(0.5f)] private float m_breathMinScale = 0.985f;
    [SerializeField, Min(0.5f)] private float m_breathMaxScale = 1.015f;

    [Header("Loading Lock")]
    [Tooltip("로딩 표시 중 Time.timeScale을 0으로 설정하고 닫을 때 이전 값으로 복원합니다.")]
    [SerializeField] private bool m_pauseGameTime = true;

    [Tooltip("로딩 표시 중 현재 스쿼드의 플레이어 입력도 함께 비활성화합니다.")]
    [SerializeField] private bool m_disableGameplayInput = true;

    [Tooltip("씬 전환 중에도 프리팹을 유지합니다.")]
    [SerializeField] private bool m_persistAcrossScenes = true;

    [Tooltip("활성화와 동시에 로딩 UI를 표시합니다. 보통은 꺼 두고 Show/LoadScene을 호출합니다.")]
    [SerializeField] private bool m_showOnEnable;

    [Tooltip("화면이 너무 짧게 깜빡이지 않도록 유지할 최소 시간입니다.")]
    [SerializeField, Min(0f)] private float m_minimumVisibleDuration = 0.5f;

    [Header("Overlay Fade")]
    [SerializeField, Min(0f)] private float m_fadeInDuration = 0.2f;
    [SerializeField, Min(0f)] private float m_fadeOutDuration = 0.35f;

    private readonly List<PlayerInputController> m_fallbackInputControllers = new();
    private SquadManager m_squadManager;
    private GameObject m_selectedBeforeLoading;
    private Coroutine m_sceneLoadRoutine;
    private Coroutine m_visibilityFadeRoutine;
    private float m_previousTimeScale = 1f;
    private float m_dotElapsed;
    private float m_breathElapsed;
    private int m_dotFrame;
    private bool m_isVisible;
    private bool m_isHiding;
    private bool m_ownsTimePause;

    /// <summary>로딩 오버레이가 입력을 막고 표시 중이면 true입니다.</summary>
    public bool IsVisible => m_isVisible;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (m_persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        ResetAnimation();
        SetVisibleImmediate(false);
        if (m_showOnEnable)
        {
            Show();
        }
    }

    private void Update()
    {
        if (!m_isVisible)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        UpdateDotAnimation(deltaTime);
        UpdateBreathing(deltaTime);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (!m_isVisible)
        {
            return;
        }

        RestoreTimeScale();
        SetGameplayInputEnabled(true);
    }

    /// <summary>기본 문구로 로딩 오버레이를 표시합니다.</summary>
    public void Show()
    {
        Show(m_baseLabel);
    }

    /// <summary>문구를 바꾸고 로딩 오버레이를 표시합니다.</summary>
    public void Show(string label)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            m_baseLabel = label;
        }

        if (m_isVisible)
        {
            m_isHiding = false;
            ResetAnimation();
            SetInputBlocking(true);
            FadeVisibilityTo(1f, m_fadeInDuration, false);
            return;
        }

        m_isVisible = true;
        m_isHiding = false;
        ResetAnimation();
        SetInputBlocking(true);
        FadeVisibilityTo(1f, m_fadeInDuration, false);

        if (EventSystem.current != null)
        {
            m_selectedBeforeLoading = EventSystem.current.currentSelectedGameObject;
            EventSystem.current.SetSelectedGameObject(null);
        }

        if (m_disableGameplayInput)
        {
            SetGameplayInputEnabled(false);
        }

        if (m_pauseGameTime)
        {
            m_previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            m_ownsTimePause = true;
        }
    }

    /// <summary>로딩 오버레이를 닫고 시간과 입력 상태를 복원합니다.</summary>
    public void Hide()
    {
        if (!m_isVisible)
        {
            SetVisibleImmediate(false);
            return;
        }

        if (m_isHiding)
        {
            return;
        }

        m_isHiding = true;
        FadeVisibilityTo(0f, m_fadeOutDuration, true);
    }

    /// <summary>페이드 없이 즉시 로딩 상태를 끝냅니다.</summary>
    public void HideImmediately()
    {
        if (!m_isVisible)
        {
            SetVisibleImmediate(false);
            return;
        }

        if (m_visibilityFadeRoutine != null)
        {
            StopCoroutine(m_visibilityFadeRoutine);
            m_visibilityFadeRoutine = null;
        }

        CompleteHide();
    }

    /// <summary>오버레이를 유지한 채 단일 씬을 비동기로 불러옵니다.</summary>
    public void LoadScene(string sceneName)
    {
        if (m_sceneLoadRoutine != null)
        {
            Debug.LogWarning("[LoadingOverlay] 이미 씬을 불러오는 중입니다.", this);
            return;
        }

        m_sceneLoadRoutine = StartCoroutine(LoadSceneRoutine(sceneName));
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)
            || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"[LoadingOverlay] Build Settings에 씬 '{sceneName}'이 없습니다.", this);
            m_sceneLoadRoutine = null;
            yield break;
        }

        Show();
        float shownAt = Time.realtimeSinceStartup;

        // 오버레이가 최소 한 프레임 렌더링된 뒤 실제 로드를 시작합니다.
        yield return null;

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            Debug.LogError($"[LoadingOverlay] 씬 '{sceneName}' 로드를 시작하지 못했습니다.", this);
            Hide();
            m_sceneLoadRoutine = null;
            yield break;
        }

        while (!operation.isDone
            || Time.realtimeSinceStartup - shownAt < m_minimumVisibleDuration)
        {
            yield return null;
        }

        // 새 씬의 Awake/Start가 끝난 다음 입력과 시간을 돌려줍니다.
        yield return null;
        Hide();
        while (m_isVisible)
        {
            yield return null;
        }

        m_sceneLoadRoutine = null;
    }

    private void ResetAnimation()
    {
        m_dotElapsed = 0f;
        m_breathElapsed = 0f;
        m_dotFrame = 0;
        UpdateLabel();
        ApplyBreathing(0f);
    }

    private void UpdateDotAnimation(float deltaTime)
    {
        m_dotElapsed += deltaTime;
        float interval = Mathf.Max(0.05f, m_dotStepInterval);
        if (m_dotElapsed < interval)
        {
            return;
        }

        int elapsedSteps = Mathf.FloorToInt(m_dotElapsed / interval);
        m_dotElapsed -= elapsedSteps * interval;
        m_dotFrame = (m_dotFrame + elapsedSteps) % (Mathf.Max(1, m_maxDotCount) + 1);
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (m_loadingLabel == null)
        {
            return;
        }

        m_loadingLabel.text = m_baseLabel
            + m_dotSeparator
            + new string('.', Mathf.Max(0, m_dotFrame));
    }

    private void UpdateBreathing(float deltaTime)
    {
        m_breathElapsed += deltaTime;
        float duration = Mathf.Max(0.1f, m_breathCycleDuration);
        float phase = Mathf.Repeat(m_breathElapsed / duration, 1f);
        float pulse = 0.5f - (0.5f * Mathf.Cos(phase * Mathf.PI * 2f));
        ApplyBreathing(pulse);
    }

    private void ApplyBreathing(float pulse)
    {
        if (m_breathingCanvasGroup != null)
        {
            m_breathingCanvasGroup.alpha = Mathf.Lerp(m_breathMinAlpha, m_breathMaxAlpha, pulse);
        }

        if (m_breathingTarget != null)
        {
            float scale = Mathf.Lerp(m_breathMinScale, m_breathMaxScale, pulse);
            m_breathingTarget.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private void SetVisibleImmediate(bool visible)
    {
        if (m_rootCanvasGroup == null)
        {
            return;
        }

        m_rootCanvasGroup.alpha = visible ? 1f : 0f;
        SetInputBlocking(visible);
    }

    private void SetInputBlocking(bool block)
    {
        if (m_rootCanvasGroup == null)
        {
            return;
        }

        m_rootCanvasGroup.interactable = block;
        m_rootCanvasGroup.blocksRaycasts = block;
    }

    private void FadeVisibilityTo(float targetAlpha, float duration, bool completeHide)
    {
        if (m_rootCanvasGroup == null)
        {
            if (completeHide)
            {
                CompleteHide();
            }

            return;
        }

        if (m_visibilityFadeRoutine != null)
        {
            StopCoroutine(m_visibilityFadeRoutine);
        }

        if (duration <= 0f)
        {
            m_rootCanvasGroup.alpha = targetAlpha;
            m_visibilityFadeRoutine = null;
            if (completeHide)
            {
                CompleteHide();
            }

            return;
        }

        m_visibilityFadeRoutine = StartCoroutine(FadeVisibilityRoutine(targetAlpha, duration, completeHide));
    }

    private IEnumerator FadeVisibilityRoutine(float targetAlpha, float duration, bool completeHide)
    {
        float startAlpha = m_rootCanvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float eased = normalized * normalized * (3f - (2f * normalized));
            m_rootCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);
            yield return null;
        }

        m_rootCanvasGroup.alpha = targetAlpha;
        m_visibilityFadeRoutine = null;
        if (completeHide)
        {
            CompleteHide();
        }
    }

    private void CompleteHide()
    {
        m_isVisible = false;
        m_isHiding = false;
        SetVisibleImmediate(false);
        RestoreTimeScale();

        if (m_disableGameplayInput)
        {
            SetGameplayInputEnabled(true);
        }

        if (EventSystem.current != null
            && m_selectedBeforeLoading != null
            && m_selectedBeforeLoading.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(m_selectedBeforeLoading);
        }

        m_selectedBeforeLoading = null;
    }

    private void RestoreTimeScale()
    {
        if (!m_ownsTimePause)
        {
            return;
        }

        Time.timeScale = m_previousTimeScale;
        m_ownsTimePause = false;
    }

    private void SetGameplayInputEnabled(bool enabled)
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        }

        if (m_squadManager != null)
        {
            m_squadManager.ApplyInputModeToSquad(enabled);
            return;
        }

        if (!enabled)
        {
            m_fallbackInputControllers.Clear();
            PlayerInputController[] inputs = FindObjectsByType<PlayerInputController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < inputs.Length; i++)
            {
                PlayerInputController input = inputs[i];
                if (input == null || !input.gameObject.activeInHierarchy)
                {
                    continue;
                }

                input.SetInputGate(false);
                m_fallbackInputControllers.Add(input);
            }

            return;
        }

        for (int i = 0; i < m_fallbackInputControllers.Count; i++)
        {
            PlayerInputController input = m_fallbackInputControllers[i];
            if (input != null)
            {
                input.SetInputGate(true);
            }
        }

        m_fallbackInputControllers.Clear();

        // 씬 전환 뒤에는 이전 SquadManager가 파괴될 수 있으므로 새 씬의 입력도 명시적으로 복원합니다.
        m_squadManager = FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        if (m_squadManager != null)
        {
            m_squadManager.ApplyInputModeToSquad(true);
        }
    }
}
