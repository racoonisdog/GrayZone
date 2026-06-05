using UnityEngine;

[CreateAssetMenu(fileName = "FacilityDefinition", menuName = "GrayZone/Facility/FacilityDefinition")]
public class FacilityDefinition : ScriptableObject
{
    [System.Serializable]
    public struct CostEntry
    {
        public CurrencyType type;
        public int amount;
    }

    [SerializeField] private string m_facilityId;
    [SerializeField] private string m_facilityName;
    [SerializeField] private int m_maxCapacity;
    [SerializeField] private CostEntry[] m_unlockCost;

    public string FacilityId => m_facilityId;
    public string FacilityName => m_facilityName;
    public int MaxCapacity => m_maxCapacity;

    public CostBundle BuildUnlockCost()
    {
        if (m_unlockCost == null || m_unlockCost.Length == 0)
            return new CostBundle();

        var costs = new CurrencyCost[m_unlockCost.Length];
        for (int i = 0; i < m_unlockCost.Length; i++)
            costs[i] = new CurrencyCost(m_unlockCost[i].type, m_unlockCost[i].amount);

        return new CostBundle(costs);
    }
}
