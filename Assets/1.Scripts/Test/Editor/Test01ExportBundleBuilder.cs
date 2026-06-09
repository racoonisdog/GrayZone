using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class Test01ExportBundleBuilder
{
    private const string SourceScenePath = "Assets/Scenes/Test01.unity";
    private const string ExportRoot = "Assets/Test01_ExportBundle";
    private const string DependencyRoot = ExportRoot + "/Dependencies";
    private const string ExportScenePath = ExportRoot + "/Test01_Export.unity";
    private const string ReadmePath = ExportRoot + "/README_Test01_Export.txt";
    private const string BuilderScriptPath = "Assets/Editor/Test01ExportBundleBuilder.cs";

    private static readonly HashSet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".asmdef",
        ".asmref",
        ".dll",
        ".rsp"
    };

    [MenuItem("Tools/Test01/Prepare Single-Folder Export Bundle")]
    public static void PrepareFromMenu()
    {
        try
        {
            var result = Prepare();
            EditorUtility.DisplayDialog("Test01 Export Bundle", result.DialogMessage, "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError("[Test01ExportBundle] " + ex);
            EditorUtility.DisplayDialog("Test01 Export Bundle Failed", ex.Message, "OK");
        }
    }

    public static void DryRunForBatch()
    {
        var plan = BuildMovePlan(SourceScenePath);
        Debug.Log(plan.ToLog("DRY RUN"));
    }

    public static void PrepareForBatch()
    {
        var result = Prepare();
        Debug.Log(result.ToLog("COMPLETE"));
    }

    private static ExportResult Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play Mode before preparing the Test01 export bundle.");
        }

        if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(SourceScenePath)))
        {
            throw new FileNotFoundException("Source scene was not found.", SourceScenePath);
        }

        EnsureFolderPath(ExportRoot);
        EnsureFolderPath(DependencyRoot);

        if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ExportScenePath)))
        {
            if (!AssetDatabase.DeleteAsset(ExportScenePath))
            {
                throw new IOException("Could not replace existing export scene: " + ExportScenePath);
            }
        }

        if (!AssetDatabase.CopyAsset(SourceScenePath, ExportScenePath))
        {
            throw new IOException("Could not copy source scene to export scene: " + ExportScenePath);
        }

        AssetDatabase.ImportAsset(ExportScenePath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();

        var plan = BuildMovePlan(ExportScenePath);
        foreach (var item in plan.Items)
        {
            EnsureFolderPath(GetParentFolder(item.DestinationPath));
        }

        var validationErrors = ValidateMovePlan(plan);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException("Move validation failed:\n" + string.Join("\n", validationErrors));
        }

        var moved = new List<MoveItem>();
        var skipped = 0;
        var moveErrors = new List<string>();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var item in plan.Items)
            {
                var currentPath = NormalizePath(AssetDatabase.GUIDToAssetPath(item.Guid));
                if (string.IsNullOrEmpty(currentPath))
                {
                    moveErrors.Add("Missing asset for GUID " + item.Guid + " originally at " + item.OriginalPath);
                    continue;
                }

                if (IsInsideExportRoot(currentPath))
                {
                    skipped++;
                    continue;
                }

                var error = AssetDatabase.MoveAsset(currentPath, item.DestinationPath);
                if (!string.IsNullOrEmpty(error))
                {
                    moveErrors.Add(currentPath + " -> " + item.DestinationPath + ": " + error);
                    continue;
                }

                moved.Add(item);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        if (moveErrors.Count > 0)
        {
            throw new InvalidOperationException("One or more assets could not be moved:\n" + string.Join("\n", moveErrors));
        }

        var remainingExternalAssets = AssetDatabase.GetDependencies(ExportScenePath, true)
            .Select(NormalizePath)
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && !IsInsideExportRoot(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var packageDependencies = AssetDatabase.GetDependencies(ExportScenePath, true)
            .Select(NormalizePath)
            .Where(path => path.StartsWith("Packages/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        WriteReadme(moved.Count, skipped, remainingExternalAssets, packageDependencies);
        AssetDatabase.ImportAsset(ReadmePath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();

        return new ExportResult
        {
            PlannedMoveCount = plan.Items.Count,
            MovedCount = moved.Count,
            SkippedAlreadyInsideCount = skipped,
            SceneDependencyCount = plan.SceneDependencyCount,
            ProjectCodeAssetCount = plan.ProjectCodeAssetCount,
            RemainingExternalAssets = remainingExternalAssets,
            PackageDependencies = packageDependencies
        };
    }

    private static MovePlan BuildMovePlan(string scenePath)
    {
        var candidates = new Dictionary<string, MoveItem>(StringComparer.Ordinal);
        var sceneDependencies = AssetDatabase.GetDependencies(scenePath, true)
            .Select(NormalizePath)
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
            .Where(path => path != scenePath)
            .Where(path => !IsInsideExportRoot(path));

        var sceneDependencyCount = 0;
        foreach (var path in sceneDependencies)
        {
            sceneDependencyCount++;
            AddCandidate(candidates, path, "scene dependency");
        }

        var projectCodeAssets = AssetDatabase.GetAllAssetPaths()
            .Select(NormalizePath)
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
            .Where(path => path != BuilderScriptPath)
            .Where(path => !IsInsideExportRoot(path))
            .Where(path => CodeExtensions.Contains(Path.GetExtension(path)));

        var projectCodeAssetCount = 0;
        foreach (var path in projectCodeAssets)
        {
            projectCodeAssetCount++;
            AddCandidate(candidates, path, "project code asset");
        }

        return new MovePlan
        {
            Items = candidates.Values
                .OrderBy(item => item.OriginalPath, StringComparer.Ordinal)
                .ToList(),
            SceneDependencyCount = sceneDependencyCount,
            ProjectCodeAssetCount = projectCodeAssetCount
        };
    }

    private static void AddCandidate(Dictionary<string, MoveItem> candidates, string assetPath, string reason)
    {
        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
        {
            return;
        }

        if (!candidates.TryGetValue(guid, out var existing))
        {
            candidates.Add(guid, new MoveItem
            {
                Guid = guid,
                OriginalPath = assetPath,
                DestinationPath = ToDestinationPath(assetPath),
                Reasons = new SortedSet<string>(StringComparer.Ordinal) { reason }
            });
            return;
        }

        existing.Reasons.Add(reason);
    }

    private static List<string> ValidateMovePlan(MovePlan plan)
    {
        var errors = new List<string>();
        foreach (var item in plan.Items)
        {
            var currentPath = NormalizePath(AssetDatabase.GUIDToAssetPath(item.Guid));
            if (string.IsNullOrEmpty(currentPath))
            {
                errors.Add("Missing asset for GUID " + item.Guid + " originally at " + item.OriginalPath);
                continue;
            }

            if (IsInsideExportRoot(currentPath))
            {
                continue;
            }

            var destinationGuid = AssetDatabase.AssetPathToGUID(item.DestinationPath);
            if (!string.IsNullOrEmpty(destinationGuid) && destinationGuid != item.Guid)
            {
                errors.Add("Destination already exists: " + item.DestinationPath);
                continue;
            }

            var validation = AssetDatabase.ValidateMoveAsset(currentPath, item.DestinationPath);
            if (!string.IsNullOrEmpty(validation))
            {
                errors.Add(currentPath + " -> " + item.DestinationPath + ": " + validation);
            }
        }

        return errors;
    }

    private static void EnsureFolderPath(string folderPath)
    {
        folderPath = NormalizePath(folderPath);
        if (folderPath == "Assets" || string.IsNullOrEmpty(folderPath))
        {
            return;
        }

        var parts = folderPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
        {
            throw new ArgumentException("Folder path must be inside Assets: " + folderPath);
        }

        var current = "Assets";
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                var createdGuid = AssetDatabase.CreateFolder(current, parts[i]);
                if (string.IsNullOrEmpty(createdGuid))
                {
                    throw new IOException("Could not create folder: " + next);
                }
            }

            current = next;
        }
    }

    private static string ToDestinationPath(string assetPath)
    {
        var relativePath = assetPath.Substring("Assets/".Length);
        return DependencyRoot + "/" + relativePath;
    }

    private static string GetParentFolder(string assetPath)
    {
        return NormalizePath(Path.GetDirectoryName(assetPath));
    }

    private static bool IsInsideExportRoot(string assetPath)
    {
        return assetPath == ExportRoot || assetPath.StartsWith(ExportRoot + "/", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static void WriteReadme(
        int movedCount,
        int skippedCount,
        IReadOnlyCollection<string> remainingExternalAssets,
        IReadOnlyCollection<string> packageDependencies)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Test01 single-folder export bundle");
        builder.AppendLine();
        builder.AppendLine("Export target folder:");
        builder.AppendLine(ExportRoot);
        builder.AppendLine();
        builder.AppendLine("Scene copy to open/check:");
        builder.AppendLine(ExportScenePath);
        builder.AppendLine();
        builder.AppendLine("Export instructions:");
        builder.AppendLine("1. Select the folder Assets/Test01_ExportBundle in the Project window.");
        builder.AppendLine("2. Choose Assets > Export Package.");
        builder.AppendLine("3. Turn off Include dependencies.");
        builder.AppendLine("4. Export the package.");
        builder.AppendLine();
        builder.AppendLine("Moved assets: " + movedCount);
        builder.AppendLine("Skipped assets already inside bundle: " + skippedCount);
        builder.AppendLine();

        if (remainingExternalAssets.Count > 0)
        {
            builder.AppendLine("Remaining Assets dependencies outside the bundle:");
            foreach (var path in remainingExternalAssets)
            {
                builder.AppendLine("- " + path);
            }

            builder.AppendLine();
        }

        if (packageDependencies.Count > 0)
        {
            builder.AppendLine("Package dependencies that are not copied into this asset package:");
            foreach (var path in packageDependencies)
            {
                builder.AppendLine("- " + path);
            }
        }

        File.WriteAllText(ReadmePath, builder.ToString(), Encoding.UTF8);
    }

    private sealed class MovePlan
    {
        public List<MoveItem> Items;
        public int SceneDependencyCount;
        public int ProjectCodeAssetCount;

        public string ToLog(string label)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[Test01ExportBundle] " + label);
            builder.AppendLine("Scene dependency assets: " + SceneDependencyCount);
            builder.AppendLine("Project code assets: " + ProjectCodeAssetCount);
            builder.AppendLine("Unique assets planned for move: " + Items.Count);
            foreach (var item in Items.Take(25))
            {
                builder.AppendLine("- " + item.OriginalPath + " -> " + item.DestinationPath);
            }

            if (Items.Count > 25)
            {
                builder.AppendLine("- ... " + (Items.Count - 25) + " more");
            }

            return builder.ToString();
        }
    }

    private sealed class MoveItem
    {
        public string Guid;
        public string OriginalPath;
        public string DestinationPath;
        public SortedSet<string> Reasons;
    }

    private sealed class ExportResult
    {
        public int PlannedMoveCount;
        public int MovedCount;
        public int SkippedAlreadyInsideCount;
        public int SceneDependencyCount;
        public int ProjectCodeAssetCount;
        public List<string> RemainingExternalAssets;
        public List<string> PackageDependencies;

        public string DialogMessage
        {
            get
            {
                return "Prepared " + ExportRoot + "\n\n"
                    + "Export scene: " + ExportScenePath + "\n"
                    + "Moved assets: " + MovedCount + "\n"
                    + "Remaining external Assets dependencies: " + RemainingExternalAssets.Count + "\n\n"
                    + "Export the whole Test01_ExportBundle folder with Include dependencies turned off.";
            }
        }

        public string ToLog(string label)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[Test01ExportBundle] " + label);
            builder.AppendLine("Scene dependency assets found: " + SceneDependencyCount);
            builder.AppendLine("Project code assets found: " + ProjectCodeAssetCount);
            builder.AppendLine("Unique planned moves: " + PlannedMoveCount);
            builder.AppendLine("Moved assets: " + MovedCount);
            builder.AppendLine("Skipped assets already inside bundle: " + SkippedAlreadyInsideCount);
            builder.AppendLine("Remaining external Assets dependencies: " + RemainingExternalAssets.Count);
            foreach (var path in RemainingExternalAssets.Take(25))
            {
                builder.AppendLine("- " + path);
            }

            if (RemainingExternalAssets.Count > 25)
            {
                builder.AppendLine("- ... " + (RemainingExternalAssets.Count - 25) + " more");
            }

            builder.AppendLine("Package dependencies: " + PackageDependencies.Count);
            foreach (var path in PackageDependencies.Take(25))
            {
                builder.AppendLine("- " + path);
            }

            if (PackageDependencies.Count > 25)
            {
                builder.AppendLine("- ... " + (PackageDependencies.Count - 25) + " more");
            }

            return builder.ToString();
        }
    }
}
