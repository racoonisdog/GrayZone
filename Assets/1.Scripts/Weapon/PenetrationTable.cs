using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 관통 횟수 구간 하나와 그 구간에 적용할 값입니다.
/// </summary>
/// <remarks>
/// <see cref="MaxPenetrationCount"/>는 <b>이 구간이 끝나는 관통 횟수</b>입니다.
/// 앞 구간의 끝부터 이 횟수까지가 이 구간이며, 첫 구간은 0(관통하지 않은 첫 대상)부터 시작합니다.
///
/// 값 보관 방식은 <see cref="DamageFalloffStep"/>과 같습니다. 배율과 고정 피해를 따로 두어
/// 모드를 바꿔도 값이 엉뚱하게 읽히지 않게 합니다.
/// </remarks>
[Serializable]
public struct PenetrationStep
{
    [Tooltip("이 구간이 끝나는 관통 횟수입니다. 0은 관통하지 않은 첫 대상입니다.")]
    [SerializeField] private int m_maxPenetrationCount;

    [Tooltip("배율 모드에서 쓰는 값입니다. 1이면 손실 없음, 0.5면 절반입니다.")]
    [SerializeField] private float m_damageMultiplier;

    [Tooltip("고정 피해 모드에서 쓰는 값입니다. 기본 피해와 무관하게 이 값이 그대로 들어갑니다.")]
    [SerializeField] private int m_flatDamage;

    /// <summary>구간 하나의 끝 관통 횟수와 두 모드의 값을 함께 지정합니다.</summary>
    /// <param name="maxPenetrationCount">이 구간이 끝나는 관통 횟수입니다.</param>
    /// <param name="damageMultiplier">배율 모드에서 사용할 값입니다.</param>
    /// <param name="flatDamage">고정 피해 모드에서 사용할 값입니다.</param>
    /// <remarks>모드는 표가 소유하므로 구간은 두 값을 모두 들고 있다가 해당하는 쪽만 쓰입니다.</remarks>
    public PenetrationStep(int maxPenetrationCount, float damageMultiplier, int flatDamage)
    {
        m_maxPenetrationCount = maxPenetrationCount;
        m_damageMultiplier = damageMultiplier;
        m_flatDamage = flatDamage;
    }

    /// <summary>이 구간이 끝나는 관통 횟수입니다.</summary>
    public int MaxPenetrationCount => Mathf.Max(0, m_maxPenetrationCount);

    /// <summary>배율 모드에서 쓰는 피해 배율입니다.</summary>
    public float DamageMultiplier => Mathf.Clamp01(m_damageMultiplier);

    /// <summary>고정 피해 모드에서 쓰는 피해량입니다.</summary>
    public int FlatDamage => Mathf.Max(0, m_flatDamage);
}

/// <summary>
/// 한 발이 유닛을 몇 번 꿰뚫는지와, 꿰뚫을 때마다 피해를 얼마로 줄일지 정하는 구간 표입니다.
/// </summary>
/// <remarks>
/// <para>
/// 벽과 지형은 관통하지 않습니다. 이 표는 <b>유닛만</b> 다룹니다. 지형 관통을 넣으려면 두께나 재질 등급이
/// 필요해 레벨 아트 쪽 약속이 생기므로 범위에서 뺐습니다.
/// </para>
/// <para>
/// 기본값은 관통 없음(<see cref="MaxPenetrations"/> 0)입니다. 설정하지 않은 무기는 지금까지와 똑같이
/// 첫 대상에서 멈춥니다.
/// </para>
/// <para>
/// 구조와 조회 규칙은 <see cref="DamageFalloffTable"/>과 같게 맞췄습니다. 두 표가 다르게 동작하면
/// 하나를 이해해도 다른 하나를 다시 확인해야 합니다.
/// </para>
/// </remarks>
[Serializable]
public sealed class PenetrationTable
{
    [Tooltip("관통 횟수를 제한하지 않습니다. 켜면 아래 최대 관통 횟수를 무시하고 지형에 막힐 때까지 꿰뚫습니다.")]
    [SerializeField] private bool m_unlimited = false;

    [Tooltip("첫 대상 이후 추가로 꿰뚫을 수 있는 유닛 수입니다. 0이면 관통하지 않습니다.")]
    [SerializeField] private int m_maxPenetrations = 0;

    [Tooltip("구간 값을 배율로 적을지 고정 피해로 적을지 정합니다.")]
    [SerializeField] private DamageFalloffMode m_mode = DamageFalloffMode.Multiplier;

    [Tooltip("관통 횟수 구간 목록입니다. 횟수 순으로 정렬되며, 마지막 구간을 넘어선 횟수는 마지막 값을 그대로 씁니다.")]
    [SerializeField] private List<PenetrationStep> m_steps = new List<PenetrationStep>();

