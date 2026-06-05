using UnityEngine;
using System.Collections.Generic;


//Todo : 시설 업그레이드 관련 기능 Component만들어야함 , 기획문서 필요
public class BaseManager : MonoBehaviour
{
    [SerializeField] private FacilityDefinition[] _definitions;

    private readonly Dictionary<string, FacilityState> _states = new();

    public IReadOnlyDictionary<string, FacilityState> States => _states;

    private void Awake()
    {
        foreach (var def in _definitions)
        {
            if (def == null) continue;
            _states[def.FacilityId] = new FacilityState(def); 
        }
    }

    public FacilityState GetState(string facilityId)
        => _states.TryGetValue(facilityId, out var state) ? state : null;

    public bool TryUnlock(string facilityId, ResourceStorage storage)
    {
        var state = GetState(facilityId);
        if (state == null || state.IsUnlocked) return false;

        var cost = state.Definition.BuildUnlockCost();
        if (!cost.IsFree)
        {
            foreach (var entry in cost.Costs)
            {
                if (!storage.CanSpend(entry))
                    return false;
            }
            foreach (var entry in cost.Costs)
                storage.TrySpend(entry);
        }

        state.Unlock();
        return true;
    }
}
