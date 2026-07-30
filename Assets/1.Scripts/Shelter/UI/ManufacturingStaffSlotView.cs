using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제조 시설 헬퍼 슬롯 하나의 잠금, 초상화, 배치/해제 버튼을 표시합니다.
/// </summary>
public sealed class ManufacturingStaffSlotView : MonoBehaviour
{
    [SerializeField] private Button m_button;
    [SerializeField] private Image m_slotImage;
    [SerializeField] private Sprite m_unlockedSprite;
    [SerializeField] private Sprite m_lockedSprite;
    [SerializeField] private Button m_cancelButton;
    [SerializeField] private CharacterPortraitCatalog m_characterCatalog;

    private bool m_isUnlocked;
    private string m_helperRuntimeId;
    private string m_helperDefinitionId;
    private Action<ManufacturingStaffSlotView> m_clicked;
    private Action<ManufacturingStaffSlotView> m_cancelClicked;

    public string HelperRuntimeId => m_helperRuntimeId ?? string.Empty;
    public bool HasHelper => !string.IsNullOrWhiteSpace(m_helperRuntimeId);

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    public void Bind(
        bool isUnlocked,
        Action<ManufacturingStaffSlotView> onClicked,
        Action<ManufacturingStaffSlotView> onCancelClicked)
    {
        CacheReferences();
        m_isUnlocked = isUnlocked;
        m_clicked = onClicked;
        m_cancelClicked = onCancelClicked;

        if (m_button != null)
        {
            m_button.onClick.RemoveListener(HandleClick);
            m_button.onClick.AddListener(HandleClick);
        }

        if (m_cancelButton != null)
        {
            m_cancelButton.onClick.RemoveListener(HandleCancelClick);
            m_cancelButton.onClick.AddListener(HandleCancelClick);
        }

        RefreshVisual();
    }

    public void SetHelper(string runtimeId, string definitionId)
    {
        m_helperRuntimeId = runtimeId;
        m_helperDefinitionId = definitionId;
        RefreshVisual();
    }

    public void ClearHelper()
    {
        m_helperRuntimeId = null;
        m_helperDefinitionId = null;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (m_slotImage != null)
        {
            Sprite sprite;
            if (!m_isUnlocked)
            {
                sprite = m_lockedSprite;
            }
            else if (HasHelper && m_characterCatalog != null)
            {
                sprite = m_characterCatalog.GetPortrait(m_helperDefinitionId);
                if (sprite == null)
                    sprite = m_unlockedSprite;
            }
            else
            {
                sprite = m_unlockedSprite;
            }

            m_slotImage.sprite = sprite;
            m_slotImage.enabled = sprite != null;
        }

        if (m_button != null)
            m_button.interactable = m_isUnlocked && !HasHelper;

        if (m_cancelButton != null)
            m_cancelButton.gameObject.SetActive(m_isUnlocked && HasHelper);
    }

    private void HandleClick()
    {
        if (m_isUnlocked && !HasHelper)
            m_clicked?.Invoke(this);
    }

    private void HandleCancelClick()
    {
        if (m_isUnlocked && HasHelper)
            m_cancelClicked?.Invoke(this);
    }

    private void CacheReferences()
    {
        if (m_button == null)
            m_button = GetComponent<Button>();

        if (m_cancelButton == null)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i] != m_button)
                {
                    m_cancelButton = buttons[i];
                    break;
                }
            }
        }

        if (m_slotImage == null)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].gameObject != gameObject)
                {
                    m_slotImage = images[i];
                    break;
                }
            }
        }

        if (m_slotImage == null)
            m_slotImage = GetComponent<Image>();

        if (m_unlockedSprite == null && m_slotImage != null)
            m_unlockedSprite = m_slotImage.sprite;
    }
}
