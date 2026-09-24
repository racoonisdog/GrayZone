using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// MountainForest-Terrain2 를 500x500 -> 1000x1000 으로 확장한다. (+X 500, +Z 500)
/// 샘플 간격(0.9765625m)과 Size.Y(600), Transform 을 고정하므로 기존 지형/나무는 월드 좌표가 그대로 보존된다.
/// 새로 생긴 영역은 Y=194.0 완전 평지이며 이음매 단차는 의도적으로 남긴다.
/// </summary>
public static class GrayZoneTerrainExpand1000
{
    const string ScenePath = "Assets/0.Scenes/YeongHae/NewScenes/Shelter_DefensiveBattle_Level.unity";
    const string TerrainObjectName = "MountainForest-Terrain2";

    const int SrcRes = 513;
    const int DstRes = 1025;
    const float SrcSizeXZ = 500f;
    const float DstSizeXZ = 1000f;
    const float SizeY = 600f;
    const float FlatWorldY = 194.0f;

    [MenuItem("Tools/Codex/Expand Terrain To 1000x1000 (+X,+Z)")]
    public static void RunFromMenu()
    {
        string report;
        try
        {
            report = Execute(true);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Terrain Expand - FAILED", e.Message, "OK");
            return;
        }
        Debug.Log(report);
        EditorUtility.DisplayDialog("Terrain Expand - DONE", report, "OK");
    }

    // batchmode: -executeMethod GrayZoneTerrainExpand1000.RunBatch
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log(Execute(true));
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    static string Execute(bool saveScene)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== GrayZone Terrain Expand 500 -> 1000 ===");
        sb.AppendLine("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        Terrain terrain = null;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == TerrainObjectName) { terrain = t; break; }
        }
        if (terrain == null) throw new Exception("Terrain GameObject not found: " + TerrainObjectName);

        TerrainData data = terrain.terrainData;
        if (data == null) throw new Exception("TerrainData is null on " + TerrainObjectName);

        // ---------- 사전 검증: 예상과 다르면 아무것도 건드리지 않고 중단 ----------
        if (data.heightmapResolution != SrcRes)
            throw new Exception(string.Format("heightmapResolution mismatch: expected {0}, got {1}. ABORTED.", SrcRes, data.heightmapResolution));
        Vector3 size = data.size;
        if (Mathf.Abs(size.x - SrcSizeXZ) > 0.01f || Mathf.Abs(size.z - SrcSizeXZ) > 0.01f || Mathf.Abs(size.y - SizeY) > 0.01f)
            throw new Exception("TerrainData.size mismatch: expected (500,600,500), got " + size.ToString("F3") + ". ABORTED.");

        Vector3 origin = terrain.transform.position;
        sb.AppendLine("terrain origin (unchanged): " + origin.ToString("F5"));
        sb.AppendLine("size before: " + size.ToString("F3") + " / heightmapResolution " + data.heightmapResolution);

        // ---------- 스냅샷 ----------
        float[,] srcH = data.GetHeights(0, 0, SrcRes, SrcRes);

        int srcAlphaRes = data.alphamapResolution;
        int layerCount = data.alphamapLayers;
        float[,,] srcA = data.GetAlphamaps(0, 0, srcAlphaRes, srcAlphaRes);

        bool[,] srcHoles = null;
        try { srcHoles = data.GetHoles(0, 0, SrcRes - 1, SrcRes - 1); }
        catch (Exception e) { sb.AppendLine("holes read skipped: " + e.Message); }

        TreeInstance[] srcTrees = data.treeInstances;
        Vector3[] treeWorldBefore = new Vector3[srcTrees.Length];
        for (int i = 0; i < srcTrees.Length; i++)
        {
            Vector3 p = srcTrees[i].position;
            treeWorldBefore[i] = origin + new Vector3(p.x * SrcSizeXZ, p.y * SizeY, p.z * SrcSizeXZ);
        }

        int srcDetailProtoCount = data.detailPrototypes != null ? data.detailPrototypes.Length : 0;
        sb.AppendLine(string.Format("snapshot: trees {0}, alphamapRes {1}, layers {2}, detailPrototypes {3}",
            srcTrees.Length, srcAlphaRes, layerCount, srcDetailProtoCount));

