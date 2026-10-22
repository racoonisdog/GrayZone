using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 리소스 폴더 아래에서 고른 폴더의 텍스처에 Windows, Mac, Linux(Standalone) 플랫폼 오버라이드와 압축 포맷을 일괄 적용합니다.
/// </summary>
/// <remarks>
/// 임포터 설정 변경은 Undo되지 않습니다. 이미 목표 설정과 같은 텍스처는 건드리지 않아 불필요한 재임포트를 막습니다.
/// <para>
/// "압축 안함"은 오버라이드에 명시 포맷이 필요하므로 알파가 있으면 RGBA32, 없으면 RGB24로 지정합니다.
/// </para>
/// <para>
/// 노멀맵은 Unity가 DXT1을 허용하지 않으므로 기본으로 압축을 고르면 BC5(RG 2채널, Z는 셰이더에서 복원)로 대체하고,
/// "압축 안함"은 RGBA32로 지정합니다. 그 밖에 텍스처 타입이 허용하지 않는 포맷은 건드리지 않고 건너뜁니다.
/// </para>
/// </remarks>
public sealed class StandaloneTextureCompressionWindow : EditorWindow
{
    private const string DefaultRootFolder = "Assets/3.Resources";
    private const string StandalonePlatform = "Standalone";
    private const string RootPrefKey = "GrayZone.StandaloneTextureCompression.Root";
    private const float IndentWidth = 14.0f;

    private enum CompressionChoice
    {
        Dxt1,
        Dxt5,
        None,
    }

    private static readonly GUIContent[] s_compressionLabels =
    {
        new GUIContent("DXT1 (RGB, 알파 없음)"),
        new GUIContent("DXT5 (RGBA, 알파 포함)"),
        new GUIContent("압축 안함 (RGBA32 / RGB24)"),
    };

    [SerializeField] private string m_rootFolder = DefaultRootFolder;
    [SerializeField] private bool m_overrideStandalone = true;
    [SerializeField] private CompressionChoice m_compression = CompressionChoice.Dxt5;
    [SerializeField] private bool m_includeSubfolders = true;
    [SerializeField] private bool m_excludeDeprecated = true;
    [SerializeField] private bool m_normalMapUseBc5 = true;

    private readonly HashSet<string> m_checkedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_expandedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> m_subFolderCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> m_scannedPaths = new List<string>();
    private int m_scannedChangeCount;
    private int m_scannedUnsupportedCount;
    private bool m_hasScan;

    private Vector2 m_scroll;
    private string m_report = string.Empty;
    private MessageType m_reportType = MessageType.Info;

    [MenuItem("Tools/GrayZone/Texture/Standalone 텍스처 압축 설정")]
    private static void Open()
    {
        StandaloneTextureCompressionWindow window =
            GetWindow<StandaloneTextureCompressionWindow>("텍스처 압축 설정");
        window.minSize = new Vector2(460.0f, 520.0f);
        window.Show();
    }

    private void OnEnable()
    {
        m_rootFolder = EditorPrefs.GetString(RootPrefKey, DefaultRootFolder);
        if (!AssetDatabase.IsValidFolder(m_rootFolder))
        {
            m_rootFolder = DefaultRootFolder;
        }

        m_expandedFolders.Add(m_rootFolder);
    }

    private void OnProjectChange()
    {
        // 폴더가 추가·삭제되면 트리 캐시가 낡으므로 비웁니다.
        m_subFolderCache.Clear();
        InvalidateScan();
        Repaint();
    }

    private void OnGUI()
    {
        DrawRootField();
        EditorGUILayout.Space();
        DrawOptions();
        EditorGUILayout.Space();
        DrawFolderTree();
        EditorGUILayout.Space();
        DrawActions();

        if (!string.IsNullOrEmpty(m_report))
        {
            EditorGUILayout.HelpBox(m_report, m_reportType);
        }
    }

    private void DrawRootField()
    {
        EditorGUILayout.LabelField("기준 리소스 폴더", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            DefaultAsset current = AssetDatabase.LoadAssetAtPath<DefaultAsset>(m_rootFolder);
            DefaultAsset picked = (DefaultAsset)EditorGUILayout.ObjectField(current, typeof(DefaultAsset), false);
            if (picked != current && picked != null)
            {
                SetRootFolder(AssetDatabase.GetAssetPath(picked));
            }

            if (GUILayout.Button("기본값", GUILayout.Width(56.0f)))
            {
                SetRootFolder(DefaultRootFolder);
            }
        }

        EditorGUILayout.LabelField(m_rootFolder, EditorStyles.miniLabel);
    }

