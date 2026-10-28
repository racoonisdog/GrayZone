using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 시설별 <c>*UpgradeLevel</c> 시트(목표 레벨 하나당 한 행)를 <see cref="FacilityUpgradeTableSO"/>에 반영하는 공통 임포터입니다.
/// </summary>
/// <remarks>
/// 시설마다 시트와 에셋이 하나씩이라, 하위 클래스는 <see cref="TableSheetImporter.SheetName"/>과 <see cref="AssetPath"/>만 지정합니다.
/// </remarks>
internal abstract class FacilityUpgradeLevelSheetImporter : TableSheetImporter
{
    /// <summary>이 시트를 쓸 <see cref="FacilityUpgradeTableSO"/> 에셋 경로입니다.</summary>
    protected abstract string AssetPath { get; }

    public override void Import(CsvTable table)
    {
        table.RequireColumns("Level", "ResourceId", "Amount", "Effect");

        List<FacilityUpgradeTableSO.LevelEntry> rows = new List<FacilityUpgradeTableSO.LevelEntry>();
        SortedSet<int> levels = new SortedSet<int>();
        foreach (CsvRow row in table.Rows)
        {
            int level = row.GetInt("Level");
            string resourceId = ResourceIds.Normalize(row.GetString("ResourceId"));
            int amount = row.GetInt("Amount");

            if (level < FacilityUpgradeTableSO.FirstLevel)
                throw row.Error("Level", $"레벨은 목표 레벨이라 {FacilityUpgradeTableSO.FirstLevel}부터 시작합니다. (Lv.1은 시작 레벨)");
            if (!levels.Add(level))
                throw row.Error("Level", $"Lv.{level} 행이 두 번 있습니다.");
            if (amount < 0)
                throw row.Error("Amount", "수량은 0 이상이어야 합니다.");
            if (amount > 0 && !ResourceIds.IsDefined(resourceId))
                throw row.Error("ResourceId", $"ResourceIds에 없는 자원입니다: \"{resourceId}\" (가능한 값: {string.Join(", ", ResourceIds.All)})");

            rows.Add(new FacilityUpgradeTableSO.LevelEntry
            {
                Level = level,
                ResourceId = resourceId,
                Amount = amount,
                Effect = row.GetString("Effect"),
            });
        }

        // 최대 레벨은 가장 높은 Level로 정해지므로, 중간 레벨이 빠지면 그 레벨에서 비용을 찾지 못해 진행이 막힙니다.
        int expected = FacilityUpgradeTableSO.FirstLevel;
        foreach (int level in levels)
        {
            if (level != expected)
                throw new TableImportException(table.FilePath, 0, "Level", $"Lv.{expected} 행이 없습니다. 레벨은 {FacilityUpgradeTableSO.FirstLevel}부터 빠짐없이 적어야 합니다.");
            expected++;
        }

        FacilityUpgradeTableSO asset = LoadAssetAtPath<FacilityUpgradeTableSO>(table, AssetPath);
        Undo.RecordObject(asset, $"Import {SheetName} Sheet");
        asset.EditorReplaceLevels(rows);
        EditorUtility.SetDirty(asset);
    }
}

/// <summary><c>MedicalUpgradeLevel</c> 시트를 의료실 업그레이드 표에 반영합니다.</summary>
internal sealed class MedicalUpgradeLevelSheetImporter : FacilityUpgradeLevelSheetImporter
{
    public override string SheetName => "MedicalUpgradeLevel";
    protected override string AssetPath => "Assets/5.Data/ScriptableObject/GameSOData/Facility/MedicalUpgradeTable.asset";
}

/// <summary><c>ManufacturingUpgradeLevel</c> 시트를 제조실 업그레이드 표에 반영합니다.</summary>
internal sealed class ManufacturingUpgradeLevelSheetImporter : FacilityUpgradeLevelSheetImporter
{
    public override string SheetName => "ManufacturingUpgradeLevel";
    protected override string AssetPath => "Assets/5.Data/ScriptableObject/GameSOData/Facility/ManufacturingUpgradeTable.asset";
}
