using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling.Memory;
using UnityEngine;
using VInspector;

/// <summary>
/// 프로파일링·캡처용 고정 시점 카메라입니다. 켜면 다른 카메라의 렌더링을 끄고 이 시점만 그려, 매번 같은 장면으로 비교할 수 있게 합니다.
/// </summary>
/// <remarks>
/// 기본은 꺼진 상태라 평소 게임 렌더링에 영향을 주지 않습니다. 다른 카메라는 Camera 컴포넌트의 렌더링만 끄고
/// 오브젝트와 CinemachineBrain 등은 그대로 두므로, 끄면 원래 상태로 돌아갑니다.
/// 메모리 스냅샷은 프로젝트 루트의 MemoryCaptures 폴더에 저장되며 Memory Profiler 창에서 열 수 있습니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class ProfilingCaptureCamera : MonoBehaviour
{
    [Tooltip("켜면 Play 시작 시 이 카메라 시점으로 바로 전환합니다. 디버그 전용입니다.")]
    [SerializeField] private bool m_activateOnPlay;

    [Tooltip("메모리 스냅샷 파일 이름 앞에 붙일 문구입니다. 비교할 조건(예: before, after)을 적습니다.")]
    [SerializeField] private string m_snapshotLabel = "profiling";

    private readonly List<Camera> m_disabledCameras = new List<Camera>();
    private Camera m_camera;

    /// <summary>이 카메라 시점이 켜져 있는지 여부입니다.</summary>
    public bool IsActive => m_camera != null && m_camera.enabled;

    /// <summary>마지막으로 요청한 스냅샷의 저장 경로입니다. 저장이 끝나기 전에는 파일이 없을 수 있습니다.</summary>
    public string LastSnapshotPath { get; private set; }

    /// <summary>마지막 스냅샷 저장이 끝났고 성공했는지 여부입니다.</summary>
    public bool? LastSnapshotSucceeded { get; private set; }

    private void Awake()
    {
        m_camera = GetComponent<Camera>();
        m_camera.enabled = false;
    }

    private void Start()
    {
        if (m_activateOnPlay)
        {
            Activate();
        }
    }

    private void OnDisable()
    {
        Deactivate();
    }

    /// <summary>다른 카메라의 렌더링을 끄고 이 카메라 시점으로 전환합니다.</summary>
    [Button("시점 켜기")]
    public void Activate()
    {
        if (m_camera == null || IsActive)
        {
            return;
        }

        m_disabledCameras.Clear();
        foreach (Camera other in Camera.allCameras)
        {
            if (other != m_camera && other.enabled)
            {
                other.enabled = false;
                m_disabledCameras.Add(other);
            }
        }

        m_camera.enabled = true;
    }

    /// <summary>이 카메라를 끄고, 켤 때 꺼 두었던 카메라를 다시 켭니다.</summary>
    [Button("시점 끄기")]
    public void Deactivate()
    {
        if (m_camera == null || !IsActive)
        {
            return;
        }

        m_camera.enabled = false;
        foreach (Camera other in m_disabledCameras)
        {
            if (other != null)
            {
                other.enabled = true;
            }
        }

        m_disabledCameras.Clear();
    }

    /// <summary>이 시점으로 전환한 상태에서 메모리 스냅샷을 찍습니다. 저장은 비동기로 끝납니다.</summary>
    [Button("메모리 스냅샷")]
    public void TakeMemorySnapshot()
    {
        TakeMemorySnapshot(m_snapshotLabel);
    }

    /// <summary>이 시점으로 전환한 상태에서 메모리 스냅샷을 찍습니다. 저장은 비동기로 끝납니다.</summary>
    /// <param name="label">파일 이름 앞에 붙일 문구입니다.</param>
    public void TakeMemorySnapshot(string label)
    {
        Activate();
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "MemoryCaptures");
        Directory.CreateDirectory(folder);
        string safeLabel = string.IsNullOrWhiteSpace(label) ? "profiling" : label.Trim();
        LastSnapshotPath = Path.Combine(folder, $"{safeLabel}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.snap");
        LastSnapshotSucceeded = null;
        MemoryProfiler.TakeSnapshot(LastSnapshotPath, (path, success) => LastSnapshotSucceeded = success);
    }
}
