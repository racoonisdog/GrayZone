using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이콘 목록에서 제조 레시피 하나의 선택, 잠금 표시와 hover 정보를 표현합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ManufacturingRecipeListItemView : MonoBehaviour
{
    [SerializeField] private Button m_button;
    [SerializeField] private Image m_iconImage;
    [SerializeField] private Sprite m_fallbackIcon;
    [SerializeField] private TMP_Text m_nameText;
    [SerializeField] private ItemListSlotHoverInfo m_hoverInfo;

    private string m_recipeId;
    private bool m_isSelectable;
    private Action<string> m_clicked;
    private ItemListTooltipPresenter m_tooltipPresenter;

    public string RecipeId => m_recipeId;
    public bool IsSelectable => m_isSelectable;

    /// <summary>씬의 마스크 바깥에 배치된 공용 말풍선을 연결합니다.</summary>
    public void SetTooltipPresenter(ItemListTooltipPresenter presenter)
    {
        m_tooltipPresenter = presenter;
    }

    public void Bind(
        ManufacturingRecipeDefinition recipe,
        bool isSelectable,
        Action<string> onClicked)
    {
        CacheReferences();

        if (recipe == null || string.IsNullOrWhiteSpace(recipe.RecipeId))
        {
            Clear();
            return;
        }

        gameObject.SetActive(true);
        m_recipeId = recipe.RecipeId;
        m_isSelectable = isSelectable;
        m_clicked = onClicked;

        if (m_iconImage != null)
        {
            m_iconImage.sprite = recipe.Icon != null
                ? recipe.Icon
                : m_fallbackIcon;

            // 버튼의 TargetGraphic을 겸할 수 있으므로 아이콘이 없어도 Graphic은 유지합니다.
            m_iconImage.enabled = true;
            Color color = m_iconImage.color;
            color.a = isSelectable ? 1.0f : 0.45f;
            m_iconImage.color = color;
        }

        if (m_nameText != null)
        {
            m_nameText.text = isSelectable
                ? recipe.DisplayName
                : $"{recipe.DisplayName}\nLv.{recipe.RequiredFacilityLevel}";
        }

        if (m_hoverInfo != null)
        {
            m_hoverInfo.Bind(
                m_tooltipPresenter,
                recipe.DisplayName,
                recipe.Info);
            m_hoverInfo.SetInfoEnabled(true);
        }

        if (m_button != null)
        {
            m_button.onClick.RemoveListener(HandleClick);
            m_button.onClick.AddListener(HandleClick);
            m_button.interactable = isSelectable;
        }
    }

    public void Clear()
    {
        CacheReferences();

        m_recipeId = null;
        m_isSelectable = false;
        m_clicked = null;

        if (m_button != null)
        {
            m_button.onClick.RemoveListener(HandleClick);
            m_button.interactable = false;
        }

        if (m_iconImage != null)
        {
            m_iconImage.sprite = m_fallbackIcon;
            Color color = m_iconImage.color;
            color.a = 1.0f;
            m_iconImage.color = color;
        }

        if (m_nameText != null)
            m_nameText.text = string.Empty;

        if (m_hoverInfo != null)
            m_hoverInfo.Clear();

        gameObject.SetActive(false);
    }

    private void HandleClick()
    {
        if (m_isSelectable && !string.IsNullOrWhiteSpace(m_recipeId))
            m_clicked?.Invoke(m_recipeId);
    }

    private void CacheReferences()
    {
        if (m_button == null)
            m_button = GetComponentInChildren<Button>(true);

        if (m_iconImage == null && m_button != null)
            m_iconImage = m_button.targetGraphic as Image;

        if (m_iconImage == null)
            m_iconImage = GetComponentInChildren<Image>(true);

        if (m_hoverInfo == null)
            m_hoverInfo = GetComponent<ItemListSlotHoverInfo>();
    }
}
