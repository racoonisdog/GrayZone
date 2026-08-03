using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 거리 감쇠 구간의 값을 어떤 방식으로 적을지 정합니다.
/// </summary>
public enum DamageFalloffMode
{
    /// <summary>기본 피해에 곱할 배율로 적습니다. 기본 피해를 바꾸면 전 구간이 함께 따라옵니다.</summary>
    Multiplier = 0,

    /// <summary>구간의 피해를 직접 적습니다. 몇 발에 죽는지 바로 읽히지만 기본 피해와 연동되지 않습니다.</summary>
    FlatDamage = 1,
}

/// <summary>
/// 거리 구간 하나와 그 구간에 적용할 값입니다.
/// </summary>
/// <remarks>
/// <see cref="MaxDistance"/>는 <b>이 구간이 끝나는 거리</b>입니다.
/// 앞 구간의 끝부터 이 거리까지가 이 구간의 범위이며, 첫 구간은 0부터 시작합니다.
///
/// 배율과 고정 피해를 <b>따로 보관합니다.</b> 한 필드를 모드에 따라 다르게 해석하면
/// 모드를 바꿀 때 값이 엉뚱하게 읽히고(배율 0.7 → 피해 0), 되돌려도 원래 값이 남지 않습니다.
/// </remarks>
[Serializable]
public struct DamageFalloffStep
{
    [Tooltip("이 구간이 끝나는 거리(m)입니다. 앞 구간의 끝부터 여기까지가 이 구간입니다.")]
    [SerializeField] private float m_maxDistance;

    [Tooltip("배율 모드에서 쓰는 값입니다. 1이면 손실 없음, 0.5면 절반입니다.")]
    [SerializeField] private float m_damageMultiplier;

    [Tooltip("고정 피해 모드에서 쓰는 값입니다. 기본 피해와 무관하게 이 값이 그대로 들어갑니다.")]
    [SerializeField] private int m_flatDamage;

    public DamageFalloffStep(float maxDistance, float damageMultiplier, int flatDamage)
    {
        m_maxDistance = maxDistance;
        m_damageMultiplier = damageMultiplier;
        m_flatDamage = flatDamage;
    }

    /// <summary>이 구간이 끝나는 거리(m)입니다.</summary>
    public float MaxDistance => Mathf.Max(0.0f, m_maxDistance);

    /// <summary>배율 모드에서 쓰는 피해 배율입니다.</summary>
    public float DamageMultiplier => Mathf.Clamp01(m_damageMultiplier);

    /// <summary>고정 피해 모드에서 쓰는 피해량입니다.</summary>
    public int FlatDamage => Mathf.Max(0, m_flatDamage);
}

/// <summary>
/// 거리에 따라 피해를 계단식으로 돌려주는 구간 표입니다.
/// </summary>
/// <remarks>
/// <para>
/// 구간 사이를 보간하지 않습니다. 어떤 거리든 그 거리가 속한 구간의 값을 그대로 씁니다.
/// 보간하지 않는 쪽을 고른 이유는 "이 거리 구간에서는 이 피해"가 명확해 검증하기 쉽기 때문입니다.
/// </para>
/// <para>
/// 구간이 하나도 없으면 기본 피해를 그대로 돌려줍니다. 감쇠를 설정하지 않은 무기가 그대로 동작해야 하기 때문입니다.
/// </para>
/// <para>
/// 조회는 거리 오름차순을 전제합니다. 편집기에서 마커를 놓을 때와 <see cref="Clone"/> 시점에 정렬하며,
/// 사격마다 정렬하지 않습니다. 사격 경로에 정렬 비용을 얹을 이유가 없습니다.
/// </para>
/// </remarks>
[Serializable]
public sealed class DamageFalloffTable
{
    [Tooltip("구간 값을 배율로 적을지 고정 피해로 적을지 정합니다.")]
    [SerializeField] private DamageFalloffMode m_mode = DamageFalloffMode.Multiplier;

    [Tooltip("트랙 왼쪽 끝에 해당하는 거리(m)입니다. 보이는 범위만 정하며 판정에는 영향이 없습니다.")]
    [SerializeField] private float m_trackMinDistance = 0.0f;

    [Tooltip("트랙 오른쪽 끝에 해당하는 거리(m)입니다. 0 이하로 두면 무기 사거리를 그대로 씁니다.")]
    [SerializeField] private float m_trackMaxDistance = 0.0f;

    [SerializeField] private List<DamageFalloffStep> m_steps = new List<DamageFalloffStep>();

    /// <summary>구간 값을 해석하는 방식입니다.</summary>
    public DamageFalloffMode Mode => m_mode;

    /// <summary>트랙 왼쪽 끝 거리(m)입니다.</summary>
    public float TrackMinDistance => Mathf.Max(0.0f, m_trackMinDistance);

