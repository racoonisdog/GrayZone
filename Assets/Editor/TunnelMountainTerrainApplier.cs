using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds a smooth, driveable mountain around the tunnel already placed in the
/// shelter grey-box scene.  The tunnel transform is the source of truth: the
/// object is never moved or resized.
/// </summary>
public static class TunnelMountainTerrainApplier
{
    private const string ExpectedScene = "Shelter_Grey_Boxing_BackUp_3";
    private const string TunnelName = "Tunnel";
    private const string BackupFolder = "Assets/Generated/TunnelMountain/Backups";
    private const string ShellMeshPath = "Assets/Generated/TunnelMountain/Codex_TunnelMountainShell_Mesh.asset";
    private const string ShellObjectName = "Codex_TunnelMountainShell";
    private const string ShellMaterialPath = "Assets/Mountain Forest/Assets/Materials/MossyRock56.mat";

    [MenuItem("Tools/Codex/Build Mountain Around Placed Tunnel")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != ExpectedScene)
            throw new InvalidOperationException("Open " + ExpectedScene + " before building the tunnel mountain.");

        GameObject tunnel = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == TunnelName);
        if (tunnel == null)
            throw new InvalidOperationException("The active scene does not contain a GameObject named '" + TunnelName + "'.");

        Terrain terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)
            .Where(t => t != null && t.terrainData != null)
            .OrderBy(t => HorizontalDistanceToBounds(t, tunnel.transform.position))
            .FirstOrDefault();
        if (terrain == null)
            throw new InvalidOperationException("The active scene does not contain a valid Terrain.");

        Renderer[] renderers = tunnel.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException("The placed Tunnel has no Renderer bounds to use as the entrance reference.");

        Bounds tunnelBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            tunnelBounds.Encapsulate(renderers[i].bounds);

        CreateBackups(scene.path, terrain.terrainData);
        TunnelFrame frame = BuildFrame(tunnel.transform, tunnelBounds, terrain);
        SculptSmoothMountain(terrain, frame);
        CutOpenTunnelCorridor(terrain, frame);
        RemoveGeneratedTunnelShell();
        ClearTreesNearTunnel(terrain, frame);

        terrain.Flush();
        EditorUtility.SetDirty(terrain.terrainData);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[TunnelMountainTerrainApplier] Built a smooth mountain around the placed tunnel. " +
            $"Entrance={frame.portal}, outward={frame.outward}, openingHalfWidth={frame.openingHalfWidth:F1}m, " +
            $"ground={frame.groundY:F1}m, peak={frame.peakY:F1}m. Terrain holes keep the tunnel interior open.");
    }

    [MenuItem("Tools/Codex/Focus Tunnel Mountain Overview")]
    public static void FocusOverview()
    {
        GameObject tunnel = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == TunnelName);
        SceneView view = SceneView.lastActiveSceneView;
        if (tunnel == null || view == null)
            return;

        Vector3 outward = tunnel.transform.forward;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.001f)
            outward = Vector3.left;
        outward.Normalize();
        Vector3 cameraOutward = Quaternion.Euler(0f, -28f, 0f) * outward;

        view.pivot = tunnel.transform.position + Vector3.up * 20f;
        view.rotation = Quaternion.LookRotation(-cameraOutward - Vector3.up * 0.68f, Vector3.up);
        view.size = 118f;
        view.Repaint();
    }

    [MenuItem("Tools/Codex/Audit Tunnel Mountain Scope")]
    public static void AuditScope()
    {
        GameObject tunnel = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == TunnelName);
        Terrain terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)
            .FirstOrDefault(t => t != null && t.terrainData != null);
        if (tunnel == null || terrain == null)
            throw new InvalidOperationException("Tunnel or Terrain is missing from the active scene.");

        string prefix = terrain.terrainData.name + "_before_tunnel_mountain_";
        string backupPath = AssetDatabase.FindAssets("t:TerrainData", new[] { BackupFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        TerrainData backup = string.IsNullOrEmpty(backupPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<TerrainData>(backupPath);
        if (backup == null)
            throw new InvalidOperationException("No tunnel-mountain TerrainData backup was found.");

        Renderer[] renderers = tunnel.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        TunnelFrame frame = BuildFrame(tunnel.transform, bounds, terrain);

        int resolution = terrain.terrainData.heightmapResolution;
        float[,] currentHeights = terrain.terrainData.GetHeights(0, 0, resolution, resolution);
        float[,] backupHeights = backup.GetHeights(0, 0, resolution, resolution);
        Vector3 size = terrain.terrainData.size;
        Vector3 origin = terrain.transform.position;
        float sideRadius = Mathf.Max(150f, frame.openingHalfWidth * 3.05f);
        float frontRadius = Mathf.Max(205f, frame.openingHalfWidth * 3.8f);
        float backRadius = Mathf.Max(125f, frame.tunnelHalfLength + 75f);
        float centerBehind = Mathf.Max(34f, frame.tunnelHalfLength * 0.58f);
        Vector2 mountainCenter = frame.portal - frame.outward * centerBehind;

        int changedHeights = 0;
        int outOfScopeHeights = 0;
        Vector2 changedMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 changedMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int z = 0; z < resolution; z++)
        {
            float localZ = (z / (float)(resolution - 1)) * size.z;
            for (int x = 0; x < resolution; x++)
            {
                if (Mathf.Abs(currentHeights[z, x] - backupHeights[z, x]) <= 0.000001f)
                    continue;
                changedHeights++;
                float localX = (x / (float)(resolution - 1)) * size.x;
                Vector3 wp = origin + new Vector3(localX, 0f, localZ);
                Vector2 world = new Vector2(wp.x, wp.z);
                changedMin = Vector2.Min(changedMin, world);
                changedMax = Vector2.Max(changedMax, world);
                Vector2 q = world - mountainCenter;
                float lateral = Vector2.Dot(q, frame.right);
                float longitudinal = Vector2.Dot(q, frame.outward);
                float longRadius = longitudinal >= 0f ? frontRadius : backRadius;
                float ellipse = Mathf.Sqrt((lateral * lateral) / (sideRadius * sideRadius) +
                                           (longitudinal * longitudinal) / (longRadius * longRadius));
                if (ellipse >= 1.181f)
                    outOfScopeHeights++;
            }
        }

        int holeResolution = terrain.terrainData.holesResolution;
        bool[,] currentHoles = terrain.terrainData.GetHoles(0, 0, holeResolution, holeResolution);
        bool[,] backupHoles = backup.GetHoles(0, 0, holeResolution, holeResolution);
        int changedHoles = 0;
        int outOfScopeHoles = 0;
        float insideDepth = Mathf.Max(60f, frame.tunnelHalfLength * 1.25f);
        float outsideDepth = 3.5f;
        float holeHalfWidth = Mathf.Max(8f, frame.openingHalfWidth * 0.88f);
        for (int z = 0; z < holeResolution; z++)
        {
            float localZ = ((z + 0.5f) / holeResolution) * size.z;
            for (int x = 0; x < holeResolution; x++)
            {
                if (currentHoles[z, x] == backupHoles[z, x])
                    continue;
                changedHoles++;
                float localX = ((x + 0.5f) / holeResolution) * size.x;
                Vector3 wp = origin + new Vector3(localX, 0f, localZ);
                Vector2 d = new Vector2(wp.x, wp.z) - frame.portal;
                float s = Vector2.Dot(d, frame.outward);
                float r = Mathf.Abs(Vector2.Dot(d, frame.right));
                if (s < -insideDepth || s > outsideDepth || r > holeHalfWidth + 0.1f)
                    outOfScopeHoles++;
            }
        }

        Debug.Log(
            $"[TunnelMountainTerrainApplier] SCOPE AUDIT: heightRes={resolution}, holesRes={holeResolution}, " +
            $"dataSize={size}, terrainScale={terrain.transform.lossyScale}, portal={frame.portal}, " +
            $"openingHalfWidth={frame.openingHalfWidth:F1}, halfLength={frame.tunnelHalfLength:F1}; " +
            $"heightCells={changedHeights}, " +
            $"heightOutsideTunnelMountain={outOfScopeHeights}, changedXZ={changedMin}..{changedMax}; " +
            $"holeCells={changedHoles}, holeOutsideTunnelCorridor={outOfScopeHoles}; " +
            $"trees={backup.treeInstanceCount}->{terrain.terrainData.treeInstanceCount}; backup={backupPath}");
    }

    private readonly struct TunnelFrame
    {
        public readonly Vector2 portal;
        public readonly Vector2 outward;
        public readonly Vector2 right;
        public readonly float openingHalfWidth;
        public readonly float tunnelHalfLength;
        public readonly float groundY;
        public readonly float portalTopY;
        public readonly float peakY;

        public TunnelFrame(Vector2 portal, Vector2 outward, Vector2 right, float openingHalfWidth,
            float tunnelHalfLength, float groundY, float portalTopY, float peakY)
        {
            this.portal = portal;
            this.outward = outward;
            this.right = right;
            this.openingHalfWidth = openingHalfWidth;
            this.tunnelHalfLength = tunnelHalfLength;
            this.groundY = groundY;
            this.portalTopY = portalTopY;
            this.peakY = peakY;
        }
    }

    private static TunnelFrame BuildFrame(Transform tunnel, Bounds bounds, Terrain terrain)
    {
        Vector2 outward = new Vector2(tunnel.forward.x, tunnel.forward.z);
        if (outward.sqrMagnitude < 0.001f)
            outward = Vector2.left;
        outward.Normalize();
        Vector2 right = new Vector2(-outward.y, outward.x);

        Vector3 ext = bounds.extents;
        float forwardSupport = Mathf.Abs(outward.x) * ext.x + Mathf.Abs(outward.y) * ext.z;
        float rightSupport = Mathf.Abs(right.x) * ext.x + Mathf.Abs(right.y) * ext.z;
        Vector2 center = new Vector2(bounds.center.x, bounds.center.z);
        Vector2 portal = center + outward * forwardSupport;

        // Sample well in front of the portal so the existing lumpy mound does not
        // bias the road/entrance elevation.
        Vector3 groundProbe = new Vector3(
            portal.x + outward.x * Mathf.Max(30f, rightSupport * 0.75f),
            0f,
            portal.y + outward.y * Mathf.Max(30f, rightSupport * 0.75f));
        float groundY = terrain.SampleHeight(groundProbe) + terrain.transform.position.y;
        groundY = Mathf.Min(groundY, bounds.min.y + 1.5f);

        float openingHalfWidth = Mathf.Max(10f, rightSupport * 0.82f);
        float tunnelHalfLength = Mathf.Max(12f, forwardSupport);
        float peakY = Mathf.Max(groundY + 55f, bounds.max.y + 24f);
        return new TunnelFrame(portal, outward, right, openingHalfWidth, tunnelHalfLength,
            groundY, bounds.max.y, peakY);
    }

    private static void SculptSmoothMountain(Terrain terrain, TunnelFrame frame)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.heightmapResolution;
        float[,] oldHeights = data.GetHeights(0, 0, resolution, resolution);
        float[,] heights = (float[,])oldHeights.Clone();
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;

        string prefix = data.name + "_before_tunnel_mountain_";
        string originalPath = AssetDatabase.FindAssets("t:TerrainData", new[] { BackupFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        TerrainData original = string.IsNullOrEmpty(originalPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<TerrainData>(originalPath);
        float[,] originalHeights = original != null && original.heightmapResolution == resolution
            ? original.GetHeights(0, 0, resolution, resolution)
            : null;

        float sideRadius = Mathf.Max(150f, frame.openingHalfWidth * 3.05f);
        float frontRadius = Mathf.Max(205f, frame.openingHalfWidth * 3.8f);
        float backRadius = Mathf.Max(125f, frame.tunnelHalfLength + 75f);
        float centerBehind = Mathf.Max(34f, frame.tunnelHalfLength * 0.58f);
        Vector2 mountainCenter = frame.portal - frame.outward * centerBehind;

        Undo.RegisterCompleteObjectUndo(data, "Build smooth mountain around placed tunnel");

        for (int z = 0; z < resolution; z++)
        {
            float localZ = (z / (float)(resolution - 1)) * size.z;
            for (int x = 0; x < resolution; x++)
            {
                float localX = (x / (float)(resolution - 1)) * size.x;
                // TerrainData size is the rendered world footprint in this legacy
                // scene; the Transform's X/Z scale of 50 is intentionally ignored.
                Vector3 worldPoint = origin + new Vector3(localX, 0f, localZ);
                Vector2 world = new Vector2(worldPoint.x, worldPoint.z);
                Vector2 q = world - mountainCenter;
                float lateral = Vector2.Dot(q, frame.right);
                float longitudinal = Vector2.Dot(q, frame.outward);
                float longRadius = longitudinal >= 0f ? frontRadius : backRadius;
                float ellipse = Mathf.Sqrt(
                    (lateral * lateral) / (sideRadius * sideRadius) +
                    (longitudinal * longitudinal) / (longRadius * longRadius));

                if (ellipse >= 1.18f)
                {
                    if (originalHeights != null)
                        heights[z, x] = originalHeights[z, x];
                    continue;
                }

                float oldWorldY = origin.y + oldHeights[z, x] * size.y;

                // A broad smoothstep dome gives the green-line silhouette: rounded
                // crown, no sharp peak, and long gentle falloff in front and sideways.
                float dome = Smooth01(1f - ellipse);
                float shoulder = Mathf.Exp(-Mathf.Pow((ellipse - 0.43f) / 0.34f, 2f)) * 2.2f;
                float targetY = Mathf.Lerp(frame.groundY, frame.peakY, dome) + shoulder;

                // Keep the mouth and approach road low and open.  The cap widens
                // toward the viewer so every part of the portal remains visible.
                Vector2 fromPortal = world - frame.portal;
                float s = Vector2.Dot(fromPortal, frame.outward);
                float r = Mathf.Abs(Vector2.Dot(fromPortal, frame.right));
                float approachOuterWidth = frame.openingHalfWidth + 18f + Mathf.Max(0f, s) * 0.28f;
                if (s > -5f && s < 120f && r < approachOuterWidth)
                {
                    float endFade = 1f - Smooth01((s - 88f) / 32f);
                    float sideFade = 1f - Smooth01(
                        (r - frame.openingHalfWidth) /
                        Mathf.Max(approachOuterWidth - frame.openingHalfWidth, 0.01f));
                    float clearance = endFade * sideFade;
                    float gentleGrade = frame.groundY + Mathf.Lerp(0.8f, 5.5f, Mathf.Clamp01(s / 120f));
                    targetY = Mathf.Lerp(targetY, Mathf.Min(targetY, gentleGrade), clearance);
                }

                // Feather into untouched terrain beyond the designed footprint.
                float blend = 1f - Smooth01((ellipse - 0.92f) / 0.26f);
                float finalY = Mathf.Lerp(oldWorldY, targetY, blend);
                heights[z, x] = Mathf.Clamp01((finalY - origin.y) / size.y);
            }
        }

        data.SetHeights(0, 0, heights);
    }

    private static void CutOpenTunnelCorridor(Terrain terrain, TunnelFrame frame)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.holesResolution;
        if (resolution <= 0)
            return;

        // Always start from the first pre-operation backup so a previous preview
        // cannot leave an open-top trench behind.  Existing holes elsewhere are
        // preserved exactly.
        string prefix = data.name + "_before_tunnel_mountain_";
        string originalPath = AssetDatabase.FindAssets("t:TerrainData", new[] { BackupFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        TerrainData original = string.IsNullOrEmpty(originalPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<TerrainData>(originalPath);
        bool[,] holes = original != null && original.holesResolution == resolution
            ? original.GetHoles(0, 0, resolution, resolution)
            : data.GetHoles(0, 0, resolution, resolution);
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;
        float insideDepth = Mathf.Max(18f, frame.tunnelHalfLength * 0.90f);
        float outsideDepth = 3.5f;
        float holeHalfWidth = Mathf.Max(6.5f, frame.openingHalfWidth * 0.58f);

        for (int z = 0; z < resolution; z++)
        {
            float localZ = ((z + 0.5f) / resolution) * size.z;
            for (int x = 0; x < resolution; x++)
            {
                float localX = ((x + 0.5f) / resolution) * size.x;
                Vector3 wp = origin + new Vector3(localX, 0f, localZ);
                Vector2 d = new Vector2(wp.x, wp.z) - frame.portal;
                float s = Vector2.Dot(d, frame.outward);
                float r = Mathf.Abs(Vector2.Dot(d, frame.right));

                // Keep the cut compact and entirely under/just behind the portal.
                // This exposes the entrance without creating an open trench.
                if (s >= -insideDepth && s <= outsideDepth && r <= holeHalfWidth)
                    holes[z, x] = false;
            }
        }

        data.SetHoles(0, 0, holes);
    }

    private static void CreateOrUpdateTunnelShell(Terrain terrain, TunnelFrame frame, Bounds tunnelBounds)
    {
        EnsureFolder("Assets/Generated/TunnelMountain");
        const int segments = 24;
        float insideDepth = Mathf.Max(18f, frame.tunnelHalfLength * 0.90f);
        float outsideDepth = 3.5f;
        float holeHalfWidth = Mathf.Max(6.5f, frame.openingHalfWidth * 0.58f);
        float shellHalfWidth = holeHalfWidth + 2.4f;
        float floorHalfWidth = holeHalfWidth * 0.94f;
        float floorY = frame.groundY + 0.35f;
        float ceilingY = Mathf.Max(floorY + 8f, tunnelBounds.max.y + 1.0f);
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;

        var vertices = new List<Vector3>(512);
        var uvs = new List<Vector2>(512);
        var triangles = new List<int>(1536);

        Vector3[,] top = new Vector3[segments + 1, 2];
        Vector3[,] ceiling = new Vector3[segments + 1, 2];
        Vector3[,] floor = new Vector3[segments + 1, 2];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float s = Mathf.Lerp(-insideDepth, outsideDepth, t);
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                Vector2 topXZ = frame.portal + frame.outward * s + frame.right * (sign * shellHalfWidth);
                float nx = Mathf.Clamp01((topXZ.x - origin.x) / size.x);
                float nz = Mathf.Clamp01((topXZ.y - origin.z) / size.z);
                float topY = origin.y + data.GetInterpolatedHeight(nx, nz) + 0.12f;
                top[i, side] = new Vector3(topXZ.x, topY, topXZ.y);

                Vector2 innerXZ = frame.portal + frame.outward * s + frame.right * (sign * holeHalfWidth);
                ceiling[i, side] = new Vector3(innerXZ.x, ceilingY, innerXZ.y);

                Vector2 floorXZ = frame.portal + frame.outward * s + frame.right * (sign * floorHalfWidth);
                floor[i, side] = new Vector3(floorXZ.x, floorY, floorXZ.y);
            }
        }

        for (int i = 0; i < segments; i++)
        {
            float v0 = i / (float)segments;
            float v1 = (i + 1) / (float)segments;
            AddDoubleQuad(vertices, uvs, triangles, top[i, 0], top[i + 1, 0], top[i + 1, 1], top[i, 1], v0, v1);
            AddDoubleQuad(vertices, uvs, triangles, ceiling[i, 1], ceiling[i + 1, 1], ceiling[i + 1, 0], ceiling[i, 0], v0, v1);
            AddDoubleQuad(vertices, uvs, triangles, floor[i, 0], floor[i + 1, 0], floor[i + 1, 1], floor[i, 1], v0, v1);
        }

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ShellMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Codex_TunnelMountainShell_Mesh" };
            AssetDatabase.CreateAsset(mesh, ShellMeshPath);
        }
        else
        {
            Undo.RegisterCompleteObjectUndo(mesh, "Update tunnel mountain shell");
            mesh.Clear();
        }

        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        GameObject shell = GameObject.Find(ShellObjectName);
        if (shell == null)
        {
            shell = new GameObject(ShellObjectName);
            Undo.RegisterCreatedObjectUndo(shell, "Create tunnel mountain shell");
        }
        shell.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        shell.transform.localScale = Vector3.one;
        shell.isStatic = true;

        MeshFilter filter = shell.GetComponent<MeshFilter>();
        if (filter == null)
            filter = Undo.AddComponent<MeshFilter>(shell);
        MeshRenderer renderer = shell.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = Undo.AddComponent<MeshRenderer>(shell);
        MeshCollider collider = shell.GetComponent<MeshCollider>();
        if (collider == null)
            collider = Undo.AddComponent<MeshCollider>(shell);
        filter.sharedMesh = mesh;
        collider.sharedMesh = mesh;
        Material material = AssetDatabase.LoadAssetAtPath<Material>(ShellMaterialPath);
        if (material != null)
            renderer.sharedMaterial = material;
        renderer.receiveShadows = true;
        EditorUtility.SetDirty(shell);
    }

    private static void RemoveGeneratedTunnelShell()
    {
        GameObject shell = GameObject.Find(ShellObjectName);
        if (shell != null)
            Undo.DestroyObjectImmediate(shell);
        if (AssetDatabase.LoadAssetAtPath<Mesh>(ShellMeshPath) != null)
            AssetDatabase.DeleteAsset(ShellMeshPath);
    }

    private static void AddDoubleQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, float v0, float v1)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        uvs.Add(new Vector2(0f, v0));
        uvs.Add(new Vector2(0f, v1));
        uvs.Add(new Vector2(1f, v1));
        uvs.Add(new Vector2(1f, v0));
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
        triangles.Add(start);
    }

    private static void ClearTreesNearTunnel(Terrain terrain, TunnelFrame frame)
    {
        TerrainData data = terrain.terrainData;
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;
        data.treeInstances = data.treeInstances.Where(tree =>
        {
            Vector3 local = new Vector3(tree.position.x * size.x, tree.position.y * size.y, tree.position.z * size.z);
            Vector3 world3 = origin + local;
            Vector2 d = new Vector2(world3.x, world3.z) - frame.portal;
            float s = Vector2.Dot(d, frame.outward);
            float r = Mathf.Abs(Vector2.Dot(d, frame.right));
            return !(s > -70f && s < 155f && r < frame.openingHalfWidth + 22f);
        }).ToArray();
    }

    private static float HorizontalDistanceToBounds(Terrain terrain, Vector3 point)
    {
        Vector3 local = point - terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float dx = Mathf.Max(0f, Mathf.Max(-local.x, local.x - size.x));
        float dz = Mathf.Max(0f, Mathf.Max(-local.z, local.z - size.z));
        return dx * dx + dz * dz;
    }

    private static void CreateBackups(string scenePath, TerrainData terrainData)
    {
        EnsureFolder(BackupFolder);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string sceneBackup = BackupFolder + "/" + Path.GetFileNameWithoutExtension(scenePath) +
                             "_before_tunnel_mountain_" + stamp + ".unity";
        string terrainPath = AssetDatabase.GetAssetPath(terrainData);
        string terrainBackup = BackupFolder + "/" + terrainData.name +
                               "_before_tunnel_mountain_" + stamp + ".asset";

        if (!AssetDatabase.CopyAsset(scenePath, sceneBackup))
            throw new IOException("Could not back up the active scene to " + sceneBackup);
        if (!AssetDatabase.CopyAsset(terrainPath, terrainBackup))
            throw new IOException("Could not back up TerrainData to " + terrainBackup);

        Debug.Log("[TunnelMountainTerrainApplier] Backups: " + sceneBackup + " | " + terrainBackup);
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

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
