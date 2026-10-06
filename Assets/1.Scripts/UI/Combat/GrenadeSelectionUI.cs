using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조작 대원이 고른 투척물 아이콘과 보유 수량을 표시합니다. 기본은 항상 표시이고, 설정에 따라 G 투척 모드에서만 표시합니다.
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

    [Tooltip("켜면 투척 모드가 아니어도 조작 대원이 고른 투척물 아이콘과 보유 수량을 항상 표시합니다(Figma Reviving HUD_Description_Test01). 끄면 G 투척 모드에서만 표시합니다.")]
    [SerializeField] private bool m_alwaysVisible = true;

    [Tooltip("아이콘 위에 투척 모드 키를 표시할 텍스트입니다. 선택 사항입니다.")]
    [SerializeField] private TMP_Text m_keyText;

    [Tooltip("투척 모드 키를 읽어 올 입력 액션 이름입니다. 조작 대원의 PlayerInput에서 키보드 바인딩 표시 이름을 가져옵니다.")]
    [SerializeField] private string m_keyActionName = "ThrowMode";

    [Tooltip("입력 액션에서 키를 찾지 못했을 때 표시할 글자입니다.")]
    [SerializeField] private string m_fallbackKeyLabel = "G";

    // 대원마다 고른 투척물이 다를 수 있어, 각 대원이 마지막으로 알려 준 선택을 기억합니다.
    private readonly Dictionary<ExplosiveProjectileShooter, ProjectileBase> m_selectionByOwner =
        new Dictionary<ExplosiveProjectileShooter, ProjectileBase>();
    private SquadManager m_squadManager;
    private SquadInventoryManager m_inventory;
    private SquadMemberController m_keyLabelMember;

    private ExplosiveProjectileShooter m_owner;
    private ProjectileBase m_displayedProjectile;
    private int m_displayedQuantity = -1;

    private void Awake()
    {
        ResolveReferences();
        EnsureQuantityText();
        SetVisible(false);
    }

    /// <summary>항상 표시 모드에서 조작 대원의 선택 투척물과 스쿼드 인벤토리 수량을 따라갑니다.</summary>
    private void LateUpdate()
    {
        if (!m_alwaysVisible)
        {
            return;
        }

        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        }

        if (m_inventory == null)
        {
            m_inventory = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        }

        SquadMemberController member = m_squadManager != null ? m_squadManager.PlayerSquadMember : null;
        ExplosiveProjectileShooter shooter = member != null
            ? member.GetComponentInChildren<ExplosiveProjectileShooter>(true)
            : null;
        if (shooter == null || !m_selectionByOwner.TryGetValue(shooter, out ProjectileBase projectile) || projectile == null)
        {
            // 대원이 아직 선택을 알려 주지 않았으면 목록의 첫 투척물을 보여 줍니다.
            projectile = m_iconBindings != null && m_iconBindings.Count > 0 && m_iconBindings[0] != null
                ? m_iconBindings[0].ProjectilePrefab
                : null;
        }

        SetVisible(projectile != null);
        if (projectile == null)
        {
            return;
        }

        RefreshKeyLabel(member);

        if (m_displayedProjectile != projectile)
        {
            m_displayedProjectile = projectile;
            RefreshIcon(projectile);
        }

        int quantity = m_inventory != null ? m_inventory.CountOf(projectile.InventoryItemDefinitionId) : 0;
        if (m_displayedQuantity != quantity)
        {
            m_displayedQuantity = quantity;
            RefreshQuantity(quantity);
        }
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

        if (selectedProjectile != null)
        {
            m_selectionByOwner[owner] = selectedProjectile;
        }

        // 항상 표시 모드에서는 표시 여부와 수량을 LateUpdate가 정합니다. 여기서는 선택만 기록합니다.
        if (m_alwaysVisible)
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

    /// <summary>조작 대원의 입력 액션에서 투척 모드 키의 키보드 표시 이름을 읽어 표시합니다.</summary>
    private void RefreshKeyLabel(SquadMemberController member)
    {
        // 바인딩 표시 이름은 문자열을 새로 만들므로 조작 대원이 바뀔 때만 다시 읽습니다.
        if (m_keyText == null || (member == m_keyLabelMember && !string.IsNullOrEmpty(m_keyText.text)))
        {
            return;
        }

        m_keyLabelMember = member;
        string label = m_fallbackKeyLabel;
        UnityEngine.InputSystem.PlayerInput playerInput = member != null
            ? member.GetComponentInChildren<UnityEngine.InputSystem.PlayerInput>(true)
            : null;
        UnityEngine.InputSystem.InputAction action = playerInput != null && playerInput.actions != null
            ? playerInput.actions.FindAction(m_keyActionName)
            : null;
        if (action != null)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].effectivePath.StartsWith("<Keyboard>"))
                {
                    label = UnityEngine.InputSystem.InputActionRebindingExtensions.GetBindingDisplayString(action, i);
                    break;
                }
            }
        }

        if (m_keyText.text != label)
        {
            m_keyText.text = label;
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
