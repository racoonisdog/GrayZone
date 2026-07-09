using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 목표 체크박스입니다. bool 값에 따라 체크/비체크 스프라이트를 전환합니다.
/// </summary>
/// <remarks>
/// 같은 GameObject의 <see cref="Image"/>에 체크 스프라이트(Description_CheckBox_On)와
/// 비체크 스프라이트(Description_CheckBox)를 번갈아 적용합니다. 목표 리스트 항목의 좌측 체크 표시에 사용합니다.
/// </remarks>
[RequireComponent(typeof(Image))]
[DisallowMultipleComponent]
public class GoalCheckbox : MonoBehaviour
{
    [Tooltip("체크됨 상태 스프라이트(Description_CheckBox_On)입니다.")]
    [SerializeField] private Sprite m_checkedSprite;

    [Tooltip("비체크 상태 스프라이트(Description_CheckBox)입니다.")]
    [SerializeField] private Sprite m_uncheckedSprite;

    [Tooltip("현재 체크 여부입니다.")]
    [SerializeField] private bool m_isChecked;

    private Image m_image;

    /// <summary>현재 체크 여부입니다. 설정하면 즉시 스프라이트가 갱신됩니다.</summary>
    public bool IsChecked
    {
        get => m_isChecked;
        set
        {
            m_isChecked = value;
            Apply();
        }
    }

    private void Awake()
    {
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Apply();
    }
#endif

    /// <summary>체크 여부를 설정합니다.</summary>
    public void SetChecked(bool value)
    {
        IsChecked = value;
    }

    /// <summary>체크 여부를 반전합니다.</summary>
    public void Toggle()
    {
        IsChecked = !m_isChecked;
    }

    private void Apply()
    {
        if (m_image == null)
        {
            m_image = GetComponent<Image>();
        }

        if (m_image == null)
        {
            return;
        }

        Sprite next = m_isChecked ? m_checkedSprite : m_uncheckedSprite;
        if (next != null)
        {
            m_image.sprite = next;
        }
    }
}
