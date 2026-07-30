using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// 밸런스 값을 받을 런타임 필드임을 선언합니다.
/// </summary>
/// <remarks>
/// 데이터 원본 SO에는 이 특성을 붙이지 않습니다. 원본과 대상은 <b>필드 이름</b>으로 연결되며,
/// SO는 이 특성이 붙은 스크립트에서 생성하므로 이름이 어긋날 여지가 없습니다.
/// <para>
/// 값의 범위는 이 특성이 아니라 <see cref="ClampAttribute"/>가 선언합니다. 둘을 나눈 이유는
/// 경계가 SO 주입에만 필요한 것이 아니기 때문입니다. SO를 쓰지 않는 필드도 Inspector 입력을 제한해야 하고,
/// 그 경계는 SO를 쓰게 되더라도 그대로 유효합니다. 그래서 경계는 독립 선언으로 두고 여기서는
/// "이 필드는 SO에서 값을 받는다"만 표시합니다. 둘을 함께 붙이면 Inspector 입력과 SO 주입이 같은 범위를 따릅니다.
/// </para>
/// <para>
/// <b>붙일 수 있는 값의 범위(중요)</b>: 이 특성은 <b>상수 또는 첫 초기화 기본값</b>에만 붙입니다.
/// 플레이 중 변하는 값(현재 탄약, 현재 체력, 부품으로 증감한 실효 수치 등)은 SO에 두지 않습니다.
/// 그런 값의 소유자는 GameDataManager와 각 씬 데이터 매니저, 세이브 매니저이며,
/// 씬을 옮길 때 SO에서 다시 읽으면 진행 상황이 덮여 사라집니다.
/// </para>
/// <para>
/// 최대 체력·최대 탄창처럼 "기본값은 상수인데 런타임에 보정될 수 있는" 값은 <b>SO가 첫 초기화만</b> 담당합니다.
/// 바인딩은 대상이 새로 생성될 때마다(Awake) 실행되므로, 그 뒤에 데이터 매니저가 저장된 실효값을 반드시 다시 적용해야 합니다.
/// 예: <c>Gun.m_maxBullet</c>은 이 특성으로 기본값을 받고, 실효값은 스냅샷 복원이 덮어씁니다.
/// </para>
/// </remarks>
[Preserve]
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class BalanceFieldAttribute : Attribute
{
}

/// <summary>
/// 공용 필드 대입 이후 런타임 상태 및 변수 간 관계를 정리하는 선택적 후처리 seam입니다.
/// </summary>
public interface IBalancePostProcess
{
    /// <summary>하나 이상의 밸런스 값이 대상에 대입된 직후 호출됩니다.</summary>
    void OnBalanceApplied();
}

/// <summary>
/// 한 번의 밸런스 바인딩 결과입니다.
/// </summary>
public readonly struct BalanceBindResult
{
    /// <summary>
    /// 바인딩 과정에서 집계한 성공·누락·변환·보정·오류 수를 생성합니다.
    /// </summary>
    /// <param name="applied">대상 필드에 실제로 대입한 값의 수입니다.</param>
    /// <param name="missingSources">대상이 요구하는 이름의 원본 필드를 찾지 못한 수입니다.</param>
    /// <param name="typeMismatches">원본 값을 대상 필드 타입으로 변환하지 못한 수입니다.</param>
    /// <param name="clampedValues">강제 범위로 보정한 값의 수입니다.</param>
    /// <param name="errors">레이아웃 또는 실제 대입 중 발생한 오류의 수입니다.</param>
    public BalanceBindResult(
        int applied,
        int missingSources,
        int typeMismatches,
        int clampedValues,
        int errors)
    {
        Applied = applied;
        MissingSources = missingSources;
        TypeMismatches = typeMismatches;
        ClampedValues = clampedValues;
        Errors = errors;
    }

    /// <summary>대상 필드에 실제로 대입한 밸런스 값의 수입니다.</summary>
    public int Applied { get; }

    /// <summary>원본 SO에서 같은 이름의 필드를 찾지 못한 수입니다. SO 재생성이 필요하다는 신호입니다.</summary>
    public int MissingSources { get; }

    /// <summary>원본 값을 대상 필드 타입으로 변환하지 못한 값의 수입니다.</summary>
    public int TypeMismatches { get; }

    /// <summary>대상의 강제 최솟값·최댓값으로 보정한 값의 수입니다. 실패가 아니라 시트 값 점검 신호입니다.</summary>
    public int ClampedValues { get; }

    /// <summary>레이아웃 또는 실제 대입 중 발생한 오류의 수입니다.</summary>
    public int Errors { get; }

    /// <summary>예외 또는 타입 변환 오류 없이 바인딩이 끝났는지 여부입니다.</summary>
    public bool Succeeded => Errors == 0 && TypeMismatches == 0;
}

