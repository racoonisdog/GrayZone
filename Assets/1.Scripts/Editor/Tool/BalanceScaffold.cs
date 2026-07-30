using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="BalanceFieldAttribute"/>가 붙은 스크립트에서 같은 이름의 데이터 SO를 만들어내는 에디터 전용 도구입니다.
/// </summary>
/// <remarks>
/// 런타임 적용 방향은 엑셀 → SO → 스크립트 그대로이고, 이 도구는 최초 셋업에서만 역순으로 뼈대를 뽑습니다.
/// 손으로 26개 필드를 옮겨적다 생기는 오타를 없애는 것이 목적입니다.
/// 필드 이름은 스크립트와 <b>완전히 동일</b>하게 생성합니다. <see cref="BindManager"/>가 이름으로 짝을 찾기 때문입니다.
/// </remarks>
internal static class BalanceScaffold
{
    /// <summary>생성할 SO 클래스 이름의 접미사입니다.</summary>
    public const string SoSuffix = "SO";

    /// <summary>스크립트 타입에 대응하는 SO 클래스 이름을 만듭니다.</summary>
    /// <param name="scriptType">밸런스 필드를 선언한 스크립트 타입입니다.</param>
    /// <returns>"{스크립트명}SO" 형식의 클래스 이름입니다.</returns>
    public static string GetSoTypeName(Type scriptType)
    {
        return scriptType.Name + SoSuffix;
    }

