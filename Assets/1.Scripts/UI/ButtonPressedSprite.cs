using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼을 누르고 있는 동안에만 대상 Image에 눌림 스프라이트를 표시하고, 떼면 원래 스프라이트로 되돌린다.
/// Button의 Color Tint(비활성 시 어두워짐)와 함께 쓰며, interactable이 꺼진 상태에서는 스프라이트를 바꾸지 않는다.
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class ButtonPressedSprite : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("누르는 동안 표시할 스프라이트")]
    [SerializeField] private Sprite m_pressedSprite;

    [Tooltip("비워 두면 Button의 Target Graphic Image를 사용합니다.")]
    [SerializeField] private Image m_targetImage;

    private Button m_button;

    private void Awake()
    {
        m_button = GetComponent<Button>();
        if (m_targetImage == null)
            m_targetImage = m_button.targetGraphic as Image;
    }

    private void OnDisable()
    {
        Restore();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
        if (m_targetImage == null || m_pressedSprite == null)
            return;
        if (!m_button.IsInteractable())
            return;

        // overrideSprite를 사용하므로 원래 sprite 값은 건드리지 않는다.
        m_targetImage.overrideSprite = m_pressedSprite;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Restore();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Restore();
    }

    private void Restore()
    {
        if (m_targetImage != null)
            m_targetImage.overrideSprite = null;
    }
}