    private void DrawOptions()
    {
        EditorGUILayout.LabelField("적용 옵션", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        m_overrideStandalone = EditorGUILayout.ToggleLeft("Override For Windows, Mac, Linux", m_overrideStandalone);

        using (new EditorGUI.DisabledScope(!m_overrideStandalone))
        using (new EditorGUI.IndentLevelScope())
        {
            for (int i = 0; i < s_compressionLabels.Length; i++)
            {
                CompressionChoice choice = (CompressionChoice)i;
                bool isOn = m_compression == choice;
                // 토글 모양을 유지하되 하나만 켜지도록 라디오처럼 동작시킵니다.
                if (EditorGUILayout.ToggleLeft(s_compressionLabels[i], isOn) && !isOn)
                {
                    m_compression = choice;
                }
            }
        }

        if (!m_overrideStandalone)
        {
            EditorGUILayout.HelpBox("오버라이드를 끄면 대상 텍스처의 Standalone 오버라이드가 해제되어 Default 설정을 따릅니다.", MessageType.None);
        }

        using (new EditorGUI.DisabledScope(!m_overrideStandalone))
        using (new EditorGUI.IndentLevelScope())
        {
            m_normalMapUseBc5 = EditorGUILayout.ToggleLeft("노멀맵은 BC5로 대체 (끄면 DXT5는 DXT5nm으로 적용, DXT1은 건너뜀)", m_normalMapUseBc5);
        }

        EditorGUILayout.Space(4.0f);
        m_includeSubfolders = EditorGUILayout.ToggleLeft("하위 폴더 포함", m_includeSubfolders);
        m_excludeDeprecated = EditorGUILayout.ToggleLeft("Deprecated 폴더 제외", m_excludeDeprecated);

        if (EditorGUI.EndChangeCheck())
        {
            InvalidateScan();
        }
    }

    private void DrawFolderTree()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"대상 폴더 ({m_checkedFolders.Count}개 선택)", EditorStyles.boldLabel);

            if (GUILayout.Button("전체 해제", EditorStyles.miniButton, GUILayout.Width(64.0f)))
            {
                m_checkedFolders.Clear();
                InvalidateScan();
            }

