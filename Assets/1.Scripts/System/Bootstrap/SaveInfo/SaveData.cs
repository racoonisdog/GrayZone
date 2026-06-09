using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public const int CurrentSchemaVersion = 2;

    public int schemaVersion = CurrentSchemaVersion;
    public string profileId = "default";
    public long savedAtUnixTimeUtc;

    public PlayerProgressData progress = new PlayerProgressData();

    // Extend these sections as the project grows.
    public PlayerStatsData playerStats = new PlayerStatsData();
    public InventoryData inventory = new InventoryData();
    public QuestData quests = new QuestData();
    public AchievementData achievements = new AchievementData();

    // JSON string payload for custom future data blocks.
    public List<CustomDataEntry> customData = new List<CustomDataEntry>();

    public void MarkSavedNow()
    {
        savedAtUnixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    //ToDo : 저장 정보 변경 ( 게임에 맞게 )
    [Serializable]
    public class PlayerProgressData
    {
        public int playerLevel = 1;
        public int playerGold;
        public string lastStageId = string.Empty;
        public int currentDay = 1;
    }

    [Serializable]
    public class PlayerStatsData
    {
        public List<StatEntry> stats = new List<StatEntry>();
    }

    [Serializable]
    public class StatEntry
    {
        public string statId = string.Empty;
        public int value;
    }

    [Serializable]
    public class InventoryData
    {
        public List<InventoryItemEntry> items = new List<InventoryItemEntry>();
    }

    [Serializable]
    public class InventoryItemEntry
    {
        public string itemId = string.Empty;
        public int amount;
    }

    [Serializable]
    public class QuestData
    {
        public List<QuestStateEntry> activeQuests = new List<QuestStateEntry>();
        public List<QuestStateEntry> completedQuests = new List<QuestStateEntry>();
    }

    [Serializable]
    public class QuestStateEntry
    {
        public string questId = string.Empty;
        public string state = "Unknown";
    }

    [Serializable]
    public class AchievementData
    {
        public List<AchievementEntry> unlocked = new List<AchievementEntry>();
    }

    [Serializable]
    public class AchievementEntry
    {
        public string achievementId = string.Empty;
        public long unlockedAtUnixTimeUtc;
    }

    [Serializable]
    public class CustomDataEntry
    {
        public string key = string.Empty;
        public string json = "{}";
    }
}
