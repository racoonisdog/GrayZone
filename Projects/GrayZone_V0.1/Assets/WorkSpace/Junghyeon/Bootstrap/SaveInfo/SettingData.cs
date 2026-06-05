using System;
using System.Collections.Generic;

[Serializable]
public class SettingData
{
    public const int CurrentSchemaVersion = 1;

    public int schemaVersion = CurrentSchemaVersion;
    public long updatedAtUnixTimeUtc;

    public DisplaySettingData display = new DisplaySettingData();
    public AudioSettingData audio = new AudioSettingData();

    // JSON string payload for future custom settings.
    public List<CustomSettingEntry> customData = new List<CustomSettingEntry>();

    public void MarkUpdatedNow()
    {
        updatedAtUnixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    [Serializable]
    public class DisplaySettingData
    {
        public int width = 1920;
        public int height = 1080;
        public bool fullscreen = true;
    }

    [Serializable]
    public class AudioSettingData
    {
        public float masterVolume = 1f;
        public float bgmVolume = 1f;
        public float sfxVolume = 1f;
    }

    [Serializable]
    public class CustomSettingEntry
    {
        public string key = string.Empty;
        public string json = "{}";
    }
}
