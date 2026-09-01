using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Playerble Animator의 이동 상태 기계와 방향 블렌드 트리를 코드로 배선하는 에디터 도구입니다.
/// </summary>
/// <remarks>
/// 손으로 `.controller` YAML을 고치지 않는 이유는 FBX 안 애니메이션 클립의 내부 fileID를
/// 유니티가 임포트 시점에 생성하기 때문입니다. 그 값은 `.meta`에 남지 않아 바깥에서 재현할 수 없고,
/// 잘못 적으면 모션 참조가 조용히 비어 버립니다. 그래서 에셋 API로 실제 클립을 찾아 넣습니다.
/// 같은 이유로 <see cref="EnemyAnimatorWiring"/>도 이 방식을 씁니다.
///
/// 상태는 <c>IsAim</c> 하나로 두 갈래입니다. 걷기/달리기/웅크림은 <c>MoveState</c> 축으로,
/// 정지는 방향 2D의 중심(0,0)에 둔 정지 클립으로 가릅니다. 그래서 이동 단계가 늘어나도 상태 수는 그대로입니다.
/// Playerble에는 별도 Idle 블렌드가 없다는 전제가 이 구조의 근거입니다.
///
/// 3축 블렌드 트리는 유니티에 없습니다(1D / 2D 4종 / Direct뿐). 그래서 MoveState 1D 안에
/// 방향 2D를 중첩합니다. Direct는 자식별 가중치를 직접 먹이는 방식이라 3축 공간이 아닙니다.
///
/// 이 도구는 <c>Assets/3.Resources</c>(SVN 관리) 아래의 컨트롤러를 고칩니다. git 안전망이 없어
/// 실행 직전에 프로젝트 루트 <c>Backup/Animator</c>로 원본을 한 벌 복사합니다.
/// </remarks>
public static class PlayerAnimatorWiring
{
    private const string ControllerPath =
        "Assets/3.Resources/Animation/Playerble/PlayerInputsThirdPerson.controller";

    private const string ClipRoot = "Assets/3.Resources/Animation/Playerble/";
    private const string IdleFolder = "Idle";

    private const string MoveMachineName = "Move";
    private const string LegacyLocomotionStateName = "Idle Walk Run Blend";

    /// <summary>상태 전환에 쓰는 기본 전이 시간(초)입니다.</summary>
    private const float StateTransitionDuration = 0.15f;

    private const string ParamMoveX = "MoveX";
    private const string ParamMoveZ = "MoveZ";
    private const string ParamMoveState = "MoveState";
    private const string ParamIsAim = "IsAim";
    private const string ParamIsMove = "IsMove";

    /// <summary>
    /// MoveState 축의 단계 값입니다. 인접한 값끼리만 섞이도록 순서를 잡았습니다.
    /// </summary>
    /// <remarks>
    /// 웅크림과 달리기를 축의 양 끝에 두는 이유는 그 둘이 직접 섞이면 안 되기 때문입니다.
    /// 가운데에 걷기가 있어 웅크림에서 달리기로 가는 입력도 걷기를 지나가며 이어집니다.
    /// </remarks>
    private static readonly float[] TierThresholds = { 0.0f, 1.0f, 2.0f };

    /// <summary>이동 방향 하나와 2D 블렌드 위치입니다.</summary>
    private readonly struct Direction
    {
        public readonly string Suffix;
        public readonly Vector2 Position;

        public Direction(string suffix, float x, float y)
        {
            Suffix = suffix;
            Position = new Vector2(x, y);
        }
    }

    // 정면이 +Y(MoveZ), 오른쪽이 +X(MoveX)입니다. 대각은 단위원 위에 둡니다.
    private const float Diagonal = 0.7071f;

    private static readonly Direction[] EightWay =
    {
        new Direction("F",     0.0f,      1.0f),
        new Direction("R45",   Diagonal,  Diagonal),
        new Direction("R",     1.0f,      0.0f),
        new Direction("R135",  Diagonal, -Diagonal),
        new Direction("B",     0.0f,     -1.0f),
        new Direction("L135", -Diagonal, -Diagonal),
        new Direction("L",    -1.0f,      0.0f),
        new Direction("L45",  -Diagonal,  Diagonal),
    };

