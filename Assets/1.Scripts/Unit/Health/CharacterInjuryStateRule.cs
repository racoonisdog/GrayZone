using UnityEngine;

/// <summary>Field와 Shelter가 공통으로 사용하는 부상 심각도 단계 판정 규칙입니다.</summary>
public static class CharacterInjuryStateRule
{
    /// <summary>부상 게이지를 심각도 단계로 환산합니다.</summary>
    /// <param name="gauge">현재 부상 게이지입니다.</param>
    /// <param name="maxGauge">최대 부상 게이지입니다.</param>
    /// <returns>게이지 비율에 해당하는 부상 단계입니다.</returns>
    /// <remarks>
    /// 경계는 비율 기준으로 10·40·70%입니다. 최대치가 0 이하면 비율을 낼 수 없으므로
    /// 가장 위험한 단계로 봅니다. 조용히 정상으로 처리하면 배선 실수를 못 알아챕니다.
    /// </remarks>
    public static CharacterInjuryState FromGauge(float gauge, float maxGauge)
    {
        if (maxGauge <= 0.0f)
            return CharacterInjuryState.Critical;

        float percent = Mathf.Clamp(gauge, 0.0f, maxGauge) / maxGauge * 100.0f;
        if (percent <= 10.0f)
            return CharacterInjuryState.Normal;
        if (percent <= 40.0f)
            return CharacterInjuryState.Minor;
        if (percent <= 70.0f)
            return CharacterInjuryState.Serious;
        return CharacterInjuryState.Critical;
    }
}
