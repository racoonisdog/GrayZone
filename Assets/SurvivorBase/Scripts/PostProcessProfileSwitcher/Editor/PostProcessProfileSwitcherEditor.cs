using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PostProcessProfileSwitcher))]
public class PostProcessProfileSwitcherEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        PostProcessProfileSwitcher switcher = (PostProcessProfileSwitcher)target;

        GUILayout.Space(10);
        GUILayout.Label("Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Set Standard")) {
            switcher.SetProfile(switcher.standardProfile);
        }

        if (GUILayout.Button("Set Summer")) {
            switcher.SetProfile(switcher.summerProfile);
        }

        if (GUILayout.Button("Set Autumn")) {
            switcher.SetProfile(switcher.autumnProfile);
        }

        if (GUILayout.Button("Set Winter")) {
            switcher.SetProfile(switcher.winterProfile);
        }
    }
}