/// <summary>
/// 데이터 원본 ScriptableObject의 값을 같은 이름을 가진 런타임 필드에 대입하는 공용 바인딩 모듈입니다.
/// </summary>
/// <remarks>
/// Scene 수명주기가 필요 없는 순수 C# Singleton입니다. 바인딩 메타데이터는 타입별로 한 번만 Reflection으로
/// 수집하고 캐싱하며, 실제 대입은 초기화 또는 데이터 교체 시에만 수행합니다.
/// 순회 기준은 <b>대상 스크립트</b>입니다. 대상이 <see cref="BalanceFieldAttribute"/>로 요구한 필드만 원본에서 찾아오므로,
/// 원본 SO에만 있는 시트 식별 필드(무기 ID·이름 등)는 자동으로 무시됩니다.
/// </remarks>
[Preserve]
public sealed class BindManager
{
    private const BindingFlags DeclaredInstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Dictionary<Type, SourceLayout> m_sourceLayouts = new Dictionary<Type, SourceLayout>();
    private readonly Dictionary<Type, TargetLayout> m_targetLayouts = new Dictionary<Type, TargetLayout>();

    private BindManager()
    {
    }

    /// <summary>프로세스 전체에서 사용하는 바인딩 매니저 인스턴스입니다.</summary>
    public static BindManager Instance { get; } = new BindManager();

    /// <summary>
    /// 원본 SO의 값을 같은 이름을 가진 대상 필드에 대입합니다.
    /// </summary>
    /// <param name="source">데이터를 담은 ScriptableObject 원본입니다.</param>
    /// <param name="target"><see cref="BalanceFieldAttribute"/>가 선언된 런타임 대상 객체입니다.</param>
    /// <param name="context">로그에 연결할 Unity 오브젝트입니다. 선택 사항입니다.</param>
    /// <returns>이번 바인딩의 상세 집계 결과입니다.</returns>
    public BalanceBindResult Bind(ScriptableObject source, object target, UnityEngine.Object context = null)
    {
        // 1. 원본과 대상이 없으면 Reflection이나 대입을 시도하지 않고 즉시 실패합니다.
        if (source == null)
        {
            Debug.LogWarning("[BindManager] 밸런스 원본이 null입니다.", context);
            return new BalanceBindResult(0, 0, 0, 0, 1);
        }

        if (target == null)
        {
            Debug.LogError($"[BindManager] 원본 '{source.name}'에 대한 대상이 null입니다.", context);
            return new BalanceBindResult(0, 0, 0, 0, 1);
        }

        // 2. 타입별 Reflection 결과를 캐시에서 가져옵니다. 최초 호출 시에만 필드 메타데이터를 수집합니다.
        Type sourceType = source.GetType();
        Type targetType = target.GetType();
        SourceLayout sourceLayout = GetSourceLayout(sourceType);
        TargetLayout targetLayout = GetTargetLayout(targetType);

        // 3. 이름 중복이나 숫자형이 아닌 필드의 범위 선언 같은 구조 오류는 실제 값 대입 전에 차단합니다.
        int errors = LogLayoutErrors(sourceLayout.Errors, targetLayout.Errors, context);
        if (errors > 0)
        {
            return new BalanceBindResult(0, 0, 0, 0, errors);
        }

        // 4. 이번 호출의 대입·누락·변환·보정 결과를 누적할 카운터입니다.
        int applied = 0;
        int missingSources = 0;
        int typeMismatches = 0;
        int clampedValues = 0;

        // 5. 대상이 요구한 각 필드를 원본에서 같은 이름으로 찾아 대입합니다.
        foreach (TargetEntry targetEntry in targetLayout.Entries)
        {
            string fieldName = targetEntry.Field.Name;

            // 5-1. 원본에 같은 이름이 없으면 SO 재생성이 필요하다는 뜻이므로 경고만 남기고 넘어갑니다.
            if (!sourceLayout.ByName.TryGetValue(fieldName, out FieldInfo sourceField))
            {
                missingSources++;
                Debug.LogWarning(
                    $"[BindManager] 원본 '{sourceType.Name}'에 '{DescribeField(targetType, fieldName)}'에 대응하는 " +
                    "필드가 없습니다. SO를 다시 생성해야 할 수 있습니다.",
                    context);
                continue;
            }

            // 5-2. 원본 값을 읽고 대상이 선언한 강제 범위를 적용합니다.
            object value = sourceField.GetValue(source);
            value = ClampToRange(value, targetEntry, targetType, context, ref clampedValues);

            // 5-3. 대상 필드 타입에 맞게 직접 대입·enum 변환·수치 변환을 시도합니다.
            if (!TryConvertValue(value, targetEntry.Field.FieldType, out object converted))
            {
                typeMismatches++;
                Debug.LogError(
                    $"[BindManager] '{DescribeField(targetType, fieldName)}' 값을 " +
                    $"'{sourceField.FieldType.Name}'에서 '{targetEntry.Field.FieldType.Name}'으로 변환할 수 없습니다.",
                    context);
                continue;
            }

            // 5-4. 변환된 값을 private/public 대상 필드에 Reflection으로 대입합니다.
            try
            {
                targetEntry.Field.SetValue(target, converted);
                applied++;
            }
            catch (Exception exception)
            {
                errors++;
                Debug.LogError(
                    $"[BindManager] '{DescribeField(targetType, fieldName)}' 대입에 실패했습니다: {exception.Message}",
                    context);
            }
        }

        // 6. 하나라도 적용되었다면 대상이 런타임 후처리(필드 간 교정/UI 갱신 등)를 수행하도록 알립니다.
        if (applied > 0 && target is IBalancePostProcess postProcess)
        {
            postProcess.OnBalanceApplied();
        }

        // 7. 호출자가 QA 로그와 실패 원인을 확인할 수 있도록 누적 결과를 반환합니다.
        return new BalanceBindResult(
            applied,
            missingSources,
            typeMismatches,
            clampedValues,
            errors);
    }

