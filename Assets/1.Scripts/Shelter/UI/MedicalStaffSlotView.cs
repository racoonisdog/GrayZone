using System;
using UnityEngine;
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
    [SerializeField] private NpcPortraitCatalog m_npccatalog;

    //ToDo : 추후 세이브전용 ID 필요
    [SerializeField] private string m_slotId;   // 세이브/로드 전용 고정 식별자 (런타임 로직에서는 미사용)

    private bool m_isUnlocked;
    private string m_helperId;   // null/공백 = 비점유
    private Action<MedicalStaffSlotView> m_clicked;

    /// <summary>세이브/로드 시에만 사용하는 슬롯 식별자</summary>
    public string SlotId => m_slotId;

    /// <summary>현재 슬롯을 점유 중인 헬퍼 정의 ID</summary>
    public string HelperId => m_helperId;

    /// <summary>슬롯에 헬퍼가 배치되어 있는지 여부</summary>
    public bool HasHelper => !string.IsNullOrWhiteSpace(m_helperId);

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
    public void Bind(bool isUnlocked, Action<MedicalStaffSlotView> onClicked)
    {
        CacheReferences();
        m_isUnlocked = isUnlocked;
        m_clicked = onClicked;

        UpdateVisual();

        if (m_button != null)
        {
            m_button.onClick.RemoveAllListeners();
            m_button.onClick.AddListener(HandleClick);
        }

        UpdateButtonStates();
    }

    /// <summary>
    /// 이 슬롯에 헬퍼 정의 ID를 배치하고 표시를 갱신
    /// </summary>
    /// <param name="definitionId">배치할 헬퍼 정의 ID</param>
    public void SetHelper(string definitionId)
    {
        m_helperId = definitionId;
        UpdateVisual();
        UpdateButtonStates();
    }

    /// <summary>
    /// 헬퍼 배치를 해제하고 슬롯 표시를 초기화
    /// </summary>
    public void ClearHelper()
    {
        m_helperId = null;
        UpdateVisual();
        UpdateButtonStates();
    }

    private void HandleClick()
    {
        // 빈칸/점유 분기는 컨트롤러가 HelperId로 판단.
        m_clicked?.Invoke(this);
    }

    // 해금 슬롯은 항상 클릭 가능: 빈 슬롯 클릭=배치, 점유 슬롯 클릭=배치취소(별도 취소 버튼 없음).
    private void UpdateButtonStates()
    {
        if (m_button != null)
            m_button.interactable = m_isUnlocked;
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

        if (HasHelper && m_npccatalog != null)
        {
            Sprite portrait = m_npccatalog.GetPortrait(m_helperId);
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
    }
}