    // 웅크림 세트에는 정측면(L/R) 클립이 없습니다. 90도 입력은 45도와 135도가 섞여 채웁니다.
    private static readonly Direction[] CrouchSixWay =
    {
        new Direction("F",     0.0f,      1.0f),
        new Direction("R45",   Diagonal,  Diagonal),
        new Direction("R135",  Diagonal, -Diagonal),
        new Direction("B",     0.0f,     -1.0f),
        new Direction("L135", -Diagonal, -Diagonal),
        new Direction("L45",  -Diagonal,  Diagonal),
    };

    // 달리기는 전방 세 방향만 있습니다. 뒤/옆 달리기는 기획상 없는 동작이라
    // ThirdPersonController가 전력질주를 전방 부채꼴로 제한해 이 범위를 벗어나지 않게 합니다.
    private static readonly Direction[] RunForwardFan =
    {
        new Direction("F",     0.0f,     1.0f),
        new Direction("R45",   Diagonal, Diagonal),
        new Direction("L45",  -Diagonal, Diagonal),
    };

    /// <summary>방향 블렌드 트리 하나의 구성입니다.</summary>
    private readonly struct TreeSpec
    {
        public readonly string TreeName;
        public readonly string Folder;
        public readonly string ClipPrefix;
        public readonly string CenterClip;
        public readonly Direction[] Directions;
        public readonly string Note;

        public TreeSpec(
            string treeName, string folder, string clipPrefix,
            string centerClip, Direction[] directions, string note = null)
        {
            TreeName = treeName;
            Folder = folder;
            ClipPrefix = clipPrefix;
            CenterClip = centerClip;
            Directions = directions;
            Note = note;
        }
    }

    // 중심(0,0)에 정지 클립을 넣습니다. 이동을 시작하거나 멈추는 순간 방향값이 0을 지나는데,
    // 중심이 비어 있으면 그 순간 값이 튑니다. 정지 상태 자체는 IsMove로 갈린 별도 상태가 담당합니다.
    private static readonly TreeSpec[] TreeSpecs =
    {
        new TreeSpec("Default Rifle Crouch", "Crouch_Walk", "R_Crouch_Walk_",    "R_Crouch_Idle",    CrouchSixWay),
        new TreeSpec("Default Rifle Walk",   "Walk",        "R_StrafeWalk_",     "R_Idle",           EightWay),
        new TreeSpec("Default Rifle Run",    "Run",         "R_StrafeRun_",      "R_Idle",           RunForwardFan),
        new TreeSpec("Aim Rifle Crouch",     "Crouch_Aim",  "R_Crouch_AimWalk_", "R_Crouch_AimIdle", CrouchSixWay),
        new TreeSpec("Aim Rifle Walk",       "Walk_Aim",    "R_AimWalk_",        "R_AimIdle",        EightWay),
        new TreeSpec("Aim Rifle Run",        "Run",         "R_StrafeRun_",      "R_AimIdle",        RunForwardFan,
            "조준 달리기 클립이 없어 비조준 달리기 클립을 씁니다. 전투 자세는 전력질주가 잠기므로 실제로는 도달하지 않는 단계입니다."),
    };

    /// <summary>Move 상태 기계 안의 상태 하나입니다.</summary>
    private readonly struct StateSpec
    {
        public readonly string StateName;
        public readonly bool Aim;

        /// <summary>MoveState 1D에 단계 순서대로 넣을 방향 트리 이름입니다.</summary>
        public readonly string[] TierTrees;

        public StateSpec(string stateName, bool aim, params string[] tierTrees)
        {
            StateName = stateName;
            Aim = aim;
            TierTrees = tierTrees;
        }
    }

    private static readonly StateSpec[] StateSpecs =
    {
        new StateSpec("Default Move Blend", false,
            "Default Rifle Crouch", "Default Rifle Walk", "Default Rifle Run"),

        new StateSpec("Aim Move Blend", true,
            "Aim Rifle Crouch", "Aim Rifle Walk", "Aim Rifle Run"),
    };

