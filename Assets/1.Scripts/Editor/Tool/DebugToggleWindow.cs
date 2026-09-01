using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 씬에 있는 컴포넌트들의 Debug 구역 필드를 한곳에 모아 탭으로 보여 주는 에디터 창입니다.
/// </summary>
/// <remarks>
/// 디버그 항목은 각 컴포넌트의 인스펙터 안에 흩어져 있어서, 하나를 켜려면 해당 오브젝트를 찾아 선택하고
/// 폴드아웃을 펼쳐야 합니다. 켜 둔 것을 나중에 되찾아 끄기는 더 어렵습니다.
/// 이 창은 그 항목들을 <b>소유 컴포넌트에서 읽어</b> 한 화면에 모읍니다.
///
/// 수집 규칙은 <see cref="DebugSectionRegistry"/>가 소유합니다. 런타임 트레이너도 같은 수집기를 쓰므로
/// 두 화면의 목록이 어긋나지 않습니다.
///
/// 값은 복제하지 않고 <see cref="SerializedObject"/>로 원본에 직접 씁니다. 별도 저장소를 두면
/// 인스펙터와 이 창의 값이 갈라집니다. 프리팹 오버라이드와 Undo도 그대로 따라옵니다.
/// </remarks>
public sealed class DebugToggleWindow : EditorWindow
{
    private readonly Dictionary<Component, SerializedObject> m_serializedCache =
        new Dictionary<Component, SerializedObject>();

    private List<DebugFieldEntry> m_entries = new List<DebugFieldEntry>();
    private int m_tabIndex;
    private string m_filter = string.Empty;
    private Vector2 m_scroll;
    private bool m_includeInactive = true;
    private double m_nextAutoScanTime;

    /// <summary>자동으로 목록을 다시 훑는 주기(초)입니다.</summary>
    /// <remarks>
    /// Play 중에는 스폰으로 대상이 늘어납니다. 매 프레임 훑으면 씬이 클 때 눈에 띄게 느려지므로 주기를 둡니다.
    /// </remarks>
    private const double AutoScanInterval = 2.0;

    [MenuItem("Tools/GrayZone/Debug Toggles")]
    private static void Open()
    {
        DebugToggleWindow window = GetWindow<DebugToggleWindow>();
        window.titleContent = new GUIContent("Debug Toggles");
        window.minSize = new Vector2(360.0f, 260.0f);
        window.Rescan();
    }

    private void OnEnable()
    {
        Rescan();
    }

    private void OnInspectorUpdate()
    {
        // 창이 열려 있는 동안 값이 밖에서 바뀔 수 있어(인스펙터·런타임 트레이너) 표시를 계속 갱신합니다.
        Repaint();

        if (EditorApplication.timeSinceStartup < m_nextAutoScanTime)
        {
            return;
        }

        m_nextAutoScanTime = EditorApplication.timeSinceStartup + AutoScanInterval;
        Rescan();
    }

    /// <summary>씬을 다시 훑어 목록을 만듭니다.</summary>
    private void Rescan()
    {
        m_serializedCache.Clear();

        IEnumerable<Component> components = m_includeInactive
            ? Resources.FindObjectsOfTypeAll<Component>().Where(IsSceneObject)
            : FindObjectsByType<Component>(FindObjectsSortMode.None);

        m_entries = DebugSectionRegistry.Collect(components);
    }

    /// <summary>
    /// 에셋이나 프리팹 원본이 아니라 실제 씬에 놓인 오브젝트인지 확인합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="Resources.FindObjectsOfTypeAll{T}"/>는 비활성 오브젝트까지 주는 대신 프리팹 에셋과
    /// 에디터 내부 오브젝트도 함께 줍니다. 그것들까지 목록에 올리면 같은 이름이 수십 개 늘어서 못 씁니다.
    /// </remarks>
    private static bool IsSceneObject(Component component)
    {
        if (component == null || component.gameObject.hideFlags != HideFlags.None)
        {
            return false;
        }

        return component.gameObject.scene.IsValid();
    }

    private void OnGUI()
    {
        DrawToolbar();

        m_tabIndex = GUILayout.Toolbar(m_tabIndex, BuildTabTitles());
        EditorGUILayout.Space(2.0f);

        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        DrawEntries();
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("다시 훑기", EditorStyles.toolbarButton, GUILayout.Width(70.0f)))
            {
                Rescan();
            }

            bool includeInactive = GUILayout.Toggle(
                m_includeInactive, "비활성 포함", EditorStyles.toolbarButton, GUILayout.Width(80.0f));

            if (includeInactive != m_includeInactive)
            {
                m_includeInactive = includeInactive;
                Rescan();
            }

