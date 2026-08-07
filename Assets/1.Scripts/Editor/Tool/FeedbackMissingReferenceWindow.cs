using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 무기·감염체·표면 피드백 데이터의 누락 참조를 한 화면에서 검사하는 Editor 전용 창입니다.
/// </summary>
/// <remarks>
/// 프로젝트의 Feedback 에셋, <c>Assets/2.Prefabs</c>의 프리팹, 현재 열린 씬을 읽기 전용으로 검사합니다.
/// 결과 행의 버튼으로 문제를 소유한 컴포넌트 또는 피드백 에셋을 선택하고 Inspector를 바로 열 수 있습니다.
/// </remarks>
public sealed class FeedbackMissingReferenceWindow : EditorWindow
{
    private enum FeedbackCategory
    {
        Weapon,
        Enemy,
        Surface
    }

    /// <summary>검사에서 발견한 누락 한 건입니다.</summary>
    private sealed class MissingIssue
    {
        /// <summary>무기·감염체·표면 중 어느 계열의 누락인지입니다.</summary>
        public FeedbackCategory category;

        /// <summary>누락을 소유한 컴포넌트나 에셋의 표시 이름입니다.</summary>
        public string ownerLabel;

        /// <summary>비어 있는 항목의 표시 이름입니다.</summary>
        public string fieldLabel;

        /// <summary>어느 프리팹·씬·에셋에서 발견했는지 알려 주는 경로입니다.</summary>
        public string contextPath;

        /// <summary>누락을 소유한 컴포넌트입니다. 결과 행의 소유자 버튼이 이것을 선택합니다.</summary>
        public Object ownerTarget;

        /// <summary>참조가 비어 있는 피드백 에셋입니다. 에셋 자체를 못 찾았으면 <c>null</c>입니다.</summary>
        public Object profileTarget;
    }

    private readonly List<MissingIssue> m_issues = new List<MissingIssue>();
    private readonly HashSet<int> m_scannedFeedbackIds = new HashSet<int>();

    [SerializeField] private string m_search = string.Empty;
    [SerializeField] private bool m_showWeapon = true;
    [SerializeField] private bool m_showEnemy = true;
    [SerializeField] private bool m_showSurface = true;

    private Vector2 m_scroll;
    private bool m_projectChanged;
    private int m_weaponOwnerCount;
    private int m_enemyOwnerCount;
    private int m_surfaceOwnerCount;
    private int m_feedbackCount;

