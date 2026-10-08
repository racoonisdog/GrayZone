// Builds the FireBarrel_Trap prefab and rebuilds Spike_Trap on the Thorn_Trap2_2 model,
// then swaps the matching scene objects in the open scene for those trap prefabs.
//
//   Tools > GrayZone > Traps > 1. Build FireBarrel & Spike Prefabs
//   Tools > GrayZone > Traps > 2. Replace MetalBarrel & Thorn In Open Scene
//
// Step 2 only swaps scene-level instances of the plain MetalBarrel prefab (not MetalBarrel_d)
// and of the Thorn_Trap2_2 model. Position, rotation, scale, parent, sibling order and active
// state are kept. The scene is left dirty (one Undo group) and never saved here.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrayZone.LevelTools
{
    public static class TrapPrefabConversionTool
    {
        const string MetalBarrelPath = "Assets/3.Resources/ThirdParty/DestructibleProps/Prefabs_indestructible/MetalBarrel.prefab";
        const string ThornModelPath = "Assets/3.Resources/ThirdParty/AI_Model/Thorn_Trap2_2/Thorn_Trap2_2.fbx.fbx";
        const string FirePrefabPath = "Assets/2.Prefabs/Trap/FireBoom_Trap.prefab";
        const string ExplosionEffectPath = "Assets/3.Resources/VFX/Explosion/Explosion1.prefab";
        const string BlueprintMaterialPath = "Assets/2.Prefabs/Trap/M_TrapBlueprint.mat";

        const string FireBarrelPrefabPath = "Assets/2.Prefabs/Trap/FireBarrel_Trap.prefab";
        const string SpikePrefabPath = "Assets/2.Prefabs/Trap/Spike_Trap.prefab";

        // One large hit per enemy on entry. The old Spike_Trap used 8.
        const int SpikeDamage = 30;

        // The interaction trigger is a little larger than the visual so it is easy to target.
        const float TriggerPadding = 1.1f;

        const float MinTriggerHeight = 1.0f;

        [MenuItem("Tools/GrayZone/Traps/1. Build FireBarrel & Spike Prefabs")]
        public static void BuildPrefabs()
        {
            // Rebuilding saves a fresh hierarchy with new fileIDs, which breaks scene instances
            // already placed from these prefabs. Once built, edit the prefabs directly instead.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FireBarrelPrefabPath) != null)
            {
                Debug.LogWarning("[TrapPrefabConversionTool] FireBarrel_Trap already exists; skipped. Edit the prefab directly.");
            }

            GameObject existingSpike = AssetDatabase.LoadAssetAtPath<GameObject>(SpikePrefabPath);
            if (existingSpike != null && existingSpike.transform.Find("Visual") != null)
            {
                Debug.LogWarning("[TrapPrefabConversionTool] Spike_Trap is already on the Thorn model; skipped. Edit the prefab directly.");
            }

            bool barrel = AssetDatabase.LoadAssetAtPath<GameObject>(FireBarrelPrefabPath) != null || BuildFireBarrelPrefab();
            bool spike = (existingSpike != null && existingSpike.transform.Find("Visual") != null) || BuildSpikePrefab();
            AssetDatabase.SaveAssets();
            Debug.Log($"[TrapPrefabConversionTool] FireBarrel_Trap: {(barrel ? "built" : "FAILED")}, Spike_Trap: {(spike ? "rebuilt" : "FAILED")}");
        }

        [MenuItem("Tools/GrayZone/Traps/2. Replace MetalBarrel & Thorn In Open Scene")]
        public static void ReplaceInOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject fireBarrel = AssetDatabase.LoadAssetAtPath<GameObject>(FireBarrelPrefabPath);
            GameObject spike = AssetDatabase.LoadAssetAtPath<GameObject>(SpikePrefabPath);
            if (fireBarrel == null || spike == null)
            {
                Debug.LogError("[TrapPrefabConversionTool] Run step 1 first: FireBarrel_Trap or Spike_Trap prefab is missing.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace MetalBarrel & Thorn With Traps");

            int barrels = ReplaceInstances(scene, MetalBarrelPath, fireBarrel, "FireBarrel_Trap");
            int spikes = ReplaceInstances(scene, ThornModelPath, spike, "Spike_Trap");

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[TrapPrefabConversionTool] '{scene.path}': MetalBarrel -> FireBarrel_Trap {barrels}, Thorn_Trap2_2 -> Spike_Trap {spikes}. Scene is dirty, not saved.");
        }

        static bool BuildFireBarrelPrefab()
        {
            GameObject barrelModel = AssetDatabase.LoadAssetAtPath<GameObject>(MetalBarrelPath);
            FireTrap firePrefab = AssetDatabase.LoadAssetAtPath<FireTrap>(FirePrefabPath);
            if (barrelModel == null || firePrefab == null)
            {
                Debug.LogError($"[TrapPrefabConversionTool] Missing source: {MetalBarrelPath} or {FirePrefabPath}");
                return false;
            }

            GameObject root = new GameObject("FireBarrel_Trap");
            try
            {
                root.layer = LayerMask.NameToLayer("Trap");

                // The visual stays on its own (Default) layer: player shots skip the Trap layer,
                // so its solid MeshCollider is what bullets hit.
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(barrelModel, root.transform);
                visual.name = "Visual";

                Collider[] shotColliders = visual.GetComponentsInChildren<Collider>(true)
                    .Where(c => !c.isTrigger)
                    .ToArray();
                if (shotColliders.Length == 0)
                {
                    Debug.LogWarning("[TrapPrefabConversionTool] MetalBarrel has no solid collider; FireBarrel_Trap cannot be shot.");
                }

                AddTriggerFromBounds(root, visual);

                FireBarrelTrap trap = root.AddComponent<FireBarrelTrap>();
                SerializedObject so = new SerializedObject(trap);
                so.FindProperty("m_startPlaced").boolValue = false;
                so.FindProperty("m_buildCostAmount").intValue = 0; // same as the other trap prefabs
                so.FindProperty("m_maxHealth").intValue = 3;
                so.FindProperty("m_damageMode").enumValueIndex = (int)TrapDamageMode.Once;
                so.FindProperty("m_rebuildPolicy").enumValueIndex = (int)TrapRebuildPolicy.OnRest;
                so.FindProperty("m_blueprintMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(BlueprintMaterialPath);
                so.FindProperty("m_firePrefab").objectReferenceValue = firePrefab;
                so.FindProperty("m_explosionEffectPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionEffectPath);

                SerializedProperty colliders = so.FindProperty("m_shotColliders");
                colliders.arraySize = shotColliders.Length;
                for (int i = 0; i < shotColliders.Length; i++)
                {
                    colliders.GetArrayElementAtIndex(i).objectReferenceValue = shotColliders[i];
                }

                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, FireBarrelPrefabPath, out bool success);
                return success;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Rebuilds Spike_Trap on the Thorn_Trap2_2 model. The SpikeTrap values (entry slow, force walk,
        /// durability, rebuild policy...) are carried over from the existing prefab; only the damage is raised.
        /// Saving over the same path keeps the prefab GUID.
        /// </summary>
        static bool BuildSpikePrefab()
        {
            GameObject thornModel = AssetDatabase.LoadAssetAtPath<GameObject>(ThornModelPath);
            if (thornModel == null)
            {
                Debug.LogError($"[TrapPrefabConversionTool] Missing source: {ThornModelPath}");
                return false;
            }

            SpikeTrap oldSpike = AssetDatabase.LoadAssetAtPath<SpikeTrap>(SpikePrefabPath);

            GameObject root = new GameObject("Spike_Trap");
            try
            {
                int trapLayer = LayerMask.NameToLayer("Trap");
                root.layer = trapLayer;

                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(thornModel, root.transform);
                visual.name = "Visual";
                foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = trapLayer;
                }

                AddTriggerFromBounds(root, visual);

                SpikeTrap spike = root.AddComponent<SpikeTrap>();
                if (oldSpike != null)
                {
                    EditorUtility.CopySerialized(oldSpike, spike);
                }

                SerializedObject so = new SerializedObject(spike);
                so.FindProperty("m_damage").intValue = SpikeDamage;
                so.FindProperty("m_damageMode").enumValueIndex = (int)TrapDamageMode.Once;

                // These pointed into the old prefab's hierarchy and would dangle.
                so.FindProperty("m_blueprintVisualRoot").objectReferenceValue = null;
                so.FindProperty("m_builtVisualRoot").objectReferenceValue = null;

                SerializedProperty blueprint = so.FindProperty("m_blueprintMaterial");
                if (blueprint.objectReferenceValue == null)
                {
                    blueprint.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(BlueprintMaterialPath);
                }

                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, SpikePrefabPath, out bool success);
                return success;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Adds a trigger BoxCollider on the root that wraps the visual's renderers.</summary>
        static void AddTriggerFromBounds(GameObject root, GameObject visual)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // The root sits at the origin with identity rotation and scale, so world bounds are local bounds.
            // Flat models (the thorn strip is ~0.2m tall) get a minimum height so walking enemies always overlap it.
            float height = Mathf.Max(bounds.size.y, MinTriggerHeight);
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(bounds.center.x, bounds.min.y + height * 0.5f, bounds.center.z);
            trigger.size = new Vector3(bounds.size.x * TriggerPadding, height, bounds.size.z * TriggerPadding);
        }

        static int ReplaceInstances(Scene scene, string sourceAssetPath, GameObject newPrefab, string newBaseName)
        {
            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourceAssetPath);
            if (sourceAsset == null)
            {
                Debug.LogError($"[TrapPrefabConversionTool] Missing source: {sourceAssetPath}");
                return 0;
            }

            List<GameObject> targets = new List<GameObject>();
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (Transform t in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(go)
                        && PrefabUtility.GetCorrespondingObjectFromSource(go) == sourceAsset)
                    {
                        targets.Add(go);
                    }
                }
            }

            // "MetalBarrel (3)" -> "FireBarrel_Trap (3)"; keeps the Unity duplicate suffix.
            Regex suffix = new Regex(@"(\s\(\d+\))$");

            foreach (GameObject old in targets)
            {
                Transform oldTransform = old.transform;
                GameObject created = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, scene);
                Undo.RegisterCreatedObjectUndo(created, "Create trap");

                Transform t = created.transform;
                t.SetParent(oldTransform.parent, false);
                t.localPosition = oldTransform.localPosition;
                t.localRotation = oldTransform.localRotation;
                t.localScale = oldTransform.localScale;
                t.SetSiblingIndex(oldTransform.GetSiblingIndex());

                Match match = suffix.Match(old.name);
                created.name = newBaseName + (match.Success ? match.Groups[1].Value : string.Empty);
                created.SetActive(old.activeSelf);

                Undo.DestroyObjectImmediate(old);
            }

            return targets.Count;
        }
    }
}
