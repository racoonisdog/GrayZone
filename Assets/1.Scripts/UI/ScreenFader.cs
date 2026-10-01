using System.Collections;
using UnityEngine;

/// <summary>
/// 화면 전체를 덮는 검은 이미지의 알파를 조절하는 화면 페이드 컴포넌트
/// </summary>
/// <remarks>
/// Screen Space Overlay Canvas(높은 Sort Order) 아래 전체 화면 Image에 붙여 사용.
/// timeScale이 0이어도 동작하도록 unscaled 시간으로 진행하며, 가려진 동안에는 뒤쪽 UI 입력을 막기.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class ScreenFader : MonoBehaviour
{
    [SerializeField] private CanvasGroup m_group;
    [Tooltip("시작 시 알파값입니다. 0은 투명, 1은 완전히 가림입니다.")]
    [SerializeField, Range(0f, 1f)] private float m_initialAlpha = 0f;

    private Coroutine m_fadeRoutine;

    /// <summary>현재 페이드 알파값</summary>
    public float Alpha => m_group != null ? m_group.alpha : 0f;

    /// <summary>페이드 진행 중 여부</summary>
    public bool IsFading => m_fadeRoutine != null;

    private void Awake()
    {
        if (m_group == null)
            m_group = GetComponent<CanvasGroup>();

        SetAlpha(m_initialAlpha);
    }

    private void OnDisable()
    {
        m_fadeRoutine = null;
    }

    /// <summary>
    /// 진행 중인 페이드를 멈추고 알파를 즉시 지정
    /// </summary>
    /// <param name="alpha">적용할 알파값</param>
    public void SetAlpha(float alpha)
    {
        if (m_fadeRoutine != null)
        {
            StopCoroutine(m_fadeRoutine);
            m_fadeRoutine = null;
        }

        ApplyAlpha(alpha);
    }

    /// <summary>
    /// 지정한 알파까지 페이드하는 코루틴. 호출 측에서 <c>yield return</c>으로 완료를 기다릴 수 있음
    /// </summary>
    /// <param name="targetAlpha">목표 알파값</param>
    /// <param name="duration">페이드 시간(초). 0 이하면 즉시 적용</param>
    public IEnumerator Fade(float targetAlpha, float duration)
    {
        if (m_fadeRoutine != null)
            StopCoroutine(m_fadeRoutine);

        m_fadeRoutine = StartCoroutine(FadeRoutine(Mathf.Clamp01(targetAlpha), duration));
        yield return m_fadeRoutine;
    }

    /// <summary>화면을 검게 가리는 페이드</summary>
    public IEnumerator FadeOut(float duration) => Fade(1f, duration);

    /// <summary>가린 화면을 다시 보여주는 페이드</summary>
    public IEnumerator FadeIn(float duration) => Fade(0f, duration);

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        float startAlpha = Alpha;
        if (duration > 0f)
        {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                ApplyAlpha(Mathf.Lerp(startAlpha, targetAlpha, t / duration));
                yield return null;
            }
        }

        ApplyAlpha(targetAlpha);
        m_fadeRoutine = null;
    }

    private void ApplyAlpha(float alpha)
    {
        if (m_group == null)
            return;

        m_group.alpha = Mathf.Clamp01(alpha);
        bool covering = m_group.alpha > 0f;
        m_group.blocksRaycasts = covering;
        m_group.interactable = false;
    }
}
