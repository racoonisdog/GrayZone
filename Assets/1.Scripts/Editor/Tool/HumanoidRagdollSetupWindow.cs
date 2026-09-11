using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Humanoid 캐릭터를 끌어다 놓고 버튼 한 번으로 래그돌을 구성하는 Editor 전용 창입니다.
/// </summary>
/// <remarks>
/// 프리팹 에셋과 씬 오브젝트를 모두 받습니다. 프리팹 에셋이면 프리팹을 열어 구성한 뒤 저장하고,
/// 씬 오브젝트면 그 자리에서 구성한 뒤 씬을 변경 상태로 표시합니다(저장은 하지 않습니다).
/// 실제 구성 로직은 <see cref="HumanoidRagdollBuilder"/>가 가지고 있으며, 이 창은 대상 선택과
/// 저장 방식, 로그 표시만 담당합니다.
/// </remarks>
public sealed class HumanoidRagdollSetupWindow : EditorWindow
{
    private const string DefaultTargetPath = "Assets/2.Prefabs/Enemy/Howler.prefab";

    private readonly List<string> m_log = new List<string>();

    [SerializeField] private GameObject m_target;

    private Vector2 m_scroll;
    private string m_summary = string.Empty;
    private MessageType m_summaryType = MessageType.Info;

    [MenuItem("Tools/GrayZone/래그돌 구성")]
    private static void Open()
    {
        HumanoidRagdollSetupWindow window = GetWindow<HumanoidRagdollSetupWindow>("래그돌 구성");
        window.minSize = new Vector2(520.0f, 420.0f);
        window.Show();
    }

    private void OnEnable()
    {
        if (m_target == null)
        {
            m_target = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultTargetPath);
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Humanoid 캐릭터에 래그돌 골격을 구성합니다.", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Humanoid 릭을 가진 프리팹이나 씬 오브젝트를 끌어다 놓고 [래그돌 구성]을 누르세요.\n"
            + "표준 Humanoid 뼈 매핑만 쓰므로 모델의 Transform 이름에는 의존하지 않습니다. "
            + "이미 래그돌이 있으면 기존 컴포넌트를 재사용하고 값만 교정합니다.",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        m_target = (GameObject)EditorGUILayout.ObjectField("대상", m_target, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            m_log.Clear();
            m_summary = string.Empty;
        }

        DrawTargetState();

        using (new EditorGUI.DisabledScope(!CanBuild()))
        {
            if (GUILayout.Button("래그돌 구성", GUILayout.Height(30.0f)))
            {
                Execute();
            }
        }

        if (!string.IsNullOrEmpty(m_summary))
        {
            EditorGUILayout.HelpBox(m_summary, m_summaryType);
        }

        DrawLog();
    }

    /// <summary>
    /// 대상의 종류와 미리 알 수 있는 문제를 표시합니다.
    /// </summary>
    /// <remarks>
    /// 버튼을 누르기 전에 "무엇에, 어디에 저장되는지"를 보여 주려는 것입니다.
    /// 프리팹 인스턴스에 그대로 구성하면 씬 오버라이드로만 남아 다른 인스턴스에는 적용되지 않으므로 짚어 줍니다.
    /// </remarks>
    private void DrawTargetState()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("Play Mode에서는 구성할 수 없습니다. Play를 멈추고 다시 시도하세요.", MessageType.Warning);
            return;
        }

        if (m_target == null)
        {
            EditorGUILayout.HelpBox("대상을 지정하세요.", MessageType.Info);
            return;
        }

        if (PrefabUtility.IsPartOfPrefabAsset(m_target))
        {
            EditorGUILayout.HelpBox(
                $"프리팹 에셋: {AssetDatabase.GetAssetPath(m_target)}\n구성 후 프리팹에 바로 저장됩니다.",
                MessageType.Info);
            return;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(m_target))
        {
            EditorGUILayout.HelpBox(
                "이 오브젝트는 프리팹 인스턴스입니다. 여기에 구성하면 씬 오버라이드로만 남아 "
                + "같은 프리팹의 다른 인스턴스에는 적용되지 않습니다. 원본 프리팹 에셋을 대상으로 삼는 편이 낫습니다.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox("씬 오브젝트입니다. 구성 후 씬이 변경 상태가 되며, 저장은 직접 하셔야 합니다.", MessageType.Info);
    }

    private bool CanBuild()
    {
        return m_target != null && !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private void DrawLog()
    {
        if (m_log.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"구성 로그 ({m_log.Count}줄)", EditorStyles.boldLabel);
            if (GUILayout.Button("복사", GUILayout.Width(60.0f)))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", m_log);
            }
        }

        m_scroll = EditorGUILayout.BeginScrollView(m_scroll, EditorStyles.helpBox);
        for (int i = 0; i < m_log.Count; i++)
        {
            EditorGUILayout.LabelField(m_log[i], EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.EndScrollView();
    }

    private void Execute()
    {
        m_log.Clear();
        m_summary = string.Empty;

        GameObject target = m_target;
        bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(target);

        m_log.Add($"대상: {target.name} ({(isPrefabAsset ? "프리팹 에셋" : "씬 오브젝트")})");

        bool success = isPrefabAsset
            ? BuildPrefabAsset(target)
            : BuildSceneObject(target);

        if (success)
        {
            m_summary = isPrefabAsset
                ? $"'{target.name}' 래그돌 구성을 프리팹에 저장했습니다. "
                    + "Play Mode에서 사망 전 Collider 비활성, 사망 후 물리 전환과 관절 꺾임을 확인하세요."
                : $"'{target.name}' 래그돌 구성을 마쳤습니다. 씬 저장은 직접 하셔야 합니다.";
            m_summaryType = MessageType.Info;
            Debug.Log($"[래그돌 구성] {target.name}: 성공 ({m_log.Count}줄 로그)", target);
        }
        else
        {
            m_summary = $"'{target.name}' 래그돌 구성에 실패했습니다. 아래 로그를 확인하세요. 대상은 바뀌지 않았습니다.";
            m_summaryType = MessageType.Error;
            Debug.LogError($"[래그돌 구성] {target.name}: 실패 - {(m_log.Count > 0 ? m_log[m_log.Count - 1] : "사유 없음")}", target);
        }
    }

    private bool BuildPrefabAsset(GameObject target)
    {
        string path = AssetDatabase.GetAssetPath(target);
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null)
        {
            m_log.Add($"프리팹을 열지 못했습니다: {path}");
            return false;
        }

        try
        {
            if (!HumanoidRagdollBuilder.Build(contents, m_log))
            {
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            AssetDatabase.SaveAssets();
            m_log.Add($"프리팹 저장 완료: {path}");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private bool BuildSceneObject(GameObject target)
    {
        Undo.RegisterFullObjectHierarchyUndo(target, "래그돌 구성");

        if (!HumanoidRagdollBuilder.Build(target, m_log))
        {
            return false;
        }

        EditorUtility.SetDirty(target);
        if (target.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(target.scene);
            m_log.Add($"씬 '{target.scene.name}'을 변경 상태로 표시했습니다.");
        }

        return true;
    }
}
