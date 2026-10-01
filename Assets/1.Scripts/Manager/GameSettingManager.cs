using System.IO;
using UnityEngine;
using UnityEngine.Audio;

public class GameSettingManager : MonoBehaviour
{
    [System.Serializable]
    public class SettingSnapshot
    {
        public int gameplaySettingsVersion = 1;
        public int width = 1920;
        public int height = 1080;
        public bool fullscreen = true;
        public float masterVolume = 1f;
        public float bgmVolume = 1f;
        public float sfxVolume = 1f;
        public float mouseSensitivity = 1f;
        public bool cameraKickEnabled = true;
    }

    public static GameSettingManager Instance { get; private set; }

    [Header("Display Settings")]
    [SerializeField] private SettingSnapshot currentSettings = new SettingSnapshot();

    [Header("Audio Mixer (선택)")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterVolumeParam = "MasterVolume";
    [SerializeField] private string bgmVolumeParam = "BgmVolume";
    [SerializeField] private string sfxVolumeParam = "SfxVolume";

    private const float MuteDb = -80f;
    private const float MaxDb = 0f;
    public const float MinMouseSensitivity = 0.01f;
    public const float MaxMouseSensitivity = 10f;

    public float MouseSensitivity => currentSettings.mouseSensitivity;
    public bool CameraKickEnabled => currentSettings.cameraKickEnabled;

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        Instance = this;
        NormalizeGameplaySettings(currentSettings);

        if (!LoadSettings())
        {
            ApplySnapshot(currentSettings);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetResolution(int width, int height, bool fullscreen)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning($"[GameSettingManager] Invalid resolution: {width}x{height}");
            return;
        }

        currentSettings.width = width;
        currentSettings.height = height;
        currentSettings.fullscreen = fullscreen;

        Screen.SetResolution(width, height, fullscreen);
    }

    public void SetFullscreen(bool isFullscreen)
    {
        currentSettings.fullscreen = isFullscreen;
        Screen.fullScreen = isFullscreen;
    }

    public void SetMasterVolume(float volume)
    {
        currentSettings.masterVolume = Mathf.Clamp01(volume);
        ApplyMixerVolume(masterVolumeParam, currentSettings.masterVolume);
    }

    public void SetBgmVolume(float volume)
    {
        currentSettings.bgmVolume = Mathf.Clamp01(volume);
        ApplyMixerVolume(bgmVolumeParam, currentSettings.bgmVolume);
    }

    public void SetSfxVolume(float volume)
    {
        currentSettings.sfxVolume = Mathf.Clamp01(volume);
        ApplyMixerVolume(sfxVolumeParam, currentSettings.sfxVolume);
    }

    public void SetMouseSensitivity(float sensitivity)
    {
        float clamped = Mathf.Clamp(sensitivity, MinMouseSensitivity, MaxMouseSensitivity);
        currentSettings.mouseSensitivity = Mathf.Round(clamped * 100f) / 100f;
        currentSettings.gameplaySettingsVersion = 1;
    }

    public void SetCameraKickEnabled(bool enabled)
    {
        currentSettings.cameraKickEnabled = enabled;
        currentSettings.gameplaySettingsVersion = 1;
    }

    // Applies all currently cached display/audio values.
    public void ApplySettings()
    {
        SetResolution(currentSettings.width, currentSettings.height, currentSettings.fullscreen);
        SetFullscreen(currentSettings.fullscreen);
        SetMasterVolume(currentSettings.masterVolume);
        SetBgmVolume(currentSettings.bgmVolume);
        SetSfxVolume(currentSettings.sfxVolume);
        SetMouseSensitivity(currentSettings.mouseSensitivity);
        SetCameraKickEnabled(currentSettings.cameraKickEnabled);
    }

