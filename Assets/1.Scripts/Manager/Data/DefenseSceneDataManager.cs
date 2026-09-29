using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VInspector;

/// <summary>
/// 방어전 씬에서 방어전 전용 입장 데이터·진행 데이터·결과를 소유하는 씬 전용 데이터 매니저입니다.
/// </summary>
/// <remarks>
/// 스쿼드 입장, 처치 집계, 귀환 정산, GameDataManager 반영은 같은 씬의 <see cref="FieldSceneDataManager"/>가 계속 맡습니다.
/// 이 매니저는 웨이브 진행과 Scramble 시설 값처럼 방어전에만 있는 값만 다루고,
/// 공통 정산에 필요한 신호(임무 완료)는 <see cref="FieldSceneDataManager"/>로 넘깁니다.
/// <see cref="DefenseManager"/>는 진행 상태가 바뀔 때마다 이 매니저를 통해 데이터를 갱신합니다.
/// </remarks>
[DefaultExecutionOrder(-90)]
[DisallowMultipleComponent]
public sealed class DefenseSceneDataManager : MonoBehaviour
{
    /// <summary>방어전 승리 때 지급할 자원 한 종류입니다.</summary>
    [Serializable]
    public struct VictoryReward
    {
        [Tooltip("지급할 자원입니다. 셸터에 이미 정의된 자원 중에서 고릅니다.")]
        [Variants(
            ResourceIds.UpgradeMaterial,
            ResourceIds.CraftingMaterial,
            ResourceIds.TrapMaterial)]
        public string ResourceId;

        [Tooltip("지급할 수량입니다. 0이면 지급하지 않습니다.")]
        [Min(0)] public int Amount;

        [Tooltip("귀환 정산 화면의 자원 슬롯에 표시할 아이콘입니다. 비워 두면 아이콘 없이 수량만 표시합니다.")]
        public Texture2D Icon;
    }

    [Foldout("Victory Reward")]
    [Tooltip("마지막 웨이브를 막았을 때 전리품으로 기록할 자원 목록입니다. 귀환 정산에서 셸터 자원에 더해집니다.")]
    [SerializeField] private VictoryReward[] m_victoryRewards = Array.Empty<VictoryReward>();

    [Foldout("References")]
    [Tooltip("공통 정산을 맡는 필드 데이터 매니저입니다. 비워 두면 런타임에 찾습니다.")]
    [SerializeField] private FieldSceneDataManager m_fieldSceneDataManager;

    [Foldout("Runtime State")]
    [ReadOnly][SerializeField] private DefenseEntryData m_entryData;

    [Foldout("Runtime State")]
    [ReadOnly][SerializeField] private DefenseRuntimeData m_runtimeData = new();

    [Foldout("Result State")]
    [ReadOnly][SerializeField] private DefenseResultData m_finalResult;

    private static DefenseSceneDataManager s_instance;

    /// <summary>현재 방어전 씬의 인스턴스입니다. 방어전 씬이 아니면 <c>null</c>입니다.</summary>
    public static DefenseSceneDataManager Instance => s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    private FieldSceneDataManager m_subscribedFieldSceneDataManager;

    /// <summary>방어전 결과가 승리 또는 패배로 확정되었을 때 한 번 발생합니다.</summary>
    public event Action<DefenseResultData> OnDefenseFinalized;

    /// <summary>입장 데이터가 적용되었는지 여부입니다.</summary>
    public bool HasEntryData => m_entryData != null;

    /// <summary>웨이브 설정까지 적용되어 진행 데이터를 받을 준비가 되었는지 여부입니다.</summary>
    public bool IsInitialized => m_runtimeData != null && m_runtimeData.Phase != DefensePhase.Uninitialized;

    /// <summary>방어전 결과가 확정되었는지 여부입니다.</summary>
    public bool IsFinalized => m_finalResult != null;

    /// <summary>현재 진행 단계입니다.</summary>
    public DefensePhase Phase => m_runtimeData?.Phase ?? DefensePhase.Uninitialized;

    /// <summary>
    /// 지정한 Scramble 업그레이드의 이번 방어전 레벨입니다. 입장 데이터가 없으면 0입니다.
    /// </summary>
    /// <remarks>
    /// 업그레이드 효과를 적용할 쪽이 읽을 값입니다. 출격 시점 값으로 고정되므로, 씬 도착 뒤 전역 데이터가 바뀌어도 이번 판에는 영향이 없습니다.
    /// </remarks>
    public int GetUpgradeLevel(ScrambleUpgradeType type)
    {
        return m_entryData?.Upgrades.GetLevel(type) ?? 0;
    }

    private void Reset()
    {
        ResolveFieldSceneDataManager();
    }

