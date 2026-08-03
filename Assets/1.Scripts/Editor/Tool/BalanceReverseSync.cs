using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 컴포넌트 인스펙터에 지금 들어 있는 값을 밸런스 SO와 CSV로 되돌려 쓰는 공용 함수입니다.
/// </summary>
/// <remarks>
/// <para>
/// <c>BindManager</c>가 SO에서 컴포넌트로 값을 넣는 방향이라면, 이쪽은 그 반대입니다.
/// 플레이테스트에서 인스펙터 값을 만져 감을 잡은 뒤 그 값을 정본으로 승격시키는 데 씁니다.
/// 그러지 않으면 만진 값은 Awake에서 SO 값에 덮여 사라집니다.
/// </para>
/// <para>
/// 대응 규칙은 주입과 같은 <b>필드 이름</b>입니다. 대상이 <c>[BalanceField]</c>로 선언한 필드만 보고,
/// SO에 같은 이름의 필드가 있을 때만 씁니다. 한쪽에만 있는 필드는 건너뛰고 결과에 수를 남깁니다.
/// </para>
/// <para>
/// 에디터 전용입니다. SO와 CSV는 프로젝트 자산이라 빌드에서는 쓸 수 없습니다.
/// 런타임 창(트레이너)에서 부를 때도 <c>UNITY_EDITOR</c> 안에서만 호출해야 합니다.
/// </para>
/// </remarks>
public static class BalanceReverseSync
{
    /// <summary>역동기화 한 번의 결과입니다.</summary>
    public struct Result
    {
        /// <summary>값이 실제로 바뀐 필드 수입니다.</summary>
        public int Changed;

        /// <summary>이름은 맞았지만 값이 이미 같아 쓰지 않은 필드 수입니다.</summary>
        public int Unchanged;

        /// <summary>SO에 같은 이름이 없어 건너뛴 필드 수입니다.</summary>
        public int MissingInSource;

        /// <summary>타입이 맞지 않아 건너뛴 필드 수입니다.</summary>
        public int TypeMismatch;

        /// <summary>대상이 물고 있던 밸런스 SO입니다. 못 찾으면 null입니다.</summary>
        public ScriptableObject Source;

        /// <summary>CSV로 내보낸 에셋 수입니다. CSV를 건너뛰면 -1입니다.</summary>
        public int CsvAssetCount;

        /// <summary>쓴 CSV 경로입니다. CSV를 건너뛰면 빈 문자열입니다.</summary>
        public string CsvPath;

        /// <summary>바뀐 필드의 사람이 읽을 수 있는 목록입니다.</summary>
        public List<string> ChangedFields;

        /// <summary>사용자에게 그대로 보여줄 수 있는 한 줄 요약입니다.</summary>
        public string Summary()
        {
            if (Source == null)
            {
                return "밸런스 SO를 찾지 못했습니다. 대상 컴포넌트에 SO가 연결되어 있어야 합니다.";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"{Source.name}: 변경 {Changed} / 동일 {Unchanged}");

            if (MissingInSource > 0)
            {
                sb.Append($" / SO에 없음 {MissingInSource}");
            }

            if (TypeMismatch > 0)
            {
                sb.Append($" / 타입 불일치 {TypeMismatch}");
            }

            if (CsvAssetCount >= 0)
            {
                sb.Append($" / CSV {CsvAssetCount}개 → {CsvPath}");
            }

            return sb.ToString();
        }
    }

    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// 런타임 코드가 이 구현을 부를 수 있도록 연결점에 꽂습니다.
    /// </summary>
    /// <remarks>
    /// 런타임 어셈블리는 에디터 어셈블리를 참조할 수 없어, 참조가 가능한 이쪽에서 등록합니다.
    /// 자세한 배경은 <see cref="BalanceReverseSyncHook"/>에 적어 두었습니다.
    /// </remarks>
    [InitializeOnLoadMethod]
    private static void RegisterRuntimeHook()
    {
        BalanceReverseSyncHook.Handler = target => Run(target).Summary();
        BalanceReverseSyncHook.Probe = target => FindBoundBalanceAsset(target) != null;
    }