    /// <summary>로그와 시트에서 사용할 "클래스명.필드명" 형식의 식별자를 만듭니다.</summary>
    /// <param name="type">필드를 선언한 타입입니다.</param>
    /// <param name="fieldName">대상 필드 이름입니다.</param>
    /// <returns>사람이 읽을 수 있는 고정 식별자입니다.</returns>
    public static string DescribeField(Type type, string fieldName)
    {
        return $"{type.Name}.{fieldName}";
    }

    /// <summary>원본 타입의 Reflection 레이아웃을 캐시에서 가져오거나 처음 생성합니다.</summary>
    /// <param name="type">이름으로 필드를 찾을 원본 타입입니다.</param>
    private SourceLayout GetSourceLayout(Type type)
    {
        if (!m_sourceLayouts.TryGetValue(type, out SourceLayout layout))
        {
            layout = BuildSourceLayout(type);
            m_sourceLayouts.Add(type, layout);
        }

        return layout;
    }

    /// <summary>대상 타입의 Reflection 레이아웃을 캐시에서 가져오거나 처음 생성합니다.</summary>
    /// <param name="type">[BalanceField]를 검색할 대상 타입입니다.</param>
    private TargetLayout GetTargetLayout(Type type)
    {
        if (!m_targetLayouts.TryGetValue(type, out TargetLayout layout))
        {
            layout = BuildTargetLayout(type);
            m_targetLayouts.Add(type, layout);
        }

        return layout;
    }

    /// <summary>원본 타입의 인스턴스 필드를 이름으로 조회할 수 있게 수집합니다.</summary>
    /// <param name="type">검색할 원본 타입입니다.</param>
    /// <returns>이름별 원본 필드와 이름 중복 오류를 포함한 레이아웃입니다.</returns>
    /// <remarks>원본에는 특성을 요구하지 않습니다. 대상이 요구하지 않는 필드는 조회되지 않으므로 그대로 무시됩니다.</remarks>
    private static SourceLayout BuildSourceLayout(Type type)
    {
        Dictionary<string, FieldInfo> byName = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        List<string> errors = new List<string>();

        foreach (FieldInfo field in EnumerateInstanceFields(type))
        {
            if (byName.ContainsKey(field.Name))
            {
                // 파생 클래스가 기반 클래스의 필드를 같은 이름으로 가리는 경우입니다.
                errors.Add($"원본 '{type.Name}'에 '{field.Name}' 이름이 중복됩니다.");
                continue;
            }

            byName.Add(field.Name, field);
        }

        return new SourceLayout(byName, errors);
    }

