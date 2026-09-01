using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// 공용 필드 대입 이후 런타임 상태 및 변수 간 관계를 정리하는 선택적 후처리 seam입니다.
/// </summary>
/// <remarks>
/// 단일 필드 경계는 <see cref="ClampAttribute"/>가 담당합니다. 여기는 "다른 필드가 경계인 관계"처럼
/// 필드 하나의 선언으로 표현할 수 없는 정리만 맡습니다. 예: 현재 탄약은 최대 탄창을 넘을 수 없다.
/// <para>
/// 밸런스 바인딩 뒤에만 호출됩니다. 피드백 바인딩은 참조를 꽂을 뿐 값 사이의 관계를 만들지 않으므로 호출하지 않습니다.
/// </para>
/// </remarks>
public interface IBalancePostProcess
{
    /// <summary>하나 이상의 밸런스 값이 대상에 대입된 직후 호출됩니다.</summary>
    void OnBalanceApplied();
}

/// <summary>
/// 한 번의 바인딩 결과입니다.
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

    /// <summary>대상 필드에 실제로 대입한 값의 수입니다.</summary>
    public int Applied { get; }

    /// <summary>원본 SO에서 대응하는 이름의 필드를 찾지 못한 수입니다. SO 재생성이 필요하다는 신호입니다.</summary>
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
/// Scene 수명주기가 필요 없는 순수 C# Singleton입니다.
/// <para>
/// 순회 기준은 <b>대상 스크립트</b>입니다. 대상이 <see cref="BalanceFieldAttribute"/> 또는
/// <see cref="FeedbackFieldAttribute"/>로 요구한 필드만 원본에서 찾아오므로,
/// 원본 SO에만 있는 시트 식별 필드(무기 ID·이름 등)는 자동으로 무시됩니다.
/// </para>
/// <para>
/// <b>대입 계획 캐싱</b>: (원본 타입, 대상 타입, 특성) 조합마다 "어느 원본 필드를 어느 대상 필드에 어떻게 넣을지"를
/// 한 번만 계산해 재사용합니다. 값 자체는 캐싱하지 않고 매번 원본에서 읽습니다.
/// 그래야 에디터에서 SO 값을 만졌을 때 다음 바인딩에 바로 반영됩니다.
/// 대량 스폰에서 사라지는 비용은 이름 조회·타입 판정·범위 판정이고, 남는 비용은 값 읽기와 대입뿐입니다.
/// </para>
/// <para>
/// 대입 자체는 <see cref="FieldInfo.SetValue"/>로 합니다. 세터를 미리 컴파일하면 더 빠르지만
/// <c>Expression.Compile</c>은 IL2CPP AOT에서 보장되지 않아, 빌드 타깃이 확정되기 전에는 넣지 않습니다.
/// </para>
/// </remarks>
[Preserve]
public sealed class BindManager
{
    private const BindingFlags DeclaredInstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Dictionary<Type, SourceLayout> m_sourceLayouts = new Dictionary<Type, SourceLayout>();
    private readonly Dictionary<PlanKey, BindPlan> m_plans = new Dictionary<PlanKey, BindPlan>();

    private BindManager()
    {
    }

    /// <summary>프로세스 전체에서 사용하는 바인딩 매니저 인스턴스입니다.</summary>
    public static BindManager Instance { get; } = new BindManager();

    /// <summary>
    /// 밸런스 SO의 값을 대상의 <see cref="BalanceFieldAttribute"/> 필드에 대입합니다.
    /// </summary>
    /// <param name="source">수치를 담은 밸런스 ScriptableObject입니다.</param>
    /// <param name="target">밸런스 필드를 선언한 런타임 대상 객체입니다.</param>
    /// <param name="context">로그에 연결할 Unity 오브젝트입니다. 선택 사항입니다.</param>
    /// <returns>이번 바인딩의 상세 집계 결과입니다.</returns>
    public BalanceBindResult Bind(ScriptableObject source, object target, UnityEngine.Object context = null)
    {
        return BindInternal(source, target, typeof(BalanceFieldAttribute), true, context);
    }