    [MenuItem("GrayZone/Player/이동 상태 기계와 블렌드 트리 배선")]
    public static void Wire()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[PlayerAnimatorWiring] 컨트롤러를 찾지 못했습니다: {ControllerPath}");
            return;
        }

        List<string> log = new List<string>();

        if (!TryBackupController(log))
        {
            Debug.LogError("[PlayerAnimatorWiring] 백업에 실패해 아무것도 고치지 않았습니다.\n" + string.Join("\n", log));
            return;
        }

        EnsureParameter(controller, ParamMoveX, AnimatorControllerParameterType.Float, log);
        EnsureParameter(controller, ParamMoveZ, AnimatorControllerParameterType.Float, log);

        // MoveState는 정수처럼 쓰지만 형식은 Float입니다. 블렌드 트리 축은 Float만 받습니다.
        EnsureParameter(controller, ParamMoveState, AnimatorControllerParameterType.Float, log);

        EnsureParameter(controller, ParamIsAim, AnimatorControllerParameterType.Bool, log);
        EnsureParameter(controller, ParamIsMove, AnimatorControllerParameterType.Bool, log);

        AnimatorStateMachine root = controller.layers[0].stateMachine;
        AnimatorStateMachine move = FindStateMachine(root, MoveMachineName);

        if (move == null)
        {
            Debug.LogError($"[PlayerAnimatorWiring] '{MoveMachineName}' 상태 기계를 찾지 못했습니다.");
            return;
        }

        Dictionary<string, BlendTree> trees = BuildDirectionalTrees(controller, log);
        Dictionary<string, AnimatorState> states = BuildMoveStates(controller, move, trees, log);

        RemoveObsoleteStates(root, move, states, log);
        WireStateSwitching(states, log);
        AdoptLegacyLocomotionWiring(root, move, states, log);
        SyncExternalOutgoing(states, log);
        WireLayerEntry(root, move, log);
        RemoveOrphanBlendTrees(controller, log);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[PlayerAnimatorWiring] 완료\n" + string.Join("\n", log));
    }

    /// <summary>
    /// 컨트롤러 원본을 프로젝트 루트 Backup/Animator 아래로 복사합니다.
    /// </summary>
    /// <returns>백업에 성공했으면 true입니다.</returns>
    /// <remarks>
    /// 이 컨트롤러는 SVN 관리 대상이라 git으로 되돌릴 수 없습니다. 배선이 어긋났을 때
    /// 되돌릴 지점이 없으면 손으로 다시 그려야 하므로, 고치기 전에 한 벌 남깁니다.
    /// </remarks>
    private static bool TryBackupController(List<string> log)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string directory = Path.Combine(projectRoot, "Backup", "Animator");
            Directory.CreateDirectory(directory);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string destination = Path.Combine(
                directory, $"{Path.GetFileNameWithoutExtension(ControllerPath)}_{stamp}.controller");

            File.Copy(Path.GetFullPath(ControllerPath), destination, false);
            log.Add($"백업했습니다: {destination}");
            return true;
        }
        catch (Exception exception)
        {
            log.Add($"백업 실패: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 이름 붙은 방향 블렌드 트리 6개를 2D로 바꾸고 클립을 채웁니다.
    /// </summary>
    private static Dictionary<string, BlendTree> BuildDirectionalTrees(
        AnimatorController controller, List<string> log)
    {
        Dictionary<string, BlendTree> existing = CollectBlendTrees(controller);
        Dictionary<string, BlendTree> result = new Dictionary<string, BlendTree>();

        foreach (TreeSpec spec in TreeSpecs)
        {
            if (!existing.TryGetValue(spec.TreeName, out BlendTree tree))
            {
                tree = CreateTree(controller, spec.TreeName);
                log.Add($"{spec.TreeName} 트리를 새로 만들었습니다.");
            }

            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = ParamMoveX;
            tree.blendParameterY = ParamMoveZ;

            // 기존 자식을 비우고 다시 채웁니다. 같은 클립이 중복으로 쌓이는 것을 막습니다.
            tree.children = new ChildMotion[0];

            List<string> missing = new List<string>();

            AnimationClip center = FindClip(IdleFolder, spec.CenterClip, log);
            if (center != null)
            {
                tree.AddChild(center, Vector2.zero);
            }
            else
            {
                missing.Add(spec.CenterClip);
            }

            int added = 0;

            foreach (Direction direction in spec.Directions)
            {
                string clipName = spec.ClipPrefix + direction.Suffix;
                AnimationClip clip = FindClip(spec.Folder, clipName, log);

                if (clip == null)
                {
                    missing.Add(clipName);
                    continue;
                }

                tree.AddChild(clip, direction.Position);
                added++;
            }

            string line = $"{spec.TreeName}: 2D Freeform Directional, 중심 {spec.CenterClip}, 방향 {added}/{spec.Directions.Length}개";

            if (missing.Count > 0)
            {
                line += $" / 찾지 못한 클립: {string.Join(", ", missing)}";
            }

            if (!string.IsNullOrEmpty(spec.Note))
            {
                line += $" / {spec.Note}";
            }

            log.Add(line);
            result[spec.TreeName] = tree;
        }

        return result;
    }

    /// <summary>
    /// Move 상태 기계 안의 네 상태를 만들고 각자의 MoveState 1D 트리를 물립니다.
    /// </summary>
    private static Dictionary<string, AnimatorState> BuildMoveStates(
        AnimatorController controller,
        AnimatorStateMachine move,
        Dictionary<string, BlendTree> trees,
        List<string> log)
    {
        Dictionary<string, AnimatorState> states = new Dictionary<string, AnimatorState>();

        for (int i = 0; i < StateSpecs.Length; i++)
        {
            StateSpec spec = StateSpecs[i];
            AnimatorState state = FindChildState(move, spec.StateName);

            if (state == null)
            {
                state = move.AddState(spec.StateName, new Vector3(420.0f, 20.0f + i * 80.0f, 0.0f));
                log.Add($"{spec.StateName} 상태를 새로 만들었습니다.");
            }

            state.motion = BuildTierTree(controller, state, spec, trees, log);
            states[spec.StateName] = state;
        }

        // 기본 상태는 비조준 정지입니다. 진입 프레임의 파라미터가 무엇이든 한 번의 전이로 알맞은 상태에 갑니다.
        move.defaultState = states[StateSpecs[0].StateName];
        log.Add($"Move 기본 상태를 {StateSpecs[0].StateName}으로 두었습니다.");

        return states;
    }

    /// <summary>
    /// 상태에 물릴 MoveState 1D 트리를 만듭니다.
    /// </summary>
    /// <remarks>
    /// 이름으로 찾지 않습니다. 컨트롤러 안에 'Blend Tree'라는 같은 이름이 여러 개 있어 이름 조회로는
    /// 어느 것이 이 상태의 것인지 가려낼 수 없습니다. 상태가 이미 물고 있는 것을 재사용합니다.
    /// 단, 그것이 방향 트리 중 하나라면 재사용하면 안 됩니다. 방향 트리를 1D로 덮어써 버립니다.
    /// </remarks>
    private static Motion BuildTierTree(
        AnimatorController controller,
        AnimatorState state,
        StateSpec spec,
        Dictionary<string, BlendTree> trees,
        List<string> log)
    {
        string outerName = spec.StateName + " Tier";
        BlendTree outer = state.motion as BlendTree;

        if (outer == null || trees.ContainsValue(outer))
        {
            outer = CreateTree(controller, outerName);
        }

        outer.name = outerName;
        outer.blendType = BlendTreeType.Simple1D;
        outer.blendParameter = ParamMoveState;

        // 1D에서 Y축은 쓰이지 않지만, 재사용한 트리에 없는 파라미터 이름이 남아 있으면
        // 애니메이터가 그 이름을 찾다가 경고를 냅니다. 같은 축으로 덮어 둡니다.
        outer.blendParameterY = ParamMoveState;
        outer.useAutomaticThresholds = false;
        outer.children = new ChildMotion[0];

        string[] tierNames = spec.TierTrees;
        List<string> missing = new List<string>();

        for (int i = 0; i < tierNames.Length; i++)
        {
            float threshold = TierThresholds[Mathf.Min(i, TierThresholds.Length - 1)];

            if (trees.TryGetValue(tierNames[i], out BlendTree child))
            {
                outer.AddChild(child, threshold);
            }
            else
            {
                missing.Add(tierNames[i]);
            }
        }

        string line = $"{spec.StateName}: MoveState 1D(웅크림 0 / 걷기 1 / 달리기 2)로 {tierNames.Length - missing.Count}단계를 묶었습니다.";

        if (missing.Count > 0)
        {
            line += $" / 찾지 못한 항목: {string.Join(", ", missing)}";
        }

        log.Add(line);

        return outer;
    }

    /// <summary>
    /// 새 구성에 없는 옛 Move 하위 상태를 제거합니다.
    /// </summary>
    /// <remarks>
    /// 가리키는 전이를 먼저 걷어내지 않으면 상태가 사라진 뒤 그 전이에 손댈 때 파괴된 객체 참조가 됩니다.
    ///
    /// 모션을 먼저 떼어내는 것도 같은 이유입니다. <see cref="AnimatorStateMachine.RemoveState"/>는
    /// 상태가 물고 있던 블렌드 트리를 컨트롤러 하위 에셋째로 파괴합니다. 옛 웅크림 상태가 물고 있던 트리는
    /// 새 구조에서도 단계 트리의 자식으로 쓰는 것이라, 떼지 않고 지우면 그 참조가 끊어집니다.
    /// 실측에서 MissingReferenceException으로 드러났습니다.
    /// </remarks>
    private static void RemoveObsoleteStates(
        AnimatorStateMachine root,
        AnimatorStateMachine move,
        Dictionary<string, AnimatorState> states,
        List<string> log)
    {
        List<AnimatorState> obsolete = new List<AnimatorState>();

        foreach (ChildAnimatorState child in move.states)
        {
            if (child.state != null && !states.ContainsValue(child.state))
            {
                obsolete.Add(child.state);
            }
        }

        foreach (AnimatorState state in obsolete)
        {
            string name = state.name;

            // 옛 상태를 가리키던 전이는 Move 상태 기계로 돌려, 진입 시점 파라미터로 알맞은 상태에 가게 합니다.
            int redirected = RedirectTransitions(root, state, move);
            ClearOutgoing(state);
            state.motion = null;
            move.RemoveState(state);

            log.Add($"쓰이지 않는 상태 '{name}'을 제거했습니다. 가리키던 전이 {redirected}개는 Move로 돌렸습니다.");
        }
    }

    /// <summary>
    /// 두 상태 사이를 IsAim으로 오갈 수 있게 전이를 놓습니다.
    /// </summary>
    private static void WireStateSwitching(Dictionary<string, AnimatorState> states, List<string> log)
    {
        // 상태끼리 오가는 전이만 걷어냅니다. 바깥으로 나가는 전이는 따로 관리합니다.
        foreach (StateSpec spec in StateSpecs)
        {
            AnimatorState from = states[spec.StateName];
            AnimatorStateTransition[] existing = from.transitions;

            for (int i = existing.Length - 1; i >= 0; i--)
            {
                AnimatorState destination = existing[i].destinationState;

                if (destination != null && states.ContainsValue(destination))
                {
                    from.RemoveTransition(existing[i]);
                }
            }
        }

        int count = 0;

        foreach (StateSpec fromSpec in StateSpecs)
        {
            foreach (StateSpec toSpec in StateSpecs)
            {
                if (fromSpec.StateName == toSpec.StateName)
                {
                    continue;
                }

                AnimatorStateTransition transition = states[fromSpec.StateName]
                    .AddTransition(states[toSpec.StateName]);

                transition.hasExitTime = false;
                transition.hasFixedDuration = true;
                transition.duration = StateTransitionDuration;
                transition.canTransitionToSelf = false;

                // 목적지 상태가 성립하는 조건을 그대로 씁니다. 조건이 상태의 정의와 같아야
                // 어느 상태에서 출발해도 같은 곳에 도착합니다.
                transition.AddCondition(
                    toSpec.Aim ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0.0f, ParamIsAim);

                count++;
            }
        }

        log.Add($"Move 안 상태 전이 {count}개를 놓았습니다. (IsAim)");
    }

    /// <summary>
    /// 기존 지상 이동 상태의 들어오는/나가는 전이를 Move가 물려받게 하고 그 상태를 제거합니다.
    /// </summary>
    /// <remarks>
    /// 이미 이관이 끝난 뒤 다시 불리면 아무 일도 하지 않습니다. 나가는 전이 유지는
    /// <see cref="SyncExternalOutgoing"/>가 이어서 담당합니다.
    /// </remarks>
    private static void AdoptLegacyLocomotionWiring(
        AnimatorStateMachine root,
        AnimatorStateMachine move,
        Dictionary<string, AnimatorState> states,
        List<string> log)
    {
        AnimatorState legacy = FindState(root, LegacyLocomotionStateName);

        if (legacy == null)
        {
            return;
        }

        int copied = 0;

        foreach (AnimatorStateTransition source in legacy.transitions)
        {
            foreach (StateSpec spec in StateSpecs)
            {
                if (TryCopyTransition(source, states[spec.StateName]))
                {
                    copied++;
                }
            }
        }

        int redirected = RedirectTransitions(root, legacy, move);

        ClearOutgoing(legacy);
        RemoveState(root, legacy);

        log.Add($"'{LegacyLocomotionStateName}'을 제거했습니다. 나가는 전이 복제 {copied}개, 들어오는 전이 {redirected}개를 Move로 돌렸습니다.");
    }

    /// <summary>
    /// 바깥으로 나가는 전이를 네 상태에 모두 같게 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 점프나 기립처럼 이동 단계와 무관한 전이는 어느 상태에 있어도 걸려야 합니다. 서브 상태 기계의
    /// Exit로 묶으면 전이 수는 줄지만, 어느 상태에서 나갔는지에 따라 조건이 달라지지 않는 지금 구조에서는
    /// 복제가 읽기 쉽고 예측 가능합니다.
    ///
    /// 새로 만든 상태에는 이 전이가 없으므로, 이미 갖고 있는 상태에서 본을 떠 옵니다.
    /// 같은 목적지와 같은 조건 조합은 한 번만 놓아 다시 실행해도 늘지 않습니다.
    /// </remarks>
    private static void SyncExternalOutgoing(Dictionary<string, AnimatorState> states, List<string> log)
    {
        List<AnimatorStateTransition> templates = new List<AnimatorStateTransition>();
        HashSet<string> collected = new HashSet<string>();

        foreach (AnimatorState state in states.Values)
        {
            foreach (AnimatorStateTransition transition in state.transitions)
            {
                if (IsInternal(transition, states))
                {
                    continue;
                }

                if (collected.Add(DescribeTransition(transition)))
                {
                    templates.Add(transition);
                }
            }
        }

        int added = 0;

        foreach (AnimatorState state in states.Values)
        {
            HashSet<string> present = new HashSet<string>();

            foreach (AnimatorStateTransition transition in state.transitions)
            {
                if (!IsInternal(transition, states))
                {
                    present.Add(DescribeTransition(transition));
                }
            }

            foreach (AnimatorStateTransition template in templates)
            {
                if (present.Contains(DescribeTransition(template)))
                {
                    continue;
                }

                if (TryCopyTransition(template, state))
                {
                    added++;
                }
            }
        }

        log.Add($"바깥으로 나가는 전이를 맞췄습니다. 본 {templates.Count}종, 추가 {added}개.");
    }

    /// <summary>전이의 목적지가 네 상태 안쪽인지 확인합니다.</summary>
    private static bool IsInternal(AnimatorStateTransition transition, Dictionary<string, AnimatorState> states)
    {
        return transition.destinationState != null && states.ContainsValue(transition.destinationState);
    }

    /// <summary>목적지와 조건 조합으로 전이를 식별하는 문자열을 만듭니다.</summary>
    private static string DescribeTransition(AnimatorStateTransition transition)
    {
        string destination = transition.destinationState != null
            ? "S:" + transition.destinationState.name
            : transition.destinationStateMachine != null
                ? "M:" + transition.destinationStateMachine.name
                : "Exit";

        List<string> conditions = new List<string>();

        foreach (AnimatorCondition condition in transition.conditions)
        {
            conditions.Add($"{condition.parameter}:{condition.mode}:{condition.threshold}");
        }

        conditions.Sort();

        return destination + "|" + string.Join(",", conditions);
    }

    /// <summary>
    /// Base Layer의 진입점을 Move 상태 기계로 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 반드시 상태 제거 뒤에 불러야 합니다. <see cref="AnimatorStateMachine.RemoveState"/>는 지운 것이
    /// 기본 상태였을 때 남은 상태 중 하나를 자동으로 기본으로 올립니다.
    /// </remarks>
    private static void WireLayerEntry(AnimatorStateMachine root, AnimatorStateMachine move, List<string> log)
    {
        foreach (AnimatorTransition transition in root.entryTransitions)
        {
            if (transition.destinationStateMachine == move)
            {
                return;
            }
        }

        root.AddEntryTransition(move);
        log.Add("Base Layer 진입 전이를 Move 상태 기계로 놓았습니다.");
    }

    /// <summary>
    /// 전이 하나를 다른 상태에 같은 설정으로 복제합니다.
    /// </summary>
    /// <returns>복제했으면 true, 목적지가 비어 있어 건너뛰었으면 false입니다.</returns>
    private static bool TryCopyTransition(AnimatorStateTransition source, AnimatorState from)
    {
        AnimatorStateTransition copy;

        if (source.destinationState != null)
        {
            copy = from.AddTransition(source.destinationState);
        }
        else if (source.destinationStateMachine != null)
        {
            copy = from.AddTransition(source.destinationStateMachine);
        }
        else
        {
            return false;
        }

        copy.hasExitTime = source.hasExitTime;
        copy.exitTime = source.exitTime;
        copy.hasFixedDuration = source.hasFixedDuration;
        copy.duration = source.duration;
        copy.offset = source.offset;
        copy.interruptionSource = source.interruptionSource;
        copy.orderedInterruption = source.orderedInterruption;
        copy.canTransitionToSelf = source.canTransitionToSelf;

        foreach (AnimatorCondition condition in source.conditions)
        {
            copy.AddCondition(condition.mode, condition.threshold, condition.parameter);
        }

        return true;
    }

    /// <summary>
    /// 지정한 상태를 가리키는 모든 전이의 목적지를 상태 기계로 바꿉니다.
    /// </summary>
    private static int RedirectTransitions(
        AnimatorStateMachine machine, AnimatorState target, AnimatorStateMachine destination)
    {
        int count = 0;

        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state == null || child.state == target)
            {
                continue;
            }

            foreach (AnimatorStateTransition transition in child.state.transitions)
            {
                if (transition.destinationState != target)
                {
                    continue;
                }

                transition.destinationStateMachine = destination;
                count++;
            }
        }

        foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
        {
            if (transition.destinationState != target)
            {
                continue;
            }

            transition.destinationStateMachine = destination;
            count++;
        }

        foreach (AnimatorTransition transition in machine.entryTransitions)
        {
            if (transition.destinationState != target)
            {
                continue;
            }

            transition.destinationStateMachine = destination;
            count++;
        }

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            count += RedirectTransitions(sub.stateMachine, target, destination);
        }

        return count;
    }

    /// <summary>상태에서 나가는 전이를 모두 지웁니다.</summary>
    private static void ClearOutgoing(AnimatorState state)
    {
        AnimatorStateTransition[] existing = state.transitions;

        for (int i = existing.Length - 1; i >= 0; i--)
        {
            state.RemoveTransition(existing[i]);
        }
    }

    /// <summary>상태 기계와 그 하위에서 지정한 상태를 지웁니다.</summary>
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
    /// 어느 상태에서도 닿지 않는 블렌드 트리 하위 에셋을 지웁니다.
    /// </summary>
    /// <remarks>
    /// 상태를 지울 때 모션을 떼어 두기 때문에(공유 트리를 함께 파괴하지 않으려고) 쓰이지 않는 트리가 남습니다.
    /// 컨트롤러 안에서만 도달성을 따지므로, 다른 레이어가 쓰고 있는 트리는 지우지 않습니다.
    /// </remarks>
    private static void RemoveOrphanBlendTrees(AnimatorController controller, List<string> log)
    {
        HashSet<Motion> reachable = new HashSet<Motion>();

        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            if (layer.stateMachine != null)
            {
                CollectReachableMotions(layer.stateMachine, reachable);
            }
        }

        string path = AssetDatabase.GetAssetPath(controller);
        List<string> removed = new List<string>();

        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is not BlendTree tree || reachable.Contains(tree))
            {
                continue;
            }

            removed.Add(string.IsNullOrEmpty(tree.name) ? "(이름 없음)" : tree.name);
            UnityEngine.Object.DestroyImmediate(tree, true);
        }

        if (removed.Count > 0)
        {
            log.Add($"쓰이지 않는 블렌드 트리 {removed.Count}개를 지웠습니다: {string.Join(", ", removed)}");
        }
    }

    /// <summary>상태 기계에서 닿을 수 있는 모션을 모읍니다. 블렌드 트리는 자식까지 내려갑니다.</summary>
    private static void CollectReachableMotions(AnimatorStateMachine machine, HashSet<Motion> reachable)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null)
            {
                CollectMotion(child.state.motion, reachable);
            }
        }

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            if (sub.stateMachine != null)
            {
                CollectReachableMotions(sub.stateMachine, reachable);
            }
        }
    }

    /// <summary>모션 하나와 그 하위 모션을 모읍니다.</summary>
    private static void CollectMotion(Motion motion, HashSet<Motion> reachable)
    {
        if (motion == null || !reachable.Add(motion))
        {
            return;
        }

        if (motion is not BlendTree tree)
        {
            return;
        }

        foreach (ChildMotion child in tree.children)
        {
            CollectMotion(child.motion, reachable);
        }
    }

    /// <summary>새 블렌드 트리를 만들어 컨트롤러의 하위 에셋으로 붙입니다.</summary>
    private static BlendTree CreateTree(AnimatorController controller, string name)
    {
        BlendTree tree = new BlendTree { name = name, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    /// <summary>컨트롤러 안의 모든 블렌드 트리를 이름으로 모읍니다.</summary>
    private static Dictionary<string, BlendTree> CollectBlendTrees(AnimatorController controller)
    {
        Dictionary<string, BlendTree> found = new Dictionary<string, BlendTree>();

        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)))
        {
            if (asset is BlendTree tree && !found.ContainsKey(tree.name))
            {
                found.Add(tree.name, tree);
            }
        }

        return found;
    }

    /// <summary>직속 자식 상태에서 이름이 같은 것을 찾습니다.</summary>
    private static AnimatorState FindChildState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == name)
            {
                return child.state;
            }
        }

        return null;
    }

    /// <summary>상태 기계와 그 하위에서 이름이 같은 상태를 찾습니다.</summary>
    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        AnimatorState direct = FindChildState(machine, name);

        if (direct != null)
        {
            return direct;
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

    /// <summary>상태 기계와 그 하위에서 이름이 같은 상태 기계를 찾습니다.</summary>
    private static AnimatorStateMachine FindStateMachine(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            if (sub.stateMachine == null)
            {
                continue;
            }

            if (sub.stateMachine.name == name)
            {
                return sub.stateMachine;
            }

            AnimatorStateMachine found = FindStateMachine(sub.stateMachine, name);

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

    /// <summary>FBX 파일 하나에서 애니메이션 클립을 찾습니다.</summary>
    /// <remarks>
    /// FBX 하나에 여러 하위 에셋이 들어 있어 경로만으로는 클립을 집을 수 없습니다.
    /// 이름이 파일명과 같으면 그것을 쓰고, 아니면 그 파일의 유일한 클립을 씁니다.
    ///
    /// 이름 일치만 요구하면 안 되는 이유는 임포트 설정의 클립 이름이 파일명과 갈린 파일이 섞여 있기 때문입니다.
    /// 실측에서 <c>Walk</c> 폴더의 4개(R45, R, R135, L135)가 원본 테이크 이름(<c>Strafe_Walk_FR</c> 등)을
    /// 그대로 들고 있어 조용히 누락됐습니다. FBX 하나에 클립이 하나뿐이라 이름은 보조 단서로만 씁니다.
    ///
    /// 미리보기용 숨은 클립(<c>__preview__</c>)은 제외합니다.
    /// </remarks>
    private static AnimationClip FindClip(string folder, string clipName, List<string> log = null)
    {
        string path = ClipRoot + folder + "/" + clipName + ".fbx";

        AnimationClip fallback = null;
        int candidates = 0;

        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is not AnimationClip clip || clip.name.StartsWith("__preview__"))
            {
                continue;
            }

            if (clip.name == clipName)
            {
                return clip;
            }

            fallback = clip;
            candidates++;
        }

        if (fallback == null)
        {
            return null;
        }

        if (candidates > 1)
        {
            log?.Add($"{clipName}: 이름이 맞는 클립이 없고 후보가 {candidates}개라 첫 번째({fallback.name})를 씁니다. 임포트 설정 확인이 필요합니다.");
        }
        else
        {
            log?.Add($"{clipName}: 내부 클립 이름이 '{fallback.name}'입니다. 파일명과 달라 이름 대신 파일로 찾았습니다.");
        }

        return fallback;
    }
}
