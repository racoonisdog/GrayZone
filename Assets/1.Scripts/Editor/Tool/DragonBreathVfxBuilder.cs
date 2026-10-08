using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 용숨결탄 시각 효과 프리팹 두 개(총구 화염·원뿔 스파크 확산, 착탄 스파크 분출·낙하)와 머티리얼을 만듭니다.
/// </summary>
/// <remarks>
/// <para>
/// 참고 영상(드래곤 브레스 산탄 실사격)의 발사 직후 약 2초를 기준으로 구성했습니다.
/// 0~0.05초 총구에서 흰 불기둥이 곧게 뻗고, 0.15~0.3초 앞쪽이 넓은 원뿔형 스파크로 흩어지며,
/// 맞은 자리에서는 스파크가 반구형으로 튄 뒤 중력으로 떨어지고 바닥에 남은 불씨가 1~2초 동안 꺼져 갑니다.
/// </para>
/// <para>
/// 총구 쪽과 착탄 쪽을 프리팹 둘로 나눈 이유는 생성 위치가 다르기 때문입니다. 총구 쪽은 발사 위치·방향에,
/// 착탄 쪽은 불길이 닿은 면에(+Z가 면 바깥쪽) <see cref="DragonBreathEffect"/>가 따로 생성합니다.
/// 두 프리팹 모두 +Z가 앞쪽이라 <c>m_visualEffectRotation</c>은 0이어야 합니다.
/// </para>
/// <para>
/// 값은 이 파일에 모여 있습니다. 조정할 때는 숫자를 바꾸고 1번 메뉴를 다시 실행하면 같은 경로의 프리팹을 덮어씁니다.
/// </para>
/// </remarks>
public static class DragonBreathVfxBuilder
{
    private const string Folder = "Assets/2.Prefabs/VFX/DragonBreath";
    private const string MuzzlePrefabPath = Folder + "/DragonBreath_Muzzle.prefab";
    private const string ImpactPrefabPath = Folder + "/DragonBreath_Impact.prefab";
    private const string GlowMaterialPath = Folder + "/M_DragonBreath_Glow.mat";
    private const string FireMaterialPath = Folder + "/M_DragonBreath_Fire.mat";

    private const string GlowTexturePath = "Assets/3.Resources/VFX/HSFiles/Textures/Point12.png";
    private const string FireTexturePath = "Assets/3.Resources/VFX/HSFiles/Textures/EmberFire1.png";
    private const int FireSheetTiles = 8;

    private const string EffectPrefabPath = "Assets/Resources/Skill/DragonBreathEffect.prefab";

    // 불기둥(CoreJet)의 실제 속도에 맞춘 착탄 지연 속도입니다. 불기둥 길이를 바꾸면 DragonBreathEffect의 판정 길이(m_damageRange)도
    // 같이 맞춥니다. 판정 길이가 곧 착탄을 찾는 거리라, 불기둥보다 길면 불이 닿지 않은 벽에서 스파크가 터집니다.
    // 측정(2026-10-06): 불기둥 앞쪽 끝 0.1초 10.2m, 0.2초 16.4m.
    private const float ImpactTravelSpeed = 85.0f;

    // 불꽃 색: 가장 뜨거운 흰색 → 노랑 → 주황 → 식은 붉은색.
    private static readonly Color White = new Color(1.0f, 0.98f, 0.92f);
    private static readonly Color Yellow = new Color(1.0f, 0.85f, 0.42f);
    private static readonly Color Orange = new Color(1.0f, 0.45f, 0.08f);
    private static readonly Color Ember = new Color(0.75f, 0.13f, 0.02f);

    /// <summary>1단계. 머티리얼 두 개와 총구·착탄 프리팹을 만들거나 덮어씁니다.</summary>
    [MenuItem("Tools/GrayZone/VFX/Dragon Breath/1. Build VFX Prefabs")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        Material glow = CreateOrUpdateAdditiveMaterial(GlowMaterialPath, GlowTexturePath, new Color(2.0f, 1.8f, 1.6f, 1.0f), false);
        Material fire = CreateOrUpdateAdditiveMaterial(FireMaterialPath, FireTexturePath, new Color(1.8f, 1.5f, 1.3f, 1.0f), true);

        BuildMuzzle(glow, fire);
        BuildImpact(glow, fire);

        AssetDatabase.SaveAssets();
        Debug.Log($"[DragonBreathVfxBuilder] 생성 완료: {MuzzlePrefabPath}, {ImpactPrefabPath}");
    }

