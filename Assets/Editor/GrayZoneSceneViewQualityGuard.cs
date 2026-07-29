using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class GrayZoneSceneViewQualityGuard
{
    private const string TargetScenePath = "Assets/Scenes/GrayZone_TutorialMap_01V.unity";
    private const string SafeViewSessionKey = "GrayZone.SceneView.SafeViewApplied";

    private static readonly Vector3 SafePivot = new Vector3(0f, 10f, 0f);
    private static readonly Quaternion SafeRotation = Quaternion.Euler(50f, 315f, 0f);

    static GrayZoneSceneViewQualityGuard()
    {
        SceneView.duringSceneGui += OnSceneGui;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        ScheduleApply();
    }

    [MenuItem("GrayZone/Scene View/Reset Safe Isometric View")]
    private static void ResetSafeIsometricView()
    {
        if (!IsTargetSceneActive())
        {
            return;
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        ApplyCameraSettings(sceneView);
        SetSafeOverview(sceneView);
        SessionState.SetBool(SafeViewSessionKey, true);
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == TargetScenePath)
        {
            ScheduleApply();
        }
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (IsTargetSceneActive())
        {
            ApplyCameraSettings(sceneView);
        }
    }

    private static void ScheduleApply()
    {
        EditorApplication.delayCall -= ApplyDelayed;
        EditorApplication.delayCall += ApplyDelayed;
    }

    private static void ApplyDelayed()
    {
        if (!IsTargetSceneActive())
        {
            return;
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        ApplyCameraSettings(sceneView);

        if (!SessionState.GetBool(SafeViewSessionKey, false))
        {
            SetSafeOverview(sceneView);
            SessionState.SetBool(SafeViewSessionKey, true);
        }
    }

    private static bool IsTargetSceneActive()
    {
        return SceneManager.GetActiveScene().path == TargetScenePath;
    }

    private static void ApplyCameraSettings(SceneView sceneView)
    {
        SceneView.CameraSettings settings = sceneView.cameraSettings;
        settings.dynamicClip = false;
        settings.nearClip = 0.01f;
        settings.farClip = 2000f;
        settings.occlusionCulling = false;
    }

    private static void SetSafeOverview(SceneView sceneView)
    {
        sceneView.orthographic = true;
        sceneView.LookAtDirect(SafePivot, SafeRotation, 115f);
        sceneView.Repaint();
    }
}