    /// <summary>
    /// 피드백 SO의 리소스 참조를 대상의 <see cref="FeedbackFieldAttribute"/> 필드에 대입합니다.
    /// </summary>
    /// <param name="source">표현 리소스를 담은 피드백 ScriptableObject입니다.</param>
    /// <param name="target">피드백 필드를 선언한 런타임 대상 객체입니다.</param>
    /// <param name="context">로그에 연결할 Unity 오브젝트입니다. 선택 사항입니다.</param>
    /// <returns>이번 바인딩의 상세 집계 결과입니다.</returns>
    /// <remarks>
    /// 밸런스와 완전히 같은 경로를 탑니다. 다른 점은 찾는 특성과, 후처리를 부르지 않는다는 것뿐입니다.
    /// </remarks>
    public BalanceBindResult BindFeedback(ScriptableObject source, object target, UnityEngine.Object context = null)
    {
        return BindInternal(source, target, typeof(FeedbackFieldAttribute), false, context);
    }

    /// <summary>로그와 시트에서 사용할 "클래스명.필드명" 형식의 식별자를 만듭니다.</summary>
    /// <param name="type">필드를 선언한 타입입니다.</param>
    /// <param name="fieldName">대상 필드 이름입니다.</param>
    /// <returns>사람이 읽을 수 있는 고정 식별자입니다.</returns>
    public static string DescribeField(Type type, string fieldName)
    {
        return $"{type.Name}.{fieldName}";
    }

    /// <summary>
    /// 원본 SO에서 대상 필드 하나에 대응하는 필드 이름을 만듭니다.
    /// </summary>
    /// <param name="targetTypeName">값을 받을 대상 타입 이름입니다.</param>
    /// <param name="fieldName">대상 필드 이름입니다.</param>
    /// <returns>접두사가 붙은 SO 필드 이름입니다.</returns>
    /// <remarks>
    /// SO를 생성하는 에디터 도구와 여기가 같은 규칙을 써야 하므로 이름 조립을 이 한 곳에 둡니다.
    /// </remarks>
    public static string BuildScopedFieldName(string targetTypeName, string fieldName)
    {
        return $"{targetTypeName}_{fieldName}";
    }

    /// <summary>
    /// 대상 필드 하나에 대응할 수 있는 원본 필드 이름을 우선순위대로 돌려줍니다.
    /// </summary>
    /// <param name="targetType">값을 받을 대상 타입입니다.</param>
    /// <param name="fieldName">대상 필드 이름입니다.</param>
    /// <param name="shared">여러 컴포넌트가 같은 필드를 공유하는지 여부입니다.</param>
    /// <returns>먼저 찾아봐야 하는 이름부터의 후보 목록입니다.</returns>
    /// <remarks>
    /// SO를 생성하는 도구와 역동기화·프리뷰 같은 에디터 경로가 런타임 바인딩과 같은 이름 규칙을 써야 하므로
    /// 규칙을 이 한 곳에 두고 모두 여기를 호출합니다. 규칙이 갈라지면 인스펙터에서 본 것과 실제 대입이 달라집니다.
    /// <para>
    /// 전용 필드는 <c>{대상 타입 이름}_{필드 이름}</c>을 먼저 찾고, 기반 타입 이름까지 거슬러 올라갑니다.
    /// SO를 생성할 때 고른 스크립트가 실제 컴포넌트의 기반 타입일 수 있기 때문입니다.
    /// 마지막 후보인 접두사 없는 이름은 공유 필드의 조회 경로이자, 접두사 규칙 이전에 만들어진 SO가
    /// 계속 동작하는 경로이기도 합니다.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> EnumerateSourceFieldNames(Type targetType, string fieldName, bool shared)
    {
        if (!shared)
        {
            for (Type current = targetType; current != null && current != typeof(object); current = current.BaseType)
            {
                yield return BuildScopedFieldName(current.Name, fieldName);
            }
        }

        yield return fieldName;
    }

