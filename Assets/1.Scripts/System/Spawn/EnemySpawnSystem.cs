using System.Collections.Generic;
using UnityEngine;
using VInspector;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 자식 <see cref="EnemySpawnPoint"/>의 생성과 공통 Inspector 설정 일괄 적용만 담당하는 에디터 중심 스폰 매니저입니다.
/// </summary>
/// <remarks>
/// 런타임 풀, 생산 주기, 적 생성 수용량은 모두 자식 스폰 포인트가 소유합니다.
/// 이 컴포넌트의 템플릿 값은 버튼을 눌렀을 때만 자식에게 복사되므로, 개별 자식 설정은 버튼 실행 전까지 독립적으로 유지됩니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemySpawnSystem : MonoBehaviour
{
    [Foldout("Spawn Point Template")]
    [Tooltip("새 자식 스폰 포인트에 넣고, 일괄 적용 버튼으로 기존 자식에도 복사할 적 프리팹별 생산 설정 SO 목록입니다. SO 하나마다 자식에서 독립 풀을 만듭니다.")]
    [SerializeField] private List<EnemySpawnEntrySO> m_templateSpawnEntries = new List<EnemySpawnEntrySO>();

    [Tooltip("새 자식과 일괄 적용 대상의 월드 X/Z 무작위 스폰 범위(m)입니다.")]
    [SerializeField] private Vector2 m_templateSpawnAreaSize = new Vector2(10.0f, 10.0f);

    [Tooltip("새 자식과 일괄 적용 대상에서 직전 스폰 위치와 떨어져야 하는 X/Z 최소 거리(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_templateMinimumSpawnDistance = 1.5f;

    [Tooltip("새 자식과 일괄 적용 대상의 신규 배치 생산 허용 상태입니다. 끄면 기존 활성 적은 유지됩니다.")]
    [SerializeField] private bool m_templateSpawnEnabled = true;

    [EndFoldout]
    [Foldout("Debug")]
    [Tooltip("선택된 매니저의 모든 자식 스폰 범위를 Scene View에 표시합니다. 에디터 진단용이며 런타임 생성 규칙에는 영향을 주지 않습니다.")]
    [SerializeField] private bool m_debugDrawChildSpawnAreas;

    [EndFoldout]

    /// <summary>템플릿 Inspector 값이 유효 범위에 남도록 보정합니다.</summary>
    private void OnValidate()
    {
        m_templateSpawnAreaSize = new Vector2(
            Mathf.Max(0.0f, m_templateSpawnAreaSize.x),
            Mathf.Max(0.0f, m_templateSpawnAreaSize.y));
        m_templateMinimumSpawnDistance = Mathf.Max(0.0f, m_templateMinimumSpawnDistance);
    }

    /// <summary>템플릿 값을 적용한 자식 스폰 포인트를 생성합니다.</summary>
    /// <remarks>에디터에서는 Undo를 지원하고, 생성 뒤 새 자식을 선택해 Scene View에서 즉시 위치를 옮길 수 있습니다.</remarks>
    [Foldout("Spawn Point Template")]
    [Button("Add Spawn Point")]
    private void AddSpawnPointChild()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("[EnemySpawnSystem] Play Mode에서는 자식 스폰 포인트를 만들지 않습니다.", this);
            return;
        }

        GameObject child = new GameObject("Enemy Spawn Point");
        Undo.RegisterCreatedObjectUndo(child, "Add Enemy Spawn Point");
        child.transform.SetParent(transform, false);

        EnemySpawnPoint point = Undo.AddComponent<EnemySpawnPoint>(child);
        Undo.RecordObject(point, "Configure Enemy Spawn Point");
        ApplyTemplate(point);
        EditorUtility.SetDirty(point);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Selection.activeGameObject = child;
#endif
    }

    /// <summary>현재 매니저 아래의 모든 자식 스폰 포인트에 템플릿 Inspector 값을 복사합니다.</summary>
    /// <remarks>실행 전 개별 자식 값은 유지되고, 버튼을 눌렀을 때만 덮어씁니다. 비활성 자식도 함께 적용합니다.</remarks>
    [Foldout("Spawn Point Template")]
    [Button("Apply Template To Child Spawn Points")]
    private void ApplyTemplateToChildSpawnPoints()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("[EnemySpawnSystem] Play Mode에서는 자식 스폰 포인트 설정을 일괄 적용하지 않습니다.", this);
            return;
        }

        EnemySpawnPoint[] points = GetComponentsInChildren<EnemySpawnPoint>(true);
        for (int i = 0; i < points.Length; i++)
        {
            EnemySpawnPoint point = points[i];
            Undo.RecordObject(point, "Apply Enemy Spawn Point Template");
            ApplyTemplate(point);
            EditorUtility.SetDirty(point);
        }

        EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    /// <summary>템플릿 값을 지정 자식 스폰 포인트에 복사합니다.</summary>
    /// <param name="point">설정을 적용할 자식 스폰 포인트입니다.</param>
    private void ApplyTemplate(EnemySpawnPoint point)
    {
        if (point == null)
        {
            return;
        }

        point.ApplyConfiguration(
            m_templateSpawnEntries,
            m_templateSpawnAreaSize,
            m_templateMinimumSpawnDistance,
            m_templateSpawnEnabled);
    }

    /// <summary>선택된 매니저의 자식 스폰 범위를 Scene View에 한꺼번에 표시합니다.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawChildSpawnAreas)
        {
            return;
        }

        EnemySpawnPoint[] points = GetComponentsInChildren<EnemySpawnPoint>(true);
        Gizmos.color = new Color(1.0f, 0.7f, 0.15f, 0.7f);

        for (int i = 0; i < points.Length; i++)
        {
            EnemySpawnPoint point = points[i];
            if (point == null)
            {
                continue;
            }

            Vector2 areaSize = point.SpawnAreaSize;
            Gizmos.DrawWireCube(
                point.transform.position,
                new Vector3(areaSize.x, 0.05f, areaSize.y));
        }
    }
}
