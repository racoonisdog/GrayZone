using UnityEngine;

/// <summary>
/// 소음이 어떤 행동에서 났는지 나타내는 분류입니다.
/// </summary>
/// <remarks>
/// 공용 `적 시스템` v0.2 §5.4.4가 소음 이벤트의 구성 정보로 규정한 "소음 행동 유형"입니다.
/// 다만 같은 절의 우선순위 규칙은 감지 강도와 발생 시점만 사용하므로 지금은 이 값을 판단에 쓰지 않습니다.
/// 유형별로 다르게 반응하는 규칙이 문서에 생기면 그때 소비합니다. 자리를 미리 두는 이유는
/// 나중에 추가하면 이미 발신하는 모든 곳을 고쳐야 하기 때문입니다.
/// </remarks>
public enum NoiseBehaviorType
{
    /// <summary>사격 소음입니다.</summary>
    Gunshot,

    /// <summary>발소리 등 이동 소음입니다.</summary>
    Movement,

    /// <summary>구조 같은 상호작용 소음입니다.</summary>
    Interaction,
}

/// <summary>
/// 한 번 발생한 소음의 내용입니다.
/// </summary>
/// <remarks>
/// 공용 `적 시스템` v0.2 §5.4.4의 소음 이벤트 정보를 옮긴 구조입니다.
///
/// <b>도달 거리가 곧 들리는 거리입니다.</b> 도달 거리 안이면 무조건 들리고, 밖이면 들리지 않습니다.
/// 감쇠는 "들리는지"를 정하지 않고 <b>어느 소음이 우선인지</b>만 정합니다(§5.4.4의 우선순위 규칙).
/// 두 책임을 감쇠 하나가 겸하면 도달 거리와 감지 기준이 곱해져 제3의 값이 실제 가청 거리가 되고,
/// 인스펙터의 숫자가 의미하는 바가 숨습니다. 실제로 그렇게 어긋나 발소리가 시야보다 짧게 들렸습니다.
///
/// <b>어떤 캐릭터가 냈는지를 담지 않습니다.</b> 소음은 발생 위치만 전달하며 캐릭터를 유효 대상으로
/// 만들지 않기 때문입니다. 캐릭터 참조를 넣어 두면 수신 쪽에서 그것을 대상으로 써 버리기 쉬워집니다.
///
/// <see cref="EmittedByAi"/>만 예외로 담습니다. 이 값은 대상 선정용이 아니라 비전투 감지 보호(§5.6)에서
/// "AI 조작 캐릭터가 낸 소음인지"를 가리는 데만 씁니다. 그 판단은 수신자별 교전 상태에 따라 달라지므로
/// 발신 쪽에서 미리 걸러 낼 수 없습니다.
/// </remarks>
public readonly struct NoiseEvent
{
    /// <summary>소음원 식별값입니다. 같은 값이면 같은 소음원의 반복 이벤트로 취급합니다.</summary>
    public readonly int SourceId;

    /// <summary>소음이 발생한 위치입니다.</summary>
    public readonly Vector3 Position;

    /// <summary>거리 감쇠 이전의 기본 소음량입니다. 우선순위 비교의 기준이 됩니다.</summary>
    public readonly float BaseLevel;

    /// <summary>이 소음이 들리는 거리입니다. 이 거리 안이면 들리고 밖이면 들리지 않습니다.</summary>
    public readonly float Range;

    /// <summary>도달 거리 끝에서 남는 강도의 비율입니다. 0이면 끝에서 0까지 떨어집니다.</summary>
    public readonly float FloorRatio;

    /// <summary>감쇠 곡선의 지수입니다. 1이면 선형입니다.</summary>
    public readonly float CurveExponent;

    /// <summary>발생 시점입니다.</summary>
    public readonly float Time;

    /// <summary>소음을 낸 행동의 분류입니다.</summary>
    public readonly NoiseBehaviorType BehaviorType;

    /// <summary>AI가 조작하는 스쿼드 캐릭터가 낸 소음인지 여부입니다.</summary>
    public readonly bool EmittedByAi;

    /// <summary>소음 이벤트를 만듭니다.</summary>
    /// <param name="sourceId">소음원 식별값입니다.</param>
    /// <param name="position">발생 위치입니다.</param>
    /// <param name="baseLevel">거리 감쇠 이전의 기본 소음량입니다.</param>
    /// <param name="range">이 소음이 들리는 거리입니다.</param>
    /// <param name="floorRatio">도달 거리 끝에서 남는 강도의 비율입니다.</param>
    /// <param name="curveExponent">감쇠 곡선의 지수입니다. 1이면 선형입니다.</param>
    /// <param name="time">발생 시점입니다.</param>
    /// <param name="behaviorType">소음을 낸 행동의 분류입니다.</param>
    /// <param name="emittedByAi">AI 조작 캐릭터가 낸 소음이면 true입니다.</param>
    public NoiseEvent(
        int sourceId,
        Vector3 position,
        float baseLevel,
        float range,
        float floorRatio,
        float curveExponent,
        float time,
        NoiseBehaviorType behaviorType,
        bool emittedByAi)
    {
        SourceId = sourceId;
        Position = position;
        BaseLevel = Mathf.Max(0f, baseLevel);
        Range = Mathf.Max(0f, range);
        FloorRatio = Mathf.Clamp01(floorRatio);
        CurveExponent = Mathf.Max(0.01f, curveExponent);
        Time = time;
        BehaviorType = behaviorType;
        EmittedByAi = emittedByAi;
    }

    /// <summary>
    /// 이 소음이 듣는 쪽에 들리는지 판정합니다.
    /// </summary>
    /// <param name="listenerPosition">소음을 듣는 위치입니다.</param>
    /// <param name="hearingMultiplier">듣는 쪽의 청각 배수입니다. 1이면 도달 거리 그대로입니다.</param>
    /// <returns>들리면 true입니다.</returns>
    /// <remarks>
    /// 벽이나 엄폐물에 의한 장애물 감쇠는 적용하지 않습니다(§5.4.4). 소음이 벽을 통과하는 것이 아니라,
    /// 차폐를 계산하지 않기로 문서가 정한 것입니다.
    ///
    /// 발신자가 "얼마나 멀리"를, 수신자가 "그 거리를 몇 배로 듣는가"를 소유합니다.
    /// 총기별 소음과 개체별 청각을 서로 건드리지 않고 조정할 수 있고, 둘 다 곱셈 한 번이라
    /// 실제 가청 거리를 머리에서 계산할 수 있습니다.
    /// </remarks>
    public bool IsAudibleAt(Vector3 listenerPosition, float hearingMultiplier)
    {
        if (Range <= 0f || BaseLevel <= 0f)
        {
            return false;
        }

        float audibleRange = Range * Mathf.Max(0f, hearingMultiplier);
        return Vector3.Distance(Position, listenerPosition) <= audibleRange;
    }

    /// <summary>
    /// 듣는 쪽 위치에서의 감지 강도를 계산합니다.
    /// </summary>
    /// <param name="listenerPosition">소음을 듣는 위치입니다.</param>
    /// <returns>거리 감쇠를 적용한 감지 강도입니다.</returns>
    /// <remarks>
    /// <b>이 값은 가청 여부를 정하지 않습니다.</b> 서로 다른 소음원 중 어느 것을 추적할지 비교하는 데만 씁니다(§5.4.4).
    ///
    /// 도달 거리 끝에서 <see cref="FloorRatio"/>만큼 남깁니다. 최저치가 없으면 끝에서 강도가 0에 수렴해
    /// 두 소음이 사실상 동점이 되고 우선순위가 무작위에 가까워집니다.
    ///
    /// 최저치를 절대값이 아니라 <b>소음량에 대한 비율</b>로 두는 것이 중요합니다. 절대값이면 총성과 발소리의
    /// 바닥이 같아져서, 멀리서 난 총성이 바로 옆 발소리에 밀립니다.
    ///
    /// 청각 배수를 여기 쓰지 않습니다. 소리의 크기 분포는 소리 자신의 성질이고, 얼마나 멀리까지 들리는지는
    /// 듣는 쪽의 성질입니다. 그래서 예민한 개체가 도달 거리 밖에서 들었다면 최저 강도로 받습니다.
    /// </remarks>
    public float GetIntensityAt(Vector3 listenerPosition)
    {
        if (Range <= 0f || BaseLevel <= 0f)
        {
            return 0f;
        }

        float normalized = Mathf.Clamp01(Vector3.Distance(Position, listenerPosition) / Range);
        float shaped = Mathf.Pow(normalized, CurveExponent);

        return BaseLevel * Mathf.Lerp(1f, FloorRatio, shaped);
    }
}
