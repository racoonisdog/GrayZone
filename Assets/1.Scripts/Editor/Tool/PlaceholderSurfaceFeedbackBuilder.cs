using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 표면 데칼 파이프라인을 지금 눈으로 확인하기 위한 임시 표면 피드백 자산을 만듭니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>플레이스홀더입니다.</b> 최종 아트가 아니며, 실제 데칼이 들어오면 이 자산을 대체하고
/// 이 도구는 지웁니다. 이름에 <c>Placeholder</c>를 붙이는 이유도 아트 결과물과 섞이지 않게 하기 위해서입니다.
/// </para>
/// <para>
/// 만드는 이유는 <see cref="SurfaceFeedbackSO"/> 에셋이 프로젝트에 하나도 없어 시스템·배선·예산이
/// 모두 갖춰져 있어도 아무것도 나오지 않기 때문입니다. 검증할 대상이 없으면 데칼 풀링과 예산이 실제로
/// 도는지 확인할 방법이 없습니다.
/// </para>
/// </remarks>
public static class PlaceholderSurfaceFeedbackBuilder
{
    private const string PrefabFolder = "Assets/2.Prefabs/Feedback";
    private const string DataFolder = "Assets/5.Data/ScriptableObject/Feedback";

    private const string DecalPrefabPath = PrefabFolder + "/Placeholder_BulletHole.prefab";
    private const string DecalMaterialPath = DataFolder + "/Placeholder_BulletHole.mat";
    private const string FeedbackAssetPath = DataFolder + "/Placeholder_DefaultSurfaceFeedback.asset";

    /// <summary>
    /// 1단계. 프리팹·머티리얼·표면 피드백 에셋을 만듭니다.
    /// </summary>
    /// <remarks>
    /// 배선을 같은 실행에 넣지 않습니다. <see cref="AssetDatabase.CreateAsset"/> 직후의 인스턴스는
    /// 같은 실행 안에서 영속 에셋으로 잡히지 않는 구간이 있어, 그대로 씬에 대입하면 <c>fileID 0</c>으로
    /// 저장됩니다. 실측에서 로그는 "물렸다"고 나오는데 디스크는 비어 있었고,
    /// 진단으로 <c>IsPersistent=false</c>, <c>GetAssetPath</c>가 빈 문자열임을 확인했습니다.
    /// 메뉴를 나누면 2단계는 임포트가 끝난 뒤 새로 시작하므로 이 구간을 피합니다.
    /// </remarks>
    [MenuItem("GrayZone/Field/임시 탄흔 자산 1 - 생성")]
    public static void Build()
    {
        List<string> log = new List<string>();

        EnsureFolder(PrefabFolder);
        EnsureFolder(DataFolder);

        GameObject decal = BuildDecalPrefab(log);
        BuildFeedbackAsset(decal, log);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        log.Add("생성까지 끝났습니다. 이어서 '임시 탄흔 자산 2 - 배선'을 실행하십시오.");

        WriteLog(log);
        Debug.Log("[PlaceholderSurfaceFeedback] 생성 완료\n" + string.Join("\n", log));
    }

    /// <summary>
    /// 2단계. 만들어 둔 표면 피드백을 씬과 프리팹의 EffectManager에 물립니다.
    /// </summary>
    [MenuItem("GrayZone/Field/임시 탄흔 자산 2 - 배선")]
    public static void Assign()
    {
        List<string> log = new List<string>();

        AssignToScenes(log);

        WriteLog(log);
        Debug.Log("[PlaceholderSurfaceFeedback] 배선 완료\n" + string.Join("\n", log));
    }

