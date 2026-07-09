using System.Collections.Generic;

public sealed class CostBundle
{
    private readonly List<CurrencyCost> m_costs = new();

    public IReadOnlyList<CurrencyCost> Costs => m_costs;

    public CostBundle(params CurrencyCost[] costs)
    {
        if (costs == null)
            return;

        foreach (CurrencyCost cost in costs)
        {
            if (cost.Amount > 0)
                m_costs.Add(cost);
        }
    }

    public bool IsFree => m_costs.Count == 0;
}
