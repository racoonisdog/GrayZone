using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public const int CurrentSchemaVersion = 12;

    public int schemaVersion = CurrentSchemaVersion;
    public ShelterCheckpointId shelterCheckpointId = ShelterCheckpointId.BeforeDefense1;

    public int trapUpgradeLevel;
    public int spikeUpgradeLevel;
    public int explosiveUpgradeLevel;
    public int shooterUpgradeLevel;
    public int wireUpgradeLevel;

    public List<ResourceAmountData> resources = new List<ResourceAmountData>();
    public List<CharacterStateSaveData> characters = new List<CharacterStateSaveData>();

    [Serializable]
    public class ResourceAmountData
    {
        public string resourceId = string.Empty;
        public int amount;
    }

    [Serializable]
    public class CharacterStateSaveData
    {
        public PlayerbleCharacterId characterId;
        public int currentHp;
        public int maxHp = 1;
        public float injurySeverityGauge;
        public float maxInjuryGauge = 100.0f;
        public CharacterInjuryState injuryState;
        public bool isDown;
        public bool isCombatOut;
    }
}