    /// <summary>관통 횟수를 제한하지 않는지 여부입니다.</summary>
    public bool Unlimited => m_unlimited;

    /// <summary>첫 대상 이후 추가로 꿰뚫을 수 있는 유닛 수입니다.</summary>
    public int MaxPenetrations => Mathf.Max(0, m_maxPenetrations);

    /// <summary>구간 값을 해석하는 방식입니다.</summary>
    public DamageFalloffMode Mode => m_mode;

    /// <summary>구간 목록입니다. 관통 횟수 오름차순으로 유지합니다.</summary>
    public IReadOnlyList<PenetrationStep> Steps => m_steps;

    /// <summary>관통을 조금이라도 허용하는지 여부입니다.</summary>
    public bool AllowsPenetration => m_unlimited || MaxPenetrations > 0;

    /// <summary>구간이 하나도 없는지 여부입니다. 비어 있으면 피해 감쇠가 적용되지 않습니다.</summary>
    public bool IsEmpty => m_steps == null || m_steps.Count == 0;

    /// <summary>
    /// 이미 꿰뚫은 수를 기준으로 한 번 더 꿰뚫을 수 있는지 확인합니다.
    /// </summary>
    /// <param name="penetrationsUsed">지금까지 꿰뚫은 유닛 수입니다.</param>
    /// <returns>한 번 더 꿰뚫을 수 있으면 true입니다.</returns>
    public bool CanPenetrate(int penetrationsUsed)
    {
        return m_unlimited || penetrationsUsed < MaxPenetrations;
    }

    /// <summary>
    /// 몇 번째 대상인지에 따라 적용할 피해를 돌려줍니다.
    /// </summary>
    /// <param name="penetrationIndex">0이면 첫 대상, 1이면 한 번 꿰뚫은 뒤의 대상입니다.</param>
    /// <param name="baseDamage">거리 감쇠까지 적용된 피해입니다. 배율 모드에서만 쓰입니다.</param>
    /// <returns>이 대상에 넣을 피해입니다. 구간이 없으면 <paramref name="baseDamage"/> 그대로입니다.</returns>
    /// <remarks>
    /// 구간에 걸렸다면 최소 1을 보장합니다. 맞았는데 0이 들어가면 피격 표시만 뜨고 아무 일도 일어나지 않아
    /// 버그로 보이기 때문이며, <see cref="DamageFalloffTable.ResolveDamage"/>와 같은 규약입니다.
    ///
    /// 마지막 구간보다 많이 꿰뚫은 경우는 마지막 구간의 값을 씁니다. 무제한 관통을 켰을 때
    /// 표를 무한히 늘리지 않아도 되도록 한 규칙입니다.
    /// </remarks>
    public int ResolveDamage(int penetrationIndex, int baseDamage)
    {
        if (IsEmpty)
        {
            return baseDamage;
        }

        int index = m_steps.Count - 1;
        for (int i = 0; i < m_steps.Count; i++)
        {
            if (penetrationIndex <= m_steps[i].MaxPenetrationCount)
            {
                index = i;
                break;
            }
        }

        return ResolveStepDamage(m_steps[index], baseDamage);
    }

    /// <summary>구간 하나가 내는 피해를 계산합니다.</summary>
    public int ResolveStepDamage(PenetrationStep step, int baseDamage)
    {
        int damage = m_mode == DamageFalloffMode.FlatDamage
            ? step.FlatDamage
            : Mathf.RoundToInt(baseDamage * step.DamageMultiplier);

        return Mathf.Max(1, damage);
    }

    /// <summary>구간을 관통 횟수 오름차순으로 정렬합니다.</summary>
    public void Sort()
    {
        m_steps?.Sort((a, b) => a.MaxPenetrationCount.CompareTo(b.MaxPenetrationCount));
    }

    /// <summary>
    /// 내용이 같은 새 표를 만듭니다.
    /// </summary>
    /// <remarks>
    /// 밸런스 주입은 참조를 그대로 대입하므로, 복제하지 않으면 무기와 밸런스 SO가 같은 목록을 공유합니다.
    /// 그 상태에서 런타임에 표를 고치면 프로젝트 자산인 SO가 함께 바뀝니다.
    /// </remarks>
    public PenetrationTable Clone()
    {
        PenetrationTable copy = new PenetrationTable
        {
            m_unlimited = m_unlimited,
            m_maxPenetrations = m_maxPenetrations,
            m_mode = m_mode,
        };

        if (m_steps != null)
        {
            copy.m_steps = new List<PenetrationStep>(m_steps);
        }

        copy.Sort();
        return copy;
    }
}