    /// <summary>2단계. 용숨결 효과 프리팹에 총구·착탄 프리팹을 연결하고 회전을 0으로 맞춥니다.</summary>
    /// <remarks>1단계와 나눈 이유: 같은 실행에서 막 만든 에셋을 대입하면 참조가 비어 저장되는 경우가 있습니다.</remarks>
    [MenuItem("Tools/GrayZone/VFX/Dragon Breath/2. Assign To DragonBreathEffect")]
    public static void AssignToEffect()
    {
        GameObject muzzle = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzlePrefabPath);
        GameObject impact = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPrefabPath);
        if (muzzle == null || impact == null)
        {
            Debug.LogError("[DragonBreathVfxBuilder] 먼저 1단계로 프리팹을 만드세요.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(EffectPrefabPath);
        try
        {
            DragonBreathEffect effect = root.GetComponent<DragonBreathEffect>();
            SerializedObject so = new SerializedObject(effect);
            so.FindProperty("m_visualEffectPrefab").objectReferenceValue = muzzle;
            so.FindProperty("m_visualEffectRotation").vector3Value = Vector3.zero;
            so.FindProperty("m_impactEffectPrefab").objectReferenceValue = impact;
            so.FindProperty("m_impactTravelSpeed").floatValue = ImpactTravelSpeed;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, EffectPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log($"[DragonBreathVfxBuilder] {EffectPrefabPath}에 연결 완료.");
    }

    // ───────────────────────── 총구: 화염 + 원뿔형 스파크 확산 ─────────────────────────

    private static void BuildMuzzle(Material glow, Material fire)
    {
        GameObject root = new GameObject("DragonBreath_Muzzle");
        try
        {
            // 발사 순간 총구의 흰 섬광.
            ParticleSystem flash = CreateSystem(root, "Flash", glow, 0.1f, 6);
            SetMain(flash, lifetime: (0.05f, 0.09f), speed: (0.0f, 0.0f), size: (1.0f, 1.5f), color: White);
            SetBursts(flash, 3);
            SetShapeSphere(flash, 0.05f, new Vector3(0.0f, 0.0f, 0.25f));
            SetSizeOverLifetime(flash, 0.6f, 1.2f);
            SetColorOverLifetime(flash, Gradient2(White, Yellow), Alpha(1.0f, 0.0f));
            RandomRotation(flash);

            // 총구 바로 앞의 짧은 불꽃 혀.
            ParticleSystem tongue = CreateSystem(root, "FlameTongue", glow, 0.1f, 12);
            SetMain(tongue, lifetime: (0.06f, 0.1f), speed: (6.0f, 12.0f), size: (0.15f, 0.3f), color: White);
            SetBursts(tongue, 10);
            SetShapeCone(tongue, 2.07f, 0.03f);
            SetStretched(tongue, 0.12f, 2.5f);
            AnchorStretchAtOrigin(tongue);
            SetColorOverLifetime(tongue, Gradient2(White, Orange), Alpha(1.0f, 0.0f));

            // 흰 불기둥. 빠르게 곧게 뻗다가 감속하며 굵어집니다.
            ParticleSystem core = CreateSystem(root, "CoreJet", glow, 0.15f, 1400);
            SetMain(core, lifetime: (0.17f, 0.29f), speed: (60.0f, 90.0f), size: (0.03f, 0.07f), color: White);
            SetBursts(core, 50, 1200.0f);
            SetShapeCone(core, 1.55f, 0.05f);
            SetStretched(core, 0.05f, 1.0f);
            AnchorStretchAtOrigin(core);
            SetCollision(core, bounce: 0.0f, dampen: 1.0f, lifetimeLoss: 1.0f);
            SetLimitVelocity(core, 36.0f, 0.1f);
            SetSizeOverLifetime(core, 1.0f, 1.4f);
            SetColorOverLifetime(core, Gradient3(White, Yellow, Orange), Alpha(1.0f, 1.0f, 0.0f));

            // 총구 앞 약 3m를 한 겹 더 감싸는 외피. 영상에서 총구 가까운 쪽 불기둥이 두껍다가
            // 앞으로 갈수록 가늘어지는 모양입니다. 굵은 입자 몇 개로 만들면 뭉툭한 원통이 되므로
            // 가는 선을 많이 겹치고, 나아가며 가늘어지게 해 끝이 뾰족하게 빠지도록 합니다.
            // 총구 쪽 입자는 빠르게 나가 0.1초 안에 사라지게 합니다. 오래 남으면 총구에 불이 고여 보입니다.
            ParticleSystem sheath = CreateSystem(root, "MuzzleSheath", glow, 0.12f, 700);
            SetMain(sheath, lifetime: (0.04f, 0.07f), speed: (30.0f, 45.0f), size: (0.04f, 0.08f), color: new Color(1.0f, 0.95f, 0.8f, 0.7f));
            SetBursts(sheath, 20, 500.0f);
            SetShapeCone(sheath, 3.45f, 0.06f);
            SetStretched(sheath, 0.06f, 1.5f);
            AnchorStretchAtOrigin(sheath);
            SetCollision(sheath, bounce: 0.0f, dampen: 1.0f, lifetimeLoss: 1.0f);
            SetLimitVelocity(sheath, 20.0f, 0.1f);
            SetSizeOverLifetime(sheath, 1.0f, 0.3f);
            SetColorOverLifetime(sheath, Gradient3(White, Yellow, Orange), Alpha(1.0f, 0.8f, 0.0f));

            // 불기둥을 감싸는 넓고 옅은 발광. 영상에서 불기둥이 굵고 하얗게 번져 보이는 부분입니다.
            ParticleSystem halo = CreateSystem(root, "CoreHalo", glow, 0.12f, 250);
            SetMain(halo, lifetime: (0.05f, 0.09f), speed: (48.0f, 72.0f), size: (0.5f, 0.9f), color: new Color(1.0f, 0.85f, 0.6f, 0.12f));
            SetBursts(halo, 8, 250.0f);
            SetShapeCone(halo, 1.04f, 0.05f);
            SetLimitVelocity(halo, 30.0f, 0.1f);
            SetCollision(halo, bounce: 0.0f, dampen: 1.0f, lifetimeLoss: 1.0f);
            SetSizeOverLifetime(halo, 0.5f, 1.2f);
            SetColorOverLifetime(halo, Gradient2(White, Orange), Alpha(1.0f, 0.8f, 0.0f));

            // 불기둥 둘레의 주황 불꽃 덩어리. 총구 앞에 머물지 않고 앞으로 쓸려 나가며 금방 꺼집니다.
            ParticleSystem body = CreateSystem(root, "FireBody", fire, 0.1f, 80);
            SetMain(body, lifetime: (0.05f, 0.09f), speed: (20.0f, 35.0f), size: (0.4f, 0.8f), color: new Color(1.0f, 0.78f, 0.5f));
            SetBursts(body, 6, 80.0f);
            SetShapeCone(body, 2.07f, 0.05f);
            SetLimitVelocity(body, 15.0f, 0.1f);
            SetSizeOverLifetime(body, 0.6f, 2.2f);
            SetColorOverLifetime(body, Gradient2(Color.white, Orange), Alpha(1.0f, 0.7f, 0.0f));
            SetFlipbook(body);
            RandomRotation(body);

            // 원뿔형 스파크. 빠른 속도를 거의 유지한 채 멀리 날아가며 식습니다(감속은 약하게).
            // 긴 선에 Noise를 넣으면 선이 물결치듯 휘어 보여서 늘린 스파크에는 넣지 않습니다.
            ParticleSystem sparks = CreateSystem(root, "SparkCone", glow, 0.15f, 1600);
            SetMain(sparks, lifetime: (0.17f, 0.41f), speed: (35.0f, 65.0f), size: (0.025f, 0.05f), color: White, gravity: 0.3f);
            SetBursts(sparks, 250, 1500.0f);
            SetShapeCone(sparks, 4.14f, 0.05f);
            SetStretched(sparks, 0.06f, 2.5f);
            AnchorStretchAtOrigin(sparks);
            SetCollision(sparks, bounce: 0.2f, dampen: 0.5f, lifetimeLoss: 0.6f);
            SetLimitVelocity(sparks, 18.0f, 0.04f);
            SetSizeOverLifetime(sparks, 1.0f, 0.4f);
            SetColorOverLifetime(sparks, Gradient4(White, Yellow, Orange, Ember), Alpha(1.0f, 1.0f, 0.8f, 0.0f));

            // 바깥쪽으로 넓게 흩어지는 스파크. 참고 영상 0.15~0.3초의 넓은 원뿔을 만듭니다.
            ParticleSystem spray = CreateSystem(root, "SparkSprayWide", glow, 0.15f, 400);
            SetMain(spray, lifetime: (0.15f, 0.3f), speed: (25.0f, 45.0f), size: (0.02f, 0.04f), color: Yellow, gravity: 0.4f);
            SetBursts(spray, 80, 400.0f);
            SetShapeCone(spray, 7.59f, 0.05f);
            SetStretched(spray, 0.06f, 2.0f);
            AnchorStretchAtOrigin(spray);
            SetLimitVelocity(spray, 12.0f, 0.05f);
            SetColorOverLifetime(spray, Gradient3(Yellow, Orange, Ember), Alpha(1.0f, 0.8f, 0.0f));

            // 지면을 비추는 불빛. 불기둥 길이를 따라 3개를 띄웁니다.
            AddLights(root, "GlowLights", lightCount: 3, lifetime: (0.18f, 0.28f), lineLength: 3.0f,
                range: 7.0f, intensity: 12.0f, color: new Color(1.0f, 0.72f, 0.38f));

            PrefabUtility.SaveAsPrefabAsset(root, MuzzlePrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ───────────────────────── 착탄: 스파크 분출 + 낙하 ─────────────────────────

    private static void BuildImpact(Material glow, Material fire)
    {
        GameObject root = new GameObject("DragonBreath_Impact");
        try
        {
            ParticleSystem flash = CreateSystem(root, "ImpactFlash", glow, 0.1f, 4);
            SetMain(flash, lifetime: (0.08f, 0.14f), speed: (0.0f, 0.0f), size: (0.44f, 0.66f), color: Yellow);
            SetBursts(flash, 2);
            SetShapeSphere(flash, 0.03f, Vector3.zero);
            SetSizeOverLifetime(flash, 0.7f, 1.3f);
            SetColorOverLifetime(flash, Gradient2(White, Orange), Alpha(1.0f, 0.0f));
            RandomRotation(flash);

            // 불길이 계속 들이치는 동안 착탄 지점에 남는 하얀 발광 덩어리.
            ParticleSystem coreGlow = CreateSystem(root, "ImpactCoreGlow", glow, 0.35f, 60);
            SetMain(coreGlow, lifetime: (0.25f, 0.45f), speed: (0.13f, 0.44f), size: (0.19f, 0.32f), color: new Color(1.0f, 0.9f, 0.7f, 0.45f));
            SetBursts(coreGlow, 4, 60.0f);
            SetShapeSphere(coreGlow, 0.126f, new Vector3(0.0f, 0.0f, 0.095f));
            SetSizeOverLifetime(coreGlow, 0.8f, 1.3f);
            SetColorOverLifetime(coreGlow, Gradient3(White, Yellow, Orange), Alpha(1.0f, 0.8f, 0.0f));
            RandomRotation(coreGlow);

            // 위로 높이 솟는 빠른 스파크. 영상에서 표적 위로 수 m 치솟는 분수 모양입니다.
            ParticleSystem fountain = CreateSystem(root, "SparkFountain", glow, 0.35f, 900);
            SetMain(fountain, lifetime: (0.35f, 0.8f), speed: (3.8f, 7.1f), size: (0.03f, 0.05f), color: White, gravity: 1.2f);
            SetBursts(fountain, 120, 600.0f);
            SetShapeCone(fountain, 35.0f, 0.126f);
            SetStretched(fountain, 0.08f, 2.5f);
            SetLimitVelocity(fountain, 4.0f, 0.08f);
            SetColorOverLifetime(fountain, Gradient4(White, Yellow, Orange, Ember), Alpha(1.0f, 1.0f, 0.7f, 0.0f));
            // 분수는 맞은 면보다 위쪽으로 기울여 솟게 합니다.
            fountain.transform.localRotation = Quaternion.Euler(-35.0f, 0.0f, 0.0f);

            // 맞은 자리의 짧은 불덩이.
            ParticleSystem fireball = CreateSystem(root, "ImpactFireball", fire, 0.1f, 8);
            SetMain(fireball, lifetime: (0.4f, 0.6f), speed: (0.21f, 0.42f), size: (0.28f, 0.44f), color: new Color(1.0f, 0.8f, 0.55f));
            SetBursts(fireball, 4);
            SetShapeCone(fireball, 40.0f, 0.063f);
            SetSizeOverLifetime(fireball, 0.6f, 1.4f);
            SetColorOverLifetime(fireball, Gradient2(Color.white, Orange), Alpha(1.0f, 0.6f, 0.0f));
            SetFlipbook(fireball);
            RandomRotation(fireball);

            // 반구형으로 튀는 스파크. 중력으로 떨어지고 바닥에서 튕깁니다.
            ParticleSystem burst = CreateSystem(root, "SparkBurst", glow, 0.4f, 2000);
            SetMain(burst, lifetime: (0.45f, 1.05f), speed: (2.05f, 6.15f), size: (0.03f, 0.06f), color: White, gravity: 1.0f);
            SetBursts(burst, 300, 1000.0f);
            SetShapeCone(burst, 65.0f, 0.095f);
            SetStretched(burst, 0.08f, 2.5f);
            SetLimitVelocity(burst, 1.37f, 0.06f);
            SetSizeOverLifetime(burst, 1.0f, 0.5f);
            SetColorOverLifetime(burst, Gradient4(White, Yellow, Orange, Ember), Alpha(1.0f, 1.0f, 0.8f, 0.0f));
            SetCollision(burst, bounce: 0.3f, dampen: 0.4f, lifetimeLoss: 0.15f);

            // 오래 남아 깜빡이며 떨어지는 불씨. 바닥에 내려앉아 식습니다.
            ParticleSystem embers = CreateSystem(root, "FallingEmbers", glow, 0.4f, 300);
            SetMain(embers, lifetime: (1.2f, 2.0f), speed: (0.47f, 1.89f), size: (0.04f, 0.08f), color: Yellow, gravity: 0.6f);
            SetBursts(embers, 70, 200.0f);
            SetShapeCone(embers, 70.0f, 0.095f);
            SetLimitVelocity(embers, 2.0f, 0.1f);
            SetNoise(embers, 0.6f, 0.8f);
            SetColorOverLifetime(embers, Gradient3(Yellow, Orange, Ember),
                Alpha(1.0f, 0.55f, 1.0f, 0.4f, 0.9f, 0.3f, 0.7f, 0.0f));
            SetCollision(embers, bounce: 0.1f, dampen: 0.8f, lifetimeLoss: 0.0f);

            AddLights(root, "ImpactLight", lightCount: 1, lifetime: (0.5f, 0.6f), lineLength: 0.0f,
                range: 3.8f, intensity: 10.0f, color: new Color(1.0f, 0.62f, 0.3f));

            PrefabUtility.SaveAsPrefabAsset(root, ImpactPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ───────────────────────── 구성 도우미 ─────────────────────────

    private static Material CreateOrUpdateAdditiveMaterial(string path, string texturePath, Color baseColor, bool flipbookBlending)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        material.SetColor("_BaseColor", baseColor);
        material.SetFloat("_Surface", 1.0f); // Transparent
        material.SetFloat("_Blend", 2.0f);   // Additive
        material.SetFloat("_ColorMode", 0.0f); // Multiply
        material.SetFloat("_FlipbookBlending", flipbookBlending ? 1.0f : 0.0f);
        material.SetFloat("_SoftParticlesEnabled", 1.0f);
        material.SetFloat("_SoftParticlesNearFadeDistance", 0.0f);
        material.SetFloat("_SoftParticlesFarFadeDistance", 0.6f);

        BaseShaderGUI.SetupMaterialBlendMode(material);
        ParticleGUI.SetMaterialKeywords(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    private static ParticleSystem CreateSystem(GameObject root, string name, Material material, float duration, int maxParticles)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = true;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0.0f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return system;
    }

    private static void SetMain(ParticleSystem system, (float min, float max) lifetime, (float min, float max) speed,
        (float min, float max) size, Color color, float gravity = 0.0f)
    {
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
        main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
        main.startColor = color;
        main.gravityModifier = gravity;
    }

    /// <summary>시작 순간 한 번에 <paramref name="count"/>개를 내고, <paramref name="rate"/>가 있으면 길이 동안 계속 냅니다.</summary>
    private static void SetBursts(ParticleSystem system, int count, float rate = 0.0f)
    {
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = rate;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, (short)count) });
    }

    private static void SetShapeCone(ParticleSystem system, float angle, float radius)
    {
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.radiusThickness = 1.0f;
    }

    private static void SetShapeSphere(ParticleSystem system, float radius, Vector3 offset)
    {
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.position = offset;
    }

    private static void SetStretched(ParticleSystem system, float velocityScale, float lengthScale)
    {
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = velocityScale;
        renderer.lengthScale = lengthScale;
    }

    /// <summary>
    /// 생성 위치를 +Z로 "가장 긴 선 길이의 절반"만큼 옮겨, 늘린 선의 꼬리 끝이 원점(총구·맞은 면)에서 시작하게 합니다.
    /// </summary>
    /// <remarks>
    /// 늘린 빌보드는 입자 위치를 가운데로 앞뒤로 늘어납니다. 생기자마자 길게 그려지는 빠른 입자는 꼬리 절반이
    /// 총구 뒤(쏘는 캐릭터 몸 쪽)나 맞은 면 속으로 삐져나옵니다. 속도 배율(speed modifier)로 출발을 늦추는 방법은
    /// 위치에만 적용되고 늘림 길이에는 적용되지 않아 효과가 없었습니다. <see cref="SetMain"/>과 <see cref="SetStretched"/> 뒤에 부릅니다.
    /// </remarks>
    private static void AnchorStretchAtOrigin(ParticleSystem system)
    {
        ParticleSystem.MainModule main = system.main;
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        float longest = GetMax(main.startSpeed) * renderer.velocityScale + GetMax(main.startSize) * renderer.lengthScale;

        ParticleSystem.ShapeModule shape = system.shape;
        Vector3 position = shape.position;
        position.z += longest * 0.5f;
        shape.position = position;
    }

    private static float GetMax(ParticleSystem.MinMaxCurve curve)
    {
        return curve.mode == ParticleSystemCurveMode.TwoConstants ? curve.constantMax : curve.constant;
    }

    /// <summary>공기 저항처럼 속도가 <paramref name="limit"/>까지 서서히 줄게 합니다.</summary>
    private static void SetLimitVelocity(ParticleSystem system, float limit, float dampen)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule module = system.limitVelocityOverLifetime;
        module.enabled = true;
        module.limit = limit;
        module.dampen = dampen;
    }

    private static void SetNoise(ParticleSystem system, float strength, float frequency)
    {
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
        noise.scrollSpeed = 1.0f;
        noise.quality = ParticleSystemNoiseQuality.Medium;
    }

    private static void SetSizeOverLifetime(ParticleSystem system, float start, float end)
    {
        ParticleSystem.SizeOverLifetimeModule module = system.sizeOverLifetime;
        module.enabled = true;
        module.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0.0f, start, 1.0f, end));
    }

    private static void SetColorOverLifetime(ParticleSystem system, GradientColorKey[] colors, GradientAlphaKey[] alphas)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(colors, alphas);
        ParticleSystem.ColorOverLifetimeModule module = system.colorOverLifetime;
        module.enabled = true;
        module.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private static void SetFlipbook(ParticleSystem system)
    {
        ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = FireSheetTiles;
        sheet.numTilesY = FireSheetTiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f));
        sheet.cycleCount = 1;
    }

    private static void RandomRotation(ParticleSystem system)
    {
        ParticleSystem.MainModule main = system.main;
        main.startRotation = new ParticleSystem.MinMaxCurve(0.0f, Mathf.PI * 2.0f);
    }

    private static void SetCollision(ParticleSystem system, float bounce, float dampen, float lifetimeLoss)
    {
        ParticleSystem.CollisionModule collision = system.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.bounce = bounce;
        collision.dampen = dampen;
        collision.lifetimeLoss = lifetimeLoss;
        collision.radiusScale = 1.0f;
        collision.enableDynamicColliders = true;
        collision.sendCollisionMessages = false;

        // 쏜 사람과 앞에 선 팀원 몸에서는 불길이 끊기지 않게 합니다(피해·착탄 규칙도 아군을 통과).
        collision.collidesWith = ~LayerMask.GetMask("Player", "Ignore Raycast");
    }

    /// <summary>
    /// 그리지 않는 파티클에 Lights 모듈을 붙여 짧게 깜빡이는 점광원을 만듭니다.
    /// <paramref name="lineLength"/>가 0보다 크면 +Z 방향 그 길이 안에 흩어 둡니다.
    /// </summary>
    private static void AddLights(GameObject root, string name, int lightCount, (float min, float max) lifetime,
        float lineLength, float range, float intensity, Color color)
    {
        // Lights 모듈이 설정만 복사해 쓰는 원본 광원입니다. 꺼 두어야 원본 자체는 비추지 않습니다.
        GameObject template = new GameObject(name + "_Template");
        template.transform.SetParent(root.transform, false);
        Light light = template.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
        template.SetActive(false);

        ParticleSystem system = CreateSystem(root, name, null, 0.05f, lightCount);
        SetMain(system, lifetime, (0.0f, 0.0f), (1.0f, 1.0f), Color.white);
        SetBursts(system, lightCount);
        if (lineLength > 0.0f)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.0f, 0.0f, lineLength);
            shape.position = new Vector3(0.0f, 0.0f, lineLength * 0.5f);
        }

        SetColorOverLifetime(system, Gradient2(Color.white, Color.white), Alpha(1.0f, 0.6f, 0.0f));

        ParticleSystem.LightsModule lights = system.lights;
        lights.enabled = true;
        lights.light = light;
        lights.ratio = 1.0f;
        lights.maxLights = lightCount;
        lights.useParticleColor = true;
        lights.alphaAffectsIntensity = true;
        lights.sizeAffectsRange = false;
        lights.intensityMultiplier = 1.0f;
        lights.rangeMultiplier = 1.0f;

        system.GetComponent<ParticleSystemRenderer>().enabled = false;
    }

    private static GradientColorKey[] Gradient2(Color a, Color b)
    {
        return new[] { new GradientColorKey(a, 0.0f), new GradientColorKey(b, 1.0f) };
    }

    private static GradientColorKey[] Gradient3(Color a, Color b, Color c)
    {
        return new[] { new GradientColorKey(a, 0.0f), new GradientColorKey(b, 0.35f), new GradientColorKey(c, 1.0f) };
    }

    private static GradientColorKey[] Gradient4(Color a, Color b, Color c, Color d)
    {
        return new[]
        {
            new GradientColorKey(a, 0.0f), new GradientColorKey(b, 0.25f),
            new GradientColorKey(c, 0.6f), new GradientColorKey(d, 1.0f),
        };
    }

    /// <summary>값들을 수명 0~1에 같은 간격으로 놓은 알파 키를 만듭니다(최대 8개).</summary>
    private static GradientAlphaKey[] Alpha(params float[] values)
    {
        GradientAlphaKey[] keys = new GradientAlphaKey[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            keys[i] = new GradientAlphaKey(values[i], values.Length == 1 ? 0.0f : i / (float)(values.Length - 1));
        }

        return keys;
    }
}
