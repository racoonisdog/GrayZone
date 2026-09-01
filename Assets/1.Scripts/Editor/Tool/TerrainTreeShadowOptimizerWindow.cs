using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Terrain Tree 프로토타입을 프로젝트 소유 프리팹으로 복제하고 LOD별 그림자 정책을 적용합니다.
/// </summary>
/// <remarks>
/// 원본 프리팹은 수정하지 않습니다. 선택한 TerrainData의 Tree Prototype 참조만 새 복제본으로 교체합니다.
/// TerrainData 참조 교체는 Undo할 수 있지만 새로 생성된 프리팹 에셋은 Undo 시 자동 삭제되지 않습니다.
/// </remarks>
public sealed class TerrainTreeShadowOptimizerWindow : EditorWindow
{
    private const string DefaultOutputFolder = "Assets/2.Prefabs/Optimization/TerrainTrees";

    private enum ShadowPolicy
    {
        AllOff,
        Lod1AndBeyondOff,
        LastLodOff,
    }

    private sealed class PrototypeEntry
    {
        public int index;
        public GameObject prefab;
        public string assetPath;
        public int instanceCount;
        public int rendererCount;
        public int shadowCasterCount;
        public int lodGroupCount;
        public bool selected;
        public ShadowPolicy policy;
    }

    private struct Replacement
    {
        public int prototypeIndex;
        public GameObject variantPrefab;
        public string variantPath;
        public int changedRendererCount;
    }

    private static readonly GUIContent[] s_policyLabels =
    {
        new GUIContent("전체 Renderer 그림자 OFF"),
        new GUIContent("LOD1 이후 그림자 OFF"),
        new GUIContent("마지막 LOD만 그림자 OFF"),
    };

    [SerializeField] private Terrain m_targetTerrain;
    [SerializeField] private string m_outputFolder = DefaultOutputFolder;

    private readonly List<PrototypeEntry> m_entries = new List<PrototypeEntry>();

    private Vector2 m_scroll;
    private string m_report = string.Empty;
    private MessageType m_reportType = MessageType.Info;

    [MenuItem("Tools/GrayZone/Terrain Tree 그림자 최적화")]
    private static void Open()
    {
        TerrainTreeShadowOptimizerWindow window =
            GetWindow<TerrainTreeShadowOptimizerWindow>("Terrain Tree 그림자");
        window.minSize = new Vector2(720.0f, 520.0f);
        window.Show();
    }

    private void OnEnable()
    {
        if (m_targetTerrain == null && Selection.activeGameObject != null)
        {
            m_targetTerrain = Selection.activeGameObject.GetComponentInParent<Terrain>();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Terrain Tree 그림자 최적화", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "선택한 Terrain의 Tree Prototype을 분석한 뒤, 선택한 원본 프리팹을 프로젝트 소유 폴더에 복제하고 "
            + "복제본에만 그림자 정책을 적용합니다. 서드파티 원본 프리팹은 수정하지 않습니다.",
            MessageType.Info);
        EditorGUILayout.HelpBox(
            "실행하면 TerrainData의 Tree Prototype 참조가 변경됩니다. Undo로 참조는 복원할 수 있지만, "
            + "새로 생성된 최적화 프리팹은 검토를 위해 남습니다.",
            MessageType.Warning);

        DrawTarget();
        DrawOutputFolder();
        EditorGUILayout.Space();
        DrawAnalysisControls();
        EditorGUILayout.Space();
        DrawEntries();
        EditorGUILayout.Space();
        DrawExecute();
        DrawReport();
    }

    private void DrawTarget()
    {
        EditorGUI.BeginChangeCheck();
        Terrain nextTerrain = (Terrain)EditorGUILayout.ObjectField(
            "대상 Terrain",
            m_targetTerrain,
            typeof(Terrain),
            true);

        if (EditorGUI.EndChangeCheck())
        {
            m_targetTerrain = nextTerrain;
            m_entries.Clear();
            m_report = string.Empty;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("현재 선택에서 가져오기", GUILayout.Width(160.0f)))
            {
                m_targetTerrain = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponentInParent<Terrain>()
                    : null;
                m_entries.Clear();
                m_report = string.Empty;
            }
        }

