[System.Serializable]
public sealed class FacilityRuntimeState
{
    public string facilityId = string.Empty;
    public bool isUnlocked;
    public int upgradeLevel;

    public FacilityRuntimeState()
    {
    }

    public FacilityRuntimeState(string facilityId, bool isUnlocked, int upgradeLevel = 0)
    {
        this.facilityId = NormalizeFacilityId(facilityId);
        this.isUnlocked = isUnlocked;
        this.upgradeLevel = System.Math.Max(0, upgradeLevel);
    }

    public void EnsureValid(string fallbackFacilityId = "")
    {
        if (string.IsNullOrWhiteSpace(facilityId))
            facilityId = NormalizeFacilityId(fallbackFacilityId);

        upgradeLevel = System.Math.Max(0, upgradeLevel);
    }

    private static string NormalizeFacilityId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
