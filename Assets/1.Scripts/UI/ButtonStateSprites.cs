using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼 배경을 기본·마우스 올림·누름 세 상태로 바꿔 그립니다. 누름 상태는 불투명도도 따로 줄 수 있습니다.
/// </summary>
/// <remarks>
/// Button의 Sprite Swap 전환은 클릭한 버튼이 선택 상태로 남으면 마우스를 올려도 올림 모양이 나오지 않고,
/// 불투명도도 바꿀 수 없어 이 컴포넌트로 직접 바꿉니다. 이 컴포넌트를 쓰는 버튼은 Button 전환을 None으로 둡니다.
/// 원래 sprite 값은 건드리지 않고 overrideSprite만 바꾸므로, 끄거나 상태가 끝나면 기본 모양으로 돌아갑니다.
/// 누를 수 없는 버튼은 바꾸지 않고 기본 모양에 비활성 불투명도를 줍니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public sealed class ButtonStateSprites : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    private const float DisabledAlpha = 0.4f;

    [Tooltip("비워 두면 Selectable의 Target Graphic Image를 씁니다.")]
    [SerializeField] private Image m_targetImage;

    [Tooltip("마우스를 올렸을 때 배경입니다. 비워 두면 기본 배경을 그대로 씁니다.")]
    [SerializeField] private Sprite m_hoverSprite;

    [Tooltip("누르고 있는 동안 배경입니다. 비워 두면 마우스 올림 배경을 씁니다.")]
    [SerializeField] private Sprite m_pressedSprite;

    [Tooltip("누르고 있는 동안 배경의 불투명도입니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float m_pressedAlpha = 1f;

    private Selectable m_selectable;
    private bool m_isPointerOver;
    private bool m_isPointerDown;
    private bool m_lastInteractable = true;

    /// <summary>코드로 만든 버튼에 상태별 배경을 지정합니다.</summary>
    public void Configure(Image targetImage, Sprite hoverSprite, Sprite pressedSprite, float pressedAlpha)
    {
        m_targetImage = targetImage;
        m_hoverSprite = hoverSprite;
        m_pressedSprite = pressedSprite;
        m_pressedAlpha = Mathf.Clamp01(pressedAlpha);
        Apply();
    }

    private void Awake()
    {
        CacheReferences();
        Apply();
    }

    private void OnDisable()
    {
        m_isPointerOver = false;
        m_isPointerDown = false;
        Apply();
    }

    private void Update()
    {
        // interactable은 바뀌어도 알림이 없어, 바뀐 프레임에만 다시 그립니다.
        bool interactable = m_selectable == null || m_selectable.IsInteractable();
        if (interactable != m_lastInteractable)
        {
            Apply();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        m_isPointerOver = true;
        Apply();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        m_isPointerOver = false;
        Apply();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        m_isPointerDown = true;
        Apply();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        m_isPointerDown = false;
        Apply();
    }

    private void CacheReferences()
    {
        if (m_selectable == null)
        {
            m_selectable = GetComponent<Selectable>();
        }

        if (m_targetImage == null && m_selectable != null)
        {
            m_targetImage = m_selectable.targetGraphic as Image;
        }
    }

    private void Apply()
    {
        CacheReferences();
        bool interactable = m_selectable == null || m_selectable.IsInteractable();
        m_lastInteractable = interactable;

        if (m_targetImage == null)
        {
            return;
        }

        Sprite sprite = null;
        float alpha = 1f;
        if (!interactable)
        {
            alpha = DisabledAlpha;
        }
        else if (m_isPointerDown && m_isPointerOver)
        {
            sprite = m_pressedSprite != null ? m_pressedSprite : m_hoverSprite;
            alpha = m_pressedSprite != null ? m_pressedAlpha : 1f;
        }
        else if (m_isPointerOver)
        {
            sprite = m_hoverSprite;
        }

        m_targetImage.overrideSprite = sprite;
        Color color = m_targetImage.color;
        color.a = alpha;
        m_targetImage.color = color;
    }
}
