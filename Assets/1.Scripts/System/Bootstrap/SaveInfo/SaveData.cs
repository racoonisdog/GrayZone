using System;
using System.Collections.Generic;

/// <summary>저장 파일이 오토세이브인지 수동 저장인지 구분합니다.</summary>
public enum SaveSlotType
{
    Auto,
    Manual
}

[Serializable]
public class SaveData
{
    public const int CurrentSchemaVersion = 6;

    public int schemaVersion = CurrentSchemaVersion;
    public string profileId = "default";
    public SaveSlotType slotType = SaveSlotType.Manual;

    public long savedAtUnixTimeUtc;

    public SharedSaveData shared = new SharedSaveData();
    public ShelterSaveData shelter = new ShelterSaveData();
    public BattleResultData lastBattleResult;

    /// <summary>현재 UTC 시각을 저장 파일 생성 시각으로 기록합니다.</summary>
    public void MarkSavedNow()
    {
        savedAtUnixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    [Serializable]
    public class SharedSaveData
    {
        public string lastStageId = string.Empty;
        public int shelterStability = 100;
        public int playableCharacterCount;
        public int nonPlayableNpcCount;
        public List<ResourceAmountData> resources = new List<ResourceAmountData>();
        public List<NpcSaveData> npcs = new List<NpcSaveData>();
    }

    [Serializable]
    public class ShelterSaveData
    {
        public int currentDay = 1;
        public List<string> battleSquadNpcDefinitionIds = new List<string>();
        public List<FacilitySaveData> facilities = new List<FacilitySaveData>();
    }

    [Serializable]
    public class ResourceAmountData
    {
        public CurrencyType type;
        public int amount;
    }

    [Serializable]
    public class NpcSaveData
    {
        /// <summary>캐릭터 기본 수치와 장착 총기 상태를 함께 보존하는 schemaVersion 5 이상 공용 스냅샷입니다.</summary>
        public CharacterSnapshotData characterSnapshot = new CharacterSnapshotData();

        // schemaVersion 4 이하 저장 파일을 불러오기 위한 호환 필드입니다.
        public string definitionId = string.Empty;
        public NPCType type;
        public int maxHp = 1;
        public int currentHp = 1;
        public bool isAssignedToShelter;
        public string assignedRoomId = string.Empty;
        public float injuryGauge = 100;
        public float maxInjuryGauge = 100;
    }

    [Serializable]
    public class FacilitySaveData
    {
        public string facilityId = string.Empty;
        public bool isUnlocked;
        public int upgradeLevel;
    }
}
