using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제조 재료 하나의 정적 표시와 수량 충족 상태만 표현합니다.
/// </summary>
/// <remarks>
/// 카탈로그나 창고를 직접 조회하지 않으며 제작 명령도 수행하지 않습니다.
/// 상위 목록 View가 준비한 표시 정보와 재료 견적을 <see cref="Bind"/>로 전달해야 합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class ManufacturingMaterialRowView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image m_iconImage;
    [SerializeField] private TMP_Text m_nameText;
    [SerializeField] private TMP_Text m_quantityText;

    [Header("Presentation")]
    [SerializeField] private string m_quantityFormat = "{0} / {1}";
    [SerializeField] private Color m_sufficientQuantityColor = Color.white;
    [SerializeField] private Color m_insufficientQuantityColor =
        new(1f, 0.25f, 0.25f, 1f);

    /// <summary>
    /// 재료의 이름, 아이콘, 필요량/보유량과 부족 색상을 한 번에 갱신합니다.
    /// </summary>
    public void Bind(
        ResourcePresentation presentation,
        ManufacturingMaterialQuoteLine quoteLine)
    {
        if (!string.Equals(
                presentation.ResourceId,
                quoteLine.ResourceId,
                System.StringComparison.Ordinal))
        {
            Debug.LogError(
                $"[{nameof(ManufacturingMaterialRowView)}] Presentation ID "
                + $"'{presentation.ResourceId}' does not match quote ID "
                + $"'{quoteLine.ResourceId}'.",
                this);
        }

        if (m_iconImage != null)
        {
            m_iconImage.sprite = presentation.Icon;
            m_iconImage.enabled = presentation.Icon != null;
        }

        if (m_nameText != null)
            m_nameText.text = presentation.DisplayName;

        if (m_quantityText != null)
        {
            m_quantityText.text = string.Format(
                m_quantityFormat,
                quoteLine.RequiredAmount,
                quoteLine.OwnedAmount);
            m_quantityText.color = quoteLine.IsEnough
                ? m_sufficientQuantityColor
                : m_insufficientQuantityColor;
        }
    }

    /// <summary>
    /// 캐시된 행을 다시 사용하기 전에 이전 표시값을 제거합니다.
    /// </summary>
    public void Clear()
    {
        if (m_iconImage != null)
        {
            m_iconImage.sprite = null;
            m_iconImage.enabled = false;
        }

        if (m_nameText != null)
            m_nameText.text = string.Empty;

        if (m_quantityText != null)
        {
            m_quantityText.text = string.Empty;
            m_quantityText.color = m_sufficientQuantityColor;
        }
    }
}
