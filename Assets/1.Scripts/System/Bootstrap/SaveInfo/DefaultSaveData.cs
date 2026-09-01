using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 새 게임 시작 시 사용할 저장 데이터의 읽기 전용 작성 템플릿입니다.
/// </summary>
/// <remarks>
/// 이 에셋 자체는 런타임 진행 상태를 소유하지 않습니다.
/// <see cref="CreateSaveData"/>를 호출할 때마다 독립적인 <see cref="SaveData"/>를 생성합니다.
/// </remarks>
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

    [Header("Save Identity")]
    [SerializeField] private string profileId = SaveFilePaths.DefaultProfileId;
    [SerializeField] private SaveSlotType slotType = SaveSlotType.Manual;

    [Header("Shared Defaults")]
    [SerializeField] private string startStageId = string.Empty;
    [SerializeField, Range(0, 100)] private int shelterStability = 100;
    [SerializeField] private StartingResourceAmount[] startingResources = Array.Empty<StartingResourceAmount>();
    [SerializeField] private PlayerbleCharacterDefinition[] startingCharacterDefinitions = Array.Empty<PlayerbleCharacterDefinition>();

    [Header("Shelter Defaults")]
    [SerializeField, Min(1)] private int startDay = 1;
    [SerializeField] private FacilityDefinition[] startingFacilityDefinitions = Array.Empty<FacilityDefinition>();

    /// <summary>
    /// 현재 에셋 값을 독립적인 새 게임 저장 데이터로 변환합니다.
    /// </summary>
    public SaveData CreateSaveData()
    {
        SaveData saveData = new SaveData
        {
            schemaVersion = SaveData.CurrentSchemaVersion,
            profileId = ResolveProfileId(profileId),
            slotType = slotType,
            shared = new SaveData.SharedSaveData
            {
                lastStageId = startStageId?.Trim() ?? string.Empty,
                shelterStability = Mathf.Clamp(shelterStability, 0, 100)
            },
            shelter = new SaveData.ShelterSaveData
            {
                currentDay = Mathf.Max(1, startDay)
            }
        };

        AddStartingResources(saveData.shared);
        AddStartingCharacters(saveData.shared);
        AddStartingFacilities(saveData.shelter);
        return saveData;
    }

    private void AddStartingResources(SaveData.SharedSaveData sharedSaveData)
    {
        if (sharedSaveData == null || startingResources == null)
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

            sharedSaveData.resources.Add(new SaveData.ResourceAmountData
            {
                resourceId = resourceId,
                amount = startingResource.Amount
            });
        }
    }

    private void AddStartingCharacters(SaveData.SharedSaveData sharedSaveData)
    {
        if (sharedSaveData == null || startingCharacterDefinitions == null)
        {
            return;
        }

        for (int i = 0; i < startingCharacterDefinitions.Length; i++)
        {
            PlayerbleCharacterDefinition definition = startingCharacterDefinitions[i];
            if (definition != null)
            {
                sharedSaveData.characters.Add(definition.CreateSnapshot());
            }
        }
    }

    private void AddStartingFacilities(SaveData.ShelterSaveData shelterSaveData)
    {
        if (shelterSaveData == null || startingFacilityDefinitions == null)
        {
            return;
        }

        HashSet<string> addedFacilityIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < startingFacilityDefinitions.Length; i++)
        {
            SaveData.FacilitySaveData facilitySaveData =
                FacilitySaveDataMapper.FromDefinition(startingFacilityDefinitions[i]);
            if (facilitySaveData == null || !addedFacilityIds.Add(facilitySaveData.facilityId))
            {
                continue;
            }

            shelterSaveData.facilities.Add(facilitySaveData);
        }
    }

    private static string ResolveProfileId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? SaveFilePaths.DefaultProfileId
            : value.Trim();
    }
}
