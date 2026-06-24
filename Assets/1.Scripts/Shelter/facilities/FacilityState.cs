public sealed class FacilityState
{
    public FacilityDefinition Definition { get; }
    public FacilityRuntimeState RuntimeState { get; }
    public string FacilityId => RuntimeState.facilityId;
    public bool IsUnlocked => RuntimeState.isUnlocked;
    public int UpgradeLevel => RuntimeState.upgradeLevel;

    public FacilityState(FacilityDefinition definition, FacilityRuntimeState runtimeState)
    {
        if (definition == null)
            throw new System.ArgumentNullException(nameof(definition));

        if (runtimeState == null)
            throw new System.ArgumentNullException(nameof(runtimeState));

        Definition = definition;
        RuntimeState = runtimeState;

        if (string.IsNullOrWhiteSpace(RuntimeState.facilityId))
            RuntimeState.facilityId = definition.FacilityId ?? string.Empty;

        RuntimeState.EnsureValid(definition.FacilityId);
    }

    public void Unlock() => RuntimeState.isUnlocked = true;

    public void SetUpgradeLevel(int level)
    {
        RuntimeState.upgradeLevel = System.Math.Max(0, level);
    }
}
