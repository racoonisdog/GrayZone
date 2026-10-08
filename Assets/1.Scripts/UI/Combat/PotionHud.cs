using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 우측 하단 총기 칸의 회복약 아이콘과 보유 수량을 표시합니다(Figma <c>Reviving HUD_Description_Test01</c>의 <c>Potion</c>).
/// </summary>
/// <remarks>
/// 레이아웃은 씬에 미리 배치돼 있고, 이 컴포넌트는 수량·흐림·쿨타임 채움만 갱신합니다.
/// 수량이 0이면 아이콘을 흐리게 표시합니다. 쿨타임 채움 이미지는 선택 사항이며, 쿨타임이 0이면 항상 비어 있습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class PotionHud : MonoBehaviour
{
    [Header("References")]
    [Tooltip("회복약 아이콘입니다. 수량이 0이면 흐리게 표시합니다.")]
    [SerializeField] private Image m_icon;

    [Tooltip("보유 수량 텍스트입니다.")]
    [SerializeField] private TMP_Text m_countText;

    [Tooltip("아이콘 위에 사용 키(PotionManager의 키)를 표시할 텍스트입니다. 선택 사항입니다.")]
    [SerializeField] private TMP_Text m_keyText;

    [Tooltip("쿨타임 동안 아이콘 위를 덮는 Filled 이미지입니다. 선택 사항입니다.")]
    [SerializeField] private Image m_cooldownFill;

    [Tooltip("회복약 사용을 담당하는 PotionManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private PotionManager m_potionManager;

    [Header("Display")]
    [Tooltip("수량이 0일 때 아이콘과 수량의 불투명도(0~1)입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_emptyAlpha = 0.35f;

    private int m_shownCount = -1;

    private void Awake()
    {
        if (m_potionManager == null)
        {
            m_potionManager = FindFirstObjectByType<PotionManager>(FindObjectsInactive.Include);
        }
    }

    private void LateUpdate()
    {
        if (m_potionManager == null)
        {
            return;
        }

        if (m_keyText != null)
        {
            // 키보드 배열에 맞는 표시 이름을 씁니다. 키보드가 없으면 키 이름 그대로 씁니다.
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            string label = keyboard != null && m_potionManager.UseKey != UnityEngine.InputSystem.Key.None
                ? keyboard[m_potionManager.UseKey].displayName
                : m_potionManager.UseKey.ToString();
            if (m_keyText.text != label)
            {
                m_keyText.text = label;
            }
        }

        int count = m_potionManager.Count;
        if (count != m_shownCount)
        {
            m_shownCount = count;
            if (m_countText != null)
            {
                m_countText.text = count.ToString();
            }

            float alpha = count > 0 ? 1.0f : m_emptyAlpha;
            SetAlpha(m_icon, alpha);
            SetAlpha(m_countText, alpha);
        }

        if (m_cooldownFill != null)
        {
            float duration = m_potionManager.CooldownDuration;
            m_cooldownFill.fillAmount = duration > 0.0f ? m_potionManager.CooldownRemaining / duration : 0.0f;
        }
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
        {
            return;
        }

        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }
}
