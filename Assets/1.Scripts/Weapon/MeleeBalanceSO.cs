using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 근접 무기 한 자루의 순수 밸런스 수치를 보관합니다.
/// </summary>
/// <remarks>
/// Unity 오브젝트 및 미디어 참조를 포함하지 않습니다. 이 타입의 직렬화 필드는 모두
/// 기획 테이블 입출력 대상이며, 런타임 상태는 <see cref="Melee"/>가 별도로 소유합니다.
///
/// 총기와 달리 탄약·탄퍼짐·반동이 없어 한참 단순합니다. 대신 <b>연타(콤보)</b>가 있어
/// 같은 무기라도 몇 타째인지에 따라 피해가 달라질 수 있습니다.
/// 몇 타째인지는 이 SO가 알지 못하며, 재생 중인 애니메이터 스테이트를 보고 <see cref="Melee"/>가 정합니다.
/// 그래서 각 타는 자기가 어느 스테이트에 대응하는지를 이름으로 들고 있습니다.
/// </remarks>
[CreateAssetMenu(fileName = "MeleeBalance", menuName = "GrayZone/Combat/Melee Balance")]
public sealed class MeleeBalanceSO : ScriptableObject, IBalanceTableData
{
    [Header("Identity")]
    [Tooltip("테이블과 런타임에서 근접 무기를 식별하는 고정 ID입니다. 에셋 이름과 별도로 유지합니다.")]
    [SerializeField] private string m_meleeId = "melee.claw.01";

    [Header("Damage")]
    [Tooltip("약점 판정을 사용할지 여부입니다. 변이체의 공격은 약점 없이 항상 정배수로 확정되어 있어 기본값은 꺼짐입니다.")]
    [SerializeField] private bool m_allowHeadshot = false;

    [Tooltip("약점에 적중했을 때 곱할 피해 배율입니다. 약점 판정을 쓰지 않으면 의미가 없습니다.")]
    [SerializeField] private float m_headshotDamageMultiplier = 2f;

    [Header("Attacks")]
    [Tooltip("이 무기가 가진 공격들입니다. 각 공격은 대응하는 애니메이터 스테이트와 자기 수치를 가집니다.")]
    [SerializeField] private List<MeleeComboStep> m_combo = new List<MeleeComboStep>();

    [Tooltip("공격 정보에서 현재 공격을 찾지 못했을 때 사용할 피해량입니다.")]
    [SerializeField] private int m_fallbackDamage = 1;

    [Tooltip("공격 정보에서 현재 공격을 찾지 못했을 때 사용할 경직력입니다.")]
    [SerializeField] private int m_fallbackStaggerPower = 1;

    /// <summary>근접 무기를 식별하는 고정 ID입니다.</summary>
    public string MeleeId => m_meleeId;

    /// <summary>약점 판정을 사용하는지 여부입니다.</summary>
    public bool AllowHeadshot => m_allowHeadshot;

    /// <summary>약점 적중 시 곱할 피해 배율입니다.</summary>
    public float HeadshotDamageMultiplier => Mathf.Max(0f, m_headshotDamageMultiplier);

    /// <summary>등록된 연타 수입니다.</summary>
    public int ComboCount => m_combo != null ? m_combo.Count : 0;

    /// <summary>공격 정보를 찾지 못했을 때 쓰는 기본 피해량입니다.</summary>
    public int FallbackDamage => Mathf.Max(0, m_fallbackDamage);

    /// <summary>공격 정보를 찾지 못했을 때 쓰는 기본 경직력입니다.</summary>
    public int FallbackStaggerPower => Mathf.Max(0, m_fallbackStaggerPower);

    /// <summary>
    /// 지정한 순번의 피해량을 돌려줍니다.
    /// </summary>
    /// <param name="index">0부터 세는 공격 순번입니다.</param>
    /// <returns>해당 순번의 피해량이며, 범위를 벗어나면 <see cref="FallbackDamage"/>입니다.</returns>
    public int GetDamage(int index)
    {
        if (m_combo == null || index < 0 || index >= m_combo.Count)
        {
            return FallbackDamage;
        }

        return Mathf.Max(0, m_combo[index].Damage);
    }