    /// <summary>트랙 오른쪽 끝 거리(m)입니다. 0 이하면 무기 사거리를 쓰겠다는 뜻입니다.</summary>
    public float TrackMaxDistance => m_trackMaxDistance;

    /// <summary>
    /// 트랙에 실제로 쓸 오른쪽 끝 거리를 정합니다.
    /// </summary>
    /// <param name="weaponRange">무기 사거리(m)입니다.</param>
    /// <returns>지정값이 있으면 그 값, 없으면 무기 사거리입니다.</returns>
    /// <remarks>
    /// 기본을 무기 사거리로 두는 이유는 지정값과 사거리가 어긋나면 트랙에 보이는 위치가
    /// 실제 사거리와 달라져 판단을 그르치기 때문입니다. 좁은 구간을 확대해 보고 싶을 때만 지정합니다.
    /// </remarks>
    public float ResolveTrackMaxDistance(float weaponRange)
    {
        if (m_trackMaxDistance > 0.0f)
        {
            return Mathf.Max(TrackMinDistance + 1.0f, m_trackMaxDistance);
        }

        return weaponRange > 0.0f ? weaponRange : 100.0f;
    }

    /// <summary>구간 목록입니다. 거리 오름차순으로 유지합니다.</summary>
    public IReadOnlyList<DamageFalloffStep> Steps => m_steps;

    /// <summary>구간이 하나도 없는지 여부입니다. 비어 있으면 감쇠가 적용되지 않습니다.</summary>
    public bool IsEmpty => m_steps == null || m_steps.Count == 0;

    /// <summary>
    /// 지정한 거리에 적용할 최종 피해를 돌려줍니다.
    /// </summary>
    /// <param name="distance">사격 지점에서 명중 지점까지의 거리(m)입니다.</param>
    /// <param name="baseDamage">무기의 기본 피해입니다. 배율 모드에서만 쓰입니다.</param>
    /// <returns>해당 구간의 피해입니다. 구간이 없으면 <paramref name="baseDamage"/> 그대로입니다.</returns>
    /// <remarks>
    /// 구간에 걸렸다면 최소 1을 보장합니다. <see cref="CombatDamage.ResolveHit"/>와 같은 규약이며,
    /// 맞았는데 0이 들어가면 피격 표시만 뜨고 아무 일도 일어나지 않아 버그로 보이기 때문입니다.
    ///
    /// 마지막 구간보다 먼 거리는 마지막 구간의 값을 씁니다.
    /// 사거리 밖은 애초에 명중 판정이 성립하지 않으므로 그 바깥을 따로 규정하지 않습니다.
    /// </remarks>
    public int ResolveDamage(float distance, int baseDamage)
    {
        if (IsEmpty)
        {
            return baseDamage;
        }

        int index = m_steps.Count - 1;
        for (int i = 0; i < m_steps.Count; i++)
        {
            if (distance <= m_steps[i].MaxDistance)
            {
                index = i;
                break;
            }
        }

        return ResolveStepDamage(m_steps[index], baseDamage);
    }

    /// <summary>구간 하나가 내는 피해를 계산합니다.</summary>
    /// <remarks>드로어가 인스펙터에 결과를 미리 보여줄 때도 같은 계산을 써야 값이 어긋나지 않습니다.</remarks>
    public int ResolveStepDamage(DamageFalloffStep step, int baseDamage)
    {
        int damage = m_mode == DamageFalloffMode.FlatDamage
            ? step.FlatDamage
            : Mathf.RoundToInt(baseDamage * step.DamageMultiplier);

        return Mathf.Max(1, damage);
    }

    /// <summary>구간을 거리 오름차순으로 정렬합니다.</summary>
    public void Sort()
    {
        m_steps?.Sort((a, b) => a.MaxDistance.CompareTo(b.MaxDistance));
    }

    /// <summary>
    /// 내용이 같은 새 표를 만듭니다.
    /// </summary>
    /// <remarks>
    /// 밸런스 주입은 참조를 그대로 대입하므로, 복제하지 않으면 무기와 밸런스 SO가 같은 목록을 공유합니다.
    /// 그 상태에서 런타임에 표를 고치면 프로젝트 자산인 SO가 함께 바뀝니다.
    /// </remarks>
    public DamageFalloffTable Clone()
    {
        DamageFalloffTable copy = new DamageFalloffTable
        {
            m_mode = m_mode,
            m_trackMinDistance = m_trackMinDistance,
            m_trackMaxDistance = m_trackMaxDistance,
        };

        if (m_steps != null)
        {
            copy.m_steps = new List<DamageFalloffStep>(m_steps);
        }

        copy.Sort();
        return copy;
    }
}
