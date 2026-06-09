using System.Collections.Generic;

public sealed class CostBundle
{
    private readonly List<CurrencyCost> _costs = new();

    public IReadOnlyList<CurrencyCost> Costs => _costs;

    public CostBundle(params CurrencyCost[] costs)
    {
        if (costs == null)
            return;

        foreach (CurrencyCost cost in costs)
        {
            if (cost.Amount > 0)
                _costs.Add(cost);
        }
    }

    public bool IsFree => _costs.Count == 0;
}
