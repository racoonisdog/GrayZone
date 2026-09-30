using System.Collections.Generic;
using UnityEngine;
using VInspector;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 범용 적 생산 위에 Defense 전용 경로, 목표 설정, 웨이브 실행을 추가하는 스폰 포인트입니다.
/// </summary>
/// <remarks>
/// 공통 풀링·생산·Spawn SO 적용 순서는 <see cref="EnemySpawnPoint"/>가 담당합니다.
/// 이 파생형은 생성된 적을 Defense 적으로 표시하고 웨이포인트와 최종 목표를 주입합니다.
///
/// <b>웨이브 실행</b>: DefenseManager가 <see cref="PrepareForStage"/>로 방어전 SO를 넘기면, 이 스포너의
/// 공격로 이름에 해당하는 웨이브 전체의 항목으로 풀을 준비하고 자동 생산을 멈춥니다. 이후
/// <see cref="BeginCombat"/>으로 받은 웨이브 SO를 실행합니다. 첫 스폰 대기 뒤 후보 그룹 하나를 골라 통째로 생성하고,
/// 다음 간격을 다시 뽑아 기다리기를 <see cref="EndCombat"/>까지 반복합니다. 그룹이 생존 정원에 들어가지 않으면
/// 같은 그룹을 들고 자리가 날 때까지 기다리며, 간격은 실제로 생성한 시점부터 다시 셉니다.
/// 인스펙터 항목 목록(Spawn Entries)으로 계속 생산하는 기존 경로는 방어전 스포너에서 쓰지 않습니다.
/// 설계 근거: privateDoc `DEFENSE_WAVE_SPAWN_SPEC_KR.md` §4.5, §5.
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemyDefenseSpawnPoint : EnemySpawnPoint
{
    [Foldout("Defense Route")]
    [Tooltip("이 스포너의 공격로 이름입니다. 방어전 SO의 공격로 이름과 같아야 그 공격로의 웨이브를 실행합니다. 예: Bridge, Tunnel. 대소문자를 구분하고 앞뒤 공백은 무시합니다.")]
    [SerializeField] private string m_routeId;

    [Tooltip("이 지점에서 생성된 적이 먼저 순서대로 통과할 웨이포인트 목록입니다. 비어 있으면 Defense 성향에 따른 목표 선택을 즉시 시작합니다.")]
    [SerializeField] private List<Transform> m_waypoints = new List<Transform>();

    [Tooltip("웨이포인트 통과 후 향할 외부 방어선 또는 방어 목표 위치입니다. Player First 적도 유효한 플레이어가 없으면 이 위치를 사용합니다.")]
    [EndFoldout]
    [SerializeField] private Transform m_targetPosition;

    [Foldout("Wave Debug")]
    [Tooltip("에디터 확인용입니다. Play Mode에서 아래 버튼으로 이 스포너 하나만 이 웨이브를 실행해 봅니다. DefenseManager를 거치지 않습니다.")]
    [SerializeField] private DefenseWaveSO m_debugWave;

    [Tooltip("이 스포너가 지금 실행 중인 웨이브 SO입니다. 표시 전용입니다.")]
    [ReadOnly]
    [SerializeField] private DefenseWaveSO m_currentWave;

    [Tooltip("다음 그룹을 고를 시각까지 남은 시간(초)입니다. 그룹이 자리를 기다리는 중이면 0입니다. 표시 전용입니다.")]
    [ReadOnly]
    [EndFoldout]
    [SerializeField] private float m_debugTimeToNextSpawn;

    /// <summary>지금 전투 중이라 웨이브를 실행하고 있는지 여부입니다.</summary>
    private bool m_combatActive;

    /// <summary>다음 그룹을 고를 시각(Time.time)입니다.</summary>
    private float m_nextSpawnTime;

    /// <summary>골랐지만 생존 정원이 모자라 아직 생성하지 못한 그룹입니다. 없으면 null입니다.</summary>
    private SpawnGroupSO m_pendingGroup;

    /// <summary>생성된 Defense 적이 순서대로 통과할 웨이포인트 목록입니다.</summary>
    public IReadOnlyList<Transform> Waypoints => m_waypoints;

    /// <summary>웨이포인트 통과 후 사용할 외부 방어선 또는 방어 목표 위치입니다.</summary>
    public Transform TargetPosition => m_targetPosition;

    /// <summary>이 스포너의 공격로 이름입니다. 앞뒤 공백은 뺍니다.</summary>
    public string RouteId => m_routeId != null ? m_routeId.Trim() : string.Empty;

    /// <summary>방어전 스포너는 웨이브 SO로만 생성하므로 인스펙터의 Spawn Entries 목록을 숨깁니다.</summary>
    protected override bool HidesInspectorEntries => true;

    /// <summary>웨이브 SO를 실행 중인지(전투 중이고 이번 웨이브에 쉬지 않는지) 여부입니다.</summary>
    public bool IsRunningWave => m_combatActive && m_currentWave != null;

    /// <summary>
    /// 방어전 SO에서 이 공격로가 쓰는 항목으로 풀을 준비하고 웨이브 실행 모드로 바꿉니다.
    /// </summary>
    /// <param name="stage">이번 방어전의 구성입니다. 이 공격로가 없으면 빈 풀로 준비되고 모든 웨이브를 쉽니다.</param>
    /// <remarks>방어전을 시작할 때마다 부릅니다. 진행 중이던 전투는 끝냅니다.</remarks>
    public void PrepareForStage(DefenseStageSO stage)
    {
        EndCombat();

        var entries = new HashSet<EnemySpawnEntrySO>();
        stage?.CollectEntries(RouteId, entries);
        UseExternalEntries(entries);
    }

    /// <summary>
    /// 전투를 시작하고 이번 웨이브 SO를 실행합니다.
    /// </summary>
    /// <param name="wave">이번 웨이브에 이 공격로가 실행할 웨이브 SO입니다. null이면 이번 전투는 쉽니다.</param>
    /// <remarks>
    /// 첫 스폰 대기는 이 순간부터 셉니다. 풀 준비가 끝나지 않았으면 준비가 끝난 뒤에 첫 그룹이 나옵니다.
    /// 웨이브 실행 모드가 아니면(<see cref="PrepareForStage"/>를 부르지 않았으면) 경고하고 무시합니다.
    /// </remarks>
    public void BeginCombat(DefenseWaveSO wave)
    {
        if (!UsesExternalEntries)
        {
            Debug.LogWarning($"[EnemyDefenseSpawnPoint] '{name}': PrepareForStage 전에 BeginCombat이 호출돼 무시합니다.", this);
            return;
        }

        m_combatActive = true;
        m_currentWave = wave;
        m_pendingGroup = null;
        m_nextSpawnTime = wave != null ? Time.time + wave.SampleFirstSpawnDelay() : 0.0f;
    }

    /// <summary>전투를 끝냅니다. 새 그룹을 더 만들지 않고, 기다리던 그룹은 버립니다.</summary>
    /// <remarks>이미 나와 있는 적은 그대로 둡니다. 남은 적 처리는 DefenseManager가 합니다.</remarks>
    public void EndCombat()
    {
        m_combatActive = false;
        m_currentWave = null;
        m_pendingGroup = null;
        m_debugTimeToNextSpawn = 0.0f;
    }

    /// <summary>
    /// 인스펙터 항목 목록으로 자동 생산하지 않도록, 빈 외부 항목 모드로 시작합니다.
    /// </summary>
    /// <remarks>
    /// 방어전 스포너는 DefenseManager가 넘긴 웨이브 SO로만 생성합니다. 풀은 <see cref="PrepareForStage"/>에서 준비합니다.
    /// 인스펙터의 Spawn Entries 목록은 방어전 스포너에서 쓰지 않습니다.
    /// </remarks>
    protected override void Start()
    {
        if (!UsesExternalEntries)
        {
            UseExternalEntries(null);
        }

        base.Start();
    }

    /// <inheritdoc />
    protected override void Update()
    {
        base.Update();
        TickWave();
    }

    /// <summary>
    /// 전투 중이면 간격에 맞춰 그룹을 고르고, 자리가 있으면 통째로 생성합니다.
    /// </summary>
    /// <remarks>
    /// 간격은 그룹을 실제로 생성한 시점부터 다시 셉니다. 고른 시점부터 세면, 자리를 오래 기다린 뒤 생성하자마자
    /// 다음 그룹이 곧바로 나와 연달아 몰려나오기 때문입니다.
    /// </remarks>
    private void TickWave()
    {
        if (!IsRunningWave || !IsPoolReady)
        {
            return;
        }

        if (m_pendingGroup == null)
        {
            m_debugTimeToNextSpawn = Mathf.Max(0.0f, m_nextSpawnTime - Time.time);
            if (Time.time < m_nextSpawnTime)
            {
                return;
            }

            m_pendingGroup = m_currentWave.PickGroup();
            if (m_pendingGroup == null)
            {
                // 고를 수 있는 그룹이 없으면 간격만 다시 뽑아 기다립니다. 매 프레임 다시 고르지 않게 하기 위해서입니다.
                m_nextSpawnTime = Time.time + m_currentWave.SampleSpawnInterval();
                return;
            }
        }

        m_debugTimeToNextSpawn = 0.0f;
        if (!TrySpawnGroup(m_pendingGroup))
        {
            return;
        }

        m_pendingGroup = null;
        m_nextSpawnTime = Time.time + m_currentWave.SampleSpawnInterval();
    }

    /// <summary>에디터 확인용: 이 스포너 하나만 확인용 웨이브로 풀을 준비하고 전투를 시작합니다.</summary>
    [Button("Debug: Begin Wave")]
    private void DebugBeginWave()
    {
        if (!Application.isPlaying || m_debugWave == null)
        {
            Debug.LogWarning("[EnemyDefenseSpawnPoint] Play Mode에서 Debug Wave를 지정한 뒤 실행하세요.", this);
            return;
        }

        var entries = new HashSet<EnemySpawnEntrySO>();
        m_debugWave.CollectEntries(entries);
        EndCombat();
        UseExternalEntries(entries);
        BeginCombat(m_debugWave);
    }

    /// <summary>에디터 확인용: 전투를 끝냅니다.</summary>
    [Button("Debug: End Wave")]
    private void DebugEndWave()
    {
        EndCombat();
    }

    /// <inheritdoc />
    protected override void ConfigureSpawnedEnemy(EnemyController enemy, EnemySpawnEntrySO entry)
    {
        base.ConfigureSpawnedEnemy(enemy, entry);

        if (enemy != null && entry != null)
        {
            enemy.ConfigureDefenseSpawn(
                m_waypoints,
                m_targetPosition,
                entry.PrioritizeWaypointsForPlayerFirst);
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
