using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VInspector;

/// <summary>
/// 방어전 회차별로 쓸 방어전 SO의 순서 표입니다. 첫 칸이 1회차입니다.
/// </summary>
/// <remarks>
/// 회차는 방어전 클리어 횟수 + 1입니다(<see cref="GameDataManager.NextDefenseRound"/>). 실패하면 클리어 횟수가 오르지 않아
/// 같은 회차를 다시 합니다. 회차가 표보다 많아지면 마지막 칸을 계속 씁니다. 시연 중에 방어전이 멈추지 않게 하기 위해서입니다.
/// 방어전 씬의 <see cref="DefenseSceneDataManager"/>가 입장할 때 이 표에서 방어전 SO를 골라 입장 데이터에 담습니다.
/// 설계 근거: privateDoc `DEFENSE_WAVE_SPAWN_SPEC_KR.md` §10.
/// </remarks>
[CreateAssetMenu(fileName = "DefenseSchedule_Name", menuName = "GrayZone/Defense/Defense Schedule")]
public sealed class DefenseScheduleSO : ScriptableObject
{
    [Header("Rounds")]
    [Tooltip("회차 순서대로 쓸 방어전 SO입니다. 첫 칸이 1회차입니다. 회차가 목록보다 많으면 마지막 칸을 계속 씁니다. 빈 칸은 두지 않습니다.")]
    [SerializeField] private List<DefenseStageSO> m_stages = new List<DefenseStageSO>();

    /// <summary>회차 순서대로 쓸 방어전 SO 목록입니다.</summary>
    public IReadOnlyList<DefenseStageSO> Stages => m_stages;

    /// <summary>회차별로 어떤 방어전 SO를 쓰는지 보여줍니다. 에디터 확인용입니다.</summary>
    [ShowInInspector]
    public string PreviewByRound
    {
        get
        {
            if (m_stages.Count == 0)
            {
                return "(비어 있음)";
            }

            var builder = new StringBuilder();
            for (int i = 0; i < m_stages.Count; i++)
            {
                builder.Append(i + 1).Append("회차: ").Append(m_stages[i] != null ? m_stages[i].name : "(빈 칸)");
                builder.Append('\n');
            }

            builder.Append(m_stages.Count + 1).Append("회차 이후: 마지막 칸 반복");
            return builder.ToString();
        }
    }

    /// <summary>
    /// 지정한 회차에 쓸 방어전 SO를 돌려줍니다.
    /// </summary>
    /// <param name="round">1부터 시작하는 회차입니다. 1보다 작으면 1회차로 봅니다.</param>
    /// <returns>쓸 방어전 SO입니다. 표가 비었거나 해당 칸이 비었으면 null입니다.</returns>
    /// <remarks>회차가 표보다 많으면 마지막 칸을 돌려줍니다.</remarks>
    public DefenseStageSO GetStage(int round)
    {
        if (m_stages.Count == 0)
        {
            return null;
        }

        int index = Mathf.Clamp(round - 1, 0, m_stages.Count - 1);
        return m_stages[index];
    }
}
