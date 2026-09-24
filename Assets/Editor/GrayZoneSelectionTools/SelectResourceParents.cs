using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

/// <summary>
/// Converts Scene-view leaf selections (LOD renderers, lights, and similar objects)
/// into their placed-resource parent selections.
/// </summary>
internal static class SelectResourceParents
{
    private const string MenuPath = "Tools/Selection/Select Resource Parents";
    private const string ShortcutId = "GrayZone/Select Resource Parents";

    [MenuItem(MenuPath, priority = 2000)]
    [Shortcut(ShortcutId, KeyCode.G, ShortcutModifiers.Shift)]
    private static void SelectParents()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected.Length == 0)
        {
            Debug.LogWarning("[Select Resource Parents] No scene objects are selected.");
            return;
        }

        HashSet<Transform> resolved = new HashSet<Transform>();

        foreach (GameObject selectedObject in selected)
        {
            if (selectedObject == null || !selectedObject.scene.IsValid())
            {
                continue;
            }

            resolved.Add(ResolveResourceTransform(selectedObject.transform));
        }

        // If both a resource and one of its descendants were selected, retain only
        // the highest resolved resource so Delete cannot process the same hierarchy twice.
        Transform[] parents = resolved
            .Where(candidate => !resolved.Any(other => other != candidate && candidate.IsChildOf(other)))
            .OrderBy(GetHierarchyPath, StringComparer.Ordinal)
            .ToArray();

        if (parents.Length == 0)
        {
            Debug.LogWarning("[Select Resource Parents] No scene resource parents were found.");
            return;
        }

        Selection.objects = parents.Select(parent => (UnityEngine.Object)parent.gameObject).ToArray();
        Selection.activeGameObject = parents[0].gameObject;
        SceneView.RepaintAll();

        Debug.Log($"[Select Resource Parents] Converted {selected.Length} selected object(s) " +
                  $"to {parents.Length} unique resource parent(s).");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateSelectParents()
    {
        return Selection.gameObjects.Any(gameObject => gameObject != null && gameObject.scene.IsValid());
    }

    private static Transform ResolveResourceTransform(Transform selected)
    {
        if (selected.parent == null)
        {
            return selected;
        }

        // A non-leaf selection is already a useful resource container. Promoting it
        // again could unexpectedly select an entire building or organization group.
        if (selected.childCount > 0)
        {
            return selected;
        }

        // Do not cross the organizational parents created for Shelter_Residential_Area.
        // A renderer placed directly under one of these boundaries is already the
        // smallest complete resource available in the hierarchy.
        if (IsOrganizationBoundary(selected.parent))
        {
            return selected;
        }

        return selected.parent;
    }

    private static bool IsOrganizationBoundary(Transform transform)
    {
        string objectName = transform.name;

        return objectName.StartsWith("SRA_", StringComparison.Ordinal) ||
               objectName == "Shelter_Residential_Area" ||
               objectName == "Living Block 1" ||
               objectName == "Weapon Shop" ||
               objectName == "Food Shop";
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;

        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}
