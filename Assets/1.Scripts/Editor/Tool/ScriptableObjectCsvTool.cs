using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CSV &lt;-&gt; ScriptableObject sync tool for <see cref="IBalanceTableData"/> types.
/// One CSV per type (columns = serialized fields). Assets are
/// identified by GUID so the same file round-trips back onto the same assets.
///
/// Simple fields (numbers, bool, string, enum, Vector*, Color) are
/// written as human-readable cells; complex fields (nested structs, arrays of
/// structs, etc.) fall back to JSON encoded inside a single cell.
/// Unity Object and media references are rejected so Feedback data cannot enter
/// the balance table path.
///
/// Editor-only. All writes go through SerializedObject (Undo / dirty / save safe).
/// No runtime code is touched.
///
/// 이 도구의 CSV 폴더를 감시하는 자동 임포터는
/// <c>Assets/1.Scripts/Editor/SoCsvAutoImporter.cs</c>에 분리되어 있습니다.
/// 자동 임포터는 에셋 변경 시 자동으로 실행되며, 이 파일의 창은 Tools 메뉴에서 직접 열 때만 표시됩니다.
/// </summary>
public class ScriptableObjectCsvWindow : EditorWindow
{
    // Reserved (non-field) columns.
    private const string ColType = "__Type";
    private const string ColGuid = "__Guid";
    private const string ColPath = "__Path";

    /// <summary>
    /// 컬럼 설명을 담는 행의 표시입니다. __Type 자리에 들어가며 가져오기에서 건너뜁니다.
    /// </summary>
    private const string RowTooltip = "__Tooltip";

    /// <summary>세로형 시트의 첫 컬럼 머리말입니다. 이 값으로 가로형과 세로형을 구분합니다.</summary>
    private const string ColField = "__Field";

    /// <summary>CSV 내보내기 폴더의 기본값입니다.</summary>
    public const string DefaultCsvFolder = "Assets/5.Data/DataSheet/CsvData";

    /// <summary>새 .asset 생성 폴더의 기본값입니다.</summary>
    public const string DefaultNewAssetFolder = "Assets/5.Data/ScriptableObject";

    /// <summary>
    /// CSV 내보내기 대상 폴더이자 자동 가져오기가 감시하는 폴더입니다. 창에서 변경할 수 있습니다.
    /// </summary>
    public static string CsvFolder
    {
        get => SoCsvSettings.GetFolder(SoCsvSettings.CsvFolderKey, DefaultCsvFolder);
        set => SoCsvSettings.SetFolder(SoCsvSettings.CsvFolderKey, value);
    }

    /// <summary>
    /// 가져오기 행에 대응하는 에셋이 없을 때 새 .asset을 만들 폴더입니다. 창에서 변경할 수 있습니다.
    /// </summary>
    public static string NewAssetFolder
    {
        get => SoCsvSettings.GetFolder(SoCsvSettings.NewAssetFolderKey, DefaultNewAssetFolder);
        set => SoCsvSettings.SetFolder(SoCsvSettings.NewAssetFolderKey, value);
    }

    private readonly List<TypeEntry> m_types = new List<TypeEntry>();
    private int m_selectedIndex;
    [SerializeField]
    private bool m_showFolders;
    private static readonly string[] s_tabLabels = { "스크립트 → SO", "SO → 엑셀(CSV)" };

    [SerializeField]
    private int m_tab;

    // 탭1에서 만들 SO 종류입니다. 밸런스와 피드백이 같은 생성 절차를 공유합니다.
    [SerializeField]
    private BalanceScaffold.SoKind m_soKind = BalanceScaffold.SoKind.Balance;

    // 탭1: 변환 대상 스크립트들과, 초기값을 가져올 프리팹입니다.
    // 밸런스 SO는 엔티티 단위라, 한 엔티티가 여러 컴포넌트로 나뉘면 그 스크립트를 모두 넣어 한 SO로 만듭니다.
    [SerializeField]
    private List<MonoScript> m_scriptAssets = new List<MonoScript>();
    [SerializeField]
    private GameObject m_seedPrefab;

    // 탭1: 생성할 SO 클래스(.cs)를 쓸 경로입니다. 기존 파일이 있으면 그 경로로 고정됩니다.
    [SerializeField]
    private string m_generateTargetPath;

    // 탭1: 만들 SO 클래스 이름과 .asset 파일 이름입니다. 비우면 규약대로 자동 결정됩니다.
    [SerializeField]
    private string m_soTypeName;
    [SerializeField]
    private string m_soAssetName;

    // 탭2: 내보낼 SO 에셋입니다. 타입만 쓰고, 같은 타입 에셋 전부가 한 시트로 나갑니다.
    [SerializeField]
    private ScriptableObject m_exportTarget;

    // 탭2: 내보낼 CSV 파일 이름(확장자 제외)입니다.
    [SerializeField]
    private string m_csvFileName;

    private Vector2 m_scroll;

    // FindSoType은 프로젝트의 모든 ScriptableObject 타입을 훑으므로 리페인트마다 부르지 않도록 캐싱합니다.
    // 선택한 스크립트가 바뀌면 키 비교로 저절로 다시 찾고, 클래스를 새로 만들면 재컴파일이 창 상태를 초기화합니다.
    private Type m_soTypeCache;
    private int m_soTypeCacheKey;
    private string m_soTypeCacheName;
    private bool m_soTypeCacheValid;

    /// <summary>선택한 스크립트들의 런타임 타입입니다. 비어 있거나 해석 실패한 항목은 제외합니다.</summary>
    private List<Type> ScriptTypes
    {
        get
        {
            List<Type> types = new List<Type>();
            foreach (MonoScript script in m_scriptAssets)
            {
                Type type = script != null ? script.GetClass() : null;
                if (type != null && !types.Contains(type))
                {
                    types.Add(type);
                }
            }

            return types;
        }
    }

    /// <summary>이름과 경로의 기준이 되는 첫 스크립트입니다.</summary>
    private MonoScript PrimaryScript => m_scriptAssets.Count > 0 ? m_scriptAssets[0] : null;

    /// <summary>
    /// 선택한 스크립트가 들어 있는 폴더입니다. 생성할 SO 코드를 여기에 나란히 두는 것이 기본값입니다.
    /// </summary>
    /// <remarks>1대1로 바인딩되는 스크립트 옆에 두어 짝을 한눈에 보이게 합니다.</remarks>
    private string ScriptFolder
    {
        get
        {
            string path = PrimaryScript != null ? AssetDatabase.GetAssetPath(PrimaryScript) : null;
            if (string.IsNullOrEmpty(path))
            {
                return "Assets";
            }

            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            return string.IsNullOrEmpty(folder) ? "Assets" : folder;
        }
    }

    /// <summary>선택한 스크립트에 대응하는, 이미 컴파일된 SO 타입입니다. 없으면 <c>null</c>입니다.</summary>
    private Type CachedSoType
    {
        get
        {
            int key = m_scriptAssets.Count;
            foreach (MonoScript script in m_scriptAssets)
            {
                key = (key * 397) ^ (script != null ? script.GetInstanceID() : 0);
            }

            if (!m_soTypeCacheValid
                || m_soTypeCacheKey != key
                || !string.Equals(m_soTypeCacheName, m_soTypeName, StringComparison.Ordinal))
            {
                List<Type> types = ScriptTypes;
                string wanted = string.IsNullOrWhiteSpace(m_soTypeName)
                    ? (types.Count > 0 ? BalanceScaffold.GetSoTypeName(types[0], m_soKind) : null)
                    : m_soTypeName;
                m_soTypeCache = BalanceScaffold.FindSoTypeByName(wanted);
                m_soTypeCacheKey = key;
                m_soTypeCacheName = m_soTypeName;
                m_soTypeCacheValid = true;
            }

            return m_soTypeCache;
        }
    }

    /// <summary>타입 선택 목록에 표시할 밸런스 SO 타입 한 항목입니다.</summary>
    private struct TypeEntry
    {
        /// <summary>내보낼 대상 ScriptableObject 타입입니다.</summary>
        public Type type;

        /// <summary>검색 범위 안에 존재하는 그 타입의 에셋 수입니다. 0개여도 헤더만 뽑을 수 있습니다.</summary>
        public int count;

        /// <summary>드롭다운에 그대로 표시할 문구입니다.</summary>
        public string label;
    }

    [MenuItem("Tools/GrayZone/SO CSV 도구")]
    private static void Open()
    {
        ScriptableObjectCsvWindow window = GetWindow<ScriptableObjectCsvWindow>("SO CSV 도구");
        window.minSize = new Vector2(520, 320);
        window.ScanTypes();
        window.Show();
    }

