using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shelter_DefensiveBattle_Level 의 MountainForest-Terrain2 를
/// 500x500 -> 1000x1000 으로 확장한다 (+X / +Z 방향).
///
/// 형태 무손실 원리
///   - 샘플 간격 500/512 = 0.9765625 m 를 유지하기 위해
///     Size 500->1000 과 HeightmapResolution 513->1025 를 동시에 2배로 올린다.
///   - Size.Y(600) 와 Transform 은 건드리지 않는다. 높이는 정규화 0~1 값이므로
///     같은 값 = 같은 월드 높이가 된다.
///   - 원본 최소 코너가 고정되므로 기존 영역은 새 배열의 [0..512, 0..512] 에
///     보간 없이 그대로 복사된다.
///   - 나무는 정규화 좌표이므로 XZ 에 0.5 를 곱한다. (IEEE754 에서 지수만 -1 → 오차 0)
///   - 스플랫맵은 해상도를 2배로 올리고 기존 텍셀을 1:1 복사한다. (텍셀 실면적 동일)
///   - 풀(디테일 레이어)은 요청에 따라 제거한다.
///
/// 새로 늘어난 영역은 월드 Y=194.0 의 완전 평지이며, 이음매는 블렌딩 없이 단차 그대로 둔다.
/// </summary>
public static class TerrainExpand1000
{
    const string ScenePath = "Assets/0.Scenes/YeongHae/NewScenes/Shelter_DefensiveBattle_Level.unity";
    const string SourceTerrainName = "MountainForest-Terrain2";

    const float TargetSize = 1000f;
    const float FlatWorldY = 194.0f;

    // 사전 검증용 기대값 (다르면 중단한다)
    const int ExpectedRes = 513;
    const float ExpectedSize = 500f;

    static readonly StringBuilder Report = new StringBuilder();

    [MenuItem("GrayZone/Terrain/Expand To 1000x1000 (Flat, +X +Z)")]
    public static void MenuApply()
    {
        try
        {
            Apply();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Flush();
            EditorUtility.DisplayDialog("Terrain Expand 1000",
                "완료되었습니다.\n리포트: terrain_expand_1000_report.log", "확인");
        }
        catch (Exception e)
        {
            Report.AppendLine("FAILED: " + e);
            Flush();
            Debug.LogException(e);
            throw;
        }
    }

