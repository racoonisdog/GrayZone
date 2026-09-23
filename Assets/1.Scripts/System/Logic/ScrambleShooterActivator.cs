using UnityEngine;

/// <summary>
/// 전투 씬 진입 시 저장된 Scramble 시설 상태를 읽어 슈터 오브젝트를 활성화합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrambleShooterActivator : MonoBehaviour
{
    [SerializeField] private string m_facilityId = "scramble";
    [SerializeField] private GameObject m_shooter01;
    [SerializeField] private GameObject m_shooter02;

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        bool shooter01 = false;
        bool shooter02 = false;

        if (TryGetScrambleState(out FacilityRuntimeState state))
        {
            shooter01 = state.shooter01;
            shooter02 = state.shooter02;
        }
        else
        {
            Debug.LogWarning(
                $"[ScrambleShooterActivator] Facility state was not found: {m_facilityId}",
                this);
        }

        if (m_shooter01 != null)
            m_shooter01.SetActive(shooter01);
        if (m_shooter02 != null)
            m_shooter02.SetActive(shooter02);
    }

    private bool TryGetScrambleState(out FacilityRuntimeState state)
    {
        state = null;
        if (GameDataManager.Instance == null || string.IsNullOrWhiteSpace(m_facilityId))
            return false;

        ShelterRuntimeData shelterData = GameDataManager.Instance.CreateShelterRuntimeSnapshot();
        string facilityId = m_facilityId.Trim();
        for (int i = 0; i < shelterData.FacilityStates.Count; i++)
        {
            FacilityRuntimeState candidate = shelterData.FacilityStates[i];
            if (candidate != null && candidate.facilityId == facilityId)
            {
                state = candidate;
                return true;
            }
        }

        return false;
    }
}