        if (m_targetTerrain != null && m_targetTerrain.terrainData != null)
        {
            string terrainDataPath = AssetDatabase.GetAssetPath(m_targetTerrain.terrainData);
            EditorGUILayout.LabelField($"TerrainData: {terrainDataPath}", EditorStyles.miniLabel);
        }
    }

    private void DrawOutputFolder()
    {
        m_outputFolder = EditorGUILayout.TextField("복제본 출력 폴더", m_outputFolder);
        EditorGUILayout.LabelField(
            "기존 에셋을 덮어쓰지 않으며, 같은 이름이 있으면 고유한 새 이름을 생성합니다.",
            EditorStyles.miniLabel);
    }

    private void DrawAnalysisControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(m_targetTerrain == null))
            {
                if (GUILayout.Button("[1] Tree Prototype 분석", GUILayout.Height(26.0f)))
                {
                    AnalyzeTerrain();
                }
            }

            using (new EditorGUI.DisabledScope(m_entries.Count == 0))
            {
                if (GUILayout.Button("권장 초안 선택", GUILayout.Width(130.0f), GUILayout.Height(26.0f)))
                {
                    ApplyRecommendedDraft();
                }

                if (GUILayout.Button("선택 해제", GUILayout.Width(90.0f), GUILayout.Height(26.0f)))
                {
                    ClearSelection();
                }
            }
        }

        if (m_entries.Count > 0)
        {
            int totalInstances = 0;
            int selectedPrototypes = 0;
            int selectedInstances = 0;

            for (int i = 0; i < m_entries.Count; i++)
            {
                PrototypeEntry entry = m_entries[i];
                totalInstances += entry.instanceCount;
                if (entry.selected)
                {
                    selectedPrototypes++;
                    selectedInstances += entry.instanceCount;
                }
            }

            EditorGUILayout.LabelField(
                $"프로토타입 {m_entries.Count}개 / 배치 인스턴스 {totalInstances:N0}개 / "
                + $"선택 {selectedPrototypes}개 ({selectedInstances:N0} 인스턴스)",
                EditorStyles.miniLabel);
        }
    }

    private void DrawEntries()
    {
        if (m_entries.Count == 0)
        {
            EditorGUILayout.HelpBox("대상 Terrain을 지정하고 먼저 Tree Prototype을 분석하세요.", MessageType.None);
            return;
        }

        EditorGUILayout.LabelField("[2] 적용 대상과 정책 선택", EditorStyles.boldLabel);
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll, GUILayout.MinHeight(220.0f));

        for (int i = 0; i < m_entries.Count; i++)
        {
            DrawEntry(m_entries[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    private static void DrawEntry(PrototypeEntry entry)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(18.0f));
                string prefabName = entry.prefab != null ? entry.prefab.name : "<Missing Prefab>";
                EditorGUILayout.LabelField(
                    $"[{entry.index}] {prefabName}",
                    EditorStyles.boldLabel,
                    GUILayout.MinWidth(220.0f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    $"배치 {entry.instanceCount:N0} / 그림자 Renderer {entry.shadowCasterCount}/{entry.rendererCount}",
                    GUILayout.Width(250.0f));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(22.0f);
                EditorGUILayout.LabelField(entry.assetPath, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"LODGroup {entry.lodGroupCount}", GUILayout.Width(90.0f));
                entry.policy = (ShadowPolicy)EditorGUILayout.Popup(
                    (int)entry.policy,
                    s_policyLabels,
                    GUILayout.Width(190.0f));
            }

            if (entry.prefab == null)
            {
                EditorGUILayout.HelpBox("Prefab 참조가 없어 처리할 수 없습니다.", MessageType.Error);
            }
            else if (entry.lodGroupCount == 0 && entry.policy != ShadowPolicy.AllOff)
            {
                EditorGUILayout.HelpBox(
                    "LODGroup이 없으므로 LOD별 정책을 적용할 수 없습니다. 전체 Renderer OFF를 사용하세요.",
                    MessageType.Warning);
            }
        }
    }

    private void DrawExecute()
    {
        int selectedCount = CountSelectedEntries();
        bool canExecute = m_targetTerrain != null
            && m_entries.Count > 0
            && selectedCount > 0
            && !EditorApplication.isPlayingOrWillChangePlaymode;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("Play Mode에서는 실행할 수 없습니다.", MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!canExecute))
        {
            if (GUILayout.Button(
                $"[3] 최적화 복제본 생성 및 Terrain Prototype 교체 ({selectedCount}개)",
                GUILayout.Height(32.0f)))
            {
                ConfirmAndExecute();
            }
        }
    }

    private void DrawReport()
    {
        if (!string.IsNullOrEmpty(m_report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(m_report, m_reportType);
        }
    }

    private void AnalyzeTerrain()
    {
        m_entries.Clear();
        m_report = string.Empty;

        if (m_targetTerrain == null || m_targetTerrain.terrainData == null)
        {
            SetReport("유효한 Terrain과 TerrainData를 지정하세요.", MessageType.Error);
            return;
        }

        TerrainData terrainData = m_targetTerrain.terrainData;
        TreePrototype[] prototypes = terrainData.treePrototypes;
        int[] instanceCounts = new int[prototypes.Length];

        TreeInstance[] instances = terrainData.treeInstances;
        for (int i = 0; i < instances.Length; i++)
        {
            int prototypeIndex = instances[i].prototypeIndex;
            if (prototypeIndex >= 0 && prototypeIndex < instanceCounts.Length)
            {
                instanceCounts[prototypeIndex]++;
            }
        }

        for (int i = 0; i < prototypes.Length; i++)
        {
            GameObject prefab = prototypes[i].prefab;
            Renderer[] renderers = prefab != null
                ? prefab.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            LODGroup[] lodGroups = prefab != null
                ? prefab.GetComponentsInChildren<LODGroup>(true)
                : Array.Empty<LODGroup>();

            int shadowCasterCount = 0;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (renderers[rendererIndex].shadowCastingMode != ShadowCastingMode.Off)
                {
                    shadowCasterCount++;
                }
            }

            m_entries.Add(new PrototypeEntry
            {
                index = i,
                prefab = prefab,
                assetPath = prefab != null ? AssetDatabase.GetAssetPath(prefab) : string.Empty,
                instanceCount = instanceCounts[i],
                rendererCount = renderers.Length,
                shadowCasterCount = shadowCasterCount,
                lodGroupCount = lodGroups.Length,
                selected = false,
                policy = ShadowPolicy.LastLodOff,
            });
        }

        SetReport(
            $"분석 완료: Tree Prototype {prototypes.Length}개, Tree Instance {instances.Length:N0}개.",
            MessageType.Info);
    }

    private void ApplyRecommendedDraft()
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            PrototypeEntry entry = m_entries[i];
            entry.selected = entry.prefab != null && entry.shadowCasterCount > 0;

            if (entry.prefab == null)
            {
                continue;
            }

            string prefabName = entry.prefab.name;
            bool looksLikeStump = prefabName.IndexOf("Cutted", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("Stump", StringComparison.OrdinalIgnoreCase) >= 0;

            entry.policy = looksLikeStump || entry.lodGroupCount == 0
                ? ShadowPolicy.AllOff
                : ShadowPolicy.LastLodOff;
        }

        SetReport(
            "권장 초안을 적용했습니다. 그루터기 계열은 전체 OFF, 그 외 그림자 사용 프로토타입은 마지막 LOD OFF입니다. "
            + "실행 전에 각 행의 선택과 정책을 검토하세요.",
            MessageType.Info);
    }

    private void ClearSelection()
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            m_entries[i].selected = false;
        }
    }

    private int CountSelectedEntries()
    {
        int count = 0;
        for (int i = 0; i < m_entries.Count; i++)
        {
            if (m_entries[i].selected)
            {
                count++;
            }
        }

        return count;
    }

    private void ConfirmAndExecute()
    {
        string validationError = ValidateExecution();
        if (!string.IsNullOrEmpty(validationError))
        {
            SetReport(validationError, MessageType.Error);
            return;
        }

        int selectedCount = CountSelectedEntries();
        bool proceed = EditorUtility.DisplayDialog(
            "Terrain Tree 그림자 최적화",
            $"선택한 {selectedCount}개 Tree Prototype의 복제본을 생성하고 TerrainData 참조를 교체합니다.\n\n"
            + $"TerrainData: {AssetDatabase.GetAssetPath(m_targetTerrain.terrainData)}\n"
            + $"출력 폴더: {NormalizeAssetPath(m_outputFolder)}\n\n"
            + "서드파티 원본은 수정하지 않습니다. 계속하시겠습니까?",
            "생성 및 교체",
            "취소");

        if (proceed)
        {
            ExecuteOptimization();
        }
    }

    private string ValidateExecution()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return "Play Mode에서는 실행할 수 없습니다.";
        }

        if (m_targetTerrain == null || m_targetTerrain.terrainData == null)
        {
            return "유효한 Terrain과 TerrainData를 지정하세요.";
        }

        string outputFolder = NormalizeAssetPath(m_outputFolder);
        if (!IsValidAssetsFolder(outputFolder))
        {
            return "출력 폴더는 Assets 또는 Assets/ 하위 경로여야 하며 '..'을 포함할 수 없습니다.";
        }

        TreePrototype[] currentPrototypes = m_targetTerrain.terrainData.treePrototypes;
        if (currentPrototypes.Length != m_entries.Count)
        {
            return "분석 이후 Terrain Tree Prototype 구성이 변경되었습니다. 다시 분석하세요.";
        }

        for (int i = 0; i < m_entries.Count; i++)
        {
            PrototypeEntry entry = m_entries[i];
            if (!entry.selected)
            {
                continue;
            }

            if (entry.prefab == null || currentPrototypes[entry.index].prefab != entry.prefab)
            {
                return $"Prototype [{entry.index}] 구성이 분석 이후 변경되었거나 Prefab이 없습니다. 다시 분석하세요.";
            }

            if (string.IsNullOrEmpty(entry.assetPath)
                || !entry.assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !entry.assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return $"'{entry.prefab.name}'은 복제 가능한 Assets 내 Prefab이 아닙니다.";
            }

            if (entry.policy != ShadowPolicy.AllOff && entry.lodGroupCount == 0)
            {
                return $"'{entry.prefab.name}'에는 LODGroup이 없습니다. 전체 Renderer 그림자 OFF를 선택하세요.";
            }
        }

        return string.Empty;
    }

    private void ExecuteOptimization()
    {
        string outputFolder = NormalizeAssetPath(m_outputFolder);
        var createdPaths = new List<string>();
        var replacements = new List<Replacement>();

        try
        {
            EnsureAssetFolderExists(outputFolder);

            for (int i = 0; i < m_entries.Count; i++)
            {
                PrototypeEntry entry = m_entries[i];
                if (!entry.selected)
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(entry.assetPath)
                    + "_TerrainShadow_"
                    + GetPolicySuffix(entry.policy)
                    + ".prefab";
                string destinationPath = AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/" + fileName);

                if (!AssetDatabase.CopyAsset(entry.assetPath, destinationPath))
                {
                    throw new InvalidOperationException($"Prefab 복제 실패: {entry.assetPath}");
                }

                createdPaths.Add(destinationPath);
                int changedRendererCount = ApplyPolicyToPrefab(destinationPath, entry.policy);
                GameObject variantPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath);
                if (variantPrefab == null)
                {
                    throw new InvalidOperationException($"생성된 Prefab을 불러오지 못했습니다: {destinationPath}");
                }

                replacements.Add(new Replacement
                {
                    prototypeIndex = entry.index,
                    variantPrefab = variantPrefab,
                    variantPath = destinationPath,
                    changedRendererCount = changedRendererCount,
                });
            }

            TerrainData terrainData = m_targetTerrain.terrainData;
            Undo.RegisterCompleteObjectUndo(terrainData, "Terrain Tree 그림자 최적화");

            TreePrototype[] prototypes = terrainData.treePrototypes;
            for (int i = 0; i < replacements.Count; i++)
            {
                Replacement replacement = replacements[i];
                prototypes[replacement.prototypeIndex].prefab = replacement.variantPrefab;
            }

            terrainData.treePrototypes = prototypes;
            EditorUtility.SetDirty(terrainData);
            AssetDatabase.SaveAssets();
            m_targetTerrain.Flush();
            SceneView.RepaintAll();

            int changedRendererTotal = 0;
            for (int i = 0; i < replacements.Count; i++)
            {
                changedRendererTotal += replacements[i].changedRendererCount;
                Debug.Log(
                    $"[Terrain Tree 그림자] Prototype [{replacements[i].prototypeIndex}] -> "
                    + $"{replacements[i].variantPath} (변경 Renderer {replacements[i].changedRendererCount})",
                    m_targetTerrain);
            }

            AnalyzeTerrain();
            SetReport(
                $"완료: 최적화 Prefab {replacements.Count}개 생성, Renderer 그림자 설정 {changedRendererTotal}건 변경, "
                + "Terrain Prototype 교체 완료. Game View와 Profiler에서 동일 시점 A/B를 확인하세요.",
                MessageType.Info);
        }
        catch (Exception exception)
        {
            for (int i = 0; i < createdPaths.Count; i++)
            {
                AssetDatabase.DeleteAsset(createdPaths[i]);
            }

            AssetDatabase.Refresh();
            SetReport($"실행 실패: {exception.Message}\n생성 중이던 복제본은 정리했습니다.", MessageType.Error);
            Debug.LogException(exception);
        }
    }

    private static int ApplyPolicyToPrefab(string prefabPath, ShadowPolicy policy)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            throw new InvalidOperationException($"Prefab Contents를 열지 못했습니다: {prefabPath}");
        }

        try
        {
            var targets = new HashSet<Renderer>();

            if (policy == ShadowPolicy.AllOff)
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    targets.Add(renderers[i]);
                }
            }
            else
            {
                LODGroup[] lodGroups = root.GetComponentsInChildren<LODGroup>(true);
                if (lodGroups.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"LOD별 정책을 선택했지만 LODGroup이 없습니다: {prefabPath}");
                }

                for (int groupIndex = 0; groupIndex < lodGroups.Length; groupIndex++)
                {
                    LOD[] lods = lodGroups[groupIndex].GetLODs();
                    int firstOffIndex = policy == ShadowPolicy.LastLodOff
                        ? lods.Length - 1
                        : 1;

                    for (int lodIndex = firstOffIndex; lodIndex < lods.Length; lodIndex++)
                    {
                        Renderer[] renderers = lods[lodIndex].renderers;
                        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                        {
                            if (renderers[rendererIndex] != null)
                            {
                                targets.Add(renderers[rendererIndex]);
                            }
                        }
                    }
                }
            }

            int changedRendererCount = 0;
            foreach (Renderer renderer in targets)
            {
                if (renderer.shadowCastingMode == ShadowCastingMode.Off)
                {
                    continue;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                changedRendererCount++;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return changedRendererCount;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureAssetFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                string guid = AssetDatabase.CreateFolder(current, parts[i]);
                if (string.IsNullOrEmpty(guid))
                {
                    throw new InvalidOperationException($"출력 폴더를 만들지 못했습니다: {next}");
                }
            }

            current = next;
        }
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/').TrimEnd('/');
    }

    private static bool IsValidAssetsFolder(string path)
    {
        return (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal))
            && path.IndexOf("..", StringComparison.Ordinal) < 0;
    }

    private static string GetPolicySuffix(ShadowPolicy policy)
    {
        switch (policy)
        {
            case ShadowPolicy.AllOff:
                return "AllOff";
            case ShadowPolicy.Lod1AndBeyondOff:
                return "LOD1PlusOff";
            case ShadowPolicy.LastLodOff:
                return "LastLODOff";
            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, null);
        }
    }

    private void SetReport(string message, MessageType type)
    {
        m_report = message;
        m_reportType = type;
        Repaint();
    }
}
