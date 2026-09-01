using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// FieldManager와 같은 GameObject에 붙어 있던 매니저들을 전용 자식 오브젝트로 옮기는 일회성 마이그레이션 도구입니다.
/// </summary>
/// <remarks>
/// <para>
/// 손으로 옮기지 않는 이유는 대상이 씬 2개와 프리팹 1개에 흩어져 있고, <see cref="EnemyManager"/>가
/// 종류별 시체 설정을 직렬화 값으로 들고 있기 때문입니다. 컴포넌트를 지우고 새로 붙이면 그 값이 사라집니다.
/// </para>
/// <para>
/// 값 보존은 <see cref="ComponentUtility.CopyComponent"/>와 <see cref="ComponentUtility.PasteComponentAsNew"/>에
/// 맡깁니다. 인스펙터의 복사·붙여넣기와 같은 경로라 직렬화 값이 그대로 넘어갑니다.
/// <c>SerializedObject</c>로 필드를 하나씩 옮기면 새로 생긴 필드나 중첩 배열을 빠뜨리기 쉽습니다.
/// </para>
/// <para>
/// 실행 전에 대상 파일을 프로젝트 루트 <c>Backup/FieldManagerMigration</c>으로 복사합니다.
/// 씬과 프리팹은 되돌릴 지점이 없으면 손으로 다시 배선해야 합니다.
/// </para>
/// </remarks>
public static class FieldManagerChildMigration
{
    /// <summary>
    /// 이관 대상 후보입니다. 없는 경로는 건너뜁니다.
    /// </summary>
    /// <remarks>
    /// 존재 여부를 확인하는 이유는 씬 파일 이름이 실제로 바뀐 적이 있기 때문입니다.
    /// 없는 경로를 그대로 열면 도구 전체가 멈춰, 살아 있는 대상까지 처리하지 못합니다.
    /// </remarks>
    private static readonly string[] TargetCandidates =
    {
        "Assets/0.Scenes/JangHu/CombatPlayTest.unity",
        "Assets/0.Scenes/JangHu/CombatPlayTest 1.unity",
        "Assets/2.Prefabs/Manager/FieldManager.prefab",
    };

    /// <summary>후보 중 실제로 있는 것만 돌려줍니다.</summary>
    private static List<string> ResolveTargets(List<string> log)
    {
        List<string> found = new List<string>();

        foreach (string path in TargetCandidates)
        {
            if (File.Exists(Path.GetFullPath(path)))
            {
                found.Add(path);
            }
            else
            {
                log.Add($"대상 없음(건너뜀): {path}");
            }
        }

        return found;
    }

    /// <summary>옮길 매니저 하나와 그것이 들어갈 자식 오브젝트 이름입니다.</summary>
    private readonly struct ManagerSlot
    {
        public readonly Type Type;
        public readonly string ChildName;

        public ManagerSlot(Type type, string childName)
        {
            Type = type;
            ChildName = childName;
        }
    }

    private static readonly ManagerSlot[] Slots =
    {
        new ManagerSlot(typeof(EffectManager), "EffectManager"),
        new ManagerSlot(typeof(AudioManager), "AudioManager"),
        new ManagerSlot(typeof(EnemyManager), "EnemyManager"),
    };

