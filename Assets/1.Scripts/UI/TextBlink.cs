using TMPro;
using UnityEngine;

/// <summary>
/// TextMeshProUGUI의 알파를 주기적으로 올렸다 내려 깜빡이게 합니다.
/// timeScale이 0인 상황(컷신, 일시정지)에서도 동작하도록 unscaled time을 사용합니다.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class TextBlink : MonoBehaviour
{
    [Tooltip("한 번 깜빡이는 주기(초)입니다.")]
    [SerializeField, Min(0.01f)] private float m_period = 1.2f;

    [Tooltip("가장 어두울 때의 알파값입니다.")]
    [SerializeField, Range(0f, 1f)] private float m_minAlpha = 0.2f;

    [Tooltip("가장 밝을 때의 알파값입니다.")]
    [SerializeField, Range(0f, 1f)] private float m_maxAlpha = 1f;

    private TextMeshProUGUI m_text;
    private float m_startTime;

    private void Awake()
    {
        m_text = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        // 켜질 때마다 밝은 상태부터 시작합니다.
        m_startTime = Time.unscaledTime;
    }

    private void OnDisable()
    {
        if (m_text != null)
        {
            m_text.alpha = m_maxAlpha;
        }
    }

    private void Update()
    {
        float t = (Time.unscaledTime - m_startTime) / m_period;
        float wave = (Mathf.Cos(t * Mathf.PI * 2f) + 1f) * 0.5f;
        m_text.alpha = Mathf.Lerp(m_minAlpha, m_maxAlpha, wave);
    }
}
