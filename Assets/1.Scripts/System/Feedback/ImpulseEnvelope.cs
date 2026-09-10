using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 발사처럼 순간적으로 들어오는 자극을 시간 기반 곡선으로 펼쳐 주는 런타임 상태입니다.
/// </summary>
/// <remarks>
/// <para>
/// 발 하나가 엔벨로프 인스턴스 하나가 되고, 살아 있는 인스턴스를 모두 더해 현재 값을 냅니다.
/// 연사 중 앞 발의 곡선이 끝나기 전에 다음 발이 들어오면 두 곡선이 겹쳐 자연히 누적됩니다.
/// </para>
/// <para>
/// 발마다 곡선을 처음부터 다시 시작하는 방식은 연사 시 반동이 쌓이지 않아 쓰지 않았고,
/// 현재 값을 시작점으로 새 곡선을 시작하는 방식은 시작점에 따라 모양이 달라져
/// "피크가 0.35 지점" 같은 지정의 의미가 흐려지므로 쓰지 않았습니다.
/// </para>
/// <para>
/// 이 타입은 직렬화 대상이 아닙니다. 켜기/끄기와 지속시간, 곡선은 각 컴포넌트가 소유하고
/// 매 프레임 <see cref="Evaluate"/>에 넘겨 줍니다. 밸런스 주입은 필드 이름으로 이루어지므로
/// 조절 대상 값을 이 클래스 안에 감싸 두면 <c>BindManager</c>가 찾지 못합니다.
/// </para>
/// </remarks>
public sealed class ImpulseEnvelope
{
    /// <summary>동시에 살아 있을 수 있는 엔벨로프 수의 상한입니다.</summary>
    /// <remarks>
    /// 연사가 빠르고 지속시간이 길면 인스턴스가 계속 늘어납니다. 상한에 닿으면 가장 오래된 것을 버립니다.
    /// 가장 오래된 것은 이미 곡선 뒤쪽에 있어 남은 기여가 가장 작으므로, 버릴 때 티가 가장 덜 납니다.
    /// </remarks>
    private const int MaxActiveCount = 32;

    /// <summary>진행 중인 엔벨로프 하나입니다.</summary>
    private struct Instance
    {
        /// <summary>시작 후 경과 시간(초)입니다.</summary>
        public float Elapsed;

        /// <summary>이 발의 크기입니다. 부호를 포함합니다.</summary>
        public float Magnitude;
    }

    private readonly List<Instance> m_active = new List<Instance>(MaxActiveCount);

    /// <summary>지금 살아 있는 엔벨로프 수입니다.</summary>
    public int ActiveCount => m_active.Count;

    /// <summary>새 자극을 추가합니다.</summary>
    /// <param name="magnitude">이번 발의 크기입니다. 부호를 그대로 넘깁니다.</param>
    public void Add(float magnitude)
    {
        if (Mathf.Approximately(magnitude, 0.0f))
        {
            return;
        }

        if (m_active.Count >= MaxActiveCount)
        {
            m_active.RemoveAt(0);
        }

        m_active.Add(new Instance { Elapsed = 0.0f, Magnitude = magnitude });
    }

    /// <summary>
    /// 살아 있는 모든 엔벨로프를 진행시키고 합을 돌려줍니다.
    /// </summary>
    /// <param name="duration">엔벨로프 하나의 전체 길이(초)입니다. 0 이하면 자극이 즉시 사라집니다.</param>
    /// <param name="curve">정규화 시간(0~1)을 세기 배율로 바꾸는 곡선입니다. 비어 있으면 삼각형 모양으로 대체합니다.</param>
    /// <param name="deltaTime">이번 프레임의 경과 시간(초)입니다.</param>
    /// <returns>모든 엔벨로프의 기여를 더한 값입니다.</returns>
    /// <remarks>
    /// 곡선의 y가 1을 넘으면 그만큼 목표를 넘어섰다가(오버슈트) 돌아옵니다.
    /// 피크 위치는 곡선에서 y가 가장 큰 x이며, 이 클래스가 따로 알 필요는 없습니다.
    /// </remarks>
    public float Evaluate(float duration, AnimationCurve curve, float deltaTime)
    {
        if (duration <= 0.0f)
        {
            m_active.Clear();
            return 0.0f;
        }

        float sum = 0.0f;

        for (int i = m_active.Count - 1; i >= 0; i--)
        {
            Instance instance = m_active[i];
            instance.Elapsed += deltaTime;

            if (instance.Elapsed >= duration)
            {
                m_active.RemoveAt(i);
                continue;
            }

            float normalized = instance.Elapsed / duration;
            sum += instance.Magnitude * SampleCurve(curve, normalized);
            m_active[i] = instance;
        }

        return sum;
    }