    private void OnEnable()
    {
        if (m_types.Count == 0)
            ScanTypes();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        m_tab = GUILayout.Toolbar(m_tab, s_tabLabels, GUILayout.Height(24));

        using (EditorGUILayout.ScrollViewScope scope = new EditorGUILayout.ScrollViewScope(m_scroll))
        {
            m_scroll = scope.scrollPosition;

            if (m_tab == 0)
            {
                DrawScriptSelection();
                DrawScriptToSo();
            }
            else
            {
                DrawSoToCsv();
            }

            DrawFolderSettings();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "세로형 시트: 1열=필드 이름, 2열=설명, 3열부터 에셋. __로 시작하는 행은 메타·설명이라 값 대입에서 제외됩니다.",
                EditorStyles.miniLabel);
        }
    }

    // ---------------------------------------------------------------- [1] 대상 선택

    /// <summary>변환 대상 스크립트들과 초기값을 가져올 프리팹을 고르는 영역입니다.</summary>
    /// <remarks>
    /// 밸런스 SO는 기획자가 튜닝하는 엔티티 단위입니다. 적 한 종이 여러 컴포넌트로 나뉘어 있으면
    /// 그 스크립트를 모두 넣어 하나의 SO로 만듭니다. 하나만 넣으면 기존과 동일하게 동작합니다.
    /// </remarks>
    private void DrawScriptSelection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1. 대상 선택", EditorStyles.boldLabel);

        using (EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope())
        {
            m_soKind = (BalanceScaffold.SoKind)EditorGUILayout.EnumPopup("만들 SO 종류", m_soKind);
            if (check.changed)
            {
                // 종류가 바뀌면 유도되는 이름과 경로가 달라집니다.
                m_soTypeName = null;
                m_soAssetName = null;
                m_generateTargetPath = null;
            }
        }

        DrawScriptList();

        m_seedPrefab = (GameObject)EditorGUILayout.ObjectField(
            "프리팹 (선택)", m_seedPrefab, typeof(GameObject), false);

        string markerLabel = $"[{BalanceScaffold.GetMarkerAttributeType(m_soKind).Name.Replace("Attribute", string.Empty)}]";

        List<Type> types = ScriptTypes;
        if (m_scriptAssets.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"{markerLabel}가 선언된 스크립트를 넣어주세요. 한 엔티티가 여러 컴포넌트로 나뉘어 있으면 모두 넣으면 됩니다. " +
                "프리팹을 넣으면 그 프리팹에 배선된 값으로 초기값을 채웁니다.",
                MessageType.Info);
            return;
        }

        if (types.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "넣은 스크립트의 클래스를 찾을 수 없습니다. 컴파일이 끝났는지, 파일명과 클래스명이 같은지 확인하세요.",
                MessageType.Error);
            return;
        }

        List<BalanceScaffold.FieldBlock> blocks = BalanceScaffold.CollectBlocks(types, m_soKind, out List<string> duplicates);
        int fieldCount = 0;
        foreach (BalanceScaffold.FieldBlock block in blocks)
        {
            fieldCount += block.Fields.Count;
        }

        if (fieldCount == 0)
        {
            EditorGUILayout.HelpBox(
                $"넣은 스크립트에 {markerLabel} 필드가 없습니다. SO로 뽑을 필드에 특성을 먼저 붙여주세요.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField($"{markerLabel} 필드: {fieldCount}개 (스크립트 {blocks.Count}개)");

        if (duplicates.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"이름이 겹치는 필드 {duplicates.Count}개는 SO에 하나만 만들어져, 두 컴포넌트가 같은 값을 받습니다. " +
                "의도한 공유가 아니면 한쪽 스크립트의 필드 이름을 바꾸세요.\n  " + string.Join("\n  ", duplicates),
                MessageType.Warning);
        }

        string defaultTypeName = BalanceScaffold.GetSoTypeName(types[0], m_soKind);
        m_soTypeName = DrawNameRow("SO 클래스 이름", m_soTypeName, defaultTypeName);
        if (!BalanceScaffold.IsValidTypeName(m_soTypeName))
        {
            EditorGUILayout.HelpBox(
                "클래스 이름은 문자나 밑줄로 시작하고 영숫자·밑줄만 쓸 수 있습니다.", MessageType.Warning);
        }

        string defaultAssetName = BalanceScaffold.BuildAssetName(
            m_seedPrefab != null ? m_seedPrefab.name : null, m_soTypeName);
        m_soAssetName = DrawNameRow("SO 데이터(.asset) 이름", m_soAssetName, defaultAssetName);

        Type soType = CachedSoType;
        EditorGUILayout.LabelField(
            soType != null ? $"상태: {soType.Name} 코드가 이미 있습니다." : "상태: SO 코드가 아직 없습니다.",
            EditorStyles.miniLabel);

        if (m_seedPrefab != null)
        {
            List<string> missing = new List<string>();
            foreach (Type type in types)
            {
                if (typeof(Component).IsAssignableFrom(type) && m_seedPrefab.GetComponent(type) == null)
                {
                    missing.Add(type.Name);
                }
            }

            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"'{m_seedPrefab.name}' 프리팹에 {string.Join(", ", missing)} 컴포넌트가 없어 그 값은 비웁니다.",
                    MessageType.Warning);
            }
        }
    }

    /// <summary>대상 스크립트 목록을 추가·삭제할 수 있게 그립니다.</summary>
    private void DrawScriptList()
    {
        for (int i = 0; i < m_scriptAssets.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                MonoScript picked = (MonoScript)EditorGUILayout.ObjectField(
                    i == 0 ? "스크립트 (.cs)" : " ", m_scriptAssets[i], typeof(MonoScript), false);
                if (picked != m_scriptAssets[i])
                {
                    m_scriptAssets[i] = picked;
                    OnScriptListChanged();
                }

                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    m_scriptAssets.RemoveAt(i);
                    OnScriptListChanged();
                    GUI.FocusControl(null);
                    return;
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("스크립트 추가", GUILayout.Height(20)))
            {
                m_scriptAssets.Add(null);
                OnScriptListChanged();
            }
        }
    }

    /// <summary>대상 목록이 바뀌면 유도된 이름과 경로를 다시 계산하게 합니다.</summary>
    private void OnScriptListChanged()
    {
        m_soTypeName = null;
        m_soAssetName = null;
        m_generateTargetPath = null;
        AutoFillSeedPrefab();
    }

    // ---------------------------------------------------------------- [2] 스크립트 → SO

    /// <summary>스크립트에서 SO 클래스와 에셋을 만드는 영역입니다.</summary>
    private void DrawScriptToSo()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("생성", EditorStyles.boldLabel);

        List<Type> types = ScriptTypes;
        bool ready = types.Count > 0 && BalanceScaffold.CollectBlocks(types, m_soKind, out _).Count > 0;
        Type soType = ready ? CachedSoType : null;

        DrawGenerateTargetPath(types, ready);

        EditorGUILayout.HelpBox(
            "클래스(.cs)를 만들면 Unity가 다시 컴파일합니다. 컴파일이 끝나기 전에는 그 타입이 존재하지 않아 " +
            ".asset을 같은 클릭에서 만들 수 없습니다. 그래서 두 단계로 나눠 눌러야 합니다.",
            MessageType.None);

        using (new EditorGUI.DisabledScope(!ready))
        {
            if (GUILayout.Button(
                    soType != null ? "① SO 코드(.cs) 갱신" : "① SO 코드(.cs) 생성",
                    GUILayout.Height(26)))
            {
                GenerateSoScript();
            }
        }

        using (new EditorGUI.DisabledScope(!ready || soType == null))
        {
            if (GUILayout.Button("② SO 데이터(.asset) 생성", GUILayout.Height(26)))
            {
                CreateSoAsset(soType);
            }
        }

        if (ready && soType == null)
        {
            EditorGUILayout.LabelField(
                "② 는 코드 컴파일이 끝나면 활성화됩니다.", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!ready))
        {
            if (GUILayout.Button("한 번에 하기 (①→②→CSV 내보내기)", GUILayout.Height(30)))
            {
                RunAll();
            }
        }

        EditorGUILayout.LabelField(
            "클래스가 없으면 클래스만 만들고 멈춥니다. 컴파일 후 한 번 더 누르면 이어서 진행합니다.",
            EditorStyles.miniLabel);
    }

    /// <summary>기본값이 미리 채워진 이름 입력 칸을 그립니다.</summary>
    /// <param name="label">표시할 항목 이름입니다.</param>
    /// <param name="current">현재 입력값입니다. 비어 있으면 기본값으로 채웁니다.</param>
    /// <param name="defaultValue">비었을 때 채울 기본 이름입니다.</param>
    /// <returns>확정된 이름입니다.</returns>
    /// <remarks>입력 중에는 반영하지 않고 Enter 또는 포커스 이동 시점에만 확정합니다.</remarks>
    private static string DrawNameRow(string label, string current, string defaultValue)
    {
        string value = string.IsNullOrWhiteSpace(current) ? defaultValue : current;

        using (new EditorGUILayout.HorizontalScope())
        {
            value = EditorGUILayout.DelayedTextField(label, value);

            using (new EditorGUI.DisabledScope(value == defaultValue))
            {
                if (GUILayout.Button("기본값", GUILayout.Width(60)))
                {
                    value = defaultValue;
                    GUI.FocusControl(null);
                }
            }
        }

        return value;
    }

    /// <summary>생성할 .cs 파일 경로를 보여주고 직접 고칠 수 있게 합니다.</summary>
    /// <param name="scriptType">선택한 스크립트 타입입니다.</param>
    /// <param name="ready">생성 가능한 상태인지 여부입니다.</param>
    /// <remarks>
    /// 이 프로젝트는 스크립트와 SO를 다른 폴더에 두는 관례라(예: Unit/Gun.cs ↔ Weapon/SOScript/GunBalanceSO.cs)
    /// 위치를 자동으로 유도할 수 없습니다. 기존 파일이 있으면 그 경로로 고정하고, 새로 만들 때만 지정하게 합니다.
    /// </remarks>
    private void DrawGenerateTargetPath(List<Type> scriptTypes, bool ready)
    {
        if (!ready)
        {
            return;
        }

        string soName = string.IsNullOrWhiteSpace(m_soTypeName)
            ? BalanceScaffold.GetSoTypeName(scriptTypes[0])
            : m_soTypeName;
        string existing = BalanceScaffold.FindExistingScriptPath(soName);
        if (!string.IsNullOrEmpty(existing))
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("코드(.cs) 생성 위치", existing);
            }

            EditorGUILayout.LabelField(
                "이미 있는 파일이라 그 자리에 갱신합니다. 옮기려면 프로젝트 창에서 파일을 이동하세요.",
                EditorStyles.miniLabel);
            return;
        }

        string suggested = $"{ScriptFolder.TrimEnd('/')}/{soName}.cs";
        if (string.IsNullOrWhiteSpace(m_generateTargetPath) || !m_generateTargetPath.EndsWith(".cs", StringComparison.Ordinal))
        {
            m_generateTargetPath = suggested;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            m_generateTargetPath = EditorGUILayout.DelayedTextField("코드(.cs) 생성 위치", m_generateTargetPath);
            using (new EditorGUI.DisabledScope(m_generateTargetPath == suggested))
            {
                if (GUILayout.Button("기본값", GUILayout.Width(60)))
                {
                    m_generateTargetPath = suggested;
                    GUI.FocusControl(null);
                }
            }
        }

        if (!IsAssetFolderPath(m_generateTargetPath))
        {
            EditorGUILayout.HelpBox("경로는 Assets 폴더 안이어야 합니다.", MessageType.Warning);
        }
    }

    // ---------------------------------------------------------------- [3] SO → 엑셀

    /// <summary>선택한 스크립트에 대응하는 SO를 CSV로 주고받는 영역입니다.</summary>
    private void DrawSoToCsv()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("대상 선택", EditorStyles.boldLabel);

        m_exportTarget = (ScriptableObject)EditorGUILayout.ObjectField(
            "SO 에셋", m_exportTarget, typeof(ScriptableObject), false);

        if (m_exportTarget == null)
        {
            EditorGUILayout.HelpBox(
                "내보낼 SO 에셋을 넣거나, 아래 목록에서 타입을 고르세요. " +
                "같은 타입의 에셋이 여러 개면 전부 한 시트의 각 행으로 함께 나갑니다. " +
                "에셋이 0개인 타입도 고를 수 있고, 그때는 시트 헤더만 나갑니다.",
                MessageType.Info);

            DrawTypePicker();
        }
        else
        {
            Type soType = m_exportTarget.GetType();
            bool validBalanceType = TryValidateBalanceType(soType, out string validationError);
            int count = LoadAllOfType(soType).Count;

            EditorGUILayout.LabelField($"타입: {soType.Name}    /    함께 나갈 에셋: {count}개");

            if (!validBalanceType)
            {
                EditorGUILayout.HelpBox(validationError, MessageType.Error);
            }

            m_csvFileName = DrawNameRow("CSV 파일 이름", m_csvFileName, GetCsvFileName(soType));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("내보낼 경로", $"{CsvFolder.TrimEnd('/')}/{m_csvFileName}.csv");
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!validBalanceType))
            {
                if (GUILayout.Button($"{soType.Name} → CSV 내보내기", GUILayout.Height(28)))
                {
                    ExportType(soType, m_csvFileName);
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("가져오기", EditorStyles.boldLabel);
        if (GUILayout.Button("CSV → SO 가져오기 (타입 자동 감지)", GUILayout.Height(28)))
        {
            ImportCsv();
        }

        EditorGUILayout.LabelField(
            BalanceTableSettings.AutoImportCsv
                ? $"{CsvFolder} 안의 CSV가 바뀌면 자동으로도 반영됩니다. 이 버튼은 다른 위치의 파일을 직접 고를 때 씁니다."
                : "자동 가져오기는 꺼져 있습니다. CSV는 내보낸 스냅샷이며, 되돌려 넣을 때만 이 버튼을 씁니다.",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            "필드 구성이 다른 낡은 시트는 일부만 반영하지 않고 파일 단위로 거부합니다.",
            EditorStyles.miniLabel);
    }

    /// <summary>에셋 없이 타입만 골라 내보낼 때 쓰는 목록입니다.</summary>
    /// <remarks>
    /// 이 프로젝트가 정의한 <see cref="IBalanceTableData"/> 구현 타입만 나옵니다.
    /// </remarks>
    private void DrawTypePicker()
    {
        List<TypeEntry> shown = m_types;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"밸런스 SO 타입: {shown.Count}개", GUILayout.Width(180));
            if (GUILayout.Button("다시 검색", GUILayout.Width(90)))
            {
                ScanTypes();
            }
        }

        if (shown.Count == 0)
        {
            EditorGUILayout.HelpBox($"{nameof(IBalanceTableData)}를 구현한 타입이 없습니다.", MessageType.Warning);
            return;
        }

        string[] labels = new string[shown.Count];
        for (int i = 0; i < shown.Count; i++)
        {
            labels[i] = shown[i].label;
        }

        m_selectedIndex = Mathf.Clamp(m_selectedIndex, 0, shown.Count - 1);
        m_selectedIndex = EditorGUILayout.Popup("타입 (에셋 수)", m_selectedIndex, labels);

        TypeEntry selected = shown[m_selectedIndex];
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("내보낼 경로", $"{CsvFolder.TrimEnd('/')}/{GetCsvFileName(selected.type)}.csv");
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button($"{selected.type.Name} → CSV 내보내기", GUILayout.Height(26)))
            {
                ExportType(selected.type);
            }

            if (GUILayout.Button("전체 밸런스 → CSV 내보내기", GUILayout.Height(26)))
            {
                ExportAllTypes();
            }
        }

        DrawReverseSyncSection();
    }

    /// <summary>
    /// 선택한 GameObject의 인스펙터 값을 SO와 CSV로 되돌려 쓰는 영역을 그립니다.
    /// </summary>
    /// <remarks>
    /// 여기 있는 다른 동작이 모두 CSV에서 SO로 가는 방향이라, 반대 방향을 같은 창에 두어
    /// 어느 쪽으로 값이 흐르는지 한눈에 보이게 합니다. 실제 처리는 인스펙터 기어 메뉴와
    /// 런타임 트레이너 버튼이 함께 쓰는 <see cref="BalanceReverseSync"/>가 담당합니다.
    /// </remarks>
    private void DrawReverseSyncSection()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("인스펙터 → SO + CSV (역방향)", EditorStyles.boldLabel);

        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
        {
            EditorGUILayout.HelpBox("GameObject를 선택하면 그 아래에서 밸런스 SO를 물고 있는 컴포넌트를 갱신할 수 있습니다.",
                MessageType.Info);
            return;
        }

        List<Component> targets = new List<Component>();
        foreach (Component component in selectedObject.GetComponentsInChildren<Component>(true))
        {
            if (component != null && BalanceReverseSync.ResolveEffectiveBalanceAsset(component) != null)
            {
                targets.Add(component);
            }
        }

        if (targets.Count == 0)
        {
            EditorGUILayout.HelpBox($"'{selectedObject.name}' 아래에 밸런스 SO를 물고 있는 컴포넌트가 없습니다.",
                MessageType.Info);
            return;
        }

        foreach (Component target in targets)
        {
            ScriptableObject asset = BalanceReverseSync.ResolveEffectiveBalanceAsset(target);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{target.GetType().Name} → {asset.name}");
                if (GUILayout.Button("갱신", GUILayout.Width(60)))
                {
                    BalanceReverseSync.Result result = BalanceReverseSync.Run(target);
                    Debug.Log($"[BalanceReverseSync] {result.Summary()}", target);
                    EditorUtility.DisplayDialog("밸런스 역동기화", result.Summary(), "확인");
                }
            }
        }
    }

    // ---------------------------------------------------------------- 생성 동작

    /// <summary>선택한 스크립트에서 SO 클래스 소스를 만듭니다.</summary>
    private void GenerateSoScript()
    {
        GameObject seed = ResolveSeedPrefab();

        // 창에서 지정한 경로를 우선 쓰고, 비어 있으면 설정 폴더로 떨어집니다.
        string target = !string.IsNullOrWhiteSpace(m_generateTargetPath)
            ? m_generateTargetPath
            : ScriptFolder;

        if (!BalanceScaffold.GenerateSoScript(
                ScriptTypes, m_soTypeName, seed, target, m_soKind, out string path, out string message))
        {
            EditorUtility.DisplayDialog("SO 코드 생성", message, "확인");
            return;
        }

        AssetDatabase.Refresh();
        PingAsset(path);
        EditorUtility.DisplayDialog("SO 코드 생성", message, "확인");
    }

    /// <summary>컴파일된 SO 타입으로 에셋을 만듭니다.</summary>
    /// <param name="soType">생성 대상 SO 타입입니다.</param>
    private void CreateSoAsset(Type soType)
    {
        GameObject seed = ResolveSeedPrefab();
        if (!BalanceScaffold.CreateSoAsset(
                ScriptTypes, soType, m_soAssetName, seed, NewAssetFolder, m_soKind, out string path, out string message))
        {
            EditorUtility.DisplayDialog("SO 데이터 생성", message, "확인");
            return;
        }

        AssetDatabase.Refresh();
        ScanTypes();
        PingAsset(path);
        EditorUtility.DisplayDialog("SO 데이터 생성", message, "확인");
    }

    /// <summary>가능한 단계까지 이어서 실행합니다.</summary>
    private void RunAll()
    {
        Type soType = CachedSoType;

        if (soType == null)
        {
            GenerateSoScript();
            return;
        }

        CreateSoAsset(soType);
        ExportType(soType);
    }

    /// <summary>프리팹에 대상 컴포넌트가 실제로 있을 때만 초기값 출처로 사용합니다.</summary>
    private GameObject ResolveSeedPrefab()
    {
        if (m_seedPrefab == null)
        {
            return null;
        }

        // 컴포넌트가 하나라도 붙어 있으면 씁니다. 없는 것은 생성 단계에서 건너뜁니다.
        foreach (Type type in ScriptTypes)
        {
            if (!typeof(Component).IsAssignableFrom(type) || m_seedPrefab.GetComponent(type) != null)
            {
                return m_seedPrefab;
            }
        }

        return null;
    }

    /// <summary>선택한 스크립트를 가진 프리팹이 프로젝트에 하나뿐이면 자동으로 채웁니다.</summary>
    private void AutoFillSeedPrefab()
    {
        List<Type> types = ScriptTypes;
        Type scriptType = types.Count > 0 ? types[0] : null;
        if (scriptType == null || !typeof(Component).IsAssignableFrom(scriptType))
        {
            return;
        }

        if (m_seedPrefab != null && m_seedPrefab.GetComponent(scriptType) != null)
        {
            return;
        }

        GameObject found = null;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.GetComponent(scriptType) == null)
            {
                continue;
            }

            if (found != null)
            {
                // 후보가 둘 이상이면 사용자가 직접 고르도록 비워 둡니다.
                return;
            }

            found = prefab;
        }

        m_seedPrefab = found;
    }

    // ---------------------------------------------------------------- 폴더 설정 UI

    /// <summary>내보내기 및 생성 폴더를 지정하는 설정 영역을 그립니다.</summary>
    private void DrawFolderSettings()
    {
        EditorGUILayout.Space();
        m_showFolders = EditorGUILayout.Foldout(m_showFolders, "폴더 설정", true);
        if (!m_showFolders)
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            DrawFolderRow("CSV(엑셀) 내보내기", CsvFolder, DefaultCsvFolder, value => CsvFolder = value);
            DrawFolderRow("SO 데이터(.asset)", NewAssetFolder, DefaultNewAssetFolder, value => NewAssetFolder = value);
            EditorGUILayout.LabelField(
                "데이터(.asset) = 무기 한 정의 수치가 담긴 에셋입니다. SO 코드(.cs)는 스크립트 옆에 생성되며 탭1에서 건별로 지정합니다.",
                EditorStyles.miniLabel);

            if (!IsAssetFolderPath(CsvFolder) || !IsAssetFolderPath(NewAssetFolder))
            {
                EditorGUILayout.HelpBox(
                    "폴더 경로는 프로젝트의 Assets 폴더 안이어야 합니다. 예: Assets/5.Data/DataSheet/CsvData",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "위 두 폴더는 개인 취향이라 이 PC에만 저장됩니다.\n" +
                $"반면 시트에 들어갈 SO를 찾는 범위와 자동 가져오기 여부는 팀 공통이라 코드에 있습니다({nameof(BalanceTableSettings)}).\n" +
                BalanceTableSettings.DescribeExportScope(),
                MessageType.None);
        }
    }

    /// <summary>폴더 한 줄을 직접 입력·탐색·기본값 복원과 함께 그립니다.</summary>
    /// <param name="label">표시할 항목 이름입니다.</param>
    /// <param name="current">현재 설정된 폴더 경로입니다.</param>
    /// <param name="defaultValue">기본값 버튼이 되돌릴 경로입니다.</param>
    /// <param name="apply">변경된 경로를 저장할 대상입니다.</param>
    private static void DrawFolderRow(string label, string current, string defaultValue, Action<string> apply)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            // 입력 중에는 반영하지 않고 Enter 또는 포커스 이동 시점에만 저장합니다.
            string edited = EditorGUILayout.DelayedTextField(label, current);
            if (edited != current)
                apply(edited);

            if (GUILayout.Button("찾아보기", GUILayout.Width(74)))
            {
                string picked = PickFolder(label, current);
                if (picked != current)
                    apply(picked);

                GUI.FocusControl(null);
            }

            using (new EditorGUI.DisabledScope(current == defaultValue))
            {
                if (GUILayout.Button("기본값", GUILayout.Width(60)))
                {
                    apply(defaultValue);
                    GUI.FocusControl(null);
                }
            }
        }
    }

    /// <summary>폴더 선택 창을 띄우고 프로젝트 상대 경로를 반환합니다.</summary>
    /// <param name="label">선택 창 제목에 사용할 항목 이름입니다.</param>
    /// <param name="current">취소하거나 잘못된 폴더를 고를 때 유지할 현재 경로입니다.</param>
    /// <returns>Assets 기준 상대 경로입니다.</returns>
    private static string PickFolder(string label, string current)
    {
        string start = AssetDatabase.IsValidFolder(current)
            ? Path.GetFullPath(current)
            : Path.GetFullPath("Assets");

        string picked = EditorUtility.OpenFolderPanel($"{label} 폴더 선택", start, string.Empty);
        if (string.IsNullOrEmpty(picked))
            return current;

        string relative = ToProjectRelative(picked);
        if (relative == null)
        {
            EditorUtility.DisplayDialog(
                "SO CSV",
                "이 프로젝트의 Assets 폴더 안에 있는 폴더만 선택할 수 있습니다.",
                "확인");
            return current;
        }

        return relative;
    }

    /// <summary>절대 경로를 Assets 기준 상대 경로로 바꿉니다.</summary>
    /// <param name="absolute">변환할 절대 경로입니다.</param>
    /// <returns>Assets 밖의 경로이면 <c>null</c>입니다.</returns>
    private static string ToProjectRelative(string absolute)
    {
        string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
        string target = Path.GetFullPath(absolute).Replace('\\', '/').TrimEnd('/');

        if (string.Equals(target, assets, StringComparison.OrdinalIgnoreCase))
            return "Assets";

        if (target.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
            return "Assets/" + target.Substring(assets.Length + 1);

        return null;
    }

    /// <summary>경로가 Assets 폴더 내부를 가리키는지 확인합니다.</summary>
    /// <param name="folder">검사할 폴더 경로입니다.</param>
    /// <returns>Assets 자신이거나 그 하위 경로이면 <c>true</c>입니다.</returns>
    private static bool IsAssetFolderPath(string folder)
    {
        return folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- scanning

    private void ScanTypes()
    {
        m_types.Clear();

        // 에셋 개수를 먼저 세어 둡니다. 0개인 타입도 목록에 남겨 헤더만 먼저 뽑을 수 있게 합니다.
        Dictionary<Type, int> counts = new Dictionary<Type, int>();
        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject"))
        {
            ScriptableObject so = AssetDatabase.LoadMainAssetAtPath(
                AssetDatabase.GUIDToAssetPath(guid)) as ScriptableObject;
            if (so == null)
            {
                continue;
            }

            Type type = so.GetType();
            counts.TryGetValue(type, out int c);
            counts[type] = c + 1;
        }

        foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
        {
            if (type.IsAbstract
                || !IsProjectType(type)
                || !typeof(IBalanceTableData).IsAssignableFrom(type))
            {
                continue;
            }

            counts.TryGetValue(type, out int count);
            m_types.Add(new TypeEntry
            {
                type = type,
                count = count,
                label = $"{type.Name}  ({count})"
            });
        }

        m_types.Sort((a, b) => string.CompareOrdinal(a.type.Name, b.type.Name));
        m_selectedIndex = Mathf.Clamp(m_selectedIndex, 0, Mathf.Max(0, m_types.Count - 1));
        Repaint();
    }

    /// <summary>이 프로젝트가 직접 정의한 타입인지 확인합니다.</summary>
    /// <param name="type">검사할 타입입니다.</param>
    /// <returns>프로젝트 스크립트 어셈블리 소속이면 true입니다.</returns>
    /// <remarks>
    /// Unity 패키지와 서드파티(MagicaCloth, VInspector 등)는 각자 어셈블리를 가지므로 이 검사로 걸러집니다.
    /// 그렇게 하지 않으면 목록에 수백 개가 쏟아져 쓸 수 없습니다.
    /// </remarks>
    private static bool IsProjectType(Type type)
    {
        string assembly = type.Assembly.GetName().Name;
        return assembly == "Assembly-CSharp" || assembly == "Assembly-CSharp-Editor";
    }

    /// <summary>CSV 경로에 들어올 수 있는 순수 밸런스 SO인지 검사합니다.</summary>
    private static bool TryValidateBalanceType(Type type, out string error)
    {
        if (type == null
            || !typeof(ScriptableObject).IsAssignableFrom(type)
            || !typeof(IBalanceTableData).IsAssignableFrom(type))
        {
            error = $"'{type?.Name ?? "(null)"}'은 {nameof(IBalanceTableData)} 구현 SO가 아니므로 밸런스 CSV에서 사용할 수 없습니다.";
            return false;
        }

        string referencePath = FindUnityObjectReferenceInSerializedFields(type, type.Name, new HashSet<Type>());
        if (!string.IsNullOrEmpty(referencePath))
        {
            error = $"'{type.Name}'의 '{referencePath}'에 Unity Object 참조가 있습니다. 사운드·이펙트·프리팹 참조는 Feedback SO에 두고 밸런스 CSV에서 제외해야 합니다.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 시트의 필드 행 집합이 현재 SO 스키마와 호환되는지 확인합니다.
    /// </summary>
    /// <param name="type">대상 ScriptableObject 타입입니다.</param>
    /// <param name="sheetFields">시트에 있는 필드 이름들입니다. <c>__</c>로 시작하는 메타는 무시합니다.</param>
    /// <param name="error">호환되지 않을 때 사람이 읽을 수 있는 사유입니다.</param>
    /// <returns>양쪽 필드 집합이 정확히 같으면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 행 단위로 조용히 건너뛰지 않고 <b>파일 단위로 거부</b>합니다. 개수가 어긋난 시트는
    /// 스크립트가 바뀐 뒤 다시 내보내지 않은 낡은 스냅샷이라, 남은 행만 반영하면
    /// "일부는 새 값, 일부는 옛 값"이라는 가장 찾기 어려운 상태가 만들어지기 때문입니다.
    /// <para>
    /// 정본이 SO인 동안에는 고치는 방법이 항상 같습니다. 다시 내보내고 그 시트를 고치면 됩니다.
    /// </para>
    /// </remarks>
    private static bool TryValidateSheetSchema(Type type, IEnumerable<string> sheetFields, out string error)
    {
        HashSet<string> current = new HashSet<string>(CollectSchemaFields(type), StringComparer.Ordinal);
        HashSet<string> incoming = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in sheetFields)
        {
            if (!string.IsNullOrEmpty(name) && !name.StartsWith("__", StringComparison.Ordinal))
            {
                incoming.Add(name);
            }
        }

        List<string> unknown = new List<string>();
        foreach (string name in incoming)
        {
            if (!current.Contains(name))
            {
                unknown.Add(name);
            }
        }

        List<string> missing = new List<string>();
        foreach (string name in current)
        {
            if (!incoming.Contains(name))
            {
                missing.Add(name);
            }
        }

        if (unknown.Count == 0 && missing.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"'{type.Name}' 스키마와 시트가 맞지 않습니다.");
        if (unknown.Count > 0)
        {
            sb.Append($" SO에 없는 행 {unknown.Count}개({DescribeNames(unknown)}).");
        }

        if (missing.Count > 0)
        {
            sb.Append($" 시트에 없는 필드 {missing.Count}개({DescribeNames(missing)}).");
        }

        sb.Append(" 스크립트가 바뀐 뒤 시트를 다시 내보내지 않은 상태로 보입니다. 내보내기로 갱신한 뒤 값을 옮겨 적으세요.");
        error = sb.ToString();
        return false;
    }

    /// <summary>타입별 스키마 검사 결과를 재사용하며 호환 여부를 확인합니다.</summary>
    /// <param name="type">대상 ScriptableObject 타입입니다.</param>
    /// <param name="sheetFields">시트에 있는 필드 이름들입니다.</param>
    /// <param name="cache">이번 파일에서 이미 검사한 타입의 결과입니다.</param>
    /// <param name="filePath">로그에 표시할 파일 경로입니다.</param>
    /// <returns>호환되면 <c>true</c>입니다.</returns>
    /// <remarks>한 시트의 모든 열이 같은 행 집합을 공유하므로 타입당 한 번만 검사하면 됩니다.</remarks>
    private static bool IsSchemaCompatible(
        Type type,
        IEnumerable<string> sheetFields,
        Dictionary<Type, bool> cache,
        string filePath)
    {
        if (cache.TryGetValue(type, out bool cached))
        {
            return cached;
        }

        bool compatible = TryValidateSheetSchema(type, sheetFields, out string error);
        if (!compatible)
        {
            Debug.LogWarning($"[SoCsv] '{Path.GetFileName(filePath)}' 반영을 건너뜁니다: {error}");
        }

        cache[type] = compatible;
        return compatible;
    }

    /// <summary>타입의 현재 직렬화 필드 이름을 모읍니다.</summary>
    /// <param name="type">검사할 ScriptableObject 타입입니다.</param>
    /// <returns>내보내기가 행으로 쓰는 것과 같은 필드 목록입니다.</returns>
    /// <remarks>임시 인스턴스를 만들어 읽습니다. 에셋이 하나도 없는 타입도 검사할 수 있어야 하기 때문입니다.</remarks>
    private static List<string> CollectSchemaFields(Type type)
    {
        List<string> ordered = new List<string>();
        HashSet<string> seen = new HashSet<string>();

        ScriptableObject probe = ScriptableObject.CreateInstance(type);
        try
        {
            CollectColumns(probe, ordered, seen);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }

        return ordered;
    }

    /// <summary>이름 목록을 로그 한 줄에 들어갈 길이로 줄입니다.</summary>
    /// <param name="names">표시할 이름들입니다.</param>
    /// <returns>앞의 몇 개와 남은 개수를 담은 문자열입니다.</returns>
    private static string DescribeNames(List<string> names)
    {
        const int shown = 3;
        if (names.Count <= shown)
        {
            return string.Join(", ", names);
        }

        return string.Join(", ", names.GetRange(0, shown)) + $" 외 {names.Count - shown}개";
    }

    private static string FindUnityObjectReferenceInSerializedFields(Type type, string path, HashSet<Type> visited)
    {
        if (type == null || !visited.Add(type))
        {
            return string.Empty;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(flags))
            {
                if (field.IsStatic || field.IsNotSerialized)
                {
                    continue;
                }

                bool isSerialized = field.IsPublic
                    || field.GetCustomAttribute<SerializeField>(true) != null
                    || field.GetCustomAttribute<SerializeReference>(true) != null;
                if (!isSerialized)
                {
                    continue;
                }

                string found = FindUnityObjectReferencePath(field.FieldType, path + "." + field.Name, visited);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Unity 직렬화 대상 필드 안에서 Object 참조가 처음 나타나는 경로를 찾습니다.</summary>
    private static string FindUnityObjectReferencePath(Type type, string path, HashSet<Type> visited)
    {
        if (type == null)
        {
            return string.Empty;
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            return path;
        }

        Type nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
        {
            return FindUnityObjectReferencePath(nullableType, path, visited);
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return string.Empty;
        }

        if (type.IsArray)
        {
            return FindUnityObjectReferencePath(type.GetElementType(), path + "[]", visited);
        }

        if (type.IsGenericType)
        {
            Type[] arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
            {
                string found = FindUnityObjectReferencePath(arguments[i], path + "[]", visited);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }

            return string.Empty;
        }

        return FindUnityObjectReferenceInSerializedFields(type, path, visited);
    }

    // ---------------------------------------------------------------- export

    private void ExportType(Type type)
    {
        ExportType(type, null);
    }

    /// <summary>지정한 파일 이름으로 한 타입을 CSV로 내보냅니다.</summary>
    /// <param name="type">내보낼 ScriptableObject 타입입니다.</param>
    /// <param name="fileNameOverride">확장자를 뺀 파일 이름입니다. 비우면 타입 이름을 씁니다.</param>
    private void ExportType(Type type, string fileNameOverride)
    {
        if (!TryValidateBalanceType(type, out string validationError))
        {
            Debug.LogError($"[SoCsv] {validationError}");
            EditorUtility.DisplayDialog("SO CSV", validationError, "확인");
            return;
        }

        EnsureFolder(CsvFolder);
        string fileName = string.IsNullOrWhiteSpace(fileNameOverride) ? GetCsvFileName(type) : fileNameOverride.Trim();
        string path = $"{CsvFolder}/{fileName}.csv";
        int count = WriteCsvForType(type, path);
        AssetDatabase.ImportAsset(path);

        Debug.Log($"[SoCsv] '{type.Name}' 에셋 {count}개를 {path}로 내보냈습니다.");
        PingAsset(path);
        EditorUtility.DisplayDialog("SO CSV", $"'{type.Name}' 에셋 {count}개를 내보냈습니다:\n{path}", "확인");
    }

    private void ExportAllTypes()
    {
        EnsureFolder(CsvFolder);
        int files = 0;
        int total = 0;
        int rejected = 0;
        foreach (TypeEntry entry in m_types)
        {
            if (!TryValidateBalanceType(entry.type, out string validationError))
            {
                Debug.LogError($"[SoCsv] {validationError}");
                rejected++;
                continue;
            }

            string path = $"{CsvFolder}/{GetCsvFileName(entry.type)}.csv";
            total += WriteCsvForType(entry.type, path);
            AssetDatabase.ImportAsset(path);
            files++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[SoCsv] 타입 {files}개 / 에셋 {total}개를 {CsvFolder}로 내보냈습니다. 거부:{rejected}");
        EditorUtility.DisplayDialog(
            "SO CSV",
            $"CSV {files}개 파일에 에셋 {total}개를 내보냈습니다.\n거부된 타입: {rejected}개\n{CsvFolder}",
            "확인");
    }

    /// <summary>한 타입의 에셋들을 세로형 CSV로 씁니다.</summary>
    /// <param name="type">내보낼 ScriptableObject 타입입니다.</param>
    /// <param name="filePath">쓸 파일 경로입니다.</param>
    /// <returns>내보낸 에셋 수입니다.</returns>
    /// <remarks>
    /// 필드가 행, 에셋이 열입니다. 밸런스 시트는 보통 필드가 수십 개인데 에셋은 몇 개뿐이라,
    /// 가로형으로 두면 옆으로 한없이 스크롤해야 합니다. 세로형이면 설명이 필드 이름 바로 옆에 붙어 읽기도 낫습니다.
    /// </remarks>
    /// <summary>
    /// 창을 열지 않고 한 타입을 CSV로 내보냅니다.
    /// </summary>
    /// <param name="type">내보낼 ScriptableObject 타입입니다.</param>
    /// <returns>내보낸 에셋 수와 쓴 파일 경로입니다.</returns>
    /// <remarks>
    /// 인스펙터·트레이너의 역동기화 버튼처럼 창 밖에서 부르는 경로를 위한 진입점입니다.
    /// 대화상자를 띄우지 않으므로 부르는 쪽이 결과를 알려야 합니다.
    /// </remarks>
    public static (int count, string path) ExportTypeSilently(Type type)
    {
        if (!TryValidateBalanceType(type, out string validationError))
        {
            Debug.LogError($"[SoCsv] {validationError}");
            return (-1, string.Empty);
        }

        EnsureFolder(CsvFolder);
        string path = $"{CsvFolder}/{GetCsvFileName(type)}.csv";
        int count = WriteCsvForType(type, path);
        AssetDatabase.ImportAsset(path);
        return (count, path);
    }

    private static int WriteCsvForType(Type type, string filePath)
    {
        if (!TryValidateBalanceType(type, out string validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        List<ScriptableObject> assets = LoadAllOfType(type);
        List<string> fields = CollectColumns(type, assets);

        // 에셋별로 값을 미리 읽어 둡니다. 열 하나가 에셋 하나입니다.
        List<string> assetNames = new List<string>();
        List<string> guids = new List<string>();
        List<string> paths = new List<string>();
        List<Dictionary<string, string>> values = new List<Dictionary<string, string>>();

        foreach (ScriptableObject asset in assets)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            assetNames.Add(Path.GetFileNameWithoutExtension(assetPath));
            guids.Add(AssetDatabase.AssetPathToGUID(assetPath));
            paths.Add(assetPath);

            SerializedObject so = new SerializedObject(asset);
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in fields)
            {
                SerializedProperty prop = so.FindProperty(field);
                map[field] = prop != null ? SoCsvCodec.EncodeCell(prop) : string.Empty;
            }

            values.Add(map);
        }

        StringBuilder sb = new StringBuilder();

        List<string> header = new List<string> { ColField, RowTooltip };
        header.AddRange(assetNames);
        sb.AppendLine(JoinCsv(header));

        sb.AppendLine(JoinCsv(MetaRow(ColType, type.FullName, assets.Count)));
        sb.AppendLine(JoinCsv(BuildRow(ColGuid, string.Empty, guids)));
        sb.AppendLine(JoinCsv(BuildRow(ColPath, string.Empty, paths)));

        foreach (string field in fields)
        {
            List<string> cells = new List<string>();
            foreach (Dictionary<string, string> map in values)
            {
                cells.Add(map.TryGetValue(field, out string v) ? v : string.Empty);
            }

            sb.AppendLine(JoinCsv(BuildRow(field, GetFieldTooltip(type, field), cells)));
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        return assets.Count;
    }

    /// <summary>필드 이름·설명과 에셋별 값을 한 행으로 묶습니다.</summary>
    private static List<string> BuildRow(string field, string tooltip, List<string> cells)
    {
        List<string> row = new List<string> { field, tooltip };
        row.AddRange(cells);
        return row;
    }

    /// <summary>모든 에셋 열에 같은 값을 채우는 메타 행을 만듭니다.</summary>
    private static List<string> MetaRow(string field, string value, int assetCount)
    {
        List<string> cells = new List<string>();
        for (int i = 0; i < assetCount; i++)
        {
            cells.Add(value);
        }

        return BuildRow(field, string.Empty, cells);
    }

    private static List<string> CollectColumns(Type type, List<ScriptableObject> assets)
    {
        List<string> ordered = new List<string>();
        HashSet<string> seen = new HashSet<string>();

        foreach (ScriptableObject asset in assets)
        {
            CollectColumns(asset, ordered, seen);
        }

        // Balance schemas must be exportable before their first asset is created.
        if (assets.Count == 0 && type != null && !type.IsAbstract)
        {
            ScriptableObject template = ScriptableObject.CreateInstance(type);
            try
            {
                CollectColumns(template, ordered, seen);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        return ordered;
    }

    private static void CollectColumns(
        ScriptableObject asset,
        List<string> ordered,
        HashSet<string> seen)
    {
        SerializedObject so = new SerializedObject(asset);
        SerializedProperty it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script")
            {
                continue;
            }

            if (seen.Add(it.name))
            {
                ordered.Add(it.name);
            }
        }
    }

    // ---------------------------------------------------------------- import

    private void ImportCsv()
    {
        string path = EditorUtility.OpenFilePanel("CSV 가져오기", FullFolder(CsvFolder), "csv");
        if (string.IsNullOrEmpty(path))
            return;

        if (!EditorUtility.DisplayDialog(
                "SO CSV",
                $"가져올 파일:\n{path}\n\n__Guid(다음으로 __Path)로 에셋을 찾아 갱신하고, 짝이 없는 행은 새 에셋을 만듭니다. 되돌리기(Undo)를 지원합니다.",
                "가져오기",
                "취소"))
        {
            return;
        }

        (int updated, int created, int skipped) = ImportFile(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"갱신: {updated}개\n생성: {created}개\n건너뜀: {skipped}개";
        EditorUtility.DisplayDialog("SO CSV", summary, "확인");
        ScanTypes();
    }

    /// <summary>
    /// Reads one CSV file and bakes its rows into ScriptableObject assets.
    /// Shared by the manual Import button and the auto-import post-processor.
    /// Performs no UI; returns the operation counts.
    /// </summary>
    public static (int updated, int created, int skipped) ImportFile(string filePath)
    {
        int updated = 0, created = 0, skipped = 0;

        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[SoCsv] CSV 파일을 찾을 수 없습니다: {filePath}");
            return (updated, created, skipped);
        }

        List<List<string>> rows = SoCsvText.ParseCsv(File.ReadAllText(filePath, Encoding.UTF8));
        if (rows.Count < 2)
        {
            Debug.LogWarning($"[SoCsv] '{filePath}': 헤더와 데이터 행이 최소 한 줄씩 필요합니다.");
            return (updated, created, skipped);
        }

        // 첫 칸으로 방향을 판별합니다. 세로형은 __Field, 예전 가로형은 __Type 으로 시작합니다.
        string firstCell = rows[0].Count > 0 ? rows[0][0].Trim() : string.Empty;
        if (string.Equals(firstCell, ColField, StringComparison.Ordinal))
        {
            return ImportTransposed(filePath, rows);
        }

        Dictionary<string, int> col = SoCsvText.BuildColumnMap(rows[0]);
        if (!col.ContainsKey(ColType))
        {
            Debug.LogWarning($"[SoCsv] '{filePath}': 첫 칸이 '{ColField}'도 '{ColType}'도 아니어서 건너뜁니다.");
            return (updated, created, skipped);
        }

        Dictionary<Type, bool> schemaCache = new Dictionary<Type, bool>();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 1; i < rows.Count; i++)
            {
                List<string> row = rows[i];
                string typeName = SoCsvText.Cell(row, col, ColType).Trim();

                // __로 시작하는 표시 행(컬럼 설명 등)은 데이터가 아니므로 조용히 건너뜁니다.
                if (typeName.StartsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }

                Type type = SoTypeResolver.Resolve(typeName);
                if (type == null)
                {
                    Debug.LogWarning($"[SoCsv] {i + 1}번째 행을 건너뜁니다: 알 수 없는 타입 '{typeName}'.");
                    skipped++;
                    continue;
                }

                if (!TryValidateBalanceType(type, out string validationError))
                {
                    Debug.LogWarning($"[SoCsv] {i + 1}번째 행을 건너뜁니다: {validationError}");
                    skipped++;
                    continue;
                }

                // 세로형과 같은 이유로 낡은 시트는 부분 반영하지 않고 통째로 건너뜁니다.
                if (!IsSchemaCompatible(type, col.Keys, schemaCache, filePath))
                {
                    skipped++;
                    continue;
                }

                string guid = SoCsvText.Cell(row, col, ColGuid).Trim();
                string assetPath = SoCsvText.Cell(row, col, ColPath).Trim();

                ScriptableObject asset = ResolveAsset(guid, assetPath, type, out bool isNew);
                if (asset == null)
                {
                    skipped++;
                    continue;
                }

                if (isNew) created++; else updated++;

                ApplyRow(asset, type, row, col);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[SoCsv] '{Path.GetFileName(filePath)}' 가져오기 완료 → 갱신:{updated} 생성:{created} 건너뜀:{skipped}");
        return (updated, created, skipped);
    }

    /// <summary>세로형 시트를 읽어 에셋에 반영합니다.</summary>
    /// <param name="filePath">로그에 표시할 파일 경로입니다.</param>
    /// <param name="rows">파싱된 전체 행입니다.</param>
    /// <returns>갱신·생성·건너뜀 수입니다.</returns>
    /// <remarks>열 하나가 에셋 하나이므로, 열마다 __Type·__Guid·__Path를 모아 대상을 찾은 뒤 각 필드 행을 대입합니다.</remarks>
    private static (int updated, int created, int skipped) ImportTransposed(string filePath, List<List<string>> rows)
    {
        int updated = 0, created = 0, skipped = 0;

        // 필드 이름 -> 그 행. 메타 행도 같은 방식으로 찾습니다.
        Dictionary<string, List<string>> byField = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int assetColumns = 0;
        for (int i = 1; i < rows.Count; i++)
        {
            List<string> row = rows[i];
            if (row.Count == 0)
            {
                continue;
            }

            string field = row[0].Trim();
            if (field.Length == 0 || byField.ContainsKey(field))
            {
                continue;
            }

            byField[field] = row;
            assetColumns = Mathf.Max(assetColumns, row.Count - 2);
        }

        if (!byField.ContainsKey(ColType))
        {
            Debug.LogWarning($"[SoCsv] '{filePath}': '{ColType}' 행이 없어 파일을 건너뜁니다.");
            return (updated, created, skipped);
        }

        Dictionary<Type, bool> schemaCache = new Dictionary<Type, bool>();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int c = 0; c < assetColumns; c++)
            {
                string typeName = CellAt(byField, ColType, c);
                if (typeName.Length == 0)
                {
                    continue;
                }

                Type type = SoTypeResolver.Resolve(typeName);
                if (type == null)
                {
                    Debug.LogWarning($"[SoCsv] {c + 1}번째 에셋 열을 건너뜁니다: 알 수 없는 타입 '{typeName}'.");
                    skipped++;
                    continue;
                }

                if (!TryValidateBalanceType(type, out string validationError))
                {
                    Debug.LogWarning($"[SoCsv] {c + 1}번째 에셋 열을 건너뜁니다: {validationError}");
                    skipped++;
                    continue;
                }

                // 낡은 시트가 일부 필드만 되돌려 놓는 상태를 막기 위해 스키마가 어긋나면 통째로 건너뜁니다.
                if (!IsSchemaCompatible(type, byField.Keys, schemaCache, filePath))
                {
                    skipped++;
                    continue;
                }

                ScriptableObject asset = ResolveAsset(
                    CellAt(byField, ColGuid, c), CellAt(byField, ColPath, c), type, out bool isNew);
                if (asset == null)
                {
                    skipped++;
                    continue;
                }

                if (isNew) created++; else updated++;

                Undo.RecordObject(asset, "Import ScriptableObject CSV");
                SerializedObject so = new SerializedObject(asset);
                foreach (KeyValuePair<string, List<string>> kv in byField)
                {
                    // __로 시작하는 행은 메타·설명이므로 값 대입 대상이 아닙니다.
                    if (kv.Key.StartsWith("__", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    SerializedProperty prop = so.FindProperty(kv.Key);
                    if (prop == null)
                    {
                        Debug.LogWarning($"[SoCsv] '{type.Name}': '{kv.Key}' 필드가 없어 해당 행을 무시합니다.", asset);
                        continue;
                    }

                    string cell = c + 2 < kv.Value.Count ? kv.Value[c + 2] : string.Empty;
                    SoCsvCodec.DecodeCell(prop, cell, $"{type.Name}.{kv.Key}");
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[SoCsv] '{Path.GetFileName(filePath)}' 가져오기 완료 → 갱신:{updated} 생성:{created} 건너뜀:{skipped}");
        return (updated, created, skipped);
    }

    /// <summary>세로형에서 지정한 행의 에셋 열 값을 읽습니다.</summary>
    /// <param name="byField">필드 이름으로 찾는 행 목록입니다.</param>
    /// <param name="field">읽을 행 이름입니다.</param>
    /// <param name="assetIndex">0부터 시작하는 에셋 열 번호입니다.</param>
    /// <returns>값이 없으면 빈 문자열입니다.</returns>
    private static string CellAt(Dictionary<string, List<string>> byField, string field, int assetIndex)
    {
        if (!byField.TryGetValue(field, out List<string> row))
        {
            return string.Empty;
        }

        int index = assetIndex + 2;
        return index < row.Count ? row[index].Trim() : string.Empty;
    }

    private static void ApplyRow(ScriptableObject asset, Type type, List<string> row, Dictionary<string, int> col)
    {
        Undo.RecordObject(asset, "Import ScriptableObject CSV");
        SerializedObject so = new SerializedObject(asset);

        foreach (KeyValuePair<string, int> kv in col)
        {
            string column = kv.Key;
            if (column == ColType || column == ColGuid || column == ColPath)
                continue;

            SerializedProperty prop = so.FindProperty(column);
            if (prop == null)
            {
                Debug.LogWarning($"[SoCsv] '{type.Name}': '{column}' 필드가 없어 해당 셀을 무시합니다.", asset);
                continue;
            }

            string cell = kv.Value < row.Count ? row[kv.Value] : string.Empty;
            SoCsvCodec.DecodeCell(prop, cell, $"{type.Name}.{column}");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
    }

    private static ScriptableObject ResolveAsset(string guid, string assetPath, Type type, out bool isNew)
    {
        isNew = false;

        // 1) by GUID
        if (!string.IsNullOrEmpty(guid))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(p))
            {
                ScriptableObject existing = AssetDatabase.LoadAssetAtPath(p, type) as ScriptableObject;
                if (existing != null)
                    return existing;
            }
        }

        // 2) by path
        if (!string.IsNullOrEmpty(assetPath))
        {
            ScriptableObject existing = AssetDatabase.LoadAssetAtPath(assetPath, type) as ScriptableObject;
            if (existing != null)
                return existing;
        }

        // 3) create new
        string targetPath = !string.IsNullOrEmpty(assetPath) ? assetPath : $"{NewAssetFolder}/{type.Name}.asset";
        string folder = Path.GetDirectoryName(targetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
        {
            Debug.LogError($"[SoCsv] 새 '{type.Name}' 에셋을 만들 폴더를 결정할 수 없습니다.");
            return null;
        }

        EnsureFolder(folder);

        ScriptableObject created = ScriptableObject.CreateInstance(type);
        string unique = AssetDatabase.GenerateUniqueAssetPath(targetPath);
        AssetDatabase.CreateAsset(created, unique);
        Undo.RegisterCreatedObjectUndo(created, "Create ScriptableObject");
        Debug.Log($"[SoCsv] 새 에셋을 만들었습니다: {unique}", created);
        isNew = true;
        return created;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>필드에 붙은 [Tooltip] 설명을 찾습니다. 기반 타입까지 거슬러 올라갑니다.</summary>
    /// <param name="type">필드를 선언한 ScriptableObject 타입입니다.</param>
    /// <param name="fieldName">직렬화 필드 이름입니다.</param>
    /// <returns>설명이 없으면 빈 문자열입니다.</returns>
    private static string GetFieldTooltip(Type type, string fieldName)
    {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            System.Reflection.FieldInfo field = current.GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);
            if (field == null)
            {
                continue;
            }

            object[] found = field.GetCustomAttributes(typeof(TooltipAttribute), true);
            return found.Length > 0 ? ((TooltipAttribute)found[0]).tooltip ?? string.Empty : string.Empty;
        }

        return string.Empty;
    }

    /// <summary>SO 타입 이름에서 CSV 파일 이름을 만듭니다.</summary>
    /// <param name="type">내보낼 ScriptableObject 타입입니다.</param>
    /// <returns>끝의 "SO"를 "CSV"로 바꾼 이름입니다.</returns>
    /// <remarks>
    /// 시트 파일은 SO가 아니라 시트이므로 접미어를 바꿉니다. 폴더에서 둘을 눈으로 구분할 수 있고,
    /// 같은 이름의 에셋과 시트가 섞여 헷갈리는 것도 막습니다. 가져오기는 파일명이 아니라 __Type 행으로
    /// 타입을 찾으므로 이름을 바꿔도 왕복은 그대로 동작합니다.
    /// </remarks>
    public static string GetCsvFileName(Type type)
    {
        string name = type.Name;
        return name.EndsWith("SO", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - 2) + "CSV"
            : name + "CSV";
    }

    /// <summary>시트에 들어갈 대상 에셋을 모읍니다.</summary>
    /// <param name="type">찾을 ScriptableObject 타입입니다.</param>
    /// <returns>경로 순으로 정렬된 에셋 목록입니다.</returns>
    /// <remarks>
    /// 검색 범위는 <see cref="BalanceTableSettings.ExportSearchFolders"/>로 제한합니다.
    /// 바꿔 끼워 보려고 만든 임시 SO가 최종 시트에 섞여 들어가는 것을 막기 위해서입니다.
    /// </remarks>
    private static List<ScriptableObject> LoadAllOfType(Type type)
    {
        List<ScriptableObject> list = new List<ScriptableObject>();
        string[] folders = BalanceTableSettings.GetExportSearchFolders();
        string[] guids = folders == null
            ? AssetDatabase.FindAssets($"t:{type.Name}")
            : AssetDatabase.FindAssets($"t:{type.Name}", folders);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject so = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
            // FindAssets("t:Name") can over-match by short name; keep only exact type.
            if (so != null && so.GetType() == type)
                list.Add(so);
        }
        list.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        return list;
    }

    private static string FullFolder(string assetFolder)
    {
        return AssetDatabase.IsValidFolder(assetFolder) ? Path.GetFullPath(assetFolder) : Path.GetFullPath("Assets");
    }

    /// <summary>Creates an "Assets/..." folder (and any missing parents) if it does not exist.</summary>
    private static void EnsureFolder(string assetFolder)
    {
        assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
            return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(assetFolder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            return;

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void PingAsset(string assetPath)
    {
        UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (obj != null)
            EditorGUIUtility.PingObject(obj);
    }

    private static string JoinCsv(List<string> cells)
    {
        for (int i = 0; i < cells.Count; i++)
            cells[i] = SoCsvText.Escape(cells[i]);
        return string.Join(",", cells);
    }
}

// ===================================================================== settings

/// <summary>
/// SO CSV 도구의 폴더 설정을 <see cref="EditorPrefs"/>에 보관합니다.
/// </summary>
/// <remarks>
/// EditorPrefs는 이 PC의 모든 Unity 프로젝트가 공유하므로, 프로젝트 경로 해시를 키에 섞어
/// 같은 프로젝트의 다른 체크아웃끼리 설정이 섞이지 않게 합니다. 팀원 간에는 공유되지 않습니다.
/// </remarks>
internal static class SoCsvSettings
{
    /// <summary>CSV 내보내기 폴더를 저장하는 설정 키입니다.</summary>
    public const string CsvFolderKey = "CsvFolder";

    /// <summary>새 .asset 생성 폴더를 저장하는 설정 키입니다.</summary>
    public const string NewAssetFolderKey = "NewAssetFolder";

    private static string s_prefix;

    private static string Prefix
    {
        get
        {
            if (string.IsNullOrEmpty(s_prefix))
            {
                string project = Application.dataPath.Replace('\\', '/');
                s_prefix = $"GrayZone.SoCsv.{StableHash(project)}.";
            }

            return s_prefix;
        }
    }

    /// <summary>저장된 폴더 경로를 반환하고, 비어 있으면 기본값을 사용합니다.</summary>
    /// <param name="key">설정 키입니다.</param>
    /// <param name="fallback">저장된 값이 없을 때 사용할 기본 경로입니다.</param>
    /// <returns>구분자를 정리한 폴더 경로입니다.</returns>
    public static string GetFolder(string key, string fallback)
    {
        string value = EditorPrefs.GetString(Prefix + key, fallback);
        return string.IsNullOrWhiteSpace(value) ? Normalize(fallback) : Normalize(value);
    }

    /// <summary>폴더 경로를 정리해 저장합니다.</summary>
    /// <param name="key">설정 키입니다.</param>
    /// <param name="value">저장할 폴더 경로입니다.</param>
    public static void SetFolder(string key, string value)
    {
        EditorPrefs.SetString(Prefix + key, Normalize(value));
    }

    private static string Normalize(string folder)
    {
        return (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
    }

    /// <summary>실행마다 같은 값을 보장하는 FNV-1a 해시입니다.</summary>
    /// <param name="value">해시할 문자열입니다.</param>
    /// <returns>8자리 16진수 해시입니다.</returns>
    private static string StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }

            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}

// ===================================================================== codec

/// <summary>Encodes/decodes a single SerializedProperty to/from a CSV cell.</summary>
internal static class SoCsvCodec
{
    /// <summary>프로퍼티 하나를 CSV 셀 문자열로 바꿉니다.</summary>
    /// <param name="p">인코딩할 프로퍼티입니다.</param>
    /// <returns>사람이 읽고 고칠 수 있는 셀 값입니다. 복잡한 타입은 JSON 한 칸으로 접어 넣습니다.</returns>
    public static string EncodeCell(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return p.boolValue ? "TRUE" : "FALSE";
            // float 필드는 float 정밀도로 씁니다. doubleValue를 그대로 쓰면 5.335가
            // 5.3350000381469727처럼 넓혀져 시트가 지저분해지고 손으로 고치기도 어려워집니다.
            case SerializedPropertyType.Float:
                return p.type == "float"
                    ? ((float)p.doubleValue).ToString("R", CultureInfo.InvariantCulture)
                    : p.doubleValue.ToString("R", CultureInfo.InvariantCulture);
            case SerializedPropertyType.String: return p.stringValue ?? string.Empty;
            case SerializedPropertyType.Enum: return EnumName(p);
            case SerializedPropertyType.ObjectReference: return EncodeObjectRef(p.objectReferenceValue);
            case SerializedPropertyType.Vector2: return V(p.vector2Value.x, p.vector2Value.y);
            case SerializedPropertyType.Vector3: return V(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z);
            case SerializedPropertyType.Vector4: return V(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w);
            case SerializedPropertyType.Quaternion: return V(p.quaternionValue.x, p.quaternionValue.y, p.quaternionValue.z, p.quaternionValue.w);
            case SerializedPropertyType.Color: return V(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a);
            default:
                object tree = SoCsvTree.Read(p);
                return tree == null ? string.Empty : MiniJson.Serialize(tree);
        }
    }

    /// <summary>CSV 셀 문자열을 프로퍼티에 씁니다.</summary>
    /// <param name="p">값을 받을 프로퍼티입니다.</param>
    /// <param name="cell">시트에서 읽은 셀 값입니다.</param>
    /// <param name="context">해석 실패를 알릴 때 표시할 "타입.필드" 식별자입니다.</param>
    public static void DecodeCell(SerializedProperty p, string cell, string context)
    {
        cell ??= string.Empty;
        try
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: p.longValue = ParseLong(cell); break;
                case SerializedPropertyType.Boolean: p.boolValue = ParseBool(cell); break;
                case SerializedPropertyType.Float: p.doubleValue = ParseDouble(cell); break;
                case SerializedPropertyType.String: p.stringValue = cell; break;
                case SerializedPropertyType.Enum: SetEnumByName(p, cell.Trim()); break;
                case SerializedPropertyType.ObjectReference: p.objectReferenceValue = DecodeObjectRef(cell); break;
                case SerializedPropertyType.Vector2: { float[] f = Floats(cell, 2); p.vector2Value = new Vector2(f[0], f[1]); break; }
                case SerializedPropertyType.Vector3: { float[] f = Floats(cell, 3); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
                case SerializedPropertyType.Vector4: { float[] f = Floats(cell, 4); p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); break; }
                case SerializedPropertyType.Quaternion: { float[] f = Floats(cell, 4); p.quaternionValue = new Quaternion(f[0], f[1], f[2], f[3]); break; }
                case SerializedPropertyType.Color: { float[] f = Floats(cell, 4); p.colorValue = new Color(f[0], f[1], f[2], f[3]); break; }
                default:
                    if (!string.IsNullOrWhiteSpace(cell))
                        SoCsvTree.Write(p, MiniJson.Deserialize(cell));
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SoCsv] '{context}' 값을 셀 '{cell}'에서 해석하지 못했습니다: {e.Message}");
        }
    }

    // ----- object references stored as "guid:localId" -----

    /// <summary>Unity 오브젝트 참조를 셀에 적을 문자열로 바꿉니다.</summary>
    /// <param name="obj">인코딩할 참조입니다.</param>
    /// <returns>비어 있으면 빈 문자열입니다.</returns>
    /// <remarks>밸런스 SO는 참조를 담지 않으므로 이 경로는 예외 상황 대비입니다.</remarks>
    public static string EncodeObjectRef(UnityEngine.Object obj)
    {
        if (obj == null)
            return string.Empty;
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
            return $"{guid}:{localId}";
        return string.Empty;
    }

    /// <summary>셀 문자열에서 Unity 오브젝트 참조를 복원합니다.</summary>
    /// <param name="value">인코딩된 참조 문자열입니다.</param>
    /// <returns>찾지 못하면 <c>null</c>입니다.</returns>
    public static UnityEngine.Object DecodeObjectRef(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0)
            return null;

        string guid = value;
        long localId = 0;
        int colon = value.IndexOf(':');
        if (colon > 0)
        {
            guid = value.Substring(0, colon);
            long.TryParse(value.Substring(colon + 1), out localId);
        }

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
            return null;

        if (localId != 0)
        {
            foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (candidate != null
                    && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long id)
                    && id == localId)
                    return candidate;
            }
        }

        return AssetDatabase.LoadMainAssetAtPath(path);
    }

    // ----- primitives -----

    private static string EnumName(SerializedProperty p)
    {
        return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length
            ? p.enumNames[p.enumValueIndex]
            : p.intValue.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>enum 프로퍼티를 멤버 이름으로 지정합니다.</summary>
    /// <param name="p">값을 받을 enum 프로퍼티입니다.</param>
    /// <param name="name">지정할 멤버 이름입니다.</param>
    /// <remarks>이름으로 쓰는 이유는 멤버 순서가 바뀌어도 시트가 계속 맞기 때문입니다.</remarks>
    public static void SetEnumByName(SerializedProperty p, string name)
    {
        int idx = Array.IndexOf(p.enumNames, name);
        if (idx < 0 && int.TryParse(name, out int raw) && raw >= 0 && raw < p.enumNames.Length)
            idx = raw;
        p.enumValueIndex = idx >= 0 ? idx : 0;
    }

    private static string V(params float[] xs)
    {
        string[] s = new string[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            s[i] = xs[i].ToString("R", CultureInfo.InvariantCulture);
        return string.Join(";", s);
    }

    private static float[] Floats(string cell, int n)
    {
        float[] result = new float[n];
        string[] parts = (cell ?? string.Empty).Split(';');
        for (int i = 0; i < n; i++)
            result[i] = i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        return result;
    }

    /// <summary>셀 문자열을 정수로 해석합니다. 해석할 수 없으면 0입니다.</summary>
    /// <remarks>고정 문화권으로 읽습니다. 엑셀의 지역 설정이 달라도 시트가 같게 해석되어야 하기 때문입니다.</remarks>
    public static long ParseLong(string s) => long.TryParse((s ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;

    /// <summary>셀 문자열을 실수로 해석합니다. 해석할 수 없으면 0입니다.</summary>
    /// <remarks>고정 문화권으로 읽으므로 소수점은 항상 점입니다. 쉼표를 쓰는 지역 설정에서도 값이 흔들리지 않습니다.</remarks>
    public static double ParseDouble(string s) => double.TryParse((s ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    /// <summary>셀 문자열을 bool로 해석합니다. 해석할 수 없으면 <c>false</c>입니다.</summary>
    public static bool ParseBool(string s)
    {
        s = (s ?? string.Empty).Trim();
        return s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1"
            || s.Equals("Y", StringComparison.OrdinalIgnoreCase) || s.Equals("YES", StringComparison.OrdinalIgnoreCase);
    }
}

// ============================================================ property <-> tree

/// <summary>
/// Recursively converts a SerializedProperty subtree to/from plain objects
/// (Dictionary / List / scalar) for JSON fallback of complex fields.
/// </summary>
internal static class SoCsvTree
{
    /// <summary>프로퍼티 값을 JSON으로 직렬화할 수 있는 형태로 읽어 냅니다.</summary>
    /// <param name="p">읽을 프로퍼티입니다.</param>
    /// <returns>중첩 구조는 사전과 목록으로 펼친 결과입니다.</returns>
    public static object Read(SerializedProperty p)
    {
        if (p.isArray && p.propertyType == SerializedPropertyType.Generic)
        {
            List<object> list = new List<object>(p.arraySize);
            for (int i = 0; i < p.arraySize; i++)
                list.Add(Read(p.GetArrayElementAtIndex(i)));
            return list;
        }

        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue;
            case SerializedPropertyType.Boolean: return p.boolValue;
            case SerializedPropertyType.Float: return p.doubleValue;
            case SerializedPropertyType.String: return p.stringValue ?? string.Empty;
            case SerializedPropertyType.Enum: return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? p.enumNames[p.enumValueIndex] : (object)p.intValue;
            case SerializedPropertyType.ObjectReference: return SoCsvCodec.EncodeObjectRef(p.objectReferenceValue);
            case SerializedPropertyType.Vector2: return new List<object> { (double)p.vector2Value.x, (double)p.vector2Value.y };
            case SerializedPropertyType.Vector3: return new List<object> { (double)p.vector3Value.x, (double)p.vector3Value.y, (double)p.vector3Value.z };
            case SerializedPropertyType.Vector4: return new List<object> { (double)p.vector4Value.x, (double)p.vector4Value.y, (double)p.vector4Value.z, (double)p.vector4Value.w };
            case SerializedPropertyType.Quaternion: return new List<object> { (double)p.quaternionValue.x, (double)p.quaternionValue.y, (double)p.quaternionValue.z, (double)p.quaternionValue.w };
            case SerializedPropertyType.Color: return new List<object> { (double)p.colorValue.r, (double)p.colorValue.g, (double)p.colorValue.b, (double)p.colorValue.a };
            case SerializedPropertyType.Generic:
                Dictionary<string, object> dict = new Dictionary<string, object>();
                SerializedProperty end = p.GetEndProperty();
                SerializedProperty it = p.Copy();
                bool enter = true;
                while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
                {
                    enter = false;
                    dict[it.name] = Read(it.Copy());
                }
                return dict;
            default:
                return null; // AnimationCurve/Gradient/etc. are not round-tripped.
        }
    }

    /// <summary>JSON에서 읽어 낸 값을 프로퍼티 구조에 맞춰 씁니다.</summary>
    /// <param name="p">값을 받을 프로퍼티입니다.</param>
    /// <param name="value">사전·목록·스칼라 중 하나입니다.</param>
    public static void Write(SerializedProperty p, object value)
    {
        if (p.isArray && p.propertyType == SerializedPropertyType.Generic)
        {
            List<object> list = value as List<object> ?? new List<object>();
            p.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
                Write(p.GetArrayElementAtIndex(i), list[i]);
            return;
        }

        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: p.longValue = ToLong(value); break;
            case SerializedPropertyType.Boolean: p.boolValue = ToBool(value); break;
            case SerializedPropertyType.Float: p.doubleValue = ToDouble(value); break;
            case SerializedPropertyType.String: p.stringValue = value?.ToString() ?? string.Empty; break;
            case SerializedPropertyType.Enum: SoCsvCodec.SetEnumByName(p, value?.ToString() ?? string.Empty); break;
            case SerializedPropertyType.ObjectReference: p.objectReferenceValue = SoCsvCodec.DecodeObjectRef(value?.ToString()); break;
            case SerializedPropertyType.Vector2: { float[] f = Vec(value, 2); p.vector2Value = new Vector2(f[0], f[1]); break; }
            case SerializedPropertyType.Vector3: { float[] f = Vec(value, 3); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
            case SerializedPropertyType.Vector4: { float[] f = Vec(value, 4); p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); break; }
            case SerializedPropertyType.Quaternion: { float[] f = Vec(value, 4); p.quaternionValue = new Quaternion(f[0], f[1], f[2], f[3]); break; }
            case SerializedPropertyType.Color: { float[] f = Vec(value, 4); p.colorValue = new Color(f[0], f[1], f[2], f[3]); break; }
            case SerializedPropertyType.Generic:
                Dictionary<string, object> dict = value as Dictionary<string, object>;
                if (dict == null)
                    break;
                SerializedProperty end = p.GetEndProperty();
                SerializedProperty it = p.Copy();
                bool enter = true;
                while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
                {
                    enter = false;
                    if (dict.TryGetValue(it.name, out object child))
                        Write(it.Copy(), child);
                }
                break;
        }
    }

    private static float[] Vec(object value, int n)
    {
        float[] result = new float[n];
        if (value is List<object> list)
        {
            for (int i = 0; i < n && i < list.Count; i++)
                result[i] = (float)ToDouble(list[i]);
        }
        return result;
    }

    private static long ToLong(object v) => v is long l ? l : v is double d ? (long)d : long.TryParse(v?.ToString(), out long r) ? r : 0;
    private static double ToDouble(object v) => v is double d ? d : v is long l ? l : double.TryParse(v?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0;
    private static bool ToBool(object v) => v is bool b ? b : SoCsvCodec.ParseBool(v?.ToString());
}

// ===================================================================== type resolver

internal static class SoTypeResolver
{
    private static Dictionary<string, Type> s_cache;

    /// <summary>시트의 <c>__Type</c> 행에 적힌 이름으로 실제 타입을 찾습니다.</summary>
    /// <param name="fullName">내보낼 때 기록한 타입 전체 이름입니다.</param>
    /// <returns>찾지 못하면 <c>null</c>입니다.</returns>
    /// <remarks>파일명이 아니라 이 행으로 타입을 정하므로 시트 이름을 바꿔도 왕복이 유지됩니다.</remarks>
    public static Type Resolve(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return null;

        if (s_cache == null)
        {
            s_cache = new Dictionary<string, Type>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (!string.IsNullOrEmpty(t.FullName))
                    s_cache[t.FullName] = t;
            }
        }

        return s_cache.TryGetValue(fullName, out Type type) ? type : null;
    }
}

// ===================================================================== CSV text

internal static class SoCsvText
{
    /// <summary>쉼표·따옴표·줄바꿈이 든 값을 CSV 규칙에 맞게 감쌉니다.</summary>
    public static string Escape(string value)
    {
        value ??= string.Empty;
        bool quote = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
        return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>머리말 행에서 컬럼 이름과 위치의 대응을 만듭니다.</summary>
    /// <param name="header">첫 행의 셀 목록입니다.</param>
    /// <returns>컬럼 이름으로 인덱스를 찾는 사전입니다.</returns>
    public static Dictionary<string, int> BuildColumnMap(List<string> header)
    {
        Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            string key = header[i].Trim();
            if (key.Length > 0 && !map.ContainsKey(key))
                map[key] = i;
        }
        return map;
    }

    /// <summary>컬럼 이름으로 한 행에서 셀 값을 꺼냅니다.</summary>
    /// <param name="row">대상 행입니다.</param>
    /// <param name="columns">컬럼 이름과 위치의 대응입니다.</param>
    /// <param name="name">꺼낼 컬럼 이름입니다.</param>
    /// <returns>컬럼이나 셀이 없으면 빈 문자열입니다.</returns>
    public static string Cell(List<string> row, Dictionary<string, int> columns, string name)
    {
        return columns.TryGetValue(name, out int i) && i < row.Count ? row[i] : string.Empty;
    }

    /// <summary>CSV 텍스트 전체를 행과 셀로 나눕니다.</summary>
    /// <param name="text">파일에서 읽은 원본 텍스트입니다.</param>
    /// <returns>행마다 셀 목록을 담은 결과입니다.</returns>
    /// <remarks>따옴표로 감싼 셀 안의 쉼표와 줄바꿈을 값으로 취급합니다.</remarks>
    public static List<List<string>> ParseCsv(string text)
    {
        List<List<string>> rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text))
            return rows;

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text.Substring(1);

        List<string> current = new List<string>();
        StringBuilder field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': current.Add(field.ToString()); field.Clear(); break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRow(rows, ref current, field);
                    break;
                case '\n':
                    EndRow(rows, ref current, field);
                    break;
                default: field.Append(c); break;
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            if (!IsEmpty(current)) rows.Add(current);
        }

        return rows;
    }

    private static void EndRow(List<List<string>> rows, ref List<string> current, StringBuilder field)
    {
        current.Add(field.ToString());
        field.Clear();
        if (!IsEmpty(current)) rows.Add(current);
        current = new List<string>();
    }

    private static bool IsEmpty(List<string> row)
    {
        foreach (string c in row)
            if (!string.IsNullOrWhiteSpace(c)) return false;
        return true;
    }
}

// ===================================================================== MiniJson

/// <summary>Minimal JSON serializer/parser for the tree types used by SoCsvTree.</summary>
internal static class MiniJson
{
    /// <summary>사전·목록·스칼라 구조를 JSON 문자열로 만듭니다.</summary>
    /// <param name="value">직렬화할 값입니다.</param>
    /// <returns>셀 한 칸에 넣을 JSON 문자열입니다.</returns>
    public static string Serialize(object value)
    {
        StringBuilder sb = new StringBuilder();
        Write(sb, value);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object v)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case string s: WriteString(sb, s); break;
            case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
            case IDictionary<string, object> dict:
                sb.Append('{');
                bool firstD = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!firstD) sb.Append(',');
                    firstD = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                break;
            case IEnumerable list:
                sb.Append('[');
                bool firstL = true;
                foreach (object item in list)
                {
                    if (!firstL) sb.Append(',');
                    firstL = false;
                    Write(sb, item);
                }
                sb.Append(']');
                break;
            default: WriteString(sb, v.ToString()); break;
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>JSON 문자열을 사전·목록·스칼라 구조로 되돌립니다.</summary>
    /// <param name="json">셀에 들어 있던 JSON 문자열입니다.</param>
    /// <returns>해석에 실패하면 <c>null</c>입니다.</returns>
    public static object Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        int index = 0;
        object result = ParseValue(json, ref index);
        return result;
    }

    private static object ParseValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length) return null;
        char c = s[i];
        switch (c)
        {
            case '{': return ParseObject(s, ref i);
            case '[': return ParseArray(s, ref i);
            case '"': return ParseString(s, ref i);
            case 't': i += 4; return true;
            case 'f': i += 5; return false;
            case 'n': i += 4; return null;
            default: return ParseNumber(s, ref i);
        }
    }

    private static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();
        i++; // {
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) break;
            if (s[i] == '}') { i++; break; }
            string key = ParseString(s, ref i);
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            object value = ParseValue(s, ref i);
            dict[key] = value;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; break; }
        }
        return dict;
    }

    private static List<object> ParseArray(string s, ref int i)
    {
        List<object> list = new List<object>();
        i++; // [
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) break;
            if (s[i] == ']') { i++; break; }
            list.Add(ParseValue(s, ref i));
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
        }
        return list;
    }

    private static string ParseString(string s, ref int i)
    {
        StringBuilder sb = new StringBuilder();
        i++; // opening quote
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') break;
            if (c == '\\' && i < s.Length)
            {
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object ParseNumber(string s, ref int i)
    {
        int start = i;
        bool isFloat = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '-' || c == '+' || (c >= '0' && c <= '9')) { i++; }
            else if (c == '.' || c == 'e' || c == 'E') { isFloat = true; i++; }
            else break;
        }
        string num = s.Substring(start, i - start);
        if (!isFloat && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            return l;
        double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
        return d;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
