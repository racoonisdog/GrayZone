/// <summary>
/// 업그레이드 창의 "제공하는 기능" 한 줄을 나타내는 표시용 값입니다.
/// </summary>
/// <remarks>
/// 로직 영향 없는 표시 전용. 시설이 다음 레벨이 제공하는 내용을 이 형태의 리스트로 노출합니다.
/// </remarks>
public readonly struct FacilityFeatureLine
{
    /// <summary>항목 이름(예: "환자 슬롯").</summary>
    public readonly string Label;

    /// <summary>항목 값/변화(예: "3 → 4", "해금").</summary>
    public readonly string Value;

    /// <summary>표시할 항목 이름과 값을 묶습니다.</summary>
    /// <param name="label">항목 이름입니다.</param>
    /// <param name="value">항목 값 또는 변화 표기입니다.</param>
    public FacilityFeatureLine(string label, string value)
    {
        Label = label;
        Value = value;
    }
}
