using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 테이블형 CSV 시트 하나를 게임 데이터(ScriptableObject)에 반영하는 임포터의 기반 클래스입니다.
/// </summary>
/// <remarks>
/// 시트 파일 이름(확장자 제외)이 <see cref="SheetName"/>과 같으면 이 임포터가 맡습니다.
/// 새 시트를 추가하려면 이 클래스를 상속한 클래스 하나만 만들면 되고, 등록은 <see cref="TypeCache"/>가 찾아 줍니다.
/// <para>
/// 이 경로에서는 <b>시트가 정본</b>이고 SO는 시트에서 만들어지는 결과물입니다. SO를 Inspector에서 직접 고쳐도
/// 다음 가져오기에서 덮어씁니다. (SO가 정본인 밸런스 값은 SO CSV 도구가 맡습니다.)
/// </para>
/// </remarks>
internal abstract class TableSheetImporter
{
    /// <summary>테이블 시트를 두는 폴더입니다. 이 폴더의 CSV는 저장하면 자동으로 가져옵니다.</summary>
    public const string SheetFolder = "Assets/5.Data/DataSheet/TableData";

    /// <summary>담당하는 시트 파일 이름(확장자 제외)입니다.</summary>
    public abstract string SheetName { get; }

    /// <summary>
    /// 시트를 읽어 대상 에셋에 씁니다. 실패하면 <see cref="TableImportException"/>을 던지고,
    /// 이때는 에셋에 아무것도 쓰지 않아야 합니다(모두 검증한 뒤 마지막에 한 번에 씁니다).
    /// </summary>
    public abstract void Import(CsvTable table);

    /// <summary>프로젝트에 하나만 있어야 하는 테이블 에셋을 찾습니다.</summary>
    protected static T FindSingleAsset<T>(CsvTable table) where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        if (guids.Length == 0)
            throw new TableImportException(table.FilePath, 0, null, $"{typeof(T).Name} 에셋이 없습니다. 먼저 에셋을 만들어 두세요.");
        if (guids.Length > 1)
            throw new TableImportException(table.FilePath, 0, null, $"{typeof(T).Name} 에셋이 {guids.Length}개입니다. 하나만 두세요.");

        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    /// <summary>
    /// 경로를 고정한 테이블 에셋을 불러옵니다. 같은 SO 타입의 에셋이 여러 개라
    /// <see cref="FindSingleAsset{T}"/>를 쓸 수 없을 때(시설별 업그레이드 표 등) 사용합니다.
    /// </summary>
    protected static T LoadAssetAtPath<T>(CsvTable table, string assetPath) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null)
            throw new TableImportException(table.FilePath, 0, null, $"{typeof(T).Name} 에셋이 없습니다: {assetPath}. 먼저 에셋을 만들어 두세요.");

        return asset;
    }

    /// <summary>시트 경로에 맞는 임포터를 찾아 가져옵니다. 결과는 콘솔에 남깁니다.</summary>
    public static bool ImportSheet(string path)
    {
        string sheetName = Path.GetFileNameWithoutExtension(path);
        TableSheetImporter importer = FindImporter(sheetName);
        if (importer == null)
        {
            Debug.LogWarning($"[TableImporter] {sheetName}: 이 시트를 맡는 임포터가 없습니다. TableSheetImporter를 상속해 SheetName을 \"{sheetName}\"으로 지정하세요.");
            return false;
        }

        try
        {
            importer.Import(CsvTable.Load(path));
            Debug.Log($"[TableImporter] {sheetName} 가져오기 완료");
            return true;
        }
        catch (TableImportException e)
        {
            Debug.LogError($"[TableImporter] {e.Message}");
            return false;
        }
    }

    [MenuItem("Tools/GrayZone/테이블 시트 전체 다시 가져오기")]
    private static void ImportAll()
    {
        if (!AssetDatabase.IsValidFolder(SheetFolder))
        {
            Debug.LogWarning($"[TableImporter] 시트 폴더가 없습니다: {SheetFolder}");
            return;
        }

        int succeeded = 0;
        int failed = 0;
        foreach (string path in Directory.GetFiles(SheetFolder, "*.csv", SearchOption.AllDirectories))
        {
            if (ImportSheet(path.Replace('\\', '/')))
                succeeded++;
            else
                failed++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[TableImporter] 전체 가져오기: 성공 {succeeded} / 실패 {failed}");
    }

    private static TableSheetImporter FindImporter(string sheetName)
    {
        foreach (Type type in TypeCache.GetTypesDerivedFrom<TableSheetImporter>())
        {
            if (type.IsAbstract)
                continue;

            TableSheetImporter importer = (TableSheetImporter)Activator.CreateInstance(type);
            if (string.Equals(importer.SheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                return importer;
        }

        return null;
    }
}

/// <summary>
/// 시트 폴더의 CSV가 추가·변경되면 해당 임포터로 자동 반영합니다.
/// </summary>
/// <remarks>
/// 에셋 임포트 콜백 안에서 에셋을 직접 수정하지 않도록 실제 반영은 <see cref="EditorApplication.delayCall"/>로 지연합니다.
/// CSV를 되쓰지 않으므로 반영이 다시 임포트를 부르는 순환은 없습니다.
/// </remarks>
internal sealed class TableSheetAutoImporter : AssetPostprocessor
{
    private static readonly HashSet<string> s_pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool s_scheduled;

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
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
        string prefix = TableSheetImporter.SheetFolder + "/";
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
                TableSheetImporter.ImportSheet(path);
        }

        AssetDatabase.SaveAssets();
    }
}