    /// <summary>대상 타입에서 쓰기 가능한 [BalanceField] 필드를 수집합니다.</summary>
    /// <param name="type">검색할 대상 타입입니다.</param>
    /// <returns>대입 대상 필드 목록과 레이아웃 오류를 포함한 결과입니다.</returns>
    private static TargetLayout BuildTargetLayout(Type type)
    {
        List<TargetEntry> entries = new List<TargetEntry>();
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        List<string> errors = new List<string>();

        foreach (FieldInfo field in EnumerateInstanceFields(type))
        {
            BalanceFieldAttribute attribute = field.GetCustomAttribute<BalanceFieldAttribute>(true);
            if (attribute == null)
            {
                continue;
            }

            if (field.IsInitOnly || field.IsStatic)
            {
                errors.Add($"대상 '{type.Name}.{field.Name}'은 쓰기 가능한 인스턴스 필드여야 합니다.");
                continue;
            }

            if (!names.Add(field.Name))
            {
                errors.Add($"대상 '{type.Name}'에 '{field.Name}' 이름이 중복됩니다.");
                continue;
            }

            // 범위는 별도 선언이므로 없을 수도 있습니다. 없으면 이 필드는 자르지 않고 그대로 대입합니다.
            ClampAttribute clamp = field.GetCustomAttribute<ClampAttribute>(true);
            if (clamp != null && !clamp.Validate(field.FieldType, out string clampError))
            {
                if (clamp.IsInverted)
                {
                    // 선언이 뒤집힌 것은 기획 데이터가 아니라 코드 오타입니다.
                    // 조용히 넘기면 원인을 찾기 어려우므로 경고하되, 서로 바꿔 적용해 동작은 유지합니다.
                    Debug.LogWarning($"[BindManager] '{type.Name}.{field.Name}' {clampError}");
                }
                else
                {
                    errors.Add($"대상 '{type.Name}.{field.Name}': {clampError}");
                    continue;
                }
            }

            entries.Add(new TargetEntry(field, clamp));
        }

        return new TargetLayout(entries, errors);
    }

