using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 전환 진입점입니다. 어떤 UI/시스템에서도 씬 이름만 지정해 재사용할 수 있습니다.
/// </summary>
/// <remarks>
/// 가벼운 로딩 씬(<see cref="LoadingSceneName"/>)으로 먼저 넘어가 이전 씬을 내린 뒤,
/// 로딩 씬의 <see cref="LoadingSceneController"/>가 목표 씬을 비동기로 불러옵니다.
/// 이전 씬과 목표 씬이 메모리에 함께 올라가지 않게 하려는 구성입니다.
/// 로딩 씬이 Build Settings에 없으면 예전처럼 목표 씬을 바로 불러옵니다.
/// </remarks>
public static class SceneTransitionController
{
    /// <summary>중간에 거쳐 갈 로딩 씬 이름입니다.</summary>
    public const string LoadingSceneName = "LoadingScene";

    /// <summary>로딩 씬이 불러올 목표 씬 이름입니다. 로딩 씬이 읽고 비웁니다.</summary>
    public static string PendingSceneName { get; private set; }

    public static void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[SceneTransitionController] Scene name is empty.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(LoadingSceneName))
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        PendingSceneName = sceneName;
        SceneManager.LoadScene(LoadingSceneName);
    }

    /// <summary>로딩 씬이 목표 씬 이름을 가져가면서 비웁니다. 없으면 null입니다.</summary>
    public static string TakePendingSceneName()
    {
        string sceneName = PendingSceneName;
        PendingSceneName = null;
        return sceneName;
    }
}
