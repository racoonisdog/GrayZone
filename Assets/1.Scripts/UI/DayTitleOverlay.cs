using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 셸터 씬에 들어올 때 화면 중앙보다 조금 위에 "1일차", "2일차" 같은 일차 문구를 페이드 인·아웃으로 띄웁니다.
/// </summary>
/// <remarks>
/// 일차는 다음에 할 방어전 회차(<see cref="GameDataManager.NextDefenseRound"/>)와 같습니다. 방어전을 한 번 클리어하고
/// 돌아오면 2일차가 됩니다. 씬이 시작될 때 한 번만 재생하고, 씬 안에서 다시 띄우지 않습니다.
/// 시간이 멈춰 있어도 진행되도록 실제 시간으로 페이드합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DayTitleOverlay : MonoBehaviour
{
    [Tooltip("페이드할 CanvasGroup입니다. 문구 전체의 투명도를 조절합니다.")]
    [SerializeField] private CanvasGroup m_canvasGroup;

    [Tooltip("일차 문구를 표시할 텍스트입니다.")]
    [SerializeField] private TMP_Text m_dayText;

    [Tooltip("일차 문구 형식입니다. {0}에 일차가 들어갑니다.")]
    [SerializeField] private string m_dayFormat = "{0}일차";

    [Tooltip("씬이 시작된 뒤 문구를 띄우기까지 기다릴 시간(초)입니다.")]
    [SerializeField, Min(0f)] private float m_startDelay = 0.5f;

    [Tooltip("나타나는 시간(초)입니다.")]
    [SerializeField, Min(0f)] private float m_fadeInDuration = 0.8f;

    [Tooltip("다 나타난 뒤 유지하는 시간(초)입니다.")]
    [SerializeField, Min(0f)] private float m_holdDuration = 1.5f;

    [Tooltip("사라지는 시간(초)입니다.")]
    [SerializeField, Min(0f)] private float m_fadeOutDuration = 0.8f;

    private void Awake()
    {
        if (m_canvasGroup == null)
        {
            m_canvasGroup = GetComponent<CanvasGroup>();
        }

        SetAlpha(0f);
    }

    private void Start()
    {
        int day = GameDataManager.Instance != null ? GameDataManager.Instance.NextDefenseRound : 1;
        if (m_dayText != null)
        {
            m_dayText.text = string.Format(m_dayFormat, day);
        }

        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        yield return new WaitForSecondsRealtime(m_startDelay);
        yield return Fade(0f, 1f, m_fadeInDuration);
        yield return new WaitForSecondsRealtime(m_holdDuration);
        yield return Fade(1f, 0f, m_fadeOutDuration);
        gameObject.SetActive(false);
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, elapsed / duration));
            yield return null;
        }

        SetAlpha(to);
    }

    private void SetAlpha(float alpha)
    {
        if (m_canvasGroup != null)
        {
            m_canvasGroup.alpha = alpha;
        }
    }
}
