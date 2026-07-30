using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <c>[FormerlySerializedAs]</c>를 은퇴시키는 4단계 도구입니다.
/// 에셋을 강제 재직렬화해 옛 필드 이름을 실제로 소멸시킨 뒤, 더 이상 쓸모가 없어진 어트리뷰트를 코드에서 제거합니다.
/// </summary>
/// <remarks>
/// 이 어트리뷰트는 <b>읽을 때만</b> 개입합니다. 마이그레이션이 실제로 끝나는 시점은 해당 에셋이 <b>다시 저장될 때</b>이며,
/// 그때 새 필드 이름으로 다시 써지면서 옛 키가 사라집니다. 재저장은 게으르게 일어나므로(누군가 그 씬을 열고 저장해야 함)
/// 개명 직후에 어트리뷰트를 지우면 아직 옛 키를 들고 있던 에셋의 값이 <b>조용히 기본값으로 죽습니다.</b>
/// 컴파일도 되고 예외도 없어 검증 없이는 알아챌 수 없으므로, 3단계 잔존 검사가 0건이 아니면 4단계 버튼이 잠깁니다.
///
/// 한계: 검사는 <b>현재 워킹트리만</b> 봅니다. 다른 사람 브랜치나 아직 머지되지 않은 작업에 옛 키가 남아 있으면,
/// 그 에셋이 나중에 합쳐질 때 값이 죽습니다. 팀 작업에서는 모든 브랜치가 합쳐진 뒤에 실행하십시오.
///
/// SVN 관리 폴더(<c>Assets/3.Resources</c>, <c>Assets/4.ThirdParty</c>)는 재직렬화와 코드 수정에서 모두 제외됩니다.
/// 실행 전 커밋해 두면 결과를 diff로 검토하고 되돌릴 수 있습니다.
/// </remarks>
public class FormerlySerializedAsCleanupWindow : EditorWindow
{
    /// <summary>재직렬화와 코드 수정에서 제외할 경로 접두사입니다. SVN이 소유하므로 Git 쪽에서 건드리지 않습니다.</summary>
    private static readonly string[] s_excludedPathPrefixes =
    {
        "Assets/3.Resources",
        "Assets/4.ThirdParty",
    };

    /// <summary>옛 키 잔존 검사 대상 에셋 확장자입니다. 텍스트 직렬화된 것만 검사할 수 있습니다.</summary>
    private static readonly string[] s_assetExtensions =
    {
        ".unity",
        ".prefab",
        ".asset",
    };

    /// <summary>
    /// 어트리뷰트 1건의 발견 위치입니다.
    /// </summary>
    private struct AttributeHit
    {
        /// <summary>프로젝트 상대 경로입니다.</summary>
        public string path;

        /// <summary>1부터 시작하는 줄 번호입니다.</summary>
        public int line;

        /// <summary>어트리뷰트가 선언한 옛 필드 이름입니다.</summary>
        public string oldName;
    }

    /// <summary>
    /// 옛 키 하나의 잔존 검사 결과입니다.
    /// </summary>
    private struct LeftoverEntry
    {
        /// <summary>검사한 옛 필드 이름입니다.</summary>
        public string oldName;

        /// <summary>그 키를 아직 담고 있는 에셋 경로들입니다.</summary>
        public List<string> assetPaths;
    }

    private readonly List<AttributeHit> m_hits = new List<AttributeHit>();
    private readonly List<LeftoverEntry> m_leftovers = new List<LeftoverEntry>();

    private bool m_scanned;
    private bool m_reserialized;
    private bool m_leftoverChecked;
    private int m_leftoverTotal = -1;
    private bool m_removeUnusedUsing = true;
    private string m_lastReport = string.Empty;
    private Vector2 m_scroll;

    [MenuItem("Tools/GrayZone/FormerlySerializedAs 정리")]
    private static void Open()
    {
        FormerlySerializedAsCleanupWindow window =
            GetWindow<FormerlySerializedAsCleanupWindow>("FormerlySerializedAs 정리");
        window.minSize = new Vector2(620, 460);
        window.Show();
    }

