using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the "2안 컬러" concept palette (blue grey + dusty beige + mint point)
/// to the terrain layers, terrain detail (grass) tints and foliage materials of
/// the currently open scene.
///
/// TerrainData is serialised as binary, so grass/detail colours cannot be edited
/// as text - they have to go through the Terrain API, which is why this lives in
/// an editor tool instead of a direct asset edit.
///
/// Global colour grading (Volume profile) is handled separately in
/// Assets/3.Resources/Scenes/Shelter_Grey_Boxing/Global Volume Profile.asset.
/// </summary>
public static class GrayZoneConceptColorApplier
{
    const string BackupDir = "Assets/Generated/ConceptColor";
    const string BackupPath = BackupDir + "/ConceptColorBackup.json";

    // --- concept palette (sRGB) -------------------------------------------------
    // 메인 컬러: 블루 그레이 -> 더스티 베이지 -> 오프화이트
    static readonly Color GrassHealthy = new Color32(0x65, 0x6B, 0x5C, 0xFF); // 탁한 올리브 그레이
    static readonly Color GrassDry     = new Color32(0x7C, 0x77, 0x67, 0xFF); // 더스티 베이지
    static readonly Color GrassWaving  = new Color32(0x6E, 0x72, 0x65, 0xFF);
    static readonly Color FoliageTint  = new Color32(0x97, 0xA0, 0x98, 0xFF); // 채도 뺀 냉한 녹회색

    // terrain layer targets, expressed as hue/saturation overrides in HSV
    const float DirtHue = 42f / 360f;   // 더스티 베이지
    const float RockHue = 210f / 360f;  // 블루 그레이
    const float GrassHue = 75f / 360f;  // 올리브
    const float DarkValueThreshold = 0.28f;

    static readonly string[] FoliageHints =
    {
        "leaf", "leaves", "foliage", "frond", "needle", "branch",
        "grass", "plant", "bush", "shrub", "vegetation", "speedtree", "canopy"
    };

    // ---------------------------------------------------------------------------

    [MenuItem("Tools/GrayZone/Concept Color/1. Audit Terrain And Vegetation Colors")]
    public static void Audit() { Run(dryRun: true); }

    [MenuItem("Tools/GrayZone/Concept Color/2. Apply Concept Colors (2안)")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("컨셉 컬러 적용",
                "열려 있는 씬의 터레인 레이어 / 잔디 / 식생 머티리얼 색을 2안 컨셉 팔레트로 변경합니다.\n\n" +
                "원본 값은 " + BackupPath + " 에 백업되며 3번 메뉴로 되돌릴 수 있습니다.\n\n계속할까요?",
                "적용", "취소"))
            return;

