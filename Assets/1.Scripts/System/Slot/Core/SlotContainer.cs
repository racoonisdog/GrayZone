using System;
using System.Collections.Generic;

/// <summary>
/// 인덱스를 소유하는 슬롯 컨테이너입니다. 필드 인벤토리와 셸터 보관함, 배치 슬롯이 공용으로 사용합니다.
/// </summary>
/// <remarks>
/// <para><b>이 클래스의 정체</b>: 목록이 아니라 <c>고정 길이 배열 + 빈 칸 표현</c>입니다.
/// "3번 칸"이라는 위치 자체가 데이터이며, 목록 투영형 UI는 이 위에서 조합해 만듭니다.</para>
///
/// <para><b>코어는 동결 대상입니다.</b> 여기에 두는 것은 <b>내부 구조를 알아야만 되는 연산</b>뿐입니다
/// (스택 병합, 자리 교환, 이동, 정리). 툴팁·정렬 표시·필터·드래그&amp;드롭 같이 공개 API 조합으로
/// 되는 것은 <c>System/Slot/Field</c>, <c>System/Slot/Shelter</c>의 <b>확장 메서드</b>로 각자 만듭니다.
/// 각 씬 담당자가 이 파일에 함수를 덧붙이면 같은 hunk에서 충돌하고, 여기에 필드를 추가하면
/// 반대편 시스템의 직렬화·인스펙터에까지 노출되기 때문입니다.</para>
///
/// <para><b>실패 보고 규약</b>: 게임 규칙상 정상적으로 일어날 수 있는 거절은 <see cref="SlotOpResult"/>로,
/// 범위 밖 인덱스나 null 내용물 같은 프로그래머 실수는 예외로 보고합니다.</para>
///
/// <para><b>원자성 규약</b>: 모든 연산은 <b>전부 검사한 뒤 전부 적용</b>합니다. 중간에 실패하면 아무것도 바꾸지 않으며,
/// 이벤트는 변경을 전부 확정한 뒤에 발생합니다. 되돌리기는 외부 시스템의 책임이라는 분담이 성립하려면
/// 절반만 적용된 상태가 관측 가능해서는 안 되기 때문입니다.
/// 다만 수량 일부만 수용된 <b>부분 성공은 실패가 아니며</b> <c>out int leftover</c>로 보고합니다
/// (인벤토리 시스템 §13 "공간 부족을 이유로 이동 전체를 취소하지 않는다").</para>
///
/// <para><b>다른 컨테이너로의 이동</b>은 코어에 두지 않습니다. 대상에 먼저 넣고 실제로 들어간 만큼만 원본에서 빼면
/// 두 컨테이너 사이에서도 절반 적용 상태가 생기지 않습니다.
/// <code>
/// target.TryAdd(content, qty, out int leftover);
/// source.TryRemove(index, qty - leftover);   // 보유가 확인된 수량이라 실패하지 않음
/// </code></para>
///
/// <para><b>다른 종류 위에 전체 스택을 놓으면 자리 교환</b>이라는 규칙(인벤토리 시스템 §12)은 코어가 판단하지 않습니다.
/// 코어는 <see cref="SlotOpResult.ContentMismatch"/>만 돌려주고, 드래그&amp;드롭 확장이
/// "전체 스택이었으니 <see cref="TrySwap"/>로 바꾼다"를 결정합니다.</para>
/// </remarks>
/// <typeparam name="T">칸에 담기는 내용물의 종류. 아이템 정의 ID, 아이템 정의 참조, 배치된 캐릭터 등.</typeparam>
public sealed class SlotContainer<T>
{
    /// <summary>스택 한도를 지정하지 않았을 때 쓰는 기본 한도입니다.</summary>
    public const int DefaultStackLimit = 9999;

    private readonly SlotState<T>[] slots;
    private readonly Func<T, int> stackLimitSelector;
    private readonly IEqualityComparer<T> contentComparer;
    private int unlockedCount;

    /// <summary>칸 하나가 바뀌었을 때 그 인덱스와 함께 발생합니다. 변경이 전부 확정된 뒤에 발생합니다.</summary>
    public event Action<int> SlotChanged;

