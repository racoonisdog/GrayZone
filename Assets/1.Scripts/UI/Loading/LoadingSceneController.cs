using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로딩 씬에서 목표 씬을 비동기로 불러오며 진행도를 게이지로 보여 줍니다.
/// </summary>
/// <remarks>
/// 흐름: 이전 씬이 내려간 상태에서 쓰지 않는 리소스를 정리하고, 목표 씬을 <c>allowSceneActivation = false</c>로 읽습니다.
/// 읽기가 끝나면(진행도 0.9) 게이지를 끝까지 채우고 최소 표시 시간을 채운 뒤 씬을 켭니다.
/// 씬이 켜지는 순간의 Awake/Start 처리는 게이지에 잡히지 않으므로 게이지가 다 찬 뒤 잠깐 멈출 수 있습니다.
/// 시간 배율이 0이어도 동작하도록 실제 시간으로 셉니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class LoadingSceneController : MonoBehaviour
{
    /// <summary>allowSceneActivation이 false일 때 읽기가 끝나면 progress가 멈추는 값입니다.</summary>
    private const float LoadedProgress = 0.9f;

    [Header("References")]
    [Tooltip("진행도를 채울 이미지입니다(Image Type: Filled).")]
    [SerializeField] private Image m_progressFill;

    [Tooltip("진행도를 퍼센트로 보여 줄 텍스트입니다. 비워 두면 표시하지 않습니다.")]
    [SerializeField] private TMP_Text m_progressText;

    [Tooltip("문구와 점 애니메이션을 보여 줄 텍스트입니다. 비워 두면 표시하지 않습니다.")]
    [SerializeField] private TMP_Text m_loadingLabel;

    [Header("Loading Text")]
    [Tooltip("점 앞에 표시할 문구입니다.")]
    [SerializeField] private string m_baseLabel = "구역 이동 중";

    [Tooltip("점을 0개부터 이 개수까지 늘린 뒤 다시 0개로 돌아갑니다.")]
    [Min(1)]
    [SerializeField] private int m_maxDotCount = 3;

    [Tooltip("점이 하나 늘어나는 간격(초)입니다. 한 바퀴(점 0개~최대)는 이 값 × (최대 개수 + 1)초입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_dotStepInterval = 1.0f;

    [Header("Fallback")]
    [Tooltip("목표 씬 없이 로딩 씬이 열렸을 때(에디터에서 직접 실행 등) 불러올 씬입니다.")]
    [SerializeField] private string m_fallbackSceneName = "TitleScene";

    [Header("Timing")]
    [Tooltip("화면이 너무 짧게 깜빡이지 않도록 유지할 최소 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_minimumVisibleDuration = 0.5f;

    [Tooltip("게이지가 실제 진행도를 따라가는 속도(초당 비율)입니다. 진행도가 한 번에 튀어도 부드럽게 찹니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_fillSpeed = 1.5f;

    private float m_displayedProgress;
    private int m_lastPercent = -1;
    private float m_dotElapsed;
    private int m_dotCount = -1;

    private void Update()
    {
        // 첫 프레임은 씬 전환으로 길어지므로 그 시간은 세지 않고 점 0개부터 시작합니다.
        if (m_dotCount < 0)
        {
            m_dotCount = 0;
            m_dotElapsed = 0.0f;
            ApplyLabel();
            return;
        }

        // 씬을 읽느라 프레임이 길어져도 한 번에 한 칸씩만 넘어갑니다.
        m_dotElapsed += Mathf.Min(Time.unscaledDeltaTime, m_dotStepInterval);
        if (m_dotElapsed < m_dotStepInterval)
        {
            return;
        }

        m_dotElapsed -= m_dotStepInterval;
        m_dotCount = (m_dotCount + 1) % (m_maxDotCount + 1);
        ApplyLabel();
    }

    /// <summary>
    /// 문구 뒤에 점을 붙입니다. 아직 나오지 않은 점은 투명하게 두어 문구 위치가 점 개수에 따라 흔들리지 않게 합니다.
    /// </summary>
    private void ApplyLabel()
    {
        if (m_loadingLabel == null)
        {
            return;
        }

        string shown = new string('.', m_dotCount);
        string hidden = new string('.', m_maxDotCount - m_dotCount);
        m_loadingLabel.text = hidden.Length > 0
            ? $"{m_baseLabel}{shown}<alpha=#00>{hidden}"
            : m_baseLabel + shown;
    }

    private IEnumerator Start()
    {
        float shownAt = Time.realtimeSinceStartup;
        ApplyProgress(0.0f);

        string sceneName = SceneTransitionController.TakePendingSceneName();
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = m_fallbackSceneName;
        }

        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[LoadingSceneController] Build Settings에 씬 '{sceneName}'이 없습니다.", this);
            yield break;
        }

        // 로딩 화면이 한 프레임 그려진 뒤 무거운 작업을 시작합니다.
        yield return null;

        // 이전 씬이 쓰던 리소스를 목표 씬을 읽기 전에 내려 메모리 최고점을 낮춥니다.
        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            Debug.LogError($"[LoadingSceneController] 씬 '{sceneName}' 로드를 시작하지 못했습니다.", this);
            yield break;
        }

        operation.allowSceneActivation = false;

        while (true)
        {
            float target = Mathf.Clamp01(operation.progress / LoadedProgress);
            StepProgress(target);

            bool loaded = operation.progress >= LoadedProgress;
            bool shownLongEnough = Time.realtimeSinceStartup - shownAt >= m_minimumVisibleDuration;
            if (loaded && shownLongEnough && m_displayedProgress >= 1.0f)
            {
                break;
            }

            yield return null;
        }

        // 100%가 그려진 프레임을 한 번 보여 준 뒤 씬을 켭니다.
        yield return null;
        operation.allowSceneActivation = true;
    }

    private void StepProgress(float target)
    {
        float next = Mathf.MoveTowards(m_displayedProgress, target, m_fillSpeed * Time.unscaledDeltaTime);
        ApplyProgress(next);
    }

    private void ApplyProgress(float progress)
    {
        m_displayedProgress = progress;
        if (m_progressFill != null)
        {
            m_progressFill.fillAmount = progress;
        }

        if (m_progressText == null)
        {
            return;
        }

        int percent = Mathf.RoundToInt(progress * 100.0f);
        if (percent == m_lastPercent)
        {
            return;
        }

        m_lastPercent = percent;
        m_progressText.SetText("{0}%", percent);
    }
}
