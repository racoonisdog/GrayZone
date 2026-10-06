using System.Collections;
using UnityEngine;

/// <summary>
/// 개별 UI 오브젝트의 CanvasGroup 투명도를 범용적으로 페이드 인·아웃합니다.
/// </summary>
/// <remarks>
/// 각 로고·버튼 오브젝트에 하나씩 부착해 시작 연출과 이후 UnityEvent 또는 코드 호출을
/// 독립적으로 설정합니다. 숨겨진 상태에서는 선택·클릭을 차단합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class CanvasGroupFader : MonoBehaviour
{
    /// <summary>오브젝트가 시작될 때 실행할 페이드 동작입니다.</summary>
    public enum StartFadeMode
    {
        None,
        FadeIn,
        FadeOut
    }

    [Header("References")]
    [Tooltip("투명도와 입력 차단을 제어할 같은 오브젝트의 CanvasGroup입니다.")]
    [SerializeField] private CanvasGroup m_canvasGroup;

    [Header("Start Fade")]
    [Tooltip("Start 시 한 번 실행할 동작입니다. None은 현재 alpha를 그대로 사용합니다.")]
    [SerializeField] private StartFadeMode m_startFadeMode = StartFadeMode.FadeIn;

    [Tooltip("Start 페이드 전에 대기할 시간(초)입니다.")]
    [Min(0f)]
    [SerializeField] private float m_startDelay;

    [Header("Durations")]
    [Tooltip("완전히 보이는 상태까지 걸리는 페이드 인 시간(초)입니다.")]
    [Min(0f)]
    [SerializeField] private float m_fadeInDuration = 0.8f;

    [Tooltip("완전히 숨겨지는 상태까지 걸리는 페이드 아웃 시간(초)입니다.")]
    [Min(0f)]
    [SerializeField] private float m_fadeOutDuration = 0.8f;

    [Header("Behaviour")]
    [Tooltip("Time.timeScale이 0이어도 페이드가 진행되도록 unscaled time을 사용할지 결정합니다.")]
    [SerializeField] private bool m_useUnscaledTime = true;

    [Tooltip("숨겨질 때 UI 입력을 차단하고, 표시될 때 다시 허용할지 결정합니다.")]
    [SerializeField] private bool m_manageInteraction = true;

    private Coroutine m_fadeRoutine;

    /// <summary>현재 CanvasGroup 투명도입니다.</summary>
    public float Alpha => m_canvasGroup != null ? m_canvasGroup.alpha : 0f;

    private void Awake()
    {
        AutoAssignCanvasGroup();

        if (m_canvasGroup == null)
        {
            enabled = false;
            return;
        }

        switch (m_startFadeMode)
        {
            case StartFadeMode.FadeIn:
                ApplyAlpha(0f);
                break;
            case StartFadeMode.FadeOut:
                ApplyAlpha(1f);
                break;
            case StartFadeMode.None:
                ApplyInteractionState(m_canvasGroup.alpha > 0f);
                break;
        }
    }

    private void Start()
    {
        if (m_startFadeMode == StartFadeMode.FadeIn)
        {
            FadeIn(m_startDelay);
        }
        else if (m_startFadeMode == StartFadeMode.FadeOut)
        {
            FadeOut(m_startDelay);
        }
    }

    private void OnDisable()
    {
        StopActiveFade();
    }

    private void OnValidate()
    {
        m_startDelay = Mathf.Max(0f, m_startDelay);
        m_fadeInDuration = Mathf.Max(0f, m_fadeInDuration);
        m_fadeOutDuration = Mathf.Max(0f, m_fadeOutDuration);
    }

    /// <summary>같은 오브젝트의 CanvasGroup을 자동으로 연결합니다.</summary>
    [ContextMenu("Auto Assign CanvasGroup")]
    public void AutoAssignCanvasGroup()
    {
        if (m_canvasGroup == null)
        {
            m_canvasGroup = GetComponent<CanvasGroup>();
        }
    }

    /// <summary>설정된 시간으로 페이드 인을 시작합니다.</summary>
    public void FadeIn()
    {
        FadeIn(0f);
    }

    /// <summary>지정한 대기 시간 후 설정된 시간으로 페이드 인을 시작합니다.</summary>
    public void FadeIn(float delay)
    {
        StartFade(1f, m_fadeInDuration, delay);
    }

    /// <summary>설정된 시간으로 페이드 아웃을 시작합니다.</summary>
    public void FadeOut()
    {
        FadeOut(0f);
    }

    /// <summary>지정한 대기 시간 후 설정된 시간으로 페이드 아웃을 시작합니다.</summary>
    public void FadeOut(float delay)
    {
        StartFade(0f, m_fadeOutDuration, delay);
    }

    /// <summary>애니메이션 없이 즉시 표시합니다.</summary>
    public void ShowImmediately()
    {
        AutoAssignCanvasGroup();
        StopActiveFade();
        ApplyAlpha(1f);
    }

    /// <summary>애니메이션 없이 즉시 숨깁니다.</summary>
    public void HideImmediately()
    {
        AutoAssignCanvasGroup();
        StopActiveFade();
        ApplyAlpha(0f);
    }

    private void StartFade(float targetAlpha, float duration, float delay)
    {
        if (m_canvasGroup == null)
        {
            AutoAssignCanvasGroup();
        }

        if (m_canvasGroup == null)
        {
            Debug.LogWarning("[CanvasGroupFader] CanvasGroup이 연결되지 않았습니다.", this);
            return;
        }

        StopActiveFade();
        if (!isActiveAndEnabled || (duration <= 0f && delay <= 0f))
        {
            ApplyAlpha(targetAlpha);
            return;
        }

        m_fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, duration, Mathf.Max(0f, delay)));
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration, float delay)
    {
        if (delay > 0f)
        {
            yield return WaitForDuration(delay);
        }

        if (targetAlpha > 0f)
        {
            ApplyInteractionState(true);
        }

        if (duration <= 0f)
        {
            ApplyAlpha(targetAlpha);
            m_fadeRoutine = null;
            yield break;
        }

        float startAlpha = m_canvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += m_useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            ApplyAlpha(Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration)), false);
            yield return null;
        }

        ApplyAlpha(targetAlpha);
        m_fadeRoutine = null;
    }

    private IEnumerator WaitForDuration(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += m_useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }
    }

    private void ApplyAlpha(float alpha, bool updateInteraction = true)
    {
        if (m_canvasGroup == null)
        {
            return;
        }

        m_canvasGroup.alpha = Mathf.Clamp01(alpha);
        if (updateInteraction)
        {
            ApplyInteractionState(m_canvasGroup.alpha > 0f);
        }
    }

    private void ApplyInteractionState(bool visible)
    {
        if (!m_manageInteraction || m_canvasGroup == null)
        {
            return;
        }

        m_canvasGroup.interactable = visible;
        m_canvasGroup.blocksRaycasts = visible;
    }

    private void StopActiveFade()
    {
        if (m_fadeRoutine == null)
        {
            return;
        }

        StopCoroutine(m_fadeRoutine);
        m_fadeRoutine = null;
    }
}
