using UnityEngine;

/// <summary>
/// 시설의 정적 식별자, 표시명, 기본 해금 상태, 해금 비용을 담는 정의 에셋
/// </summary>
[CreateAssetMenu(fileName = "FacilityDefinition", menuName = "GrayZone/Facility/FacilityDefinition")]
public class FacilityDefinition : ScriptableObject
{
    /// <summary>
    /// 시설 해금에 필요한 단일 재화 비용 항목
    /// </summary>
    [System.Serializable]
    public struct CostEntry
    {
        /// <summary>필요한 안정적인 자원 ID</summary>
        public string resourceId;

        /// <summary>필요한 재화 수량</summary>
        public int amount;
    }

    [SerializeField] private string m_facilityId;
    [SerializeField] private string m_facilityName;
    [SerializeField] private bool m_unlockedByDefault;
    [SerializeField] private CostEntry[] m_unlockCost;

    /// <summary>시설 런타임/저장 상태와 매칭되는 고정 ID</summary>
    public string FacilityId => m_facilityId;

    /// <summary>UI 표시용 시설 이름</summary>
    public string FacilityName => m_facilityName;

    /// <summary>새 저장 데이터에서 기본 해금 상태로 시작할지 여부</summary>
    public bool UnlockedByDefault => m_unlockedByDefault;

    /// <summary>
    /// 인스펙터 비용 배열을 런타임 비용 묶음으로 변환
    /// </summary>
    /// <returns>해금에 필요한 비용 묶음 비용이 없으면 무료 묶음</returns>
    public CostBundle BuildUnlockCost()
    {
        if (m_unlockCost == null || m_unlockCost.Length == 0)
            return new CostBundle();

        var costs = new ResourceCost[m_unlockCost.Length];
        for (int i = 0; i < m_unlockCost.Length; i++)
        {
            costs[i] = new ResourceCost(
                m_unlockCost[i].resourceId,
                m_unlockCost[i].amount);
        }

        return new CostBundle(costs);
    }
}