        Run(dryRun: false);
    }

    [MenuItem("Tools/GrayZone/Concept Color/3. Revert Concept Colors")]
    public static void Revert()
    {
        if (!File.Exists(BackupPath))
        {
            EditorUtility.DisplayDialog("되돌리기", "백업 파일이 없습니다: " + BackupPath, "확인");
            return;
        }

        var backup = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        var log = new StringBuilder("[ConceptColor] Revert\n");

        foreach (var e in backup.layers)
        {
            var layer = LoadByGuid<TerrainLayer>(e.guid);
            if (layer == null) { log.AppendLine("  missing layer " + e.guid); continue; }
            Undo.RecordObject(layer, "Revert Concept Color");
            layer.diffuseRemapMin = e.remapMin;
            layer.diffuseRemapMax = e.remapMax;
            EditorUtility.SetDirty(layer);
            log.AppendLine("  layer  " + layer.name);
        }

        foreach (var e in backup.terrains)
        {
            var data = LoadByGuid<TerrainData>(e.guid);
            if (data == null) { log.AppendLine("  missing terrainData " + e.guid); continue; }
            Undo.RecordObject(data, "Revert Concept Color");
            var protos = data.detailPrototypes;
            for (int i = 0; i < protos.Length && i < e.healthy.Length; i++)
            {
                protos[i].healthyColor = e.healthy[i];
                protos[i].dryColor = e.dry[i];
            }
            data.detailPrototypes = protos;
            data.wavingGrassTint = e.wavingGrassTint;
            EditorUtility.SetDirty(data);
            log.AppendLine("  terrain " + data.name);
        }

        foreach (var e in backup.materials)
        {
            var mat = LoadByGuid<Material>(e.guid);
            if (mat == null) { log.AppendLine("  missing material " + e.guid); continue; }
            Undo.RecordObject(mat, "Revert Concept Color");
            if (mat.HasProperty(e.property)) mat.SetColor(e.property, e.color);
            EditorUtility.SetDirty(mat);
            log.AppendLine("  mat    " + mat.name + "." + e.property);
        }

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("되돌리기", "원본 색상으로 복구했습니다. 콘솔 로그를 확인하세요.", "확인");
    }

    // ---------------------------------------------------------------------------

    static void Run(bool dryRun)
    {
        var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (terrains.Length == 0)
        {
            EditorUtility.DisplayDialog("컨셉 컬러", "열려 있는 씬에 Terrain이 없습니다.", "확인");
            return;
        }

        var backup = new Backup();
        var log = new StringBuilder();
        log.AppendLine(dryRun ? "[ConceptColor] AUDIT (변경 없음)" : "[ConceptColor] APPLY");
        log.AppendLine("terrains: " + terrains.Length);

        var seenLayers = new HashSet<TerrainLayer>();
        var seenData = new HashSet<TerrainData>();
        var foliageMaterials = new HashSet<Material>();

        foreach (var terrain in terrains)
        {
            var data = terrain.terrainData;
            if (data == null || !seenData.Add(data)) continue;

            log.AppendLine("\n=== TerrainData: " + data.name + " ===");

            // --- terrain layers ---------------------------------------------
            foreach (var layer in data.terrainLayers)
            {
                if (layer == null || !seenLayers.Add(layer)) continue;

                Color avg = AverageAlbedo(layer.diffuseTexture);
                Vector4 oldMax = layer.diffuseRemapMax;
                Vector4 newMax = BuildRemap(avg, oldMax, out string kind);

                log.AppendFormat("  layer {0,-28} avg #{1} [{2}]  remap {3} -> {4}\n",
                    layer.name, ColorUtility.ToHtmlStringRGB(avg), kind,
                    Fmt(oldMax), Fmt(newMax));

                backup.layers.Add(new LayerEntry
                {
                    guid = GuidOf(layer),
                    remapMin = layer.diffuseRemapMin,
                    remapMax = oldMax
                });

                if (!dryRun)
                {
                    Undo.RecordObject(layer, "Apply Concept Color");
                    layer.diffuseRemapMin = Vector4.zero;
                    layer.diffuseRemapMax = newMax;
                    EditorUtility.SetDirty(layer);
                }
            }

            // --- detail (grass) prototypes ----------------------------------
            var protos = data.detailPrototypes;
            var entry = new TerrainEntry
            {
                guid = GuidOf(data),
                wavingGrassTint = data.wavingGrassTint,
                healthy = protos.Select(p => p.healthyColor).ToArray(),
                dry = protos.Select(p => p.dryColor).ToArray()
            };
            backup.terrains.Add(entry);

            log.AppendFormat("  wavingGrassTint #{0} -> #{1}\n",
                ColorUtility.ToHtmlStringRGB(data.wavingGrassTint),
                ColorUtility.ToHtmlStringRGB(GrassWaving));

            for (int i = 0; i < protos.Length; i++)
            {
                string who = protos[i].prototype != null ? protos[i].prototype.name
                           : protos[i].prototypeTexture != null ? protos[i].prototypeTexture.name
                           : "detail " + i;
                log.AppendFormat("  detail {0,-26} healthy #{1} -> #{2} | dry #{3} -> #{4}\n",
                    who,
                    ColorUtility.ToHtmlStringRGB(protos[i].healthyColor), ColorUtility.ToHtmlStringRGB(GrassHealthy),
                    ColorUtility.ToHtmlStringRGB(protos[i].dryColor), ColorUtility.ToHtmlStringRGB(GrassDry));

                protos[i].healthyColor = GrassHealthy;
                protos[i].dryColor = GrassDry;

                CollectFoliageMaterials(protos[i].prototype, foliageMaterials);
            }

            if (!dryRun)
            {
                Undo.RecordObject(data, "Apply Concept Color");
                data.detailPrototypes = protos;
                data.wavingGrassTint = GrassWaving;
                EditorUtility.SetDirty(data);
            }

            // --- tree prototypes --------------------------------------------
            foreach (var tree in data.treePrototypes)
                CollectFoliageMaterials(tree.prefab, foliageMaterials);
        }

        // --- foliage placed directly in the scene ---------------------------
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var mat in r.sharedMaterials)
                if (IsFoliage(mat)) foliageMaterials.Add(mat);

        log.AppendLine("\n=== Foliage materials: " + foliageMaterials.Count + " ===");
        foreach (var mat in foliageMaterials.OrderBy(m => m.name))
        {
            string prop = mat.HasProperty("_BaseColor") ? "_BaseColor"
                        : mat.HasProperty("_Color") ? "_Color" : null;
            if (prop == null) { log.AppendLine("  skip (no color prop) " + mat.name); continue; }

            Color old = mat.GetColor(prop);
            Color tinted = new Color(old.r * FoliageTint.r, old.g * FoliageTint.g, old.b * FoliageTint.b, old.a);

            log.AppendFormat("  mat {0,-34} {1} #{2} -> #{3}\n",
                mat.name, prop, ColorUtility.ToHtmlStringRGB(old), ColorUtility.ToHtmlStringRGB(tinted));

            backup.materials.Add(new MaterialEntry { guid = GuidOf(mat), property = prop, color = old });

            if (!dryRun)
            {
                Undo.RecordObject(mat, "Apply Concept Color");
                mat.SetColor(prop, tinted);
                EditorUtility.SetDirty(mat);
            }
        }

        if (!dryRun)
        {
            Directory.CreateDirectory(BackupDir);
            File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            log.AppendLine("\nbackup written: " + BackupPath);
        }

        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("컨셉 컬러",
            (dryRun ? "감사(Audit) 완료 - 변경 없음.\n" : "적용 완료.\n") + "상세 내역은 콘솔 로그를 확인하세요.", "확인");
    }

    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a _DiffuseRemapScale that pushes the layer's average albedo onto the
    /// concept palette: keeps the layer's brightness, strips most of the chroma and
    /// re-hues it to dusty beige (ground), blue grey (dark rock) or olive (green).
    /// </summary>
    static Vector4 BuildRemap(Color avg, Vector4 oldMax, out string kind)
    {
        Color.RGBToHSV(avg, out float h, out float s, out float v);
        float hueDeg = h * 360f;

        float targetHue, targetSat;
        if (s > 0.10f && hueDeg >= 60f && hueDeg <= 180f)
        {
            kind = "green";
            targetHue = GrassHue;
            targetSat = Mathf.Min(s * 0.25f, 0.10f);
        }
        else if (v < DarkValueThreshold)
        {
            kind = "dark/rock";
            targetHue = RockHue;
            targetSat = Mathf.Min(s * 0.35f, 0.07f);
        }
        else
        {
            kind = "ground";
            targetHue = DirtHue;
            targetSat = Mathf.Min(s * 0.35f, 0.08f);
        }

        Color target = Color.HSVToRGB(targetHue, targetSat, v * 0.95f);

        // the shader multiplies in linear space, so build the ratio there
        Color avgLin = avg.linear;
        Color tgtLin = target.linear;
        const float eps = 0.0025f;
        var scale = new Vector4(
            Mathf.Clamp(tgtLin.r / Mathf.Max(avgLin.r, eps), 0.2f, 2f),
            Mathf.Clamp(tgtLin.g / Mathf.Max(avgLin.g, eps), 0.2f, 2f),
            Mathf.Clamp(tgtLin.b / Mathf.Max(avgLin.b, eps), 0.2f, 2f),
            oldMax.w); // w drives useOpacityAsDensity - never touch it
        return scale;
    }

    /// <summary>Average colour of a texture, via a blit so non-readable assets work too.</summary>
    static Color AverageAlbedo(Texture texture)
    {
        if (texture == null) return Color.grey;

        const int size = 64;
        var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(texture, rt);
        RenderTexture.active = rt;

        var tmp = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        tmp.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tmp.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        var pixels = tmp.GetPixels();
        UnityEngine.Object.DestroyImmediate(tmp);

        float r = 0f, g = 0f, b = 0f;
        foreach (var p in pixels) { r += p.r; g += p.g; b += p.b; }
        int n = Mathf.Max(pixels.Length, 1);
        return new Color(r / n, g / n, b / n, 1f);
    }

    static void CollectFoliageMaterials(GameObject prefab, HashSet<Material> into)
    {
        if (prefab == null) return;
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in r.sharedMaterials)
                if (IsFoliage(mat)) into.Add(mat);
    }

    static bool IsFoliage(Material mat)
    {
        if (mat == null) return false;
        string name = mat.name.ToLowerInvariant();
        string shader = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;
        return FoliageHints.Any(h => name.Contains(h) || shader.Contains(h));
    }

    static string GuidOf(UnityEngine.Object obj)
    {
        return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj));
    }

    static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
    }

    static string Fmt(Vector4 v)
    {
        return string.Format("({0:0.00},{1:0.00},{2:0.00},{3:0.00})", v.x, v.y, v.z, v.w);
    }

    // --- backup payload --------------------------------------------------------

    [Serializable] class LayerEntry { public string guid; public Vector4 remapMin; public Vector4 remapMax; }
    [Serializable] class TerrainEntry { public string guid; public Color wavingGrassTint; public Color[] healthy; public Color[] dry; }
    [Serializable] class MaterialEntry { public string guid; public string property; public Color color; }

    [Serializable]
    class Backup
    {
        public List<LayerEntry> layers = new List<LayerEntry>();
        public List<TerrainEntry> terrains = new List<TerrainEntry>();
        public List<MaterialEntry> materials = new List<MaterialEntry>();
    }
}