    [MenuItem("Tools/GrayZone/피드백 누락 검사")]
    private static void Open()
    {
        FeedbackMissingReferenceWindow window = GetWindow<FeedbackMissingReferenceWindow>("피드백 누락 검사");
        window.minSize = new Vector2(660.0f, 380.0f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.projectChanged += HandleProjectChanged;
        RefreshAudit();
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= HandleProjectChanged;
    }

    private void HandleProjectChanged()
    {
        m_projectChanged = true;
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawSummary();
        DrawFilters();

        if (m_projectChanged)
        {
            EditorGUILayout.HelpBox("프로젝트 에셋이 변경되었습니다. 최신 상태를 보려면 새로고침하세요.", MessageType.Info);
        }

        if (m_issues.Count == 0)
        {
            EditorGUILayout.HelpBox("검사 범위에서 비어 있는 피드백 할당 항목을 찾지 못했습니다.", MessageType.Info);
            return;
        }

        DrawIssues();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("시청각 피드백 할당 상태", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("첫 누락 열기", EditorStyles.toolbarButton, GUILayout.Width(84.0f)))
            {
                MissingIssue first = FindFirstVisibleIssue();
                if (first != null)
                {
                    OpenInspector(first.profileTarget != null ? first.profileTarget : first.ownerTarget);
                }
            }

            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(68.0f)))
            {
                RefreshAudit();
            }
        }
    }

    private void DrawSummary()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUIStyle countStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15
            };

            Color previous = GUI.color;
            GUI.color = m_issues.Count > 0 ? new Color(1.0f, 0.65f, 0.65f) : new Color(0.65f, 1.0f, 0.7f);
            EditorGUILayout.LabelField($"누락 {m_issues.Count}건", countStyle);
            GUI.color = previous;

            EditorGUILayout.LabelField(
                $"검사 대상: 무기 {m_weaponOwnerCount} · 감염체 {m_enemyOwnerCount} · 표면 시스템 {m_surfaceOwnerCount} · 피드백 {m_feedbackCount}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "범위: Assets/2.Prefabs, 현재 열린 씬, 프로젝트의 Weapon/Enemy/Surface Feedback 에셋",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void DrawFilters()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("검색", GUILayout.Width(32.0f));
            m_search = EditorGUILayout.TextField(m_search ?? string.Empty);

            m_showWeapon = GUILayout.Toggle(m_showWeapon, "무기", EditorStyles.miniButtonLeft, GUILayout.Width(58.0f));
            m_showEnemy = GUILayout.Toggle(m_showEnemy, "감염체", EditorStyles.miniButtonMid, GUILayout.Width(68.0f));
            m_showSurface = GUILayout.Toggle(m_showSurface, "표면", EditorStyles.miniButtonRight, GUILayout.Width(58.0f));
        }
    }

    private void DrawIssues()
    {
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);

        FeedbackCategory? lastCategory = null;
        int visibleCount = 0;
        foreach (MissingIssue issue in m_issues)
        {
            if (!IsVisible(issue))
            {
                continue;
            }

            if (lastCategory != issue.category)
            {
                EditorGUILayout.Space(4.0f);
                EditorGUILayout.LabelField(GetCategoryLabel(issue.category), EditorStyles.boldLabel);
                lastCategory = issue.category;
            }

            DrawIssue(issue);
            visibleCount++;
        }

        if (visibleCount == 0)
        {
            EditorGUILayout.HelpBox("현재 필터와 일치하는 누락 항목이 없습니다.", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private static void DrawIssue(MissingIssue issue)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUIContent icon = EditorGUIUtility.IconContent("console.warnicon.sml");
                GUILayout.Label(icon, GUILayout.Width(20.0f), GUILayout.Height(20.0f));

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField($"{issue.ownerLabel}  ·  {issue.fieldLabel}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(issue.contextPath, EditorStyles.wordWrappedMiniLabel);
                }

                using (new EditorGUI.DisabledScope(issue.ownerTarget == null))
                {
                    if (GUILayout.Button("소유자 Inspector", GUILayout.Width(104.0f), GUILayout.Height(23.0f)))
                    {
                        OpenInspector(issue.ownerTarget);
                    }
                }

                using (new EditorGUI.DisabledScope(issue.profileTarget == null))
                {
                    if (GUILayout.Button("Feedback Inspector", GUILayout.Width(122.0f), GUILayout.Height(23.0f)))
                    {
                        OpenInspector(issue.profileTarget);
                    }
                }
            }
        }
    }

    private void RefreshAudit()
    {
        m_issues.Clear();
        m_scannedFeedbackIds.Clear();
        m_weaponOwnerCount = 0;
        m_enemyOwnerCount = 0;
        m_surfaceOwnerCount = 0;
        m_feedbackCount = 0;
        m_projectChanged = false;

        ScanFeedbackAssets();
        ScanPrefabs();
        ScanLoadedScenes();

        m_issues.Sort(CompareIssues);
        Repaint();
    }

    private void ScanFeedbackAssets()
    {
        ScanAssetsOfType<WeaponFeedbackSO>(feedback => AuditFeedback(FeedbackCategory.Weapon, feedback));
        ScanAssetsOfType<EnemyFeedbackSO>(feedback => AuditFeedback(FeedbackCategory.Enemy, feedback));
        ScanAssetsOfType<SurfaceFeedbackSO>(AuditSurfaceFeedback);
    }

    private void ScanAssetsOfType<T>(Action<T> audit) where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsExcludedPath(path))
            {
                continue;
            }

            T feedback = AssetDatabase.LoadAssetAtPath<T>(path);
            if (feedback != null)
            {
                audit(feedback);
            }
        }
    }

    private void ScanPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/2.Prefabs" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsExcludedPath(path))
            {
                continue;
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                continue;
            }

            ScanOwnerComponents(root, path);
        }
    }

    private void ScanLoadedScenes()
    {
        foreach (Gun gun in Resources.FindObjectsOfTypeAll<Gun>())
        {
            if (IsLoadedSceneObject(gun))
            {
                AuditGun(gun, GetSceneContext(gun));
            }
        }

        foreach (EnemyController enemy in Resources.FindObjectsOfTypeAll<EnemyController>())
        {
            if (IsLoadedSceneObject(enemy))
            {
                AuditEnemy(enemy, GetSceneContext(enemy));
            }
        }

        foreach (FieldManager fieldManager in Resources.FindObjectsOfTypeAll<FieldManager>())
        {
            if (IsLoadedSceneObject(fieldManager))
            {
                AuditFieldManager(fieldManager, GetSceneContext(fieldManager));
            }
        }

        foreach (EffectManager system in Resources.FindObjectsOfTypeAll<EffectManager>())
        {
            if (IsLoadedSceneObject(system))
            {
                AuditEffectManager(system, GetSceneContext(system));
            }
        }
    }

    private void ScanOwnerComponents(GameObject root, string path)
    {
        foreach (Gun gun in root.GetComponentsInChildren<Gun>(true))
        {
            AuditGun(gun, path);
        }

        foreach (EnemyController enemy in root.GetComponentsInChildren<EnemyController>(true))
        {
            AuditEnemy(enemy, path);
        }

        foreach (FieldManager fieldManager in root.GetComponentsInChildren<FieldManager>(true))
        {
            AuditFieldManager(fieldManager, path);
        }

        foreach (EffectManager system in root.GetComponentsInChildren<EffectManager>(true))
        {
            AuditEffectManager(system, path);
        }
    }

    /// <summary>무기의 피드백 배선을 검사합니다.</summary>
    /// <param name="gun">검사할 무기입니다.</param>
    /// <param name="context">결과에 표시할 위치 설명입니다.</param>
    /// <remarks>
    /// 피드백 리소스의 소유자는 <see cref="WeaponFeedbackEmitter"/>입니다.
    /// SO는 이미터의 개별 슬롯이나 엔티티 <see cref="SOBinder"/>의 통합 슬롯 중 한쪽에서 옵니다.
    /// </remarks>
    private void AuditGun(Gun gun, string context)
    {
        m_weaponOwnerCount++;

        WeaponFeedbackEmitter emitter = gun.FeedbackEmitter;
        if (emitter == null)
        {
            AddIssue(FeedbackCategory.Weapon, gun, null, "WeaponFeedbackEmitter 컴포넌트 없음", context);
            return;
        }

        WeaponFeedbackSO feedback = emitter.FeedbackSO;
        if (feedback == null)
        {
            SOBinder binder = gun.GetComponentInParent<SOBinder>(true);
            feedback = binder != null ? binder.SharedFeedback as WeaponFeedbackSO : null;
        }

        if (feedback == null)
        {
            AddIssue(FeedbackCategory.Weapon, emitter, null, "Weapon Feedback SO 미할당 (개별·통합 둘 다 비어 있음)", context);
            return;
        }

        AuditFeedback(FeedbackCategory.Weapon, feedback);
    }

    private void AuditEnemy(EnemyController enemy, string context)
    {
        m_enemyOwnerCount++;
        EnemyFeedbackSO feedback = enemy.Feedback;
        if (feedback == null)
        {
            AddIssue(FeedbackCategory.Enemy, enemy, null, "Enemy Feedback 미할당", context);
            return;
        }

        AuditFeedback(FeedbackCategory.Enemy, feedback);
    }

    private void AuditFieldManager(FieldManager fieldManager, string context)
    {
        if (fieldManager.EffectManager == null)
        {
            AddIssue(FeedbackCategory.Surface, fieldManager, null, "Surface Feedback System 컴포넌트 없음", context);
        }

        AudioManager fieldAudio = fieldManager.AudioManager;
        if (fieldAudio == null)
        {
            AddIssue(FeedbackCategory.Surface, fieldManager, null, "Field Audio System 컴포넌트 없음", context);
        }
        else if (fieldAudio.OutputMixerGroup == null)
        {
            AddIssue(FeedbackCategory.Surface, fieldAudio, null, "SFX AudioMixerGroup 미할당", context);
        }
    }

    private void AuditEffectManager(EffectManager system, string context)
    {
        m_surfaceOwnerCount++;

        if (system.DefaultSurfaceFeedback == null)
        {
            AddIssue(FeedbackCategory.Surface, system, null, "기본 표면 Feedback 미할당", context);
        }
        else
        {
            AuditSurfaceFeedback(system.DefaultSurfaceFeedback);
        }

        if (system.SurfaceFeedbacks == null || system.SurfaceFeedbacks.Count == 0)
        {
            AddIssue(FeedbackCategory.Surface, system, null, "표면별 Feedback 목록 비어 있음", context);
            return;
        }

        int nullCount = CountNullEntries(system.SurfaceFeedbacks);
        if (nullCount > 0)
        {
            AddIssue(FeedbackCategory.Surface, system, null, $"표면별 Feedback 목록에 빈 항목 {nullCount}개", context);
        }

        foreach (SurfaceFeedbackSO feedback in system.SurfaceFeedbacks)
        {
            if (feedback != null)
            {
                AuditSurfaceFeedback(feedback);
            }
        }
    }

    private void AuditSurfaceFeedback(SurfaceFeedbackSO feedback)
    {
        if (!BeginFeedbackAudit(feedback))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(feedback.SurfaceId))
        {
            AddIssue(FeedbackCategory.Surface, feedback, feedback, "표면 식별 이름 비어 있음", GetAssetContext(feedback));
        }

        AuditAttributedReferences(FeedbackCategory.Surface, feedback);
    }

    private void AuditFeedback(FeedbackCategory category, ScriptableObject feedback)
    {
        if (!BeginFeedbackAudit(feedback))
        {
            return;
        }

        AuditAttributedReferences(category, feedback);
    }

    private bool BeginFeedbackAudit(ScriptableObject feedback)
    {
        if (feedback == null || !m_scannedFeedbackIds.Add(feedback.GetInstanceID()))
        {
            return false;
        }

        m_feedbackCount++;
        return true;
    }

    private void AuditAttributedReferences(FeedbackCategory category, ScriptableObject feedback)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in feedback.GetType().GetFields(flags))
        {
            FeedbackReferenceAttribute attribute = field.GetCustomAttribute<FeedbackReferenceAttribute>(true);
            if (attribute == null)
            {
                continue;
            }

            string label = string.IsNullOrWhiteSpace(attribute.DisplayName) ? field.Name : attribute.DisplayName;
            object value = field.GetValue(feedback);
            if (typeof(Object).IsAssignableFrom(field.FieldType))
            {
                if (value as Object == null)
                {
                    AddIssue(category, feedback, feedback, $"{label} 비어 있음", GetAssetContext(feedback));
                }

                continue;
            }

            if (!(value is IEnumerable values))
            {
                Debug.LogWarning($"[FeedbackAudit] {feedback.GetType().Name}.{field.Name}은 참조 또는 참조 목록이 아닙니다.", feedback);
                continue;
            }

            int count = 0;
            int nullCount = 0;
            foreach (object entry in values)
            {
                count++;
                if (entry as Object == null)
                {
                    nullCount++;
                }
            }

            if (count == 0)
            {
                AddIssue(category, feedback, feedback, $"{label} 비어 있음", GetAssetContext(feedback));
            }
            else if (nullCount > 0)
            {
                AddIssue(category, feedback, feedback, $"{label}에 빈 항목 {nullCount}개", GetAssetContext(feedback));
            }
        }
    }

    private static int CountNullEntries<T>(IReadOnlyList<T> values) where T : Object
    {
        int count = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == null)
            {
                count++;
            }
        }

        return count;
    }

    private void AddIssue(
        FeedbackCategory category,
        Object owner,
        Object profile,
        string fieldLabel,
        string context)
    {
        m_issues.Add(new MissingIssue
        {
            category = category,
            ownerLabel = owner != null ? $"{owner.name} ({owner.GetType().Name})" : "Missing Owner",
            fieldLabel = fieldLabel,
            contextPath = string.IsNullOrEmpty(context) ? "(저장되지 않은 Editor 객체)" : context,
            ownerTarget = owner,
            profileTarget = profile
        });
    }

    private MissingIssue FindFirstVisibleIssue()
    {
        foreach (MissingIssue issue in m_issues)
        {
            if (IsVisible(issue))
            {
                return issue;
            }
        }

        return null;
    }

    private bool IsVisible(MissingIssue issue)
    {
        if ((issue.category == FeedbackCategory.Weapon && !m_showWeapon)
            || (issue.category == FeedbackCategory.Enemy && !m_showEnemy)
            || (issue.category == FeedbackCategory.Surface && !m_showSurface))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(m_search))
        {
            return true;
        }

        string search = m_search.Trim();
        return ContainsIgnoreCase(issue.ownerLabel, search)
               || ContainsIgnoreCase(issue.fieldLabel, search)
               || ContainsIgnoreCase(issue.contextPath, search);
    }

    private static bool ContainsIgnoreCase(string source, string value)
    {
        return source != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void OpenInspector(Object target)
    {
        if (target == null)
        {
            return;
        }

        Selection.activeObject = target;
        EditorGUIUtility.PingObject(target);
        EditorApplication.ExecuteMenuItem("Window/General/Inspector");
    }

    private static bool IsLoadedSceneObject(Component component)
    {
        return component != null
               && !EditorUtility.IsPersistent(component)
               && component.gameObject.scene.IsValid()
               && component.gameObject.scene.isLoaded;
    }

    private static string GetSceneContext(Component component)
    {
        string scenePath = component.gameObject.scene.path;
        if (string.IsNullOrEmpty(scenePath))
        {
            scenePath = component.gameObject.scene.name;
        }

        return $"{scenePath}  ·  {GetHierarchyPath(component.transform)}";
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static string GetAssetContext(Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        return string.IsNullOrEmpty(path) ? "(저장되지 않은 Feedback)" : path;
    }

    private static bool IsExcludedPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/Deprecated/", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("/Old/", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.StartsWith("Assets/3.Resources/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("Assets/4.ThirdParty/", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareIssues(MissingIssue left, MissingIssue right)
    {
        int category = left.category.CompareTo(right.category);
        if (category != 0)
        {
            return category;
        }

        int context = string.Compare(left.contextPath, right.contextPath, StringComparison.OrdinalIgnoreCase);
        if (context != 0)
        {
            return context;
        }

        return string.Compare(left.fieldLabel, right.fieldLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCategoryLabel(FeedbackCategory category)
    {
        switch (category)
        {
            case FeedbackCategory.Weapon:
                return "무기 피드백";
            case FeedbackCategory.Enemy:
                return "감염체 피드백";
            case FeedbackCategory.Surface:
                return "표면 피드백";
            default:
                return category.ToString();
        }
    }
}
