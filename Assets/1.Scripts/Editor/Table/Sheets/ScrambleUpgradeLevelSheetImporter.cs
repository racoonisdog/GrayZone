using System.Collections.Generic;
using UnityEditor;

/// <summary><c>ScrambleUpgradeLevel</c> 시트(업그레이드·레벨 하나당 한 행)를 <see cref="ScrambleUpgradeTableSO"/>에 반영합니다.</summary>
internal sealed class ScrambleUpgradeLevelSheetImporter : TableSheetImporter
{
    public override string SheetName => "ScrambleUpgradeLevel";

    public override void Import(CsvTable table)
    {
        table.RequireColumns("UpgradeId", "Level", "Cost", "Info");

        List<ScrambleUpgradeTableSO.LevelEntry> rows = new List<ScrambleUpgradeTableSO.LevelEntry>();
        Dictionary<ScrambleUpgradeType, SortedSet<int>> levelsByUpgrade = new Dictionary<ScrambleUpgradeType, SortedSet<int>>();
        foreach (CsvRow row in table.Rows)
        {
            ScrambleUpgradeType upgradeId = row.GetEnum<ScrambleUpgradeType>("UpgradeId");
            int level = row.GetInt("Level");
            int cost = row.GetInt("Cost");

            if (level < 1)
                throw row.Error("Level", "레벨은 1부터 시작합니다.");
            if (cost < 0)
                throw row.Error("Cost", "비용은 0 이상이어야 합니다.");

            if (!levelsByUpgrade.TryGetValue(upgradeId, out SortedSet<int> levels))
                levelsByUpgrade.Add(upgradeId, levels = new SortedSet<int>());
            if (!levels.Add(level))
                throw row.Error("Level", $"{upgradeId} Lv.{level} 행이 두 번 있습니다.");

            rows.Add(new ScrambleUpgradeTableSO.LevelEntry
            {
                UpgradeId = upgradeId,
                Level = level,
                Cost = cost,
                Info = row.GetString("Info"),
            });
        }

        // 최대 레벨은 가장 높은 Level로 정해지므로, 중간 레벨이 빠지면 그 레벨에서 비용을 찾지 못해 진행이 막힙니다.
        foreach (KeyValuePair<ScrambleUpgradeType, SortedSet<int>> pair in levelsByUpgrade)
        {
            int expected = 1;
            foreach (int level in pair.Value)
            {
                if (level != expected)
                    throw new TableImportException(table.FilePath, 0, "Level", $"{pair.Key}의 Lv.{expected} 행이 없습니다. 레벨은 1부터 빠짐없이 적어야 합니다.");
                expected++;
            }
        }

        ScrambleUpgradeTableSO asset = FindSingleAsset<ScrambleUpgradeTableSO>(table);
        Undo.RecordObject(asset, "Import ScrambleUpgradeLevel Sheet");
        asset.EditorReplaceLevels(rows);
        EditorUtility.SetDirty(asset);
    }
}
