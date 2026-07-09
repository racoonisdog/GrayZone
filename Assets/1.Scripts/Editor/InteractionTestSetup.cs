#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrayZone.EditorTools
{
    /// <summary>
    /// 열린 씬에 상호작용 테스트 환경을 붙이는 에디터 유틸리티입니다.
    /// </summary>
    /// <remarks>
    /// unity-cli로 실행: <c>unity-cli --project . menu --menu_path "GrayZone/Interaction/Setup Test In Open Scene"</c>
    /// 이미 컴파일된 메뉴라 exec와 달리 즉시 실행됩니다. 씬은 dirty로만 표시하고 자동 저장하지 않습니다(플레이 테스트는 인메모리로 동작).
    /// </remarks>
    public static class InteractionTestSetup
    {
        [MenuItem("GrayZone/Interaction/Setup Test In Open Scene")]
        public static void Setup()
        {
            var inputs = Object.FindObjectsByType<PlayerInputs>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            int added = 0;
            foreach (var pi in inputs)
            {
                if (pi.GetComponent<InteractionController>() == null)
                {
                    Undo.AddComponent<InteractionController>(pi.gameObject);
                    added++;
                }
            }

            // 테스트 프롭(중복 생성 방지). Cube 프리미티브는 BoxCollider를 포함하므로 탐지 대상이 됩니다.
            GameObject prop = GameObject.Find("InteractionTestProp");
            if (prop == null)
            {
                prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prop.name = "InteractionTestProp";
                Undo.RegisterCreatedObjectUndo(prop, "Create InteractionTestProp");

                Vector3 pos = inputs.Length > 0
                    ? inputs[0].transform.position + inputs[0].transform.forward * 2.0f + Vector3.up * 0.5f
                    : new Vector3(0.0f, 0.5f, 0.0f);
                prop.transform.position = pos;
                prop.transform.localScale = Vector3.one * 0.6f;
            }

            if (prop.GetComponent<SampleInteractable>() == null)
            {
                Undo.AddComponent<SampleInteractable>(prop);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[InteractionTestSetup] InteractionController added to {added} player object(s) (of {inputs.Length}); test prop '{prop.name}' ready at {prop.transform.position}. Scene marked dirty (not auto-saved).");
        }
    }
}
#endif
