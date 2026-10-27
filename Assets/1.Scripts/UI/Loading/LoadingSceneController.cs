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
