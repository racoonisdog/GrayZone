using UnityEngine;

public interface IFacilityService
{
    CostBundle Cost { get; }
    bool CanExecute(ResourceStorage storage);
    bool TryExecute(ResourceStorage storage);
}
