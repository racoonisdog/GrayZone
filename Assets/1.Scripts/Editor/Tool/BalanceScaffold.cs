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

    /// <summary>종류에 맞는 SO 클래스 이름 기본값을 만듭니다.</summary>
    /// <param name="scriptType">필드를 선언한 스크립트 타입입니다.</param>
    /// <param name="kind">생성할 SO 종류입니다.</param>
    /// <returns>기본 클래스 이름입니다.</returns>
    /// <remarks>
    /// 피드백은 재생을 담당하는 이미터에 필드가 붙으므로 이름을 그대로 쓰면 <c>WeaponFeedbackEmitterSO</c>가 됩니다.
    /// 데이터의 이름에 "누가 재생하는지"가 들어갈 이유가 없어 <c>Emitter</c>를 떼어 <c>WeaponFeedbackSO</c>로 만듭니다.
    /// </remarks>
    public static string GetSoTypeName(Type scriptType, SoKind kind)
    {
        string name = scriptType.Name;

        if (kind == SoKind.Feedback && name.EndsWith("Emitter", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Emitter".Length);
        }

        return name.EndsWith(SoSuffix, StringComparison.Ordinal) ? name : name + SoSuffix;
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

    /// <summary>
    /// 생성할 SO의 종류입니다. 밸런스와 피드백은 같은 생성 절차를 쓰고 결과물의 계약만 다릅니다.
    /// </summary>
    /// <remarks>
    /// 생성기는 "표시된 필드를 모아 SO를 뱉는" 범용 도구입니다. 종류마다 다른 것은
    /// 어떤 어트리뷰트를 찾는지, 어떤 인터페이스를 구현하는지, 참조 용도 표시를 함께 다는지뿐입니다.
    /// </remarks>
    public enum SoKind
    {
        /// <summary>수치를 담는 밸런스 SO입니다. <see cref="IBalanceTableData"/>를 구현하고 CSV로 나갑니다.</summary>
        Balance,

        /// <summary>표현 리소스를 담는 피드백 SO입니다. <see cref="IFeedbackData"/>를 구현하고 CSV에서 제외됩니다.</summary>
        Feedback
    }

    /// <summary>종류에 대응하는 표시 어트리뷰트 타입을 돌려줍니다.</summary>
    /// <param name="kind">생성할 SO 종류입니다.</param>
    public static Type GetMarkerAttributeType(SoKind kind)
    {
        return kind == SoKind.Feedback ? typeof(FeedbackFieldAttribute) : typeof(BalanceFieldAttribute);
    }

    /// <summary>스크립트에서 지정한 표시 어트리뷰트가 붙은 인스턴스 필드를 선언 순서대로 모읍니다.</summary>
    /// <param name="scriptType">검색할 스크립트 타입입니다.</param>
    /// <param name="kind">찾을 필드의 종류입니다.</param>
    /// <returns>기반 타입의 필드까지 포함한 목록입니다.</returns>
    public static List<FieldInfo> CollectFields(Type scriptType, SoKind kind)
    {
        Type marker = GetMarkerAttributeType(kind);
        List<FieldInfo> fields = new List<FieldInfo>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        // 파생 타입부터 훑되, 이름이 겹치면 파생 쪽을 우선합니다.
        for (Type current = scriptType; current != null && current != typeof(object); current = current.BaseType)
        {
            FieldInfo[] declared = current.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in declared)
            {
                if (field.GetCustomAttributes(marker, true).Length == 0)
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

    /// <summary>스크립트에서 <c>[BalanceField]</c>가 붙은 인스턴스 필드를 모읍니다.</summary>
    /// <param name="scriptType">검색할 스크립트 타입입니다.</param>
    /// <returns>밸런스 필드 목록입니다.</returns>
    public static List<FieldInfo> CollectBalanceFields(Type scriptType)
    {
        return CollectFields(scriptType, SoKind.Balance);
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
        return GenerateSoScript(scriptTypes, soTypeName, seedPrefab, fallbackTarget, SoKind.Balance, out assetPath, out message);
    }

    /// <summary>여러 스크립트의 표시된 필드를 모아 SO 클래스 소스를 생성하거나 갱신합니다.</summary>
    /// <param name="scriptTypes">필드를 선언한 스크립트들입니다. 선언 순서대로 블록이 나뉩니다.</param>
    /// <param name="soTypeName">만들 SO 클래스 이름입니다. 비우면 첫 스크립트 기준 이름을 씁니다.</param>
    /// <param name="seedPrefab">필드 초기값을 가져올 프리팹입니다.</param>
    /// <param name="fallbackTarget">기존 파일이 없을 때 쓸 경로입니다.</param>
    /// <param name="kind">생성할 SO 종류입니다.</param>
    /// <param name="assetPath">생성한 파일의 프로젝트 경로입니다.</param>
    /// <param name="message">사용자에게 보여줄 결과 설명입니다.</param>
    /// <returns>파일을 쓰면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 밸런스와 피드백이 같은 본문을 씁니다. 생성기는 표시된 필드를 모아 SO를 뱉는 범용 도구이고,
    /// 종류가 정하는 것은 찾을 어트리뷰트·구현할 인터페이스·참조 용도 표시 방출 여부뿐입니다.
    /// </remarks>
    public static bool GenerateSoScript(
        IReadOnlyList<Type> scriptTypes,
        string soTypeName,
        GameObject seedPrefab,
        string fallbackTarget,
        SoKind kind,
        out string assetPath,
        out string message)
    {
        assetPath = null;

        if (scriptTypes == null || scriptTypes.Count == 0)
        {
            message = "대상 스크립트를 하나 이상 넣어주세요.";
            return false;
        }

        List<FieldBlock> blocks = CollectBlocks(scriptTypes, kind, out List<string> duplicates);
        int total = 0;
        foreach (FieldBlock block in blocks)
        {
            total += block.Fields.Count;
        }

        if (total == 0)
        {
            message = $"선택한 스크립트에 [{GetMarkerAttributeType(kind).Name.Replace("Attribute", string.Empty)}] 필드가 없습니다.";
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

        // 재생성이 SO 전용 필드(시트 식별자 등)를 지우지 않도록, 이미 컴파일된 클래스에서 그것들을 건져 옵니다.
        List<PreservedField> preserved = CollectPreservedFields(FindSoTypeByName(soName), blocks);
        string source = BuildSoSource(soName, blocks, seedPrefab, preserved, kind);

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

        string kept = preserved.Count > 0
            ? $"\n\n[보존] 스크립트에 대응이 없는 SO 전용 필드 {preserved.Count}개를 그대로 유지했습니다:\n  " +
              string.Join("\n  ", preserved.ConvertAll(entry => entry.Name))
            : string.Empty;

        message =
            $"{soName} 클래스를 {where}했습니다 (스크립트 {blocks.Count}개 / 필드 {total}개, {seeded}).\n{assetPath}{dup}{kept}\n\n" +
            "Unity가 컴파일을 마친 뒤 '.asset 생성'을 눌러주세요.";
        return true;
    }

    /// <summary>
    /// 이미 컴파일된 SO 클래스에서, 이번 생성 결과에 포함되지 않는 직렬화 필드를 건져 옵니다.
    /// </summary>
    /// <param name="soType">기존 SO 타입입니다. 처음 생성이면 <c>null</c>입니다.</param>
    /// <param name="blocks">이번에 스크립트에서 만들어 낼 필드 묶음입니다.</param>
    /// <returns>재생성 후에도 유지해야 하는 필드 목록입니다.</returns>
    /// <remarks>
    /// <b>이것이 없으면 재생성이 데이터를 지웁니다.</b> 생성기는 <c>[BalanceField]</c> 필드만 방출하는데,
    /// SO에는 스크립트에 대응이 없는 필드가 있습니다. 시트에서 어느 열이 무엇인지 알려 주는 식별자
    /// (<c>m_weaponId</c>·<c>m_weaponName</c>·<c>m_weaponType</c> 등)가 그렇습니다.
    /// 그대로 재생성하면 클래스에서 사라지고 에셋에 들어 있던 값도 함께 날아갑니다.
    /// <para>
    /// 기본값은 임시 인스턴스를 만들어 읽습니다. 소스의 초기화식을 다시 파싱하는 것보다 정확하고,
    /// C# 텍스트를 정규식으로 뜯을 필요도 없습니다. 리터럴로 적을 수 없는 타입(중첩 구조 등)은
    /// 초기화식 없이 선언만 유지합니다. 값은 어차피 에셋에 직렬화되어 있습니다.
    /// </para>
    /// </remarks>
    private static List<PreservedField> CollectPreservedFields(Type soType, List<FieldBlock> blocks)
    {
        List<PreservedField> preserved = new List<PreservedField>();
        if (soType == null)
        {
            return preserved;
        }

        HashSet<string> generated = new HashSet<string>(StringComparer.Ordinal);
        foreach (FieldBlock block in blocks)
        {
            foreach (FieldInfo field in block.Fields)
            {
                generated.Add(GetSoFieldName(block.ScriptType, field));

                // 접두사가 붙기 전의 이름도 "생성 대상"으로 봅니다. 그 이름은 새 필드의
                // [FormerlySerializedAs] 대상이므로, 보존해서 실제 필드로 남기면 이름이 충돌해
                // 값이 옮겨오지 못하고 같은 값이 두 벌로 갈라집니다.
                generated.Add(field.Name);
            }
        }

        // 기본값은 인스턴스에서 읽습니다. 소스를 다시 파싱하지 않기 위해서입니다.
        ScriptableObject probe = ScriptableObject.CreateInstance(soType);
        try
        {
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type current = soType; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(flags))
                {
                    if (field.IsStatic || field.IsNotSerialized || generated.Contains(field.Name))
                    {
                        continue;
                    }

                    bool serialized = field.IsPublic || field.GetCustomAttribute<SerializeField>(true) != null;
                    if (!serialized)
                    {
                        continue;
                    }

                    TooltipAttribute tooltip = field.GetCustomAttribute<TooltipAttribute>(true);
                    preserved.Add(new PreservedField(
                        field.Name,
                        field.FieldType,
                        tooltip != null ? tooltip.tooltip : null,
                        ToLiteral(field.GetValue(probe), field.FieldType)));
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }

        return preserved;
    }

    /// <summary>재생성 후에도 유지할 SO 전용 필드 하나입니다.</summary>
    private sealed class PreservedField
    {
        /// <summary>이름·타입·설명·초기값을 묶습니다.</summary>
        /// <param name="name">필드 이름입니다. 직렬화 키이므로 그대로 유지해야 합니다.</param>
        /// <param name="fieldType">필드 타입입니다.</param>
        /// <param name="tooltip">기존 설명입니다. 없으면 <c>null</c>입니다.</param>
        /// <param name="literal">기본값 리터럴입니다. 적을 수 없는 타입이면 빈 문자열입니다.</param>
        public PreservedField(string name, Type fieldType, string tooltip, string literal)
        {
            Name = name;
            FieldType = fieldType;
            Tooltip = tooltip;
            Literal = literal;
        }

        public string Name { get; }
        public Type FieldType { get; }
        public string Tooltip { get; }
        public string Literal { get; }
    }

    /// <summary>필드가 여러 컴포넌트가 함께 받는 공유 필드로 선언되었는지 확인합니다.</summary>
    /// <param name="field">검사할 스크립트 필드입니다.</param>
    /// <returns><c>[BalanceField(Shared = true)]</c>로 선언되었으면 <c>true</c>입니다.</returns>
    public static bool IsShared(FieldInfo field)
    {
        BindFieldAttribute attribute = field?.GetCustomAttribute<BindFieldAttribute>(true);
        return attribute != null && attribute.Shared;
    }

    /// <summary>스크립트 필드에 대응하는 SO 쪽 필드 이름을 만듭니다.</summary>
    /// <param name="scriptType">필드를 선언한 스크립트 타입입니다.</param>
    /// <param name="field">대상 스크립트 필드입니다.</param>
    /// <returns>전용이면 접두사가 붙은 이름, 공유면 원래 이름입니다.</returns>
    /// <remarks>
    /// 이름 규칙은 <see cref="BindManager.EnumerateSourceFieldNames"/>가 소유합니다.
    /// 그 첫 후보가 곧 생성할 이름이므로 여기서 규칙을 다시 적지 않습니다.
    /// </remarks>
    public static string GetSoFieldName(Type scriptType, FieldInfo field)
    {
        foreach (string candidate in BindManager.EnumerateSourceFieldNames(scriptType, field.Name, IsShared(field)))
        {
            return candidate;
        }

        return field.Name;
    }

    /// <summary>스크립트별로 밸런스 필드를 모으고 SO 이름이 겹치는 것을 걸러냅니다.</summary>
    /// <param name="scriptTypes">검색할 스크립트 타입들입니다.</param>
    /// <param name="duplicates">겹쳐서 제외된 필드 설명입니다.</param>
    /// <returns>스크립트 순서대로의 필드 블록입니다.</returns>
    /// <remarks>
    /// 전용 필드는 스크립트 이름이 접두사로 붙어 구조적으로 겹치지 않습니다.
    /// 그래서 여기 걸리는 것은 <c>Shared = true</c>로 선언한 공유 필드이거나,
    /// 이름이 같은 서로 다른 스크립트 타입처럼 예상 밖의 경우입니다.
    /// <para>
    /// 양쪽 모두 공유로 선언했다면 의도한 합치기이므로 조용히 하나만 만듭니다.
    /// 한쪽만 공유로 선언한 경우는 선언이 어긋난 것이라 목록으로 알립니다.
    /// </para>
    /// </remarks>
    public static List<FieldBlock> CollectBlocks(IReadOnlyList<Type> scriptTypes, out List<string> duplicates)
    {
        return CollectBlocks(scriptTypes, SoKind.Balance, out duplicates);
    }

    /// <summary>스크립트별로 지정한 종류의 필드를 모으고 SO 이름이 겹치는 것을 걸러냅니다.</summary>
    /// <param name="scriptTypes">검색할 스크립트 타입들입니다.</param>
    /// <param name="kind">모을 필드의 종류입니다.</param>
    /// <param name="duplicates">겹쳐서 제외된 필드 설명입니다.</param>
    /// <returns>스크립트 순서대로의 필드 블록입니다.</returns>
    public static List<FieldBlock> CollectBlocks(
        IReadOnlyList<Type> scriptTypes,
        SoKind kind,
        out List<string> duplicates)
    {
        List<FieldBlock> blocks = new List<FieldBlock>();
        duplicates = new List<string>();
        Dictionary<string, SeenField> seen = new Dictionary<string, SeenField>(StringComparer.Ordinal);

        foreach (Type scriptType in scriptTypes)
        {
            if (scriptType == null)
            {
                continue;
            }

            List<FieldInfo> kept = new List<FieldInfo>();
            foreach (FieldInfo field in CollectFields(scriptType, kind))
            {
                bool shared = IsShared(field);
                string soFieldName = GetSoFieldName(scriptType, field);

                if (seen.TryGetValue(soFieldName, out SeenField previous))
                {
                    if (!previous.Shared || !shared)
                    {
                        duplicates.Add($"{soFieldName} ({previous.Owner.Name} / {scriptType.Name})");
                    }

                    continue;
                }

                seen.Add(soFieldName, new SeenField(scriptType, shared));
                kept.Add(field);
            }

            if (kept.Count > 0)
            {
                blocks.Add(new FieldBlock(scriptType, kept));
            }
        }

        return blocks;
    }

    /// <summary>이미 SO 필드를 만든 스크립트와 그 공유 여부입니다.</summary>
    private readonly struct SeenField
    {
        /// <summary>이 SO 필드를 처음 만든 스크립트와 공유 선언을 보관합니다.</summary>
        /// <param name="owner">필드를 처음 선언한 스크립트 타입입니다.</param>
        /// <param name="shared">공유 필드로 선언되었는지 여부입니다.</param>
        public SeenField(Type owner, bool shared)
        {
            Owner = owner;
            Shared = shared;
        }

        /// <summary>이 SO 필드를 처음 만들어 낸 스크립트 타입입니다.</summary>
        public Type Owner { get; }

        /// <summary>그 선언이 공유 필드였는지 여부입니다. 양쪽 모두 공유일 때만 합치기가 의도된 것으로 봅니다.</summary>
        public bool Shared { get; }
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

        /// <summary>이 블록의 필드를 선언한 스크립트 타입입니다. SO 필드 접두사와 Header 이름의 근거입니다.</summary>
        public Type ScriptType { get; }

        /// <summary>그 스크립트에서 모은 밸런스 필드 목록입니다. 선언 순서를 유지합니다.</summary>
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
        return CreateSoAsset(scriptTypes, soType, assetBaseName, seedPrefab, assetFolder, SoKind.Balance, out assetPath, out message);
    }

    /// <summary>생성된 SO 타입의 에셋을 만들고 프리팹 값으로 채웁니다.</summary>
    /// <param name="scriptTypes">값을 가져올 컴포넌트들의 타입입니다.</param>
    /// <param name="soType">이미 컴파일된 SO 타입입니다.</param>
    /// <param name="assetBaseName">만들 .asset 파일 이름입니다.</param>
    /// <param name="seedPrefab">값을 가져올 프리팹입니다.</param>
    /// <param name="assetFolder">에셋을 만들 폴더입니다.</param>
    /// <param name="kind">만들 SO 종류입니다.</param>
    /// <param name="assetPath">생성한 에셋 경로입니다.</param>
    /// <param name="message">사용자에게 보여줄 결과 설명입니다.</param>
    /// <returns>에셋을 만들면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 피드백이면 프리팹 인스펙터에 이미 꽂혀 있는 에셋 참조를 그대로 옮겨 담습니다.
    /// 손으로 배선해 둔 것이 있으면 그것이 곧 SO의 초기값이 됩니다.
    /// </remarks>
    public static bool CreateSoAsset(
        IReadOnlyList<Type> scriptTypes,
        Type soType,
        string assetBaseName,
        GameObject seedPrefab,
        string assetFolder,
        SoKind kind,
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
        List<FieldBlock> blocks = CollectBlocks(scriptTypes, kind, out _);
        foreach (FieldBlock block in blocks)
        {
            SerializedObject seed = CreateSeed(block.ScriptType, seedPrefab);
            if (seed == null)
            {
                continue;
            }

            foreach (FieldInfo field in block.Fields)
            {
                // 프리팹 쪽은 스크립트 필드 이름, SO 쪽은 접두사가 붙은 이름입니다.
                SerializedProperty from = seed.FindProperty(field.Name);
                SerializedProperty to = target.FindProperty(GetSoFieldName(block.ScriptType, field));
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
    private static string BuildSoSource(
        string soName,
        List<FieldBlock> blocks,
        GameObject seedPrefab,
        List<PreservedField> preserved,
        SoKind kind)
    {
        bool feedback = kind == SoKind.Feedback;
        StringBuilder sb = new StringBuilder();
        List<string> sourceNames = new List<string>();
        foreach (FieldBlock block in blocks)
        {
            sourceNames.Add(block.ScriptType.Name);
        }

        sb.Append("using UnityEngine;\r\n");

        // 접두사가 붙는 필드가 하나라도 있으면 기존 에셋 값을 옮겨 받기 위해 이 using이 필요합니다.
        if (NeedsSerializationUsing(blocks))
        {
            sb.Append("using UnityEngine.Serialization;\r\n");
        }

        string markerName = GetMarkerAttributeType(kind).Name.Replace("Attribute", string.Empty);

        sb.Append("\r\n");
        sb.Append("/// <summary>\r\n");
        sb.Append("/// " + string.Join(", ", sourceNames)
            + (feedback ? "에 주입할 표현 리소스를 보관합니다.\r\n" : "에 주입할 순수 수치 밸런스 데이터를 보관합니다.\r\n"));
        sb.Append("/// </summary>\r\n");
        sb.Append("/// <remarks>\r\n");
        sb.Append($"/// 이 파일은 SO CSV 도구가 위 스크립트들의 [{markerName}] 필드에서 생성했습니다.\r\n");
        sb.Append("/// 필드 이름은 \"{스크립트 이름}_{필드 이름}\" 규칙을 따릅니다. 통합 SO 하나가 여러 컴포넌트 값을 함께 담아도\r\n");
        sb.Append("/// 이름이 겹치지 않게 하기 위해서이며, BindManager가 같은 규칙으로 짝을 찾습니다.\r\n");
        sb.Append($"/// [{markerName}(Shared = true)]로 선언한 필드만 접두사 없이 생성되어 여러 컴포넌트가 같은 값을 받습니다.\r\n");

        if (feedback)
        {
            sb.Append("/// 이 SO는 Unity 에셋 참조를 담으므로 CSV 대상이 아닙니다. 값은 Inspector에서 할당합니다.\r\n");
            sb.Append("/// [FeedbackReference]의 용도와 표시명은 필드 타입에서 추론한 초안입니다. 맞지 않으면 손으로 고치십시오.\r\n");
        }
        else
        {
            sb.Append("/// 값의 정본은 SO이며 CSV는 내보낸 스냅샷입니다. 허용 범위는 스크립트 쪽 [Clamp]에 선언됩니다.\r\n");
            sb.Append("/// 미디어·런타임 참조는 담지 않습니다. 표현 리소스는 Feedback SO가 소유합니다.\r\n");
        }

        sb.Append("/// </remarks>\r\n");
        sb.Append($"[CreateAssetMenu(fileName = \"{soName}\", menuName = \"GrayZone/{(feedback ? "Feedback" : "Balance")}/{soName}\")]\r\n");
        sb.Append($"public sealed class {soName} : ScriptableObject, {(feedback ? "IFeedbackData" : "IBalanceTableData")}\r\n");
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
            for (int f = 0; f < block.Fields.Count; f++)
            {
                FieldInfo field = block.Fields[f];
                sb.Append("\r\n");

                // 소스 주석은 코드에만 남아 Inspector와 시트에서는 보이지 않습니다.
                // 블록 첫 필드에 Header를 붙여야 인스펙터에서도 어느 컴포넌트 구간인지 보입니다.
                if (f == 0)
                {
                    sb.Append($"    [Header(\"{EscapeLiteral(block.ScriptType.Name)}\")]\r\n");
                }

                string tooltipText = BuildTooltipText(field);
                if (!string.IsNullOrWhiteSpace(tooltipText))
                {
                    sb.Append($"    [Tooltip(\"{EscapeLiteral(tooltipText)}\")]\r\n");
                }

                // 참조 필드에만 용도 표시를 답니다. 누락 검사가 이것을 근거로 빈 참조를 찾습니다.
                if (feedback && TryDescribeFeedbackReference(field, out string kindName, out string displayName))
                {
                    sb.Append(
                        $"    [FeedbackReference(FeedbackReferenceKind.{kindName}, \"{EscapeLiteral(displayName)}\")]\r\n");
                }

                string soFieldName = GetSoFieldName(block.ScriptType, field);
                if (!string.Equals(soFieldName, field.Name, StringComparison.Ordinal))
                {
                    // 접두사 규칙 이전에 만들어진 에셋의 값이 재생성으로 사라지지 않게 옛 이름을 남깁니다.
                    sb.Append($"    [FormerlySerializedAs(\"{EscapeLiteral(field.Name)}\")]\r\n");
                }

                string initializer = BuildInitializer(field, seed);
                sb.Append($"    [SerializeField] private {TypeRef(field.FieldType)} {soFieldName}{initializer};\r\n");
            }
        }

        AppendPreservedFields(sb, preserved);

        sb.Append("}\r\n");
        return sb.ToString();
    }

    /// <summary>스크립트에 대응이 없는 SO 전용 필드를 별도 블록으로 다시 씁니다.</summary>
    /// <param name="sb">소스를 조립 중인 버퍼입니다.</param>
    /// <param name="preserved">유지할 필드 목록입니다.</param>
    /// <remarks>
    /// 이름을 그대로 유지하는 것이 핵심입니다. 이름이 직렬화 키이므로, 바뀌면 에셋에 들어 있던 값을 잃습니다.
    /// 블록을 따로 두는 이유는 다음 재생성에서도 이 필드들이 "스크립트에서 온 것이 아니다"라고 읽히게 하기 위함입니다.
    /// </remarks>
    private static void AppendPreservedFields(StringBuilder sb, List<PreservedField> preserved)
    {
        if (preserved == null || preserved.Count == 0)
        {
            return;
        }

        sb.Append("\r\n");
        sb.Append("    // ───────────── SO 전용 (스크립트에 대응 없음) ─────────────\r\n");
        sb.Append("    // 생성기가 만들지 않는 필드입니다. 재생성해도 지워지지 않게 여기 유지합니다.\r\n");

        foreach (PreservedField field in preserved)
        {
            sb.Append("\r\n");

            if (!string.IsNullOrWhiteSpace(field.Tooltip))
            {
                sb.Append($"    [Tooltip(\"{EscapeLiteral(field.Tooltip)}\")]\r\n");
            }

            string initializer = string.IsNullOrEmpty(field.Literal) ? string.Empty : $" = {field.Literal}";
            sb.Append($"    [SerializeField] private {TypeRef(field.FieldType)} {field.Name}{initializer};\r\n");
        }
    }

    /// <summary>
    /// 피드백 필드에 붙일 참조 용도 표시를 필드 타입에서 추론합니다.
    /// </summary>
    /// <param name="field">대상 스크립트 필드입니다.</param>
    /// <param name="kindName">추론한 <see cref="FeedbackReferenceKind"/> 멤버 이름입니다.</param>
    /// <param name="displayName">누락 검사 창에 표시할 이름입니다.</param>
    /// <returns>참조 필드여서 표시를 달아야 하면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 수명·볼륨 같은 비참조 설정에는 달지 않습니다. 누락 검사는 "비어 있으면 안 되는 참조"를 찾는 것이지
    /// 숫자가 0인지 보는 것이 아니기 때문입니다.
    /// <para>
    /// 용도는 타입만으로 정확히 알 수 없습니다. <c>GameObject</c>는 머즐일 수도 트레이서일 수도 탄피일 수도 있습니다.
    /// 그래서 <b>초안</b>만 만들고, 맞지 않으면 사람이 고치도록 생성 파일 주석에 그 사실을 적습니다.
    /// 처음부터 손으로 다 적는 것보다 고치는 편이 싸다는 것이 이 생성기의 전제입니다.
    /// </para>
    /// </remarks>
    private static bool TryDescribeFeedbackReference(FieldInfo field, out string kindName, out string displayName)
    {
        kindName = null;
        displayName = null;

        Type type = field.FieldType;
        Type element = type.IsArray ? type.GetElementType() : type;
        if (element != null && element.IsGenericType)
        {
            Type[] arguments = element.GetGenericArguments();
            element = arguments.Length > 0 ? arguments[0] : element;
        }

        if (element == null || !typeof(UnityEngine.Object).IsAssignableFrom(element))
        {
            return false;
        }

        if (typeof(AudioClip).IsAssignableFrom(element))
        {
            kindName = nameof(FeedbackReferenceKind.Audio);
        }
        else if (typeof(Material).IsAssignableFrom(element))
        {
            kindName = nameof(FeedbackReferenceKind.SurfaceMaterial);
        }
        else
        {
            // 이름에 단서가 있으면 쓰고, 없으면 가장 흔한 용도로 둡니다.
            string lower = field.Name.ToLowerInvariant();
            if (lower.Contains("tracer"))
            {
                kindName = nameof(FeedbackReferenceKind.Tracer);
            }
            else if (lower.Contains("shell"))
            {
                kindName = nameof(FeedbackReferenceKind.Shell);
            }
            else if (lower.Contains("decal"))
            {
                kindName = nameof(FeedbackReferenceKind.Decal);
            }
            else
            {
                kindName = nameof(FeedbackReferenceKind.VisualEffect);
            }
        }

        displayName = BuildFeedbackDisplayName(field);
        return true;
    }

    /// <summary>누락 검사 창에 표시할 이름을 만듭니다.</summary>
    /// <param name="field">대상 필드입니다.</param>
    /// <returns><c>m_</c> 접두사를 뗀 필드 이름입니다.</returns>
    /// <remarks>
    /// 툴팁 문장을 쓰지 않습니다. 문장 길이가 제각각이라 어떤 항목은 한 줄 설명이, 어떤 항목은 필드 이름이
    /// 표시되어 목록이 들쭉날쭉해집니다. 설명은 바로 위 <c>[Tooltip]</c>에 이미 있으므로,
    /// 표시명은 짧고 일관된 쪽이 목록에서 찾기 쉽습니다. 더 나은 이름이 있으면 사람이 고칩니다.
    /// </remarks>
    private static string BuildFeedbackDisplayName(FieldInfo field)
    {
        return field.Name.StartsWith("m_", StringComparison.Ordinal) ? field.Name.Substring(2) : field.Name;
    }

    /// <summary>생성할 필드 중 이름이 바뀌는 것이 있어 마이그레이션 특성이 필요한지 확인합니다.</summary>
    /// <param name="blocks">생성 대상 필드 묶음입니다.</param>
    /// <returns>하나라도 접두사가 붙으면 <c>true</c>입니다.</returns>
    private static bool NeedsSerializationUsing(List<FieldBlock> blocks)
    {
        foreach (FieldBlock block in blocks)
        {
            foreach (FieldInfo field in block.Fields)
            {
                if (!string.Equals(GetSoFieldName(block.ScriptType, field), field.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
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

    /// <summary>런타임 값을 C# 리터럴로 바꿉니다. 리터럴로 적을 수 없는 타입은 빈 문자열입니다.</summary>
    /// <param name="value">인스턴스에서 읽은 기본값입니다.</param>
    /// <param name="fieldType">필드에 선언된 타입입니다.</param>
    /// <returns>초기화식에 넣을 리터럴입니다. 기본값과 같아 적을 필요가 없으면 빈 문자열입니다.</returns>
    /// <remarks>
    /// 보존 필드의 초기화식을 만들 때 씁니다. 소스의 초기화식을 파싱하는 대신 실제 기본값을 읽어
    /// 리터럴로 되돌리므로 텍스트 파싱이 필요 없습니다.
    /// </remarks>
    private static string ToLiteral(object value, Type fieldType)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (fieldType.IsEnum)
        {
            string enumName = value.ToString();
            return Enum.IsDefined(fieldType, enumName) ? $"{TypeRef(fieldType)}.{enumName}" : string.Empty;
        }

        switch (value)
        {
            case string s:
                // 빈 문자열은 굳이 적지 않습니다. 선언만 있어도 같은 값이 됩니다.
                return s.Length == 0 ? string.Empty : $"\"{EscapeLiteral(s)}\"";
            case bool b:
                return b ? "true" : string.Empty;
            case float f:
                return f != 0.0f ? f.ToString("R", CultureInfo.InvariantCulture) + "f" : string.Empty;
            case double d:
                return d != 0.0 ? d.ToString("R", CultureInfo.InvariantCulture) : string.Empty;
            case int i:
                return i != 0 ? i.ToString(CultureInfo.InvariantCulture) : string.Empty;
            case long l:
                return l != 0L ? l.ToString(CultureInfo.InvariantCulture) : string.Empty;
            default:
                return string.Empty;
        }
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

            // 피드백 SO는 에셋 참조가 값이므로 이것이 없으면 프리팹 배선을 옮겨 담지 못합니다.
            case SerializedPropertyType.ObjectReference:
                to.objectReferenceValue = from.objectReferenceValue;
                return true;

            // 사운드 후보 목록처럼 참조 배열도 그대로 옮깁니다.
            case SerializedPropertyType.Generic when from.isArray && to.isArray:
                CopyArray(from, to);
                return true;

            default: return false;
        }
    }

    /// <summary>배열/List 프로퍼티를 요소 단위로 복사합니다.</summary>
    /// <param name="from">값을 읽어올 배열 프로퍼티입니다.</param>
    /// <param name="to">값을 받을 배열 프로퍼티입니다.</param>
    /// <remarks>요소 타입이 서로 다르면 복사하지 못한 요소는 기본값으로 남습니다.</remarks>
    private static void CopyArray(SerializedProperty from, SerializedProperty to)
    {
        to.arraySize = from.arraySize;
        for (int i = 0; i < from.arraySize; i++)
        {
            CopyValue(from.GetArrayElementAtIndex(i), to.GetArrayElementAtIndex(i));
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