    /// <summary>여러 칸이 한꺼번에 바뀌어 개별 통지가 무의미할 때 발생합니다. 현재는 <see cref="TryCompact"/>만 사용합니다.</summary>
    public event Action BulkChanged;

    /// <summary>잠긴 칸을 포함한 전체 칸 수입니다. 생성 이후 변하지 않습니다.</summary>
    /// <remarks>
    /// 슬롯 수의 정본이 이 값 하나뿐이라, 다른 사람이 만든 확장 파일에서
    /// 목록에 항목을 넣고 빼는 우회로로 불변식이 조용히 뚫리지 않습니다.
    /// </remarks>
    public int SlotCount => slots.Length;

    /// <summary>현재 해금되어 사용할 수 있는 칸 수입니다.</summary>
    public int UnlockedCount => unlockedCount;

    /// <summary>전체 칸이 해금된 컨테이너를 만듭니다. 고정 슬롯 수를 쓰는 필드 인벤토리용입니다.</summary>
    /// <param name="capacity">전체 칸 수.</param>
    /// <param name="stackLimitSelector">
    /// 내용물별 최대 스택 수량을 돌려줍니다. <c>null</c>이면 전부 <see cref="DefaultStackLimit"/>로 봅니다.
    /// 1 미만을 돌려주면 1로 봅니다. 코어가 아이템 정의를 몰라도 되게 하려고 주입으로 받습니다.
    /// </param>
    /// <param name="contentComparer">
    /// 같은 내용물인지 판정합니다. <c>null</c>이면 <see cref="EqualityComparer{T}.Default"/>를 씁니다.
    /// 현재 규칙은 "아이템 ID가 같으면 동일"이지만, 내구도·잔탄 같은 개별 상태를 가진 아이템이 추가되면
    /// 스택 조건을 다시 정의해야 한다고 인벤토리 시스템 §3.4와 §19에 예고돼 있어 주입점을 열어 둡니다.
    /// </param>
    public SlotContainer(
        int capacity,
        Func<T, int> stackLimitSelector = null,
        IEqualityComparer<T> contentComparer = null)
        : this(capacity, capacity, stackLimitSelector, contentComparer)
    {
    }

