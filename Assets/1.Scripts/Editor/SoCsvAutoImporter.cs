using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

/// <summary>
/// CsvData 폴더에 추가되거나 변경된 CSV 파일을 감지해 대응하는 ScriptableObject 에셋에 자동 반영합니다.
/// </summary>
/// <remarks>
/// <b>현재 기본값은 꺼짐입니다.</b> <see cref="BalanceTableSettings.AutoImportCsv"/>가 <c>true</c>일 때만 동작합니다.
/// 정본이 SO이고 CSV가 스냅샷인 동안에는 자동 반영이 낡은 시트로 SO를 되돌리는 통로가 되기 때문입니다.
/// 그동안 가져오기는 도구 창의 수동 버튼으로만 합니다.
/// <para>
/// 에셋 임포트 콜백 안에서 에셋을 직접 수정하지 않도록 실제 반영은 <see cref="EditorApplication.delayCall"/>로 지연합니다.
/// 내보내기 및 수동 가져오기 UI는 <c>Assets/1.Scripts/Editor/Tool/ScriptableObjectCsvTool.cs</c>에 있습니다.
/// </para>
/// </remarks>
internal sealed class SoCsvAutoImporter : AssetPostprocessor
{
    private static readonly HashSet<string> s_pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool s_scheduled;

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // 현재 정본은 SO이고 CSV는 내보낸 스냅샷입니다. 그 상태에서 자동 반영은 낡은 시트가
        // SO를 조용히 되돌리는 통로가 되므로 팀 공통 설정으로 잠가 둡니다.
        if (!BalanceTableSettings.AutoImportCsv)
        {
            return;
        }

        Collect(importedAssets);
        Collect(movedAssets);

        if (s_pending.Count > 0 && !s_scheduled)
        {
            s_scheduled = true;
            EditorApplication.delayCall += ProcessPending;
        }
    }

    private static void Collect(string[] paths)
    {
        string prefix = ScriptableObjectCsvWindow.CsvFolder + "/";
        foreach (string raw in paths)
        {
            string path = raw.Replace('\\', '/');
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s_pending.Add(path);
            }
        }
    }

    private static void ProcessPending()
    {
        EditorApplication.delayCall -= ProcessPending;
        s_scheduled = false;

        if (s_pending.Count == 0)
            return;

        List<string> batch = new List<string>(s_pending);
        s_pending.Clear();

        foreach (string path in batch)
        {
            if (File.Exists(path))
                ScriptableObjectCsvWindow.ImportFile(path);
        }

        AssetDatabase.SaveAssets();
    }
}
