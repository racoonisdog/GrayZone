using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VInspector;

/// <summary>
/// 필드 씬에서 발생한 런타임 결과값을 수집하고, 귀환 시점에 스냅샷으로 확정하는 씬 전용 데이터 매니저입니다.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class FieldSceneDataManager : MonoBehaviour
{
    /// <summary>
    /// 귀환 정산 화면의 자원 슬롯 하나에 표시할 자원 정보입니다.
    /// </summary>
    [Serializable]
    public struct ResourceResult
    {
        /// <summary>자원 데이터를 외부 시스템과 연결할 때 사용할 식별자입니다.</summary>
        public int id;

        /// <summary>셸터 자원 체계와 연결되는 안정적인 자원 ID입니다.</summary>
        public string ResourceId;

        /// <summary>목업 결과 UI에 표시할 자원 아이콘입니다.</summary>
        public Texture2D Icon;

        /// <summary>이번 필드에서 확보한 자원 수량입니다.</summary>
        [Min(0)] public int Count;
    }

    /// <summary>
    /// 귀환 시점에 확정되는 스쿼드원 한 명의 필드 결과입니다.
    /// </summary>
    public readonly struct PlayerbleResult
    {
        /// <summary>스쿼드원 한 명의 귀환 정산 표시값을 생성합니다.</summary>
        public PlayerbleResult(string displayName, PlayerbleCharacterId characterId, Sprite portrait, int injuryGauge, CharacterInjuryState injuryState, bool isCombatOut, int killCount)
        {
            DisplayName = displayName;
            CharacterId = characterId;
            Portrait = portrait;
            InjuryGauge = injuryGauge;
            InjuryState = injuryState;
            IsCombatOut = isCombatOut;
            KillCount = killCount;
        }

        /// <summary>결과 UI와 로그에 표시할 캐릭터 이름입니다.</summary>
        public string DisplayName { get; }

        /// <summary>결과창 캐릭터 초상화 선택 등에 사용하는 고정 캐릭터 식별자입니다.</summary>
        public PlayerbleCharacterId CharacterId { get; }

        /// <summary>필드 결과 UI에 직접 전달할 플레이어블 캐릭터 초상화입니다.</summary>
        public Sprite Portrait { get; }

        /// <summary>필드 귀환 시점에 반올림하여 확정한 부상 게이지입니다.</summary>
        public int InjuryGauge { get; }

        /// <summary>부상 게이지에서 판정된 부상 상태입니다.</summary>
        public CharacterInjuryState InjuryState { get; }

        /// <summary>이번 필드에서 구조 실패 등으로 전투 이탈했는지 여부입니다.</summary>
        public bool IsCombatOut { get; }

        /// <summary>이번 필드에서 이 캐릭터의 무기가 확정한 적 처치 수입니다.</summary>
        public int KillCount { get; }
    }

    /// <summary>
    /// 귀환 순간의 필드 결과를 고정한 읽기 전용 데이터 묶음입니다.
    /// UI와 이후 셸터 반영은 이 스냅샷만 소비해야 필드 진행 중 상태 변화와 분리됩니다.
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

        /// <summary>필드 전체에서 사망 처리된 적의 수입니다.</summary>
        public int KillCount { get; }

        /// <summary>스쿼드원별 필드 부상·전투 이탈·처치 결과입니다.</summary>
        public IReadOnlyList<PlayerbleResult> Characters { get; }

        /// <summary>이번 필드에서 확보한 자원 결과입니다.</summary>
        public IReadOnlyList<ResourceResult> Resources { get; }
    }

    [Foldout("References")]
    [SerializeField] private SquadManager m_squadManager;

    [Foldout("Runtime State")]
    [ReadOnly][SerializeField] private FieldEntryData m_entryData;

    [Foldout("Runtime State")]
    [ReadOnly][SerializeField] private FieldRuntimeData m_runtimeData = new();

    [Foldout("Result State")]
    [ReadOnly][SerializeField] private FieldResultData m_finalResult;

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
    private readonly HashSet<EnemyHealth> m_countedEnemyActivations = new();
    private readonly Dictionary<WeaponController, Action<CombatDamage.HitFeedback>> m_weaponKillHandlers = new();
    private readonly Dictionary<PlayerbleUnitData, Action> m_playerDataHandlers = new();
    private readonly Dictionary<PlayerbleUnitData, int> m_characterKillCounts = new();
    private readonly List<PlayerbleResult> m_characterResults = new();
    private readonly List<ResourceResult> m_resourceResults = new();
    private SquadManager m_subscribedSquadManager;

    /// <summary>스쿼드 전멸로 정산 없는 게임오버 화면 전환이 필요할 때 발생합니다.</summary>
    public event Action OnGameOverRequested;

    /// <summary>현재 필드의 임무 목표 달성 여부입니다.</summary>
    public bool MissionCompleted => m_missionCompleted;

    /// <summary>현재 필드에서 확정된 전체 적 처치 수입니다.</summary>
    public int KillCount => m_killCount;

    /// <summary>입장 데이터가 적용되어 필드 런타임 데이터가 준비되었는지 여부입니다.</summary>
    public bool IsInitialized => m_runtimeData != null && m_runtimeData.Phase != FieldPhase.Uninitialized;

    /// <summary>필드 최종 결과가 한 번 이상 확정되었는지 여부입니다.</summary>
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

    /// <summary>외부 입장 데이터가 주입되지 않았으면 현재 씬 기준으로 필드 데이터를 초기화합니다.</summary>
    private void Start()
    {
        if (!IsInitialized)
        {
            Initialize(CreateSceneEntryData());
        }
    }

    /// <summary>필드가 진행 중인 동안 경과 시간을 누적합니다.</summary>
    private void Update()
    {
        m_runtimeData?.AddElapsedTime(Time.deltaTime);
    }

    /// <summary>활성화 시 적·무기·스쿼드 공개 데이터 이벤트를 구독합니다.</summary>
    private void OnEnable()
    {
        EnemyHealth.OnEnemyEnabled += HandleEnemyEnabled;
        RefreshEnemySubscriptions();
        RefreshWeaponSubscriptions();
        RefreshPlayerDataSubscriptions();
        RefreshSquadEliminationSubscription();
    }

    /// <summary>비활성화 시 이 컴포넌트가 등록한 모든 런타임 이벤트를 해제합니다.</summary>
    private void OnDisable()
    {
        EnemyHealth.OnEnemyEnabled -= HandleEnemyEnabled;
        UnsubscribeEnemies();
        UnsubscribeWeapons();
        UnsubscribePlayerData();
        UnsubscribeSquadElimination();
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
    public void RecordResource(
        string resourceId,
        int amount,
        Texture2D icon = null)
    {
        string id = ResourceIds.Normalize(resourceId);
        if (IsFinalized || string.IsNullOrEmpty(id) || amount <= 0)
        {
            return;
        }

        m_runtimeData?.RecordResource(id, amount);

        for (int i = 0; i < m_mockResources.Count; i++)
        {
            if (!string.Equals(
                    m_mockResources[i].ResourceId,
                    id,
                    StringComparison.Ordinal))
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
            ResourceId = id,
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

    /// <summary>새 필드 시작 시 누적 결과를 초기화합니다.</summary>
    public void ResetFieldResult()
    {
        m_missionCompleted = false;
        m_killCount = 0;
        m_finalResult = null;
        m_resultApplied = false;
        m_characterKillCounts.Clear();
        m_mockResources.Clear();

        if (m_entryData != null)
        {
            m_runtimeData ??= new FieldRuntimeData();
            m_runtimeData.Initialize(m_entryData, CountActiveEnemies());
            ResetCountedEnemyActivations();
            m_runtimeData.StartField();
        }

        RefreshEnemySubscriptions();
        RefreshWeaponSubscriptions();
        RefreshPlayerDataSubscriptions();
        SyncAllRuntimeMemberStates();
    }

    /// <summary>
    /// 필드 입장 스냅샷을 복제하고 현재 씬의 실제 스쿼드 상태로 보완하여 런타임 데이터를 시작합니다.
    /// </summary>
    public void Initialize(FieldEntryData entryData)
    {
        m_entryData = entryData?.Clone() ?? CreateEmptyEntryData();
        ApplySceneSquadData(m_entryData);
        BindSceneCharactersToGameDataManager();

        m_missionCompleted = false;
        m_killCount = 0;
        m_finalResult = null;
        m_resultApplied = false;
        m_characterKillCounts.Clear();

        m_runtimeData ??= new FieldRuntimeData();
        m_runtimeData.Initialize(m_entryData, CountActiveEnemies());
        ResetCountedEnemyActivations();
        for (int i = 0; i < m_mockResources.Count; i++)
        {
            ResourceResult resource = m_mockResources[i];
            m_runtimeData.RecordResource(
                resource.ResourceId,
                resource.Count);
        }

        m_runtimeData.StartField();
        RefreshEnemySubscriptions();
        RefreshWeaponSubscriptions();
        RefreshPlayerDataSubscriptions();
        SyncAllRuntimeMemberStates();
    }

    /// <summary>필드 씬에서 최종 구성된 스쿼드 스냅샷을 GameDataManager 정본에 먼저 등록합니다.</summary>
    private void BindSceneCharactersToGameDataManager()
    {
        GameDataManager gameDataManager = GameDataManager.Instance;
        if (gameDataManager == null || m_entryData == null)
        {
            Debug.LogWarning("[FieldSceneDataManager] 필드 캐릭터 바인딩을 수행할 GameDataManager 또는 입장 데이터가 없습니다.", this);
            return;
        }

        int boundCount = 0;
        for (int i = 0; i < m_entryData.Members.Count; i++)
        {
            CharacterSnapshotData snapshot = m_entryData.Members[i]?.Snapshot;
            if (gameDataManager.TryBindFieldCharacterSnapshot(snapshot))
            {
                boundCount++;
            }
        }

        Debug.Log(
            $"[FieldSceneDataManager] 필드 캐릭터 GameDataManager 바인딩 완료. bound={boundCount}, "
            + $"source={m_entryData.Members.Count}, total={gameDataManager.CharacterCount}",
            this);
    }

    /// <summary>현재 필드 입장 데이터를 외부 변경으로부터 분리된 스냅샷으로 반환합니다.</summary>
    public FieldEntryData CreateEntrySnapshot()
    {
        return m_entryData?.Clone();
    }

    /// <summary>현재 필드 런타임 데이터를 외부 변경으로부터 분리된 스냅샷으로 반환합니다.</summary>
    public FieldRuntimeData CreateRuntimeSnapshot()
    {
        return m_runtimeData?.Clone();
    }

    /// <summary>
    /// 현재 필드 값을 한 번만 최종 결과로 확정하고 GameDataManager에 반영합니다.
    /// 이후 호출은 최초 확정 결과의 복사본만 반환합니다.
    /// </summary>
    public FieldResultData FinalizeField(FieldEndReason endReason)
    {
        if (m_finalResult != null)
        {
            return m_finalResult.Clone();
        }

        if (!IsInitialized)
        {
            Initialize(CreateSceneEntryData());
        }

        if (endReason == FieldEndReason.None)
        {
            endReason = FieldEndReason.Aborted;
        }

        if (endReason == FieldEndReason.MissionCompleted)
        {
            SetMissionCompleted(true);
        }

        RefreshPlayerDataSubscriptions();
        SyncAllRuntimeMemberStates();
        if (endReason == FieldEndReason.Escaped || endReason == FieldEndReason.MissionCompleted)
        {
            m_runtimeData.ConfirmDownMembersAsCombatOut();
        }

        m_runtimeData.SetMissionCompleted(m_missionCompleted);

        if (!m_runtimeData.BeginFinalization())
        {
            return null;
        }

        FieldOutcome outcome = ResolveFieldOutcome(endReason, m_missionCompleted);
        m_finalResult = new FieldResultData(m_runtimeData, outcome, endReason);
        m_runtimeData.CompleteFinalization();
        if (!TryApplyFinalResult() && GameDataManager.Instance == null)
        {
            Debug.LogWarning("[FieldSceneDataManager] GameDataManager.Instance가 없어 확정 결과를 아직 반영하지 못했습니다.", this);
        }
        return m_finalResult.Clone();
    }

    /// <summary>이미 확정된 최종 결과의 깊은 복사본을 반환합니다.</summary>
    public FieldResultData CreateFinalResultSnapshot()
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

        m_resultApplied = GameDataManager.Instance.ApplyFieldResult(m_finalResult);
        return m_resultApplied;
    }

    /// <summary>현재 필드 결과를 귀환 정산 UI가 소비할 수 있는 불변 스냅샷으로 만듭니다.</summary>
    public ResultSnapshot CaptureResult()
    {
        if (m_finalResult != null)
        {
            return CreateResultSnapshot(m_finalResult);
        }

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

    /// <summary>확정된 필드 결과만 사용해 귀환 정산 UI용 불변 스냅샷을 생성합니다.</summary>
    private ResultSnapshot CreateResultSnapshot(FieldResultData resultData)
    {
        m_characterResults.Clear();
        m_resourceResults.Clear();

        for (int i = 0; i < resultData.Members.Count; i++)
        {
            CharacterSnapshotData snapshot = resultData.Members[i]?.Snapshot;
            if (snapshot == null)
            {
                continue;
            }

            m_characterResults.Add(new PlayerbleResult(
                snapshot.DisplayName,
                snapshot.CharacterId,
                ResolveResultPortrait(snapshot.CharacterId),
                Mathf.RoundToInt(snapshot.InjurySeverityGauge),
                snapshot.InjuryState,
                snapshot.IsCombatOut,
                snapshot.KillCount));
        }

        for (int i = 0; i < resultData.AcquiredResources.Count; i++)
        {
            FieldResourceAmountData resource = resultData.AcquiredResources[i];
            if (resource == null || resource.Amount <= 0)
            {
                continue;
            }

            ResourceResult presentation =
                FindResourcePresentation(resource.ResourceId);
            presentation.ResourceId = resource.ResourceId;
            presentation.Count = resource.Amount;
            m_resourceResults.Add(presentation);
        }

        return new ResultSnapshot(
            resultData.MissionCompleted,
            resultData.TotalKillCount,
            m_characterResults.ToArray(),
            m_resourceResults.ToArray());
    }

    /// <summary>지정한 자원 종류에 대응하는 목업 ID와 아이콘 표시 정보를 찾습니다.</summary>
    private ResourceResult FindResourcePresentation(string resourceId)
    {
        string id = ResourceIds.Normalize(resourceId);
        for (int i = 0; i < m_mockResources.Count; i++)
        {
            if (string.Equals(
                    m_mockResources[i].ResourceId,
                    id,
                    StringComparison.Ordinal))
            {
                return m_mockResources[i];
            }
        }

        return new ResourceResult { ResourceId = id };
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
    private FieldEntryData CreateSceneEntryData()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string stageId = activeScene.IsValid() ? activeScene.name : string.Empty;
        string fieldId = $"{stageId}_{Guid.NewGuid():N}";
        int randomSeed = Guid.NewGuid().GetHashCode();

        return GameDataManager.Instance != null
            ? GameDataManager.Instance.CreateFieldEntryData(fieldId, stageId, randomSeed)
            : new FieldEntryData(fieldId, stageId, randomSeed, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>외부 입장 데이터가 없을 때 사용할 최소 식별 데이터를 만듭니다.</summary>
    private static FieldEntryData CreateEmptyEntryData()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string stageId = activeScene.IsValid() ? activeScene.name : string.Empty;
        return new FieldEntryData(
            $"{stageId}_{Guid.NewGuid():N}",
            stageId,
            Guid.NewGuid().GetHashCode(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>영속 정의 ID를 우선 사용해 입장 스냅샷과 씬 스쿼드원의 캐릭터·장비 값을 연결합니다.</summary>
    private void ApplySceneSquadData(FieldEntryData entryData)
    {
        AutoFindReferences();
        if (entryData == null || m_squadManager == null)
        {
            return;
        }

        IReadOnlyList<PlayerbleUnitData> players = m_squadManager.PlayerDataSources;
        List<FieldMemberEntryData> persistedMembers = new();
        for (int i = 0; i < entryData.Members.Count; i++)
        {
            if (entryData.Members[i] != null)
            {
                persistedMembers.Add(entryData.Members[i].Clone());
            }
        }

        entryData.ClearMembers();
        HashSet<int> usedPersistedIndices = new();
        bool canUseIndexFallback = persistedMembers.Count == players.Count;
        bool indexFallbackLogged = false;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerbleUnitData player = players[i];
            if (player == null)
            {
                continue;
            }

            int persistedIndex = FindPersistedMemberIndex(player.DefinitionId, persistedMembers, usedPersistedIndices);
            if (persistedIndex < 0
                && string.IsNullOrWhiteSpace(player.DefinitionId)
                && canUseIndexFallback
                && i < persistedMembers.Count
                && !usedPersistedIndices.Contains(i))
            {
                persistedIndex = i;
                if (!indexFallbackLogged && persistedMembers.Count > 0)
                {
                    Debug.LogWarning(
                        "[FieldSceneDataManager] PlayerbleUnitData.DefinitionId가 없어 씬 순서로 출전 데이터를 연결합니다. "
                        + "외부 스폰 연결 시 DefinitionId를 반드시 주입해야 합니다.",
                        this);
                    indexFallbackLogged = true;
                }
            }

            FieldMemberEntryData persistedMember = persistedIndex >= 0 ? persistedMembers[persistedIndex] : null;
            if (persistedIndex >= 0)
            {
                usedPersistedIndices.Add(persistedIndex);
            }

            CharacterSnapshotData persistedSnapshot = persistedMember?.Snapshot;
            if (persistedSnapshot != null)
            {
                player.ApplyCharacterSnapshot(persistedSnapshot);
            }

            NPCType npcType = persistedSnapshot?.NpcType ?? default;
            CharacterSnapshotData sceneSnapshot = player.CreateCharacterSnapshot(npcType);
            CharacterSnapshotData resolvedSnapshot = persistedSnapshot?.Clone() ?? sceneSnapshot.Clone();

            string definitionId = !string.IsNullOrWhiteSpace(player.DefinitionId)
                ? player.DefinitionId
                : persistedMember?.DefinitionId ?? string.Empty;

            resolvedSnapshot.SetPersistentIdentity(definitionId, npcType);
            resolvedSnapshot.SetSceneIdentity(player.RuntimeId, player.CharacterId, player.DisplayName);
            resolvedSnapshot.SetReliability(sceneSnapshot.Reliability);
            resolvedSnapshot.SetCombatState(
                sceneSnapshot.CurrentHp,
                sceneSnapshot.MaxHp,
                sceneSnapshot.InjurySeverityGauge,
                sceneSnapshot.MaxInjuryGauge,
                sceneSnapshot.InjuryState,
                sceneSnapshot.IsDown,
                sceneSnapshot.IsCombatOut,
                i == m_squadManager.PlayerSquadMemberIndex);

            WeaponSnapshotData resolvedWeapon = resolvedSnapshot.Weapon;
            resolvedWeapon.MergeSceneAmmo(sceneSnapshot.Weapon);
            resolvedSnapshot.SetWeapon(resolvedWeapon);
            entryData.AddMember(new FieldMemberEntryData(resolvedSnapshot));
        }


        if (!canUseIndexFallback && persistedMembers.Count > 0 && usedPersistedIndices.Count < persistedMembers.Count)
        {
            Debug.LogWarning(
                $"[FieldSceneDataManager] 출전 데이터와 씬 스쿼드 구성이 일치하지 않아 연결되지 않은 영속 멤버를 제외했습니다. "
                + $"entry={persistedMembers.Count}, scene={players.Count}, matched={usedPersistedIndices.Count}",
                this);
        }
    }

    /// <summary>아직 사용하지 않은 입장 멤버 중 영속 정의 ID가 일치하는 목록 인덱스를 찾습니다.</summary>
    private static int FindPersistedMemberIndex(
        string definitionId,
        IReadOnlyList<FieldMemberEntryData> persistedMembers,
        ISet<int> usedIndices)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return -1;
        }

        string normalizedDefinitionId = definitionId.Trim();
        for (int i = 0; i < persistedMembers.Count; i++)
        {
            if (!usedIndices.Contains(i) && persistedMembers[i].DefinitionId == normalizedDefinitionId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>현재 활성 상태인 적 수를 필드 시작 시점의 전체 적 수로 집계합니다.</summary>
    private static int CountActiveEnemies()
    {
        return FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
    }

    /// <summary>필드 시작 시점에 활성 상태인 적을 이미 집계된 인스턴스로 등록합니다.</summary>
    private void ResetCountedEnemyActivations()
    {
        m_countedEnemyActivations.Clear();
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] != null)
            {
                m_countedEnemyActivations.Add(enemies[i]);
            }
        }
    }

    /// <summary>임무 달성 여부와 직접 종료 사유를 귀환 정산 결과로 변환합니다.</summary>
    private static FieldOutcome ResolveFieldOutcome(FieldEndReason endReason, bool missionCompleted)
    {
        if (missionCompleted || endReason == FieldEndReason.MissionCompleted)
        {
            return FieldOutcome.Success;
        }

        return endReason == FieldEndReason.Escaped
            ? FieldOutcome.Evacuated
            : FieldOutcome.Failure;
    }

    /// <summary>현재 활성 적을 찾아 사망 이벤트를 중복 없이 구독합니다.</summary>
    private void RefreshEnemySubscriptions()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyHealth enemy = enemies[i];
            SubscribeEnemy(enemy);
            RecordEnemyActivationIfNew(enemy);
        }
    }

    /// <summary>적 사망 이벤트를 중복 없이 구독하고 새 구독 여부를 반환합니다.</summary>
    private bool SubscribeEnemy(EnemyHealth enemy)
    {
        if (enemy == null || !m_subscribedEnemies.Add(enemy))
        {
            return false;
        }

        enemy.OnDeath += HandleEnemyDeath;
        return true;
    }

    /// <summary>필드 도중 활성화된 적을 구독하고 전체 및 생존 적 수에 추가합니다.</summary>
    private void HandleEnemyEnabled(EnemyHealth enemy)
    {
        SubscribeEnemy(enemy);
        RecordEnemyActivationIfNew(enemy);
    }

    /// <summary>아직 집계하지 않은 적 활성화만 새 적 생성으로 런타임 데이터에 반영합니다.</summary>
    private void RecordEnemyActivationIfNew(EnemyHealth enemy)
    {
        if (enemy != null && m_countedEnemyActivations.Add(enemy))
        {
            m_runtimeData?.RecordEnemySpawned();
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

    /// <summary>현재 스쿼드 매니저의 전멸 이벤트 구독을 최신 참조로 갱신합니다.</summary>
    private void RefreshSquadEliminationSubscription()
    {
        AutoFindReferences();
        if (m_subscribedSquadManager == m_squadManager)
        {
            return;
        }

        UnsubscribeSquadElimination();
        m_subscribedSquadManager = m_squadManager;
        if (m_subscribedSquadManager != null)
        {
            m_subscribedSquadManager.OnSquadEliminated += HandleSquadEliminated;
        }
    }

    /// <summary>현재 스쿼드 매니저에 등록한 전멸 이벤트 구독을 해제합니다.</summary>
    private void UnsubscribeSquadElimination()
    {
        if (m_subscribedSquadManager != null)
        {
            m_subscribedSquadManager.OnSquadEliminated -= HandleSquadEliminated;
            m_subscribedSquadManager = null;
        }
    }

    /// <summary>스쿼드 전멸 상태를 고정하고 정산 없이 게임오버 화면 전환을 요청합니다.</summary>
    private void HandleSquadEliminated()
    {
        if (IsFinalized || m_runtimeData == null)
        {
            return;
        }

        SyncAllRuntimeMemberStates();
        if (m_runtimeData.EnterGameOver())
        {
            OnGameOverRequested?.Invoke();
        }
    }

    /// <summary>현재 스쿼드원 전체의 공개 상태를 필드 런타임 데이터에 다시 반영합니다.</summary>
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

        m_runtimeData.UpdateMemberSnapshot(player.CreateCharacterSnapshot());
    }

    /// <summary>적 사망을 전체 처치 수와 필드 런타임 데이터에 반영합니다.</summary>
    private void HandleEnemyDeath()
    {
        if (IsFinalized)
        {
            return;
        }

        m_killCount++;
        m_runtimeData?.RecordEnemyKill();
        m_countedEnemyActivations.RemoveWhere(enemy => enemy == null || enemy.IsDead);
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

    /// <summary>지정한 스쿼드원의 최종 부상·이탈·처치 값을 기존 Result UI 결과에 추가합니다.</summary>
    private void AddCharacterResult(PlayerbleUnitData player)
    {
        if (player == null)
        {
            return;
        }

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        int injuryGauge = health != null ? health.RoundedInjuryGauge : 0;
        CharacterInjuryState injuryState = health != null ? health.CurrentInjuryState : CharacterInjuryState.Normal;
        bool isCombatOut = health != null && health.IsDead;

        m_characterResults.Add(new PlayerbleResult(
            player.DisplayName,
            player.CharacterId,
            player.ResultPortrait,
            injuryGauge,
            injuryState,
            isCombatOut,
            GetCharacterKillCount(player)));
    }

    /// <summary>지정한 스쿼드원이 현재 필드에서 확정한 적 처치 수를 반환합니다.</summary>
    private int GetCharacterKillCount(PlayerbleUnitData player)
    {
        return player != null && m_characterKillCounts.TryGetValue(player, out int killCount)
            ? killCount
            : 0;
    }

    private Sprite ResolveResultPortrait(PlayerbleCharacterId characterId)
    {
        if (m_squadManager == null)
        {
            return null;
        }

        IReadOnlyList<PlayerbleUnitData> players = m_squadManager.PlayerDataSources;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerbleUnitData player = players[i];
            if (player != null && player.CharacterId == characterId)
            {
                return player.ResultPortrait;
            }
        }

        return null;
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
