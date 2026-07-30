using UnityEngine;

/// <summary>Field와 Shelter가 공통으로 사용하는 부상 심각도 단계 판정 규칙입니다.</summary>
public static class CharacterInjuryStateRule
{
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
