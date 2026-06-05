using System.IO;
using UnityEngine;
using UnityEngine.Audio;

public class GameSettingManager : MonoBehaviour
{
    [System.Serializable]
    public class SettingSnapshot
    {
        public int width = 1920;
        public int height = 1080;
        public bool fullscreen = true;
        public float masterVolume = 1f;
        public float bgmVolume = 1f;
        public float sfxVolume = 1f;
    }

    public static GameSettingManager Instance { get; private set; }

    [Header("화면 설정")]
    [SerializeField] private SettingSnapshot currentSettings = new SettingSnapshot();

    [Header("오디오 믹서 (선택)")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterVolumeParam = "MasterVolume";
    [SerializeField] private string bgmVolumeParam = "BgmVolume";
    [SerializeField] private string sfxVolumeParam = "SfxVolume";

    private const float MuteDb = -80f;
    private const float MaxDb = 0f;

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        Instance = this;

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

    // Applies all currently cached display/audio values.
    public void ApplySettings()
    {
        SetResolution(currentSettings.width, currentSettings.height, currentSettings.fullscreen);
        SetFullscreen(currentSettings.fullscreen);
        SetMasterVolume(currentSettings.masterVolume);
        SetBgmVolume(currentSettings.bgmVolume);
        SetSfxVolume(currentSettings.sfxVolume);
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
            width = currentSettings.width,
            height = currentSettings.height,
            fullscreen = currentSettings.fullscreen,
            masterVolume = currentSettings.masterVolume,
            bgmVolume = currentSettings.bgmVolume,
            sfxVolume = currentSettings.sfxVolume
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

        SetResolution(snapshot.width, snapshot.height, snapshot.fullscreen);
        SetFullscreen(snapshot.fullscreen);
        SetMasterVolume(snapshot.masterVolume);
        SetBgmVolume(snapshot.bgmVolume);
        SetSfxVolume(snapshot.sfxVolume);
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

        SettingSnapshot snapshot = new SettingSnapshot
        {
            width = data.display.width,
            height = data.display.height,
            fullscreen = data.display.fullscreen,
            masterVolume = data.audio.masterVolume,
            bgmVolume = data.audio.bgmVolume,
            sfxVolume = data.audio.sfxVolume
        };

        ApplySnapshot(snapshot);
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
