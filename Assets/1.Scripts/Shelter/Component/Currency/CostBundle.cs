using System.Collections.Generic;

/// <summary>
/// 여러 <see cref="ResourceCost"/>를 하나로 묶은 시설 비용 묶음
/// </summary>
public sealed class CostBundle
{
    private readonly List<ResourceCost> m_costs = new();

    /// <summary>유효 수량을 가진 비용 항목 목록</summary>
    public IReadOnlyList<ResourceCost> Costs => m_costs;

    /// <summary>
    /// 0 이하 수량을 제외하고 비용 묶음을 생성
    /// </summary>
    /// <param name="costs">묶음에 포함할 재화 비용 항목</param>
    public CostBundle(params ResourceCost[] costs)
    {
        if (costs == null)
            return;

        foreach (ResourceCost cost in costs)
        {
            if (cost.IsValid)
                m_costs.Add(cost);
        }
    }

    /// <summary>차감할 비용이 없는 무료 상태인지 반환</summary>
    public bool IsFree => m_costs.Count == 0;
}
