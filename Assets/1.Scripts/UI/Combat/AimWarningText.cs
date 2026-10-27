using TMPro;
using UnityEngine;

/// <summary>
/// 조준선 위에 짧은 경고 문구(예: "투척물이 부족합니다!")를 띄우고 잠시 뒤 서서히 사라지게 합니다.
/// </summary>
/// <remarks>
/// 레이아웃(위치, 글꼴, 색)은 씬에 미리 배치돼 있고, 이 컴포넌트는 문구와 알파만 바꿉니다.
/// 캐릭터 프리팹(투척기 등)이 씬 오브젝트를 직접 참조할 수 없어서, 씬에 하나 둔 인스턴스를 <see cref="Show"/>로 부릅니다.
/// 씬에 없으면 아무것도 하지 않습니다. 같은 문구를 다시 부르면 처음부터 다시 보여 줍니다.
/// 일시정지 중에도 사라지도록 실제 시간으로 셉니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class AimWarningText : MonoBehaviour
{
    private static AimWarningText s_instance;

    /// <summary>한 프레임에 진행할 수 있는 최대 시간(초)입니다.</summary>
    private const float MaxStepSeconds = 1.0f / 30.0f;

    [Tooltip("문구를 표시할 텍스트입니다.")]
    [SerializeField] private TMP_Text m_text;

    [Tooltip("알파를 조절할 CanvasGroup입니다. 비어 있으면 이 오브젝트에서 찾습니다.")]
    [SerializeField] private CanvasGroup m_canvasGroup;

    [Tooltip("완전히 보인 채로 유지하는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_holdDuration = 0.5f;

    [Tooltip("사라지는 데 걸리는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_fadeDuration = 0.8f;

    /// <summary>남은 표시 시간(초)입니다. 유지 시간과 사라지는 시간을 합친 값에서 줄어듭니다.</summary>
    private float m_remaining;

    /// <summary>
    /// 씬의 경고 문구에 <paramref name="message"/>를 띄웁니다. 씬에 경고 문구가 없으면 아무것도 하지 않습니다.
    /// </summary>
    public static void Show(string message)
    {
        if (s_instance != null && !string.IsNullOrEmpty(message))
        {
            s_instance.ShowMessage(message);
        }
    }

    private void Awake()
    {
        if (m_canvasGroup == null)
        {
            m_canvasGroup = GetComponent<CanvasGroup>();
        }

        SetAlpha(0.0f);
    }

    private void OnEnable()
    {
        if (s_instance == null)
        {
            s_instance = this;
        }
    }

    private void OnDisable()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void Update()
    {
        if (m_remaining <= 0.0f)
        {
            return;
        }

        // 한 프레임에 줄어드는 시간을 묶습니다. 문구를 띄운 프레임이 길게 걸리면(로딩, 히치) 그 시간이 한 번에 빠져
        // 문구가 보이기도 전에 사라지기 때문입니다.
        m_remaining = Mathf.Max(0.0f, m_remaining - Mathf.Min(Time.unscaledDeltaTime, MaxStepSeconds));
        SetAlpha(m_remaining >= m_fadeDuration ? 1.0f : m_remaining / m_fadeDuration);
    }

    private void ShowMessage(string message)
    {
        if (m_text != null && m_text.text != message)
        {
            m_text.text = message;
        }

        m_remaining = m_holdDuration + m_fadeDuration;
        SetAlpha(1.0f);
    }

    private void SetAlpha(float alpha)
    {
        if (m_canvasGroup != null)
        {
            m_canvasGroup.alpha = alpha;
        }
    }
}
