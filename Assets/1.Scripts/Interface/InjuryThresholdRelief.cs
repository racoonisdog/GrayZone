/// <summary>
/// 부상 상태 임계값(정상·경상·중상 상한)을 경계별로 완화하는 델타 묶음입니다.
/// </summary>
/// <remarks>
/// 셸터 시설들이 각 경계에 주는 완화량을 하나의 값으로 담아 전달합니다.
/// 치명상은 최상단이라 별도 델타가 없습니다. 값의 적용 방향(어느 쪽으로 넓히는지)은 NPC 부상상태 규칙과의 계약으로 정합니다.
/// </remarks>
public readonly struct InjuryThresholdRelief
{
    /// <summary>정상 상한 완화 포인트입니다.</summary>
    public readonly int HealthyMaxDelta;

    /// <summary>경상 상한 완화 포인트입니다.</summary>
    public readonly int LightMaxDelta;

    /// <summary>중상 상한 완화 포인트입니다.</summary>
    public readonly int HeavyMaxDelta;

    public InjuryThresholdRelief(int healthyMaxDelta, int lightMaxDelta, int heavyMaxDelta)
    {
        HealthyMaxDelta = healthyMaxDelta;
        LightMaxDelta = lightMaxDelta;
        HeavyMaxDelta = heavyMaxDelta;
    }

    /// <summary>완화 없음(모든 델타 0)입니다.</summary>
    public static readonly InjuryThresholdRelief None = new InjuryThresholdRelief(0, 0, 0);

    /// <summary>두 완화값을 경계별로 합산합니다(여러 시설 기여 집계용).</summary>
    public static InjuryThresholdRelief operator +(InjuryThresholdRelief a, InjuryThresholdRelief b)
        => new InjuryThresholdRelief(
            a.HealthyMaxDelta + b.HealthyMaxDelta,
            a.LightMaxDelta + b.LightMaxDelta,
            a.HeavyMaxDelta + b.HeavyMaxDelta);
}