    /// <summary>지정한 이름의 ScriptableObject 타입이 컴파일되어 있으면 반환합니다.</summary>
    /// <param name="typeName">찾을 클래스 이름입니다. 규약과 다른 이름도 허용합니다.</param>
    /// <returns>찾지 못하면 <c>null</c>입니다.</returns>
    public static Type FindSoTypeByName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        foreach (Type candidate in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
        {
            if (!candidate.IsAbstract && string.Equals(candidate.Name, typeName, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>C# 클래스 이름으로 쓸 수 있는 문자열인지 확인합니다.</summary>
    /// <param name="name">검사할 이름입니다.</param>
    /// <returns>문자 또는 밑줄로 시작하고 영숫자·밑줄로만 이루어지면 true입니다.</returns>
    public static bool IsValidTypeName(string name)
    {
        if (string.IsNullOrEmpty(name) || (!char.IsLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>스크립트에서 [BalanceField]가 붙은 인스턴스 필드를 선언 순서대로 모읍니다.</summary>
    /// <param name="scriptType">검색할 스크립트 타입입니다.</param>
    /// <returns>기반 타입의 필드까지 포함한 밸런스 필드 목록입니다.</returns>
    public static List<FieldInfo> CollectBalanceFields(Type scriptType)
    {
        List<FieldInfo> fields = new List<FieldInfo>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        // 파생 타입부터 훑되, 이름이 겹치면 파생 쪽을 우선합니다.
        for (Type current = scriptType; current != null && current != typeof(object); current = current.BaseType)
        {
            FieldInfo[] declared = current.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in declared)
            {
                if (field.GetCustomAttribute<BalanceFieldAttribute>(true) == null)
                {
                    continue;
                }

                if (seen.Add(field.Name))
                {
                    fields.Add(field);
                }
            }
        }

        return fields;
    }

    /// <summary>여러 스크립트의 밸런스 필드를 모아 SO 클래스 소스를 생성하거나 갱신합니다.</summary>
    /// <param name="scriptTypes">밸런스 필드를 선언한 스크립트들입니다. 선언 순서대로 블록이 나뉩니다.</param>
    /// <param name="soTypeName">만들 SO 클래스 이름입니다. 비우면 첫 스크립트 기준 "{스크립트명}SO"를 씁니다.</param>
    /// <param name="seedPrefab">필드 초기값을 가져올 프리팹입니다. 없으면 초기값을 적지 않습니다.</param>
    /// <param name="fallbackTarget">기존 파일이 없을 때 쓸 경로입니다. 폴더만 주면 파일명을 붙입니다.</param>
    /// <param name="assetPath">생성한 파일의 프로젝트 경로입니다.</param>
    /// <param name="message">사용자에게 보여줄 결과 설명입니다.</param>
    /// <returns>파일을 쓰면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 밸런스 SO는 스크립트가 아니라 <b>기획자가 튜닝하는 엔티티</b> 단위입니다. 적 한 종이 여러 컴포넌트로 나뉘어도
    /// SO는 하나여야 시트도 한 장이 됩니다. 대입은 컴포넌트마다 Bind를 한 번씩 부르면 각자 자기 필드만 가져갑니다.
    /// </remarks>
    public static bool GenerateSoScript(
        IReadOnlyList<Type> scriptTypes,
        string soTypeName,
        GameObject seedPrefab,
        string fallbackTarget,
        out string assetPath,
        out string message)
    {
        assetPath = null;

        if (scriptTypes == null || scriptTypes.Count == 0)
        {
            message = "대상 스크립트를 하나 이상 넣어주세요.";
            return false;
        }

        List<FieldBlock> blocks = CollectBlocks(scriptTypes, out List<string> duplicates);
        int total = 0;
        foreach (FieldBlock block in blocks)
        {
            total += block.Fields.Count;
        }

        if (total == 0)
        {
            message = "선택한 스크립트에 [BalanceField] 필드가 없습니다.";
            return false;
        }

        // 이름이 겹치면 SO 필드가 하나로 합쳐져 두 컴포넌트가 같은 값을 받습니다.
        string soName = string.IsNullOrWhiteSpace(soTypeName)
            ? GetSoTypeName(scriptTypes[0])
            : soTypeName.Trim();
        if (!IsValidTypeName(soName))
        {
            message = $"'{soName}'은(는) 클래스 이름으로 쓸 수 없습니다. 문자로 시작하고 영숫자·밑줄만 사용하세요.";
            return false;
        }

        // 이미 같은 이름의 소스가 있으면 그 자리를 유지합니다. 재생성이 파일을 옮기지 않도록 합니다.
        string existing = FindExistingScriptPath(soName);
        assetPath = existing ?? NormalizeTargetPath(fallbackTarget, soName);

        string source = BuildSoSource(soName, blocks, seedPrefab);

        string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(directory))
        {
            EnsureFolder(directory);
        }

        // 프로젝트 다른 스크립트와 같은 UTF-8(BOM) + \r\n로 저장합니다.
        File.WriteAllText(assetPath, source, new UTF8Encoding(true));
        AssetDatabase.ImportAsset(assetPath);

        string where = existing != null ? "갱신" : "생성";
        string seeded = seedPrefab != null ? $"'{seedPrefab.name}' 프리팹 값으로 초기값 지정" : "초기값 없음";

        // 이름이 겹치면 SO 필드는 하나만 만들어지고 두 컴포넌트가 같은 값을 받습니다.
        // 의도한 공유일 수 있어 막지 않고 목록만 알립니다.
        string dup = duplicates.Count > 0
            ? $"\n\n[주의] 이름이 겹친 필드 {duplicates.Count}개는 한 번만 만들었습니다. 두 컴포넌트가 같은 값을 받게 됩니다:\n  " +
              string.Join("\n  ", duplicates)
            : string.Empty;

        message =
            $"{soName} 클래스를 {where}했습니다 (스크립트 {blocks.Count}개 / 필드 {total}개, {seeded}).\n{assetPath}{dup}\n\n" +
            "Unity가 컴파일을 마친 뒤 '.asset 생성'을 눌러주세요.";
        return true;
    }

    /// <summary>스크립트별로 밸런스 필드를 모으고 이름이 겹치는 것을 걸러냅니다.</summary>
    /// <param name="scriptTypes">검색할 스크립트 타입들입니다.</param>
    /// <param name="duplicates">겹쳐서 제외된 필드 설명입니다.</param>
    /// <returns>스크립트 순서대로의 필드 블록입니다.</returns>
    /// <remarks>
    /// 이름이 겹치면 SO에는 하나만 만들고, 대입 때 두 컴포넌트가 같은 값을 받습니다.
    /// 의도한 공유일 수 있어 오류로 막지 않고 목록만 알립니다.
    /// </remarks>
    public static List<FieldBlock> CollectBlocks(IReadOnlyList<Type> scriptTypes, out List<string> duplicates)
    {
        List<FieldBlock> blocks = new List<FieldBlock>();
        duplicates = new List<string>();
        Dictionary<string, Type> seen = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (Type scriptType in scriptTypes)
        {
            if (scriptType == null)
            {
                continue;
            }

            List<FieldInfo> kept = new List<FieldInfo>();
            foreach (FieldInfo field in CollectBalanceFields(scriptType))
            {
                if (seen.TryGetValue(field.Name, out Type owner))
                {
                    duplicates.Add($"{field.Name} ({owner.Name} / {scriptType.Name})");
                    continue;
                }

                seen.Add(field.Name, scriptType);
                kept.Add(field);
            }

            if (kept.Count > 0)
            {
                blocks.Add(new FieldBlock(scriptType, kept));
            }
        }

        return blocks;
    }

    /// <summary>한 스크립트에서 모은 밸런스 필드 묶음입니다.</summary>
    public sealed class FieldBlock
    {
        /// <summary>필드를 선언한 스크립트와 그 필드 목록을 묶습니다.</summary>
        /// <param name="scriptType">필드를 선언한 스크립트 타입입니다.</param>
        /// <param name="fields">그 스크립트의 밸런스 필드입니다.</param>
        public FieldBlock(Type scriptType, List<FieldInfo> fields)
        {
            ScriptType = scriptType;
            Fields = fields;
        }

        public Type ScriptType { get; }
        public List<FieldInfo> Fields { get; }
    }

    /// <summary>생성된 SO 타입의 에셋을 만들고 프리팹 값으로 채웁니다.</summary>
    /// <param name="scriptTypes">값을 가져올 컴포넌트들의 타입입니다.</param>
    /// <param name="soType">이미 컴파일된 SO 타입입니다.</param>
    /// <param name="assetBaseName">만들 .asset 파일 이름입니다. 비우면 프리팹 이름에서 만듭니다.</param>
    /// <param name="seedPrefab">값을 가져올 프리팹입니다. 없으면 기본값으로 만듭니다.</param>
    /// <param name="assetFolder">에셋을 만들 폴더입니다.</param>
    /// <param name="assetPath">생성한 에셋 경로입니다.</param>
    /// <param name="message">사용자에게 보여줄 결과 설명입니다.</param>
    /// <returns>에셋을 만들면 <c>true</c>입니다.</returns>
    public static bool CreateSoAsset(
        IReadOnlyList<Type> scriptTypes,
        Type soType,
        string assetBaseName,
        GameObject seedPrefab,
        string assetFolder,
        out string assetPath,
        out string message)
    {
        assetPath = null;

        EnsureFolder(assetFolder);
        string baseName = !string.IsNullOrWhiteSpace(assetBaseName)
            ? Sanitize(assetBaseName)
            : BuildAssetName(seedPrefab != null ? seedPrefab.name : null, soType.Name);

        ScriptableObject asset = ScriptableObject.CreateInstance(soType);
        int copied = 0;
        int identity = 0;

        SerializedObject target = new SerializedObject(asset);

        // 컴포넌트마다 자기 필드만 복사합니다. 프리팹에 그 컴포넌트가 없으면 조용히 건너뜁니다.
        List<FieldBlock> blocks = CollectBlocks(scriptTypes, out _);
        foreach (FieldBlock block in blocks)
        {
            SerializedObject seed = CreateSeed(block.ScriptType, seedPrefab);
            if (seed == null)
            {
                continue;
            }

            foreach (FieldInfo field in block.Fields)
            {
                SerializedProperty from = seed.FindProperty(field.Name);
                SerializedProperty to = target.FindProperty(field.Name);
                if (from == null || to == null || from.propertyType != to.propertyType)
                {
                    continue;
                }

                if (CopyValue(from, to))
                {
                    copied++;
                }
            }
        }

        if (seedPrefab != null)
        {
            identity = FillIdentity(target, seedPrefab.name);
        }

        target.ApplyModifiedPropertiesWithoutUndo();

        assetPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder.TrimEnd('/')}/{baseName}.asset");
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.SaveAssets();
        Undo.RegisterCreatedObjectUndo(asset, "Create Balance SO Asset");

        message =
            $"{soType.Name} 에셋을 만들었습니다.\n{assetPath}\n\n" +
            (seedPrefab != null
                ? $"프리팹 '{seedPrefab.name}'에서 값 {copied}개, 식별 필드 {identity}개를 채웠습니다."
                : "프리팹을 지정하지 않아 기본값으로 만들었습니다.");
        return true;
    }

    // ---------------------------------------------------------------- 내부 구현

    /// <summary>프리팹에서 대상 컴포넌트를 찾아 값 읽기용 SerializedObject를 만듭니다.</summary>
    /// <param name="scriptType">찾을 컴포넌트 타입입니다.</param>
    /// <param name="prefab">검색할 프리팹입니다.</param>
    /// <returns>프리팹이나 컴포넌트가 없으면 <c>null</c>입니다.</returns>
    private static SerializedObject CreateSeed(Type scriptType, GameObject prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        Component component = prefab.GetComponent(scriptType);
        return component != null ? new SerializedObject(component) : null;
    }

    /// <summary>SO 클래스 소스 텍스트를 조립합니다. 스크립트별로 구분선 주석을 넣습니다.</summary>
    /// <param name="soName">만들 클래스 이름입니다.</param>
    /// <param name="blocks">스크립트별 필드 묶음입니다.</param>
    /// <param name="seedPrefab">초기값을 가져올 프리팹입니다.</param>
    /// <returns>파일에 쓸 소스 전체입니다.</returns>
    private static string BuildSoSource(string soName, List<FieldBlock> blocks, GameObject seedPrefab)
    {
        StringBuilder sb = new StringBuilder();
        List<string> sourceNames = new List<string>();
        foreach (FieldBlock block in blocks)
        {
            sourceNames.Add(block.ScriptType.Name);
        }

        sb.Append("using UnityEngine;\r\n\r\n");
        sb.Append("/// <summary>\r\n");
        sb.Append("/// " + string.Join(", ", sourceNames) + "에 주입할 순수 수치 밸런스 데이터를 보관합니다.\r\n");
        sb.Append("/// </summary>\r\n");
        sb.Append("/// <remarks>\r\n");
        sb.Append("/// 이 파일은 SO CSV 도구가 위 스크립트들의 [BalanceField] 필드에서 생성했습니다.\r\n");
        sb.Append("/// 필드 이름은 스크립트와 완전히 동일해야 합니다. BindManager가 이름으로 짝을 찾기 때문입니다.\r\n");
        sb.Append("/// 값의 정본은 엑셀 시트이며 CSV 임포터가 채웁니다. 허용 범위는 스크립트 쪽 [BalanceField]에 선언됩니다.\r\n");
        sb.Append("/// 미디어·런타임 참조는 담지 않습니다. 그런 참조는 Inspector에서 직접 배선합니다.\r\n");
        sb.Append("/// </remarks>\r\n");
        sb.Append($"[CreateAssetMenu(fileName = \"{soName}\", menuName = \"GrayZone/Balance/{soName}\")]\r\n");
        sb.Append($"public sealed class {soName} : ScriptableObject, IBalanceTableData\r\n");
        sb.Append("{\r\n");

        for (int b = 0; b < blocks.Count; b++)
        {
            FieldBlock block = blocks[b];
            if (b > 0)
            {
                sb.Append("\r\n");
            }

            sb.Append($"    // ───────────── {block.ScriptType.Name} ─────────────\r\n");

            SerializedObject seed = CreateSeed(block.ScriptType, seedPrefab);
            foreach (FieldInfo field in block.Fields)
            {
                sb.Append("\r\n");

                string tooltipText = BuildTooltipText(field);
                if (!string.IsNullOrWhiteSpace(tooltipText))
                {
                    sb.Append($"    [Tooltip(\"{EscapeLiteral(tooltipText)}\")]\r\n");
                }

                string initializer = BuildInitializer(field, seed);
                sb.Append($"    [SerializeField] private {TypeRef(field.FieldType)} {field.Name}{initializer};\r\n");
            }
        }

        sb.Append("}\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// SO 필드에 붙일 설명을 만듭니다. 원본 설명 뒤에 허용 범위를 덧붙입니다.
    /// </summary>
    /// <param name="field">설명을 가져올 원본 스크립트 필드입니다.</param>
    /// <returns>설명이 없고 범위도 없으면 빈 문자열입니다.</returns>
    /// <remarks>
    /// 범위는 SO에 <see cref="ClampAttribute"/>로 복사하지 않고 문구로만 싣습니다.
    /// SO와 시트는 값을 담는 그릇이고, 자르는 것은 게임플레이 직전 Bind 한 곳에서만 해야 하기 때문입니다.
    /// 시트에 범위 밖 값이 들어가도 그대로 저장되지만 Bind가 잘라내므로, 기획자는 문구로 한계를 알고 값을 넣습니다.
    /// 범위 자체를 바꾸려면 코드의 선언을 고쳐야 합니다.
    /// </remarks>
    private static string BuildTooltipText(FieldInfo field)
    {
        TooltipAttribute tooltip = field.GetCustomAttribute<TooltipAttribute>(true);
        string description = tooltip != null && !string.IsNullOrWhiteSpace(tooltip.tooltip)
            ? tooltip.tooltip.Trim()
            : string.Empty;

        ClampAttribute clamp = field.GetCustomAttribute<ClampAttribute>(true);
        string range = clamp != null ? clamp.DescribeRange() : string.Empty;

        if (string.IsNullOrEmpty(range))
        {
            return description;
        }

        return string.IsNullOrEmpty(description) ? $"({range})" : $"{description} ({range})";
    }

    /// <summary>프리팹 값이 있으면 " = 리터럴" 형태의 초기화식을 만듭니다.</summary>
    private static string BuildInitializer(FieldInfo field, SerializedObject seed)
    {
        SerializedProperty p = seed?.FindProperty(field.Name);
        if (p == null)
        {
            return string.Empty;
        }

        string literal = ToLiteral(p, field.FieldType);
        return string.IsNullOrEmpty(literal) ? string.Empty : $" = {literal}";
    }

    /// <summary>SerializedProperty 값을 C# 리터럴로 바꿉니다. 지원하지 않는 타입은 빈 문자열입니다.</summary>
    private static string ToLiteral(SerializedProperty p, Type fieldType)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer:
                return p.longValue.ToString(CultureInfo.InvariantCulture);

            case SerializedPropertyType.Boolean:
                return p.boolValue ? "true" : "false";

            case SerializedPropertyType.Float:
                {
                    // float 필드는 float 정밀도로 찍습니다. doubleValue를 그대로 쓰면
                    // 5.335f 가 5.3350000381469727f 처럼 넓혀진 값으로 생성됩니다.
                    if (fieldType == typeof(float))
                    {
                        return ((float)p.doubleValue).ToString("R", CultureInfo.InvariantCulture) + "f";
                    }

                    return p.doubleValue.ToString("R", CultureInfo.InvariantCulture);
                }

            case SerializedPropertyType.String:
                return $"\"{EscapeLiteral(p.stringValue ?? string.Empty)}\"";

            case SerializedPropertyType.Enum:
                {
                    if (p.enumValueIndex < 0 || p.enumValueIndex >= p.enumNames.Length)
                    {
                        return string.Empty;
                    }

                    return $"{TypeRef(fieldType)}.{p.enumNames[p.enumValueIndex]}";
                }

            default:
                return string.Empty;
        }
    }

    /// <summary>SerializedProperty 값을 같은 종류의 다른 SerializedProperty로 복사합니다.</summary>
    private static bool CopyValue(SerializedProperty from, SerializedProperty to)
    {
        switch (from.propertyType)
        {
            case SerializedPropertyType.Integer: to.longValue = from.longValue; return true;
            case SerializedPropertyType.Boolean: to.boolValue = from.boolValue; return true;
            case SerializedPropertyType.Float: to.doubleValue = from.doubleValue; return true;
            case SerializedPropertyType.String: to.stringValue = from.stringValue; return true;
            case SerializedPropertyType.Enum: to.enumValueIndex = from.enumValueIndex; return true;
            default: return false;
        }
    }

    /// <summary>SO에만 있는 시트 식별 필드를 프리팹 이름 기준으로 채웁니다.</summary>
    /// <param name="target">채울 SO의 SerializedObject입니다.</param>
    /// <param name="prefabName">이름과 ID의 근거가 되는 프리팹 이름입니다.</param>
    /// <returns>채운 필드 수입니다.</returns>
    /// <remarks>식별 필드는 스크립트에 없어 생성 대상이 아니므로, 존재할 때만 값을 넣습니다.</remarks>
    private static int FillIdentity(SerializedObject target, string prefabName)
    {
        int filled = 0;

        SerializedProperty name = target.FindProperty("m_weaponName");
        if (name != null && name.propertyType == SerializedPropertyType.String)
        {
            name.stringValue = prefabName;
            filled++;
        }

        SerializedProperty id = target.FindProperty("m_weaponId");
        if (id != null && id.propertyType == SerializedPropertyType.String)
        {
            id.stringValue = Sanitize(prefabName).ToLowerInvariant();
            filled++;
        }

        return filled;
    }

    /// <summary>중첩 타입까지 올바르게 참조되는 타입 표기를 만듭니다.</summary>
    private static string TypeRef(Type type)
    {
        if (type == typeof(int)) return "int";
        if (type == typeof(float)) return "float";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        if (type == typeof(double)) return "double";
        if (type == typeof(long)) return "long";
        if (type == typeof(short)) return "short";
        if (type == typeof(byte)) return "byte";

        if (type.IsNested && type.DeclaringType != null)
        {
            return $"{TypeRef(type.DeclaringType)}.{type.Name}";
        }

        return type.Name;
    }

    /// <summary>문자열 리터럴에 넣을 수 있도록 따옴표와 역슬래시를 이스케이프합니다.</summary>
    private static string EscapeLiteral(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", string.Empty)
            .Replace("\n", " ");
    }

    /// <summary>파일 이름과 ID에 쓸 수 있도록 공백과 특수문자를 제거합니다.</summary>
    /// <param name="value">정리할 문자열입니다.</param>
    /// <returns>영숫자와 밑줄만 남긴 문자열입니다. 남는 것이 없으면 "Balance"입니다.</returns>
    /// <remarks>밑줄은 이름 규칙의 구분자로 쓰므로 유지합니다.</remarks>
    private static string Sanitize(string value)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
        }

        return sb.Length > 0 ? sb.ToString() : "Balance";
    }

    /// <summary>프리팹 이름과 SO 클래스 이름으로 .asset 파일 이름을 만듭니다.</summary>
    /// <param name="prefabName">값을 가져온 프리팹 이름입니다. 비우면 클래스 이름만 씁니다.</param>
    /// <param name="soTypeName">SO 클래스 이름입니다.</param>
    /// <returns>"프리팹이름_클래스이름" 형식의 파일 이름입니다.</returns>
    /// <remarks>창의 미리보기와 실제 생성이 어긋나지 않도록 규칙을 이 한 곳에 둡니다.</remarks>
    public static string BuildAssetName(string prefabName, string soTypeName)
    {
        string suffix = (soTypeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return string.IsNullOrEmpty(suffix) ? "Balance" : suffix;
        }

        string prefix = Sanitize(prefabName);
        return string.IsNullOrEmpty(suffix) ? prefix : $"{prefix}_{suffix}";
    }

    /// <summary>폴더만 주어져도 파일 경로가 되도록 보정합니다.</summary>
    /// <param name="target">폴더 또는 .cs 파일 경로입니다.</param>
    /// <param name="soName">붙일 클래스 이름입니다.</param>
    /// <returns>.cs로 끝나는 프로젝트 경로입니다.</returns>
    private static string NormalizeTargetPath(string target, string soName)
    {
        string cleaned = (target ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = "Assets";
        }

        return cleaned.EndsWith(".cs", StringComparison.Ordinal) ? cleaned : $"{cleaned}/{soName}.cs";
    }

    /// <summary>프로젝트에서 지정한 이름의 스크립트 파일 경로를 찾습니다.</summary>
    /// <param name="typeName">찾을 클래스 이름입니다.</param>
    /// <returns>없으면 <c>null</c>입니다.</returns>
    public static string FindExistingScriptPath(string typeName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{typeName} t:MonoScript"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), typeName, StringComparison.Ordinal))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>"Assets/..." 폴더가 없으면 상위 폴더까지 만듭니다.</summary>
    private static void EnsureFolder(string assetFolder)
    {
        assetFolder = (assetFolder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(assetFolder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
        {
            return;
        }

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