    private void OnValidate()
    {
        if (m_victoryRewards == null)
        {
            return;
        }

        for (int i = 0; i < m_victoryRewards.Length; i++)
        {
            m_victoryRewards[i].Amount = Mathf.Max(0, m_victoryRewards[i].Amount);
        }
    }

    private void Awake()
    {
        // 방어전 데이터 정본도 씬에 하나만 있어야 합니다. 중복되면 웨이브 기록이 두 갈래로 갈립니다.
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning("[DefenseSceneDataManager] 씬에 이미 인스턴스가 있어 중복된 쪽을 제거합니다.", this);
            Destroy(gameObject);
            return;
        }

        s_instance = this;

        // 인스펙터 표시용 직렬화 필드라 Unity가 빈 객체를 채워 넣습니다. 그대로 두면 입장 데이터가 이미
        // 있는 것으로 보여 GameDataManager 값을 읽지 않고, 결과도 확정된 것으로 보이므로 시작 전에 비웁니다.
        m_entryData = null;
        m_finalResult = null;

        ResolveFieldSceneDataManager();
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void OnEnable()
    {
        RefreshGameOverSubscription();
    }

    private void OnDisable()
    {
        UnsubscribeGameOver();
    }

    /// <summary>외부 입장 데이터가 주입되지 않았으면 GameDataManager 값으로 입장 데이터를 만듭니다.</summary>
    private void Start()
    {
        if (!HasEntryData)
        {
            InitializeEntry(CreateSceneEntryData());
        }

        // FieldSceneDataManager가 Awake 이후에 늦게 잡히는 경우를 대비해 한 번 더 붙입니다.
        RefreshGameOverSubscription();
    }

    private void Update()
    {
        m_runtimeData?.AddElapsedTime(Time.deltaTime);
    }

    /// <summary>방어전 입장 데이터를 복제해 적용합니다.</summary>
    public void InitializeEntry(DefenseEntryData entryData)
    {
        m_entryData = entryData?.Clone() ?? CreateEmptyEntryData();
    }

    /// <summary>
    /// <see cref="DefenseManager"/>의 웨이브 설정으로 진행 데이터를 시작 대기 상태로 초기화합니다.
    /// </summary>
    /// <remarks>방어전을 처음부터 다시 시작할 때도 이 경로로 기록을 비웁니다.</remarks>
    public void ConfigureWaves(int totalWaveCount)
    {
        if (!HasEntryData)
        {
            InitializeEntry(CreateSceneEntryData());
        }

        m_finalResult = null;
        m_runtimeData ??= new DefenseRuntimeData();
        m_runtimeData.Initialize(totalWaveCount);
    }

    /// <summary>웨이브 전투 구간 시작을 기록합니다.</summary>
    public void RecordWaveStarted(int waveNumber)
    {
        if (IsFinalized || m_runtimeData == null)
        {
            return;
        }

        if (!m_runtimeData.BeginWave(waveNumber, GetCurrentKillCount()))
        {
            Debug.LogWarning($"[DefenseSceneDataManager] 웨이브 시작을 기록하지 못했습니다. phase={Phase}, wave={waveNumber}", this);
        }
    }

    /// <summary>웨이브를 막고 휴식 구간으로 넘어간 것을 기록합니다.</summary>
    public void RecordRestStarted()
    {
        if (IsFinalized || m_runtimeData == null)
        {
            return;
        }

        m_runtimeData.BeginRest(GetCurrentKillCount());
    }

    /// <summary>
    /// 마지막 웨이브를 막아 방어전 결과를 승리로 확정하고, 공통 정산에 임무 완료와 승리 보상을 넘깁니다.
    /// </summary>
    /// <remarks>
    /// 보상은 <see cref="FieldSceneDataManager.RecordResource"/>로 기록합니다. 그래서 귀환 정산 화면의 자원 슬롯과
    /// <see cref="GameDataManager.ApplyFieldResult"/>의 자원 반영이 필드 획득 자원과 같은 경로를 탑니다.
    /// </remarks>
    public DefenseResultData CompleteVictory()
    {
        if (IsFinalized)
        {
            return m_finalResult.Clone();
        }

        if (m_runtimeData == null || !m_runtimeData.CompleteVictory(GetCurrentKillCount()))
        {
            Debug.LogWarning($"[DefenseSceneDataManager] 승리를 확정하지 못했습니다. phase={Phase}", this);
            return null;
        }

        // 귀환 구역에 들어가 FinalizeField가 불릴 때 결과가 Success로 정산되도록 먼저 표시합니다.
        FieldSceneDataManager fieldData = ResolveFieldSceneDataManager();
        if (fieldData != null)
        {
            fieldData.SetMissionCompleted(true);
            RecordVictoryRewards(fieldData);
        }
        else
        {
            Debug.LogWarning("[DefenseSceneDataManager] FieldSceneDataManager가 없어 임무 완료와 승리 보상을 넘기지 못했습니다.", this);
        }

        return FinalizeDefense(DefenseOutcome.Victory);
    }

