using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>새 게임 시작 시 사용할 최소 Auto 복구 데이터의 작성 템플릿입니다.</summary>
[CreateAssetMenu(fileName = "DefaultSaveData", menuName = "GrayZone/Save/Default Save Data")]
public sealed class DefaultSaveData : ScriptableObject
{
    [Serializable]
    public struct StartingResourceAmount
    {
        [SerializeField] private string resourceId;
        [SerializeField, Min(0)] private int amount;

        public string ResourceId => ResourceIds.Normalize(resourceId);
        public int Amount => Mathf.Max(0, amount);
    }

    [Header("Checkpoint")]
    [SerializeField] private ShelterCheckpointId shelterCheckpointId = ShelterCheckpointId.BeforeDefense1;

    [Header("Defense Upgrade Defaults")]
    [SerializeField, Min(0)] private int trapUpgradeLevel;
    [SerializeField, Min(0)] private int spikeUpgradeLevel;
    [SerializeField, Min(0)] private int explosiveUpgradeLevel;
    [SerializeField, Min(0)] private int shooterUpgradeLevel;
    [SerializeField, Min(0)] private int wireUpgradeLevel;

    [Header("Starting State")]
    [SerializeField] private StartingResourceAmount[] startingResources = Array.Empty<StartingResourceAmount>();
    [SerializeField] private PlayerbleCharacterDefinition[] startingCharacterDefinitions = Array.Empty<PlayerbleCharacterDefinition>();

    public SaveData CreateSaveData()
    {
        SaveData saveData = new SaveData
        {
            schemaVersion = SaveData.CurrentSchemaVersion,
            shelterCheckpointId = shelterCheckpointId,
            trapUpgradeLevel = Mathf.Max(0, trapUpgradeLevel),
            spikeUpgradeLevel = Mathf.Max(0, spikeUpgradeLevel),
            explosiveUpgradeLevel = Mathf.Max(0, explosiveUpgradeLevel),
            shooterUpgradeLevel = Mathf.Max(0, shooterUpgradeLevel),
            wireUpgradeLevel = Mathf.Max(0, wireUpgradeLevel)
        };

        AddStartingResources(saveData);
        AddStartingCharacterStates(saveData);
        return saveData;
    }

    public List<CharacterSnapshotData> CreateStartingCharacterSnapshots()
    {
        List<CharacterSnapshotData> snapshots = new List<CharacterSnapshotData>();
        if (startingCharacterDefinitions == null)
        {
            return snapshots;
        }

        HashSet<PlayerbleCharacterId> addedCharacterIds = new HashSet<PlayerbleCharacterId>();
        for (int i = 0; i < startingCharacterDefinitions.Length; i++)
        {
            PlayerbleCharacterDefinition definition = startingCharacterDefinitions[i];
            if (definition == null
                || definition.CharacterId == PlayerbleCharacterId.Unknown
                || !addedCharacterIds.Add(definition.CharacterId))
            {
                continue;
            }

            snapshots.Add(definition.CreateSnapshot());
        }

        return snapshots;
    }

    private void AddStartingResources(SaveData saveData)
    {
        if (startingResources == null)
        {
            return;
        }

        HashSet<string> addedResourceIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < startingResources.Length; i++)
        {
            StartingResourceAmount startingResource = startingResources[i];
            string resourceId = startingResource.ResourceId;
            if (string.IsNullOrEmpty(resourceId) || !addedResourceIds.Add(resourceId))
            {
                continue;
            }

            saveData.resources.Add(new SaveData.ResourceAmountData
            {
                resourceId = resourceId,
                amount = startingResource.Amount
            });
        }
    }

    private void AddStartingCharacterStates(SaveData saveData)
    {
        List<CharacterSnapshotData> snapshots = CreateStartingCharacterSnapshots();
        for (int i = 0; i < snapshots.Count; i++)
        {
            CharacterSnapshotData snapshot = snapshots[i];
            saveData.characters.Add(new SaveData.CharacterStateSaveData
            {
                characterId = snapshot.CharacterId,
                currentHp = snapshot.CurrentHp,
                maxHp = snapshot.MaxHp,
                injurySeverityGauge = snapshot.InjurySeverityGauge,
                maxInjuryGauge = snapshot.MaxInjuryGauge,
                injuryState = snapshot.InjuryState,
                isDown = snapshot.IsDown,
                isCombatOut = snapshot.IsCombatOut
            });
        }
    }
}
