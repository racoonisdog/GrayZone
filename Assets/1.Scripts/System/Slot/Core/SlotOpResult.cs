/// <summary>
/// 슬롯 연산의 결과 코드입니다. 실패를 이유별로 구분해 호출부가 서로 다른 안내를 띄울 수 있게 합니다.
/// </summary>
/// <remarks>
/// <para>성공/실패를 1·0·-1 같은 삼상태로 뭉개지 않는 이유: "확인 버튼을 눌렀는데 실패했다"를
/// 사용자에게 설명하려면 실패 이유가 필요하고, 삼상태는 그 이유를 전부 지웁니다.</para>
/// <para>범위 밖 인덱스나 null 내용물 같은 <b>프로그래머 실수는 이 열거형이 아니라 예외</b>로 보고합니다.
/// 이 열거형은 게임 규칙상 정상적으로 일어날 수 있는 거절만 담습니다.</para>
/// <para>요청 수량 중 일부만 처리된 <b>부분 성공은 실패가 아니라</b>
/// <c>out int leftover</c>(남은 수량)로 별도 보고합니다.</para>
/// </remarks>
public enum SlotOpResult
{
    /// <summary>요청한 변경이 전부 적용됐습니다.</summary>
    Success,

    /// <summary>요청은 유효하지만 이미 그 상태여서 바뀐 것이 없습니다. 실패가 아닙니다.</summary>
    NoChange,

    /// <summary>아직 해금되지 않은 칸이라 사용할 수 없습니다.</summary>
    SlotDisabled,

    /// <summary>내용물이 고정된 칸이라 꺼내거나 덮어쓸 수 없습니다.</summary>
    ContentLocked,

    /// <summary>비어 있는 칸이라 꺼낼 것이 없습니다.</summary>
    SlotEmpty,

    /// <summary>
    /// 스택 한도에 걸려 요청 수량을 다 넣지 못했습니다. 넣지 못한 수량은 <c>leftover</c>로 돌려줍니다.
    /// 한 개도 못 넣은 경우와 일부만 넣은 경우 모두 이 값이며, 구분은 <c>leftover</c>로 합니다.
    /// </summary>
    StackLimitReached,

    /// <summary>보유 수량이 요청 수량보다 적습니다. 이 경우 아무것도 적용되지 않습니다.</summary>
    NotEnoughQuantity,

    /// <summary>다른 종류의 내용물 위에 겹치려 했습니다.</summary>
    ContentMismatch,
}