    [MenuItem("GrayZone/Field/FieldManager 매니저를 자식 오브젝트로 이관")]
    public static void Migrate()
    {
        List<string> log = new List<string>();

        List<string> targets = ResolveTargets(log);

        if (!TryBackupTargets(targets, log))
        {
            Debug.LogError("[FieldManagerChildMigration] 백업 실패로 중단했습니다.\n" + string.Join("\n", log));
            return;
        }

        foreach (string path in targets)
        {
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                MigratePrefab(path, log);
            }
            else
            {
                MigrateScene(path, log);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 콘솔은 첫 줄만 보여 주는 경로가 있어 전체를 파일로도 남깁니다.
        try
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string directory = Path.Combine(root, "Backup", "FieldManagerMigration");
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "last-log.txt"), log);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[FieldManagerChildMigration] 로그 파일 기록 실패: {exception.Message}");
        }

        Debug.Log("[FieldManagerChildMigration] 완료\n" + string.Join("\n", log));
    }

    /// <summary>대상 파일을 프로젝트 루트 Backup 아래로 복사합니다.</summary>
    private static bool TryBackupTargets(List<string> targets, List<string> log)
    {
        try
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string directory = Path.Combine(root, "Backup", "FieldManagerMigration", stamp);
            Directory.CreateDirectory(directory);

            foreach (string path in targets)
            {
                File.Copy(Path.GetFullPath(path), Path.Combine(directory, Path.GetFileName(path)), true);
            }

            log.Add($"백업했습니다: {directory}");
            return true;
        }
        catch (Exception exception)
        {
            log.Add($"백업 실패: {exception.Message}");
            return false;
        }
    }

    private static void MigrateScene(string path, List<string> log)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        bool changed = false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (FieldManager fieldManager in root.GetComponentsInChildren<FieldManager>(true))
            {
                changed |= MigrateOne(fieldManager, path, log);
            }
        }

        if (!changed)
        {
            log.Add($"{path}: 바꿀 것이 없었습니다.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add($"{path}: 저장했습니다.");
    }

    private static void MigratePrefab(string path, List<string> log)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(path);

        try
        {
            bool changed = false;

            foreach (FieldManager fieldManager in contents.GetComponentsInChildren<FieldManager>(true))
            {
                changed |= MigrateOne(fieldManager, path, log);
            }

            if (!changed)
            {
                log.Add($"{path}: 바꿀 것이 없었습니다.");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            log.Add($"{path}: 저장했습니다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>FieldManager 하나를 자식 오브젝트 구조로 바꿉니다.</summary>
    /// <returns>무언가 바뀌었으면 true입니다.</returns>
    private static bool MigrateOne(FieldManager fieldManager, string context, List<string> log)
    {
        GameObject host = fieldManager.gameObject;
        bool changed = RemoveMissingScripts(host, context, log);

        foreach (ManagerSlot slot in Slots)
        {
            Transform child = host.transform.Find(slot.ChildName);

            if (child == null)
            {
                GameObject created = new GameObject(slot.ChildName);
                created.transform.SetParent(host.transform, false);
                child = created.transform;
                log.Add($"{context}: 자식 '{slot.ChildName}'을 만들었습니다.");
                changed = true;
            }

            Component existingOnChild = child.GetComponent(slot.Type);
            Component onHost = host.GetComponent(slot.Type);

            if (existingOnChild != null)
            {
                // 이미 옮겨져 있습니다. 부모에 남은 중복만 걷어냅니다.
                if (onHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(onHost, true);
                    log.Add($"{context}: 부모에 남아 있던 중복 {slot.Type.Name}을 제거했습니다.");
                    changed = true;
                }

                continue;
            }

            if (onHost != null)
            {
                // 인스펙터 복사·붙여넣기와 같은 경로라 직렬화 값이 그대로 넘어갑니다.
                ComponentUtility.CopyComponent(onHost);
                ComponentUtility.PasteComponentAsNew(child.gameObject);
                UnityEngine.Object.DestroyImmediate(onHost, true);
                log.Add($"{context}: {slot.Type.Name}을 값과 함께 '{slot.ChildName}'으로 옮겼습니다.");
            }
            else
            {
                child.gameObject.AddComponent(slot.Type);
                log.Add($"{context}: {slot.Type.Name}이 없어 '{slot.ChildName}'에 새로 붙였습니다(기본값).");
            }

            changed = true;
        }

        // 참조 연결도 변경으로 셉니다. 이것만 바뀐 경우에 저장을 건너뛰면 다시 실행해도 계속 비어 있습니다.
        changed |= RewireReferences(fieldManager, context, log);

        return changed;
    }

    /// <summary>
    /// FieldManager의 매니저 참조를 옮긴 자식 컴포넌트로 다시 잡습니다.
    /// </summary>
    /// <remarks>
    /// <see cref="EditorUtility.SetDirty"/>만으로는 채워지지 않습니다. <c>OnValidate</c>가 저장 시점에
    /// 맞춰 도는 보장이 없어, 실측에서 세 참조가 모두 <c>fileID: 0</c>으로 저장됐습니다.
    ///
    /// 런타임에는 <c>Awake</c>가 자식에서 찾아 채우므로 비어 있어도 동작은 합니다. 그래도 씬에 적어 두는
    /// 이유는 인스펙터에서 비어 보이면 배선이 빠진 것으로 오해하기 때문입니다.
    /// </remarks>
    /// <returns>참조를 하나라도 새로 잡았으면 true입니다.</returns>
    private static bool RewireReferences(FieldManager fieldManager, string context, List<string> log)
    {
        SerializedObject serialized = new SerializedObject(fieldManager);

        (string Field, Type Type)[] bindings =
        {
            ("m_effectManager", typeof(EffectManager)),
            ("m_audioManager", typeof(AudioManager)),
            ("m_enemyManager", typeof(EnemyManager)),
        };

        int wired = 0;

        foreach ((string field, Type type) in bindings)
        {
            SerializedProperty property = serialized.FindProperty(field);

            if (property == null)
            {
                log.Add($"{context}: 필드 '{field}'를 찾지 못했습니다.");
                continue;
            }

            Component target = fieldManager.GetComponentInChildren(type, true);

            if (target == null)
            {
                continue;
            }

            // 이미 같은 값이어도 다시 씁니다. 씬을 열 때 OnValidate가 메모리에서만 채워 두기 때문에,
            // "값이 같으니 건너뛴다"로 판단하면 디스크에는 계속 비어 있는 채로 남습니다.
            // 실측에서 이 최적화 때문에 씬 두 곳이 fileID 0으로 저장됐습니다.
            property.objectReferenceValue = target;
            wired++;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(fieldManager);

        log.Add($"{context}: FieldManager 참조 {wired}개를 자식으로 연결했습니다.");
        return wired > 0;
    }

    /// <summary>
    /// 스크립트가 사라져 남은 빈 컴포넌트 슬롯을 걷어냅니다.
    /// </summary>
    /// <remarks>
    /// 구 <c>SurfaceFeedbackSystem</c>을 삭제해 그 자리가 Missing Script로 남습니다. 그대로 두면
    /// 인스펙터에 경고가 계속 뜨고, 나중에 이 오브젝트를 손댈 때 원인을 다시 조사하게 됩니다.
    /// </remarks>
    private static bool RemoveMissingScripts(GameObject host, string context, List<string> log)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(host);

        if (count <= 0)
        {
            return false;
        }

        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(host);
        log.Add($"{context}: 스크립트가 사라진 컴포넌트 {count}개를 제거했습니다.");
        return true;
    }
}
