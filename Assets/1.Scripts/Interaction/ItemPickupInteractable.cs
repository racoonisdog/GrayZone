using UnityEngine;

/// <summary>
/// 필드에 놓인 아이템을 상호작용으로 스쿼드 공용 인벤토리에 넣는 획득 지점입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>기획 편차 — 읽고 시작할 것.</b> 공용 `인벤토리 시스템` v0.1이 정의한 획득 경로는 <b>인벤토리 박스</b>(§3.2·§5.2)이고,
/// 바닥에 놓인 낱개 아이템을 줍는 규칙은 문서에 없습니다. 이 컴포넌트는 UI가 없는 상태에서 보유·획득 판정을
/// 검증하기 위한 <b>최소 획득 경로</b>이며, 박스 연동이 들어오면 그쪽에 흡수되거나 별도 규칙으로 승격되어야 합니다.
/// </para>
/// <para>
/// 그래서 문서에 없는 규칙은 여기서 만들지 않았습니다. 스폰 위치·등장 수량(§17.4), 재생성·갱신 규칙(§19)은
/// 구현하지 않으며, 다 주워진 오브젝트는 파괴하지 않고 <b>비활성</b>만 합니다. 파괴는 되돌릴 수 없고
/// 재생성 규칙이 정해지지 않은 상태에서 먼저 굳힐 판단이 아닙니다.
/// </para>
/// <para>
/// 공간이 부족하면 획득 전체를 취소하지 않고 수용 가능한 만큼만 넣습니다(§13). 넣지 못한 수량은
/// 이 오브젝트에 그대로 남아 다시 주울 수 있습니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class ItemPickupInteractable : MonoBehaviour, IInteractable
{
    [Tooltip("주웠을 때 인벤토리에 넣을 아이템의 정적 정의입니다. 비어 있으면 상호작용할 수 없습니다.")]
    [SerializeField] private ItemDefinition m_itemDefinition;

    [Tooltip("이 오브젝트에 남아 있는 수량입니다. 일부만 들어가면 나머지가 여기에 남습니다.")]
    [Min(1)]
    [SerializeField] private int m_quantity = 1;

    [Tooltip("상호작용 프롬프트에 표시할 문구입니다. 비어 있으면 아이템 표시명과 수량으로 만듭니다.")]
    [SerializeField] private string m_promptOverride = string.Empty;

    [Tooltip("0보다 크면 그 시간만큼 꾹 눌러야 획득합니다. 0이면 즉시 획득입니다.")]
    [Min(0f)]
    [SerializeField] private float m_holdDuration;

    /// <summary>이 오브젝트에 남아 있는 수량입니다.</summary>
    public int RemainingQuantity => Mathf.Max(0, m_quantity);

    /// <inheritdoc />
    public float HoldDuration => m_holdDuration;

    /// <inheritdoc />
    public bool CanInteract(GameObject interactor)
    {
        // 공간 부족은 여기서 거르지 않습니다. §13이 "공간이 부족하다는 피드백을 제공한다"고 정하므로,
        // 프롬프트가 조용히 사라지는 것이 아니라 눌렀을 때 이유를 알 수 있어야 합니다.
        return m_itemDefinition != null
            && RemainingQuantity > 0
            && ResolveInventory() != null;
    }

    /// <inheritdoc />
    public void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return;

        SquadInventoryManager inventory = ResolveInventory();
        SlotOpResult result = inventory.TryAcquire(
            m_itemDefinition.ItemDefinitionId,
            RemainingQuantity,
            out int leftover);

        int acquired = RemainingQuantity - leftover;
        if (acquired <= 0)
        {
            // §13: 수용 가능한 수량이 0이면 아이템을 이동하지 않고 공간 부족 피드백만 제공한다.
            // 실제 피드백 표시는 UI·UX 문서(§17.5)의 책임이라 여기서는 알리기만 합니다.
            Debug.Log(
                $"[ItemPickupInteractable] 인벤토리에 공간이 없어 {m_itemDefinition.DisplayName}을(를) 넣지 못했습니다. ({result})",
                this);
            return;
        }

        m_quantity = leftover;

        if (leftover > 0)
        {
            Debug.Log(
                $"[ItemPickupInteractable] {m_itemDefinition.DisplayName} {acquired}개만 획득했습니다. {leftover}개가 남았습니다.",
                this);
            return;
        }

        // 재생성·갱신 규칙(§19 미결)이 정해지지 않았으므로 파괴하지 않고 비활성만 합니다.
        gameObject.SetActive(false);
    }

    /// <inheritdoc />
    public string GetPrompt()
    {
        if (!string.IsNullOrWhiteSpace(m_promptOverride))
            return m_promptOverride;

        if (m_itemDefinition == null)
            return "획득";

        return RemainingQuantity > 1
            ? $"{m_itemDefinition.DisplayName} x{RemainingQuantity} 획득"
            : $"{m_itemDefinition.DisplayName} 획득";
    }

    /// <summary>
    /// 스쿼드 공용 인벤토리를 찾습니다. 인벤토리는 스쿼드에 하나뿐이라 상호작용 주체와 무관하게 같은 것을 씁니다(§3.1).
    /// </summary>
    private static SquadInventoryManager ResolveInventory()
    {
        return FieldManager.Instance != null
            ? FieldManager.Instance.SquadInventory
            : null;
    }
}