    /// <summary>실제 대입을 수행하는 공용 본문입니다.</summary>
    /// <param name="source">데이터 원본 ScriptableObject입니다.</param>
    /// <param name="target">값을 받을 대상 객체입니다.</param>
    /// <param name="attributeType">이번 바인딩이 찾을 표시 특성입니다.</param>
    /// <param name="runPostProcess">대입 후 <see cref="IBalancePostProcess"/>를 부를지 여부입니다.</param>
    /// <param name="context">로그에 연결할 Unity 오브젝트입니다.</param>
    private BalanceBindResult BindInternal(
        ScriptableObject source,
        object target,
        Type attributeType,
        bool runPostProcess,
        UnityEngine.Object context)
    {
        // 1. 원본과 대상이 없으면 Reflection이나 대입을 시도하지 않고 즉시 실패합니다.
        if (source == null)
        {
            Debug.LogWarning("[BindManager] 데이터 원본이 null입니다.", context);
            return new BalanceBindResult(0, 0, 0, 0, 1);
        }

        if (target == null)
        {
            Debug.LogError($"[BindManager] 원본 '{source.name}'에 대한 대상이 null입니다.", context);
            return new BalanceBindResult(0, 0, 0, 0, 1);
        }

        // 2. 이 타입 조합의 대입 계획을 캐시에서 가져옵니다. 최초 호출 시에만 Reflection으로 계산합니다.
        Type targetType = target.GetType();
        BindPlan plan = GetPlan(source.GetType(), targetType, attributeType);

        // 3. 구조 문제는 인스턴스마다 반복되므로 계획을 만든 뒤 한 번만 보고합니다.
        //    대량 스폰에서 같은 경고가 수십 줄 쌓이는 것을 막습니다.
        plan.ReportOnce(context);

        // 4. 이름 중복이나 숫자형이 아닌 필드의 범위 선언 같은 구조 오류는 실제 값 대입 전에 차단합니다.
        if (plan.Errors.Count > 0)
        {
            return new BalanceBindResult(0, 0, 0, 0, plan.Errors.Count);
        }

        int applied = 0;
        int typeMismatches = plan.TypeMismatches;
        int clampedValues = 0;
        int errors = 0;

        // 5. 계획에 남은 항목만 대입합니다. 누락·타입 불일치는 계획 단계에서 이미 걸러졌습니다.
        foreach (PlanEntry entry in plan.Entries)
        {
            object value = entry.Source.GetValue(source);

            // 5-1. 대상이 선언한 강제 범위를 적용합니다. 잘렸으면 이미 대상 타입으로 변환된 값이 돌아옵니다.
            value = ApplyClamp(value, entry, targetType, context, ref clampedValues, out bool converted);

            // 5-2. 잘리지 않았다면 계획이 정한 방식으로 대상 필드 타입에 맞춥니다.
            if (!converted && !TryConvert(value, entry, out value))
            {
                typeMismatches++;
                Debug.LogError(
                    $"[BindManager] '{DescribeField(targetType, entry.Target.Name)}' 값을 " +
                    $"'{entry.Source.FieldType.Name}'에서 '{entry.Target.FieldType.Name}'으로 변환할 수 없습니다.",
                    context);
                continue;
            }

            // 5-3. 변환된 값을 private/public 대상 필드에 Reflection으로 대입합니다.
            try
            {
                entry.Target.SetValue(target, value);
                applied++;
            }
            catch (Exception exception)
            {
                errors++;
                Debug.LogError(
                    $"[BindManager] '{DescribeField(targetType, entry.Target.Name)}' 대입에 실패했습니다: {exception.Message}",
                    context);
            }
        }

        // 6. 하나라도 적용되었다면 대상이 런타임 후처리(필드 간 교정/UI 갱신 등)를 수행하도록 알립니다.
        if (runPostProcess && applied > 0 && target is IBalancePostProcess postProcess)
        {
            postProcess.OnBalanceApplied();
        }

        // 7. 호출자가 QA 로그와 실패 원인을 확인할 수 있도록 누적 결과를 반환합니다.
        return new BalanceBindResult(
            applied,
            plan.MissingSources,
            typeMismatches,
            clampedValues,
            errors);
    }