    // Saves only SettingData to disk.
    public bool SaveSettings()
    {
        SettingData data = CreateSettingData();
        string path = SaveFilePaths.GetSettingPath();

        try
        {
            SaveFilePaths.EnsureSaveDirectory();
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameSettingManager] SaveSettings failed. Path: {path}. Error: {ex.Message}");
            return false;
        }
    }

    // Loads SettingData from disk. Returns false when file is missing or invalid.
    public bool LoadSettings()
    {
        string path = SaveFilePaths.GetSettingPath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            SettingData data = JsonUtility.FromJson<SettingData>(json);
            if (data == null)
            {
                return false;
            }

            ApplySettingData(data);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameSettingManager] LoadSettings failed. Path: {path}. Error: {ex.Message}");
            return false;
        }
    }

    public SettingSnapshot CreateSnapshot()
    {
        return new SettingSnapshot
        {
            gameplaySettingsVersion = 1,
            width = currentSettings.width,
            height = currentSettings.height,
            fullscreen = currentSettings.fullscreen,
            masterVolume = currentSettings.masterVolume,
            bgmVolume = currentSettings.bgmVolume,
            sfxVolume = currentSettings.sfxVolume,
            mouseSensitivity = currentSettings.mouseSensitivity,
            cameraKickEnabled = currentSettings.cameraKickEnabled
        };
    }

    public SettingData CreateSettingData()
    {
        SettingData data = new SettingData();
        data.display.width = currentSettings.width;
        data.display.height = currentSettings.height;
        data.display.fullscreen = currentSettings.fullscreen;
        data.audio.masterVolume = currentSettings.masterVolume;
        data.audio.bgmVolume = currentSettings.bgmVolume;
        data.audio.sfxVolume = currentSettings.sfxVolume;
        data.gameplay.mouseSensitivity = currentSettings.mouseSensitivity;
        data.gameplay.cameraKickEnabled = currentSettings.cameraKickEnabled;
        data.MarkUpdatedNow();
        return data;
    }

    public void ApplySnapshot(SettingSnapshot snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameSettingManager] Snapshot is null.");
            return;
        }

        NormalizeGameplaySettings(snapshot);
        SetResolution(snapshot.width, snapshot.height, snapshot.fullscreen);
        SetFullscreen(snapshot.fullscreen);
        SetMasterVolume(snapshot.masterVolume);
        SetBgmVolume(snapshot.bgmVolume);
        SetSfxVolume(snapshot.sfxVolume);
        SetMouseSensitivity(snapshot.mouseSensitivity);
        SetCameraKickEnabled(snapshot.cameraKickEnabled);
    }

    public void ApplySettingData(SettingData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[GameSettingManager] SettingData is null.");
            return;
        }

        if (data.display == null)
        {
            data.display = new SettingData.DisplaySettingData();
        }

        if (data.audio == null)
        {
            data.audio = new SettingData.AudioSettingData();
        }

        bool hasGameplaySettings = data.schemaVersion >= 2 && data.gameplay != null;
        if (data.gameplay == null)
        {
            data.gameplay = new SettingData.GameplaySettingData();
        }

        SettingSnapshot snapshot = new SettingSnapshot
        {
            gameplaySettingsVersion = 1,
            width = data.display.width,
            height = data.display.height,
            fullscreen = data.display.fullscreen,
            masterVolume = data.audio.masterVolume,
            bgmVolume = data.audio.bgmVolume,
            sfxVolume = data.audio.sfxVolume,
            mouseSensitivity = hasGameplaySettings ? data.gameplay.mouseSensitivity : 1f,
            cameraKickEnabled = !hasGameplaySettings || data.gameplay.cameraKickEnabled
        };

        ApplySnapshot(snapshot);
    }

    private static void NormalizeGameplaySettings(SettingSnapshot snapshot)
    {
        if (snapshot == null || snapshot.gameplaySettingsVersion >= 1)
        {
            return;
        }

        // gameplaySettingsVersion이 없던 기존 Scene/Prefab 직렬화 값은 숫자와 bool이 0/false로 들어옵니다.
        // 새 옵션을 추가한 첫 실행에서 카메라 킥이 갑자기 꺼지지 않도록 명시적인 기본값으로 이관합니다.
        snapshot.gameplaySettingsVersion = 1;
        snapshot.mouseSensitivity = 1f;
        snapshot.cameraKickEnabled = true;
    }

    private void ApplyMixerVolume(string parameterName, float normalizedVolume)
    {
        if (audioMixer == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        float clamped = Mathf.Clamp01(normalizedVolume);
        float db = clamped <= 0.0001f ? MuteDb : Mathf.Clamp(Mathf.Log10(clamped) * 20f, MuteDb, MaxDb);
        audioMixer.SetFloat(parameterName, db);
    }

    private bool TryRejectDuplicateOrInvalidRoot()
    {
        GameManager rootManager = GetComponentInParent<GameManager>();
        if (rootManager == null)
        {
            Debug.LogWarning("[GameSettingManager] Parent GameManager not found. Destroying duplicate/orphan instance.");
            Destroy(gameObject);
            return true;
        }

        if (GameManager.Instance != null && rootManager != GameManager.Instance)
        {
            Destroy(gameObject);
            return true;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