    /// <summary>앞에서부터 <paramref name="unlockedCount"/>개만 해금된 컨테이너를 만듭니다.</summary>
    /// <remarks>
    /// 축소가 아니라 해금으로 용량을 늘리는 이유: 인덱스가 영구히 안정되어
    /// 세이브·외부 참조·UI 재바인딩이 용량 변화의 영향을 받지 않습니다.
    /// 의료실 환자 슬롯(레벨별 1→4)이 이미 이 모델로 동작하고 있습니다.
    /// </remarks>
    /// <param name="capacity">전체 칸 수. 해금으로 도달할 수 있는 최대치입니다.</param>
    /// <param name="unlockedCount">처음부터 해금해 둘 칸 수. 나머지 꼬리는 잠긴 상태로 만듭니다.</param>
    /// <param name="stackLimitSelector">내용물별 최대 스택 수량. <c>null</c>이면 <see cref="DefaultStackLimit"/>.</param>
    /// <param name="contentComparer">내용물 동등성 판정. <c>null</c>이면 <see cref="EqualityComparer{T}.Default"/>.</param>
    public SlotContainer(
        int capacity,
        int unlockedCount,
        Func<T, int> stackLimitSelector = null,
        IEqualityComparer<T> contentComparer = null)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "칸 수는 0 이상이어야 합니다.");
        if (unlockedCount < 0 || unlockedCount > capacity)
            throw new ArgumentOutOfRangeException(nameof(unlockedCount), unlockedCount, "해금 칸 수는 0 이상 capacity 이하여야 합니다.");

        slots = new SlotState<T>[capacity];
        this.stackLimitSelector = stackLimitSelector;
        this.contentComparer = contentComparer ?? EqualityComparer<T>.Default;
        this.unlockedCount = unlockedCount;

        // default(SlotState<T>)가 해금된 빈 칸이므로, 잠글 꼬리만 명시적으로 표시합니다.
        for (int i = unlockedCount; i < capacity; i++)
            slots[i].SetLocked(true);
    }

    // ─────────────────────────────────────────────────────────────
    // 조회
    // ─────────────────────────────────────────────────────────────

    /// <summary>해당 칸이 해금되어 사용할 수 있는지 여부입니다.</summary>
    public bool IsUnlocked(int index)
    {
        ValidateIndex(index);
        return slots[index].IsUnlocked;
    }

    /// <summary>해당 칸이 비어 있는지 여부입니다. 잠긴 칸도 내용물이 없으므로 <c>true</c>입니다.</summary>
    public bool IsEmpty(int index)
    {
        ValidateIndex(index);
        return slots[index].IsEmpty;
    }

    /// <summary>해당 칸의 내용물이 고정되어 있는지 여부입니다.</summary>
    public bool IsContentLocked(int index)
    {
        ValidateIndex(index);
        return slots[index].IsContentLocked;
    }

    /// <summary>해당 칸의 내용물입니다. 빈 칸이면 <c>default(T)</c>입니다.</summary>
    public T GetContent(int index)
    {
        ValidateIndex(index);
        return slots[index].Content;
    }

    /// <summary>해당 칸의 수량입니다. 빈 칸이면 0입니다.</summary>
    public int GetQuantity(int index)
    {
        ValidateIndex(index);
        return slots[index].Quantity;
    }

    /// <summary>여러 칸에 흩어진 것을 합쳐 해당 내용물의 총 보유 수량을 셉니다.</summary>
    public int CountOf(T content)
    {
        ValidateContent(content);

        int total = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsEmpty || !AreSameContent(slots[i].Content, content))
                continue;

            total += slots[i].Quantity;
        }

        return total;
    }

    /// <summary>해당 내용물을 요청 수량 이상 보유하고 있는지 확인합니다.</summary>
    public bool Contains(T content, int quantity)
    {
        ValidateContent(content);

        if (quantity <= 0)
            return true;

        return CountOf(content) >= quantity;
    }

    // ─────────────────────────────────────────────────────────────
    // 잠금
    // ─────────────────────────────────────────────────────────────

    /// <summary>잠긴 칸을 해금합니다. 이미 해금돼 있으면 <see cref="SlotOpResult.NoChange"/>입니다.</summary>
    /// <remarks>재잠금 API는 두지 않습니다. 용량 축소가 게임에 존재하지 않아 지금은 쓰일 데가 없습니다.</remarks>
    public SlotOpResult TryUnlock(int index)
    {
        ValidateIndex(index);

        if (slots[index].IsUnlocked)
            return SlotOpResult.NoChange;

        slots[index].SetLocked(false);
        unlockedCount++;
        SlotChanged?.Invoke(index);
        return SlotOpResult.Success;
    }

    /// <summary>해당 칸의 내용물 고정 여부를 바꿉니다. 빈 칸도 미리 고정해 예약 칸으로 쓸 수 있습니다.</summary>
    public SlotOpResult TrySetContentLocked(int index, bool contentLocked)
    {
        ValidateIndex(index);

        if (slots[index].IsLocked)
            return SlotOpResult.SlotDisabled;
        if (slots[index].IsContentLocked == contentLocked)
            return SlotOpResult.NoChange;

        slots[index].SetContentLocked(contentLocked);
        SlotChanged?.Invoke(index);
        return SlotOpResult.Success;
    }

    // ─────────────────────────────────────────────────────────────
    // 내용물 변경
    // ─────────────────────────────────────────────────────────────

    /// <summary>지정한 칸에 내용물을 넣습니다. 같은 내용물이 이미 있으면 스택 한도까지 합칩니다.</summary>
    /// <param name="leftover">스택 한도에 걸려 넣지 못한 수량입니다. 실패한 경우 요청 수량 전체가 돌아옵니다.</param>
    public SlotOpResult TryPlace(int index, T content, int quantity, out int leftover)
    {
        ValidateIndex(index);
        ValidateContent(content);
        ValidateQuantity(quantity);

        leftover = quantity;
        if (quantity == 0)
        {
            leftover = 0;
            return SlotOpResult.NoChange;
        }

        if (slots[index].IsLocked)
            return SlotOpResult.SlotDisabled;
        if (slots[index].IsContentLocked)
            return SlotOpResult.ContentLocked;

        int current = 0;
        if (!slots[index].IsEmpty)
        {
            if (!AreSameContent(slots[index].Content, content))
                return SlotOpResult.ContentMismatch;

            current = slots[index].Quantity;
        }

        int accepted = Math.Min(quantity, GetStackLimit(content) - current);
        if (accepted <= 0)
            return SlotOpResult.StackLimitReached;

        slots[index].SetContent(content, current + accepted);
        leftover = quantity - accepted;
        SlotChanged?.Invoke(index);
        return leftover > 0 ? SlotOpResult.StackLimitReached : SlotOpResult.Success;
    }

    /// <summary>칸을 지정하지 않고 넣습니다. 미완성 스택을 먼저 채우고, 남으면 앞쪽 빈 칸부터 사용합니다.</summary>
    /// <remarks>
    /// 배치 순서는 인벤토리 시스템 §11.3(①동일 아이템의 미완성 스택 ②표시 순서상 첫 번째 빈 슬롯 ③다음 빈 슬롯)을 따릅니다.
    /// 전부 넣지 못해도 넣을 수 있는 만큼은 넣습니다(§13). 한 칸도 못 넣은 경우와 일부만 넣은 경우는
    /// 둘 다 <see cref="SlotOpResult.StackLimitReached"/>이며 <paramref name="leftover"/>로 구분합니다.
    /// </remarks>
    /// <param name="leftover">공간이 없어 넣지 못한 수량입니다.</param>
    public SlotOpResult TryAdd(T content, int quantity, out int leftover)
    {
        ValidateContent(content);
        ValidateQuantity(quantity);

        leftover = quantity;
        if (quantity == 0)
        {
            leftover = 0;
            return SlotOpResult.NoChange;
        }

        int limit = GetStackLimit(content);
        int remaining = quantity;

        // 관측 가능한 중간 상태를 만들지 않기 위해, 먼저 계획만 세운 뒤 한 번에 적용합니다.
        List<int> touchedIndices = null;
        int[] plannedQuantities = null;

        // 1패스: 같은 내용물의 미완성 스택부터 채웁니다.
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (!IsWritable(i) || slots[i].IsEmpty)
                continue;
            if (!AreSameContent(slots[i].Content, content))
                continue;

            int accepted = Math.Min(remaining, limit - slots[i].Quantity);
            if (accepted <= 0)
                continue;

            EnsurePlan(ref touchedIndices, ref plannedQuantities);
            touchedIndices.Add(i);
            plannedQuantities[i] = slots[i].Quantity + accepted;
            remaining -= accepted;
        }

        // 2패스: 표시 순서대로 빈 칸을 채웁니다.
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (!IsWritable(i) || !slots[i].IsEmpty)
                continue;

            int accepted = Math.Min(remaining, limit);
            EnsurePlan(ref touchedIndices, ref plannedQuantities);
            touchedIndices.Add(i);
            plannedQuantities[i] = accepted;
            remaining -= accepted;
        }

        if (touchedIndices == null)
            return SlotOpResult.StackLimitReached;

        for (int i = 0; i < touchedIndices.Count; i++)
        {
            int index = touchedIndices[i];
            slots[index].SetContent(content, plannedQuantities[index]);
        }

        leftover = remaining;

        for (int i = 0; i < touchedIndices.Count; i++)
            SlotChanged?.Invoke(touchedIndices[i]);

        return leftover > 0 ? SlotOpResult.StackLimitReached : SlotOpResult.Success;
    }

    /// <summary>지정한 칸에서 수량을 뺍니다. 보유 수량이 모자라면 아무것도 바꾸지 않습니다.</summary>
    public SlotOpResult TryRemove(int index, int quantity)
    {
        ValidateIndex(index);
        ValidateQuantity(quantity);

        if (quantity == 0)
            return SlotOpResult.NoChange;
        if (slots[index].IsLocked)
            return SlotOpResult.SlotDisabled;
        if (slots[index].IsContentLocked)
            return SlotOpResult.ContentLocked;
        if (slots[index].IsEmpty)
            return SlotOpResult.SlotEmpty;
        if (slots[index].Quantity < quantity)
            return SlotOpResult.NotEnoughQuantity;

        // 수량이 0이 되면 SetContent가 빈 칸으로 정규화합니다.
        slots[index].SetContent(slots[index].Content, slots[index].Quantity - quantity);
        SlotChanged?.Invoke(index);
        return SlotOpResult.Success;
    }

    /// <summary>지정한 칸을 통째로 비웁니다. 이미 비어 있으면 <see cref="SlotOpResult.NoChange"/>입니다.</summary>
    public SlotOpResult TryClear(int index)
    {
        ValidateIndex(index);

        if (slots[index].IsLocked)
            return SlotOpResult.SlotDisabled;
        if (slots[index].IsContentLocked)
            return SlotOpResult.ContentLocked;
        if (slots[index].IsEmpty)
            return SlotOpResult.NoChange;

        slots[index].ClearContent();
        SlotChanged?.Invoke(index);
        return SlotOpResult.Success;
    }

    /// <summary>두 칸의 내용물을 맞바꿉니다. 잠금 상태는 칸의 속성이므로 따라가지 않습니다.</summary>
    /// <remarks>
    /// 스택 한도는 내용물마다 정해지므로 자리만 바뀌는 교환에서는 한도를 다시 검사하지 않습니다.
    /// </remarks>
    public SlotOpResult TrySwap(int indexA, int indexB)
    {
        ValidateIndex(indexA);
        ValidateIndex(indexB);

        if (indexA == indexB)
            return SlotOpResult.NoChange;
        if (slots[indexA].IsLocked || slots[indexB].IsLocked)
            return SlotOpResult.SlotDisabled;
        if (slots[indexA].IsContentLocked || slots[indexB].IsContentLocked)
            return SlotOpResult.ContentLocked;
        if (slots[indexA].IsEmpty && slots[indexB].IsEmpty)
            return SlotOpResult.NoChange;

        T contentA = slots[indexA].Content;
        int quantityA = slots[indexA].Quantity;

        slots[indexA].SetContent(slots[indexB].Content, slots[indexB].Quantity);
        slots[indexB].SetContent(contentA, quantityA);

        SlotChanged?.Invoke(indexA);
        SlotChanged?.Invoke(indexB);
        return SlotOpResult.Success;
    }

    /// <summary>같은 컨테이너 안에서 한 칸의 내용물 일부 또는 전부를 다른 칸으로 옮깁니다.</summary>
    /// <remarks>
    /// 대상 칸에 다른 종류가 들어 있으면 <see cref="SlotOpResult.ContentMismatch"/>입니다.
    /// 전체 스택이었다면 자리 교환으로 처리한다는 규칙(인벤토리 시스템 §12)은 코어가 아니라 호출부가 판단합니다.
    /// 합치고 남은 수량은 원래 칸에 그대로 남습니다.
    /// </remarks>
    /// <param name="leftover">대상의 스택 한도에 걸려 옮기지 못하고 원래 칸에 남은 수량입니다.</param>
    public SlotOpResult TryMove(int fromIndex, int toIndex, int quantity, out int leftover)
    {
        ValidateIndex(fromIndex);
        ValidateIndex(toIndex);
        ValidateQuantity(quantity);

        leftover = quantity;
        if (quantity == 0 || fromIndex == toIndex)
        {
            leftover = 0;
            return SlotOpResult.NoChange;
        }

        if (slots[fromIndex].IsLocked || slots[toIndex].IsLocked)
            return SlotOpResult.SlotDisabled;
        if (slots[fromIndex].IsContentLocked || slots[toIndex].IsContentLocked)
            return SlotOpResult.ContentLocked;
        if (slots[fromIndex].IsEmpty)
            return SlotOpResult.SlotEmpty;
        if (slots[fromIndex].Quantity < quantity)
            return SlotOpResult.NotEnoughQuantity;

        T content = slots[fromIndex].Content;

        int current = 0;
        if (!slots[toIndex].IsEmpty)
        {
            if (!AreSameContent(slots[toIndex].Content, content))
                return SlotOpResult.ContentMismatch;

            current = slots[toIndex].Quantity;
        }

        int accepted = Math.Min(quantity, GetStackLimit(content) - current);
        if (accepted <= 0)
            return SlotOpResult.StackLimitReached;

        slots[toIndex].SetContent(content, current + accepted);
        slots[fromIndex].SetContent(content, slots[fromIndex].Quantity - accepted);
        leftover = quantity - accepted;

        SlotChanged?.Invoke(fromIndex);
        SlotChanged?.Invoke(toIndex);
        return leftover > 0 ? SlotOpResult.StackLimitReached : SlotOpResult.Success;
    }

    /// <summary>흩어진 내용물을 앞쪽으로 모으고 같은 종류의 미완성 스택을 합칩니다.</summary>
    /// <remarks>
    /// 잠긴 칸과 내용물이 고정된 칸은 자리를 지키며, 정리 대상끼리의 상대 순서는 유지합니다.
    /// 여러 칸이 한꺼번에 바뀌므로 개별 <see cref="SlotChanged"/> 대신 <see cref="BulkChanged"/>가 한 번 발생합니다.
    /// </remarks>
    public SlotOpResult TryCompact()
    {
        SlotState<T>[] compacted = (SlotState<T>[])slots.Clone();

        // 옮길 수 있는 것만 순서대로 뽑아내고, 그 자리는 비웁니다.
        List<T> contents = null;
        List<int> quantities = null;
        for (int i = 0; i < compacted.Length; i++)
        {
            if (!IsWritable(i) || compacted[i].IsEmpty)
                continue;

            contents ??= new List<T>();
            quantities ??= new List<int>();
            contents.Add(compacted[i].Content);
            quantities.Add(compacted[i].Quantity);
            compacted[i].ClearContent();
        }

        if (contents == null)
            return SlotOpResult.NoChange;

        for (int entry = 0; entry < contents.Count; entry++)
        {
            T content = contents[entry];
            int limit = GetStackLimit(content);
            int remaining = quantities[entry];

            for (int i = 0; i < compacted.Length && remaining > 0; i++)
            {
                if (!IsWritable(i))
                    continue;
                if (!compacted[i].IsEmpty && !AreSameContent(compacted[i].Content, content))
                    continue;

                int current = compacted[i].IsEmpty ? 0 : compacted[i].Quantity;
                int accepted = Math.Min(remaining, limit - current);
                if (accepted <= 0)
                    continue;

                compacted[i].SetContent(content, current + accepted);
                remaining -= accepted;
            }

            // 뽑아낸 것을 도로 담는 것이므로 자리가 모자랄 수 없습니다.
            if (remaining > 0)
                throw new InvalidOperationException("슬롯 정리 중 원래 있던 수량을 되담지 못했습니다. 스택 한도가 호출 사이에 바뀌었을 수 있습니다.");
        }

        if (!HasAnyDifference(compacted))
            return SlotOpResult.NoChange;

        Array.Copy(compacted, slots, slots.Length);
        BulkChanged?.Invoke();
        return SlotOpResult.Success;
    }

    // ─────────────────────────────────────────────────────────────
    // 내부
    // ─────────────────────────────────────────────────────────────

    /// <summary>해금됐고 내용물도 고정돼 있지 않아 코어가 자유롭게 쓸 수 있는 칸인지 여부입니다.</summary>
    private bool IsWritable(int index)
    {
        return slots[index].IsUnlocked && !slots[index].IsContentLocked;
    }

    private bool AreSameContent(T left, T right)
    {
        return contentComparer.Equals(left, right);
    }

    private int GetStackLimit(T content)
    {
        if (stackLimitSelector == null)
            return DefaultStackLimit;

        int limit = stackLimitSelector(content);
        return limit < 1 ? 1 : limit;
    }

    private bool HasAnyDifference(SlotState<T>[] other)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Quantity != other[i].Quantity)
                return true;
            if (!slots[i].IsEmpty && !AreSameContent(slots[i].Content, other[i].Content))
                return true;
        }

        return false;
    }

    private void EnsurePlan(ref List<int> touchedIndices, ref int[] plannedQuantities)
    {
        touchedIndices ??= new List<int>();
        plannedQuantities ??= new int[slots.Length];
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= slots.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"슬롯 인덱스가 범위를 벗어났습니다. 유효 범위는 0 이상 {slots.Length} 미만입니다.");
    }

    private static void ValidateContent(T content)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content), "빈 칸은 null 내용물이 아니라 수량 0으로 표현합니다.");
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "수량은 음수일 수 없습니다.");
    }
}