    // ---------------------------------------------------------------- 계획 수립

    /// <summary>타입 조합의 대입 계획을 캐시에서 가져오거나 처음 계산합니다.</summary>
    /// <param name="sourceType">값을 읽어올 원본 SO 타입입니다.</param>
    /// <param name="targetType">값을 받을 대상 타입입니다.</param>
    /// <param name="attributeType">찾을 표시 특성입니다.</param>
    private BindPlan GetPlan(Type sourceType, Type targetType, Type attributeType)
    {
        PlanKey key = new PlanKey(sourceType, targetType, attributeType);
        if (!m_plans.TryGetValue(key, out BindPlan plan))
        {
            plan = BuildPlan(GetSourceLayout(sourceType), sourceType, targetType, attributeType);
            m_plans.Add(key, plan);
        }

        return plan;
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

    /// <summary>대상 타입의 표시된 필드를 원본 필드와 짝지어 대입 계획을 만듭니다.</summary>
    /// <param name="sourceLayout">이름으로 조회 가능한 원본 필드 목록입니다.</param>
    /// <param name="sourceType">로그에 표시할 원본 타입입니다.</param>
    /// <param name="targetType">검색할 대상 타입입니다.</param>
    /// <param name="attributeType">찾을 표시 특성입니다.</param>
    private static BindPlan BuildPlan(
        SourceLayout sourceLayout,
        Type sourceType,
        Type targetType,
        Type attributeType)
    {
        BindPlan plan = new BindPlan();
        plan.Errors.AddRange(sourceLayout.Errors);

        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

        foreach (FieldInfo field in EnumerateInstanceFields(targetType))
        {
            BindFieldAttribute marker = GetMarker(field, attributeType);
            if (marker == null)
            {
                continue;
            }

            if (field.IsInitOnly || field.IsStatic)
            {
                plan.Errors.Add($"대상 '{targetType.Name}.{field.Name}'은 쓰기 가능한 인스턴스 필드여야 합니다.");
                continue;
            }

            if (!names.Add(field.Name))
            {
                plan.Errors.Add($"대상 '{targetType.Name}'에 '{field.Name}' 이름이 중복됩니다.");
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
                    plan.Warnings.Add($"'{targetType.Name}.{field.Name}' {clampError}");
                }
                else
                {
                    plan.Errors.Add($"대상 '{targetType.Name}.{field.Name}': {clampError}");
                    continue;
                }
            }

            FieldInfo sourceField = ResolveSourceField(sourceLayout, targetType, field.Name, marker.Shared);
            if (sourceField == null)
            {
                plan.MissingSources++;
                plan.Warnings.Add(
                    $"원본 '{sourceType.Name}'에 '{DescribeField(targetType, field.Name)}'에 대응하는 " +
                    "필드가 없습니다. SO를 다시 생성해야 할 수 있습니다.");
                continue;
            }

            if (!TryResolveConversion(sourceField.FieldType, field.FieldType, out ConversionKind conversion))
            {
                plan.TypeMismatches++;
                plan.Warnings.Add(
                    $"'{DescribeField(targetType, field.Name)}'은 '{sourceField.FieldType.Name}'에서 " +
                    $"'{field.FieldType.Name}'으로 변환할 수 없어 대입 대상에서 제외했습니다.");
                continue;
            }

            // 범위 보정은 숫자만 대상입니다. 판정을 여기서 끝내 대입 경로에 남기지 않습니다.
            bool canClamp = clamp != null && ClampAttribute.IsClampableType(sourceField.FieldType);
            plan.Entries.Add(new PlanEntry(sourceField, field, canClamp ? clamp : null, conversion));
        }

        return plan;
    }

