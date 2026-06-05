using System.IO;
using System.Text;
using UnityEngine;

public static class SaveFilePaths
{
    public const string SaveDirectoryName = "SaveData";
    public const string DefaultProfileId = "default";
    public const string SettingsFileName = "settings.json";

    public static string SaveDirectoryPath => Path.Combine(Application.persistentDataPath, SaveDirectoryName);

    public static void EnsureSaveDirectory()
    {
        if (!Directory.Exists(SaveDirectoryPath))
        {
            Directory.CreateDirectory(SaveDirectoryPath);
        }
    }

    public static string GetGameSavePath(string profileId)
    {
        string resolvedProfileId = string.IsNullOrWhiteSpace(profileId) ? DefaultProfileId : profileId;
        string safeProfileId = SanitizeFileName(resolvedProfileId);
        return Path.Combine(SaveDirectoryPath, $"game_save_{safeProfileId}.json");
    }

    public static string GetSettingPath()
    {
        return Path.Combine(SaveDirectoryPath, SettingsFileName);
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(fileName.Length);

        for (int i = 0; i < fileName.Length; i++)
        {
            char c = fileName[i];
            bool isInvalid = false;
            for (int j = 0; j < invalidChars.Length; j++)
            {
                if (c == invalidChars[j])
                {
                    isInvalid = true;
                    break;
                }
            }

            builder.Append(isInvalid ? '_' : c);
        }

        string sanitized = builder.ToString().Trim();
        return string.IsNullOrEmpty(sanitized) ? DefaultProfileId : sanitized;
    }
}