            if (GUILayout.Button("모두 접기", EditorStyles.miniButton, GUILayout.Width(64.0f)))
            {
                m_expandedFolders.Clear();
                m_expandedFolders.Add(m_rootFolder);
            }
        }

        if (!AssetDatabase.IsValidFolder(m_rootFolder))
        {
            EditorGUILayout.HelpBox($"폴더를 찾을 수 없습니다: {m_rootFolder}", MessageType.Error);
            return;
        }

        using (EditorGUILayout.ScrollViewScope scope = new EditorGUILayout.ScrollViewScope(m_scroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            m_scroll = scope.scrollPosition;
            DrawFolderNode(m_rootFolder, 0, false);
        }
    }

    /// <param name="inheritedChecked">하위 폴더 포함 옵션에서 조상이 이미 선택되어 이 폴더가 자동 포함되는지 여부입니다.</param>
    private void DrawFolderNode(string folder, int depth, bool inheritedChecked)
    {
        if (m_excludeDeprecated && IsDeprecatedPath(folder))
        {
            return;
        }

        string[] children = GetSubFolders(folder);
        bool expanded = m_expandedFolders.Contains(folder);
        bool isChecked = m_checkedFolders.Contains(folder);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(depth * IndentWidth);

            if (children.Length > 0)
            {
                Rect foldoutRect = GUILayoutUtility.GetRect(IndentWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(IndentWidth));
                bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                if (nextExpanded != expanded)
                {
                    SetExpanded(folder, nextExpanded);
                    expanded = nextExpanded;
                }
            }
            else
            {
                GUILayout.Space(IndentWidth);
            }

            string label = depth == 0 ? folder : System.IO.Path.GetFileName(folder);

            using (new EditorGUI.DisabledScope(inheritedChecked))
            {
                bool nextChecked = EditorGUILayout.ToggleLeft(label, isChecked || inheritedChecked);
                if (!inheritedChecked && nextChecked != isChecked)
                {
                    SetChecked(folder, nextChecked);
                    isChecked = nextChecked;
                }
            }
        }

        if (!expanded)
        {
            return;
        }

        bool childInherited = inheritedChecked || (m_includeSubfolders && isChecked);
        for (int i = 0; i < children.Length; i++)
        {
            DrawFolderNode(children[i], depth + 1, childInherited);
        }
    }

    private void DrawActions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(m_checkedFolders.Count == 0))
            {
                if (GUILayout.Button("대상 스캔", GUILayout.Height(26.0f)))
                {
                    Scan();
                }

                if (GUILayout.Button("적용", GUILayout.Height(26.0f)))
                {
                    Apply();
                }
            }
        }

        if (m_hasScan)
        {
            EditorGUILayout.LabelField(
                $"텍스처 {m_scannedPaths.Count}개 중 변경 필요 {m_scannedChangeCount}개, 포맷 미지원 건너뜀 {m_scannedUnsupportedCount}개 → {DescribeTarget()}",
                EditorStyles.miniLabel);
        }
    }

    private void Scan()
    {
        CollectTextures(m_scannedPaths);

        m_scannedChangeCount = 0;
        m_scannedUnsupportedCount = 0;
        for (int i = 0; i < m_scannedPaths.Count; i++)
        {
            TextureImporter importer = AssetImporter.GetAtPath(m_scannedPaths[i]) as TextureImporter;
            if (importer == null)
            {
                continue;
            }

            if (!IsSupported(importer))
            {
                m_scannedUnsupportedCount++;
            }
            else if (NeedsChange(importer))
            {
                m_scannedChangeCount++;
            }
        }

        m_hasScan = true;
        m_report = string.Empty;
    }

    private void Apply()
    {
        List<string> paths = new List<string>();
        CollectTextures(paths);

        if (paths.Count == 0)
        {
            SetReport("선택한 폴더에 텍스처가 없습니다.", MessageType.Warning);
            return;
        }

        string message = $"텍스처 {paths.Count}개를 검사해 다음 설정으로 바꿉니다.\n\n{DescribeTarget()}\n\n임포터 설정 변경은 Undo되지 않습니다. 진행할까요?";
        if (!EditorUtility.DisplayDialog("Standalone 텍스처 압축 설정", message, "적용", "취소"))
        {
            return;
        }

        int changed = 0;
        int skipped = 0;
        int unsupported = 0;
        List<string> unsupportedPaths = new List<string>();
        bool cancelled = false;

        try
        {
            // 묶어서 재임포트해야 텍스처마다 에셋 DB 갱신이 일어나지 않습니다.
            AssetDatabase.StartAssetEditing();

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar("텍스처 압축 설정", path, (float)i / paths.Count))
                {
                    cancelled = true;
                    break;
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && !IsSupported(importer))
                {
                    unsupported++;
                    unsupportedPaths.Add($"{path} ({importer.textureType})");
                    continue;
                }

                if (importer == null || !NeedsChange(importer))
                {
                    skipped++;
                    continue;
                }

                ApplyTo(importer);
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
        InvalidateScan();

        string result = $"변경 {changed}개, 건너뜀 {skipped}개, 포맷 미지원 {unsupported}개 ({DescribeTarget()})";
        if (cancelled)
        {
            SetReport("중간에 취소했습니다. " + result, MessageType.Warning);
        }
        else
        {
            SetReport(result, MessageType.Info);
        }

        Debug.Log($"[StandaloneTextureCompression] {result}");
        if (unsupportedPaths.Count > 0)
        {
            Debug.LogWarning("[StandaloneTextureCompression] 포맷 미지원으로 건너뛴 텍스처:\n" + string.Join("\n", unsupportedPaths));
        }
    }

    private void CollectTextures(List<string> result)
    {
        result.Clear();

        List<string> folders = new List<string>();
        foreach (string folder in m_checkedFolders)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                folders.Add(folder);
            }
        }

        if (folders.Count == 0)
        {
            return;
        }

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] guids = AssetDatabase.FindAssets("t:Texture", folders.ToArray());
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || !seen.Add(path))
            {
                continue;
            }

            // FindAssets는 항상 하위 폴더까지 찾으므로, 하위 폴더 미포함일 때는 직속 파일만 남깁니다.
            if (!m_includeSubfolders && !m_checkedFolders.Contains(GetParentFolder(path)))
            {
                continue;
            }

            if (m_excludeDeprecated && IsDeprecatedPath(path))
            {
                continue;
            }

            // RenderTexture 등 TextureImporter가 없는 텍스처는 대상이 아닙니다.
            if (AssetImporter.GetAtPath(path) is TextureImporter)
            {
                result.Add(path);
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private bool NeedsChange(TextureImporter importer)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(StandalonePlatform);

        if (!m_overrideStandalone)
        {
            return settings.overridden;
        }

        TryResolveFormat(importer, out TextureImporterFormat format);
        return !settings.overridden || settings.format != format;
    }

    /// <summary>오버라이드를 켠 상태에서 이 텍스처 타입에 넣을 수 있는 포맷이 있는지 확인합니다.</summary>
    private bool IsSupported(TextureImporter importer)
    {
        return !m_overrideStandalone || TryResolveFormat(importer, out _);
    }

    private void ApplyTo(TextureImporter importer)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(StandalonePlatform);
        settings.name = StandalonePlatform;
        settings.overridden = m_overrideStandalone;

        if (m_overrideStandalone)
        {
            TryResolveFormat(importer, out TextureImporterFormat format);
            settings.format = format;
            settings.textureCompression = m_compression == CompressionChoice.None
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.Compressed;
        }

        importer.SetPlatformTextureSettings(settings);
    }

    /// <summary>선택한 압축 옵션을 이 텍스처 타입에 맞는 실제 포맷으로 바꿉니다.</summary>
    /// <returns>Standalone에서 이 텍스처 타입이 허용하는 포맷이면 true입니다.</returns>
    private bool TryResolveFormat(TextureImporter importer, out TextureImporterFormat format)
    {
        bool isNormalMap = importer.textureType == TextureImporterType.NormalMap;
        if (isNormalMap && m_compression == CompressionChoice.None)
        {
            // 노멀맵은 RGB24를 허용하지 않습니다.
            format = TextureImporterFormat.RGBA32;
        }
        else if (isNormalMap && m_normalMapUseBc5)
        {
            format = TextureImporterFormat.BC5;
        }
        else
        {
            switch (m_compression)
            {
                case CompressionChoice.Dxt1:
                    format = TextureImporterFormat.DXT1;
                    break;
                case CompressionChoice.Dxt5:
                    format = TextureImporterFormat.DXT5;
                    break;
                default:
                    bool hasAlpha = importer.alphaSource != TextureImporterAlphaSource.None && importer.DoesSourceTextureHaveAlpha();
                    format = hasAlpha ? TextureImporterFormat.RGBA32 : TextureImporterFormat.RGB24;
                    break;
            }
        }

        return TextureImporter.IsPlatformTextureFormatValid(importer.textureType, BuildTarget.StandaloneWindows64, format);
    }

    private string DescribeTarget()
    {
        if (!m_overrideStandalone)
        {
            return "Standalone 오버라이드 해제";
        }

        return $"Standalone 오버라이드 + {s_compressionLabels[(int)m_compression].text}";
    }

    private void SetRootFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            SetReport($"폴더가 아닙니다: {folder}", MessageType.Warning);
            return;
        }

        m_rootFolder = folder;
        EditorPrefs.SetString(RootPrefKey, folder);
        m_checkedFolders.Clear();
        m_expandedFolders.Clear();
        m_expandedFolders.Add(folder);
        InvalidateScan();
    }

    private void SetChecked(string folder, bool isChecked)
    {
        if (isChecked)
        {
            m_checkedFolders.Add(folder);
        }
        else
        {
            m_checkedFolders.Remove(folder);
        }

        InvalidateScan();
    }

    private void SetExpanded(string folder, bool expanded)
    {
        if (expanded)
        {
            m_expandedFolders.Add(folder);
        }
        else
        {
            m_expandedFolders.Remove(folder);
        }
    }

    private string[] GetSubFolders(string folder)
    {
        if (!m_subFolderCache.TryGetValue(folder, out string[] children))
        {
            children = AssetDatabase.GetSubFolders(folder);
            Array.Sort(children, StringComparer.OrdinalIgnoreCase);
            m_subFolderCache[folder] = children;
        }

        return children;
    }

    private void InvalidateScan()
    {
        m_hasScan = false;
        m_scannedPaths.Clear();
        m_scannedChangeCount = 0;
        m_scannedUnsupportedCount = 0;
    }

    private void SetReport(string message, MessageType type)
    {
        m_report = message;
        m_reportType = type;
    }

    private static string GetParentFolder(string assetPath)
    {
        int slash = assetPath.LastIndexOf('/');
        return slash > 0 ? assetPath.Substring(0, slash) : assetPath;
    }

    private static bool IsDeprecatedPath(string assetPath)
    {
        return assetPath.IndexOf("/Deprecated", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