    /// <summary>
    /// 대상 필드 하나에 대응하는 원본 필드를 찾습니다.
    /// </summary>
    /// <param name="layout">이름으로 조회 가능한 원본 필드 목록입니다.</param>
    /// <param name="targetType">값을 받을 대상 타입입니다.</param>
    /// <param name="fieldName">대상 필드 이름입니다.</param>
    /// <param name="shared">여러 컴포넌트가 같은 필드를 공유하는지 여부입니다.</param>
    /// <returns>찾지 못하면 <c>null</c>입니다.</returns>
    /// <remarks>이름 규칙 자체는 <see cref="EnumerateSourceFieldNames"/>가 소유합니다. 여기서는 후보를 차례로 조회만 합니다.</remarks>
    private static FieldInfo ResolveSourceField(
        SourceLayout layout,
        Type targetType,
        string fieldName,
        bool shared)
    {
        foreach (string candidate in EnumerateSourceFieldNames(targetType, fieldName, shared))
        {
            if (layout.ByName.TryGetValue(candidate, out FieldInfo found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>필드에 붙은 이번 바인딩 대상 특성을 찾습니다.</summary>
    /// <param name="field">검사할 대상 필드입니다.</param>
    /// <param name="attributeType">찾을 특성 타입입니다.</param>
    /// <returns>붙어 있지 않으면 <c>null</c>입니다.</returns>
    private static BindFieldAttribute GetMarker(FieldInfo field, Type attributeType)
    {
        object[] found = field.GetCustomAttributes(attributeType, true);
        return found.Length > 0 ? found[0] as BindFieldAttribute : null;
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

    // ---------------------------------------------------------------- 값 처리

    /// <summary>대상 필드에 선언된 강제 범위를 적용하고 보정 횟수를 증가시킵니다.</summary>
    /// <param name="value">보정할 원본 값입니다.</param>
    /// <param name="entry">범위 메타데이터를 가진 대입 항목입니다.</param>
    /// <param name="targetType">로그 식별자에 사용할 대상 타입입니다.</param>
    /// <param name="context">로그에 연결할 Unity 오브젝트입니다.</param>
    /// <param name="clampedValues">보정 횟수를 누적하는 참조 변수입니다.</param>
    /// <param name="converted">보정 과정에서 이미 대상 타입으로 변환되었으면 <c>true</c>입니다.</param>
    /// <returns>범위 적용 후 대상 대입에 사용할 값입니다.</returns>
    private static object ApplyClamp(
        object value,
        PlanEntry entry,
        Type targetType,
        UnityEngine.Object context,
        ref int clampedValues,
        out bool converted)
    {
        converted = false;

        // 범위 선언이 없거나 숫자가 아닌 필드는 그대로 대입합니다.
        if (entry.Clamp == null || !TryToDouble(value, out double numeric))
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
            $"[BindManager] '{DescribeField(targetType, entry.Target.Name)}' 값 " +
            $"{numeric.ToString(CultureInfo.InvariantCulture)}이 허용 범위를 벗어나 " +
            $"{clamped.ToString(CultureInfo.InvariantCulture)}으로 보정했습니다.",
            context);

        converted = true;
        return Convert.ChangeType(clamped, entry.Target.FieldType, CultureInfo.InvariantCulture);
    }

    /// <summary>계획이 정한 방식으로 원본 값을 대상 필드 타입에 맞춥니다.</summary>
    /// <param name="value">변환할 원본 값입니다.</param>
    /// <param name="entry">변환 방식을 가진 대입 항목입니다.</param>
    /// <param name="converted">변환된 값입니다.</param>
    /// <returns>변환에 성공하면 <c>true</c>입니다.</returns>
    private static bool TryConvert(object value, PlanEntry entry, out object converted)
    {
        Type targetFieldType = entry.Target.FieldType;

        if (value == null)
        {
            converted = null;
            return !targetFieldType.IsValueType || Nullable.GetUnderlyingType(targetFieldType) != null;
        }

        switch (entry.Conversion)
        {
            case ConversionKind.Direct:
                converted = value;
                return true;

            case ConversionKind.EnumByName:
                {
                    // 값 자체가 대상 enum에 없는 멤버일 수 있어 계획이 아니라 여기서 판정합니다.
                    string enumName = value.ToString();
                    if (Enum.IsDefined(targetFieldType, enumName))
                    {
                        converted = Enum.Parse(targetFieldType, enumName, false);
                        return true;
                    }

                    converted = null;
                    return false;
                }

            default:
                try
                {
                    converted = Convert.ChangeType(value, targetFieldType, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception)
                {
                    // 오버플로처럼 값에 달린 실패입니다. 호출자가 필드 식별자와 함께 한 번만 보고합니다.
                    converted = null;
                    return false;
                }
        }
    }

    /// <summary>원본 필드 타입에서 대상 필드 타입으로 가는 변환 방식을 정합니다.</summary>
    /// <param name="sourceFieldType">원본 필드에 선언된 타입입니다.</param>
    /// <param name="targetFieldType">대상 필드에 선언된 타입입니다.</param>
    /// <param name="conversion">결정된 변환 방식입니다.</param>
    /// <returns>변환할 방법이 있으면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 선언 타입만으로 판정하므로 계획 단계에서 한 번만 계산합니다.
    /// 값에 달린 실패(정의되지 않은 enum 이름, 오버플로)는 여기서 알 수 없어 대입 시점에 다시 확인합니다.
    /// </remarks>
    private static bool TryResolveConversion(
        Type sourceFieldType,
        Type targetFieldType,
        out ConversionKind conversion)
    {
        if (targetFieldType.IsAssignableFrom(sourceFieldType))
        {
            conversion = ConversionKind.Direct;
            return true;
        }

        if (targetFieldType.IsEnum && sourceFieldType.IsEnum)
        {
            conversion = ConversionKind.EnumByName;
            return true;
        }

        if (ClampAttribute.IsClampableType(sourceFieldType) && ClampAttribute.IsClampableType(targetFieldType))
        {
            conversion = ConversionKind.Numeric;
            return true;
        }

        conversion = ConversionKind.Direct;
        return false;
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

    // ---------------------------------------------------------------- 내부 타입

    /// <summary>원본 값을 대상 필드 타입으로 맞추는 방식입니다.</summary>
    private enum ConversionKind
    {
        /// <summary>그대로 대입할 수 있습니다.</summary>
        Direct,

        /// <summary>같은 이름의 enum 멤버로 옮깁니다.</summary>
        EnumByName,

        /// <summary>숫자 타입 사이의 변환입니다.</summary>
        Numeric
    }

    /// <summary>대입 계획을 구분하는 타입 조합입니다.</summary>
    private readonly struct PlanKey : IEquatable<PlanKey>
    {
        private readonly Type m_source;
        private readonly Type m_target;
        private readonly Type m_attribute;

        /// <summary>원본·대상·특성 세 타입으로 계획 키를 만듭니다.</summary>
        public PlanKey(Type source, Type target, Type attribute)
        {
            m_source = source;
            m_target = target;
            m_attribute = attribute;
        }

        /// <summary>세 타입이 모두 같은 키인지 비교합니다.</summary>
        /// <param name="other">비교할 다른 키입니다.</param>
        /// <returns>원본·대상·특성 타입이 모두 같으면 <c>true</c>입니다.</returns>
        public bool Equals(PlanKey other)
        {
            return m_source == other.m_source
                && m_target == other.m_target
                && m_attribute == other.m_attribute;
        }

        /// <summary>같은 타입의 키와만 같다고 판단합니다.</summary>
        /// <param name="obj">비교할 객체입니다.</param>
        /// <returns>같은 조합을 가리키는 키이면 <c>true</c>입니다.</returns>
        public override bool Equals(object obj)
        {
            return obj is PlanKey other && Equals(other);
        }

        /// <summary>세 타입을 조합한 해시를 만듭니다.</summary>
        /// <returns>Dictionary 조회에 사용할 해시값입니다.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = m_source != null ? m_source.GetHashCode() : 0;
                hash = (hash * 397) ^ (m_target != null ? m_target.GetHashCode() : 0);
                hash = (hash * 397) ^ (m_attribute != null ? m_attribute.GetHashCode() : 0);
                return hash;
            }
        }
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

        /// <summary>이름으로 조회할 수 있는 원본 필드 목록입니다.</summary>
        public Dictionary<string, FieldInfo> ByName { get; }

        /// <summary>원본 레이아웃에서 발견한 구조 오류입니다. 이름 중복 등이 여기 담깁니다.</summary>
        public List<string> Errors { get; }
    }

    /// <summary>한 타입 조합에 대해 한 번만 계산하는 대입 계획입니다.</summary>
    private sealed class BindPlan
    {
        private bool m_reported;

        /// <summary>실제로 대입할 항목입니다. 누락과 타입 불일치는 이미 제외되어 있습니다.</summary>
        public List<PlanEntry> Entries { get; } = new List<PlanEntry>();

        /// <summary>대입을 막는 구조 오류입니다.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>대입을 막지는 않지만 알려야 하는 구조 문제입니다.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>원본에서 같은 이름을 찾지 못한 대상 필드 수입니다.</summary>
        public int MissingSources { get; set; }

        /// <summary>타입이 맞지 않아 대입 대상에서 제외한 필드 수입니다.</summary>
        public int TypeMismatches { get; set; }

        /// <summary>구조 문제를 최초 한 번만 콘솔에 보고합니다.</summary>
        /// <param name="context">로그에 연결할 Unity 오브젝트입니다.</param>
        /// <remarks>
        /// 구조 문제는 인스턴스가 아니라 타입 조합에 달려 있어 매번 찍으면 같은 줄만 쌓입니다.
        /// 그래서 처음 만든 인스턴스의 context로 한 번만 남깁니다. 이후 호출은 결과 집계로 확인합니다.
        /// </remarks>
        public void ReportOnce(UnityEngine.Object context)
        {
            if (m_reported)
            {
                return;
            }

            m_reported = true;

            foreach (string error in Errors)
            {
                Debug.LogError($"[BindManager] {error}", context);
            }

            foreach (string warning in Warnings)
            {
                Debug.LogWarning($"[BindManager] {warning}", context);
            }
        }
    }

    /// <summary>대입 한 건에 필요한 정보를 미리 묶어 둔 항목입니다.</summary>
    private sealed class PlanEntry
    {
        /// <summary>원본 필드, 대상 필드, 범위, 변환 방식을 묶습니다.</summary>
        /// <param name="source">값을 읽어올 원본 필드입니다.</param>
        /// <param name="target">값을 받을 대상 필드입니다.</param>
        /// <param name="clamp">적용할 범위입니다. 없거나 숫자가 아니면 <c>null</c>입니다.</param>
        /// <param name="conversion">원본 값을 대상 타입으로 맞추는 방식입니다.</param>
        public PlanEntry(FieldInfo source, FieldInfo target, ClampAttribute clamp, ConversionKind conversion)
        {
            Source = source;
            Target = target;
            Clamp = clamp;
            Conversion = conversion;
        }

        /// <summary>값을 읽어올 원본 SO의 필드입니다.</summary>
        public FieldInfo Source { get; }

        /// <summary>값을 받을 대상 객체의 필드입니다.</summary>
        public FieldInfo Target { get; }

        /// <summary>이 필드에 적용할 범위입니다. null이면 자르지 않습니다.</summary>
        public ClampAttribute Clamp { get; }

        /// <summary>원본 값을 대상 필드 타입으로 맞추는 방식입니다. 계획 단계에서 한 번만 정합니다.</summary>
        public ConversionKind Conversion { get; }
    }
}
