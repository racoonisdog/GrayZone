using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 밸런스 SO를 적용했을 때 프리팹의 값이 어떻게 되는지 저장 없이 보여주는 Editor 전용 창입니다.
/// </summary>
/// <remarks>
/// 인스펙터에 보이는 값과 실제 런타임 값이 다른 문제를 푸는 도구입니다. 프리팹에 값을 구워 넣으면
/// 정본이 둘로 갈라지므로, 여기서는 <b>보여주기만</b> 하고 어떤 에셋도 수정하지 않습니다.
/// <para>
/// <b>값을 흉내내지 않고 실제 Bind를 돌립니다.</b> 대상 프리팹을 비활성 계층 아래에 임시로 만들어
/// 컴포넌트가 평소에 쓰는 바인딩 경로를 그대로 호출한 뒤 결과 필드를 읽습니다.
/// 그래서 <see cref="ClampAttribute"/> 보정과 <see cref="IBalancePostProcess"/> 후처리까지 반영된,
/// 런타임과 같은 값이 나옵니다. 규칙을 여기서 다시 구현하지 않으므로 런타임과 어긋날 여지도 없습니다.
/// 비활성 계층이라 <c>Awake</c>·<c>OnEnable</c>은 돌지 않아 다른 부작용은 생기지 않습니다.
/// </para>
/// </remarks>
public sealed class BalancePreviewWindow : EditorWindow
{
    private const string MissingCell = "—";

    [SerializeField] private GameObject m_target;
    [SerializeField] private ScriptableObject m_sharedOverride;

    private readonly List<PreviewGroup> m_groups = new List<PreviewGroup>();
    private Vector2 m_scroll;
    private string m_message = string.Empty;
    private bool m_built;

    /// <summary>프리뷰 창을 엽니다.</summary>
    [MenuItem("Tools/GrayZone/밸런스 프리뷰")]
    public static void Open()
    {
        Open(Selection.activeGameObject);
    }

    /// <summary>지정한 오브젝트를 대상으로 프리뷰 창을 엽니다.</summary>
    /// <param name="target">검사할 프리팹 또는 씬 오브젝트입니다.</param>
    /// <remarks>인스펙터의 안내 버튼처럼 창 밖에서 부르는 경로를 위한 진입점입니다.</remarks>
    public static void Open(GameObject target)
    {
        BalancePreviewWindow window = GetWindow<BalancePreviewWindow>("밸런스 프리뷰");
        window.minSize = new Vector2(620.0f, 360.0f);

        if (target != null)
        {
            window.m_target = target;
            window.Rebuild();
        }

        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        DrawInputs();
        EditorGUILayout.Space();
        DrawResult();
    }

    // ---------------------------------------------------------------- 입력

    /// <summary>대상과 가상 적용할 통합 SO를 고르는 영역입니다.</summary>
    private void DrawInputs()
    {
        using (EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope())
        {
            m_target = (GameObject)EditorGUILayout.ObjectField("대상 프리팹", m_target, typeof(GameObject), true);
            m_sharedOverride = (ScriptableObject)EditorGUILayout.ObjectField(
                "통합 SO (선택)", m_sharedOverride, typeof(ScriptableObject), false);

            if (check.changed)
            {
                m_built = false;
            }
        }

        EditorGUILayout.LabelField(
            "통합 SO를 비우면 대상에 실제로 배선된 것을 씁니다. 채우면 \"이걸 꽂으면 어떻게 되는지\"를 미리 봅니다.",
            EditorStyles.miniLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
            {
                if (GUILayout.Button("선택한 오브젝트 사용", GUILayout.Height(22)))
                {
                    m_target = Selection.activeGameObject;
                    Rebuild();
                }
            }

            using (new EditorGUI.DisabledScope(m_target == null))
            {
                if (GUILayout.Button("다시 계산", GUILayout.Height(22)))
                {
                    Rebuild();
                }
            }
        }

        EditorGUILayout.HelpBox(
            "이 창은 어떤 에셋도 수정하지 않습니다. 값을 실제로 반영하려면 SO를 고치거나, " +
            "인스펙터에서 만진 값을 SO로 올리는 역동기화를 쓰세요.",
            MessageType.None);
    }