    /// <summary>현재 타입부터 모든 기반 타입까지의 인스턴스 필드를 열거합니다.</summary>
    /// <param name="type">필드 열거를 시작할 타입입니다.</param>
    /// <returns>정적 필드와 object 타입 자체를 제외한 인스턴스 필드입니다.</returns>
    private static IEnumerable<FieldInfo> EnumerateInstanceFields(Type type)
    {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(DeclaredInstanceFields))
            {
                yield return field;
            }
        }
    }

    /// <summary>원본·대상 레이아웃 오류를 Unity 콘솔에 기록하고 개수를 반환합니다.</summary>
    /// <param name="sourceErrors">원본 레이아웃에서 발견된 오류 목록입니다.</param>
    /// <param name="targetErrors">대상 레이아웃에서 발견된 오류 목록입니다.</param>
    /// <param name="context">로그에 연결할 Unity 오브젝트입니다.</param>
    /// <returns>기록한 오류의 총 개수입니다.</returns>
    private static int LogLayoutErrors(
        IReadOnlyList<string> sourceErrors,
        IReadOnlyList<string> targetErrors,
        UnityEngine.Object context)
    {
        int count = 0;
        foreach (string error in sourceErrors)
        {
            Debug.LogError($"[BindManager] {error}", context);
            count++;
        }

        foreach (string error in targetErrors)
        {
            Debug.LogError($"[BindManager] {error}", context);
            count++;
        }

        return count;
    }

    /// <summary>대상 필드에 선언된 강제 범위를 적용하고 보정 횟수를 증가시킵니다.</summary>
    /// <param name="value">보정할 원본 값입니다.</param>
    /// <param name="entry">범위 메타데이터를 가진 대상 항목입니다.</param>
    /// <param name="targetType">로그 식별자에 사용할 대상 타입입니다.</param>
    /// <param name="context">로그에 연결할 Unity 오브젝트입니다.</param>
    /// <param name="clampedValues">보정 횟수를 누적하는 참조 변수입니다.</param>
    /// <returns>범위 적용 후 대상 대입에 사용할 값입니다.</returns>
    private static object ClampToRange(
        object value,
        TargetEntry entry,
        Type targetType,
        UnityEngine.Object context,
        ref int clampedValues)
    {
        // 범위 선언이 없는 필드는 그대로 대입합니다.
        if (entry.Clamp == null)
        {
            return value;
        }

        // enum·bool·string 등은 보정 대상이 아닙니다. 범위 선언 자체는 레이아웃 검사에서 이미 걸러집니다.
        if (!TryToDouble(value, out double numeric))
        {
            return value;
        }

        // 어떻게 자를지는 선언이 알고 있으므로 여기서는 부르기만 합니다.
        double clamped = entry.Clamp.Apply(numeric, out bool changed);
        if (!changed)
        {
            return value;
        }

        clampedValues++;
        Debug.LogWarning(
            $"[BindManager] '{DescribeField(targetType, entry.Field.Name)}' 값 " +
            $"{numeric.ToString(CultureInfo.InvariantCulture)}이 허용 범위를 벗어나 " +
            $"{clamped.ToString(CultureInfo.InvariantCulture)}으로 보정했습니다.",
            context);
        return Convert.ChangeType(clamped, entry.Field.FieldType, CultureInfo.InvariantCulture);
    }

    /// <summary>지원하는 숫자 타입을 범위 검사에 사용할 double로 변환합니다.</summary>
    /// <param name="value">변환할 값입니다.</param>
    /// <param name="numeric">변환된 숫자입니다.</param>
    /// <returns>값이 지원되는 숫자 타입이면 true입니다.</returns>
    private static bool TryToDouble(object value, out double numeric)
    {
        if (value == null || value.GetType().IsEnum)
        {
            numeric = 0d;
            return false;
        }

        switch (Type.GetTypeCode(value.GetType()))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                numeric = 0d;
                return false;
        }
    }

    /// <summary>원본 값을 대상 필드 타입으로 안전하게 변환합니다.</summary>
    /// <param name="value">변환할 원본 값입니다.</param>
    /// <param name="targetType">대상 필드의 타입입니다.</param>
    /// <param name="converted">변환된 값입니다.</param>
    /// <returns>직접 대입 또는 지원되는 enum·숫자 변환에 성공하면 true입니다.</returns>
    private static bool TryConvertValue(object value, Type targetType, out object converted)
    {
        if (value == null)
        {
            converted = null;
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
        }

        Type sourceType = value.GetType();
        if (targetType.IsAssignableFrom(sourceType))
        {
            converted = value;
            return true;
        }

        if (targetType.IsEnum && sourceType.IsEnum)
        {
            string enumName = value.ToString();
            if (Enum.IsDefined(targetType, enumName))
            {
                converted = Enum.Parse(targetType, enumName, false);
                return true;
            }
        }

        if (TryToDouble(value, out _) && ClampAttribute.IsClampableType(targetType))
        {
            try
            {
                converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                // 호출자가 필드 식별자와 함께 변환 실패를 한 번만 보고합니다.
            }
        }

        converted = null;
        return false;
    }

    private sealed class SourceLayout
    {
        /// <summary>이름별 원본 필드 레이아웃과 검사 오류를 보관합니다.</summary>
        /// <param name="byName">이름으로 조회할 수 있는 원본 필드입니다.</param>
        /// <param name="errors">레이아웃 검사 오류입니다.</param>
        public SourceLayout(Dictionary<string, FieldInfo> byName, List<string> errors)
        {
            ByName = byName;
            Errors = errors;
        }

        public Dictionary<string, FieldInfo> ByName { get; }
        public List<string> Errors { get; }
    }

    private sealed class TargetLayout
    {
        /// <summary>대상 필드 레이아웃과 검사 오류를 보관합니다.</summary>
        /// <param name="entries">대입 대상인 유효한 필드 항목입니다.</param>
        /// <param name="errors">레이아웃 검사 오류입니다.</param>
        public TargetLayout(List<TargetEntry> entries, List<string> errors)
        {
            Entries = entries;
            Errors = errors;
        }

        public List<TargetEntry> Entries { get; }
        public List<string> Errors { get; }
    }

    private sealed class TargetEntry
    {
        /// <summary>Reflection 필드와 그 필드에 선언된 범위를 묶습니다.</summary>
        /// <param name="field">밸런스 값을 받을 Reflection 필드입니다.</param>
        /// <param name="clamp">필드에 선언된 범위입니다. 선언이 없으면 null이며 그 경우 자르지 않습니다.</param>
        public TargetEntry(FieldInfo field, ClampAttribute clamp)
        {
            Field = field;
            Clamp = clamp;
        }

        public FieldInfo Field { get; }

        /// <summary>이 필드에 선언된 범위입니다. null이면 범위 제한이 없습니다.</summary>
        public ClampAttribute Clamp { get; }
    }
}
