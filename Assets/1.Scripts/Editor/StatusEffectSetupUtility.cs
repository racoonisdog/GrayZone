#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Creates the default status assets, placeholder visuals, and prefab wiring.</summary>
public static class StatusEffectSetupUtility
{
    private const string StatusFolder = "Assets/Resources/StatusEffects";
    private const string VisualFolder = "Assets/Resources/StatusEffectVisuals";
    private const string VisualPrefabFolder = "Assets/2.Prefabs/StatusEffect";
    private const string PlaceholderMaterialPath = VisualPrefabFolder + "/Placeholder_AuraParticle.mat";
    private const string BurningPath = StatusFolder + "/Burning.asset";
    private const string HastePath = StatusFolder + "/NarinTeamHaste.asset";
    private const string DragonBreathPrefabPath = "Assets/Resources/Skill/DragonBreathEffect.prefab";
    private const string ShotgunPrefabPath = "Assets/2.Prefabs/Weapon/Shotgun.prefab";

    [MenuItem("Tools/GrayZone/Setup Status Effect Defaults")]
    public static string CreateDefaultsAndWirePrefabs()
    {
        EnsureFolders();
        Material placeholderMaterial = GetOrCreatePlaceholderParticleMaterial();

        StatusEffectVisualSO burningVisual = CreateVisual(
            "Burning", new Color(1.0f, 0.24f, 0.02f, 0.9f), true, 1.0f, placeholderMaterial);
        StatusEffectVisualSO hasteVisual = CreateVisual(
            "NarinHaste", new Color(0.1f, 0.75f, 1.0f, 0.8f), true, 1.0f, placeholderMaterial);
        StatusEffectVisualSO healVisual = CreateVisual(
            "TeamHeal", new Color(0.2f, 1.0f, 0.35f, 0.85f), false, 1.5f, placeholderMaterial);
        StatusEffectVisualSO howlerVisual = CreateVisual(
            "HowlerBuff", new Color(0.65f, 0.15f, 1.0f, 0.85f), true, 1.0f, placeholderMaterial);

        StatusEffectDefinitionSO burning = GetOrCreate(BurningPath, "Burning");
        ConfigureDefinition(
            burning, "Burning", StatusEffectCategory.Debuff,
            4.0f, 100.0f, 100.0f, 2, 1.0f, burningVisual);

        StatusEffectDefinitionSO haste = GetOrCreate(HastePath, "NarinTeamHaste");
        ConfigureDefinition(
            haste, "Tactical Haste", StatusEffectCategory.Buff,
            10.0f, 150.0f, 150.0f, 0, 1.0f, hasteVisual);

        WireObjectReference<DragonBreathEffect>(DragonBreathPrefabPath, "m_burningEffect", burning);
        WireObjectReference<NarinTeamHasteSkill>(
            "Assets/2.Prefabs/Player/Narin.prefab", "m_hasteEffect", haste);
        WireObjectReference<SeoHaTeamHealSkill>(
            "Assets/2.Prefabs/Player/SeoHa.prefab", "m_healVisual", healVisual);
        WireHowlerBuffVisual(howlerVisual);
        EnsureStatusContainer("Assets/2.Prefabs/Player/Narin.prefab");
        EnsureStatusContainer("Assets/2.Prefabs/Player/ChungSol.prefab");
        EnsureStatusContainer("Assets/2.Prefabs/Player/SeoHa.prefab");
        EnsureDragonBreathShotgunChild();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "Status effects configured: definitions, pooled recipient visuals, player containers, and DragonBreathEffect";
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder(StatusFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "StatusEffects");
        }

        if (!AssetDatabase.IsValidFolder(VisualFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "StatusEffectVisuals");
        }

        if (!AssetDatabase.IsValidFolder(VisualPrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/2.Prefabs", "StatusEffect");
        }
    }

