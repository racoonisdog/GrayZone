public class FacilityState
{
    public FacilityDefinition Definition { get; }
    public bool IsUnlocked { get; private set; }
    public bool IsBroken { get; private set; }
    public bool IsAvailable => IsUnlocked && !IsBroken;

    public FacilityState(FacilityDefinition definition, bool isUnlocked = false)
    {
        Definition = definition;
        IsUnlocked = isUnlocked;
    }

    public void Unlock() => IsUnlocked = true;
    public void SetBroken(bool broken) => IsBroken = broken;
    public void Repair() => IsBroken = false;
}
