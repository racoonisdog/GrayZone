using System.IO;
using System.Text;
using UnityEngine;

public static class SaveFilePaths
{
    public const string SaveDirectoryName = "SaveData";
    public const string DefaultProfileId = "default";
    public const string SettingsFileName = "settings.json";

    //윈도우 기준 사용자의 AppData를 저장경로로 한다 (exe파일 기준으로도 가능하지만 권한문제가 생길수 있음 C:)
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

    /// <summary>게임 저장 파일을 지운 뒤 알립니다. 타이틀의 불러오기 버튼이 이 때 다시 상태를 맞춥니다.</summary>
    public static event System.Action GameSavesDeleted;

    /// <summary>
    /// 게임 저장 파일(game_save_*.json, 자동 저장 포함)을 모두 지워 처음 게임을 시작하는 상태로 만듭니다. 설정 파일은 지우지 않습니다.
    /// </summary>
    /// <returns>지운 파일 수입니다.</returns>
    public static int DeleteAllGameSaves()
    {
        int deleted = 0;
        if (Directory.Exists(SaveDirectoryPath))
        {
            foreach (string path in Directory.GetFiles(SaveDirectoryPath, "game_save_*.json"))
            {
                try
                {
                    File.Delete(path);
                    deleted++;
                }
                catch (IOException exception)
                {
                    Debug.LogWarning($"[SaveFilePaths] 저장 파일을 지우지 못했습니다: {path}\n{exception.Message}");
                }
                catch (System.UnauthorizedAccessException exception)
                {
                    Debug.LogWarning($"[SaveFilePaths] 저장 파일을 지우지 못했습니다: {path}\n{exception.Message}");
                }
            }
        }

        GameSavesDeleted?.Invoke();
        return deleted;
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
