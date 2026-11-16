using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 전투 HUD 왼쪽의 미션 목표(제목, 부제) 문구를 바꿉니다.
/// </summary>
/// <remarks>
/// 문구는 씬에 적어 둔 값으로 시작하고, 튜토리얼 같은 진행 쪽이 <see cref="SetObjective"/>로 바꿉니다.
/// 이 컴포넌트는 누가 언제 바꾸는지 모릅니다.
/// 배경(이 오브젝트) 너비는 긴 쪽 문구에 맞춰 늘어나고, 씬에 둔 너비보다 작아지지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class MissionObjectiveHud : MonoBehaviour
{
    private const string TitleName = "Title";
    private const string SubtitleName = "Subtitle";

    /// <summary>타이핑 진행에 셈할 한 프레임의 최대 시간(초)입니다.</summary>
    private const float MaxTypingFrameSeconds = 0.1f;

    [Tooltip("목표 제목 텍스트입니다. 비어 있으면 자식 'Title'을 찾습니다.")]
    [SerializeField] private TMP_Text m_titleText;

    [Tooltip("목표 부제 텍스트입니다. 선택 항목이며, 비어 있으면 자식 'Subtitle'을 찾습니다.")]
    [SerializeField] private TMP_Text m_subtitleText;

    [Header("배경 너비")]
    [Tooltip("문구 끝에서 배경 오른쪽 끝까지 둘 여백입니다.")]
    [SerializeField] private float m_rightPadding = 56.0f;

    [Tooltip("배경이 늘어날 수 있는 최대 너비입니다. 0이면 제한이 없습니다.")]
    [SerializeField] private float m_maxWidth = 720.0f;

    [Header("타이핑")]
    [Tooltip("목표가 바뀔 때 글자를 하나씩 보여 줍니다.")]
    [SerializeField] private bool m_typeOnChange = true;

    [Tooltip("1초에 보여 줄 글자 수입니다.")]
    [SerializeField] private float m_charactersPerSecond = 30.0f;

    private RectTransform m_rect;
    private float m_baseWidth = -1.0f;
    private Coroutine m_typing;

    private void Reset()
    {
        ResolveTexts();
    }

    private void Awake()
    {
        ResolveTexts();
        CacheBaseWidth();
    }

    private void OnDisable()
    {
        // 타이핑 중에 꺼지면 다음에 켜질 때 글자가 잘린 채로 남지 않게 다 보여 줍니다.
        StopTyping();
    }

    /// <summary>
    /// 목표 문구를 바꿉니다. 비어 있는 값은 바꾸지 않고 지금 문구를 둡니다.
    /// </summary>
    /// <param name="title">새 제목입니다. 비어 있으면 그대로 둡니다.</param>
    /// <param name="subtitle">새 부제입니다. 비어 있으면 그대로 둡니다.</param>
    public void SetObjective(string title, string subtitle)
    {
        ResolveTexts();
        CacheBaseWidth();

        bool titleChanged = m_titleText != null && !string.IsNullOrEmpty(title) && m_titleText.text != title;
        bool subtitleChanged = m_subtitleText != null && !string.IsNullOrEmpty(subtitle) && m_subtitleText.text != subtitle;

        // 같은 문구로 다시 불리면 진행 중인 타이핑을 끊지 않습니다.
        if (!titleChanged && !subtitleChanged)
        {
            return;
        }

        StopTyping();

        if (titleChanged)
        {
            m_titleText.text = title;
        }

        if (subtitleChanged)
        {
            m_subtitleText.text = subtitle;
        }

        FitBackground();

        if (m_typeOnChange && isActiveAndEnabled)
        {
            m_typing = StartCoroutine(TypeRoutine(titleChanged ? m_titleText : null, subtitleChanged ? m_subtitleText : null));
        }
    }

    private IEnumerator TypeRoutine(TMP_Text title, TMP_Text subtitle)
    {
        if (title != null)
        {
            title.maxVisibleCharacters = 0;
        }

        if (subtitle != null)
        {
            subtitle.maxVisibleCharacters = 0;
        }

        // 글자 수는 텍스트 정보가 갱신된 뒤에 셉니다.
        int titleCount = CountCharacters(title);
        int subtitleCount = CountCharacters(subtitle);
        int total = Mathf.Max(titleCount, subtitleCount);
        float elapsed = 0.0f;
        float speed = Mathf.Max(1.0f, m_charactersPerSecond);

        while (true)
        {
            int visible = Mathf.FloorToInt(elapsed * speed);
            if (title != null)
            {
                title.maxVisibleCharacters = visible;
            }

            if (subtitle != null)
            {
                subtitle.maxVisibleCharacters = visible;
            }

            if (visible >= total)
            {
                break;
            }

            yield return null;

            // 한 프레임이 길게 멈추면(로딩 등) 타이핑이 한 번에 끝나 보이지 않으므로 프레임당 시간을 제한합니다.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxTypingFrameSeconds);
        }

        m_typing = null;
        ShowAllCharacters();
    }

    private void StopTyping()
    {
        if (m_typing != null)
        {
            StopCoroutine(m_typing);
            m_typing = null;
        }

        ShowAllCharacters();
    }

    private void ShowAllCharacters()
    {
        if (m_titleText != null)
        {
            m_titleText.maxVisibleCharacters = int.MaxValue;
        }

        if (m_subtitleText != null)
        {
            m_subtitleText.maxVisibleCharacters = int.MaxValue;
        }
    }

    private static int CountCharacters(TMP_Text text)
    {
        if (text == null)
        {
            return 0;
        }

        text.ForceMeshUpdate();
        return text.textInfo.characterCount;
    }

    private void FitBackground()
    {
        if (m_rect == null || m_baseWidth < 0.0f)
        {
            return;
        }

        float width = Mathf.Max(m_baseWidth, Mathf.Max(RequiredWidth(m_titleText), RequiredWidth(m_subtitleText)));
        if (m_maxWidth > 0.0f)
        {
            width = Mathf.Min(width, Mathf.Max(m_baseWidth, m_maxWidth));
        }

        m_rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }

    private float RequiredWidth(TMP_Text text)
    {
        if (text == null || string.IsNullOrEmpty(text.text))
        {
            return 0.0f;
        }

        // 텍스트 왼쪽 끝 위치(배경 기준) + 문구 길이 + 여백입니다.
        Vector3 local = m_rect.InverseTransformPoint(text.rectTransform.TransformPoint(text.rectTransform.rect.min));
        float left = local.x - m_rect.rect.xMin;
        return left + text.GetPreferredValues(text.text).x + m_rightPadding;
    }

    private void CacheBaseWidth()
    {
        if (m_baseWidth >= 0.0f)
        {
            return;
        }

        m_rect = transform as RectTransform;
        if (m_rect != null)
        {
            m_baseWidth = m_rect.rect.width;
        }
    }

    private void ResolveTexts()
    {
        if (m_titleText == null)
        {
            Transform title = transform.Find(TitleName);
            m_titleText = title != null ? title.GetComponent<TMP_Text>() : null;
        }

        if (m_subtitleText == null)
        {
            Transform subtitle = transform.Find(SubtitleName);
            m_subtitleText = subtitle != null ? subtitle.GetComponent<TMP_Text>() : null;
        }
    }
}