    // Unity 를 닫은 상태에서 -executeMethod TerrainExpand1000.BatchApply
    public static void BatchApply()
    {
        int exit = 0;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Apply();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Report.AppendLine("FAILED: " + e);
            Debug.LogException(e);
            exit = 1;
        }
        Flush();
        EditorApplication.Exit(exit);
    }

    static void Apply()
    {
        Report.Clear();
        Report.AppendLine("=== Terrain Expand 500x500 -> 1000x1000 (+X/+Z, flat Y=194) ===");
        Report.AppendLine("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        Terrain terrain = FindSourceTerrain();
        TerrainData data = terrain.terrainData;
        Vector3 tfPos = terrain.transform.position;
        Vector3 oldSize = data.size;
        int oldRes = data.heightmapResolution;

        Report.AppendLine("asset            : " + AssetDatabase.GetAssetPath(data));
        Report.AppendLine($"transform        : {F(tfPos)}  scale {F(terrain.transform.localScale)}");
        Report.AppendLine($"old size         : {F(oldSize)}");
        Report.AppendLine($"old heightmap res: {oldRes}");
        Report.AppendLine($"old alphamap     : {data.alphamapResolution} x {data.alphamapLayers} layers");
        Report.AppendLine($"old detail       : res {data.detailResolution}, prototypes {data.detailPrototypes.Length}");
        Report.AppendLine($"old trees        : {data.treeInstances.Length} instances, {data.treePrototypes.Length} prototypes");

        // ---- 사전 검증 -------------------------------------------------
        if (oldRes != ExpectedRes)
            throw new InvalidOperationException($"heightmapResolution 이 {ExpectedRes} 가 아닙니다: {oldRes}");
        if (!Mathf.Approximately(oldSize.x, ExpectedSize) || !Mathf.Approximately(oldSize.z, ExpectedSize))
            throw new InvalidOperationException($"터레인 크기가 {ExpectedSize} x {ExpectedSize} 가 아닙니다: {F(oldSize)}");
        if (terrain.transform.localScale != Vector3.one)
            throw new InvalidOperationException("Terrain Transform 의 Scale 이 1 이 아닙니다. 중단합니다.");

        int newRes = (oldRes - 1) * 2 + 1;                  // 513 -> 1025
        float spacingOld = oldSize.x / (oldRes - 1);
        float spacingNew = TargetSize / (newRes - 1);
        Report.AppendLine($"new heightmap res: {newRes}");
        Report.AppendLine($"spacing          : {spacingOld:0.########} -> {spacingNew:0.########}");
        if (Mathf.Abs(spacingOld - spacingNew) > 1e-7f)
            throw new InvalidOperationException("샘플 간격이 보존되지 않습니다. 중단합니다.");

        // ---- 원본 데이터 캡처 -------------------------------------------
        float[,] oldHeights = data.GetHeights(0, 0, oldRes, oldRes);
        bool[,] oldHoles = data.GetHoles(0, 0, oldRes - 1, oldRes - 1);

        int oldAlphaRes = data.alphamapResolution;
        int layerCount = data.alphamapLayers;
        TerrainLayer[] layers = data.terrainLayers;
        float[,,] oldAlpha = layerCount > 0 ? data.GetAlphamaps(0, 0, oldAlphaRes, oldAlphaRes) : null;

        TreeInstance[] oldTrees = data.treeInstances;
        Vector3[] treeWorldBefore = new Vector3[oldTrees.Length];
        for (int i = 0; i < oldTrees.Length; i++)
            treeWorldBefore[i] = tfPos + Vector3.Scale(oldTrees[i].position, oldSize);

        // 평지의 정규화 높이 (Size.Y 와 Transform.Y 는 바뀌지 않으므로 그대로 쓸 수 있다)
        float flatNorm = (FlatWorldY - tfPos.y) / oldSize.y;
        if (flatNorm < 0f || flatNorm > 1f)
            throw new InvalidOperationException($"평지 높이 {FlatWorldY} 가 터레인 높이 범위를 벗어납니다.");
        Report.AppendLine($"flat world Y     : {FlatWorldY}  (normalized {flatNorm:0.########})");

        // ---- 리사이즈 ---------------------------------------------------
        Undo.RegisterCompleteObjectUndo(data, "Expand Terrain To 1000");

        data.heightmapResolution = newRes;                  // 높이 데이터가 초기화된다
        data.size = new Vector3(TargetSize, oldSize.y, TargetSize);

        // ---- 높이: 새 영역은 평지, 기존 영역은 보간 없이 복사 -------------
        float[,] newHeights = new float[newRes, newRes];
        for (int y = 0; y < newRes; y++)
            for (int x = 0; x < newRes; x++)
                newHeights[y, x] = flatNorm;

        for (int y = 0; y < oldRes; y++)
            for (int x = 0; x < oldRes; x++)
                newHeights[y, x] = oldHeights[y, x];        // [0..512, 0..512] = 원본

        data.SetHeights(0, 0, newHeights);
        data.SyncHeightmap();

        // ---- 홀 -----------------------------------------------------------
        int newHoleRes = newRes - 1;
        bool[,] newHoles = new bool[newHoleRes, newHoleRes];
        for (int y = 0; y < newHoleRes; y++)
            for (int x = 0; x < newHoleRes; x++)
                newHoles[y, x] = true;
        for (int y = 0; y < oldRes - 1; y++)
            for (int x = 0; x < oldRes - 1; x++)
                newHoles[y, x] = oldHoles[y, x];
        data.SetHoles(0, 0, newHoles);

        // ---- 스플랫맵 -------------------------------------------------------
        // Unity 터레인 컨트롤 맵은 (uv*(N-1)+0.5)/N 으로 샘플링된다. 즉 텍셀 i 는
        // 정규화 i/(N-1) 위치에 정점 정렬된다. 따라서 텍셀이 정확히 겹치려면
        // 새 해상도는 2N 이 아니라 2N-1 이어야 한다 (512 -> 1023).
        if (layerCount > 0)
        {
            int desiredAlphaRes = oldAlphaRes * 2 - 1;
            data.terrainLayers = layers;
            data.alphamapResolution = desiredAlphaRes;
            int newAlphaRes = data.alphamapResolution;   // Unity 가 보정할 수 있으므로 되읽는다
            if (data.baseMapResolution * 2 <= 2048)
                data.baseMapResolution = data.baseMapResolution * 2;

            float[] fill = EdgeAlphaAverage(oldAlpha, oldAlphaRes, layerCount);

            // 새 텍셀 j -> 월드 오프셋 j/(N'-1)*1000 -> 구 텍셀 좌표 u
            float step = 2f * (oldAlphaRes - 1) / (newAlphaRes - 1);
            float limit = oldAlphaRes - 1;
            float[,,] newAlpha = new float[newAlphaRes, newAlphaRes, layerCount];
            float[] tmp = new float[layerCount];
            int exact = 0, resampled = 0;

            for (int y = 0; y < newAlphaRes; y++)
            {
                float uy = y * step;
                for (int x = 0; x < newAlphaRes; x++)
                {
                    float ux = x * step;
                    if (ux <= limit + 1e-4f && uy <= limit + 1e-4f)
                    {
                        if (IsWhole(ux) && IsWhole(uy)) exact++; else resampled++;
                        SampleBilinear(oldAlpha, oldAlphaRes, layerCount, ux, uy, tmp);
                        for (int l = 0; l < layerCount; l++) newAlpha[y, x, l] = tmp[l];
                    }
                    else
                    {
                        for (int l = 0; l < layerCount; l++) newAlpha[y, x, l] = fill[l];
                    }
                }
            }

            data.SetAlphamaps(0, 0, newAlpha);
            Report.AppendLine($"alphamap         : {oldAlphaRes} -> requested {desiredAlphaRes}, actual {newAlphaRes}");
            Report.AppendLine($"  existing area  : {exact} texels exact copy, {resampled} resampled");
            Report.AppendLine("  new area splat : " + string.Join(", ", Array.ConvertAll(fill, v => v.ToString("0.###"))));
        }

        // ---- 풀(디테일) 제거 -----------------------------------------------
        int removedDetail = data.detailPrototypes.Length;
        data.detailPrototypes = new DetailPrototype[0];
        data.SetDetailResolution(64, 8);
        Report.AppendLine($"detail(grass)    : removed {removedDetail} prototypes");

        // ---- 나무: 정규화 XZ 를 0.5 배 ---------------------------------------
        float ratioX = oldSize.x / TargetSize;              // 0.5
        float ratioZ = oldSize.z / TargetSize;              // 0.5
        TreeInstance[] newTrees = new TreeInstance[oldTrees.Length];
        for (int i = 0; i < oldTrees.Length; i++)
        {
            TreeInstance t = oldTrees[i];
            Vector3 p = t.position;
            t.position = new Vector3(p.x * ratioX, p.y, p.z * ratioZ);
            newTrees[i] = t;
        }
        data.treeInstances = newTrees;
        Report.AppendLine($"trees            : {newTrees.Length} instances remapped (x{ratioX}, y unchanged, z{ratioZ})");

        terrain.Flush();
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);

        // ---- 검증 -------------------------------------------------------
        Report.AppendLine("--- verification ---");
        Report.AppendLine($"final size       : {F(data.size)}");
        Report.AppendLine($"final transform  : {F(terrain.transform.position)}  (unchanged: {terrain.transform.position == tfPos})");

        float[,] check = data.GetHeights(0, 0, oldRes, oldRes);
        double maxDiffNorm = 0;
        int diffCount = 0;
        for (int y = 0; y < oldRes; y++)
            for (int x = 0; x < oldRes; x++)
            {
                double d = Math.Abs(check[y, x] - oldHeights[y, x]);
                if (d > 0) diffCount++;
                if (d > maxDiffNorm) maxDiffNorm = d;
            }
        Report.AppendLine($"height samples   : {oldRes * oldRes} compared, {diffCount} differ, " +
                          $"max delta {maxDiffNorm:E3} normalized = {maxDiffNorm * oldSize.y:0.########} m");

        TreeInstance[] after = data.treeInstances;
        double maxTreeDelta = 0;
        if (after.Length == treeWorldBefore.Length)
        {
            for (int i = 0; i < after.Length; i++)
            {
                Vector3 w = tfPos + Vector3.Scale(after[i].position, data.size);
                double d = Vector3.Distance(w, treeWorldBefore[i]);
                if (d > maxTreeDelta) maxTreeDelta = d;
            }
            Report.AppendLine($"tree world delta : max {maxTreeDelta:0.########} m over {after.Length} instances");
        }
        else
        {
            Report.AppendLine($"tree count changed: {treeWorldBefore.Length} -> {after.Length} (Unity reordered/clamped)");
        }

        float minX = tfPos.x, minZ = tfPos.z;
        Report.AppendLine($"footprint        : X {minX} ~ {minX + TargetSize}, Z {minZ} ~ {minZ + TargetSize}");
        Report.AppendLine("=== done ===");
    }

    static bool IsWhole(float v) => Mathf.Abs(v - Mathf.Round(v)) < 1e-4f;

    /// <summary>구 스플랫맵을 실수 텍셀 좌표에서 바이리니어 샘플링한다.</summary>
    static void SampleBilinear(float[,,] src, int res, int layers, float ux, float uy, float[] dst)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(ux), 0, res - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(uy), 0, res - 1);
        int x1 = Mathf.Min(x0 + 1, res - 1);
        int y1 = Mathf.Min(y0 + 1, res - 1);
        float fx = Mathf.Clamp01(ux - x0);
        float fy = Mathf.Clamp01(uy - y0);

        float sum = 0f;
        for (int l = 0; l < layers; l++)
        {
            float a = Mathf.Lerp(src[y0, x0, l], src[y0, x1, l], fx);
            float b = Mathf.Lerp(src[y1, x0, l], src[y1, x1, l], fx);
            dst[l] = Mathf.Lerp(a, b, fy);
            sum += dst[l];
        }
        if (sum > 0.0001f)
            for (int l = 0; l < layers; l++) dst[l] /= sum;
    }

    /// <summary>새 영역에 칠할 색. +X / +Z 가장자리 텍셀의 평균 가중치를 쓴다.</summary>
    static float[] EdgeAlphaAverage(float[,,] alpha, int res, int layers)
    {
        float[] acc = new float[layers];
        int n = 0;
        for (int i = 0; i < res; i++)
        {
            for (int l = 0; l < layers; l++)
            {
                acc[l] += alpha[res - 1, i, l];   // Z max 가장자리
                acc[l] += alpha[i, res - 1, l];   // X max 가장자리
            }
            n += 2;
        }

        float sum = 0f;
        for (int l = 0; l < layers; l++) { acc[l] /= n; sum += acc[l]; }
        if (sum <= 0.0001f) { acc[0] = 1f; return acc; }
        for (int l = 0; l < layers; l++) acc[l] /= sum;
        return acc;
    }

    static Terrain FindSourceTerrain()
    {
        foreach (Terrain t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == SourceTerrainName && t.gameObject.activeInHierarchy && t.terrainData != null)
                return t;
        }
        throw new InvalidOperationException($"활성 상태의 '{SourceTerrainName}' 터레인을 찾지 못했습니다.");
    }

    static string F(Vector3 v) => $"({v.x}, {v.y}, {v.z})";

    static void Flush()
    {
        string log = Path.GetFullPath(Path.Combine(Application.dataPath, "../../terrain_expand_1000_report.log"));
        File.WriteAllText(log, Report.ToString(), new UTF8Encoding(false));
        Debug.Log(Report.ToString());
    }
}
