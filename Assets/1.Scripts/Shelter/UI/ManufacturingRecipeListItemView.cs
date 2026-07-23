using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공용 후보 Scroll View 안에서 제조 레시피 하나를 표시하는 임시 행입니다.
/// </summary>
public sealed class ManufacturingRecipeListItemView : MonoBehaviour
{
    [SerializeField] private Button m_button;
    [SerializeField] private Image m_iconImage;
    [SerializeField] private TMP_Text m_nameText;

    private string m_recipeId;
    private bool m_isSelectable;
    private Action<string> m_clicked;

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
            if (recipe.Icon != null)
                m_iconImage.sprite = recipe.Icon;

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

        if (m_button != null)
        {
            m_button.onClick.RemoveListener(HandleClick);
            m_button.onClick.AddListener(HandleClick);
            m_button.interactable = isSelectable;
        }
    }

    public void Clear()
    {
        m_recipeId = null;
        m_isSelectable = false;
        m_clicked = null;

        if (m_button != null)
        {
            m_button.onClick.RemoveListener(HandleClick);
            m_button.interactable = false;
        }

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

        if (m_nameText == null)
            m_nameText = GetComponentInChildren<TMP_Text>(true);

        if (m_nameText == null)
            m_nameText = CreateRuntimeLabel();
    }

    private TMP_Text CreateRuntimeLabel()
    {
        GameObject labelObject = new(
            "RecipeName",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(transform, false);

        RectTransform rect = (RectTransform)labelObject.transform;
        rect.anchorMin = new Vector2(0.0f, 0.0f);
        rect.anchorMax = new Vector2(1.0f, 0.34f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 16.0f;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }
}