    /// <summary>
    /// 지정한 순번의 경직력을 돌려줍니다.
    /// </summary>
    /// <param name="index">0부터 세는 공격 순번입니다.</param>
    /// <returns>해당 순번의 경직력이며, 범위를 벗어나면 <see cref="FallbackStaggerPower"/>입니다.</returns>
    /// <remarks>
    /// 경직력은 피격자의 경직 누적치에 더해지는 값이며 피해량과 별개입니다.
    /// 누적치가 피격자의 경직 한계치에 닿으면 경직이 발생합니다.
    /// 한계치와 누적치는 맞는 쪽이 소유하므로 이 무기는 더할 값만 압니다.
    /// </remarks>
    public int GetStaggerPower(int index)
    {
        if (m_combo == null || index < 0 || index >= m_combo.Count)
        {
            return FallbackStaggerPower;
        }

        return Mathf.Max(0, m_combo[index].StaggerPower);
    }

    /// <summary>
    /// 애니메이터 스테이트 이름 해시로 연타 순번을 찾습니다.
    /// </summary>
    /// <param name="stateNameHash">현재 재생 중인 스테이트의 짧은 이름 해시입니다.</param>
    /// <returns>찾은 순번이며, 대응하는 타가 없으면 -1입니다.</returns>
    /// <remarks>
    /// 이름 대신 해시로 비교하는 것은 매 판정마다 문자열을 만들지 않기 위해서입니다.
    /// 해시는 처음 조회할 때 한 번 계산해 각 타가 들고 있습니다.
    /// </remarks>
    public int FindIndexByStateHash(int stateNameHash)
    {
        if (m_combo == null)
        {
            return -1;
        }

        for (int i = 0; i < m_combo.Count; i++)
        {
            if (m_combo[i].MatchesState(stateNameHash))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// 근접 공격 하나의 수치입니다.
/// </summary>
/// <remarks>
/// 애니메이터 스테이트 이름을 함께 들고 있는 이유는, 지금 어느 공격인지를 애니메이터가 정하기 때문입니다.
/// 코드가 순번을 세지 않고 재생 중인 스테이트를 보고 판단하므로 그 연결 고리가 데이터에 있어야 합니다.
/// </remarks>
[Serializable]
public struct MeleeComboStep
{
    [Tooltip("이 공격에 대응하는 애니메이터 스테이트 이름입니다. 예: Attack_B1")]
    [SerializeField] private string m_stateName;

    [Tooltip("이 공격이 적중했을 때 적용할 피해량입니다.")]
    [SerializeField] private int m_damage;

    [Tooltip("이 공격이 적중했을 때 피격자의 경직 누적치에 더할 값입니다. 피해량과 별개입니다.")]
    [SerializeField] private int m_staggerPower;

    /// <summary>이 공격에 대응하는 애니메이터 스테이트 이름입니다.</summary>
    public string StateName => m_stateName;

    /// <summary>이 공격의 피해량입니다.</summary>
    public int Damage => m_damage;

    /// <summary>이 공격이 피격자의 경직 누적치에 더할 값입니다.</summary>
    public int StaggerPower => m_staggerPower;

    /// <summary>지정한 스테이트 이름 해시가 이 공격에 해당하는지 확인합니다.</summary>
    /// <param name="stateNameHash">비교할 스테이트의 짧은 이름 해시입니다.</param>
    /// <returns>이 공격의 스테이트와 같으면 true입니다.</returns>
    public bool MatchesState(int stateNameHash)
    {
        if (string.IsNullOrEmpty(m_stateName))
        {
            return false;
        }

        return Animator.StringToHash(m_stateName) == stateNameHash;
    }
}
