using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 필드 인벤토리 그리드에서 슬롯 한 칸을 그립니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>인덱스에 고정 바인딩됩니다.</b> 이 뷰는 <see cref="SlotIndex"/>번 칸만 평생 그리며, 목록처럼
/// 내용에 따라 자리를 옮기지 않습니다. <see cref="SlotContainer{T}"/>가 "3번 칸"이라는 위치 자체를
/// 데이터로 소유하기 때문입니다. 셸터의 <see cref="ShelterInventoryItemView"/>가 종류별 행을 그리는 것과
/// 여기가 다른 지점입니다 — 그쪽은 인덱스가 없는 목록 투영입니다.
/// </para>
/// <para>
/// <b>판단하지 않습니다.</b> 잠금·빈 칸·아이템 중 무엇을 그릴지는 <see cref="FieldInventoryUI"/>가
/// 컨테이너를 읽어 결정하고, 이 컴포넌트는 지시받은 모양만 만듭니다. 뷰가 컨테이너를 직접 읽으면
/// 칸 수만큼 조회가 흩어져 갱신 시점을 한 곳에서 통제할 수 없습니다.
/// </para>
/// <para>
/// 참조가 비어 있으면 자식에서 찾습니다. 프리팹이 아이콘 <see cref="Image"/>와 수량 <see cref="TMP_Text"/>
/// 하나씩만 가진 단순한 모양이면 인스펙터 배선 없이도 동작하며, 이는 셸터 아이템 뷰와 같은 방식입니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class FieldInventorySlotView : MonoBehaviour
{
    [Tooltip("아이템 아이콘입니다. 빈 칸과 잠긴 칸에서는 꺼집니다. 비워 두면 자식에서 찾습니다.")]
    [SerializeField] private Image m_iconImage;

    [Tooltip("수량 표시입니다. 수량이 1이면 숨깁니다. 비워 두면 자식에서 찾습니다.")]
    [SerializeField] private TMP_Text m_quantityText;

    [Tooltip("아직 해금되지 않은 칸에 켜는 오브젝트입니다. 선택 사항이며, 없으면 잠금 표시를 생략합니다.")]
    [SerializeField] private GameObject m_lockedOverlay;

    [Tooltip("빈 칸일 때 켜는 플레이스홀더입니다. 선택 사항입니다.")]
    [SerializeField] private GameObject m_emptyPlaceholder;

    /// <summary>이 뷰가 담당하는 슬롯 인덱스입니다. <see cref="Initialize"/> 전에는 -1입니다.</summary>
    public int SlotIndex { get; private set; } = -1;

    /// <summary>담당할 슬롯 인덱스를 정합니다. 그리드를 만들 때 한 번만 부릅니다.</summary>
    /// <param name="slotIndex">이 뷰가 평생 담당할 칸 번호입니다.</param>
    public void Initialize(int slotIndex)
    {
        CacheReferences();
        SlotIndex = slotIndex;

        // 계층에서 몇 번 칸인지 눈으로 찾을 수 있게 합니다. 배선 사고를 추적할 때 이것뿐입니다.
        gameObject.name = $"Slot_{slotIndex:00}";
    }

    /// <summary>아직 해금되지 않은 칸으로 그립니다.</summary>
    public void ShowLocked()
    {
        CacheReferences();
        SetIcon(null);
        SetQuantity(0);

        if (m_emptyPlaceholder != null)
            m_emptyPlaceholder.SetActive(false);

        if (m_lockedOverlay != null)
            m_lockedOverlay.SetActive(true);
    }

    /// <summary>해금됐지만 내용물이 없는 칸으로 그립니다.</summary>
    public void ShowEmpty()
    {
        CacheReferences();
        SetIcon(null);
        SetQuantity(0);

        if (m_lockedOverlay != null)
            m_lockedOverlay.SetActive(false);

        if (m_emptyPlaceholder != null)
            m_emptyPlaceholder.SetActive(true);
    }

    /// <summary>내용물이 있는 칸으로 그립니다.</summary>
    /// <param name="icon">표시할 아이콘입니다. <c>null</c>이면 아이콘을 끕니다.</param>
    /// <param name="displayName">툴팁·접근성용 표시명입니다. 지금은 오브젝트 이름에만 씁니다.</param>
    /// <param name="quantity">표시할 수량입니다. 1이면 숫자를 숨깁니다.</param>
    public void ShowItem(Sprite icon, string displayName, int quantity)
    {
        CacheReferences();

        if (m_lockedOverlay != null)
            m_lockedOverlay.SetActive(false);

        if (m_emptyPlaceholder != null)
            m_emptyPlaceholder.SetActive(false);

        SetIcon(icon);
        SetQuantity(quantity);

        // 아이콘이 비어 있을 때 무엇이 들었는지 계층에서라도 보이게 합니다.
        // 표시명 라벨은 정식 툴팁(§8)이 들어올 때 붙습니다.
        gameObject.name = string.IsNullOrEmpty(displayName)
            ? $"Slot_{SlotIndex:00}"
            : $"Slot_{SlotIndex:00} ({displayName})";
    }

    private void SetIcon(Sprite icon)
    {
        if (m_iconImage == null)
            return;

        m_iconImage.sprite = icon;

        // sprite만 비우면 Image가 흰 사각형으로 남습니다. 표시 자체를 끕니다.
        m_iconImage.enabled = icon != null;
    }

    private void SetQuantity(int quantity)
    {
        if (m_quantityText == null)
            return;

        // 수량 1에 "1"을 붙이면 칸마다 숫자가 깔려 스택 여부가 눈에 안 들어옵니다.
        bool visible = quantity > 1;
        m_quantityText.enabled = visible;
        m_quantityText.text = visible ? quantity.ToString() : string.Empty;
    }

    private void Reset()
    {
        CacheReferences();
    }

    private void CacheReferences()
    {
        if (m_iconImage == null)
            m_iconImage = GetComponentInChildren<Image>(true);

        if (m_quantityText == null)
            m_quantityText = GetComponentInChildren<TMP_Text>(true);
    }
}