    private void OnGUI()
    {
        using (EditorGUILayout.ScrollViewScope scope = new EditorGUILayout.ScrollViewScope(m_scroll))
        {
            m_scroll = scope.scrollPosition;

            DrawIntro();
            EditorGUILayout.Space();
            DrawStep1Scan();
            EditorGUILayout.Space();
            DrawStep2Reserialize();
            EditorGUILayout.Space();
            DrawStep3LeftoverCheck();
            EditorGUILayout.Space();
            DrawStep4Remove();
            EditorGUILayout.Space();
            DrawReport();
        }
    }

    // ---------------------------------------------------------------- 안내

    private void DrawIntro()
    {
        EditorGUILayout.HelpBox(
            "[FormerlySerializedAs]는 옛 필드 이름으로 저장된 값을 읽어주는 마이그레이션 보조 장치입니다.\n"
            + "에셋이 새 이름으로 다시 저장된 뒤에야 불필요해지므로, 순서를 지키지 않으면 값이 조용히 사라집니다.\n"
            + "1 스캔 -> 2 재직렬화 -> 3 잔존 검사(0건이어야 함) -> 4 제거 순으로 진행하십시오.",
            MessageType.Info);

        if (EditorSettings.serializationMode != SerializationMode.ForceText)
        {
            EditorGUILayout.HelpBox(
                $"에셋 직렬화 모드가 {EditorSettings.serializationMode}입니다. 3단계 잔존 검사는 텍스트 직렬화를 전제로 하므로 "
                + "바이너리 에셋을 놓칠 수 있습니다. Project Settings > Editor > Asset Serialization을 Force Text로 두고 진행하십시오.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "검사는 현재 워킹트리만 봅니다. 다른 브랜치나 머지되지 않은 작업에 옛 키가 남아 있으면 나중에 합쳐질 때 값이 죽습니다.\n"
            + "실행 전 커밋해 두면 diff로 검토하고 되돌릴 수 있습니다.",
            MessageType.Warning);
    }

    // ---------------------------------------------------------------- [1] 스캔

    private void DrawStep1Scan()
    {
        EditorGUILayout.LabelField("[1] 어트리뷰트 스캔", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Assets 아래 .cs에서 [FormerlySerializedAs]를 찾아 옛 필드 이름 목록을 만듭니다. 읽기만 합니다.",
            EditorStyles.miniLabel);

        if (GUILayout.Button("스캔", GUILayout.Height(24)))
        {
            ScanAttributes();
        }

        if (!m_scanned)
        {
            return;
        }

        EditorGUILayout.LabelField($"발견: {m_hits.Count}건 / 고유 옛 이름 {CollectOldNames().Count}개");

        int shown = 0;
        foreach (AttributeHit hit in m_hits)
        {
            if (shown >= 30)
            {
                EditorGUILayout.LabelField($"  ... 외 {m_hits.Count - shown}건", EditorStyles.miniLabel);
                break;
            }

            EditorGUILayout.LabelField($"  {hit.path}:{hit.line}  \"{hit.oldName}\"", EditorStyles.miniLabel);
            shown++;
        }
    }

    /// <summary>Assets 아래 모든 .cs에서 어트리뷰트 선언을 수집합니다.</summary>
    private void ScanAttributes()
    {
        m_hits.Clear();
        m_scanned = false;
        m_reserialized = false;
        m_leftoverChecked = false;
        m_leftoverTotal = -1;
        m_leftovers.Clear();

        foreach (string path in EnumerateScriptPaths())
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FormerlySerializedAs 정리] 스크립트를 읽지 못했습니다. path={path}, error={exception.Message}");
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string oldName in ExtractOldNames(lines[i]))
                {
                    m_hits.Add(new AttributeHit
                    {
                        path = ToProjectRelative(path),
                        line = i + 1,
                        oldName = oldName,
                    });
                }
            }
        }

        m_scanned = true;
        m_lastReport = $"스캔 완료: {m_hits.Count}건, 고유 옛 이름 {CollectOldNames().Count}개.";
    }

    // ---------------------------------------------------------------- [2] 재직렬화

    private void DrawStep2Reserialize()
    {
        EditorGUILayout.LabelField("[2] 강제 재직렬화", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "모든 씬·프리팹·에셋을 현재 필드 이름으로 다시 씁니다. 이 단계가 실제 마이그레이션입니다. SVN 폴더는 제외합니다.",
            EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("재직렬화 실행 (에셋을 대량 수정함)", GUILayout.Height(24)))
            {
                ConfirmAndReserialize();
            }
        }

        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.LabelField("  Play Mode에서는 실행할 수 없습니다.", EditorStyles.miniLabel);
        }

        if (m_reserialized)
        {
            EditorGUILayout.LabelField("  재직렬화를 실행했습니다. 3단계로 확인하십시오.", EditorStyles.miniLabel);
        }
    }

    private void ConfirmAndReserialize()
    {
        List<string> targets = CollectReserializeTargets();
        bool proceed = EditorUtility.DisplayDialog(
            "강제 재직렬화",
            $"에셋 {targets.Count}개를 다시 씁니다. 대량의 파일이 수정되어 커밋 diff가 매우 커질 수 있습니다.\n\n"
            + "실행 전에 커밋되어 있는지 확인하십시오. 계속하겠습니까?",
            "실행",
            "취소");
        if (!proceed)
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("강제 재직렬화", $"에셋 {targets.Count}개 처리 중", 0.5f);

            // 메타데이터는 제외한다. 필드 값은 에셋 본문에 있고, .meta까지 다시 쓰면 무의미한 diff만 커진다.
            AssetDatabase.ForceReserializeAssets(targets, ForceReserializeAssetsOptions.ReserializeAssets);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        m_reserialized = true;
        m_leftoverChecked = false;
        m_leftoverTotal = -1;
        m_leftovers.Clear();
        m_lastReport = $"재직렬화 완료: 에셋 {targets.Count}개. 이어서 3단계 잔존 검사를 실행하십시오.";
    }

    /// <summary>SVN 폴더를 제외한 재직렬화 대상 에셋 경로를 모읍니다.</summary>
    private static List<string> CollectReserializeTargets()
    {
        List<string> targets = new List<string>();
        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || IsExcluded(path))
            {
                continue;
            }

            if (!HasAssetExtension(path))
            {
                continue;
            }

            targets.Add(path);
        }

        return targets;
    }

    // ---------------------------------------------------------------- [3] 잔존 검사

    private void DrawStep3LeftoverCheck()
    {
        EditorGUILayout.LabelField("[3] 옛 키 잔존 검사", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "1단계에서 모은 옛 이름이 에셋에 아직 키로 남아 있는지 확인합니다. 0건이어야 4단계가 열립니다.",
            EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(!m_scanned))
        {
            if (GUILayout.Button("잔존 검사", GUILayout.Height(24)))
            {
                CheckLeftovers();
            }
        }

        if (!m_scanned)
        {
            EditorGUILayout.LabelField("  먼저 1단계 스캔을 실행하십시오.", EditorStyles.miniLabel);
            return;
        }

        if (!m_leftoverChecked)
        {
            return;
        }

        if (m_leftoverTotal == 0)
        {
            EditorGUILayout.HelpBox("잔존 0건입니다. 4단계 제거를 진행할 수 있습니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            $"옛 키가 아직 {m_leftoverTotal}곳에 남아 있습니다. 지금 어트리뷰트를 제거하면 이 값들이 죽습니다.\n"
            + "아래 에셋을 Unity에서 열고 저장하거나 2단계를 다시 실행하십시오.",
            MessageType.Error);

        foreach (LeftoverEntry entry in m_leftovers)
        {
            EditorGUILayout.LabelField($"  \"{entry.oldName}\" - {entry.assetPaths.Count}개", EditorStyles.miniLabel);
            for (int i = 0; i < entry.assetPaths.Count && i < 10; i++)
            {
                EditorGUILayout.LabelField($"      {entry.assetPaths[i]}", EditorStyles.miniLabel);
            }
        }
    }

    /// <summary>수집한 옛 이름들이 텍스트 에셋에서 YAML 키로 쓰이고 있는지 조사합니다.</summary>
    private void CheckLeftovers()
    {
        m_leftovers.Clear();
        m_leftoverChecked = false;

        List<string> oldNames = CollectOldNames();
        if (oldNames.Count == 0)
        {
            m_leftoverTotal = 0;
            m_leftoverChecked = true;
            m_lastReport = "제거할 어트리뷰트가 없습니다.";
            return;
        }

        // 옛 이름 -> 그 키를 담은 에셋 목록. 에셋을 한 번만 읽도록 키가 아니라 파일을 바깥 루프로 둔다.
        Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string oldName in oldNames)
        {
            found[oldName] = new List<string>();
        }

        List<string> assets = CollectReserializeTargets();
        try
        {
            for (int i = 0; i < assets.Count; i++)
            {
                if (i % 50 == 0)
                {
                    EditorUtility.DisplayProgressBar(
                        "옛 키 잔존 검사",
                        assets[i],
                        assets.Count == 0 ? 1.0f : (float)i / assets.Count);
                }

                ScanAssetForOldKeys(assets[i], oldNames, found);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        int total = 0;
        foreach (KeyValuePair<string, List<string>> pair in found)
        {
            if (pair.Value.Count == 0)
            {
                continue;
            }

            total += pair.Value.Count;
            m_leftovers.Add(new LeftoverEntry { oldName = pair.Key, assetPaths = pair.Value });
        }

        m_leftoverTotal = total;
        m_leftoverChecked = true;
        m_lastReport = total == 0
            ? $"잔존 검사 완료: 옛 이름 {oldNames.Count}개 모두 0건. 제거 가능."
            : $"잔존 검사 완료: {total}곳에 옛 키가 남아 있음. 제거 차단.";
    }

    /// <summary>에셋 한 개를 줄 단위로 읽어 옛 이름이 YAML 키로 등장하는지 확인합니다.</summary>
    private static void ScanAssetForOldKeys(
        string assetPath,
        List<string> oldNames,
        Dictionary<string, List<string>> found)
    {
        try
        {
            // 씬은 수 MB에 달하므로 전체를 메모리에 올리지 않고 스트리밍한다.
            foreach (string line in File.ReadLines(assetPath))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, colon).Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                for (int i = 0; i < oldNames.Count; i++)
                {
                    if (!string.Equals(key, oldNames[i], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    List<string> hits = found[oldNames[i]];
                    if (!hits.Contains(assetPath))
                    {
                        hits.Add(assetPath);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[FormerlySerializedAs 정리] 에셋을 읽지 못했습니다. path={assetPath}, error={exception.Message}");
        }
    }

    // ---------------------------------------------------------------- [4] 제거

    private void DrawStep4Remove()
    {
        EditorGUILayout.LabelField("[4] 어트리뷰트 제거", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "코드에서 [FormerlySerializedAs]를 지웁니다. 3단계가 0건일 때만 열립니다.",
            EditorStyles.miniLabel);

        m_removeUnusedUsing = EditorGUILayout.ToggleLeft(
            "다른 참조가 없으면 using UnityEngine.Serialization; 도 제거",
            m_removeUnusedUsing);

        bool gateOpen = m_scanned && m_leftoverChecked && m_leftoverTotal == 0 && m_hits.Count > 0;
        using (new EditorGUI.DisabledScope(!gateOpen))
        {
            if (GUILayout.Button("어트리뷰트 제거 (코드 수정)", GUILayout.Height(24)))
            {
                ConfirmAndRemove();
            }
        }

        if (!gateOpen)
        {
            EditorGUILayout.LabelField(
                m_hits.Count == 0 && m_scanned
                    ? "  제거할 어트리뷰트가 없습니다."
                    : "  1~3단계를 통과해야 열립니다.",
                EditorStyles.miniLabel);
        }
    }

    private void ConfirmAndRemove()
    {
        bool proceed = EditorUtility.DisplayDialog(
            "어트리뷰트 제거",
            $"[FormerlySerializedAs] {m_hits.Count}건을 코드에서 제거합니다.\n\n"
            + "다른 브랜치에 옛 키가 남아 있다면 그 작업이 합쳐질 때 값이 죽습니다. 계속하겠습니까?",
            "제거",
            "취소");
        if (!proceed)
        {
            return;
        }

        int changedFiles = 0;
        int removed = 0;
        int usingRemoved = 0;

        foreach (string path in EnumerateScriptPaths())
        {
            string original;
            try
            {
                original = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FormerlySerializedAs 정리] 스크립트를 읽지 못했습니다. path={path}, error={exception.Message}");
                continue;
            }

            if (original.IndexOf("FormerlySerializedAs", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            string edited = RemoveAttributes(original, out int removedHere);
            if (removedHere == 0)
            {
                continue;
            }

            if (m_removeUnusedUsing && TryRemoveUnusedUsing(edited, out string withoutUsing))
            {
                edited = withoutUsing;
                usingRemoved++;
            }

            try
            {
                File.WriteAllText(path, edited);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[FormerlySerializedAs 정리] 스크립트를 쓰지 못했습니다. path={path}, error={exception.Message}");
                continue;
            }

            changedFiles++;
            removed += removedHere;
        }

        AssetDatabase.Refresh();

        m_lastReport = $"제거 완료: 파일 {changedFiles}개, 어트리뷰트 {removed}건, using {usingRemoved}건. "
                       + "git diff로 반드시 검토하십시오.";
        ScanAttributes();
    }

    // ---------------------------------------------------------------- 텍스트 처리

    /// <summary>
    /// 한 줄에서 <c>[FormerlySerializedAs("...")]</c>가 선언한 옛 이름들을 뽑습니다.
    /// </summary>
    /// <remarks>주석 처리된 줄은 건너뜁니다. 어트리뷰트가 아니라 문자열·주석 안의 언급을 제거 대상으로 삼으면 안 됩니다.</remarks>
    private static IEnumerable<string> ExtractOldNames(string line)
    {
        List<string> names = new List<string>();
        int commentAt = line.IndexOf("//", StringComparison.Ordinal);

        foreach (Match match in Regex.Matches(
                     line,
                     @"(?:UnityEngine\.Serialization\.)?FormerlySerializedAs\s*\(\s*(?:nameof\s*\(\s*(?<n>[\w.]+)\s*\)|""(?<s>[^""]*)"")\s*\)"))
        {
            if (commentAt >= 0 && match.Index > commentAt)
            {
                continue;
            }

            string value = match.Groups["s"].Success ? match.Groups["s"].Value : match.Groups["n"].Value;
            if (value.Length == 0)
            {
                continue;
            }

            // nameof(Foo.bar) 형태면 마지막 식별자만 직렬화 키가 된다.
            int dot = value.LastIndexOf('.');
            names.Add(dot >= 0 ? value.Substring(dot + 1) : value);
        }

        return names;
    }

    /// <summary>
    /// 소스 전체에서 어트리뷰트 항목만 제거합니다. 같은 대괄호에 다른 어트리뷰트가 함께 있으면 그것들은 남깁니다.
    /// </summary>
    /// <param name="source">원본 소스 코드입니다.</param>
    /// <param name="removedCount">제거된 어트리뷰트 개수입니다.</param>
    /// <returns>수정된 소스 코드입니다.</returns>
    private static string RemoveAttributes(string source, out int removedCount)
    {
        removedCount = 0;
        string[] lines = source.Split('\n');
        List<string> output = new List<string>(lines.Length);

        foreach (string rawLine in lines)
        {
            // 줄 끝 \r을 보존해 개행 형식을 바꾸지 않는다.
            bool hadCr = rawLine.EndsWith("\r", StringComparison.Ordinal);
            string line = hadCr ? rawLine.Substring(0, rawLine.Length - 1) : rawLine;

            int commentAt = line.IndexOf("//", StringComparison.Ordinal);
            if (line.IndexOf("FormerlySerializedAs", StringComparison.Ordinal) < 0
                || (commentAt >= 0 && line.IndexOf("FormerlySerializedAs", StringComparison.Ordinal) > commentAt))
            {
                output.Add(rawLine);
                continue;
            }

            string edited = StripFromLine(line, out int removedHere);
            removedCount += removedHere;

            if (removedHere == 0)
            {
                output.Add(rawLine);
                continue;
            }

            // 어트리뷰트만 있던 줄은 통째로 지운다. 다른 내용이 남았으면 그 줄을 유지한다.
            if (edited.Trim().Length == 0)
            {
                continue;
            }

            output.Add(hadCr ? edited + "\r" : edited);
        }

        return string.Join("\n", output);
    }

    /// <summary>한 줄의 대괄호 구획들을 순회하며 해당 어트리뷰트 항목만 걷어냅니다.</summary>
    private static string StripFromLine(string line, out int removedCount)
    {
        removedCount = 0;
        StringBuilder builder = new StringBuilder(line.Length);
        int index = 0;

        while (index < line.Length)
        {
            int open = line.IndexOf('[', index);
            if (open < 0)
            {
                builder.Append(line, index, line.Length - index);
                break;
            }

            int close = FindSectionEnd(line, open);
            if (close < 0)
            {
                builder.Append(line, index, line.Length - index);
                break;
            }

            builder.Append(line, index, open - index);

            string inner = line.Substring(open + 1, close - open - 1);
            List<string> kept = new List<string>();
            foreach (string element in SplitTopLevel(inner))
            {
                if (Regex.IsMatch(
                        element.Trim(),
                        @"^(?:UnityEngine\.Serialization\.)?FormerlySerializedAs\s*\("))
                {
                    removedCount++;
                    continue;
                }

                kept.Add(element.Trim());
            }

            if (kept.Count > 0)
            {
                builder.Append('[').Append(string.Join(", ", kept)).Append(']');
            }

            index = close + 1;
        }

        // 어트리뷰트를 걷어낸 뒤 줄 끝에 남는 공백은 정리한다.
        return builder.ToString().TrimEnd();
    }

    /// <summary>대괄호 구획의 닫는 위치를 찾습니다. 문자열 리터럴과 중첩 괄호를 건너뜁니다.</summary>
    private static int FindSectionEnd(string line, int open)
    {
        int depth = 0;
        bool inString = false;

        for (int i = open; i < line.Length; i++)
        {
            char c = line[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>대괄호 안을 최상위 쉼표로만 나눕니다. 괄호와 문자열 안의 쉼표는 무시합니다.</summary>
    private static List<string> SplitTopLevel(string inner)
    {
        List<string> parts = new List<string>();
        int depth = 0;
        bool inString = false;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                case '[':
                    depth++;
                    break;
                case ')':
                case ']':
                    depth--;
                    break;
                case ',':
                    if (depth == 0)
                    {
                        parts.Add(inner.Substring(start, i - start));
                        start = i + 1;
                    }

                    break;
            }
        }

        if (start < inner.Length)
        {
            parts.Add(inner.Substring(start));
        }

        return parts;
    }

    /// <summary>
    /// 어트리뷰트를 모두 제거한 뒤 <c>UnityEngine.Serialization</c> 참조가 남지 않았다면 using을 지웁니다.
    /// </summary>
    /// <returns>using을 제거했으면 <c>true</c></returns>
    private static bool TryRemoveUnusedUsing(string source, out string edited)
    {
        edited = source;

        // 이 네임스페이스의 다른 타입을 쓰고 있을 수 있으므로, 흔적이 남아 있으면 손대지 않는다.
        if (source.IndexOf("FormerlySerializedAs", StringComparison.Ordinal) >= 0
            || source.IndexOf("Serialization.", StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        string replaced = Regex.Replace(
            source,
            @"^[ \t]*using\s+UnityEngine\.Serialization\s*;[ \t]*\r?\n",
            string.Empty,
            RegexOptions.Multiline);
        if (string.Equals(replaced, source, StringComparison.Ordinal))
        {
            return false;
        }

        edited = replaced;
        return true;
    }

    // ---------------------------------------------------------------- 공용

    private void DrawReport()
    {
        if (m_lastReport.Length == 0)
        {
            return;
        }

        EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(m_lastReport, EditorStyles.wordWrappedMiniLabel);
    }

    /// <summary>중복을 제거한 옛 이름 목록입니다.</summary>
    private List<string> CollectOldNames()
    {
        List<string> names = new List<string>();
        foreach (AttributeHit hit in m_hits)
        {
            if (!names.Contains(hit.oldName))
            {
                names.Add(hit.oldName);
            }
        }

        return names;
    }

    /// <summary>SVN 폴더를 제외한 Assets 아래 모든 .cs 절대 경로입니다.</summary>
    private static IEnumerable<string> EnumerateScriptPaths()
    {
        string assetsRoot = Application.dataPath;
        foreach (string path in Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relative = ToProjectRelative(path);
            if (IsExcluded(relative))
            {
                continue;
            }

            yield return path;
        }
    }

    /// <summary>절대 경로를 <c>Assets/</c>로 시작하는 프로젝트 상대 경로로 바꿉니다.</summary>
    private static string ToProjectRelative(string absolutePath)
    {
        string normalized = absolutePath.Replace('\\', '/');
        int assetsAt = normalized.LastIndexOf("/Assets/", StringComparison.Ordinal);
        return assetsAt >= 0 ? normalized.Substring(assetsAt + 1) : normalized;
    }

    private static bool IsExcluded(string projectRelativePath)
    {
        foreach (string prefix in s_excludedPathPrefixes)
        {
            if (projectRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAssetExtension(string path)
    {
        foreach (string extension in s_assetExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
