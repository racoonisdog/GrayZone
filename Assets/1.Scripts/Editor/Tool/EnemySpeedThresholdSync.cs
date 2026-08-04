using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 변이체 이동 블렌드 트리의 속도 임계값을 실제로 쓰이는 이동 속도에 다시 맞추는 에디터 도구입니다.
/// </summary>
/// <remarks>
/// 코드가 <c>MoveSpeed</c>에 넣는 값은 NavMeshAgent의 실제 속력(m/s)입니다. 임계값이 그 범위 밖이면
/// 블렌드 트리가 입력을 가장 가까운 임계값으로 붙여 버리므로, 아무리 달려도 대기 동작만 나오거나
/// 배회 중에 달리는 동작이 섞입니다. 값이 틀린 것이 아니라 축의 눈금이 틀린 것이라 눈으로 찾기 어렵습니다.
///
/// 속도 조정은 밸런스 작업이라 자주 일어나고 그때마다 이 눈금이 조용히 어긋납니다.
/// 그래서 배선 도구 안에 묻어 두지 않고 단독 메뉴로 뺐습니다. 속도를 만질 때마다 이것만 누르면 됩니다.
///
/// 속도를 0~1로 정규화해 넘기면 이 도구가 필요 없어지지만, 그러려면 최대 속도 기준을 따로 정해야 합니다.
/// 플레이어와 변이체가 같은 밸런스 값을 쓰지 않으므로 각자 실측 속도를 쓰는 편이 단순합니다.
/// </remarks>
public static class EnemySpeedThresholdSync
{
    private const string ControllerPath = "Assets/3.Resources/Animation/Enemy/Enemy.controller";
    private const string PrefabPath = "Assets/2.Prefabs/Enemy/Enemy(Test).prefab";

    [MenuItem("GrayZone/Enemy/이동 속도 임계값 교정")]
    public static void Sync()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[EnemySpeedThresholdSync] 컨트롤러를 찾지 못했습니다: {ControllerPath}");
            return;
        }

        List<string> log = new List<string>();

        if (!Apply(controller, log))
        {
            Debug.LogWarning("[EnemySpeedThresholdSync] 교정하지 못했습니다\n" + string.Join("\n", log));
            return;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("[EnemySpeedThresholdSync] 완료\n" + string.Join("\n", log));
    }

    /// <summary>
    /// 속도 축 블렌드 트리의 임계값을 실제 이동 속도로 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 호출자가 컨트롤러 저장을 맡습니다. 배선 도구가 여러 작업을 묶어 한 번에 저장하기 때문입니다.
    /// </remarks>
    /// <param name="controller">변이체 애니메이터 컨트롤러입니다.</param>
    /// <param name="log">수행 결과를 덧붙일 로그입니다.</param>
    /// <returns>임계값을 실제로 바꿨으면 true입니다.</returns>
    public static bool Apply(AnimatorController controller, List<string> log)
    {
        if (controller == null)
        {
            log.Add("컨트롤러가 null이라 임계값을 손대지 않았습니다.");
            return false;
        }

        if (!TryResolveSpeeds(log, out float wanderSpeed, out float chaseSpeed))
        {
            return false;
        }

        BlendTree root = FindSpeedTree(controller);
        if (root == null)
        {
            log.Add("속도 축 블렌드 트리를 찾지 못했습니다. 자식으로 Walk 트리를 가진 트리를 찾습니다.");
            return false;
        }

        root.blendParameter = "MoveSpeed";
        root.useAutomaticThresholds = false;

        // children은 값 복사본을 돌려주므로 배열을 고친 뒤 되돌려 넣어야 반영됩니다.
        ChildMotion[] children = root.children;
        List<string> before = new List<string>();
        List<string> after = new List<string>();

        for (int i = 0; i < children.Length; i++)
        {
            string name = children[i].motion != null ? children[i].motion.name : string.Empty;

            float target;
            if (name == "Idle") target = 0f;
            else if (name == "Walk") target = wanderSpeed;
            else if (name == "Run") target = chaseSpeed;
            else continue;

            before.Add($"{name} {children[i].threshold}");
            after.Add($"{name} {target}");
            children[i].threshold = target;
        }

        if (before.Count == 0)
        {
            log.Add("속도 축 트리에서 Idle/Walk/Run 자식을 찾지 못했습니다.");
            return false;
        }

        root.children = children;

        log.Add($"임계값: {string.Join(" / ", before)} -> {string.Join(" / ", after)}");
        return true;
    }

    /// <summary>
    /// 변이체가 실제로 쓰는 배회·추격 속도를 찾습니다.
    /// </summary>
    /// <remarks>
    /// 런타임과 같은 순서로 찾는 것이 핵심입니다. <see cref="EnemyController.ApplyBalance"/>는
    /// 밸런스 SO가 있을 때만 컴포넌트 필드를 덮어쓰고, 없으면 프리팹에 저작된 값이 그대로 쓰입니다.
    /// 따라서 SO를 무조건 정본으로 삼으면 SO가 미할당인 동안 실제 속도와 어긋난 눈금을 만듭니다.
    /// 실제로 그렇게 어긋난 적이 있어 배회 중에 달리는 동작이 섞였습니다.
    /// </remarks>
    private static bool TryResolveSpeeds(List<string> log, out float wanderSpeed, out float chaseSpeed)
    {
        wanderSpeed = 0f;
        chaseSpeed = 0f;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            log.Add($"프리팹을 찾지 못했습니다: {PrefabPath}");
            return false;
        }

        EnemyController enemy = prefab.GetComponentInChildren<EnemyController>(true);
        if (enemy == null)
        {
            log.Add($"프리팹에서 {nameof(EnemyController)}를 찾지 못했습니다: {PrefabPath}");
            return false;
        }

        // 직렬화된 참조를 읽는 것이라 공개 접근자가 없어도 SerializedObject로 볼 수 있습니다.
        SerializedObject serialized = new SerializedObject(enemy);
        SerializedProperty balanceProperty = serialized.FindProperty("m_balanceSO");
        EnemyBalanceSO balance = balanceProperty != null
            ? balanceProperty.objectReferenceValue as EnemyBalanceSO
            : null;

        if (balance != null)
        {
            wanderSpeed = balance.WanderSpeed;
            chaseSpeed = balance.ChaseSpeed;
            log.Add($"속도 출처: 밸런스 SO {balance.name} (배회 {wanderSpeed} / 추격 {chaseSpeed})");
            return true;
        }

        wanderSpeed = enemy.WanderSpeed;
        chaseSpeed = enemy.ChaseSpeed;
        log.Add($"속도 출처: 프리팹 직렬화 값 (배회 {wanderSpeed} / 추격 {chaseSpeed})"
                + " — 밸런스 SO가 미할당이라 ApplyBalance가 실행되지 않습니다.");
        return true;
    }

    /// <summary>속도 축 블렌드 트리를 찾습니다.</summary>
    /// <remarks>이름이 아니라 구조로 찾습니다. 자식으로 Walk 트리를 들고 있는 것이 속도 축입니다.</remarks>
    private static BlendTree FindSpeedTree(AnimatorController controller)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)))
        {
            if (asset is not BlendTree tree)
            {
                continue;
            }

            foreach (ChildMotion child in tree.children)
            {
                if (child.motion != null && child.motion.name == "Walk")
                {
                    return tree;
                }
            }
        }

        return null;
    }
}