        // ---------- 1. 풀/디테일 제거 ----------
        data.detailPrototypes = new DetailPrototype[0];

        // ---------- 2. 해상도 + 크기 확장 (샘플 간격 유지) ----------
        data.heightmapResolution = DstRes;
        data.size = new Vector3(DstSizeXZ, SizeY, DstSizeXZ);
        if (data.heightmapResolution != DstRes)
            throw new Exception("heightmapResolution did not become " + DstRes + " (got " + data.heightmapResolution + ")");

        // ---------- 3. 높이: 기존 513x513 을 최소 코너에 그대로 복사, 나머지는 평지 ----------
        float flatNorm = Mathf.Clamp01((FlatWorldY - origin.y) / SizeY);
        float[,] dstH = new float[DstRes, DstRes];
        for (int z = 0; z < DstRes; z++)
            for (int x = 0; x < DstRes; x++)
                dstH[z, x] = flatNorm;
        for (int z = 0; z < SrcRes; z++)
            for (int x = 0; x < SrcRes; x++)
                dstH[z, x] = srcH[z, x];
        data.SetHeights(0, 0, dstH);
        sb.AppendLine(string.Format("flat plane world Y = {0:F3} (normalized {1:F8})", FlatWorldY, flatNorm));

        // ---------- 4. 스플랫맵: 해상도 2배 + 1:1 복사, 신규 영역은 경계 평균 블렌드 ----------
        int dstAlphaRes = srcAlphaRes * 2;
        float[] edgeBlend = new float[layerCount];
        int edgeSamples = 0;
        for (int z = 0; z < srcAlphaRes; z++)
        {
            for (int l = 0; l < layerCount; l++) edgeBlend[l] += srcA[z, srcAlphaRes - 1, l];
            edgeSamples++;
        }
        for (int x = 0; x < srcAlphaRes; x++)
        {
            for (int l = 0; l < layerCount; l++) edgeBlend[l] += srcA[srcAlphaRes - 1, x, l];
            edgeSamples++;
        }
        float blendSum = 0f;
        for (int l = 0; l < layerCount; l++) { edgeBlend[l] /= Mathf.Max(1, edgeSamples); blendSum += edgeBlend[l]; }
        if (blendSum <= 0.0001f) { for (int l = 0; l < layerCount; l++) edgeBlend[l] = (l == 0) ? 1f : 0f; }
        else { for (int l = 0; l < layerCount; l++) edgeBlend[l] /= blendSum; }

        data.alphamapResolution = dstAlphaRes;
        float[,,] dstA = new float[dstAlphaRes, dstAlphaRes, layerCount];
        for (int z = 0; z < dstAlphaRes; z++)
            for (int x = 0; x < dstAlphaRes; x++)
                for (int l = 0; l < layerCount; l++)
                    dstA[z, x, l] = edgeBlend[l];
        for (int z = 0; z < srcAlphaRes; z++)
            for (int x = 0; x < srcAlphaRes; x++)
                for (int l = 0; l < layerCount; l++)
                    dstA[z, x, l] = srcA[z, x, l];
        data.SetAlphamaps(0, 0, dstA);
        if (data.baseMapResolution <= 1024) data.baseMapResolution = data.baseMapResolution * 2;
        sb.AppendLine(string.Format("alphamap {0} -> {1}, baseMapResolution {2}", srcAlphaRes, dstAlphaRes, data.baseMapResolution));

        // ---------- 5. 홀 ----------
        if (srcHoles != null)
        {
            int dstHoleRes = DstRes - 1;
            bool[,] dstHoles = new bool[dstHoleRes, dstHoleRes];
            for (int z = 0; z < dstHoleRes; z++)
                for (int x = 0; x < dstHoleRes; x++)
                    dstHoles[z, x] = true;
            for (int z = 0; z < SrcRes - 1; z++)
                for (int x = 0; x < SrcRes - 1; x++)
                    dstHoles[z, x] = srcHoles[z, x];
            data.SetHoles(0, 0, dstHoles);
        }

