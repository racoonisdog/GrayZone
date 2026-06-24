using UnityEngine;
using System.Collections.Generic;

public class FacilityManager : MonoBehaviour
{
    [SerializeField] private List<FacilityDefinition> m_definitions = new();

    private readonly Dictionary<string, FacilityState> m_states = new();

    public IReadOnlyDictionary<string, FacilityState> States => m_states;

    private void Awake()
    {
        m_states.Clear();

        foreach (FacilityDefinition definition in m_definitions)
        {
            if (definition == null)
                continue;

            if (string.IsNullOrWhiteSpace(definition.FacilityId))
            {
                Debug.LogWarning("[FacilityManager] FacilityDefinition has empty FacilityId.", definition);
                continue;
            }

            FacilityRuntimeState runtimeState = GetOrCreateRuntimeState(definition);
            if (runtimeState == null)
                continue;

            m_states[definition.FacilityId] = new FacilityState(definition, runtimeState);
        }
    }

    public FacilityState GetState(string facilityId)
        => m_states.TryGetValue(facilityId, out FacilityState state) ? state : null;

    public bool TryUnlock(string facilityId, ResourceStorage storage)
    {
        FacilityState state = GetState(facilityId);
        if (state == null || state.IsUnlocked) return false;

        CostBundle cost = state.Definition.BuildUnlockCost();
        if (!cost.IsFree)
        {
            foreach (CurrencyCost entry in cost.Costs)
            {
                if (!storage.CanSpend(entry))
                    return false;
            }

            foreach (CurrencyCost entry in cost.Costs)
                storage.TrySpend(entry);
        }

        state.Unlock();
        return true;
    }

    private FacilityRuntimeState GetOrCreateRuntimeState(FacilityDefinition definition)
    {
        if (ShelterDataManager.Instance != null)
        {
            return ShelterDataManager.Instance.GetOrCreateFacilityState(
                definition.FacilityId,
                definition.UnlockedByDefault);
        }

        Debug.LogWarning("[FacilityManager] ShelterDataManager is not available. Facility state will not be persistent.", this);
        return new FacilityRuntimeState(definition.FacilityId, definition.UnlockedByDefault);
    }
}
