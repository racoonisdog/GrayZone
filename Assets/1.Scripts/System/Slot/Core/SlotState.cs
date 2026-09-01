using System;

/// <summary>
/// 슬롯 한 칸의 런타임 상태입니다. 인덱스를 소유하는 슬롯 자료구조의 최소 단위입니다.
/// </summary>
/// <remarks>
/// <para><b>기본값 규약</b>: <c>default(SlotState&lt;T&gt;)</c>는 곧 <b>해금된 빈 칸</b>입니다.
/// 그래서 잠금을 <c>IsUnlocked</c>가 아니라 <see cref="IsLocked"/>(기본 <c>false</c> = 해금)로 저장합니다.
/// <c>new SlotState&lt;T&gt;[n]</c>이 그대로 "쓸 수 있는 빈 인벤토리"가 되어야
/// 세이브 로드나 테스트에서 해금을 잊어 통째로 못 쓰는 칸이 생기지 않습니다.
/// 잠긴 칸은 <see cref="SlotContainer{T}"/>가 생성 시점에 명시적으로 만듭니다.</para>
///
/// <para><b>수량 0 = 빈 칸</b>입니다. 종류는 남아 있는데 수량만 0인 유령 상태는 만들지 않으며,
/// 이는 기존 <see cref="ItemStorageEntry"/>의 유효성 규약과 같습니다.</para>
///
/// <para><b>구조체인 이유</b>: 배열 원소로 제자리 수정하기 위해서입니다. 수량 증감이 주 연산인데
/// 클래스였다면 null 상태가 표현 가능해지고, List 원소였다면 제자리 수정이 컴파일되지 않습니다.</para>
///
/// <para><b>변경 메서드가 internal인 이유</b>: 잠금·수량·내용물의 조합 규칙(예: 잠긴 칸에는 넣을 수 없다)은
/// 칸 하나가 아니라 <see cref="SlotContainer{T}"/>가 소유합니다. 이 구조체는 규칙을 판단하지 않고 상태만 담습니다.
/// asmdef를 나누지 않으므로 <c>internal</c>이 물리적 차단은 되지 않지만, 소유 경계를 선언하는 표식으로 둡니다.
/// 이 구조체를 값으로 복사해 받은 뒤 변경 메서드를 부르면 복사본만 바뀌고 컨테이너에는 반영되지 않습니다.</para>
/// </remarks>
/// <typeparam name="T">칸에 담기는 내용물의 종류. 아이템 정의 ID, 아이템 정의 참조, 배치된 캐릭터 등.</typeparam>
[Serializable]
public struct SlotState<T>
{
    private T m_content;
    private int m_quantity;
    private bool m_isLocked;
    private bool m_isContentLocked;

    /// <summary>이 칸이 담고 있는 내용물입니다. 빈 칸이면 <c>default(T)</c>입니다.</summary>
    public T Content => m_content;

    /// <summary>이 칸이 담고 있는 수량입니다. 빈 칸이면 0입니다.</summary>
    public int Quantity => m_quantity;

    /// <summary>아직 해금되지 않아 사용할 수 없는 칸인지 여부입니다. 기본값은 <c>false</c>(해금)입니다.</summary>
    public bool IsLocked => m_isLocked;

    /// <summary>해금되어 사용할 수 있는 칸인지 여부입니다.</summary>
    public bool IsUnlocked => !m_isLocked;

    /// <summary>
    /// 내용물이 고정되어 꺼내거나 덮어쓸 수 없는 칸인지 여부입니다.
    /// 칸 자체의 사용 가능 여부(<see cref="IsLocked"/>)와는 다른 축입니다.
    /// </summary>
    public bool IsContentLocked => m_isContentLocked;

    /// <summary>내용물이 없는 칸인지 여부입니다. 수량 0이 곧 빈 칸입니다.</summary>
    public bool IsEmpty => m_quantity <= 0;

    /// <summary>
    /// 내용물과 수량을 지정합니다. 수량이 0 이하이면 유령 상태를 만들지 않기 위해 빈 칸으로 정규화합니다.
    /// </summary>
    internal void SetContent(T content, int quantity)
    {
        if (quantity <= 0)
        {
            ClearContent();
            return;
        }

        m_content = content;
        m_quantity = quantity;
    }

    /// <summary>내용물을 비웁니다. 잠금 상태는 칸의 속성이므로 함께 지우지 않습니다.</summary>
    internal void ClearContent()
    {
        m_content = default;
        m_quantity = 0;
    }

    /// <summary>칸의 해금 여부를 설정합니다.</summary>
    internal void SetLocked(bool locked)
    {
        m_isLocked = locked;
    }

    /// <summary>내용물 고정 여부를 설정합니다.</summary>
    internal void SetContentLocked(bool contentLocked)
    {
        m_isContentLocked = contentLocked;
    }
}
