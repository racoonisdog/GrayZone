using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼에 마우스를 올리거나 버튼이 선택되어 있는 동안 지정한 장식 오브젝트(예: 좌우 Select_Point)를 표시합니다.
/// </summary>
/// <remarks>
/// 장식은 씬에 미리 배치해 두고 이 컴포넌트는 켜고 끄기만 합니다. 버튼을 누를 수 없는 상태에서는 표시하지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public sealed class ButtonHoverDecoration : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    ISelectHandler,
    IDeselectHandler
{
    [Tooltip("마우스를 올렸을 때 표시할 장식 오브젝트입니다.")]
    [SerializeField] private GameObject[] m_decorations;

    private Selectable m_selectable;
    private bool m_isPointerOver;
    private bool m_isSelected;

    private void Awake()
    {
        Apply();
    }

    private void OnDisable()
    {
        m_isPointerOver = false;
        m_isSelected = false;
        Apply();
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

    public void OnSelect(BaseEventData eventData)
    {
        m_isSelected = true;
        Apply();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        m_isSelected = false;
        Apply();
    }

    private void Apply()
    {
        if (m_selectable == null)
            m_selectable = GetComponent<Selectable>();

        bool visible = (m_isPointerOver || m_isSelected)
            && m_selectable != null
            && m_selectable.IsInteractable();

        if (m_decorations == null)
            return;

        for (int i = 0; i < m_decorations.Length; i++)
        {
            GameObject decoration = m_decorations[i];
            if (decoration != null && decoration.activeSelf != visible)
                decoration.SetActive(visible);
        }
    }
}
