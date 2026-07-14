using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VInspector;

/// <summary>
/// 배틀 씬에서 발생한 런타임 결과값을 수집하고, 귀환 시점에 스냅샷으로 확정하는 씬 전용 데이터 매니저입니다.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class BattleSceneDataManager : MonoBehaviour
{
    /// <summary>
    /// 귀환 정산 화면의 자원 슬롯 하나에 표시할 자원 정보입니다.
    /// </summary>
    [Serializable]
    public struct ResourceResult
    {
        /// <summary>자원 데이터를 외부 시스템과 연결할 때 사용할 식별자입니다.</summary>
        public int id;

        /// <summary>셸터 자원 체계와 연결되는 자원 종류입니다.</summary>
        public CurrencyType Type;

        /// <summary>목업 결과 UI에 표시할 자원 아이콘입니다.</summary>
        public Texture2D Icon;

        /// <summary>이번 전투에서 확보한 자원 수량입니다.</summary>
        [Min(0)] public int Count;
    }

    /// <summary>
    /// 귀환 시점에 확정되는 스쿼드원 한 명의 전투 결과입니다.
    /// </summary>
    public readonly struct PlayerbleResult
    {
        /// <summary>스쿼드원 한 명의 귀환 정산 표시값을 생성합니다.</summary>
        public PlayerbleResult(string displayName, PlayableCharacterId characterId, int injuryGauge, PlayerInjuryState injuryState, bool isCombatOut, int killCount)
        {
            DisplayName = displayName;
            CharacterId = characterId;
            InjuryGauge = injuryGauge;
            InjuryState = injuryState;
            IsCombatOut = isCombatOut;
            KillCount = killCount;
        }

        /// <summary>결과 UI와 로그에 표시할 캐릭터 이름입니다.</summary>
        public string DisplayName { get; }

        /// <summary>결과창 캐릭터 초상화 선택 등에 사용하는 고정 캐릭터 식별자입니다.</summary>
        public PlayableCharacterId CharacterId { get; }

        /// <summary>전투 종료 시 반올림하여 확정한 부상 게이지입니다.</summary>
        public int InjuryGauge { get; }

        /// <summary>부상 게이지에서 판정된 부상 상태입니다.</summary>
        public PlayerInjuryState InjuryState { get; }

        /// <summary>이번 전투에서 구조 실패 등으로 전투 이탈했는지 여부입니다.</summary>
        public bool IsCombatOut { get; }

        /// <summary>이번 전투에서 이 캐릭터의 무기가 확정한 적 처치 수입니다.</summary>
        public int KillCount { get; }
    }

    /// <summary>
    /// 귀환 순간의 전투 결과를 고정한 읽기 전용 데이터 묶음입니다.
    /// UI와 이후 셸터 반영은 이 스냅샷만 소비해야 전투 중 상태 변화와 분리됩니다.
    /// </summary>
    public sealed class ResultSnapshot
    {
        /// <summary>기존 결과 UI가 소비할 귀환 정산 표시값 묶음을 생성합니다.</summary>
        public ResultSnapshot(bool missionCompleted, int killCount, IReadOnlyList<PlayerbleResult> characters, IReadOnlyList<ResourceResult> resources)
        {
            MissionCompleted = missionCompleted;
            KillCount = killCount;
            Characters = characters;
            Resources = resources;
        }

        /// <summary>귀환 전 작전 목표를 달성했는지 여부입니다.</summary>
        public bool MissionCompleted { get; }

        /// <summary>전투 전체에서 사망 처리된 적의 수입니다.</summary>
        public int KillCount { get; }

        /// <summary>스쿼드원별 부상·전투 이탈·처치 결과입니다.</summary>
        public IReadOnlyList<PlayerbleResult> Characters { get; }

        /// <summary>이번 전투에서 확보한 자원 결과입니다.</summary>
        public IReadOnlyList<ResourceResult> Resources { get; }
    }

    [Foldout("References")]
    [SerializeField] private SquadManager m_squadManager;

    [Foldout("Runtime State")]
    [ReadOnly][SerializeField] private BattleEntryData m_entryData;

    [Foldout("Runtime State")]
    [ReadOnly][SerializeField] private BattleRuntimeData m_runtimeData = new();

    [Foldout("Result State")]
    [ReadOnly][SerializeField] private BattleResultData m_finalResult;

    [Foldout("Result State")]
    [ReadOnly][SerializeField] private bool m_resultApplied;

    [Foldout("Result State")]
    [Tooltip("임무 판정 시스템이 연결되기 전까지는 Inspector 또는 SetMissionCompleted로 설정합니다.")]
    [SerializeField] private bool m_missionCompleted;

    [ReadOnly][SerializeField] private int m_killCount;

    [Foldout("Mock Resources")]
    [Tooltip("자원 획득 시스템이 연결되기 전, Result UI 슬롯에 표시할 목업 자원입니다.")]
    [SerializeField] private List<ResourceResult> m_mockResources = new();

    private readonly HashSet<EnemyHealth> m_subscribedEnemies = new();
    private readonly Dictionary<WeaponController, Action<CombatDamage.HitFeedback>> m_weaponKillHandlers = new();
    private readonly Dictionary<PlayerbleUnitData, Action> m_playerDataHandlers = new();
    private readonly Dictionary<PlayerbleUnitData, int> m_characterKillCounts = new();
    private readonly List<PlayerbleResult> m_characterResults = new();
    private readonly List<ResourceResult> m_resourceResults = new();

    /// <summary>현재 배틀의 임무 목표 달성 여부입니다.</summary>
    public bool MissionCompleted => m_missionCompleted;

    /// <summary>현재 배틀에서 확정된 전체 적 처치 수입니다.</summary>
    public int KillCount => m_killCount;

    /// <summary>입장 데이터가 적용되어 배틀 런타임 데이터가 준비되었는지 여부입니다.</summary>
    public bool IsInitialized => m_runtimeData != null && m_runtimeData.Phase != BattlePhase.Uninitialized;

    /// <summary>배틀 최종 결과가 한 번 이상 확정되었는지 여부입니다.</summary>
    public bool IsFinalized => m_finalResult != null;

    /// <summary>확정 결과가 GameDataManager 영속 런타임 데이터에 반영되었는지 여부입니다.</summary>
    public bool ResultApplied => m_resultApplied;

    /// <summary>Inspector에서 컴포넌트를 추가하거나 Reset할 때 씬 참조를 자동 탐색합니다.</summary>
    private void Reset()
    {
        AutoFindReferences();
    }

    /// <summary>런타임 시작 전에 필요한 씬 참조를 확보합니다.</summary>
    private void Awake()
    {
        AutoFindReferences();
    }

    /// <summary>외부 입장 데이터가 주입되지 않았으면 현재 씬 기준으로 배틀 데이터를 초기화합니다.</summary>
    private void Start()
    {
        if (!IsInitialized)
        {
            Initialize(CreateSceneEntryData());
        }
    }

    /// <summary>배틀이 진행 중인 동안 경과 시간을 누적합니다.</summary>
    private void Update()
    {
        m_runtimeData?.AddElapsedTime(Time.deltaTime);
    }

    /// <summary>활성화 시 적·무기·스쿼드 공개 데이터 이벤트를 구독합니다.</summary>
    private void OnEnable()
    {
        RefreshEnemySubscriptions();
        RefreshWeaponSubscriptions();
        RefreshPlayerDataSubscriptions();
    }

    /// <summary>비활성화 시 이 컴포넌트가 등록한 모든 런타임 이벤트를 해제합니다.</summary>
    private void OnDisable()
    {
        UnsubscribeEnemies();
        UnsubscribeWeapons();
        UnsubscribePlayerData();
    }

    /// <summary>임무 달성 판정 결과를 기록합니다.</summary>
    public void SetMissionCompleted(bool value)
    {
        if (IsFinalized)
        {
            return;
        }

        m_missionCompleted = value;
        m_runtimeData?.SetMissionCompleted(value);
    }

    /// <summary>외부 획득 시스템이 자원 획득을 기록할 때 사용합니다.</summary>
    public void RecordResource(CurrencyType type, int amount, Texture2D icon = null)
    {
        if (IsFinalized || amount <= 0)
        {
            return;
        }

        m_runtimeData?.RecordResource(type, amount);

        for (int i = 0; i < m_mockResources.Count; i++)
        {
            if (m_mockResources[i].Type != type)
            {
                continue;
            }

            ResourceResult entry = m_mockResources[i];
            entry.Count += amount;
            if (entry.Icon == null && icon != null)
            {
                entry.Icon = icon;
            }

            m_mockResources[i] = entry;
            return;
        }

        m_mockResources.Add(new ResourceResult
        {
            Type = type,
            Icon = icon,
            Count = amount
        });
    }

    /// <summary>무기 외의 피해원이 캐릭터 처치를 확정했을 때 호출합니다.</summary>
    public void RecordCharacterKill(PlayerbleUnitData player)
    {
        if (IsFinalized || player == null)
        {
            return;
        }

        m_characterKillCounts.TryGetValue(player, out int currentKillCount);
        m_characterKillCounts[player] = currentKillCount + 1;
        m_runtimeData?.RecordMemberKill(player.RuntimeId, player.CharacterId);
    }

    /// <summary>새 전투 시작 시 누적 결과를 초기화합니다.</summary>
    public void ResetBattleResult()
    {
        m_missionCompleted = false;
        m_killCount = 0;
        m_finalResult = null;
        m_resultApplied = false;
        m_characterKillCounts.Clear();
        m_mockResources.Clear();

        if (m_entryData != null)
        {
            m_runtimeData ??= new BattleRuntimeData();
            m_runtimeData.Initialize(m_entryData, CountActiveEnemies());
            m_runtimeData.StartBattle();
        }

        RefreshEnemySubscriptions();
        RefreshWeaponSubscriptions();
        RefreshPlayerDataSubscriptions();
        SyncAllRuntimeMemberStates();
    }

    /// <summary>
    /// 배틀 입장 스냅샷을 복제하고 현재 씬의 실제 스쿼드 상태로 보완하여 런타임 데이터를 시작합니다.
    /// </summary>
    public void Initialize(BattleEntryData entryData)
    {
        m_entryData = entryData?.Clone() ?? CreateEmptyEntryData();
        ApplySceneSquadData(m_entryData);

        m_missionCompleted = false;
        m_killCount = 0;
        m_finalResult = null;
        m_resultApplied = false;
        m_characterKillCounts.Clear();

        m_runtimeData ??= new BattleRuntimeData();
        m_runtimeData.Initialize(m_entryData, CountActiveEnemies());
        for (int i = 0; i < m_mockResources.Count; i++)
        {
            ResourceResult resource = m_mockResources[i];
            m_runtimeData.RecordResource(resource.Type, resource.Count);
        }

        m_runtimeData.StartBattle();
        RefreshEnemySubscriptions();
        RefreshWeaponSubscriptions();
        RefreshPlayerDataSubscriptions();
        SyncAllRuntimeMemberStates();
    }

    /// <summary>현재 배틀 입장 데이터를 외부 변경으로부터 분리된 스냅샷으로 반환합니다.</summary>
    public BattleEntryData CreateEntrySnapshot()
    {
        return m_entryData?.Clone();
    }

    /// <summary>현재 배틀 런타임 데이터를 외부 변경으로부터 분리된 스냅샷으로 반환합니다.</summary>
    public BattleRuntimeData CreateRuntimeSnapshot()
    {
        return m_runtimeData?.Clone();
    }

    /// <summary>
    /// 현재 배틀 값을 한 번만 최종 결과로 확정하고 GameDataManager에 반영합니다.
    /// 이후 호출은 최초 확정 결과의 복사본만 반환합니다.
    /// </summary>
    public BattleResultData FinalizeBattle(BattleEndReason endReason)
    {
        if (m_finalResult != null)
        {
            return m_finalResult.Clone();
        }

        if (!IsInitialized)
        {
            Initialize(CreateSceneEntryData());
        }

        if (endReason == BattleEndReason.None)
        {
            endReason = BattleEndReason.Aborted;
        }

        if (endReason == BattleEndReason.MissionCompleted)
        {
            SetMissionCompleted(true);
        }

        RefreshPlayerDataSubscriptions();
        SyncAllRuntimeMemberStates();
        m_runtimeData.SetMissionCompleted(m_missionCompleted);

        if (!m_runtimeData.BeginFinalization())
        {
            return null;
        }

        BattleOutcome outcome = ResolveBattleOutcome(endReason, m_missionCompleted);
        m_finalResult = new BattleResultData(m_runtimeData, outcome, endReason);
        m_runtimeData.CompleteFinalization();
        if (!TryApplyFinalResult() && GameDataManager.Instance == null)
        {
            Debug.LogWarning("[BattleSceneDataManager] GameDataManager.Instance가 없어 확정 결과를 아직 반영하지 못했습니다.", this);
        }
        return m_finalResult.Clone();
    }

    /// <summary>이미 확정된 최종 결과의 깊은 복사본을 반환합니다.</summary>
    public BattleResultData CreateFinalResultSnapshot()
    {
        return m_finalResult?.Clone();
    }

    /// <summary>미적용 상태의 확정 결과를 현재 GameDataManager에 다시 반영합니다.</summary>
    public bool TryApplyFinalResult()
    {
        if (m_resultApplied)
        {
            return true;
        }

        if (m_finalResult == null || GameDataManager.Instance == null)
        {
            return false;
        }

        m_resultApplied = GameDataManager.Instance.ApplyBattleResult(m_finalResult);
        return m_resultApplied;
    }

    /// <summary>현재 전투 결과를 귀환 정산 UI가 소비할 수 있는 불변 스냅샷으로 만듭니다.</summary>
    public ResultSnapshot CaptureResult()
    {
        RefreshEnemySubscriptions();
        RefreshWeaponSubscriptions();
        RefreshPlayerDataSubscriptions();
        SyncAllRuntimeMemberStates();
        m_characterResults.Clear();
        m_resourceResults.Clear();

        CollectCharacterResults();
        CollectResourceResults();

        return new ResultSnapshot(
            m_missionCompleted,
            m_killCount,
            m_characterResults.ToArray(),
            m_resourceResults.ToArray());
    }

    /// <summary>씬에서 필요한 스쿼드 매니저 참조를 자동으로 탐색합니다.</summary>
    private void AutoFindReferences()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }
    }

    /// <summary>GameDataManager의 영속 데이터와 현재 씬 식별자로 기본 입장 데이터를 만듭니다.</summary>
    private BattleEntryData CreateSceneEntryData()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string stageId = activeScene.IsValid() ? activeScene.name : string.Empty;
        string battleId = $"{stageId}_{Guid.NewGuid():N}";
        int randomSeed = Guid.NewGuid().GetHashCode();

        return GameDataManager.Instance != null
            ? GameDataManager.Instance.CreateBattleEntryData(battleId, stageId, randomSeed)
            : new BattleEntryData(battleId, stageId, randomSeed, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>외부 입장 데이터가 없을 때 사용할 최소 식별 데이터를 만듭니다.</summary>
    private static BattleEntryData CreateEmptyEntryData()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string stageId = activeScene.IsValid() ? activeScene.name : string.Empty;
        return new BattleEntryData(
            $"{stageId}_{Guid.NewGuid():N}",
            stageId,
            Guid.NewGuid().GetHashCode(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>씬 스쿼드 순서에 맞춰 입장 스냅샷의 캐릭터·전투 장비 값을 확정합니다.</summary>
    private void ApplySceneSquadData(BattleEntryData entryData)
    {
        AutoFindReferences();
        if (entryData == null || m_squadManager == null)
        {
            return;
        }

        IReadOnlyList<PlayerbleUnitData> players = m_squadManager.PlayerDataSources;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerbleUnitData player = players[i];
            if (player == null)
            {
                continue;
            }

            BattleMemberEntryData persistedMember = i < entryData.Members.Count ? entryData.Members[i] : null;
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            float maxInjuryGauge = health != null ? health.MaxInjuryGauge : 100.0f;
            float injuryGauge = health != null ? health.CurrentInjuryGauge : 0.0f;
            PlayerInjuryState injuryState = health != null ? health.CurrentInjuryState : PlayerInjuryState.Normal;

            entryData.SetMemberAt(i, new BattleMemberEntryData(
                persistedMember?.DefinitionId ?? string.Empty,
                player.RuntimeId,
                player.CharacterId,
                player.DisplayName,
                player.CurrentHp,
                player.MaxHp,
                injuryGauge,
                maxInjuryGauge,
                injuryState,
                player.CurrentWeaponId,
                player.CurrentMagazineAmmo,
                player.ReserveAmmo,
                i == m_squadManager.PlayerSquadMemberIndex));
        }
    }

    /// <summary>현재 활성 상태인 적 수를 배틀 시작 시점의 전체 적 수로 집계합니다.</summary>
    private static int CountActiveEnemies()
    {
        return FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
    }

    /// <summary>임무 달성 여부와 직접 종료 사유를 귀환 정산 결과로 변환합니다.</summary>
    private static BattleOutcome ResolveBattleOutcome(BattleEndReason endReason, bool missionCompleted)
    {
        if (missionCompleted || endReason == BattleEndReason.MissionCompleted)
        {
            return BattleOutcome.Success;
        }

        return endReason == BattleEndReason.Escaped
            ? BattleOutcome.Evacuated
            : BattleOutcome.Failure;
    }

    /// <summary>현재 활성 적을 찾아 사망 이벤트를 중복 없이 구독합니다.</summary>
    private void RefreshEnemySubscriptions()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyHealth enemy = enemies[i];
            if (enemy == null || !m_subscribedEnemies.Add(enemy))
            {
                continue;
            }

            enemy.OnDeath += HandleEnemyDeath;
        }
    }

    /// <summary>현재 등록된 모든 적 사망 이벤트 구독을 해제합니다.</summary>
    private void UnsubscribeEnemies()
    {
        foreach (EnemyHealth enemy in m_subscribedEnemies)
        {
            if (enemy != null)
            {
                enemy.OnDeath -= HandleEnemyDeath;
            }
        }

        m_subscribedEnemies.Clear();
    }

    /// <summary>스쿼드원 무기의 명중 결과 이벤트를 찾아 캐릭터별 처치 집계와 연결합니다.</summary>
    private void RefreshWeaponSubscriptions()
    {
        AutoFindReferences();

        if (m_squadManager == null)
        {
            return;
        }

        IReadOnlyList<PlayerbleUnitData> players = m_squadManager.PlayerDataSources;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerbleUnitData player = players[i];
            WeaponController weapon = player != null ? player.WeaponController : null;
            if (weapon == null || m_weaponKillHandlers.ContainsKey(weapon))
            {
                continue;
            }

            Action<CombatDamage.HitFeedback> handler = feedback => HandleWeaponHitFeedback(player, feedback);
            m_weaponKillHandlers.Add(weapon, handler);
            weapon.OnHitFeedback += handler;
        }
    }

    /// <summary>현재 등록된 모든 무기 명중 결과 이벤트 구독을 해제합니다.</summary>
    private void UnsubscribeWeapons()
    {
        foreach (KeyValuePair<WeaponController, Action<CombatDamage.HitFeedback>> entry in m_weaponKillHandlers)
        {
            if (entry.Key != null)
            {
                entry.Key.OnHitFeedback -= entry.Value;
            }
        }

        m_weaponKillHandlers.Clear();
    }

    /// <summary>스쿼드 공개 데이터 변경 이벤트를 멤버별 런타임 상태 갱신과 연결합니다.</summary>
    private void RefreshPlayerDataSubscriptions()
    {
        AutoFindReferences();

        if (m_squadManager != null)
        {
            IReadOnlyList<PlayerbleUnitData> players = m_squadManager.PlayerDataSources;
            for (int i = 0; i < players.Count; i++)
            {
                SubscribePlayerData(players[i]);
            }

            return;
        }

        PlayerbleUnitData[] playersWithoutSquad = FindObjectsByType<PlayerbleUnitData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < playersWithoutSquad.Length; i++)
        {
            SubscribePlayerData(playersWithoutSquad[i]);
        }
    }

    /// <summary>지정한 스쿼드원의 공개 데이터 변경 이벤트를 한 번만 구독합니다.</summary>
    private void SubscribePlayerData(PlayerbleUnitData player)
    {
        if (player == null || m_playerDataHandlers.ContainsKey(player))
        {
            return;
        }

        Action handler = () => SyncRuntimeMemberState(player);
        m_playerDataHandlers.Add(player, handler);
        player.OnPublicDataChanged += handler;
    }

    /// <summary>현재 구독 중인 모든 스쿼드 공개 데이터 이벤트를 해제합니다.</summary>
    private void UnsubscribePlayerData()
    {
        foreach (KeyValuePair<PlayerbleUnitData, Action> entry in m_playerDataHandlers)
        {
            if (entry.Key != null)
            {
                entry.Key.OnPublicDataChanged -= entry.Value;
            }
        }

        m_playerDataHandlers.Clear();
    }

    /// <summary>현재 스쿼드원 전체의 공개 상태를 배틀 런타임 데이터에 다시 반영합니다.</summary>
    private void SyncAllRuntimeMemberStates()
    {
        if (m_runtimeData == null)
        {
            return;
        }

        AutoFindReferences();
        if (m_squadManager != null)
        {
            IReadOnlyList<PlayerbleUnitData> players = m_squadManager.PlayerDataSources;
            for (int i = 0; i < players.Count; i++)
            {
                SyncRuntimeMemberState(players[i]);
            }

            return;
        }

        foreach (PlayerbleUnitData player in m_playerDataHandlers.Keys)
        {
            SyncRuntimeMemberState(player);
        }
    }

    /// <summary>지정한 스쿼드원의 생존·부상·장비·탄약·조작 역할을 런타임 데이터에 반영합니다.</summary>
    private void SyncRuntimeMemberState(PlayerbleUnitData player)
    {
        if (player == null || m_runtimeData == null)
        {
            return;
        }

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        m_runtimeData.UpdateMemberState(
            player.RuntimeId,
            player.CharacterId,
            player.CurrentHp,
            player.MaxHp,
            health != null ? health.CurrentInjuryGauge : 0.0f,
            health != null ? health.MaxInjuryGauge : 100.0f,
            health != null ? health.CurrentInjuryState : PlayerInjuryState.Normal,
            health != null ? health.IsDowned : player.IsDown,
            health != null ? health.IsDead : player.IsDead,
            player.CurrentWeaponId,
            player.CurrentMagazineAmmo,
            player.MagazineCapacity,
            player.ReserveAmmo,
            player.MaxReserveAmmo,
            player.IsPlayerSquadMember);
    }

    /// <summary>적 사망을 전체 처치 수와 배틀 런타임 데이터에 반영합니다.</summary>
    private void HandleEnemyDeath()
    {
        if (IsFinalized)
        {
            return;
        }

        m_killCount++;
        m_runtimeData?.RecordEnemyKill();
    }

    /// <summary>무기가 적 처치를 확정했을 때 해당 무기 소유 스쿼드원의 처치 수를 증가시킵니다.</summary>
    private void HandleWeaponHitFeedback(PlayerbleUnitData player, CombatDamage.HitFeedback feedback)
    {
        if (!feedback.Killed || player == null)
        {
            return;
        }

        RecordCharacterKill(player);
    }

    /// <summary>현재 스쿼드 순서대로 기존 Result UI용 캐릭터 결과를 수집합니다.</summary>
    private void CollectCharacterResults()
    {
        AutoFindReferences();

        if (m_squadManager != null)
        {
            IReadOnlyList<PlayerbleUnitData> players = m_squadManager.PlayerDataSources;
            for (int i = 0; i < players.Count; i++)
            {
                AddCharacterResult(players[i]);
            }

            return;
        }

        PlayerbleUnitData[] playersWithoutSquad = FindObjectsByType<PlayerbleUnitData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < playersWithoutSquad.Length; i++)
        {
            AddCharacterResult(playersWithoutSquad[i]);
        }
    }

    /// <summary>지정한 스쿼드원의 최종 부상·전투 이탈·처치 값을 기존 Result UI 결과에 추가합니다.</summary>
    private void AddCharacterResult(PlayerbleUnitData player)
    {
        if (player == null)
        {
            return;
        }

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        int injuryGauge = health != null ? health.RoundedInjuryGauge : 0;
        PlayerInjuryState injuryState = health != null ? health.CurrentInjuryState : PlayerInjuryState.Normal;
        bool isCombatOut = health != null && health.IsDead;

        m_characterResults.Add(new PlayerbleResult(
            player.DisplayName,
            player.CharacterId,
            injuryGauge,
            injuryState,
            isCombatOut,
            GetCharacterKillCount(player)));
    }

    /// <summary>지정한 스쿼드원이 현재 배틀에서 확정한 적 처치 수를 반환합니다.</summary>
    private int GetCharacterKillCount(PlayerbleUnitData player)
    {
        return player != null && m_characterKillCounts.TryGetValue(player, out int killCount)
            ? killCount
            : 0;
    }

    /// <summary>0보다 큰 목업 획득 자원을 기존 Result UI 결과에 추가합니다.</summary>
    private void CollectResourceResults()
    {
        for (int i = 0; i < m_mockResources.Count; i++)
        {
            ResourceResult entry = m_mockResources[i];
            if (entry.Count > 0)
            {
                m_resourceResults.Add(entry);
            }
        }
    }
}
