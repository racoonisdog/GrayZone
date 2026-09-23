using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 범위 안에서 여러 <see cref="EnemySpawnEntrySO"/> 생산 항목을 독립적으로 풀링·생성하는 스폰 지점입니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 위치, X/Z 범위, 최소 위치 간격과 신규 생산 허용 상태만 소유합니다.
/// 적 프리팹별 생산 규칙은 <see cref="m_spawnEntries"/>의 SO가 소유하며, SO 하나마다 독립 풀과 생산 타이머를 만듭니다.
/// 적이 사망하면 <see cref="EnemyManager.Entry.CorpseLifetime"/> 동안 래그돌을 유지한 뒤 해당 SO의 풀로 반납합니다.
/// </remarks>
[DisallowMultipleComponent]
public class EnemySpawnPoint : MonoBehaviour
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

        /// <summary>이 항목이 해당 Spawn SO의 생존 정원을 차지하고 있는지 여부입니다.</summary>
        public bool OccupiesLiveCapacity;

        /// <summary>시체가 된 순서입니다. 값이 작을수록 먼저 죽은 시체입니다.</summary>
        public ulong CorpseOrder;
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

        /// <summary>현재 살아 있는 적 수입니다. 유지 중인 시체는 포함하지 않습니다.</summary>
        public int LiveEnemyCount;

        /// <summary>다음 배치 생산을 시도할 게임 시간입니다.</summary>
        public float NextProductionTime;

        /// <summary>Inspector 목록에서 제거됐거나 프리팹이 바뀌어 신규 생산을 중단한 옛 런타임인지 여부입니다.</summary>
        public bool IsRetired;

        /// <summary>현재 목표 풀 용량의 사전 생성이 끝나 생산 시작 지연시간을 예약했는지 여부입니다.</summary>
        public bool IsPrewarmComplete;

        /// <summary>프리팹 누락 경고를 반복 출력하지 않기 위한 상태입니다.</summary>
        public bool MissingPrefabWarningLogged;
    }

    private const int SpawnPositionSearchAttempts = 16;

    [Header("Spawn Entries")]
    [Tooltip("이 지점이 운용할 적 프리팹별 생산 설정 에셋입니다. 리스트의 SO 하나마다 독립 풀과 생산 타이머가 만들어집니다.")]
    [SerializeField] private List<EnemySpawnEntrySO> m_spawnEntries = new List<EnemySpawnEntrySO>();

    [Header("Spawn Area")]
    [Tooltip("스폰 지점을 중심으로 적을 무작위 생성할 가로·세로 범위(m)입니다. 이 오브젝트의 Y축 회전을 따라 함께 돌아갑니다. Y 좌표는 이 오브젝트 위치를 그대로 사용합니다.")]
    [SerializeField] private Vector2 m_spawnAreaSize = new Vector2(10.0f, 10.0f);

    [Tooltip("Scene View에서 정면(로컬 +Z)을 표시하는 선의 길이(m)입니다. 표시 전용이라 스폰 동작에는 영향이 없습니다. 0이면 그리지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_forwardGizmoLength = 3.0f;

    [Tooltip("켜면 생성 위치를 가장 가까운 NavMesh 위로 붙입니다. 끄면 이 오브젝트의 Y 좌표를 그대로 씁니다. 경사면이나 터레인 위에 지점을 놓을 때 켭니다.")]
    [SerializeField] private bool m_snapToGround = true;

    [Tooltip("지면 스냅에서 NavMesh를 찾을 최대 거리(m)입니다. 이 안에서 못 찾으면 붙이지 않고 원래 높이로 생성합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_groundSnapMaxDistance = 5.0f;

    [Tooltip("모든 생산 항목을 통틀어 직전 성공 스폰 위치의 X/Z 반경 중 다음 적을 만들지 않을 최소 거리(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_minimumSpawnDistance = 1.5f;

    [Header("Spawn State")]
    [Tooltip("켜면 각 SO의 생산 주기에 맞춰 풀 적을 활성화합니다. 끄면 앞으로의 배치 생산만 멈추며 이미 활성화된 적은 제거하지 않습니다.")]
    [SerializeField] private bool m_spawnEnabled = true;

    [Tooltip("한 프레임에 이 스포너가 미리 생성할 적 프리팹 수입니다. 큰 풀을 여러 프레임에 나눠 준비해 시작 프레임의 부하를 줄입니다.")]
    [Min(1)]
    [SerializeField] private int m_poolPrewarmCountPerFrame = 2;

    /// <summary>이 지점이 운용할 프리팹별 생산 설정 목록입니다.</summary>
    public IReadOnlyList<EnemySpawnEntrySO> SpawnEntries => m_spawnEntries;

    /// <summary>생성 범위의 가로·세로입니다. <see cref="AreaRotation"/>이 도는 평면 위에서 해석합니다.</summary>
    public Vector2 SpawnAreaSize => m_spawnAreaSize;

    /// <summary>Scene View에서 정면을 표시하는 선의 길이입니다.</summary>
    public float ForwardGizmoLength => m_forwardGizmoLength;

    /// <summary>생성 위치를 NavMesh 위로 붙일지 여부입니다.</summary>
    public bool SnapToGround => m_snapToGround;

    /// <summary>
    /// 생성 범위를 회전시킬 때 쓰는 Y축(yaw) 회전입니다.
    /// </summary>
    /// <remarks>
    /// 전체 회전이 아니라 Y축만 씁니다. 스폰 범위는 지면에 눕는 사각형이라, 지점을 경사면에 놓거나
    /// 부모를 따라 기울어졌을 때 전체 회전을 쓰면 범위가 같이 기울어 적이 공중이나 지면 아래에 생깁니다.
    /// 기즈모와 실제 생성 위치가 같은 값을 써야 보이는 것과 결과가 어긋나지 않으므로 여기서 한 번만 정합니다.
    /// </remarks>
    public Quaternion AreaRotation => Quaternion.Euler(0.0f, transform.eulerAngles.y, 0.0f);

    /// <summary>직전 성공 위치와 다음 위치 사이에 보장할 X/Z 최소 거리입니다.</summary>
    public float MinimumSpawnDistance => m_minimumSpawnDistance;

    /// <summary>신규 배치 생산을 허용하는지 여부입니다.</summary>
    public bool IsSpawnEnabled => m_spawnEnabled;

    /// <summary>한 프레임에 미리 생성하는 최대 풀 항목 수입니다.</summary>
    public int PoolPrewarmCountPerFrame => m_poolPrewarmCountPerFrame;

    /// <summary>모든 SO 풀에서 현재 살아 있는 적 수입니다. 유지 중인 시체는 포함하지 않습니다.</summary>
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

    /// <summary>지면 스냅 실패 경고를 이미 남겼는지 여부입니다. 매 생성마다 같은 경고가 쌓이는 것을 막습니다.</summary>
    private bool m_hasWarnedGroundSnapFailure;

    /// <summary>다음 후보 위치에서 최소 거리 검사를 할 직전 성공 스폰 위치입니다.</summary>
    private Vector3 m_lastSpawnPosition;

    /// <summary>풀 전체에서 시체 생성 순서를 안정적으로 비교하기 위한 증가 번호입니다.</summary>
    private ulong m_nextCorpseOrder;

    /// <summary>활성화될 때 SO 변경 이벤트를 연결하고, 재활성화라면 다음 프레임 동기화를 예약합니다.</summary>
    protected virtual void OnEnable()
    {
        RefreshEntrySubscriptions();

        if (Application.isPlaying && m_runtimeInitialized)
        {
            m_runtimeSynchronizationPending = true;
            RestartInitialSpawnDelay();
        }
    }

    /// <summary>게임 시작 시 등록된 SO마다 최대 수용량만큼 비활성 풀을 준비합니다.</summary>
    protected virtual void Start()
    {
        EnsurePoolRoot();
        m_runtimeInitialized = true;
        m_lastSpawnEnabledState = m_spawnEnabled;
        SynchronizeSpawnRuntimes();
    }

    /// <summary>각 SO의 생산 주기와 풀 수용량을 독립적으로 처리합니다.</summary>
    protected virtual void Update()
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

        int remainingPrewarmCount = Mathf.Max(1, m_poolPrewarmCountPerFrame);

        for (int i = m_spawnRuntimes.Count - 1; i >= 0; i--)
        {
            SpawnRuntime runtime = m_spawnRuntimes[i];
            RemoveExternallyDestroyedItems(runtime);

            if (runtime.IsRetired)
            {
                if (runtime.LiveEnemyCount == 0)
                {
                    DisposeRuntime(runtime);
                    m_spawnRuntimes.RemoveAt(i);
                }

                continue;
            }

            if (runtime.Entry == null)
            {
                continue;
            }

            int desiredCapacity = Mathf.Max(0, runtime.Entry.MaxCapacity);
            if (runtime.PoolItems.Count < desiredCapacity)
            {
                runtime.IsPrewarmComplete = false;
                EnsurePoolCapacity(runtime, desiredCapacity, ref remainingPrewarmCount);
            }

            if (runtime.PoolItems.Count < desiredCapacity)
            {
                runtime.IsPrewarmComplete = false;
            }
        }

        if (!AreAllSpawnRuntimesPrewarmed())
        {
            return;
        }

        ScheduleProductionAfterPrewarm();

        if (!m_spawnEnabled)
        {
            return;
        }

        for (int i = m_spawnRuntimes.Count - 1; i >= 0; i--)
        {
            SpawnRuntime runtime = m_spawnRuntimes[i];
            if (runtime.IsRetired || runtime.Entry == null || !runtime.IsPrewarmComplete)
            {
                continue;
            }

            if (Time.time < runtime.NextProductionTime)
            {
                continue;
            }

            ProduceBatch(runtime);

            // 무작위 주기 설정이면 배치마다 다시 뽑아야 간격이 실제로 흔들립니다.
            runtime.NextProductionTime = Time.time + runtime.Entry.SampleProductionInterval();
        }
    }

    /// <summary>프리팹이 유효한 모든 현재 생산 항목의 풀 준비가 끝났는지 확인합니다.</summary>
    private bool AreAllSpawnRuntimesPrewarmed()
    {
        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            SpawnRuntime runtime = m_spawnRuntimes[i];
            if (runtime.IsRetired || runtime.Entry == null || runtime.Prefab == null)
            {
                continue;
            }

            if (runtime.PoolItems.Count < Mathf.Max(0, runtime.Entry.MaxCapacity))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>전체 풀 준비가 끝난 한 시점을 기준으로 각 SO의 최초 생산 지연시간을 시작합니다.</summary>
    private void ScheduleProductionAfterPrewarm()
    {
        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            SpawnRuntime runtime = m_spawnRuntimes[i];
            if (runtime.IsRetired || runtime.Entry == null || runtime.Prefab == null || runtime.IsPrewarmComplete)
            {
                continue;
            }

            runtime.IsPrewarmComplete = true;
            runtime.NextProductionTime = Time.time + runtime.Entry.InitialSpawnDelay;
        }
    }

    /// <summary>비활성화될 때 SO 변경 이벤트 연결을 해제합니다.</summary>
    protected virtual void OnDisable()
    {
        UnsubscribeEntryChanges();
    }

    /// <summary>스폰 지점 제거 시 풀 항목과 SO 이벤트가 이 지점을 계속 참조하지 않도록 해제합니다.</summary>
    protected virtual void OnDestroy()
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
    protected virtual void OnValidate()
    {
        m_spawnAreaSize = new Vector2(
            Mathf.Max(0.0f, m_spawnAreaSize.x),
            Mathf.Max(0.0f, m_spawnAreaSize.y));
        m_minimumSpawnDistance = Mathf.Max(0.0f, m_minimumSpawnDistance);
        m_poolPrewarmCountPerFrame = Mathf.Max(1, m_poolPrewarmCountPerFrame);

        RefreshEntrySubscriptions();
        m_runtimeSynchronizationPending = true;
    }

    /// <summary>스폰 매니저가 자식 지점에 공통 Inspector 설정을 복사할 때 사용합니다.</summary>
    /// <remarks>SO 자체의 생산 값은 복사하지 않고 참조 목록만 복사합니다. Play Mode에서 호출하면 다음 Update에 런타임 구성을 동기화합니다.</remarks>
    public void ApplyConfiguration(
        IList<EnemySpawnEntrySO> spawnEntries,
        Vector2 spawnAreaSize,
        float minimumSpawnDistance,
        bool spawnEnabled,
        int poolPrewarmCountPerFrame)
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
        m_poolPrewarmCountPerFrame = Mathf.Max(1, poolPrewarmCountPerFrame);

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

    /// <summary>
    /// 지금 필드에 나와 있는 적과 시체를 모두 자기 풀로 되돌립니다.
    /// </summary>
    /// <returns>풀로 되돌린 수입니다.</returns>
    /// <remarks>
    /// 죽이는 것이 아니라 없던 일로 하는 것입니다. 사망 연출도 없고 처치 수도 오르지 않습니다.
    /// <see cref="SetSpawnEnabled"/>는 새 생산만 막고 이미 나온 적은 그대로 두므로, 필드를 비우려면
    /// 이 메서드가 따로 필요합니다.
    ///
    /// 풀로 되돌리므로 생산 정원도 함께 풀립니다. 적을 개별로 파괴하면 정원 계산이 어긋나 이후
    /// 생산이 막힙니다.
    /// </remarks>
    public int DespawnActiveEnemies()
    {
        int despawnedCount = 0;

        for (int runtimeIndex = 0; runtimeIndex < m_spawnRuntimes.Count; runtimeIndex++)
        {
            SpawnRuntime runtime = m_spawnRuntimes[runtimeIndex];
            if (runtime == null)
            {
                continue;
            }

            // 뒤에서부터 도는 이유는 ReturnToPool이 항목 목록을 건드릴 수 있기 때문입니다.
            for (int itemIndex = runtime.PoolItems.Count - 1; itemIndex >= 0; itemIndex--)
            {
                PoolItem item = runtime.PoolItems[itemIndex];
                if (item == null || !item.IsActive)
                {
                    continue;
                }

                ReturnToPool(item);
                despawnedCount++;
            }
        }

        return despawnedCount;
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
                };
                m_spawnRuntimes.Add(runtime);
            }

            runtime.IsRetired = false;
            if (runtime.PoolItems.Count < Mathf.Max(0, entry.MaxCapacity))
            {
                runtime.IsPrewarmComplete = false;
            }
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
    private void EnsurePoolCapacity(SpawnRuntime runtime, int desiredCapacity, ref int remainingPrewarmCount)
    {
        if (runtime == null || remainingPrewarmCount <= 0)
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
        while (runtime.PoolItems.Count < desiredCapacity && remainingPrewarmCount > 0)
        {
            if (CreatePoolItem(runtime) == null)
            {
                return;
            }

            remainingPrewarmCount--;
        }
    }

    /// <summary>한 SO의 생산 주기에 맞춰 배치 수만큼 적을 활성화합니다.</summary>
    private void ProduceBatch(SpawnRuntime runtime)
    {
        if (runtime == null || runtime.Entry == null || runtime.Prefab == null)
        {
            return;
        }

        int remainingCapacity = Mathf.Max(0, runtime.Entry.MaxCapacity - runtime.LiveEnemyCount);
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

            // 생존 정원에는 자리가 있지만 물리 풀이 시체로 가득 찬 경우 가장 오래된 시체부터 회수합니다.
            // 시체는 평소에는 현장에 남고, 다음 생존 적을 만들 공간이 필요할 때만 재사용됩니다.
            if (item == null && ReclaimOldestCorpse(runtime))
            {
                item = TakeAvailableItem(runtime);
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
        // 전체 회전이 아니라 Y축만 씁니다. 지점이 기울어 있어도 적은 서서 나와야 합니다.
        enemyTransform.SetPositionAndRotation(spawnPosition, AreaRotation);

        // EnemyController가 먼저 사망 상태를 정리한 뒤 이 지점이 래그돌 유지 상태로 전환하도록 순서를 보장합니다.
        if (item.Health != null && item.DeathHandler != null)
        {
            item.Health.OnDeath -= item.DeathHandler;
        }

        item.Enemy.gameObject.SetActive(true);
        RestoreInitialReusableComponentState(item);
        item.Enemy.ResetForSpawn();
        ConfigureSpawnedEnemy(item.Enemy, item.Runtime.Entry);

        if (item.Health != null && item.DeathHandler != null)
        {
            item.Health.OnDeath += item.DeathHandler;
        }

        item.IsActive = true;
        item.IsAwaitingCorpseReturn = false;
        item.OccupiesLiveCapacity = true;
        item.CorpseOrder = 0;
        item.Runtime.LiveEnemyCount++;
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
    private void BeginCorpseRetention(PoolItem item)
    {
        if (item == null || !item.IsActive || item.IsAwaitingCorpseReturn)
        {
            return;
        }

        ReleaseLiveCapacity(item);
        item.IsAwaitingCorpseReturn = true;
        item.CorpseOrder = ++m_nextCorpseOrder;
    }

    /// <summary>물리 풀이 가득 찼을 때 가장 먼저 죽은 시체 하나를 비활성 풀로 돌립니다.</summary>
    private bool ReclaimOldestCorpse(SpawnRuntime runtime)
    {
        if (runtime == null)
        {
            return false;
        }

        PoolItem oldestCorpse = null;
        for (int i = 0; i < runtime.PoolItems.Count; i++)
        {
            PoolItem candidate = runtime.PoolItems[i];
            if (candidate == null
                || !candidate.IsActive
                || !candidate.IsAwaitingCorpseReturn
                || candidate.Enemy == null)
            {
                continue;
            }

            if (oldestCorpse == null || candidate.CorpseOrder < oldestCorpse.CorpseOrder)
            {
                oldestCorpse = candidate;
            }
        }

        if (oldestCorpse == null)
        {
            return false;
        }

        ReturnToPool(oldestCorpse);
        return true;
    }

    /// <summary>살아 있는 적 정원 점유를 정확히 한 번만 해제합니다.</summary>
    private static void ReleaseLiveCapacity(PoolItem item)
    {
        if (item == null || !item.OccupiesLiveCapacity || item.Runtime == null)
        {
            return;
        }

        item.OccupiesLiveCapacity = false;
        item.Runtime.LiveEnemyCount = Mathf.Max(0, item.Runtime.LiveEnemyCount - 1);
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
        item.CorpseOrder = 0;
        ReleaseLiveCapacity(item);

        if (item.Enemy == null)
        {
            return;
        }

        EnsurePoolRoot();
        ClearSpawnedEnemyConfiguration(item.Enemy);
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
                if (item != null)
                {
                    ReleaseLiveCapacity(item);
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

    /// <summary>적의 Inspector/Balance 초기화가 끝난 결과 위에 이번 Spawn SO의 런타임 값을 적용합니다.</summary>
    /// <param name="enemy">이번에 활성화할 적입니다.</param>
    /// <param name="entry">적을 생산한 Spawn SO입니다.</param>
    /// <remarks>파생 스폰 포인트는 base 호출 뒤 자기 전용 설정을 추가해 Spawn SO 우선순위를 보존합니다.</remarks>
    protected virtual void ConfigureSpawnedEnemy(EnemyController enemy, EnemySpawnEntrySO entry)
    {
        if (enemy == null || entry == null)
        {
            return;
        }

        enemy.ConfigureSpawn(entry.SampleWalkSpeed(), entry.SampleRunSpeed());
    }

    /// <summary>풀 반환 시 이번 생성에서 주입한 Spawn SO 런타임 값만 제거합니다.</summary>
    /// <param name="enemy">풀로 반환할 적입니다.</param>
    protected virtual void ClearSpawnedEnemyConfiguration(EnemyController enemy)
    {
        enemy?.ClearSpawnConfiguration();
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

    /// <summary>직전 성공 스폰 위치와의 최소 거리를 만족하는 무작위 위치를 찾습니다.</summary>
    /// <remarks>
    /// 범위는 <see cref="AreaRotation"/>을 따라 돕니다. 가로/세로를 월드 X/Z에 그대로 더하지 않고
    /// 로컬 좌표로 뽑은 뒤 회전시켜 더합니다. 스케일은 반영하지 않습니다. 지점에 스케일을 주더라도
    /// 인스펙터에 적은 범위(m)가 그대로 유지되는 편이 예측하기 쉽기 때문입니다.
    /// </remarks>
    private bool TryGetValidSpawnPosition(out Vector3 spawnPosition)
    {
        float halfWidth = m_spawnAreaSize.x * 0.5f;
        float halfDepth = m_spawnAreaSize.y * 0.5f;
        float minimumDistanceSqr = m_minimumSpawnDistance * m_minimumSpawnDistance;
        Vector3 origin = transform.position;
        Quaternion areaRotation = AreaRotation;

        for (int i = 0; i < SpawnPositionSearchAttempts; i++)
        {
            Vector3 localOffset = new Vector3(
                UnityEngine.Random.Range(-halfWidth, halfWidth),
                0.0f,
                UnityEngine.Random.Range(-halfDepth, halfDepth));

            Vector3 candidate = origin + (areaRotation * localOffset);
            candidate.y = origin.y;
            candidate = SnapCandidateToGround(candidate);

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

    /// <summary>
    /// 생성 후보 위치를 가장 가까운 NavMesh 위로 붙입니다.
    /// </summary>
    /// <param name="candidate">범위 안에서 뽑은 원래 후보 위치입니다.</param>
    /// <returns><see cref="m_snapToGround"/>가 꺼져 있거나 NavMesh를 못 찾으면 원래 위치를 그대로 돌려줍니다.</returns>
    /// <remarks>
    /// 레이캐스트가 아니라 NavMesh를 기준으로 삼습니다. 여기서 만드는 적은 <see cref="NavMeshAgent"/>로 움직이므로,
    /// 콜라이더 표면에 붙여 봐야 그 지점이 NavMesh 밖이면 에이전트가 경로를 잡지 못합니다.
    ///
    /// 못 찾았을 때 후보를 버리지 않고 원래 위치를 쓰는 이유는, NavMesh가 없는 씬에서 모든 시도가 실패해
    /// 생산이 조용히 멈추는 쪽이 더 나쁘기 때문입니다. 대신 처음 한 번만 경고를 남깁니다.
    /// </remarks>
    private Vector3 SnapCandidateToGround(Vector3 candidate)
    {
        if (!m_snapToGround)
        {
            return candidate;
        }

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_groundSnapMaxDistance, NavMesh.AllAreas))
        {
            return hit.position;
        }

        if (!m_hasWarnedGroundSnapFailure)
        {
            m_hasWarnedGroundSnapFailure = true;
            Debug.LogWarning(
                $"[EnemySpawnPoint] '{name}': 생성 범위 안에서 {m_groundSnapMaxDistance}m 내 NavMesh를 찾지 못했습니다. " +
                "지면 스냅 없이 이 지점의 Y 좌표로 생성합니다. NavMesh 베이크 여부나 스냅 거리를 확인하세요.",
                this);
        }

        return candidate;
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

    /// <summary>모든 SO 풀의 현재 생존 적 수를 합산합니다.</summary>
    private int GetActiveEnemyCount()
    {
        int count = 0;
        for (int i = 0; i < m_spawnRuntimes.Count; i++)
        {
            count += m_spawnRuntimes[i].LiveEnemyCount;
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
            if (!runtime.IsRetired && runtime.Entry != null && runtime.IsPrewarmComplete)
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

    /// <summary>선택된 스폰 지점의 생성 범위와 정면을 Scene View에 표시합니다.</summary>
    /// <remarks>
    /// <see cref="AreaRotation"/>을 행렬로 걸어 실제 생성 범위와 같은 방향으로 그립니다.
    /// 정면 선은 로컬 +Z로, 적이 어느 쪽을 보고 생성되는지 확인하는 용도입니다.
    /// </remarks>
    protected virtual void OnDrawGizmosSelected()
    {
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, AreaRotation, Vector3.one);

        Gizmos.color = new Color(0.2f, 0.9f, 1.0f, 0.8f);
        Gizmos.DrawWireCube(
            Vector3.zero,
            new Vector3(m_spawnAreaSize.x, 0.05f, m_spawnAreaSize.y));

        if (m_forwardGizmoLength > 0.0f)
        {
            Gizmos.color = new Color(1.0f, 0.45f, 0.1f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0.0f, 0.0f, m_forwardGizmoLength));
        }

        Gizmos.matrix = previous;
    }
}
