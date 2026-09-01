using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 변이체 애니메이터에서 지속 행동 스테이트가 코드 상태를 따라 빠져나오도록 전이를 고치는 도구입니다.
/// </summary>
/// <remarks>
/// 기준은 <b>코드가 길이를 정하는 행동은 `Do*` 트리거 + `Is*` bool</b>입니다.
///
/// `exitTime`(클립 종료)만으로 나가면 코드가 상태를 먼저 빠져나올 때 애니메이션이 남습니다.
/// 경직이 가장 심한데 코드 값은 0.35초이고 클립은 3초여서 8배 차이입니다.
/// 하울링도 길이를 밸런스로 줄이면 그만큼 포효 자세로 이동하고, 두리번은 인지 게이지가 차는 시점이
/// 소음량에 따라 제각각이라 애초에 클립 길이와 맞을 수 없습니다.
///
/// 손으로 `.controller`를 고치지 않는 이유는 FBX 안 클립의 내부 fileID를 유니티가 임포트 시점에
/// 만들기 때문입니다. 그 값은 `.meta`에 남지 않아 바깥에서 재현할 수 없고, 잘못 적으면 모션 참조가
/// 조용히 비어 버립니다. 선례는 <see cref="EnemyAnimatorWiring"/>입니다.
/// </remarks>
public static class EnemyStateExitWiring
{
    private const string ControllerPath = "Assets/3.Resources/Animation/Enemy/Enemy.controller";

    /// <summary>이탈 조건을 bool로 바꿀 스테이트 하나의 정의입니다.</summary>
    private readonly struct ExitRule
    {
        /// <summary>대상 스테이트 이름입니다.</summary>
        public readonly string StateName;

        /// <summary>이탈 조건으로 쓸 bool 파라미터 이름입니다.</summary>
        public readonly string BoolName;

        /// <summary>돌아갈 스테이트 이름입니다.</summary>
        public readonly string ReturnStateName;

        public ExitRule(string stateName, string boolName, string returnStateName)
        {
            StateName = stateName;
            BoolName = boolName;
            ReturnStateName = returnStateName;
        }
    }

    // Death는 넣지 않습니다. 되돌아오지 않는 전이라 트리거 단독이 맞습니다.
    // Attack_Combo1/2는 이미 IsAttack으로 나가므로 기준에 맞습니다.
    private static readonly ExitRule[] Rules =
    {
        new ExitRule("Howl", "IsHowl", "Move"),
        new ExitRule("Look Around", "IsLookAround", "Move"),
        new ExitRule("Stagger", "IsStagger", "Move"),
    };

    [MenuItem("GrayZone/Enemy/지속 행동 이탈 전이 정리")]
    public static void Wire()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[EnemyStateExitWiring] 컨트롤러를 찾지 못했습니다: {ControllerPath}");
            return;
        }

        List<string> log = new List<string>();
        AnimatorStateMachine root = controller.layers[0].stateMachine;

        foreach (ExitRule rule in Rules)
        {
            ApplyRule(controller, root, rule, log);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("[EnemyStateExitWiring] 완료\n" + string.Join("\n", log));
    }

    /// <summary>한 스테이트의 이탈 전이를 bool 조건으로 다시 놓습니다.</summary>
    private static void ApplyRule(
        AnimatorController controller, AnimatorStateMachine root, ExitRule rule, List<string> log)
    {
        AnimatorState state = FindState(root, rule.StateName);
        if (state == null)
        {
            log.Add($"{rule.StateName}: 스테이트를 찾지 못해 건너뜁니다.");
            return;
        }

        AnimatorState returnState = FindState(root, rule.ReturnStateName);
        if (returnState == null)
        {
            log.Add($"{rule.StateName}: 돌아갈 스테이트 {rule.ReturnStateName}을 찾지 못해 건너뜁니다.");
            return;
        }

        EnsureBoolParameter(controller, rule.BoolName, log);

        // 기존 이탈 전이를 걷어냅니다. exitTime 전이가 남아 있으면 클립 종료로도 나가 두 경로가 생깁니다.
        int removed = state.transitions.Length;
        AnimatorStateTransition[] existing = state.transitions;
        for (int i = existing.Length - 1; i >= 0; i--)
        {
            state.RemoveTransition(existing[i]);
        }

        AnimatorStateTransition transition = state.AddTransition(returnState);

        // 클립 종료를 기다리지 않습니다. 코드가 bool을 내리는 순간 나가는 것이 이 정리의 목적입니다.
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.15f;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0f, rule.BoolName);

        log.Add($"{rule.StateName}: 기존 전이 {removed}개 제거 -> {rule.ReturnStateName} [IfNot {rule.BoolName}] 추가");
    }

    /// <summary>지정한 이름의 bool 파라미터가 없으면 추가합니다.</summary>
    /// <remarks>형식이 다르면 지우고 다시 만듭니다. 형식만 바꾸는 API가 없습니다.</remarks>
    private static void EnsureBoolParameter(AnimatorController controller, string name, List<string> log)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name != name)
            {
                continue;
            }

            if (parameters[i].type == AnimatorControllerParameterType.Bool)
            {
                return;
            }

            controller.RemoveParameter(i);
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
            log.Add($"파라미터 {name}을 {parameters[i].type}에서 Bool로 바꿨습니다.");
            return;
        }

        controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        log.Add($"파라미터 {name}(Bool)을 추가했습니다.");
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
}
