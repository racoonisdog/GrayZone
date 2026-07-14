using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 전환 진입점입니다. 어떤 UI/시스템에서도 씬 이름만 지정해 재사용할 수 있습니다.
/// </summary>
public static class SceneTransitionController
{
    public static void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[SceneTransitionController] Scene name is empty.");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }
}
