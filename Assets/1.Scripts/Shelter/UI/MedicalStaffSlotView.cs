using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 의료 UI의 단일 헬퍼(스태프) 슬롯 표시와 클릭 통지를 담당.
/// 후보 목록/배치/해제 로직은 부모의 <see cref="MedicalStaffSlotController"/>가 담당한다.
/// </summary>
public class MedicalStaffSlotView : MonoBehaviour
{
    [SerializeField] private Button m_button;
    [SerializeField] private Image m_slotImage;
    [SerializeField] private Sprite m_unlockedSprite;
    [SerializeField] private Sprite m_lockedSprite;
    [SerializeField] private Button m_cancelButton;
    [FormerlySerializedAs("m_npccatalog")]
    [SerializeField] private CharacterPortraitCatalog m_characterCatalog;

    //ToDo : 추후 세이브전용 ID 필요
    [SerializeField] private string m_slotId;   // 세이브/로드 전용 고정 식별자 (런타임 로직에서는 미사용)

    private bool m_isUnlocked;
    private string m_helperRuntimeId;   // null/공백 = 비점유
    private string m_helperDefinitionId;
    private Action<MedicalStaffSlotView> m_clicked;
    private Action<MedicalStaffSlotView> m_cancelClicked;

    /// <summary>세이브/로드 시에만 사용하는 슬롯 식별자</summary>
    public string SlotId => m_slotId;

    /// <summary>현재 슬롯을 점유 중인 헬퍼 런타임 ID</summary>
    public string HelperRuntimeId => m_helperRuntimeId;

    /// <summary>슬롯에 헬퍼가 배치되어 있는지 여부</summary>
    public bool HasHelper => !string.IsNullOrWhiteSpace(m_helperRuntimeId);

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    /// <summary>
    /// 슬롯의 잠금 상태와 클릭 콜백을 설정. 헬퍼 점유 상태는 변경하지 않음
    /// </summary>
    /// <param name="isUnlocked">슬롯 해금 여부</param>
    /// <param name="onClicked">슬롯 클릭 시 호출할 콜백</param>
    /// <param name="onCancelClicked">점유 해제 버튼 클릭 시 호출할 콜백</param>
    public void Bind(
        bool isUnlocked,
        Action<MedicalStaffSlotView> onClicked,
        Action<MedicalStaffSlotView> onCancelClicked)
    {
        CacheReferences();
        m_isUnlocked = isUnlocked;
        m_clicked = onClicked;
        m_cancelClicked = onCancelClicked;

        UpdateVisual();

        if (m_button != null)
        {
            m_button.onClick.RemoveAllListeners();
            m_button.onClick.AddListener(HandleClick);
        }

        if (m_cancelButton != null)
        {
            m_cancelButton.onClick.RemoveAllListeners();
            m_cancelButton.onClick.AddListener(HandleCancelClick);
        }

        UpdateButtonStates();
    }

    /// <summary>
    /// 이 슬롯에 헬퍼 런타임 ID와 정의 ID를 배치하고 표시를 갱신
    /// </summary>
    public void SetHelper(string runtimeId, string definitionId)
    {
        m_helperRuntimeId = runtimeId;
        m_helperDefinitionId = definitionId;
        UpdateVisual();
        UpdateButtonStates();
    }

    /// <summary>
    /// 헬퍼 배치를 해제하고 슬롯 표시를 초기화
    /// </summary>
    public void ClearHelper()
    {
        m_helperRuntimeId = null;
        m_helperDefinitionId = null;
        UpdateVisual();
        UpdateButtonStates();
    }

    private void HandleClick()
    {
        // 빈 슬롯에서만 후보 목록을 여는 콜백이다.
        m_clicked?.Invoke(this);
    }

    private void HandleCancelClick()
    {
        m_cancelClicked?.Invoke(this);
    }

    // 빈 슬롯은 슬롯 버튼으로 배치하고, 점유 슬롯은 별도의 취소 버튼으로 해제한다.
    private void UpdateButtonStates()
    {
        if (m_button != null)
            m_button.interactable = m_isUnlocked && !HasHelper;

        if (m_cancelButton != null)
            m_cancelButton.gameObject.SetActive(m_isUnlocked && HasHelper);
    }

    private void UpdateVisual()
    {
        if (m_slotImage == null)
            return;

        if (!m_isUnlocked)
        {
            m_slotImage.sprite = m_lockedSprite;
            return;
        }

        if (HasHelper && m_characterCatalog != null)
        {
            Sprite portrait = m_characterCatalog.GetPortrait(m_helperDefinitionId);
            m_slotImage.sprite = portrait != null ? portrait : m_unlockedSprite;
        }
        else
        {
            m_slotImage.sprite = m_unlockedSprite;
        }
    }

    private void CacheReferences()
    {
        if (m_button == null)
            m_button = GetComponent<Button>();

        if (m_slotImage == null)
            m_slotImage = GetComponent<Image>();

        if (m_cancelButton == null)
        {
            Button[] childButtons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < childButtons.Length; i++)
            {
                if (childButtons[i] != null && childButtons[i] != m_button)
                {
                    m_cancelButton = childButtons[i];
                    break;
                }
            }
        }
    }
}