    /// <summary>진행 중인 모든 엔벨로프를 버립니다.</summary>
    /// <remarks>전투 자세 재진입처럼 잔상이 남으면 안 되는 시점에 사용합니다.</remarks>
    public void Clear()
    {
        m_active.Clear();
    }

    /// <summary>
    /// 곡선을 정규화 시간으로 평가합니다.
    /// </summary>
    /// <remarks>
    /// 곡선이 비어 있으면 절반 지점을 피크로 하는 삼각형으로 대체합니다.
    /// 이렇게 두면 곡선을 아직 그리지 않은 상태에서도 값이 0으로 죽지 않아,
    /// 배선이 잘못된 것과 곡선을 안 그린 것을 구분할 수 있습니다.
    /// </remarks>
    private static float SampleCurve(AnimationCurve curve, float normalized)
    {
        if (curve == null || curve.length == 0)
        {
            return normalized < 0.5f ? normalized * 2.0f : (1.0f - normalized) * 2.0f;
        }

        return curve.Evaluate(normalized);
    }

    /// <summary>
    /// 지속시간과 피크 위치, 세기, 완급으로 3키 엔벨로프 곡선을 만듭니다.
    /// </summary>
    /// <param name="peakRatio">피크가 오는 위치입니다. 0~1로 보정합니다.</param>
    /// <param name="peakValue">피크에서의 세기 배율입니다. 1보다 크면 오버슈트가 됩니다.</param>
    /// <param name="easePower">완급입니다. 1이면 선형, 클수록 늦게 붙고, 작을수록 빨리 붙습니다.</param>
    /// <returns>0에서 시작해 피크를 지나 0으로 돌아오는 곡선입니다.</returns>
    /// <remarks>
    /// 곡선 편집기가 없는 IMGUI 트레이너에서 숫자만으로 곡선을 만들 수 있게 하기 위한 생성기입니다.
    /// 인스펙터에서 손으로 그린 곡선을 덮어쓰므로, 부르는 쪽에서 명시적인 조작일 때만 호출해야 합니다.
    /// </remarks>
    public static AnimationCurve BuildCurve(float peakRatio, float peakValue, float easePower)
    {
        float peak = Mathf.Clamp(peakRatio, 0.001f, 0.999f);
        float power = Mathf.Max(0.01f, easePower);

        // 접선을 구간 기울기에 완급을 곱해 넣습니다. power가 크면 시작 기울기가 완만해져 늦게 붙습니다.
        float riseSlope = peakValue / peak;
        float fallSlope = -peakValue / (1.0f - peak);

        Keyframe start = new Keyframe(0.0f, 0.0f, 0.0f, riseSlope / power);
        Keyframe middle = new Keyframe(peak, peakValue, 0.0f, 0.0f);
        Keyframe end = new Keyframe(1.0f, 0.0f, fallSlope * power, 0.0f);

        return new AnimationCurve(start, middle, end);
    }

    /// <summary>
    /// 빠르게 가속해 피크에 도달한 뒤 일정한 속도로 복귀하는 엔벨로프 곡선을 만듭니다.
    /// </summary>
    /// <param name="peakRatio">피크가 오는 정규화 시간 위치입니다(0~1).</param>
    /// <param name="peakValue">피크에서의 세기 배율입니다.</param>
    /// <param name="attackEasePower">상승 구간의 이징입니다. 1이면 선형, 작을수록 초반에 더 빠르게 가속합니다.</param>
    /// <returns>빠른 상승과 선형 복귀를 갖는 3키 곡선입니다.</returns>
    /// <remarks>
    /// 피크의 outgoing tangent와 끝 키의 incoming tangent를 같은 하강 할선으로 둡니다.
    /// 두 키 사이가 정확히 직선이므로, 피크에서 멈췄다가 중간에 가속하는 복귀감이 생기지 않습니다.
    /// 피크의 incoming tangent는 0으로 유지해 상승 끝이 과하게 꺾이거나 미리 떨어지지 않게 합니다.
    /// </remarks>
    public static AnimationCurve BuildFastAttackConstantReleaseCurve(
        float peakRatio,
        float peakValue,
        float attackEasePower)
    {
        float peak = Mathf.Clamp(peakRatio, 0.001f, 0.999f);
        float attackPower = Mathf.Max(0.01f, attackEasePower);
        float attackSlope = peakValue / peak;
        float releaseSlope = -peakValue / (1.0f - peak);

        Keyframe start = new Keyframe(0.0f, 0.0f, 0.0f, attackSlope / attackPower);
        Keyframe middle = new Keyframe(peak, peakValue, 0.0f, releaseSlope);
        Keyframe end = new Keyframe(1.0f, 0.0f, releaseSlope, 0.0f);

        return new AnimationCurve(start, middle, end);
    }
}