    private static StatusEffectDefinitionSO GetOrCreate(string path, string assetName)
    {
        StatusEffectDefinitionSO asset = AssetDatabase.LoadAssetAtPath<StatusEffectDefinitionSO>(path);
        if (asset != null)
        {
            return asset;
        }

        asset = ScriptableObject.CreateInstance<StatusEffectDefinitionSO>();
        asset.name = assetName;
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void ConfigureDefinition(
        StatusEffectDefinitionSO asset,
        string displayName,
        StatusEffectCategory category,
        float duration,
        float movementPercent,
        float actionPercent,
        int periodicDamage,
        float tickInterval,
        StatusEffectVisualSO recipientVisual)
    {
        SerializedObject serialized = new SerializedObject(asset);
        serialized.FindProperty("m_displayName").stringValue = displayName;
        serialized.FindProperty("m_category").enumValueIndex = (int)category;
        serialized.FindProperty("m_duration").floatValue = duration;
        serialized.FindProperty("m_stackPolicy").enumValueIndex = (int)StatusEffectStackPolicy.RefreshDuration;
        serialized.FindProperty("m_maxStacks").intValue = 1;
        serialized.FindProperty("m_recipientVisual").objectReferenceValue = recipientVisual;
        serialized.FindProperty("m_movementSpeedPercent").floatValue = movementPercent;
        serialized.FindProperty("m_actionSpeedPercent").floatValue = actionPercent;
        serialized.FindProperty("m_periodicDamage").intValue = periodicDamage;
        serialized.FindProperty("m_tickInterval").floatValue = tickInterval;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private static StatusEffectVisualSO CreateVisual(
        string assetName,
        Color color,
        bool loop,
        float oneShotLifetime,
        Material placeholderMaterial)
    {
        string prefabPath = VisualPrefabFolder + "/Placeholder_" + assetName + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            prefab = CreatePlaceholderParticlePrefab(
                prefabPath, assetName, color, loop, placeholderMaterial);
        }

        ConfigurePlaceholderParticlePrefab(
            prefabPath, color, loop, placeholderMaterial);

        string assetPath = VisualFolder + "/" + assetName + ".asset";
        StatusEffectVisualSO visual = AssetDatabase.LoadAssetAtPath<StatusEffectVisualSO>(assetPath);
        if (visual == null)
        {
            visual = ScriptableObject.CreateInstance<StatusEffectVisualSO>();
            visual.name = assetName;
            AssetDatabase.CreateAsset(visual, assetPath);
        }

        SerializedObject serialized = new SerializedObject(visual);
        serialized.FindProperty("m_prefab").objectReferenceValue = prefab;
        serialized.FindProperty("m_localPosition").vector3Value = Vector3.zero;
        serialized.FindProperty("m_localEulerAngles").vector3Value = Vector3.zero;
        serialized.FindProperty("m_localScaleMultiplier").vector3Value = Vector3.one;
        serialized.FindProperty("m_oneShotLifetime").floatValue = oneShotLifetime;
        serialized.FindProperty("m_restartOnStatusRefresh").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(visual);
        return visual;
    }

    private static GameObject CreatePlaceholderParticlePrefab(
        string prefabPath,
        string effectName,
        Color color,
        bool loop,
        Material placeholderMaterial)
    {
        GameObject root = new GameObject("Placeholder_" + effectName);
        try
        {
            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            ParticleSystemRenderer particleRenderer = root.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = placeholderMaterial;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            ParticleSystem.MainModule main = particles.main;
            main.loop = loop;
            main.duration = 1.0f;
            main.startLifetime = loop ? 1.0f : 0.8f;
            main.startSpeed = loop ? 0.15f : 0.8f;
            main.startSize = loop ? 0.16f : 0.22f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;
            main.maxParticles = 96;

            ParticleSystem.EmissionModule emission = particles.emission;
            if (loop)
            {
                emission.rateOverTime = 24.0f;
            }
            else
            {
                emission.rateOverTime = 0.0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 32) });
            }

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.8f;
            shape.radiusThickness = 0.2f;
            shape.rotation = new Vector3(90.0f, 0.0f, 0.0f);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0.0f),
                    new GradientColorKey(color, 1.0f),
                },
                new[]
                {
                    new GradientAlphaKey(color.a, 0.0f),
                    new GradientAlphaKey(0.0f, 1.0f),
                });
            colorOverLifetime.color = gradient;

            return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Material GetOrCreatePlaceholderParticleMaterial()
    {
        Texture2D texture = FindBuiltinParticleTexture();

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = "Placeholder_AuraParticle",
            };
            AssetDatabase.CreateAsset(material, PlaceholderMaterialPath);
        }
        else if (shader != null)
        {
            material.shader = shader;
        }

        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1.0f);
        material.SetFloat("_Blend", 2.0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_ZWrite", 0.0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("SHADOWCASTER", false);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D FindBuiltinParticleTexture()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Resources/unity_builtin_extra");
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Texture2D texture && texture.name == "Default-Particle")
            {
                return texture;
            }
        }

        return Texture2D.whiteTexture;
    }

    private static void ConfigurePlaceholderParticlePrefab(
        string prefabPath,
        Color color,
        bool loop,
        Material material)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem.MainModule main = particles[i].main;
                main.loop = loop;
                main.startLifetime = loop ? 1.0f : 0.8f;
                main.startSpeed = loop ? 0.15f : 0.8f;
                main.startSize = loop ? 0.16f : 0.22f;
                main.startColor = color;

                ParticleSystem.EmissionModule emission = particles[i].emission;
                emission.rateOverTime = loop ? 24.0f : 0.0f;
                emission.SetBursts(loop
                    ? System.Array.Empty<ParticleSystem.Burst>()
                    : new[] { new ParticleSystem.Burst(0.0f, 32) });

                ParticleSystem.ShapeModule shape = particles[i].shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.8f;
                shape.radiusThickness = 0.2f;
                shape.rotation = new Vector3(90.0f, 0.0f, 0.0f);
            }

            ParticleSystemRenderer[] renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void WireObjectReference<T>(string prefabPath, string propertyName, Object value)
        where T : Component
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component == null)
            {
                Debug.LogError($"[StatusEffectSetup] Could not find {typeof(T).Name} in {prefabPath}.");
                return;
            }

            SerializedObject serialized = new SerializedObject(component);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureStatusContainer(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (root.GetComponent<StatusEffectContainer>() == null)
            {
                root.AddComponent<StatusEffectContainer>();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void WireHowlerBuffVisual(StatusEffectVisualSO visual)
    {
        const string assetPath =
            "Assets/5.Data/ScriptableObject/Enemy/EnemyBuff_HowlMoveSpeed.asset";
        EnemyBuffSO buff = AssetDatabase.LoadAssetAtPath<EnemyBuffSO>(assetPath);
        if (buff == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(buff);
        serialized.FindProperty("m_recipientVisual").objectReferenceValue = visual;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(buff);
    }

    private static void EnsureDragonBreathShotgunChild()
    {
        GameObject effectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DragonBreathPrefabPath);
        GameObject shotgunRoot = PrefabUtility.LoadPrefabContents(ShotgunPrefabPath);
        try
        {
            Gun gun = shotgunRoot.GetComponentInChildren<Gun>(true);
            if (gun == null || effectPrefab == null)
            {
                Debug.LogError("[StatusEffectSetup] Could not find the Shotgun Gun or DragonBreathEffect prefab.");
                return;
            }

            DragonBreathEffect existing = gun.GetComponentInChildren<DragonBreathEffect>(true);
            if (existing == null)
            {
                Transform parent = gun.FirePos != null ? gun.FirePos : gun.transform;
                GameObject instance = PrefabUtility.InstantiatePrefab(effectPrefab, parent) as GameObject;
                if (instance == null)
                {
                    Debug.LogError("[StatusEffectSetup] Could not instantiate the DragonBreathEffect prefab.");
                    return;
                }

                instance.name = "DragonBreathEffect";
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(shotgunRoot, ShotgunPrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(shotgunRoot);
        }
    }
}
#endif
