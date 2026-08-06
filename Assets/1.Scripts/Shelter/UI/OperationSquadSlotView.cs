using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 출격 슬롯 하나의 캐릭터 초상화와 선택 해제 입력을 표시합니다.
/// 캐릭터 선택 정책과 명단 변경은 부모 OperationUI/OperationCenter가 담당합니다.
/// </summary>
public sealed class OperationSquadSlotView : MonoBehaviour
{
    [Header("Controls")]
    [SerializeField] private Button m_slotButton;
    [SerializeField] private Button m_removeButton;

    [Header("Presentation")]
    [SerializeField] private Image m_portraitImage;
    [SerializeField] private Sprite m_emptySprite;
    [SerializeField] private TMP_Text m_nameText;
    [SerializeField] private CharacterPortraitCatalog m_characterPortraitCatalog;

    private int m_slotIndex = -1;
    private string m_runtimeId = string.Empty;
    private string m_definitionId = string.Empty;
    private Action<int> m_removeRequested;

    public int SlotIndex => m_slotIndex;
    public string RuntimeId => m_runtimeId;
    public bool HasCharacter => !string.IsNullOrWhiteSpace(m_runtimeId);

    private void Awake()
    {
        CacheReferences();
        RefreshVisual();
    }

    private void Reset()
    {
        CacheReferences();
    }

    private void OnDestroy()
    {
        Unbind();
    }

    /// <summary>슬롯 순서와 선택 해제 요청을 받을 부모 콜백을 연결합니다.</summary>
    public void Bind(int slotIndex, Action<int> onRemoveRequested)
    {
        CacheReferences();
        UnbindButtonListeners();

        m_slotIndex = slotIndex;
        m_removeRequested = onRemoveRequested;

        if (m_slotButton != null)
            m_slotButton.onClick.AddListener(HandleRemoveClicked);

        if (m_removeButton != null && m_removeButton != m_slotButton)
            m_removeButton.onClick.AddListener(HandleRemoveClicked);

        RefreshVisual();
    }

    /// <summary>현재 슬롯의 부모 콜백과 버튼 연결을 해제합니다.</summary>
    public void Unbind()
    {
        UnbindButtonListeners();
        m_removeRequested = null;
    }

    /// <summary>부모가 해석한 캐릭터 정보를 받아 슬롯 표시를 갱신합니다.</summary>
    public void SetCharacter(ShelterMemberRuntimeData character)
    {
        if (character == null || string.IsNullOrWhiteSpace(character.RuntimeId))
        {
            ClearCharacter();
            return;
        }

        m_runtimeId = character.RuntimeId;
        m_definitionId = character.DefinitionId;

        if (m_nameText != null)
            m_nameText.text = character.DisplayName;

        RefreshVisual();
    }

    /// <summary>캐릭터 정보를 비우고 빈 슬롯 표시로 되돌립니다.</summary>
    public void ClearCharacter()
    {
        m_runtimeId = string.Empty;
        m_definitionId = string.Empty;

        if (m_nameText != null)
            m_nameText.text = string.Empty;

        RefreshVisual();
    }

    private void HandleRemoveClicked()
    {
        if (HasCharacter && m_slotIndex >= 0)
            m_removeRequested?.Invoke(m_slotIndex);
    }

    private void RefreshVisual()
    {
        Sprite portrait = null;
        if (HasCharacter && m_characterPortraitCatalog != null)
            portrait = m_characterPortraitCatalog.GetPortrait(m_definitionId);

        if (m_portraitImage != null)
        {
            m_portraitImage.sprite = portrait != null ? portrait : m_emptySprite;
            m_portraitImage.enabled = m_portraitImage.sprite != null;
        }

        if (m_slotButton != null)
            m_slotButton.interactable = HasCharacter;

        if (m_removeButton != null && m_removeButton != m_slotButton)
            m_removeButton.gameObject.SetActive(HasCharacter);
    }

    private void UnbindButtonListeners()
    {
        if (m_slotButton != null)
            m_slotButton.onClick.RemoveListener(HandleRemoveClicked);

        if (m_removeButton != null && m_removeButton != m_slotButton)
            m_removeButton.onClick.RemoveListener(HandleRemoveClicked);
    }

    private void CacheReferences()
    {
        if (m_slotButton == null)
            m_slotButton = GetComponent<Button>();

        if (m_portraitImage == null)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].gameObject != gameObject)
                {
                    m_portraitImage = images[i];
                    break;
                }
            }
        }

        if (m_portraitImage == null)
            m_portraitImage = GetComponent<Image>();

        if (m_emptySprite == null && m_portraitImage != null)
            m_emptySprite = m_portraitImage.sprite;
    }
}
