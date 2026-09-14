using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Opens only the Terrain cells immediately behind the artist-placed tunnel
/// portal. It never edits heights, terrain painting, or object transforms.
/// </summary>
public static class TunnelEntranceHoleOnly
{
    private const string TunnelName = "Tunnel";
    private const string BackupFolder = "Assets/Generated/TunnelEntranceHole/Backups";

    [MenuItem("Tools/Codex/Cut Tunnel Entrance Only")]
    public static void Apply()
    {
        GameObject tunnel = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == TunnelName);
        if (tunnel == null)
            throw new InvalidOperationException("No GameObject named 'Tunnel' exists in the active scene.");

        Terrain terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)
            .Where(t => t != null && t.terrainData != null)
            .OrderBy(t => HorizontalDistanceToTerrain(t, tunnel.transform.position))
            .FirstOrDefault();
        if (terrain == null)
            throw new InvalidOperationException("No valid Terrain exists in the active scene.");

        Transform[] sceneTransforms = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        Vector3[] localPositions = sceneTransforms.Select(t => t.localPosition).ToArray();
        Quaternion[] localRotations = sceneTransforms.Select(t => t.localRotation).ToArray();
        Vector3[] localScales = sceneTransforms.Select(t => t.localScale).ToArray();

        Renderer[] renderers = tunnel.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException("Tunnel has no renderer bounds for sizing its entrance.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        TerrainData data = terrain.terrainData;
        CreateBackup(data);
        float[,] heightSnapshot = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);

        Vector2 outward = new Vector2(tunnel.transform.forward.x, tunnel.transform.forward.z);
        if (outward.sqrMagnitude < 0.001f)
            outward = Vector2.left;
        outward.Normalize();
        Vector2 right = new Vector2(-outward.y, outward.x);
        Vector2 rootReference = new Vector2(tunnel.transform.position.x, tunnel.transform.position.z);

        Vector3 ext = bounds.extents;
        float forwardSupport = Mathf.Abs(outward.x) * ext.x + Mathf.Abs(outward.y) * ext.z;
        float rightSupport = Mathf.Abs(right.x) * ext.x + Mathf.Abs(right.y) * ext.z;
        Vector2 boundsCenter = new Vector2(bounds.center.x, bounds.center.z);
        Vector2 portal = boundsCenter + outward * forwardSupport;
        float innerHalfWidth = Mathf.Max(6.0f, rightSupport * 0.37f);
        const float insideDepth = 4f;
        const float outsideLip = 0.6f;

        int resolution = data.holesResolution;
        bool[,] holes = data.GetHoles(0, 0, resolution, resolution);
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;
        int restoredCells = 0;
        int openedCells = 0;

        Undo.RegisterCompleteObjectUndo(data, "Cut tunnel entrance only");

        // Clean up only the immediate mouth area from earlier previews. This is
        // deliberately bounded to the tunnel footprint and cannot affect any
        // remote part of the terrain.
        for (int z = 0; z < resolution; z++)
        {
            float wz = origin.z + ((z + 0.5f) / resolution) * size.z;
            for (int x = 0; x < resolution; x++)
            {
                float wx = origin.x + ((x + 0.5f) / resolution) * size.x;
                Vector2 d = new Vector2(wx, wz) - portal;
                float s = Vector2.Dot(d, outward);
                float r = Mathf.Abs(Vector2.Dot(d, right));
                Vector2 rootDelta = new Vector2(wx, wz) - rootReference;
                float rootS = Vector2.Dot(rootDelta, outward);
                float rootR = Mathf.Abs(Vector2.Dot(rootDelta, right));

                bool inActualMouth = s >= -25f && s <= 22f && r <= rightSupport + 2f;
                bool inPreviousRootCut = rootS >= -25f && rootS <= 22f && rootR <= rightSupport + 2f;
                if ((inActualMouth || inPreviousRootCut) && !holes[z, x])
                {
                    holes[z, x] = true;
                    restoredCells++;
                }
            }
        }

        // The cut remains inside the arch's inner width. The concrete portal
        // masks the square heightfield cells, yielding the visible arch outline.
        for (int z = 0; z < resolution; z++)
        {
            float wz = origin.z + ((z + 0.5f) / resolution) * size.z;
            for (int x = 0; x < resolution; x++)
            {
                float wx = origin.x + ((x + 0.5f) / resolution) * size.x;
                Vector2 d = new Vector2(wx, wz) - portal;
                float s = Vector2.Dot(d, outward);
                float r = Mathf.Abs(Vector2.Dot(d, right));
                if (s >= -insideDepth && s <= outsideLip && r <= innerHalfWidth)
                {
                    if (holes[z, x])
                        openedCells++;
                    holes[z, x] = false;
                }
            }
        }

        data.SetHoles(0, 0, holes);
        terrain.Flush();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        float[,] heightAfter = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
        int changedHeightCells = 0;
        for (int z = 0; z < data.heightmapResolution; z++)
        for (int x = 0; x < data.heightmapResolution; x++)
            if (heightSnapshot[z, x] != heightAfter[z, x])
                changedHeightCells++;

        int changedTransforms = 0;
        for (int i = 0; i < sceneTransforms.Length; i++)
            if (sceneTransforms[i] == null ||
                sceneTransforms[i].localPosition != localPositions[i] ||
                sceneTransforms[i].localRotation != localRotations[i] ||
                sceneTransforms[i].localScale != localScales[i])
                changedTransforms++;

        Debug.Log(
            $"[TunnelEntranceHoleOnly] Complete. portal={portal}, root={rootReference}, outward={outward}, " +
            $"innerHalfWidth={innerHalfWidth:F1}m, depth={insideDepth:F1}m, " +
            $"openedCells={openedCells}, cleanedPreviewCells={restoredCells}, " +
            $"heightCellsChanged={changedHeightCells}, objectTransformsChanged={changedTransforms}.");
    }

    [MenuItem("Tools/Codex/Focus Tunnel Entrance Front")]
    public static void FocusEntranceFront()
    {
        GameObject tunnel = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == TunnelName);
        SceneView view = SceneView.lastActiveSceneView;
        if (tunnel == null || view == null)
            return;

        Renderer[] renderers = tunnel.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 outward = tunnel.transform.forward;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.001f)
            outward = Vector3.left;
        outward.Normalize();
        float forwardSupport = Mathf.Abs(outward.x) * bounds.extents.x +
                               Mathf.Abs(outward.z) * bounds.extents.z;
        Vector3 portal = bounds.center + outward * forwardSupport;

        view.pivot = new Vector3(portal.x, bounds.center.y, portal.z);
        view.rotation = Quaternion.LookRotation(-outward - Vector3.up * 0.02f, Vector3.up);
        view.size = 16f;
        view.Repaint();
    }

    [MenuItem("Tools/Codex/Capture Tunnel Entrance Preview")]
    public static void CaptureEntrancePreview()
    {
        FocusEntranceFront();
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null)
            throw new InvalidOperationException("No active Scene view is available for verification.");

        const int width = 1600;
        const int height = 900;
        string output = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Library/CodexTunnelEntrancePreview.png"));
        Camera camera = view.camera;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture renderTarget = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = renderTarget;
            camera.Render();
            RenderTexture.active = renderTarget;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(output, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(renderTarget);
        }

        Debug.Log("[TunnelEntranceHoleOnly] Preview: " + output);
    }

    [MenuItem("Tools/Codex/Audit Tunnel Entrance Scope")]
    public static void AuditScope()
    {
        GameObject tunnel = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == TunnelName);
        Terrain terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)
            .Where(t => t != null && t.terrainData != null)
            .OrderBy(t => HorizontalDistanceToTerrain(t, tunnel.transform.position))
            .FirstOrDefault();
        if (tunnel == null || terrain == null)
            throw new InvalidOperationException("Tunnel or Terrain is missing from the active scene.");

        string firstBackupPath = AssetDatabase.FindAssets("t:TerrainData", new[] { BackupFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.Contains("_before_entrance_only_"))
            .OrderBy(path => path)
            .FirstOrDefault();
        TerrainData backup = string.IsNullOrEmpty(firstBackupPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<TerrainData>(firstBackupPath);
        if (backup == null)
            throw new InvalidOperationException("No entrance-only TerrainData backup was found.");

        TerrainData current = terrain.terrainData;
        float[,] currentHeights = current.GetHeights(0, 0, current.heightmapResolution, current.heightmapResolution);
        float[,] backupHeights = backup.GetHeights(0, 0, backup.heightmapResolution, backup.heightmapResolution);
        int changedHeights = 0;
        for (int z = 0; z < current.heightmapResolution; z++)
        for (int x = 0; x < current.heightmapResolution; x++)
            if (currentHeights[z, x] != backupHeights[z, x])
                changedHeights++;

        Renderer[] renderers = tunnel.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        Vector2 outward = new Vector2(tunnel.transform.forward.x, tunnel.transform.forward.z).normalized;
        Vector2 right = new Vector2(-outward.y, outward.x);
        Vector3 ext = bounds.extents;
        float forwardSupport = Mathf.Abs(outward.x) * ext.x + Mathf.Abs(outward.y) * ext.z;
        float rightSupport = Mathf.Abs(right.x) * ext.x + Mathf.Abs(right.y) * ext.z;
        Vector2 portal = new Vector2(bounds.center.x, bounds.center.z) + outward * forwardSupport;

        int resolution = current.holesResolution;
        bool[,] currentHoles = current.GetHoles(0, 0, resolution, resolution);
        bool[,] backupHoles = backup.GetHoles(0, 0, resolution, resolution);
        int changedHoles = 0;
        int outsideMouth = 0;
        Vector3 origin = terrain.transform.position;
        Vector3 size = current.size;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            if (currentHoles[z, x] == backupHoles[z, x])
                continue;
            changedHoles++;
            Vector2 world = new Vector2(
                origin.x + ((x + 0.5f) / resolution) * size.x,
                origin.z + ((z + 0.5f) / resolution) * size.z);
            Vector2 delta = world - portal;
            float s = Vector2.Dot(delta, outward);
            float r = Mathf.Abs(Vector2.Dot(delta, right));
            if (s < -25f || s > 22f || r > rightSupport + 2f)
                outsideMouth++;
        }

        Debug.Log($"[TunnelEntranceHoleOnly] SCOPE AUDIT: heightCellsChanged={changedHeights}, " +
                  $"holeCellsChanged={changedHoles}, holeCellsOutsideMouth={outsideMouth}, " +
                  $"baseline={firstBackupPath}.");
    }

    private static float HorizontalDistanceToTerrain(Terrain terrain, Vector3 point)
    {
        Vector3 local = point - terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float dx = Mathf.Max(0f, Mathf.Max(-local.x, local.x - size.x));
        float dz = Mathf.Max(0f, Mathf.Max(-local.z, local.z - size.z));
        return dx * dx + dz * dz;
    }

    private static void CreateBackup(TerrainData data)
    {
        EnsureFolder(BackupFolder);
        string path = AssetDatabase.GetAssetPath(data);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backup = BackupFolder + "/" + data.name + "_before_entrance_only_" + stamp + ".asset";
        if (!AssetDatabase.CopyAsset(path, backup))
            throw new IOException("Could not create TerrainData backup at " + backup);
        Debug.Log("[TunnelEntranceHoleOnly] Backup: " + backup);
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
