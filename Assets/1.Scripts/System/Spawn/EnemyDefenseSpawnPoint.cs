using System.Collections.Generic;
using UnityEngine;
using VInspector;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 범용 적 생산 위에 Defense 전용 경로와 목표 설정을 추가하는 스폰 포인트입니다.
/// </summary>
/// <remarks>
/// 공통 풀링·생산·Spawn SO 적용 순서는 <see cref="EnemySpawnPoint"/>가 담당합니다.
/// 이 파생형은 생성된 적을 Defense 적으로 표시하고 웨이포인트와 최종 목표만 추가로 주입합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemyDefenseSpawnPoint : EnemySpawnPoint
{
    [Foldout("Defense Route")]
    [Tooltip("이 지점에서 생성된 적이 먼저 순서대로 통과할 웨이포인트 목록입니다. 비어 있으면 Defense 성향에 따른 목표 선택을 즉시 시작합니다.")]
    [SerializeField] private List<Transform> m_waypoints = new List<Transform>();

    [Tooltip("웨이포인트 통과 후 향할 외부 방어선 또는 방어 목표 위치입니다. Player First 적도 유효한 플레이어가 없으면 이 위치를 사용합니다.")]
    [EndFoldout]
    [SerializeField] private Transform m_targetPosition;

    /// <summary>생성된 Defense 적이 순서대로 통과할 웨이포인트 목록입니다.</summary>
    public IReadOnlyList<Transform> Waypoints => m_waypoints;

    /// <summary>웨이포인트 통과 후 사용할 외부 방어선 또는 방어 목표 위치입니다.</summary>
    public Transform TargetPosition => m_targetPosition;

    /// <inheritdoc />
    protected override void ConfigureSpawnedEnemy(EnemyController enemy, EnemySpawnEntrySO entry)
    {
        base.ConfigureSpawnedEnemy(enemy, entry);

        if (enemy != null)
        {
            enemy.ConfigureDefenseSpawn(m_waypoints, m_targetPosition);
        }
    }

    /// <inheritdoc />
    protected override void ClearSpawnedEnemyConfiguration(EnemyController enemy)
    {
        enemy?.ClearDefenseSpawnConfiguration();
        base.ClearSpawnedEnemyConfiguration(enemy);
    }

    /// <summary>이 Defense 스폰 포인트의 자식으로 새 웨이포인트를 만들고 경로 목록 끝에 추가합니다.</summary>
    /// <remarks>에디터에서만 동작하며 Undo와 Scene dirty 처리를 함께 수행합니다.</remarks>
    [Button("Add Waypoint Child")]
    [ContextMenu("Add Waypoint Child")]
    private void AddWaypointChild()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("[EnemyDefenseSpawnPoint] Play Mode에서는 웨이포인트 자식을 만들지 않습니다.", this);
            return;
        }

        GameObject child = new GameObject($"Waypoint {m_waypoints.Count + 1}");
        Undo.RegisterCreatedObjectUndo(child, "Add Enemy Defense Waypoint");
        child.transform.SetParent(transform, false);

        Undo.RecordObject(this, "Add Enemy Defense Waypoint");
        m_waypoints.Add(child.transform);
        EditorUtility.SetDirty(this);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Selection.activeGameObject = child;
#endif
    }
}