    // ---------------------------------------------------------------- 결과

    /// <summary>계산된 비교 결과를 컴포넌트별로 그립니다.</summary>
    private void DrawResult()
    {
        if (m_target == null)
        {
            EditorGUILayout.HelpBox("대상 프리팹이나 씬 오브젝트를 넣어주세요.", MessageType.Info);
            return;
        }

        if (!m_built)
        {
            Rebuild();
        }

        if (!string.IsNullOrEmpty(m_message))
        {
            EditorGUILayout.HelpBox(m_message, MessageType.Warning);
            return;
        }

        using (EditorGUILayout.ScrollViewScope scope = new EditorGUILayout.ScrollViewScope(m_scroll))
        {
            m_scroll = scope.scrollPosition;

            foreach (PreviewGroup group in m_groups)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField($"{group.ComponentName}    출처: {group.SourceLabel}", EditorStyles.boldLabel);

                DrawRow("필드", "프리팹", "SO", "적용 후", "상태", EditorStyles.miniBoldLabel);
                foreach (PreviewRow row in group.Rows)
                {
                    DrawRow(row.Field, row.Before, row.Source, row.After, row.Status, EditorStyles.miniLabel);
                }
            }
        }
    }

    /// <summary>표 한 줄을 고정 폭으로 그립니다.</summary>
    /// <param name="field">필드 이름 칸입니다.</param>
    /// <param name="before">프리팹 값 칸입니다.</param>
    /// <param name="source">SO 원본 값 칸입니다.</param>
    /// <param name="after">적용 후 값 칸입니다.</param>
    /// <param name="status">상태 칸입니다.</param>
    /// <param name="style">칸에 사용할 스타일입니다.</param>
    private static void DrawRow(
        string field,
        string before,
        string source,
        string after,
        string status,
        GUIStyle style)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(field, style, GUILayout.MinWidth(180.0f));
            EditorGUILayout.LabelField(before, style, GUILayout.Width(90.0f));
            EditorGUILayout.LabelField(source, style, GUILayout.Width(90.0f));
            EditorGUILayout.LabelField(after, style, GUILayout.Width(90.0f));
            EditorGUILayout.LabelField(status, style, GUILayout.Width(110.0f));
        }
    }

    // ---------------------------------------------------------------- 계산

    /// <summary>대상과 SO 조합으로 비교표를 다시 만듭니다.</summary>
    private void Rebuild()
    {
        m_groups.Clear();
        m_message = string.Empty;
        m_built = true;

        if (m_target == null)
        {
            return;
        }

        // 비활성 계층 아래에 만들면 Awake/OnEnable이 돌지 않아 부작용 없이 필드만 다룰 수 있습니다.
        GameObject holder = new GameObject("BalancePreviewHolder")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        holder.SetActive(false);

        try
        {
            GameObject instance = Instantiate(m_target, holder.transform);
            BuildGroups(instance);
        }
        catch (Exception exception)
        {
            m_message = $"프리뷰를 만들지 못했습니다: {exception.Message}";
        }
        finally
        {
            DestroyImmediate(holder);
        }

        if (m_groups.Count == 0 && string.IsNullOrEmpty(m_message))
        {
            m_message =
                $"'{m_target.name}' 아래에 밸런스를 주입받는 컴포넌트가 없습니다. " +
                $"{nameof(ISharedBalanceReceiver)} 구현 여부를 확인하세요.";
        }
    }

    /// <summary>임시 인스턴스의 각 대상 컴포넌트를 바인딩 전후로 비교합니다.</summary>
    /// <param name="instance">비활성 계층에 만든 임시 인스턴스입니다.</param>
    private void BuildGroups(GameObject instance)
    {
        // 대상 자신의 바인더입니다. 창에서 SO를 지정하면 이 바인더의 몫만 가정으로 바꿔 봅니다.
        SOBinder rootBinder = instance.GetComponent<SOBinder>();
        if (rootBinder == null)
        {
            rootBinder = instance.GetComponentInChildren<SOBinder>(true);
        }

        foreach (Component component in instance.GetComponentsInChildren<Component>(true))
        {
            if (!(component is ISharedBalanceReceiver receiver))
            {
                continue;
            }

            List<FieldInfo> fields = BalanceScaffold.CollectBalanceFields(component.GetType());
            if (fields.Count == 0)
            {
                continue;
            }

            // 어느 엔티티에 속하는지는 가장 가까운 조상 바인더가 정합니다.
            // 손에 들린 총기처럼 자기 바인더를 가진 하위 프리팹은 자기 SO를 쓰므로,
            // 여기서도 같은 경계를 따라야 프리뷰가 런타임과 어긋나지 않습니다.
            SOBinder owner = component.GetComponentInParent<SOBinder>(true);
            ScriptableObject shared;
            if (owner == null || (owner == rootBinder && m_sharedOverride != null))
            {
                // 바인더가 없으면 경계도 없으므로 창에서 지정한 SO를 그대로 가정합니다.
                shared = m_sharedOverride;
            }
            else
            {
                shared = owner.SharedBalance;
            }

            // 우선순위는 런타임과 같습니다. 개별 SO가 있으면 그것이 이기고, 없을 때만 통합이 옵니다.
            ScriptableObject own = BalanceReverseSync.FindBoundBalanceAsset(component);
            ScriptableObject effective = own != null ? own : shared;

            PreviewGroup group = new PreviewGroup
            {
                ComponentName = component.GetType().Name,
                SourceLabel = DescribeSource(own, shared)
            };

            Dictionary<FieldInfo, object> before = ReadValues(component, fields);

            // 값을 흉내내지 않고 컴포넌트가 평소 쓰는 경로를 그대로 부릅니다.
            if (effective != null)
            {
                receiver.BindSharedBalance(effective);
            }

            Dictionary<FieldInfo, object> after = ReadValues(component, fields);

            foreach (FieldInfo field in fields)
            {
                group.Rows.Add(BuildRow(component.GetType(), field, before[field], after[field], effective));
            }

            m_groups.Add(group);
        }
    }

    /// <summary>비교표 한 줄을 만듭니다.</summary>
    /// <param name="componentType">필드를 선언한 컴포넌트 타입입니다.</param>
    /// <param name="field">대상 밸런스 필드입니다.</param>
    /// <param name="before">바인딩 전 값입니다.</param>
    /// <param name="after">바인딩 후 값입니다.</param>
    /// <param name="source">이번에 적용한 SO입니다. 없으면 <c>null</c>입니다.</param>
    private static PreviewRow BuildRow(
        Type componentType,
        FieldInfo field,
        object before,
        object after,
        ScriptableObject source)
    {
        FieldInfo sourceField = source != null ? ResolveSourceField(source.GetType(), componentType, field) : null;
        object sourceValue = sourceField != null ? sourceField.GetValue(source) : null;

        string status;
        if (source == null)
        {
            status = "SO 없음";
        }
        else if (sourceField == null)
        {
            status = "SO에 없음";
        }
        else if (!AreEqual(before, after))
        {
            // SO 값과 실제 대입값이 다르면 범위 보정이나 후처리가 개입한 것입니다.
            status = AreEqual(sourceValue, after) ? "변경" : "보정됨";
        }
        else
        {
            status = "같음";
        }

        return new PreviewRow
        {
            Field = field.Name,
            Before = Describe(before),
            Source = sourceField != null ? Describe(sourceValue) : MissingCell,
            After = Describe(after),
            Status = status
        };
    }

    /// <summary>대상 필드에 대응하는 SO 필드를 찾습니다.</summary>
    /// <param name="sourceType">원본 SO 타입입니다.</param>
    /// <param name="componentType">값을 받을 컴포넌트 타입입니다.</param>
    /// <param name="field">대상 스크립트 필드입니다.</param>
    /// <returns>어느 후보 이름으로도 찾지 못하면 <c>null</c>입니다.</returns>
    /// <remarks>이름 후보는 런타임 주입과 같은 <see cref="BindManager.EnumerateSourceFieldNames"/>를 씁니다.</remarks>
    private static FieldInfo ResolveSourceField(Type sourceType, Type componentType, FieldInfo field)
    {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (string candidate in BindManager.EnumerateSourceFieldNames(
                     componentType, field.Name, BalanceScaffold.IsShared(field)))
        {
            for (Type current = sourceType; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo found = current.GetField(candidate, flags);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>대상 컴포넌트의 밸런스 필드 값을 한 번에 읽습니다.</summary>
    /// <param name="component">읽을 컴포넌트입니다.</param>
    /// <param name="fields">읽을 밸런스 필드 목록입니다.</param>
    /// <returns>필드별 현재 값입니다.</returns>
    private static Dictionary<FieldInfo, object> ReadValues(Component component, List<FieldInfo> fields)
    {
        Dictionary<FieldInfo, object> values = new Dictionary<FieldInfo, object>();
        foreach (FieldInfo field in fields)
        {
            values[field] = field.GetValue(component);
        }

        return values;
    }

    /// <summary>어느 계층의 SO가 적용되는지 사람이 읽을 수 있게 설명합니다.</summary>
    /// <param name="own">컴포넌트가 직접 물고 있는 개별 SO입니다.</param>
    /// <param name="shared">엔티티 통합 SO입니다.</param>
    private static string DescribeSource(ScriptableObject own, ScriptableObject shared)
    {
        if (own != null)
        {
            return $"개별 ({own.name})";
        }

        return shared != null ? $"통합 ({shared.name})" : "없음 (프리팹 값 그대로)";
    }

    /// <summary>표에 표시할 문자열로 값을 바꿉니다.</summary>
    /// <param name="value">표시할 값입니다.</param>
    /// <returns>값이 없으면 자리표시자입니다.</returns>
    private static string Describe(object value)
    {
        if (value == null)
        {
            return MissingCell;
        }

        if (value is UnityEngine.Object unityObject)
        {
            return unityObject != null ? unityObject.name : MissingCell;
        }

        switch (value)
        {
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case bool b:
                return b ? "TRUE" : "FALSE";
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? MissingCell;
        }
    }

    /// <summary>두 값이 같은지 비교합니다.</summary>
    /// <param name="left">왼쪽 값입니다.</param>
    /// <param name="right">오른쪽 값입니다.</param>
    /// <returns>표시상 같은 값이면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 타입이 섞일 수 있어 표시 문자열로 비교합니다. 부동소수 오차까지 구분해 봐야
    /// 사람이 표에서 확인할 수 없는 차이라 도움이 되지 않습니다.
    /// </remarks>
    private static bool AreEqual(object left, object right)
    {
        return string.Equals(Describe(left), Describe(right), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 내부 타입

    /// <summary>한 컴포넌트의 비교 결과입니다.</summary>
    private sealed class PreviewGroup
    {
        /// <summary>표에 표시할 컴포넌트 이름입니다.</summary>
        public string ComponentName;

        /// <summary>어느 계층의 SO가 적용되는지 설명하는 문구입니다. 개별·통합·없음 중 하나입니다.</summary>
        public string SourceLabel;

        /// <summary>이 컴포넌트의 밸런스 필드별 비교 결과입니다.</summary>
        public List<PreviewRow> Rows { get; } = new List<PreviewRow>();
    }

    /// <summary>밸런스 필드 하나의 비교 결과입니다.</summary>
    private sealed class PreviewRow
    {
        /// <summary>대상 스크립트의 필드 이름입니다.</summary>
        public string Field;

        /// <summary>바인딩 전 프리팹에 저작되어 있던 값입니다.</summary>
        public string Before;

        /// <summary>원본 SO에 적힌 값입니다. 대응 필드가 없으면 자리표시자가 들어갑니다.</summary>
        public string Source;

        /// <summary>바인딩 후 실제로 대입된 값입니다. 범위 보정과 후처리까지 반영된 결과입니다.</summary>
        public string After;

        /// <summary>같음·변경·보정됨·SO에 없음·SO 없음 중 하나입니다.</summary>
        public string Status;
    }
}
