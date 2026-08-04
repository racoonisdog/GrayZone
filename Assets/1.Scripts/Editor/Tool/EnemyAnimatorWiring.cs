using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 변이체 Animator의 이동 블렌드 트리와 관련 파라미터를 코드로 배선하는 에디터 도구입니다.
/// </summary>
/// <remarks>
/// 손으로 `.controller` 파일을 고치지 않는 이유는 FBX 안 애니메이션 클립의 내부 fileID를
/// 유니티가 임포트 시점에 생성하기 때문입니다. 그 값은 `.meta`에 남지 않아 바깥에서 재현할 수 없고,
/// 잘못 적으면 모션 참조가 조용히 비어 버립니다. 그래서 에셋 API로 실제 클립을 찾아 넣습니다.
///
/// 방향 클립은 현재 보유한 전방 반원 5종만 사용합니다. 후방 3종은 큐레이션 폴더에 없습니다.
/// 공용 `변이체 잡몹 1 콘텐츠` 문서도 방향 클립을 최소 범위에서 제외하고 있어, 보유분에 맞춥니다.
/// </remarks>
public static class EnemyAnimatorWiring
{
    private const string ControllerPath = "Assets/3.Resources/Animation/Enemy/Enemy.controller";
    private const string ClipFolder = "Assets/3.Resources/Animation/Enemy/";

    /// <summary>이동 방향 하나에 대응하는 클립과 블렌드 위치입니다.</summary>
    private readonly struct DirectionEntry
    {
        public readonly string FbxName;
        public readonly string ClipName;
        public readonly Vector2 Position;

        public DirectionEntry(string fbxName, string clipName, float x, float y)
        {
            FbxName = fbxName;
            ClipName = clipName;
            Position = new Vector2(x, y);
        }
    }

    // 전방 반원 5방향. 정면이 +Y, 오른쪽이 +X입니다.
    private static readonly DirectionEntry[] WalkDirections =
    {
        new DirectionEntry("WW_WalkForward",      "WalkForward",       0.0f,  1.0f),
        new DirectionEntry("WW_WalkForwardLeft",  "WalkForwardLeft",  -0.7f,  0.7f),
        new DirectionEntry("WW_WalkForwardRight", "WalkForwardRight",  0.7f,  0.7f),
        new DirectionEntry("WW_WalkLeft",         "WalkLeft",         -1.0f,  0.0f),
        new DirectionEntry("WW_WalkRight",        "WalkRight",         1.0f,  0.0f),
    };

    private static readonly DirectionEntry[] RunDirections =
    {
        new DirectionEntry("WW_RunForward",      "RunForward",       0.0f,  1.0f),
        new DirectionEntry("WW_RunForwardLeft",  "RunForwardLeft",  -0.7f,  0.7f),
        new DirectionEntry("WW_RunForwardRight", "RunForwardRight",  0.7f,  0.7f),
        new DirectionEntry("WW_RunLeft",         "RunLeft",         -1.0f,  0.0f),
        new DirectionEntry("WW_RunRight",        "RunRight",         1.0f,  0.0f),
    };