    /// <summary>현재 진행 데이터를 외부 변경으로부터 분리된 복사본으로 반환합니다.</summary>
    public DefenseRuntimeData CreateRuntimeSnapshot()
    {
        return m_runtimeData?.Clone();
    }

    /// <summary>현재 입장 데이터를 외부 변경으로부터 분리된 복사본으로 반환합니다.</summary>
    public DefenseEntryData CreateEntrySnapshot()
    {
        return m_entryData?.Clone();
    }

    /// <summary>확정된 방어전 결과의 복사본을 반환합니다. 확정 전이면 <c>null</c>입니다.</summary>
    public DefenseResultData CreateFinalResultSnapshot()
    {
        return m_finalResult?.Clone();
    }

    /// <summary>
    /// 거점 파괴나 스쿼드 전멸로 게임오버가 요청되면 방어전 결과를 패배로 확정합니다.
    /// </summary>
    /// <remarks>
    /// 패배 사유별 호출부(<see cref="DefenseEventHealth"/>, 스쿼드 전멸)는 이미
    /// <see cref="FieldSceneDataManager.RequestGameOver"/>로 모여 있으므로, 그 알림 하나만 구독합니다.
    /// </remarks>
    private void HandleGameOverRequested()
    {
        if (IsFinalized || m_runtimeData == null)
        {
            return;
        }

        if (m_runtimeData.CompleteDefeat(GetCurrentKillCount()))
        {
            FinalizeDefense(DefenseOutcome.Defeat);
        }
    }

    private void RecordVictoryRewards(FieldSceneDataManager fieldData)
    {
        if (m_victoryRewards == null)
        {
            return;
        }

        for (int i = 0; i < m_victoryRewards.Length; i++)
        {
            VictoryReward reward = m_victoryRewards[i];
            // RecordResource가 빈 ID와 0 이하 수량을 걸러 냅니다.
            fieldData.RecordResource(reward.ResourceId, reward.Amount, reward.Icon);
        }
    }

    private DefenseResultData FinalizeDefense(DefenseOutcome outcome)
    {
        m_finalResult = new DefenseResultData(m_entryData, m_runtimeData, outcome);
        OnDefenseFinalized?.Invoke(m_finalResult.Clone());
        return m_finalResult.Clone();
    }

    private int GetCurrentKillCount()
    {
        FieldSceneDataManager fieldData = ResolveFieldSceneDataManager();
        return fieldData != null ? fieldData.KillCount : 0;
    }

    private FieldSceneDataManager ResolveFieldSceneDataManager()
    {
        if (m_fieldSceneDataManager == null)
        {
            m_fieldSceneDataManager = FieldSceneDataManager.Instance != null
                ? FieldSceneDataManager.Instance
                : FindFirstObjectByType<FieldSceneDataManager>();
        }

        return m_fieldSceneDataManager;
    }

    private void RefreshGameOverSubscription()
    {
        FieldSceneDataManager fieldData = ResolveFieldSceneDataManager();
        if (fieldData == null || m_subscribedFieldSceneDataManager == fieldData)
        {
            return;
        }

        UnsubscribeGameOver();
        m_subscribedFieldSceneDataManager = fieldData;
        m_subscribedFieldSceneDataManager.OnGameOverRequested += HandleGameOverRequested;
    }

    private void UnsubscribeGameOver()
    {
        if (m_subscribedFieldSceneDataManager != null)
        {
            m_subscribedFieldSceneDataManager.OnGameOverRequested -= HandleGameOverRequested;
            m_subscribedFieldSceneDataManager = null;
        }
    }

    /// <summary>GameDataManager의 Scramble 업그레이드 레벨과 현재 씬 이름으로 입장 데이터를 만듭니다.</summary>
    private static DefenseEntryData CreateSceneEntryData()
    {
        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null)
        {
            Debug.LogWarning("[DefenseSceneDataManager] GameDataManager가 없어 업그레이드를 모두 0레벨로 시작합니다.");
            return CreateEmptyEntryData();
        }

        return new DefenseEntryData(GetActiveSceneName(), DefenseUpgradeLevels.FromGameData(gameData));
    }

    private static DefenseEntryData CreateEmptyEntryData()
    {
        return new DefenseEntryData(GetActiveSceneName(), DefenseUpgradeLevels.None);
    }

    private static string GetActiveSceneName()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.IsValid() ? activeScene.name : string.Empty;
    }
}
