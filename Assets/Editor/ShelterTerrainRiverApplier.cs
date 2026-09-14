using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShelterTerrainRiverApplier
{
    private const string ExpectedScene = "Shelter_Grey_Boxing_BackUp_3";
    private const string GeneratedFolder = "Assets/Generated/ShelterTerrain";
    private const string RiverMeshPath = GeneratedFolder + "/Codex_ShelterRiver_Mesh.asset";
    private const string RiverObjectName = "Codex_NaturalRiver";
    private const string WaterMaterialPath = "Assets/Top_Down_Post-Apocalyptic_Pack/Materials/TD_Water.mat";

    private static readonly Vector2 MapCenter = new Vector2(5f, 2f);
    private static readonly Vector2 Up = new Vector2(1f, 0.60f).normalized;
    private static readonly Vector2 Right = new Vector2(Up.y, -Up.x);

    [MenuItem("Tools/Codex/Apply Shelter Mountain And River Terrain")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != ExpectedScene)
            throw new InvalidOperationException("Open " + ExpectedScene + " before applying the terrain concept.");

        var terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault();
        if (terrain == null || terrain.terrainData == null)
            throw new InvalidOperationException("The active scene does not contain a valid Terrain.");

        CreateBackups(scene.path, terrain.terrainData);
        SculptTerrain(terrain);
        CreateOrUpdateRiver(terrain);

        terrain.Flush();
        EditorUtility.SetDirty(terrain.terrainData);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = terrain.gameObject;
        Debug.Log("[ShelterTerrainRiverApplier] Applied asymmetric mountain enclosure, two empty terraces, and the upper natural river. Shelter and bridge corridors were preserved.");
    }

    private static void CreateBackups(string scenePath, TerrainData terrainData)
    {
        const string backupFolder = GeneratedFolder + "/Backups";
        EnsureFolder(backupFolder);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string sceneBackup = backupFolder + "/" + Path.GetFileNameWithoutExtension(scenePath) + "_before_mountain_river_" + stamp + ".unity";
        string terrainBackup = backupFolder + "/" + terrainData.name + "_before_mountain_river_" + stamp + ".asset";

        if (!AssetDatabase.CopyAsset(scenePath, sceneBackup))
            throw new IOException("Could not create scene backup at " + sceneBackup);

        string terrainPath = AssetDatabase.GetAssetPath(terrainData);
        if (!AssetDatabase.CopyAsset(terrainPath, terrainBackup))
            throw new IOException("Could not create TerrainData backup at " + terrainBackup);

        Debug.Log("[ShelterTerrainRiverApplier] Backups: " + sceneBackup + " | " + terrainBackup);
    }

    private static void SculptTerrain(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.heightmapResolution;
        float[,] oldHeights = data.GetHeights(0, 0, resolution, resolution);
        float[,] newHeights = new float[resolution, resolution];
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;

        Vector2[] leftRidge =
        {
            new Vector2(-128f, 132f), new Vector2(-154f, 78f), new Vector2(-160f, 12f),
            new Vector2(-145f, -55f), new Vector2(-108f, -116f), new Vector2(-55f, -172f)
        };
        float[] leftHeight = { 72f, 94f, 104f, 92f, 76f, 55f };
        float[] leftWidth = { 34f, 41f, 44f, 43f, 39f, 32f };

        Vector2[] rightRidge =
        {
            new Vector2(150f, 140f), new Vector2(184f, 88f), new Vector2(198f, 20f),
            new Vector2(190f, -54f), new Vector2(158f, -120f), new Vector2(75f, -177f)
        };
        float[] rightHeight = { 92f, 118f, 130f, 119f, 100f, 72f };
        float[] rightWidth = { 42f, 48f, 51f, 50f, 45f, 36f };

        Vector2 bridgeA = MapCenter + (-Up) * 25f;
        Vector2 bridgeB = MapCenter + (-Up) * 215f;

        Undo.RegisterCompleteObjectUndo(data, "Apply shelter mountain and river terrain");

        for (int y = 0; y < resolution; y++)
        {
            float wz = origin.z + (y / (float)(resolution - 1)) * size.z;
            for (int x = 0; x < resolution; x++)
            {
                float wx = origin.x + (x / (float)(resolution - 1)) * size.x;
                Vector2 world = new Vector2(wx, wz);
                Vector2 delta = world - MapCenter;
                float r = Vector2.Dot(delta, Right);
                float u = Vector2.Dot(delta, Up);
                Vector2 local = new Vector2(r, u);
                float oldWorld = origin.y + oldHeights[y, x] * size.y;

                float radial = delta.magnitude;
                float workMask = 1f - Smooth01((radial - 300f) / 55f);
                float baseWorld = Mathf.Lerp(198.5f, oldWorld, 0.12f);

                float left = Ridge(local, leftRidge, leftHeight, leftWidth);
                left = Mathf.Max(left, SegmentRidge(local, new Vector2(-150f, 48f), new Vector2(-232f, 8f), 62f, 35f));
                left = Mathf.Max(left, SegmentRidge(local, new Vector2(-137f, -48f), new Vector2(-218f, -103f), 55f, 32f));

                float right = Ridge(local, rightRidge, rightHeight, rightWidth);
                right = Mathf.Max(right, SegmentRidge(local, new Vector2(188f, 58f), new Vector2(260f, 12f), 73f, 38f));
                right = Mathf.Max(right, SegmentRidge(local, new Vector2(176f, -58f), new Vector2(247f, -128f), 66f, 36f));

                float mountain = Mathf.Max(left, right);
                float broadForm = Mathf.Clamp01(mountain / 34f);
                float erosionNoise = (FractalNoise(wx, wz) - 0.5f) * (8f + broadForm * 11f);
                float ridgeNoise = (Mathf.PerlinNoise(wx * 0.041f + 13.2f, wz * 0.041f + 8.4f) - 0.5f) * 7f * broadForm;
                float targetWorld = baseWorld + mountain + erosionNoise * broadForm + ridgeNoise;

                // Two empty, buildable alluvial terraces. No ruined buildings are generated.
                targetWorld = FlattenTerrace(targetWorld, local, new Vector2(-55f, 84f), new Vector2(34f, 24f), 203.5f);
                targetWorld = FlattenTerrace(targetWorld, local, new Vector2(55f, 88f), new Vector2(37f, 25f), 204.5f);

                // River valley remains north/up-image of both terraces.
                if (r > -280f && r < 280f)
                {
                    float centerU = RiverU(r);
                    float distance = Mathf.Abs(u - centerU);
                    float halfWidth = RiverHalfWidth(r);
                    float waterY = RiverWaterY(r);
                    float bedY = waterY - 1.8f;
                    float floodplainY = 197.5f + 1.2f * Mathf.PerlinNoise(r * 0.018f + 7.1f, 2.7f);

                    if (distance < halfWidth)
                    {
                        float channelT = Smooth01(distance / halfWidth);
                        targetWorld = Mathf.Min(targetWorld, Mathf.Lerp(bedY, waterY - 0.25f, channelT));
                    }
                    else if (distance < halfWidth + 28f)
                    {
                        float bankT = Smooth01((distance - halfWidth) / 28f);
                        float bankTarget = Mathf.Lerp(waterY + 0.7f, floodplainY, bankT);
                        targetWorld = Mathf.Min(targetWorld, bankTarget);
                    }
                }

                float centerPreserve = 1f - Smooth01((radial - 66f) / 26f);
                float bridgeDistance = DistanceToSegment(world, bridgeA, bridgeB);
                float bridgePreserve = 1f - Smooth01((bridgeDistance - 18f) / 14f);
                float preserve = Mathf.Max(centerPreserve, bridgePreserve);

                float designed = Mathf.Lerp(oldWorld, targetWorld, workMask);
                float finalWorld = Mathf.Lerp(designed, oldWorld, preserve);
                newHeights[y, x] = Mathf.Clamp01((finalWorld - origin.y) / size.y);
            }
        }

        data.SetHeights(0, 0, newHeights);
        ClearRiverVegetation(data, origin, size);
    }

    private static void CreateOrUpdateRiver(Terrain terrain)
    {
        EnsureFolder(GeneratedFolder);
        const int segments = 96;
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float r = Mathf.Lerp(-270f, 270f, t);
            float u = RiverU(r);
            float width = RiverHalfWidth(r) * 0.90f;
            float derivative = RiverDerivative(r);
            Vector2 tangent = (Right + Up * derivative).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            Vector2 center = MapCenter + Right * r + Up * u;
            float y = RiverWaterY(r);

            Vector2 edgeA = center + normal * width;
            Vector2 edgeB = center - normal * width;
            vertices[i * 2] = new Vector3(edgeA.x, y, edgeA.y);
            vertices[i * 2 + 1] = new Vector3(edgeB.x, y, edgeB.y);
            uv[i * 2] = new Vector2(0f, t * 18f);
            uv[i * 2 + 1] = new Vector2(1f, t * 18f);

            if (i < segments)
            {
                int vi = i * 2;
                int ti = i * 6;
                triangles[ti] = vi;
                triangles[ti + 1] = vi + 2;
                triangles[ti + 2] = vi + 1;
                triangles[ti + 3] = vi + 1;
                triangles[ti + 4] = vi + 2;
                triangles[ti + 5] = vi + 3;
            }
        }

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RiverMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Codex_ShelterRiver_Mesh" };
            AssetDatabase.CreateAsset(mesh, RiverMeshPath);
        }
        else
        {
            Undo.RegisterCompleteObjectUndo(mesh, "Update shelter river mesh");
            mesh.Clear();
        }

        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        GameObject river = GameObject.Find(RiverObjectName);
        if (river == null)
        {
            river = new GameObject(RiverObjectName);
            Undo.RegisterCreatedObjectUndo(river, "Create shelter river");
        }

        river.transform.position = Vector3.zero;
        river.transform.rotation = Quaternion.identity;
        river.transform.localScale = Vector3.one;
        river.isStatic = true;

        MeshFilter filter = river.GetComponent<MeshFilter>();
        if (!filter)
            filter = Undo.AddComponent<MeshFilter>(river);
        MeshRenderer renderer = river.GetComponent<MeshRenderer>();
        if (!renderer)
            renderer = Undo.AddComponent<MeshRenderer>(river);
        filter.sharedMesh = mesh;

        Material water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (water != null)
            renderer.sharedMaterial = water;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        EditorUtility.SetDirty(river);
    }

    private static void ClearRiverVegetation(TerrainData data, Vector3 origin, Vector3 size)
    {
        var trees = data.treeInstances;
        data.treeInstances = trees.Where(tree =>
        {
            float wx = origin.x + tree.position.x * size.x;
            float wz = origin.z + tree.position.z * size.z;
            Vector2 delta = new Vector2(wx, wz) - MapCenter;
            float r = Vector2.Dot(delta, Right);
            float u = Vector2.Dot(delta, Up);
            return r <= -282f || r >= 282f || Mathf.Abs(u - RiverU(r)) > RiverHalfWidth(r) + 6f;
        }).ToArray();
    }

    private static float Ridge(Vector2 p, Vector2[] points, float[] heights, float[] widths)
    {
        float result = 0f;
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[i + 1];
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
            float distance = Vector2.Distance(p, Vector2.Lerp(a, b, t));
            float height = Mathf.Lerp(heights[i], heights[i + 1], t);
            float width = Mathf.Lerp(widths[i], widths[i + 1], t);
            float profile = Mathf.Exp(-(distance * distance) / (2f * width * width));
            result = Mathf.Max(result, height * profile);
        }
        return result;
    }

    private static float SegmentRidge(Vector2 p, Vector2 a, Vector2 b, float height, float width)
    {
        float distance = DistanceToSegment(p, a, b);
        float endpointFade = Mathf.Clamp01(1f - Mathf.Min(Vector2.Distance(p, a), Vector2.Distance(p, b)) / 150f);
        return height * Mathf.Exp(-(distance * distance) / (2f * width * width)) * Mathf.Lerp(0.72f, 1f, endpointFade);
    }

    private static float FlattenTerrace(float source, Vector2 p, Vector2 center, Vector2 radii, float elevation)
    {
        Vector2 q = p - center;
        float ellipse = Mathf.Sqrt((q.x * q.x) / (radii.x * radii.x) + (q.y * q.y) / (radii.y * radii.y));
        float mask = 1f - Smooth01((ellipse - 0.72f) / 0.34f);
        float subtle = (Mathf.PerlinNoise(p.x * 0.035f + 2.1f, p.y * 0.035f + 4.7f) - 0.5f) * 0.7f;
        return Mathf.Lerp(source, elevation + subtle, mask);
    }

    private static float RiverU(float r)
    {
        return 156f + 18f * Mathf.Sin((r + 38f) / 72f) + 6f * Mathf.Sin((r - 12f) / 28f);
    }

    private static float RiverDerivative(float r)
    {
        return (18f / 72f) * Mathf.Cos((r + 38f) / 72f) + (6f / 28f) * Mathf.Cos((r - 12f) / 28f);
    }

    private static float RiverHalfWidth(float r)
    {
        return 10.5f + 3.2f * (0.5f + 0.5f * Mathf.Sin(r / 41f + 0.6f));
    }

    private static float RiverWaterY(float r)
    {
        return 191.2f - (r + 270f) * 0.010f;
    }

    private static float FractalNoise(float x, float z)
    {
        float a = Mathf.PerlinNoise(x * 0.0105f + 31.7f, z * 0.0105f + 19.4f);
        float b = Mathf.PerlinNoise(x * 0.024f + 7.6f, z * 0.024f + 53.1f);
        float c = Mathf.PerlinNoise(x * 0.061f + 81.3f, z * 0.061f + 12.8f);
        return a * 0.56f + b * 0.30f + c * 0.14f;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return Vector2.Distance(p, a + ab * t);
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
