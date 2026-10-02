using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// G 투척 모드에서 현재 선택된 투척물 아이콘을 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class GrenadeSelectionUI : MonoBehaviour
{
    [System.Serializable]
    private sealed class IconBinding
    {
        [Tooltip("아이콘을 연결할 투척물 Prefab입니다.")]
        [SerializeField] private ProjectileBase m_projectilePrefab;

        [Tooltip("해당 투척물이 선택됐을 때 표시할 Sprite입니다.")]
        [SerializeField] private Sprite m_icon;

        public ProjectileBase ProjectilePrefab => m_projectilePrefab;
        public Sprite Icon => m_icon;
    }

    [Tooltip("G 투척 모드에서만 활성화할 Canvas입니다. 비어 있으면 자식에서 자동으로 찾습니다.")]
    [SerializeField] private Canvas m_canvas;

    [Tooltip("현재 선택된 투척물 아이콘을 표시할 Image입니다. 비어 있으면 자식에서 자동으로 찾습니다.")]
    [SerializeField] private Image m_selectedIcon;

    [Tooltip("현재 선택된 투척물의 인벤토리 수량을 표시할 TMP Text입니다. 비어 있으면 실행 중 아이콘 우측 하단에 자동 생성합니다.")]
    [SerializeField] private TMP_Text m_quantityText;

    [Tooltip("수량 표시 형식입니다. {0}에 수량이 들어갑니다(예: \"x{0}\" → x3, \"{0}\" → 3).")]
    [SerializeField] private string m_quantityFormat = "x{0}";

    [Tooltip("투척물 Prefab과 UI 아이콘 Sprite의 대응 목록입니다.")]
    [SerializeField] private List<IconBinding> m_iconBindings = new List<IconBinding>();

    private ExplosiveProjectileShooter m_owner;
    private ProjectileBase m_displayedProjectile;
    private int m_displayedQuantity = -1;

    private void Awake()
    {
        ResolveReferences();
        EnsureQuantityText();
        SetVisible(false);
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// 요청한 Shooter가 투척 모드이면 UI를 표시하고 선택된 투척물 아이콘으로 갱신합니다.
    /// </summary>
    public void SetState(
        ExplosiveProjectileShooter owner,
        bool visible,
        ProjectileBase selectedProjectile,
        int quantity)
    {
        if (owner == null)
        {
            return;
        }

        if (!visible)
        {
            if (m_owner != null && m_owner != owner)
            {
                return;
            }

            m_owner = null;
            m_displayedProjectile = null;
            m_displayedQuantity = -1;
            SetVisible(false);
            return;
        }

        m_owner = owner;
        SetVisible(true);

        if (m_displayedProjectile != selectedProjectile)
        {
            m_displayedProjectile = selectedProjectile;
            RefreshIcon(selectedProjectile);
        }

        int displayedQuantity = Mathf.Max(0, quantity);
        if (m_displayedQuantity != displayedQuantity)
        {
            m_displayedQuantity = displayedQuantity;
            RefreshQuantity(displayedQuantity);
        }
    }

    private void ResolveReferences()
    {
        if (m_canvas == null)
        {
            m_canvas = GetComponentInChildren<Canvas>(true);
        }

        if (m_selectedIcon == null)
        {
            m_selectedIcon = GetComponentInChildren<Image>(true);
        }

        if (m_quantityText == null)
        {
            m_quantityText = GetComponentInChildren<TMP_Text>(true);
        }
    }

    private void EnsureQuantityText()
    {
        if (m_quantityText != null || !Application.isPlaying)
        {
            return;
        }

        Transform parent = m_selectedIcon != null
            ? m_selectedIcon.transform
            : m_canvas != null
                ? m_canvas.transform
                : transform;

        GameObject quantityObject = new GameObject("Quantity", typeof(RectTransform));
        RectTransform quantityRect = quantityObject.GetComponent<RectTransform>();
        quantityRect.SetParent(parent, false);
        quantityRect.anchorMin = Vector2.zero;
        quantityRect.anchorMax = Vector2.one;
        quantityRect.offsetMin = new Vector2(6.0f, 4.0f);
        quantityRect.offsetMax = new Vector2(-6.0f, -4.0f);

        TextMeshProUGUI quantityText = quantityObject.AddComponent<TextMeshProUGUI>();
        quantityText.alignment = TextAlignmentOptions.BottomRight;
        quantityText.fontSize = 28.0f;
        quantityText.fontStyle = FontStyles.Bold;
        quantityText.color = Color.white;
        quantityText.raycastTarget = false;
        m_quantityText = quantityText;
    }

    private void SetVisible(bool visible)
    {
        ResolveReferences();
        if (m_canvas != null && m_canvas.gameObject.activeSelf != visible)
        {
            m_canvas.gameObject.SetActive(visible);
        }
    }

    private void RefreshIcon(ProjectileBase projectile)
    {
        if (m_selectedIcon == null)
        {
            return;
        }

        Sprite icon = FindIcon(projectile);
        m_selectedIcon.sprite = icon;
        m_selectedIcon.preserveAspect = true;
        m_selectedIcon.enabled = icon != null;
    }

    private void RefreshQuantity(int quantity)
    {
        EnsureQuantityText();
        if (m_quantityText != null)
        {
            m_quantityText.text = string.IsNullOrEmpty(m_quantityFormat)
                ? quantity.ToString()
                : string.Format(m_quantityFormat, quantity);
        }
    }

    private Sprite FindIcon(ProjectileBase projectile)
    {
        if (projectile == null || m_iconBindings == null)
        {
            return null;
        }

        for (int i = 0; i < m_iconBindings.Count; i++)
        {
            IconBinding binding = m_iconBindings[i];
            if (binding != null && binding.ProjectilePrefab == projectile)
            {
                return binding.Icon;
            }
        }

        return null;
    }
}
