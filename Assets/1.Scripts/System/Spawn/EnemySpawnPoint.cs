using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VInspector;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 범위 안에서 여러 <see cref="EnemySpawnEntrySO"/> 생산 항목을 독립적으로 풀링·생성하는 스폰 지점입니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 위치, X/Z 범위, 최소 위치 간격과 신규 생산 허용 상태만 소유합니다.
/// 적 프리팹별 생산 규칙은 <see cref="m_spawnEntries"/>의 SO가 소유하며, SO 하나마다 독립 풀과 생산 타이머를 만듭니다.
/// 적이 사망하면 <see cref="EnemyManager.Entry.CorpseLifetime"/> 동안 래그돌을 유지한 뒤 해당 SO의 풀로 반납합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemySpawnPoint : MonoBehaviour
{
    /// <summary>풀 안의 한 적 인스턴스와 사망 뒤 복원할 상태를 보관합니다.</summary>
    private sealed class PoolItem
    {
        /// <summary>이 항목이 속한 프리팹별 런타임 풀입니다.</summary>
        public SpawnRuntime Runtime;

        /// <summary>재사용할 적 인스턴스입니다.</summary>
        public EnemyController Enemy;

        /// <summary>사망 이벤트를 수신할 체력 컴포넌트입니다.</summary>
        public EnemyHealth Health;

        /// <summary>사망 시 래그돌 유지 상태로 전환하는 콜백입니다.</summary>
        public Action DeathHandler;

        /// <summary>시체 유지 시간이 끝났을 때 이 항목을 풀로 반납하는 콜백입니다.</summary>
        public Func<bool> CorpseLifetimeHandler;

        /// <summary>사망 처리에서 비활성화될 수 있는 NavMeshAgent입니다.</summary>
        public NavMeshAgent Agent;

        /// <summary>프리팹 최초 상태의 NavMeshAgent 활성 여부입니다.</summary>
        public bool InitialAgentEnabled;

        /// <summary>프리팹 최초 상태의 NavMeshAgent 위치 갱신 여부입니다.</summary>
        public bool InitialAgentUpdatePosition;

        /// <summary>프리팹 최초 상태의 NavMeshAgent 회전 갱신 여부입니다.</summary>
        public bool InitialAgentUpdateRotation;

        /// <summary>사망 시 비활성화될 수 있는 모든 Collider입니다.</summary>
        public Collider[] Colliders;

        /// <summary>프리팹 최초 상태의 Collider 활성 여부입니다.</summary>
        public bool[] InitialColliderEnabled;

        /// <summary>현재 월드에 활성화되어 수용량을 점유하는지 여부입니다.</summary>
        public bool IsActive;

        /// <summary>사망 뒤 래그돌 유지 시간이 끝나기를 기다리는지 여부입니다.</summary>
        public bool IsAwaitingCorpseReturn;
    }

    /// <summary>SO 하나에 대응하는 독립 풀, 생산 시각과 활성 수를 보관합니다.</summary>
    private sealed class SpawnRuntime
    {
        /// <summary>이 런타임 상태가 읽는 생산 설정 에셋입니다.</summary>
        public EnemySpawnEntrySO Entry;

        /// <summary>이 런타임이 실제로 생성한 프리팹입니다. SO의 프리팹이 바뀌면 기존 풀은 퇴역 처리합니다.</summary>
        public EnemyController Prefab;

        /// <summary>이 SO 프리팹으로 생성한 전체 풀 항목입니다.</summary>
        public readonly List<PoolItem> PoolItems = new List<PoolItem>();

        /// <summary>즉시 재사용할 수 있는 비활성 풀 항목입니다.</summary>
        public readonly List<PoolItem> AvailableItems = new List<PoolItem>();

        /// <summary>현재 살아 있거나 래그돌 유지 중이라 수용량을 점유하는 항목 수입니다.</summary>
        public int ActiveEnemyCount;

        /// <summary>다음 배치 생산을 시도할 게임 시간입니다.</summary>
        public float NextProductionTime;

        /// <summary>Inspector 목록에서 제거됐거나 프리팹이 바뀌어 신규 생산을 중단한 옛 런타임인지 여부입니다.</summary>
        public bool IsRetired;

        /// <summary>프리팹 누락 경고를 반복 출력하지 않기 위한 상태입니다.</summary>
        public bool MissingPrefabWarningLogged;
    }

    private const int SpawnPositionSearchAttempts = 16;

    [Header("Spawn Entries")]
    [Tooltip("이 지점이 운용할 적 프리팹별 생산 설정 에셋입니다. 리스트의 SO 하나마다 독립 풀과 생산 타이머가 만들어집니다.")]
    [SerializeField] private List<EnemySpawnEntrySO> m_spawnEntries = new List<EnemySpawnEntrySO>();

    [Header("Spawn Area")]
    [Tooltip("스폰 지점의 월드 X/Z 좌표를 중심으로 적을 무작위 생성할 가로·세로 범위(m)입니다. Y 좌표는 이 오브젝트 위치를 그대로 사용합니다.")]
    [SerializeField] private Vector2 m_spawnAreaSize = new Vector2(10.0f, 10.0f);

    [Tooltip("모든 생산 항목을 통틀어 직전 성공 스폰 위치의 X/Z 반경 중 다음 적을 만들지 않을 최소 거리(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_minimumSpawnDistance = 1.5f;

    [Header("Defense Route")]
    [Tooltip("이 지점에서 생성된 적이 먼저 순서대로 통과할 웨이포인트 목록입니다. 비어 있으면 대상 우선 성향에 따른 목표 선택을 즉시 시작합니다.")]
    [SerializeField] private List<Transform> m_waypoints = new List<Transform>();

    [Tooltip("웨이포인트 통과 후 향할 외부 방어선 또는 방어 목표 위치입니다. 플레이어 우선 적도 감지 가능한 플레이어가 없으면 이 위치를 사용합니다.")]
    [SerializeField] private Transform m_targetPosition;

    [Header("Spawn State")]
    [Tooltip("켜면 각 SO의 생산 주기에 맞춰 풀 적을 활성화합니다. 끄면 앞으로의 배치 생산만 멈추며 이미 활성화된 적은 제거하지 않습니다.")]
    [SerializeField] private bool m_spawnEnabled = true;

    /// <summary>이 지점이 운용할 프리팹별 생산 설정 목록입니다.</summary>
    public IReadOnlyList<EnemySpawnEntrySO> SpawnEntries => m_spawnEntries;

    /// <summary>월드 X/Z 평면에서 사용할 무작위 생성 범위입니다.</summary>
    public Vector2 SpawnAreaSize => m_spawnAreaSize;

    /// <summary>직전 성공 위치와 다음 위치 사이에 보장할 X/Z 최소 거리입니다.</summary>
    public float MinimumSpawnDistance => m_minimumSpawnDistance;

    /// <summary>생성된 적이 순서대로 통과할 웨이포인트 목록입니다.</summary>
    public IReadOnlyList<Transform> Waypoints => m_waypoints;

    /// <summary>웨이포인트 통과 후 사용할 외부 방어선 또는 방어 목표 위치입니다.</summary>
    public Transform TargetPosition => m_targetPosition;

    /// <summary>신규 배치 생산을 허용하는지 여부입니다.</summary>
    public bool IsSpawnEnabled => m_spawnEnabled;

    /// <summary>모든 SO 풀에서 현재 수용량을 점유하는 적 수입니다. 래그돌 유지 중인 시체도 반환 전까지 포함합니다.</summary>
    public int ActiveEnemyCount => GetActiveEnemyCount();

    /// <summary>모든 SO 풀의 전체 항목 수입니다. 비활성 대기 적과 활성 적을 모두 포함합니다.</summary>
    public int PoolCount => GetPoolCount();

    /// <summary>SO마다 독립 풀과 생산 상태를 보관합니다.</summary>
    private readonly List<SpawnRuntime> m_spawnRuntimes = new List<SpawnRuntime>();

    /// <summary>SO Inspector 변경 이벤트를 받고 있는 고유 에셋 목록입니다.</summary>
    private readonly List<EnemySpawnEntrySO> m_subscribedEntries = new List<EnemySpawnEntrySO>();

    /// <summary>비활성 적을 정리해 둘 런타임 전용 부모입니다. 활성 적은 월드 루트로 분리합니다.</summary>
    private Transform m_poolRoot;

    /// <summary>다음 Update에서 SO 목록과 런타임 풀 구성을 한 번 동기화해야 하는지 여부입니다.</summary>
    private bool m_runtimeSynchronizationPending;

    /// <summary>Start를 지나 풀을 만들 수 있는 상태인지 여부입니다.</summary>
    private bool m_runtimeInitialized;

    /// <summary>Inspector 또는 외부 직렬화 변경으로 Spawn Enabled가 다시 켜지는 순간을 감지할 이전 값입니다.</summary>
    private bool m_lastSpawnEnabledState;

    /// <summary>직전 성공 스폰 위치가 있는지 여부입니다.</summary>
    private bool m_hasLastSpawnPosition;

    /// <summary>다음 후보 위치에서 최소 거리 검사를 할 직전 성공 스폰 위치입니다.</summary>
    private Vector3 m_lastSpawnPosition;

    /// <summary>활성화될 때 SO 변경 이벤트를 연결하고, 재활성화라면 다음 프레임 동기화를 예약합니다.</summary>
    private void OnEnable()
    {
        RefreshEntrySubscriptions();

        if (Application.isPlaying && m_runtimeInitialized)
        {
            m_runtimeSynchronizationPending = true;
            RestartInitialSpawnDelay();
        }
    }

    /// <summary>게임 시작 시 등록된 SO마다 최대 수용량만큼 비활성 풀을 준비합니다.</summary>
    private void Start()
    {
        EnsurePoolRoot();
        m_runtimeInitialized = true;
        m_lastSpawnEnabledState = m_spawnEnabled;
        SynchronizeSpawnRuntimes();
    }

    /// <summary>각 SO의 생산 주기와 풀 수용량을 독립적으로 처리합니다.</summary>
    private void Update()
    {
        if (!m_runtimeInitialized)
        {
            return;
        }

        if (m_lastSpawnEnabledState != m_spawnEnabled)
        {
            m_lastSpawnEnabledState = m_spawnEnabled;
            if (m_spawnEnabled)
            {
                RestartInitialSpawnDelay();
            }
        }

        if (m_runtimeSynchronizationPending)
        {
            SynchronizeSpawnRuntimes();
        }

        for (int i = m_spawnRuntimes.Count - 1; i >= 0; i--)
        {
            SpawnRuntime runtime = m_spawnRuntimes[i];
            RemoveExternallyDestroyedItems(runtime);

            if (runtime.IsRetired)
            {
                if (runtime.ActiveEnemyCount == 0)
                {
                    DisposeRuntime(runtime);
                    m_spawnRuntimes.RemoveAt(i);
                }

                continue;
            }

            if (!m_spawnEnabled || runtime.Entry == null)
            {
                continue;
            }

            EnsurePoolCapacity(runtime, runtime.Entry.MaxCapacity);

            if (Time.time < runtime.NextProductionTime)
            {
                continue;
            }

            ProduceBatch(runtime);
            runtime.NextProductionTime = Time.time + runtime.Entry.ProductionInterval;
        }
    }

    /// <summary>비활성화될 때 SO 변경 이벤트 연결을 해제합니다.</summary>
    private void OnDisable()
    {
        UnsubscribeEntryChanges();
    }

    /// <summary>스폰 지점 제거 시 풀 항목과 SO 이벤트가 이 지점을 계속 참조하지 않도록 해제합니다.</summary>
    private void OnDestroy()
    {
        UnsubscribeEntryChanges();

        for (int runtimeIndex = 0; runtimeIndex < m_spawnRuntimes.Count; runtimeIndex++)
        {
            SpawnRuntime runtime = m_spawnRuntimes[runtimeIndex];
            for (int itemIndex = 0; itemIndex < runtime.PoolItems.Count; itemIndex++)
            {
                UnsubscribePoolItem(runtime.PoolItems[itemIndex]);
            }
        }
    }

    /// <summary>Inspector에서 입력한 영역 값이 유효한 하한을 지키도록 보정하고, Play Mode에서는 동기화를 예약합니다.</summary>
    private void OnValidate()
    {
        m_spawnAreaSize = new Vector2(
            Mathf.Max(0.0f, m_spawnAreaSize.x),
            Mathf.Max(0.0f, m_spawnAreaSize.y));
        m_minimumSpawnDistance = Mathf.Max(0.0f, m_minimumSpawnDistance);

        RefreshEntrySubscriptions();
        m_runtimeSynchronizationPending = true;
    }

    /// <summary>이 스폰 포인트의 자식으로 새 웨이포인트를 만들고 경로 목록 끝에 추가합니다.</summary>
    /// <remarks>에디터에서만 동작하며 Undo와 Scene dirty 처리를 함께 수행합니다.</remarks>
    [Button("Add Waypoint Child")]
    [ContextMenu("Add Waypoint Child")]
    private void AddWaypointChild()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("[EnemySpawnPoint] Play Mode에서는 웨이포인트 자식을 만들지 않습니다.", this);
            return;
        }

        GameObject child = new GameObject($"Waypoint {m_waypoints.Count + 1}");
        Undo.RegisterCreatedObjectUndo(child, "Add Enemy Waypoint");
        child.transform.SetParent(transform, false);

        Undo.RecordObject(this, "Add Enemy Waypoint");
        m_waypoints.Add(child.transform);
        EditorUtility.SetDirty(this);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Selection.activeGameObject = child;
#endif
    }

    /// <summary>스폰 매니저가 자식 지점에 공통 Inspector 설정을 복사할 때 사용합니다.</summary>
    /// <remarks>SO 자체의 생산 값은 복사하지 않고 참조 목록만 복사합니다. Play Mode에서 호출하면 다음 Update에 런타임 구성을 동기화합니다.</remarks>
    public void ApplyConfiguration(
        IList<EnemySpawnEntrySO> spawnEntries,
        Vector2 spawnAreaSize,
        float minimumSpawnDistance,
        bool spawnEnabled)
    {
        m_spawnEntries.Clear();
        if (spawnEntries != null)
        {
            for (int i = 0; i < spawnEntries.Count; i++)
            {
                m_spawnEntries.Add(spawnEntries[i]);
            }
        }

        m_spawnAreaSize = new Vector2(
            Mathf.Max(0.0f, spawnAreaSize.x),
            Mathf.Max(0.0f, spawnAreaSize.y));
        m_minimumSpawnDistance = Mathf.Max(0.0f, minimumSpawnDistance);
        m_spawnEnabled = spawnEnabled;

        RefreshEntrySubscriptions();
        m_runtimeSynchronizationPending = true;
    }

    /// <summary>신규 배치 생산을 허용하거나 정지합니다.</summary>
    /// <param name="isEnabled">true면 각 SO가 다음 프레임부터 생산을 다시 판정하고, false면 이후 생산을 멈춥니다.</param>
    /// <remarks>false여도 이미 활성화된 적과 래그돌 유지 중인 시체는 제거하지 않습니다.</remarks>
    public void SetSpawnEnabled(bool isEnabled)
    {
        if (m_spawnEnabled == isEnabled)
        {
            return;
        }

        m_spawnEnabled = isEnabled;
        m_lastSpawnEnabledState = isEnabled;

        if (isEnabled && Application.isPlaying)
        {
            RestartInitialSpawnDelay();
        }
    }

    /// <summary>현재 SO 목록을 읽어 런타임 풀을 추가·유지·퇴역 처리합니다.</summary>
    /// <remarks>
    /// 같은 SO를 목록에 두 번 넣어도 각 목록 칸은 독립 생산 항목으로 취급합니다.
    /// Play Mode 중 목록에서 빠진 항목은 새 적을 만들지 않고, 남은 활성 적이 반환된 뒤 안전하게 정리합니다.
    /// </remarks>
    private void SynchronizeSpawnRuntimes()
    {
        m_runtimeSynchronizationPending = false;
        RefreshEntrySubscriptions();

        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            m_spawnRuntimes[i].IsRetired = true;
        }

        for (int entryIndex = 0; entryIndex < m_spawnEntries.Count; entryIndex++)
        {
            EnemySpawnEntrySO entry = m_spawnEntries[entryIndex];
            if (entry == null)
            {
                continue;
            }

            SpawnRuntime runtime = FindRetiredRuntime(entry);
            if (runtime == null)
            {
                runtime = new SpawnRuntime
                {
                    Entry = entry,
                    Prefab = entry.EnemyPrefab,
                    NextProductionTime = Time.time + entry.InitialSpawnDelay,
                };
                m_spawnRuntimes.Add(runtime);
            }

            runtime.IsRetired = false;
            EnsurePoolCapacity(runtime, entry.MaxCapacity);
        }
    }

    /// <summary>현재 동기화에서 아직 사용하지 않았고 프리팹도 같은 SO 런타임을 찾습니다.</summary>
    private SpawnRuntime FindRetiredRuntime(EnemySpawnEntrySO entry)
    {
        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            SpawnRuntime runtime = m_spawnRuntimes[i];
            if (runtime.IsRetired && runtime.Entry == entry && runtime.Prefab == entry.EnemyPrefab)
            {
                return runtime;
            }
        }

        return null;
    }

    /// <summary>한 SO의 최대 수용량까지 비활성 풀 항목을 미리 생성합니다.</summary>
    private void EnsurePoolCapacity(SpawnRuntime runtime, int desiredCapacity)
    {
        if (runtime == null)
        {
            return;
        }

        desiredCapacity = Mathf.Max(0, desiredCapacity);
        if (runtime.Prefab == null)
        {
            LogMissingPrefabOnce(runtime);
            return;
        }

        EnsurePoolRoot();
        while (runtime.PoolItems.Count < desiredCapacity)
        {
            if (CreatePoolItem(runtime) == null)
            {
                return;
            }
        }
    }

    /// <summary>한 SO의 생산 주기에 맞춰 배치 수만큼 적을 활성화합니다.</summary>
    private void ProduceBatch(SpawnRuntime runtime)
    {
        if (runtime == null || runtime.Entry == null || runtime.Prefab == null)
        {
            return;
        }

        int remainingCapacity = Mathf.Max(0, runtime.Entry.MaxCapacity - runtime.ActiveEnemyCount);
        int produceCount = Mathf.Min(runtime.Entry.SpawnCount, remainingCapacity);

        for (int i = 0; i < produceCount; i++)
        {
            if (!TryGetValidSpawnPosition(out Vector3 spawnPosition))
            {
                return;
            }

            PoolItem item = TakeAvailableItem(runtime);
            if (item == null && runtime.PoolItems.Count < runtime.Entry.MaxCapacity)
            {
                item = CreatePoolItem(runtime);
                if (item != null)
                {
                    runtime.AvailableItems.Remove(item);
                }
            }

            if (item == null || !ActivatePoolItem(item, spawnPosition))
            {
                return;
            }
        }
    }

    /// <summary>지정 SO 풀에서 비활성 상태인 항목 하나를 가져옵니다.</summary>
    private static PoolItem TakeAvailableItem(SpawnRuntime runtime)
    {
        for (int i = runtime.AvailableItems.Count - 1; i >= 0; i--)
        {
            PoolItem item = runtime.AvailableItems[i];
            runtime.AvailableItems.RemoveAt(i);

            if (item != null && item.Enemy != null && !item.IsActive)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>지정 SO의 프리팹 하나를 생성해 비활성 풀에 편입합니다.</summary>
    private PoolItem CreatePoolItem(SpawnRuntime runtime)
    {
        if (runtime == null || runtime.Prefab == null)
        {
            LogMissingPrefabOnce(runtime);
            return null;
        }

        EnsurePoolRoot();

        EnemyController enemy = Instantiate(runtime.Prefab, m_poolRoot);
        enemy.gameObject.SetActive(false);

        PoolItem item = new PoolItem
        {
            Runtime = runtime,
            Enemy = enemy,
            Health = enemy.GetComponent<EnemyHealth>(),
            Agent = enemy.GetComponent<NavMeshAgent>(),
            Colliders = enemy.GetComponentsInChildren<Collider>(true),
        };

        if (item.Agent != null)
        {
            item.InitialAgentEnabled = item.Agent.enabled;
            item.InitialAgentUpdatePosition = item.Agent.updatePosition;
            item.InitialAgentUpdateRotation = item.Agent.updateRotation;
        }

        item.InitialColliderEnabled = new bool[item.Colliders.Length];
        for (int i = 0; i < item.Colliders.Length; i++)
        {
            Collider collider = item.Colliders[i];
            item.InitialColliderEnabled[i] = collider != null && collider.enabled;
        }

        if (item.Health != null)
        {
            item.DeathHandler = () => BeginCorpseRetention(item);
            item.Health.OnDeath += item.DeathHandler;
        }

        item.CorpseLifetimeHandler = () => ReturnCorpseToPool(item);
        enemy.OnCorpseLifetimeElapsed += item.CorpseLifetimeHandler;

        runtime.PoolItems.Add(item);
        runtime.AvailableItems.Add(item);
        return item;
    }

    /// <summary>풀 항목을 지정 위치에서 활성화하고 적 프리팹의 런타임 상태를 초기화합니다.</summary>
    private bool ActivatePoolItem(PoolItem item, Vector3 spawnPosition)
    {
        if (item == null || item.Enemy == null || item.IsActive || item.Runtime == null)
        {
            return false;
        }

        Transform enemyTransform = item.Enemy.transform;
        enemyTransform.SetParent(null, false);
        enemyTransform.SetPositionAndRotation(spawnPosition, transform.rotation);

        float moveSpeed = item.Runtime.Entry.SampleMoveSpeed();
        item.Enemy.ConfigureDefenseSpawn(moveSpeed, m_waypoints, m_targetPosition);

        // EnemyController가 먼저 사망 상태를 정리한 뒤 이 지점이 래그돌 유지 상태로 전환하도록 순서를 보장합니다.
        if (item.Health != null && item.DeathHandler != null)
        {
            item.Health.OnDeath -= item.DeathHandler;
        }

        item.Enemy.gameObject.SetActive(true);
        RestoreInitialReusableComponentState(item);
        item.Enemy.ResetForSpawn();

        if (item.Health != null && item.DeathHandler != null)
        {
            item.Health.OnDeath += item.DeathHandler;
        }

        item.IsActive = true;
        item.IsAwaitingCorpseReturn = false;
        item.Runtime.ActiveEnemyCount++;
        m_lastSpawnPosition = spawnPosition;
        m_hasLastSpawnPosition = true;
        return true;
    }

    /// <summary>사망 처리에서 바뀐 NavMeshAgent와 Collider를 프리팹 최초 상태로 되돌립니다.</summary>
    private static void RestoreInitialReusableComponentState(PoolItem item)
    {
        if (item.Agent != null)
        {
            item.Agent.enabled = item.InitialAgentEnabled;

            if (item.InitialAgentEnabled)
            {
                item.Agent.updatePosition = item.InitialAgentUpdatePosition;
                item.Agent.updateRotation = item.InitialAgentUpdateRotation;
            }
        }

        for (int i = 0; i < item.Colliders.Length; i++)
        {
            Collider collider = item.Colliders[i];
            if (collider != null)
            {
                collider.enabled = item.InitialColliderEnabled[i];
            }
        }
    }

    /// <summary>사망한 적이 종류별 래그돌 유지 시간을 기다리도록 표시합니다.</summary>
    private static void BeginCorpseRetention(PoolItem item)
    {
        if (item != null && item.IsActive)
        {
            item.IsAwaitingCorpseReturn = true;
        }
    }

    /// <summary>종류별 시체 유지 시간이 끝났을 때 적을 자신의 SO 풀로 반환합니다.</summary>
    private bool ReturnCorpseToPool(PoolItem item)
    {
        if (item == null || !item.IsActive)
        {
            return false;
        }

        ReturnToPool(item);
        return true;
    }

    /// <summary>시체 유지 시간이 끝났거나 외부에서 비활성화된 적을 자신의 SO 풀로 돌려보냅니다.</summary>
    private void ReturnToPool(PoolItem item)
    {
        if (item == null || !item.IsActive || item.Runtime == null)
        {
            return;
        }

        item.IsActive = false;
        item.IsAwaitingCorpseReturn = false;
        item.Runtime.ActiveEnemyCount = Mathf.Max(0, item.Runtime.ActiveEnemyCount - 1);

        if (item.Enemy == null)
        {
            return;
        }

        EnsurePoolRoot();
        item.Enemy.ClearDefenseSpawnConfiguration();
        item.Enemy.transform.SetParent(m_poolRoot, true);
        item.Enemy.gameObject.SetActive(false);

        if (!item.Runtime.AvailableItems.Contains(item))
        {
            item.Runtime.AvailableItems.Add(item);
        }
    }

    /// <summary>외부 Destroy 또는 비활성화로 풀 추적에서 벗어난 항목을 정리합니다.</summary>
    private void RemoveExternallyDestroyedItems(SpawnRuntime runtime)
    {
        for (int i = runtime.PoolItems.Count - 1; i >= 0; i--)
        {
            PoolItem item = runtime.PoolItems[i];
            if (item == null || item.Enemy == null)
            {
                if (item != null && item.IsActive)
                {
                    runtime.ActiveEnemyCount = Mathf.Max(0, runtime.ActiveEnemyCount - 1);
                }

                runtime.AvailableItems.Remove(item);
                runtime.PoolItems.RemoveAt(i);
                continue;
            }

            if (item.IsActive && !item.Enemy.gameObject.activeInHierarchy)
            {
                ReturnToPool(item);
            }
        }
    }

    /// <summary>퇴역한 SO 런타임의 비활성 풀 적과 이벤트 연결을 정리합니다.</summary>
    private void DisposeRuntime(SpawnRuntime runtime)
    {
        for (int i = 0; i < runtime.PoolItems.Count; i++)
        {
            PoolItem item = runtime.PoolItems[i];
            UnsubscribePoolItem(item);

            if (item != null && item.Enemy != null)
            {
                Destroy(item.Enemy.gameObject);
            }
        }

        runtime.PoolItems.Clear();
        runtime.AvailableItems.Clear();
    }

    /// <summary>풀 항목이 체력·시체 만료 이벤트를 계속 참조하지 않도록 해제합니다.</summary>
    private static void UnsubscribePoolItem(PoolItem item)
    {
        if (item == null)
        {
            return;
        }

        if (item.Health != null && item.DeathHandler != null)
        {
            item.Health.OnDeath -= item.DeathHandler;
        }

        if (item.Enemy != null && item.CorpseLifetimeHandler != null)
        {
            item.Enemy.OnCorpseLifetimeElapsed -= item.CorpseLifetimeHandler;
        }
    }

    /// <summary>직전 성공 스폰 위치와의 최소 거리를 만족하는 무작위 월드 X/Z 위치를 찾습니다.</summary>
    private bool TryGetValidSpawnPosition(out Vector3 spawnPosition)
    {
        float halfWidth = m_spawnAreaSize.x * 0.5f;
        float halfDepth = m_spawnAreaSize.y * 0.5f;
        float minimumDistanceSqr = m_minimumSpawnDistance * m_minimumSpawnDistance;
        Vector3 origin = transform.position;

        for (int i = 0; i < SpawnPositionSearchAttempts; i++)
        {
            Vector3 candidate = new Vector3(
                origin.x + UnityEngine.Random.Range(-halfWidth, halfWidth),
                origin.y,
                origin.z + UnityEngine.Random.Range(-halfDepth, halfDepth));

            if (!m_hasLastSpawnPosition)
            {
                spawnPosition = candidate;
                return true;
            }

            Vector2 offset = new Vector2(
                candidate.x - m_lastSpawnPosition.x,
                candidate.z - m_lastSpawnPosition.z);

            if (offset.sqrMagnitude >= minimumDistanceSqr)
            {
                spawnPosition = candidate;
                return true;
            }
        }

        spawnPosition = default;
        return false;
    }

    /// <summary>비활성 풀 적을 정리해 둘 런타임 부모를 준비합니다.</summary>
    private void EnsurePoolRoot()
    {
        if (m_poolRoot != null)
        {
            return;
        }

        GameObject poolRoot = new GameObject($"{name} Pool (Runtime)");
        m_poolRoot = poolRoot.transform;
        m_poolRoot.SetParent(transform, false);
    }

    /// <summary>한 SO의 프리팹 누락 설정을 한 번만 경고합니다.</summary>
    private void LogMissingPrefabOnce(SpawnRuntime runtime)
    {
        if (runtime == null || runtime.MissingPrefabWarningLogged)
        {
            return;
        }

        string entryName = runtime.Entry != null ? runtime.Entry.name : "Missing Entry";
        Debug.LogWarning($"[EnemySpawnPoint] '{entryName}'에 Enemy Prefab이 비어 있어 풀을 준비하거나 생산할 수 없습니다.", this);
        runtime.MissingPrefabWarningLogged = true;
    }

    /// <summary>모든 SO 풀의 현재 활성 수를 합산합니다.</summary>
    private int GetActiveEnemyCount()
    {
        int count = 0;
        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            count += m_spawnRuntimes[i].ActiveEnemyCount;
        }

        return count;
    }

    /// <summary>모든 SO 풀의 전체 항목 수를 합산합니다.</summary>
    private int GetPoolCount()
    {
        int count = 0;
        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            count += m_spawnRuntimes[i].PoolItems.Count;
        }

        return count;
    }

    /// <summary>현재 활성 생산 항목의 첫 생산 시각을 각 SO의 시작 지연시간 기준으로 다시 계산합니다.</summary>
    /// <remarks>컴포넌트 재활성화와 <see cref="SetSpawnEnabled"/> 재개 때마다 이전 카운트를 버리고 처음부터 센 값을 사용합니다.</remarks>
    private void RestartInitialSpawnDelay()
    {
        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            SpawnRuntime runtime = m_spawnRuntimes[i];
            if (!runtime.IsRetired && runtime.Entry != null)
            {
                runtime.NextProductionTime = Time.time + runtime.Entry.InitialSpawnDelay;
            }
        }
    }

    /// <summary>현재 SO 목록의 Inspector 변경 이벤트를 구독합니다.</summary>
    private void RefreshEntrySubscriptions()
    {
        UnsubscribeEntryChanges();

        for (int i = 0; i < m_spawnEntries.Count; i++)
        {
            EnemySpawnEntrySO entry = m_spawnEntries[i];
            if (entry == null || m_subscribedEntries.Contains(entry))
            {
                continue;
            }

            entry.ConfigurationChanged += HandleEntryConfigurationChanged;
            m_subscribedEntries.Add(entry);
        }
    }

    /// <summary>SO 목록과 이 스폰 포인트의 이벤트 연결을 모두 해제합니다.</summary>
    private void UnsubscribeEntryChanges()
    {
        for (int i = 0; i < m_subscribedEntries.Count; i++)
        {
            EnemySpawnEntrySO entry = m_subscribedEntries[i];
            if (entry != null)
            {
                entry.ConfigurationChanged -= HandleEntryConfigurationChanged;
            }
        }

        m_subscribedEntries.Clear();
    }

    /// <summary>연결된 SO의 Inspector 값 변경을 받아 다음 프레임 풀 수용량만 동기화하도록 예약합니다.</summary>
    private void HandleEntryConfigurationChanged(EnemySpawnEntrySO changedEntry)
    {
        if (changedEntry != null && m_spawnEntries.Contains(changedEntry))
        {
            m_runtimeSynchronizationPending = true;
        }
    }

    /// <summary>선택된 스폰 지점의 월드 X/Z 생성 범위를 Scene View에 표시합니다.</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1.0f, 0.8f);
        Gizmos.DrawWireCube(
            transform.position,
            new Vector3(m_spawnAreaSize.x, 0.05f, m_spawnAreaSize.y));
    }
}
