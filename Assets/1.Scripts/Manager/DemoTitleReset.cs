using UnityEngine;

/// <summary>
/// '타이틀로 돌아가기'의 데모용 초기화입니다. 설정 화면 기타 탭에서 켜 두면, 타이틀로 갈 때 확인 팝업을 띄우고
/// '예'를 고르면 게임을 처음 켠 상태(설정 기본값, 저장 내역 없음, 진행 없음)로 되돌립니다.
/// </summary>
/// <remarks>
/// 설정은 기본값으로 되돌려 저장하고(데모용 초기화 값은 유지), 게임 저장 파일(game_save_*.json)을 지우고,
/// 씬을 넘어 유지되던 GameManager(게임 데이터·설정)를 지운 뒤 부트스트랩 씬부터 다시 시작합니다.
/// Destroy는 이번 프레임 끝에 처리되고 씬 로드는 그 뒤에 일어나므로, 부트스트랩의 새 GameManager가 남습니다.
/// </remarks>
public static class DemoTitleReset
{
    /// <summary>초기화할 때 불러올 첫 씬입니다. 이 씬이 새 GameManager를 만듭니다.</summary>
    public const string BootSceneName = "BootstrapScene";

    /// <summary>확인 팝업 문구입니다.</summary>
    public const string ConfirmMessage = "모든 설정과 저장내역을 초기화 하시겠습니까?(데모 모드)";

    /// <summary>데모용 초기화가 켜져 있으면 <c>true</c>입니다.</summary>
    public static bool IsEnabled => GameSettingManager.Instance != null && GameSettingManager.Instance.DemoResetOnTitle;

    /// <summary>
    /// 설정·저장 내역·진행을 초기화하고 다음에 불러올 씬 이름(부트스트랩)을 돌려줍니다.
    /// 부트스트랩 씬이 빌드에 없으면 초기화하지 않고 <paramref name="titleSceneName"/>을 돌려줍니다.
    /// </summary>
    public static string ResetAndGetNextScene(string titleSceneName, string bootSceneName = BootSceneName)
    {
        if (string.IsNullOrWhiteSpace(bootSceneName) || !Application.CanStreamedLevelBeLoaded(bootSceneName))
        {
            Debug.LogWarning($"[DemoTitleReset] 부트스트랩 씬 '{bootSceneName}'이 빌드 설정에 없어 초기화하지 않고 타이틀로 갑니다.");
            return titleSceneName;
        }

        GameSettingManager.Instance?.ResetToDefaultsAndSave();
        SaveFilePaths.DeleteAllGameSaves();
        if (GameManager.Instance != null)
        {
            Object.Destroy(GameManager.Instance.gameObject);
        }

        return bootSceneName;
    }
}