        // ---------- 6. 나무: 정규화 XZ x 0.5 (IEEE754 지수 연산이라 오차 0) ----------
        TreeInstance[] dstTrees = new TreeInstance[srcTrees.Length];
        for (int i = 0; i < srcTrees.Length; i++)
        {
            TreeInstance ti = srcTrees[i];
            Vector3 p = ti.position;
            p.x = p.x * (SrcSizeXZ / DstSizeXZ);
            p.z = p.z * (SrcSizeXZ / DstSizeXZ);
            ti.position = p;
            dstTrees[i] = ti;
        }
        data.treeInstances = dstTrees;

        // ---------- 7. 반영 ----------
        terrain.terrainData = data;
        TerrainCollider col = terrain.GetComponent<TerrainCollider>();
        if (col != null) col.terrainData = data;
        terrain.Flush();

        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        if (saveScene) EditorSceneManager.SaveScene(terrain.gameObject.scene);

        // ---------- 8. 검증 ----------
        sb.AppendLine("--- verification ---");
        sb.AppendLine("size after: " + data.size.ToString("F3") + " / heightmapResolution " + data.heightmapResolution);
        float spacing = data.size.x / (data.heightmapResolution - 1);
        sb.AppendLine(string.Format("sample spacing: {0:F7} (expected 0.9765625)", spacing));

        float[,] chk = data.GetHeights(0, 0, SrcRes, SrcRes);
        int diffCount = 0;
        float maxDiffNorm = 0f;
        for (int z = 0; z < SrcRes; z++)
            for (int x = 0; x < SrcRes; x++)
            {
                float d = Mathf.Abs(chk[z, x] - srcH[z, x]);
                if (d > 0f) { diffCount++; if (d > maxDiffNorm) maxDiffNorm = d; }
            }
        sb.AppendLine(string.Format("height diff in original 513x513: {0} samples differ, max {1:F6} m",
            diffCount, maxDiffNorm * SizeY));

        TreeInstance[] after = data.treeInstances;
        Vector3[] treeWorldAfter = new Vector3[after.Length];
        for (int i = 0; i < after.Length; i++)
        {
            Vector3 p = after[i].position;
            treeWorldAfter[i] = origin + new Vector3(p.x * DstSizeXZ, p.y * SizeY, p.z * DstSizeXZ);
        }
        Array.Sort(treeWorldBefore, CompareVec);
        Array.Sort(treeWorldAfter, CompareVec);
        float maxTreeDelta = -1f;
        if (treeWorldBefore.Length != treeWorldAfter.Length)
        {
            sb.AppendLine(string.Format("TREE COUNT CHANGED: {0} -> {1}", treeWorldBefore.Length, treeWorldAfter.Length));
        }
        else
        {
            maxTreeDelta = 0f;
            for (int i = 0; i < treeWorldAfter.Length; i++)
            {
                float d = Vector3.Distance(treeWorldBefore[i], treeWorldAfter[i]);
                if (d > maxTreeDelta) maxTreeDelta = d;
            }
            sb.AppendLine(string.Format("tree world position max delta: {0:F8} m  (count {1})", maxTreeDelta, after.Length));
        }

        bool ok = diffCount == 0 && Mathf.Abs(spacing - 0.9765625f) < 1e-6f && maxTreeDelta == 0f;
        sb.AppendLine(ok ? "RESULT: OK - original geometry preserved exactly" : "RESULT: CHECK THE NUMBERS ABOVE");

        string report = sb.ToString();
        try
        {
            string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../terrain_expand_1000_report.txt"));
            File.WriteAllText(logPath, report, Encoding.UTF8);
        }
        catch (Exception e) { Debug.LogWarning("report write failed: " + e.Message); }
        return report;
    }

    static int CompareVec(Vector3 a, Vector3 b)
    {
        int c = a.x.CompareTo(b.x); if (c != 0) return c;
        c = a.z.CompareTo(b.z); if (c != 0) return c;
        return a.y.CompareTo(b.y);
    }
}
