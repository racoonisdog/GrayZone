public interface IFacilityUpgradeable
{
    string FacilityId { get; }
    int UpgradeLevel { get; }
    int MaxUpgradeLevel { get; }

    void ApplyUpgradeLevel(int level);
}