    private static void WriteLog(List<string> log)
    {
        try
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string directory = Path.Combine(root, "Backup", "FieldManagerMigration");
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "placeholder-log.txt"), log);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[PlaceholderSurfaceFeedback] 로그 기록 실패: {exception.Message}");
        }

        Debug.Log("[PlaceholderSurfaceFeedback] 완료\n" + string.Join("\n", log));
    }

    /// <summary>
    /// 탄흔 자리에 놓을 작은 검은 판을 만듭니다.
    /// </summary>
    /// <remarks>
    /// <b>콜라이더를 반드시 지웁니다.</b> Quad 프리미티브는 MeshCollider를 달고 나오는데, 그대로 두면
    /// 벽에 박힌 탄흔이 다음 사격의 레이캐스트를 막아 총알이 탄흔에 맞습니다.
    /// </remarks>
    private static GameObject BuildDecalPrefab(List<string> log)
    {
        Material material = BuildUnlitMaterial(DecalMaterialPath, new Color(0.05f, 0.05f, 0.05f, 1.0f));

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Placeholder_BulletHole";
        quad.transform.localScale = new Vector3(0.12f, 0.12f, 1.0f);

        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;

        // Quad는 +Z를 정면으로 보므로, EffectPool이 LookRotation(법선)으로 놓으면 표면을 등집니다.
        quad.transform.Rotate(0.0f, 180.0f, 0.0f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(quad, DecalPrefabPath);
        Object.DestroyImmediate(quad);

        log.Add($"데칼 프리팹: {DecalPrefabPath}");
        return prefab;
    }

    private static SurfaceFeedbackSO BuildFeedbackAsset(GameObject decal, List<string> log)
    {
        SurfaceFeedbackSO feedback = AssetDatabase.LoadAssetAtPath<SurfaceFeedbackSO>(FeedbackAssetPath);

        if (feedback == null)
        {
            feedback = ScriptableObject.CreateInstance<SurfaceFeedbackSO>();
            AssetDatabase.CreateAsset(feedback, FeedbackAssetPath);
            log.Add($"표면 피드백 에셋 생성: {FeedbackAssetPath}");
        }

        SerializedObject serialized = new SerializedObject(feedback);
        serialized.FindProperty("m_surfaceId").stringValue = "Placeholder";
        serialized.FindProperty("m_decalPrefab").objectReferenceValue = decal;
        serialized.FindProperty("m_decalLifetime").floatValue = 20.0f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(feedback);
        return feedback;
    }

    /// <summary>
    /// 씬과 프리팹의 EffectManager에 기본 표면 피드백으로 물립니다.
    /// </summary>
    /// <remarks>
    /// 물리 머티리얼을 지정하지 않고 <c>기본</c> 슬롯에만 넣습니다. 그래야 머티리얼이 없는 레벨
    /// 지오메트리에도 폴백으로 걸려 어디를 쏘든 확인할 수 있습니다.
    /// </remarks>
    private static void AssignToScenes(List<string> log)
    {
        string[] sceneCandidates =
        {
            "Assets/0.Scenes/JangHu/CombatPlayTest.unity",
            "Assets/0.Scenes/JangHu/CombatPlayTest 1.unity",
        };

        foreach (string path in sceneCandidates)
        {
            // 씬 파일 이름이 바뀐 적이 있어 존재 여부를 먼저 확인합니다.
            if (!File.Exists(Path.GetFullPath(path)))
            {
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            // 씬을 연 뒤에 읽습니다. OpenScene(Single)은 이전 씬을 걷어내며 쓰이지 않는 에셋을 언로드하는데,
            // 그 전에 잡아 둔 참조는 파괴된 객체가 되어 대입해도 fileID 0으로 저장됩니다.
            SurfaceFeedbackSO feedback = LoadFeedback(log, path);

            if (feedback == null)
            {
                continue;
            }

            bool changed = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (EffectManager manager in root.GetComponentsInChildren<EffectManager>(true))
                {
                    changed |= Assign(manager, feedback, log);
                }
            }

            if (!changed)
            {
                log.Add($"{path}: EffectManager를 찾지 못했습니다.");
                continue;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add($"{path}: 기본 표면 피드백을 물렸습니다.");
        }

        const string prefabPath = "Assets/2.Prefabs/Manager/FieldManager.prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            SurfaceFeedbackSO feedback = LoadFeedback(log, prefabPath);

            if (feedback == null)
            {
                return;
            }

            bool changed = false;

            foreach (EffectManager manager in contents.GetComponentsInChildren<EffectManager>(true))
            {
                changed |= Assign(manager, feedback, log);
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                log.Add($"{prefabPath}: 기본 표면 피드백을 물렸습니다.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>표면 피드백 에셋을 경로에서 읽고 영속 객체인지 확인합니다.</summary>
    private static SurfaceFeedbackSO LoadFeedback(List<string> log, string context)
    {
        SurfaceFeedbackSO feedback = AssetDatabase.LoadAssetAtPath<SurfaceFeedbackSO>(FeedbackAssetPath);

        if (feedback == null)
        {
            log.Add($"{context}: 표면 피드백 에셋을 읽지 못했습니다. 1단계를 먼저 실행하십시오.");
            return null;
        }

        if (!EditorUtility.IsPersistent(feedback))
        {
            log.Add($"{context}: 읽어 온 에셋이 영속 객체가 아닙니다.");
            return null;
        }

        return feedback;
    }

    private static bool Assign(EffectManager manager, SurfaceFeedbackSO feedback, List<string> log)
    {
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("m_defaultSurfaceFeedback");

        if (property == null)
        {
            log.Add("  진단: m_defaultSurfaceFeedback 프로퍼티를 찾지 못했습니다.");
            return false;
        }

        // 같은 값이어도 다시 씁니다. 씬을 열 때 메모리에만 채워진 경우 건너뛰면 디스크에 남지 않습니다.
        property.objectReferenceValue = feedback;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);

        // 대입이 실제로 남았는지 되읽어 확인합니다. 비영속 객체를 대입하면 예외 없이 조용히 null이 되며,
        // 이 확인이 없으면 "물렸다"는 로그만 보고 넘어가게 됩니다.
        SerializedObject readback = new SerializedObject(manager);

        if (readback.FindProperty("m_defaultSurfaceFeedback").objectReferenceValue == null)
        {
            log.Add($"  경고: 대입이 남지 않았습니다. persistent={EditorUtility.IsPersistent(feedback)}");
            return false;
        }

        return true;
    }

    private static Material BuildUnlitMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        // URP 프로젝트라 Unlit을 먼저 찾고, 못 찾으면 내장 셰이더로 물러섭니다.
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetFullPath(path));
        AssetDatabase.Refresh();
    }
}
