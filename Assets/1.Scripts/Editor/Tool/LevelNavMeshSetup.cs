using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 레벨 프리팹의 NavMesh 생성 규칙을 정리하고 씬에서 다시 굽는 에디터 도구입니다.
/// </summary>
/// <remarks>
/// 이 맵은 <see cref="NavMeshSurface"/>를 쓰는 AI Navigation 방식으로 만들어졌습니다.
/// 그쪽은 Navigation Static 표시를 보지 않고, 오브젝트별 예외를 <see cref="NavMeshModifier"/>로 정합니다.
/// 그래서 지붕 제외 규칙을 프리팹 안에 넣어 두면 어느 씬에 배치하든 같은 결과가 나옵니다.
///
/// 건물을 굽기에서 빼지 않고 <c>Not Walkable</c>로 두는 것이 중요합니다.
/// 빼 버리면 NavMesh가 건물을 아예 모르게 되어 변이체가 벽을 통과하는 경로를 잡습니다.
/// Not Walkable은 "막되 올라갈 수는 없음"이라 두 가지를 함께 만족합니다.
/// </remarks>
public static class LevelNavMeshSetup
{
    private const string PrefabPath = "Assets/2.Prefabs/Level/LEVEL_GRAYZONE_COMBAT_5TO6MIN_V02.prefab";
    private const string LegacyNavMeshPath = "Assets/0.Scenes/JangHu/CombatPlayTest/NavMesh.asset";

    /// <summary>걸어 다닐 수 없는 영역의 번호입니다.</summary>
    private const int NotWalkableArea = 1;

    /// <summary>올라갈 수 없어야 하는 그룹입니다. 이름 일부로 찾습니다.</summary>
    private static readonly string[] BlockingGroups = { "BUILDINGS", "BOUNDARY", "HARD_BORDER" };

    [MenuItem("GrayZone/Level/NavMesh 규칙 정리 및 굽기")]
    public static void Setup()
    {
        List<string> log = new List<string>();

        ConfigurePrefab(log);
        ClearLegacyArtifacts(log);
        Bake(log);

        Debug.Log("[LevelNavMeshSetup] 완료\n" + string.Join("\n", log));
    }

    /// <summary>프리팹에 지붕 제외 규칙을 넣습니다.</summary>
    /// <remarks>
    /// <see cref="NavMeshModifier"/>는 붙은 오브젝트와 그 자식 전체에 적용됩니다.
    /// 그래서 건물 하나하나가 아니라 그룹 하나에만 붙이면 됩니다.
    /// </remarks>
    private static void ConfigurePrefab(List<string> log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            log.Add($"프리팹을 찾지 못했습니다: {PrefabPath}");
            return;
        }

        try
        {
            foreach (string groupName in BlockingGroups)
            {
                Transform group = FindChildContaining(root.transform, groupName);
                if (group == null)
                {
                    log.Add($"{groupName} 그룹을 찾지 못했습니다.");
                    continue;
                }

                NavMeshModifier modifier = group.GetComponent<NavMeshModifier>();
                if (modifier == null)
                {
                    modifier = group.gameObject.AddComponent<NavMeshModifier>();
                }

                modifier.overrideArea = true;
                modifier.area = NotWalkableArea;
                modifier.ignoreFromBuild = false;

                log.Add($"{group.name}: Not Walkable 규칙을 넣었습니다. (자식 포함)");
            }

            // 레거시 표시는 새 방식이 보지 않습니다. 남겨 두면 어느 쪽이 적용되는지 헷갈립니다.
            int cleared = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                if ((flags & StaticEditorFlags.NavigationStatic) == 0)
                {
                    continue;
                }

                // 다른 표시(라이트맵·오클루전)는 건드리지 않고 내비게이션 비트만 내립니다.
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~StaticEditorFlags.NavigationStatic);
                cleared++;
            }

            log.Add($"레거시 Navigation Static 표시 {cleared}개를 정리했습니다.");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>레거시 방식으로 구운 결과물을 지웁니다.</summary>
    private static void ClearLegacyArtifacts(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(LegacyNavMeshPath) == null)
        {
            log.Add("레거시 NavMesh 에셋이 이미 없습니다.");
            return;
        }

        AssetDatabase.DeleteAsset(LegacyNavMeshPath);
        log.Add($"레거시 NavMesh 에셋을 지웠습니다: {LegacyNavMeshPath}");
    }

    /// <summary>씬에 배치된 표면을 다시 굽고 결과를 확인합니다.</summary>
    private static void Bake(List<string> log)
    {
        NavMeshSurface[] surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
        if (surfaces.Length == 0)
        {
            log.Add("씬에서 NavMeshSurface를 찾지 못했습니다. 맵이 배치되어 있는지 확인하세요.");
            return;
        }

        foreach (NavMeshSurface surface in surfaces)
        {
            surface.BuildNavMesh();
            PersistNavMeshData(surface, log);
            EditorUtility.SetDirty(surface);
        }

        AssetDatabase.SaveAssets();

        UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
        int above = 0;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (Vector3 v in tri.vertices)
        {
            if (v.y < minY) minY = v.y;
            if (v.y > maxY) maxY = v.y;
            if (v.y > 3f) above++;
        }

        log.Add($"결과: 정점 {tri.vertices.Length} / 삼각형 {tri.indices.Length / 3}");
        log.Add($"높이 범위 {minY:F2} ~ {maxY:F2}, 3m 위 정점 {above}개(지붕에 깔렸는지 확인용)");
    }

    /// <summary>
    /// 구운 결과를 에셋 파일로 저장합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="NavMeshSurface.BuildNavMesh"/>만 부르면 결과가 메모리에만 남습니다.
    /// 그 상태에서는 지금 당장 길찾기가 되지만 에디터를 다시 열면 사라집니다.
    /// 인스펙터의 Bake 버튼은 굽기와 저장을 함께 하므로 이 단계가 필요합니다.
    ///
    /// 저장 위치와 이름은 유니티가 쓰는 규칙을 그대로 따릅니다.
    /// 씬 이름과 같은 폴더 안에 <c>NavMesh-표면이름.asset</c>으로 둡니다.
    /// 같은 경로에 이미 있으면 덮어쓰므로 여러 번 실행해도 파일이 늘지 않습니다.
    /// </remarks>
    private static void PersistNavMeshData(NavMeshSurface surface, List<string> log)
    {
        UnityEngine.AI.NavMeshData data = surface.navMeshData;
        if (data == null)
        {
            log.Add($"{surface.name}: 구운 결과가 없습니다.");
            return;
        }

        string scenePath = surface.gameObject.scene.path;
        if (string.IsNullOrEmpty(scenePath))
        {
            log.Add($"{surface.name}: 씬이 저장되지 않아 결과를 파일로 남기지 못했습니다.");
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/');
        string folderName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
        string folder = parent + "/" + folderName;

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder(parent, folderName);
        }

        string assetPath = folder + "/NavMesh-" + surface.name + ".asset";
        AssetDatabase.CreateAsset(data, assetPath);

        log.Add($"{surface.name}: 구운 뒤 파일로 저장했습니다. {assetPath}");
    }

    /// <summary>이름에 지정한 문자열이 들어간 자식을 찾습니다.</summary>
    private static Transform FindChildContaining(Transform parent, string keyword)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains(keyword))
            {
                return child;
            }
        }

        return null;
    }
}
