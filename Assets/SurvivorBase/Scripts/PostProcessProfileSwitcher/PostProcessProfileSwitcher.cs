using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class PostProcessProfileSwitcher : MonoBehaviour {
    [Header("Volume Profiles")]
    public VolumeProfile standardProfile;
    public VolumeProfile summerProfile;
    public VolumeProfile winterProfile;
    public VolumeProfile autumnProfile;

    [Header("Global Snow Settings")]
    public ScriptableObject globalSnowProfile;

    [Header("Fog Settings")]
    public Color winterFogColor = new Color(0.478f, 0.478f, 0.482f); // #7A7A7B
    public float winterFogDensity = 0.007f;
    
    public Color summerFogColor = new Color(0.769f, 0.878f, 1.0f); // #C4E0FF
    public float summerFogDensity = 0.001f;

    public Color autumnFogColor = new Color(0.933f, 0.757f, 0.667f); // #EEC1AA
    public float autumnFogDensity = 0.001f;

    public Color standardFogColor = new Color(0.419f, 0.682f, 0.980f); // #6BAEFA
    public float standardFogDensity = 0.001f;

    [Header("Light Settings")]
    public float winterLightIntensity = 0.4f;
    public float summerLightIntensity = 1.55f;
    public float autumnLightIntensity = 0.6f;
    public float standardLightIntensity = 0.95f;

    public Light directionalLight;

    private Volume _volume;

    private void Awake() {
        _volume = GetComponent<Volume>();
        if (directionalLight == null) FindDirectionalLight();
    }

    private void FindDirectionalLight() {
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in lights) {
            if (l.type == LightType.Directional) {
                directionalLight = l;
                break;
            }
        }
    }

    public void SetProfile(VolumeProfile profile) {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != "Demo_Day" && sceneName != "Demo_Night") {
            Debug.Log($"Profile switching is disabled in scene '{sceneName}'. Only allowed in 'Demo_Day' and 'Demo_Night'.");
            return;
        }

        if (profile == null) {
            Debug.LogWarning("Profile is null!");
            return;
        }

        if (_volume == null) _volume = GetComponent<Volume>();
        
        if (_volume != null) {
            _volume.sharedProfile = profile;
            Debug.Log($"Switched to profile: {profile.name}");
        } else {
            Debug.LogError("No Volume component found on this object!");
        }

        if (directionalLight == null) FindDirectionalLight();

        // Determine settings
        float targetIntensity = standardLightIntensity;
        Color? targetLightColor = null;

        if (sceneName == "Demo_Night") {
             // Night overrides
             targetIntensity = 0.14f;
             ColorUtility.TryParseHtmlString("#94DEFF", out Color nightColor);
             targetLightColor = nightColor;
        } else {
            // Day Settings (Season dependent)
            if (profile == winterProfile) targetIntensity = winterLightIntensity;
            else if (profile == summerProfile) targetIntensity = summerLightIntensity;
            else if (profile == autumnProfile) targetIntensity = autumnLightIntensity;
            else targetIntensity = standardLightIntensity;
        }

        // Apply Season Specifics (Fog, Snow)
        bool applyFog = sceneName != "Demo_Night";

        if (profile == winterProfile) {
            ApplyGlobalSnow();
            if (applyFog) ApplyFog(winterFogColor, winterFogDensity);
        } else if (profile == summerProfile) {
            RemoveGlobalSnow();
            if (applyFog) ApplyFog(summerFogColor, summerFogDensity);
        } else if (profile == autumnProfile) {
            RemoveGlobalSnow();
            if (applyFog) ApplyFog(autumnFogColor, autumnFogDensity);
        } else {
            RemoveGlobalSnow();
            if (applyFog) ApplyFog(standardFogColor, standardFogDensity);
        }

        // Apply Light
        ApplyLightSettings(targetIntensity, targetLightColor);
    }

    private void ApplyLightSettings(float intensity, Color? color = null) {
        if (directionalLight != null) {
            directionalLight.intensity = intensity;
            if (color.HasValue) {
                directionalLight.color = color.Value;
                Debug.Log($"Set Directional Light Intensity to {intensity} and Color to {ColorUtility.ToHtmlStringRGB(color.Value)}");
            } else {
                Debug.Log($"Set Directional Light Intensity to {intensity}");
            }
        } else {
            Debug.LogWarning("Directional Light not found!");
        }
    }

    private void ApplyFog(Color color, float density) {
        RenderSettings.fog = true;
        RenderSettings.fogColor = color;
        RenderSettings.fogDensity = density;
        Debug.Log($"Applied Fog: {ColorUtility.ToHtmlStringRGB(color)} (Density: {density})");
    }

    private void ApplyGlobalSnow() {
        Type snowType = GetTypeByName("GlobalSnowEffect.GlobalSnow");

        if (snowType == null) {
            Debug.LogWarning("Global Snow Asset is not installed or detected in the project. Please install Global Snow to use the Winter profile features.");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();

        if (cam != null) {
            Component snowScript = cam.GetComponent(snowType);
            if (snowScript == null) {
                snowScript = cam.gameObject.AddComponent(snowType);
            }

            if (globalSnowProfile != null) {
                // Use reflection to set the 'profile' field/property
                var profileField = snowType.GetField("profile");
                if (profileField != null) {
                    profileField.SetValue(snowScript, globalSnowProfile);
                } else {
                    var profileProp = snowType.GetProperty("profile");
                    if (profileProp != null) {
                        profileProp.SetValue(snowScript, globalSnowProfile);
                    }
                }
            }

            // Enable the script
            var enabledProp = typeof(Behaviour).GetProperty("enabled");
            if (enabledProp != null) enabledProp.SetValue(snowScript, true);
            
            Debug.Log($"Applied Global Snow Profile: {globalSnowProfile?.name}");
        }
    }

    private void RemoveGlobalSnow() {
        Type snowType = GetTypeByName("GlobalSnowEffect.GlobalSnow");
        if (snowType == null) return; // Asset not present, nothing to remove

        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();

        if (cam != null) {
            Component snowScript = cam.GetComponent(snowType);
            if (snowScript != null) {
                // DestroyImmediate is safe to call in Editor mode
                DestroyImmediate(snowScript);
                Debug.Log("Removed Global Snow from Camera");
            }
        }
    }

    private Type GetTypeByName(string name) {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            foreach (var type in assembly.GetTypes()) {
                if (type.FullName == name) return type;
            }
        }
        return null;
    }
}
