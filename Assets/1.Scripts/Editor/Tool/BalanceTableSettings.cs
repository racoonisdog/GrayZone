using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 밸런스 시트 파이프라인에서 팀 전체가 같아야 하는 규칙을 모아 둡니다.
/// </summary>
/// <remarks>
/// 창에서 바꾸는 폴더 설정은 <c>EditorPrefs</c>에 저장되어 이 PC에만 남습니다. 출력 위치 같은 개인 취향은 그래도 되지만,
/// <b>어떤 SO가 시트에 들어가는가</b>와 <b>CSV가 자동으로 SO를 덮는가</b>는 사람마다 달라지면 안 됩니다.
/// 두 사람이 같은 타입을 내보냈는데 시트 내용이 달라지고, 그대로 커밋되기 때문입니다.
/// <para>
/// 그래서 이 두 가지는 코드 상수로 두어 Git에 함께 커밋되게 합니다. 바꾸려면 이 파일을 고쳐야 하고,
/// 그 변경은 코드 리뷰를 거칩니다. 그것이 "팀 공통 규칙"이라는 성질을 보장하는 가장 싼 방법입니다.
/// </para>
/// </remarks>
internal static class BalanceTableSettings
{
    /// <summary>
    /// CSV로 내보낼 SO를 찾을 폴더입니다. 이 밖에 있는 에셋은 시트에 들어가지 않습니다.
    /// </summary>
    /// <remarks>
    /// 테스트용으로 여러 벌 만든 SO가 최종 시트를 더럽히는 것을 막습니다.
    /// 바꿔 끼워 보려고 만든 임시 SO는 이 경로 밖(예: <c>Assets/5.Data/_Sandbox</c>)에 두면 됩니다.
    /// </remarks>
    public static readonly string[] ExportSearchFolders =
    {
        "Assets/5.Data/ScriptableObject"
    };

    /// <summary>
    /// CSV 폴더의 변경을 감지해 자동으로 SO에 반영할지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 현재 정본은 SO이고 CSV는 내보낸 스냅샷입니다. 이 상태에서 자동 가져오기가 살아 있으면
    /// 낡은 시트를 건드리는 것만으로 SO가 조용히 과거로 되돌아갑니다. 파일 이동이나 재임포트도 트리거가 됩니다.
    /// <para>
    /// 기획자가 실제로 시트를 돌리기 시작해 시트가 정본이 되면 그때 켭니다.
    /// 그때는 내보낸 시각 기록까지 함께 넣어 "SO가 CSV보다 최신이면 경고"도 붙이는 편이 좋습니다.
    /// </para>
    /// </remarks>
    public const bool AutoImportCsv = false;

    /// <summary>
    /// 실제로 존재하는 내보내기 검색 폴더만 골라 반환합니다.
    /// </summary>
    /// <returns>
    /// 유효한 폴더 목록입니다. 하나도 없으면 <c>null</c>이며, 호출자는 프로젝트 전체를 검색해야 합니다.
    /// </returns>
    /// <remarks>
    /// <see cref="AssetDatabase.FindAssets(string, string[])"/>는 없는 폴더가 섞이면 경고를 냅니다.
    /// 폴더를 옮긴 직후에 경고가 쏟아지지 않도록 여기서 걸러냅니다.
    /// </remarks>
    public static string[] GetExportSearchFolders()
    {
        List<string> valid = new List<string>();
        foreach (string folder in ExportSearchFolders)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                valid.Add(folder);
            }
        }

        return valid.Count > 0 ? valid.ToArray() : null;
    }

    /// <summary>검색 범위를 사람이 읽을 수 있는 한 줄로 설명합니다.</summary>
    /// <returns>창과 로그에 그대로 쓸 수 있는 문구입니다.</returns>
    public static string DescribeExportScope()
    {
        string[] folders = GetExportSearchFolders();
        return folders == null
            ? "검색 범위 폴더가 없어 프로젝트 전체에서 찾습니다."
            : $"검색 범위: {string.Join(", ", folders)}";
    }
}
