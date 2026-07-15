using UnityEngine;

/// <summary>
/// 부상 심각도 감소 기반 치료 소요 일수 계산기. 상태를 보유하지 않는 순수 함수 모음이다.
/// 현재 심각도와 일일 감소량만으로 치료 계획(총 일수·첫 틱 보정량)을 산출한다.
/// </summary>
public static class TreatmentDurationCalculator
{
    /// <summary>
    /// 현재 게이지 기준으로 치료 계획을 산출한다.
    /// </summary>
    /// <param name="currentGauge">0이 완치인 현재 부상 심각도 게이지</param>
    /// <param name="maxGauge">부상 심각도 게이지 최대값</param>
    /// <param name="dailyRecovery">일일 심각도 감소량(게이지/일). 내부적으로 최소 1로 보정한다.</param>
    /// <returns>총 치료 일수와 첫날 회복 보정량을 담은 치료 계획</returns>
    public static TreatmentPlan Calculate(float currentGauge, float maxGauge, float dailyRecovery)
    {
        float daily = Mathf.Max(1f, dailyRecovery);
        float remainingSeverity = Mathf.Clamp(currentGauge, 0f, Mathf.Max(1f, maxGauge));
        int totalDays = Mathf.Max(1, RoundHalfUp(remainingSeverity / daily));

        // 첫 틱 보정: 마지막 (totalDays-1)일은 daily만큼 감소하고, 첫날이 나머지를 흡수한다.
        float firstTickRecovery = remainingSeverity - daily * (totalDays - 1);
        if (firstTickRecovery <= 0f)
            firstTickRecovery = daily;

        return new TreatmentPlan(totalDays, daily, firstTickRecovery);
    }

    private static int RoundHalfUp(float value)
    {
        //c#에는 은행가 반올림이 존재하기 때문에 AwayFromZero로 명시해둠
        return (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// <see cref="TreatmentDurationCalculator"/> 산출 결과. 치료 상태 진행에 필요한 값만 담는다.
/// </summary>
public readonly struct TreatmentPlan
{
    /// <summary>
    /// 치료 계획 값을 생성
    /// </summary>
    /// <param name="totalDays">완치까지 필요한 총 일수</param>
    /// <param name="dailyRecovery">보정된 일일 회복량</param>
    /// <param name="firstTickRecovery">첫날 적용할 회복량</param>
    public TreatmentPlan(int totalDays, float dailyRecovery, float firstTickRecovery)
    {
        TotalDays = totalDays;
        DailyRecovery = dailyRecovery;
        FirstTickRecovery = firstTickRecovery;
    }

    /// <summary>심각도 게이지가 0이 될 때까지 필요한 총 일수(최소 1).</summary>
    public int TotalDays { get; }

    /// <summary>최소 1로 보정된 일일 회복량. 상태 진행 시 재사용한다.</summary>
    public float DailyRecovery { get; }

    /// <summary>첫날 적용할 회복량(나머지 흡수분).</summary>
    public float FirstTickRecovery { get; }
}
