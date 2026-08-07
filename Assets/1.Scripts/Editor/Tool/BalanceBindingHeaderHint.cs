using UnityEditor;
using UnityEngine;

/// <summary>
/// 밸런스 주입 대상 컴포넌트의 인스펙터 머리말 아래에 한 줄 안내와 프리뷰 버튼을 붙입니다.
/// </summary>
/// <remarks>
/// 프리뷰 창만 있으면 "이 컴포넌트가 SO 지배를 받는지도 모르고 프리팹 값을 튜닝하는" 상태를 막지 못합니다.
/// 그렇다고 diff 목록을 인스펙터 위에 통째로 띄우면 필드가 아래에 있어 왕복해야 하므로,
/// <b>한 줄 안내와 버튼만</b> 둡니다. 읽을 것이 한 줄이라 왕복이 생기지 않습니다.
/// <para>
/// <see cref="Editor.finishedDefaultHeaderGUI"/>에 붙는 이유는 <c>CustomEditor</c>를 쓰지 않기 위해서입니다.
/// 이 프로젝트는 VInspector를 쓰고 대상 컴포넌트에 각자의 인스펙터 구성이 있어서,
/// 커스텀 에디터로 가로채면 기존 표시를 덮어쓰게 됩니다. 이 콜백은 기존 인스펙터를 그대로 두고 뒤에 덧그립니다.
/// </para>
/// </remarks>
[InitializeOnLoad]
internal static class BalanceBindingHeaderHint
{
    static BalanceBindingHeaderHint()
    {
        // 도메인 리로드마다 정적 생성자가 다시 도는데, 중복 구독이 쌓이면 같은 줄이 여러 번 그려집니다.
        Editor.finishedDefaultHeaderGUI -= Draw;
        Editor.finishedDefaultHeaderGUI += Draw;
    }

    /// <summary>대상 컴포넌트일 때만 안내 줄을 그립니다.</summary>
    /// <param name="editor">현재 머리말을 그린 에디터입니다.</param>
    private static void Draw(Editor editor)
    {
        if (editor == null || editor.targets == null || editor.targets.Length != 1)
        {
            return;
        }

        if (!(editor.target is Component component) || !(component is ISharedBalanceReceiver))
        {
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(BuildMessage(component), EditorStyles.miniLabel);

            if (GUILayout.Button("프리뷰", EditorStyles.miniButton, GUILayout.Width(56.0f)))
            {
                BalancePreviewWindow.Open(component.gameObject);
            }
        }
    }

    /// <summary>어느 계층의 SO가 이 컴포넌트를 덮는지 한 줄로 설명합니다.</summary>
    /// <param name="component">검사할 컴포넌트입니다.</param>
    /// <returns>인스펙터에 그대로 표시할 문구입니다.</returns>
    /// <remarks>우선순위는 런타임과 같습니다. 개별 SO가 있으면 그것이 이기고, 없을 때만 통합이 옵니다.</remarks>
    private static string BuildMessage(Component component)
    {
        ScriptableObject own = BalanceReverseSync.FindBoundBalanceAsset(component);
        if (own != null)
        {
            return $"개별 SO '{own.name}'이 아래 값을 덮습니다.";
        }

        // 가장 가까운 조상 바인더가 이 컴포넌트가 속한 엔티티의 바인더입니다.
        SOBinder binder = component.GetComponentInParent<SOBinder>(true);
        if (binder != null && binder.SharedBalance != null)
        {
            return $"통합 SO '{binder.SharedBalance.name}'이 아래 값을 덮습니다.";
        }

        return "SO가 지정되지 않아 아래 값이 그대로 쓰입니다.";
    }
}