    /// <summary>
    /// 선택한 GameObject 아래에서 밸런스 SO를 물고 있는 컴포넌트를 모두 찾아 역동기화합니다.
    /// </summary>
    /// <remarks>
    /// 한 캐릭터에 이동은 <c>ThirdPersonController</c>, 총기는 자식의 <c>Gun</c>처럼
    /// 여러 컴포넌트가 각자 다른 SO를 물고 있어, 하나만 고르게 하면 나머지를 빠뜨리기 쉽습니다.
    /// </remarks>
    [MenuItem("GrayZone/Balance/선택 대상 인스펙터 → SO + CSV 갱신")]
    private static void RunOnSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("밸런스 역동기화", "GameObject를 먼저 선택해야 합니다.", "확인");
            return;
        }

        StringBuilder report = new StringBuilder();
        int handled = 0;

        foreach (Component component in selected.GetComponentsInChildren<Component>(true))
        {
            if (component == null || FindBoundBalanceAsset(component) == null)
            {
                continue;
            }

            Result result = Run(component);
            handled++;
            report.AppendLine($"{component.GetType().Name} ({component.name})");
            report.AppendLine($"  {result.Summary()}");

            foreach (string line in result.ChangedFields)
            {
                report.AppendLine($"    {line}");
            }
        }

        if (handled == 0)
        {
            EditorUtility.DisplayDialog("밸런스 역동기화",
                $"'{selected.name}' 아래에서 밸런스 SO를 물고 있는 컴포넌트를 찾지 못했습니다.", "확인");
            return;
        }

        Debug.Log($"[BalanceReverseSync] {selected.name}\n{report}");
        EditorUtility.DisplayDialog("밸런스 역동기화",
            $"컴포넌트 {handled}개를 갱신했습니다. 자세한 내역은 콘솔을 확인하세요.\n\n{report}", "확인");
    }

    /// <summary>
    /// 대상 컴포넌트의 현재 값을 연결된 SO에 쓰고, 이어서 그 SO 타입의 CSV를 다시 내보냅니다.
    /// </summary>
    /// <param name="target">인스펙터 값을 읽어 올 컴포넌트입니다.</param>
    /// <param name="exportCsv">SO를 쓴 뒤 CSV까지 다시 내보내려면 true입니다.</param>
    /// <returns>무엇이 바뀌었는지 담은 결과입니다.</returns>
    public static Result Run(Component target, bool exportCsv = true)
    {
        Result result = new Result { CsvAssetCount = -1, CsvPath = string.Empty, ChangedFields = new List<string>() };

        if (target == null)
        {
            return result;
        }

        ScriptableObject source = FindBoundBalanceAsset(target);
        if (source == null)
        {
            return result;
        }

        result.Source = source;

        SerializedObject sourceObject = new SerializedObject(source);
        SerializedObject targetObject = new SerializedObject(target);

        foreach (FieldInfo field in EnumerateBalanceFields(target.GetType()))
        {
            SerializedProperty targetProperty = targetObject.FindProperty(field.Name);
            if (targetProperty == null)
            {
                continue;
            }

            SerializedProperty sourceProperty = sourceObject.FindProperty(field.Name);
            if (sourceProperty == null)
            {
                result.MissingInSource++;
                continue;
            }

            if (sourceProperty.propertyType != targetProperty.propertyType)
            {
                result.TypeMismatch++;
                continue;
            }

            if (TryCopy(sourceProperty, targetProperty, out string before, out string after))
            {
                result.Changed++;
                result.ChangedFields.Add($"{field.Name}: {before} -> {after}");
            }
            else
            {
                result.Unchanged++;
            }
        }

        if (result.Changed > 0)
        {
            sourceObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssets();
        }

        if (exportCsv)
        {
            (int count, string path) = ScriptableObjectCsvWindow.ExportTypeSilently(source.GetType());
            result.CsvAssetCount = count;
            result.CsvPath = path;
        }

        return result;
    }

    /// <summary>
    /// 대상이 물고 있는 밸런스 SO를 찾습니다.
    /// </summary>
    /// <remarks>
    /// 필드 이름을 <c>m_balanceSO</c>로 가정하지 않고 <see cref="IBalanceTableData"/>를 구현한
    /// 첫 참조를 찾습니다. 이름 규칙은 컴포넌트마다 어긋날 수 있지만 계약은 어긋나지 않습니다.
    /// </remarks>
    public static ScriptableObject FindBoundBalanceAsset(Component target)
    {
        if (target == null)
        {
            return null;
        }

        SerializedObject targetObject = new SerializedObject(target);
        SerializedProperty iterator = targetObject.GetIterator();

        while (iterator.NextVisible(true))
        {
            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            if (iterator.objectReferenceValue is ScriptableObject asset && asset is IBalanceTableData)
            {
                return asset;
            }
        }

        return null;
    }

    /// <summary>대상 타입과 기반 타입에서 <c>[BalanceField]</c>가 붙은 필드를 열거합니다.</summary>
    /// <remarks>
    /// 구조체 안으로 들어가지 않습니다. 주입 쪽(<c>BindManager</c>)도 같은 깊이만 보므로
    /// 여기서 더 깊이 들어가면 주입되지 않는 필드를 쓰게 됩니다.
    /// </remarks>
    private static IEnumerable<FieldInfo> EnumerateBalanceFields(Type type)
    {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(InstanceFields))
            {
                if (field.GetCustomAttribute<BalanceFieldAttribute>(true) != null)
                {
                    yield return field;
                }
            }
        }
    }

    /// <summary>값이 다를 때만 복사합니다.</summary>
    /// <returns>실제로 쓴 경우 true입니다.</returns>
    private static bool TryCopy(
        SerializedProperty destination,
        SerializedProperty sourceValue,
        out string before,
        out string after)
    {
        before = Describe(destination);
        after = Describe(sourceValue);

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return false;
        }

        switch (sourceValue.propertyType)
        {
            case SerializedPropertyType.Float:
                destination.floatValue = sourceValue.floatValue;
                return true;

            case SerializedPropertyType.Integer:
                destination.intValue = sourceValue.intValue;
                return true;

            case SerializedPropertyType.Boolean:
                destination.boolValue = sourceValue.boolValue;
                return true;

            case SerializedPropertyType.Enum:
                destination.enumValueIndex = sourceValue.enumValueIndex;
                return true;

            case SerializedPropertyType.String:
                destination.stringValue = sourceValue.stringValue;
                return true;

            case SerializedPropertyType.Vector2:
                destination.vector2Value = sourceValue.vector2Value;
                return true;

            case SerializedPropertyType.Vector3:
                destination.vector3Value = sourceValue.vector3Value;
                return true;

            case SerializedPropertyType.Color:
                destination.colorValue = sourceValue.colorValue;
                return true;

            case SerializedPropertyType.AnimationCurve:
                // 참조를 그대로 넘기면 SO와 컴포넌트가 같은 곡선 인스턴스를 공유해
                // 한쪽을 만지면 다른 쪽이 함께 변형됩니다. 복제해서 넣습니다.
                destination.animationCurveValue = new AnimationCurve(sourceValue.animationCurveValue.keys);
                return true;

            default:
                after = before;
                return false;
        }
    }

    /// <summary>비교와 로그에 쓸 값 표현을 만듭니다.</summary>
    private static string Describe(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Float: return property.floatValue.ToString("R");
            case SerializedPropertyType.Integer: return property.intValue.ToString();
            case SerializedPropertyType.Boolean: return property.boolValue.ToString();
            case SerializedPropertyType.Enum: return property.enumValueIndex.ToString();
            case SerializedPropertyType.String: return property.stringValue ?? string.Empty;
            case SerializedPropertyType.Vector2: return property.vector2Value.ToString("R");
            case SerializedPropertyType.Vector3: return property.vector3Value.ToString("R");
            case SerializedPropertyType.Color: return property.colorValue.ToString("R");
            case SerializedPropertyType.AnimationCurve: return DescribeCurve(property.animationCurveValue);
            default: return "(지원하지 않는 타입)";
        }
    }

    /// <summary>곡선을 키 목록 문자열로 표현합니다.</summary>
    private static string DescribeCurve(AnimationCurve curve)
    {
        if (curve == null || curve.length == 0)
        {
            return "(빈 곡선)";
        }

        StringBuilder sb = new StringBuilder();
        foreach (Keyframe key in curve.keys)
        {
            if (sb.Length > 0)
            {
                sb.Append('|');
            }

            sb.Append($"{key.time:0.###},{key.value:0.###}");
        }

        return sb.ToString();
    }
}