    [MenuItem("GrayZone/Enemy/이동 블렌드 트리 배선")]
    public static void Wire()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"컨트롤러를 찾지 못했습니다: {ControllerPath}");
            return;
        }

        List<string> log = new List<string>();

        EnsureParameter(controller, "IdleType", AnimatorControllerParameterType.Float, log);
        EnsureParameter(controller, "MoveX", AnimatorControllerParameterType.Float, log);
        EnsureParameter(controller, "MoveY", AnimatorControllerParameterType.Float, log);

        Dictionary<string, BlendTree> trees = CollectBlendTrees(controller);

        // Idle은 좀비마다 고정된 임의 값으로 두 대기 동작을 섞습니다. 축은 속도가 아니라 그 값입니다.
        if (trees.TryGetValue("Idle", out BlendTree idle))
        {
            idle.blendType = BlendTreeType.Simple1D;
            idle.blendParameter = "IdleType";
            log.Add($"Idle 트리 축을 IdleType으로 바꿨습니다. 자식 {idle.children.Length}개는 그대로 둡니다.");
        }
        else
        {
            log.Add("Idle 블렌드 트리를 찾지 못했습니다.");
        }

        WireDirectional(trees, "Walk", WalkDirections, log);
        WireDirectional(trees, "Run", RunDirections, log);
        EnemySpeedThresholdSync.Apply(controller, log);
        WireAttackSides(controller, log);
        FixAttackTransitions(controller, log);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[EnemyAnimatorWiring] 완료\n" + string.Join("\n", log));
    }

    /// <summary>공격 스테이트 하나와 그에 쓸 좌우 클립입니다.</summary>
    private readonly struct AttackEntry
    {
        public readonly string StateName;
        public readonly string FbxName;
        public readonly string LeftClip;
        public readonly string RightClip;

        public AttackEntry(string stateName, string fbxName, string leftClip, string rightClip)
        {
            StateName = stateName;
            FbxName = fbxName;
            LeftClip = leftClip;
            RightClip = rightClip;
        }
    }

    private static readonly AttackEntry[] Attacks =
    {
        new AttackEntry("Attack_Combo1", "WW_Attack_LeftSwipe01",
                        "Attack_LeftSwipe01_L", "Attack_LeftSwipe01_R"),
        new AttackEntry("Attack_Combo2", "WW_Attack_UppercutSwipe",
                        "Attack_UppercutSwipe_L", "Attack_UppercutSwipe_R"),
    };

    /// <summary>
    /// 공격 스테이트를 좌우 클립을 고르는 블렌드 트리로 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 같은 동작을 좌우로 번갈아 보여 주려는 것입니다. 좌우 클립은 같은 FBX에서 나오며,
    /// 오른쪽은 임포트 설정의 좌우반전을 켠 것이라 파일이 늘지 않습니다.
    ///
    /// 어느 쪽을 쓸지는 <c>AttackSide</c>가 정합니다. 0이면 왼쪽, 1이면 오른쪽입니다.
    /// 중간값을 넣으면 두 동작이 섞여 어색하므로 코드가 0이나 1만 넣습니다.
    /// </remarks>
    private static void WireAttackSides(AnimatorController controller, List<string> log)
    {
        EnsureParameter(controller, "AttackSide", AnimatorControllerParameterType.Float, log);

        AnimatorStateMachine root = controller.layers[0].stateMachine;

        foreach (AttackEntry entry in Attacks)
        {
            AnimatorState state = ResolveAttackState(root, entry.StateName, log);
            if (state == null)
            {
                log.Add($"{entry.StateName} 스테이트를 찾지 못했습니다.");
                continue;
            }

            AnimationClip left = FindClip(entry.FbxName, entry.LeftClip);
            AnimationClip right = FindClip(entry.FbxName, entry.RightClip);

            if (left == null || right == null)
            {
                log.Add($"{entry.StateName}: 좌우 클립을 찾지 못했습니다. "
                        + $"{entry.LeftClip}={(left != null ? "O" : "X")} {entry.RightClip}={(right != null ? "O" : "X")}");
                continue;
            }

            BlendTree tree = state.motion as BlendTree;
            if (tree == null)
            {
                tree = new BlendTree { name = entry.StateName, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, controller);
                state.motion = tree;
            }

            tree.name = entry.StateName;
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "AttackSide";
            tree.useAutomaticThresholds = false;
            tree.children = new ChildMotion[0];
            tree.AddChild(left, 0f);
            tree.AddChild(right, 1f);

            log.Add($"{entry.StateName}: {entry.LeftClip}(0) / {entry.RightClip}(1) 블렌드 트리로 바꿨습니다.");
        }
    }

    /// <summary>
    /// 공격 스테이트를 하나로 정리하고 그것을 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 같은 이름의 스테이트를 새로 만들면 유니티가 뒤에 번호를 붙입니다.
    /// 블렌드 트리로 다시 만든 스테이트가 그렇게 <c>이름 0</c>으로 남아 있어,
    /// 클립 하나만 물고 있던 옛 스테이트를 지우고 이름을 넘겨받게 합니다.
    ///
    /// 판단 기준은 이름이 아니라 모션의 종류입니다. 블렌드 트리를 물고 있는 쪽이 새 것입니다.
    /// </remarks>
    private static AnimatorState ResolveAttackState(AnimatorStateMachine root, string name, List<string> log)
    {
        AnimatorState exact = FindState(root, name);
        AnimatorState numbered = FindState(root, name + " 0");

        if (numbered == null)
        {
            return exact;
        }

        // 둘 다 있으면 블렌드 트리를 가진 쪽을 남깁니다.
        bool exactIsTree = exact != null && exact.motion is BlendTree;
        bool numberedIsTree = numbered.motion is BlendTree;

        AnimatorState keep = numberedIsTree && !exactIsTree ? numbered : exact;
        AnimatorState drop = keep == numbered ? exact : numbered;

        if (drop != null)
        {
            // 지울 스테이트를 가리키는 전이를 먼저 걷어냅니다.
            // 남겨 두면 스테이트가 사라진 뒤 그 전이에 손댈 때 파괴된 객체 참조가 됩니다.
            ClearTransitionsTo(root, drop);
            ClearOutgoing(drop);

            string dropped = drop.name;
            RemoveState(root, drop);
            log.Add($"중복 스테이트 {dropped}을 제거했습니다.");
        }

        if (keep != null && keep.name != name)
        {
            keep.name = name;
            log.Add($"스테이트 이름을 {name}으로 바꿨습니다.");
        }

        return keep;
    }

    /// <summary>지정한 스테이트를 목적지로 삼는 전이를 모두 지웁니다.</summary>
    private static void ClearTransitionsTo(AnimatorStateMachine machine, AnimatorState target)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state == null || child.state == target)
            {
                continue;
            }

            AnimatorStateTransition[] list = child.state.transitions;
            for (int i = list.Length - 1; i >= 0; i--)
            {
                if (list[i].destinationState == target)
                {
                    child.state.RemoveTransition(list[i]);
                }
            }
        }

        AnimatorStateTransition[] any = machine.anyStateTransitions;
        for (int i = any.Length - 1; i >= 0; i--)
        {
            if (any[i].destinationState == target)
            {
                machine.RemoveAnyStateTransition(any[i]);
            }
        }

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            ClearTransitionsTo(sub.stateMachine, target);
        }
    }

    /// <summary>상태 머신과 그 하위에서 지정한 스테이트를 지웁니다.</summary>
    private static void RemoveState(AnimatorStateMachine machine, AnimatorState target)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state == target)
            {
                machine.RemoveState(target);
                return;
            }
        }

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            RemoveState(sub.stateMachine, target);
        }
    }

    /// <summary>
    /// 공격 스테이트 사이의 전이를 다시 놓습니다.
    /// </summary>
    /// <remarks>
    /// 2타에서 1타로 돌아가는 길이 없어 한 번 2타에 들어가면 갇히는 문제가 있었습니다.
    /// 실측에서 클립이 끝난 뒤에도 마지막 자세로 굳은 채 판정만 계속 나갔습니다.
    ///
    /// 두 공격은 하나의 취소 불가능한 연타가 아니므로 서로 오갈 수 있어야 합니다.
    /// 순번을 정하는 것은 코드이고 애니메이터는 그 순번을 따라가기만 합니다.
    /// 설계 근거: 공용 `변이체 잡몹 1 콘텐츠` §8.1(공통 구조), §8.3(패턴 B-2 진입 판단).
    /// </remarks>
    private static void FixAttackTransitions(AnimatorController controller, List<string> log)
    {
        AnimatorStateMachine root = controller.layers[0].stateMachine;

        AnimatorState combo1 = FindState(root, "Attack_Combo1");
        AnimatorState combo2 = FindState(root, "Attack_Combo2");
        AnimatorState move = FindState(root, "Move");

        if (combo1 == null || combo2 == null || move == null)
        {
            log.Add("공격 또는 이동 스테이트를 찾지 못해 전이를 손대지 않았습니다.");
            return;
        }

        // 공격 스테이트에서 나가는 전이를 모두 지우고 필요한 것만 다시 놓습니다.
        ClearOutgoing(combo1);
        ClearOutgoing(combo2);

        // 이동에서 공격으로 들어가는 전이는 지워진 옛 스테이트를 가리킬 수 있어 다시 놓습니다.
        AnimatorStateTransition[] fromMove = move.transitions;
        for (int i = fromMove.Length - 1; i >= 0; i--)
        {
            if (fromMove[i].destinationState == null || fromMove[i].destinationState.name.StartsWith("Attack_"))
            {
                move.RemoveTransition(fromMove[i]);
            }
        }

        AddTransition(move, combo1, new[]
        {
            (AnimatorConditionMode.If, "DoAttack", 0f),
            (AnimatorConditionMode.If, "IsAttack", 0f),
            (AnimatorConditionMode.Greater, "AttackCombo", 0f),
        }, log, "Move -> Attack_Combo1");

        // 1타 -> 2타 : 코드가 순번을 2로 올리고 다시 신호를 보냈을 때
        AddTransition(combo1, combo2, new[]
        {
            (AnimatorConditionMode.If, "DoAttack", 0f),
            (AnimatorConditionMode.Greater, "AttackCombo", 1f),
        }, log, "Attack_Combo1 -> Attack_Combo2");

        // 2타 -> 1타 : 이어지는 공격까지 끝내고 첫 공격부터 다시 시작할 때
        AddTransition(combo2, combo1, new[]
        {
            (AnimatorConditionMode.If, "DoAttack", 0f),
            (AnimatorConditionMode.Less, "AttackCombo", 2f),
        }, log, "Attack_Combo2 -> Attack_Combo1");

        // 공격이 끝나면 이동으로 돌아갑니다. 클립이 끝난 뒤 판정하도록 종료 시각을 둡니다.
        AddExitTransition(combo1, move, log, "Attack_Combo1 -> Move");
        AddExitTransition(combo2, move, log, "Attack_Combo2 -> Move");
    }

    /// <summary>스테이트에서 나가는 전이를 모두 지웁니다.</summary>
    private static void ClearOutgoing(AnimatorState state)
    {
        AnimatorStateTransition[] existing = state.transitions;
        for (int i = existing.Length - 1; i >= 0; i--)
        {
            state.RemoveTransition(existing[i]);
        }
    }

    /// <summary>조건을 갖춘 즉시 전이를 만듭니다.</summary>
    private static void AddTransition(
        AnimatorState from, AnimatorState to,
        (AnimatorConditionMode mode, string param, float threshold)[] conditions,
        List<string> log, string label)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = 0.1f;

        foreach ((AnimatorConditionMode mode, string param, float threshold) c in conditions)
        {
            t.AddCondition(c.mode, c.threshold, c.param);
        }

        log.Add($"{label} 전이를 놓았습니다.");
    }

    /// <summary>클립이 끝났을 때 빠져나가는 전이를 만듭니다.</summary>
    private static void AddExitTransition(AnimatorState from, AnimatorState to, List<string> log, string label)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = 0.95f;
        t.hasFixedDuration = true;
        t.duration = 0.15f;
        t.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsAttack");

        log.Add($"{label} 전이를 놓았습니다. (IsAttack 해제 시)");
    }

    /// <summary>상태 머신과 그 하위에서 이름이 같은 스테이트를 찾습니다.</summary>
    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == name)
            {
                return child.state;
            }
        }

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            AnimatorState found = FindState(sub.stateMachine, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>지정한 이름과 형식의 파라미터가 있는지 확인하고, 없거나 형식이 다르면 맞춥니다.</summary>
    private static void EnsureParameter(
        AnimatorController controller, string name, AnimatorControllerParameterType type, List<string> log)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name != name)
            {
                continue;
            }

            if (parameters[i].type == type)
            {
                log.Add($"파라미터 {name}은 이미 {type}입니다.");
                return;
            }

            // 형식이 다르면 지우고 다시 만듭니다. 형식만 바꾸는 API가 없습니다.
            controller.RemoveParameter(i);
            controller.AddParameter(name, type);
            log.Add($"파라미터 {name}을 {parameters[i].type}에서 {type}로 바꿨습니다.");
            return;
        }

        controller.AddParameter(name, type);
        log.Add($"파라미터 {name}({type})을 추가했습니다.");
    }

    /// <summary>컨트롤러 안의 모든 블렌드 트리를 이름으로 모읍니다.</summary>
    private static Dictionary<string, BlendTree> CollectBlendTrees(AnimatorController controller)
    {
        Dictionary<string, BlendTree> found = new Dictionary<string, BlendTree>();

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)))
        {
            if (asset is BlendTree tree && !found.ContainsKey(tree.name))
            {
                found.Add(tree.name, tree);
            }
        }

        return found;
    }

    /// <summary>방향 블렌드 트리를 2D로 바꾸고 보유한 방향 클립을 채웁니다.</summary>
    private static void WireDirectional(
        Dictionary<string, BlendTree> trees, string treeName, DirectionEntry[] entries, List<string> log)
    {
        if (!trees.TryGetValue(treeName, out BlendTree tree))
        {
            log.Add($"{treeName} 블렌드 트리를 찾지 못했습니다.");
            return;
        }

        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveY";

        // 기존 자식을 비우고 다시 채웁니다. 같은 클립이 중복으로 쌓이는 것을 막습니다.
        tree.children = new ChildMotion[0];

        int added = 0;
        List<string> missing = new List<string>();

        for (int i = 0; i < entries.Length; i++)
        {
            AnimationClip clip = FindClip(entries[i].FbxName, entries[i].ClipName);
            if (clip == null)
            {
                missing.Add(entries[i].ClipName);
                continue;
            }

            tree.AddChild(clip, entries[i].Position);
            added++;
        }

        log.Add($"{treeName} 트리를 2D Freeform Directional로 바꾸고 클립 {added}개를 넣었습니다."
                + (missing.Count > 0 ? $" 찾지 못한 클립: {string.Join(", ", missing)}" : string.Empty));
    }

    /// <summary>FBX 안에서 지정한 이름의 애니메이션 클립을 찾습니다.</summary>
    /// <remarks>
    /// FBX 하나에 여러 하위 에셋이 들어 있어 경로만으로는 클립을 집을 수 없습니다.
    /// 미리보기용으로 숨겨진 클립이 함께 잡히는 것을 피하려고 이름으로 걸러 냅니다.
    /// </remarks>
    private static AnimationClip FindClip(string fbxName, string clipName)
    {
        string path = ClipFolder + fbxName + ".fbx";

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && clip.name == clipName)
            {
                return clip;
            }
        }

        return null;
    }
}