            GUILayout.Space(6.0f);
            m_filter = GUILayout.TextField(m_filter, EditorStyles.toolbarSearchField);

            if (GUILayout.Button("전부 끄기", EditorStyles.toolbarButton, GUILayout.Width(70.0f)))
            {
                TurnOffAllBoolsInCurrentTab();
            }
        }
    }

    /// <summary>탭 이름에 항목 수를 붙여 만듭니다.</summary>
    private string[] BuildTabTitles()
    {
        string[] titles = new string[DebugSectionRegistry.TabNames.Length];

        for (int i = 0; i < titles.Length; i++)
        {
            string tab = DebugSectionRegistry.TabNames[i];
            titles[i] = $"{tab} ({m_entries.Count(entry => entry.TabName == tab)})";
        }

        return titles;
    }

    private void DrawEntries()
    {
        string activeTab = DebugSectionRegistry.TabNames[
            Mathf.Clamp(m_tabIndex, 0, DebugSectionRegistry.TabNames.Length - 1)];

        List<DebugFieldEntry> visible = m_entries
            .Where(entry => entry.Owner != null && entry.TabName == activeTab)
            .Where(PassesFilter)
            .ToList();

        if (visible.Count == 0)
        {
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(m_filter) ? "이 탭에 표시할 디버그 항목이 없습니다." : "검색 결과가 없습니다.",
                EditorStyles.miniLabel);
            return;
        }

        Component currentOwner = null;

        foreach (DebugFieldEntry entry in visible)
        {
            if (entry.Owner != currentOwner)
            {
                currentOwner = entry.Owner;
                DrawOwnerHeader(currentOwner);
            }

            DrawEntryField(entry);
        }
    }

    private bool PassesFilter(DebugFieldEntry entry)
    {
        if (string.IsNullOrEmpty(m_filter))
        {
            return true;
        }

        return entry.Owner.gameObject.name.IndexOf(m_filter, StringComparison.OrdinalIgnoreCase) >= 0
            || entry.Owner.GetType().Name.IndexOf(m_filter, StringComparison.OrdinalIgnoreCase) >= 0
            || entry.Field.Name.IndexOf(m_filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void DrawOwnerHeader(Component owner)
    {
        EditorGUILayout.Space(4.0f);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                $"{owner.gameObject.name}  ·  {owner.GetType().Name}", EditorStyles.boldLabel);

            if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(40.0f)))
            {
                Selection.activeGameObject = owner.gameObject;
                EditorGUIUtility.PingObject(owner.gameObject);
            }
        }
    }

    private void DrawEntryField(DebugFieldEntry entry)
    {
        SerializedObject serialized = GetSerializedObject(entry.Owner);
        if (serialized == null)
        {
            return;
        }

        serialized.UpdateIfRequiredOrScript();

        SerializedProperty property = serialized.FindProperty(entry.Field.Name);
        if (property == null)
        {
            return;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(property, true);

        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        // SerializedObject 경로라 Undo와 프리팹 오버라이드 표시가 그대로 따라옵니다.
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(entry.Owner);
        SceneView.RepaintAll();
    }

    private SerializedObject GetSerializedObject(Component owner)
    {
        if (owner == null)
        {
            return null;
        }

        if (m_serializedCache.TryGetValue(owner, out SerializedObject cached) && cached.targetObject != null)
        {
            return cached;
        }

        SerializedObject created = new SerializedObject(owner);
        m_serializedCache[owner] = created;
        return created;
    }

    /// <summary>
    /// 현재 탭에 보이는 bool 항목을 모두 끕니다.
    /// </summary>
    /// <remarks>
    /// 여러 개를 켜 놓고 씬이 기즈모로 뒤덮였을 때 되돌리는 용도입니다. bool만 건드리는 이유는
    /// 반지름이나 세그먼트 수 같은 수치까지 0으로 만들면 되돌릴 값을 잃기 때문입니다.
    /// </remarks>
    private void TurnOffAllBoolsInCurrentTab()
    {
        string activeTab = DebugSectionRegistry.TabNames[
            Mathf.Clamp(m_tabIndex, 0, DebugSectionRegistry.TabNames.Length - 1)];

        foreach (DebugFieldEntry entry in m_entries)
        {
            if (entry.Owner == null || entry.TabName != activeTab)
            {
                continue;
            }

            SerializedObject serialized = GetSerializedObject(entry.Owner);
            if (serialized == null)
            {
                continue;
            }

            serialized.UpdateIfRequiredOrScript();

            SerializedProperty property = serialized.FindProperty(entry.Field.Name);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean || !property.boolValue)
            {
                continue;
            }

            property.boolValue = false;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(entry.Owner);
        }

        SceneView.RepaintAll();
    }
}
