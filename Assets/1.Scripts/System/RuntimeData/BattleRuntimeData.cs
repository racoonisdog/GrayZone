using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>배틀 씬 데이터가 거치는 생명주기 단계입니다.</summary>
public enum BattlePhase
{
    /// <summary>입장 데이터가 아직 적용되지 않은 상태입니다.</summary>
    Uninitialized,

    /// <summary>입장 데이터와 씬 초기값이 준비된 상태입니다.</summary>
    Ready,

    /// <summary>배틀 시간이 흐르고 결과를 수집하는 상태입니다.</summary>
    Running,

    /// <summary>귀환 정산 값을 확정하는 상태입니다.</summary>
    Finalizing,

    /// <summary>최종 결과 스냅샷 생성이 끝난 상태입니다.</summary>
    Completed
}

/// <summary>귀환 정산에서 사용하는 배틀의 최종 결과입니다.</summary>
public enum BattleOutcome
{
    /// <summary>아직 최종 결과가 판정되지 않았습니다.</summary>
    None,

    /// <summary>배틀 목표를 달성했습니다.</summary>
    Success,

    /// <summary>배틀 목표 달성에 실패했습니다.</summary>
    Failure,

    /// <summary>임무 완료와 무관하게 스쿼드가 귀환했습니다.</summary>
    Evacuated
}

/// <summary>배틀이 종료된 직접적인 사유입니다.</summary>
public enum BattleEndReason
{
    /// <summary>아직 종료 사유가 확정되지 않았습니다.</summary>
    None,

    /// <summary>임무 목표 달성으로 종료되었습니다.</summary>
    MissionCompleted,

    /// <summary>탈출 지점을 통한 귀환으로 종료되었습니다.</summary>
    Escaped,

    /// <summary>모든 스쿼드원이 전투 이탈하여 종료되었습니다.</summary>
    SquadEliminated,

    /// <summary>개발 또는 시스템 명령으로 중단되었습니다.</summary>
    Aborted
}

/// <summary>배틀 입장 또는 획득 자원 한 종류의 수량입니다.</summary>
[Serializable]
public sealed class BattleResourceAmountData
{
    [SerializeField] private CurrencyType type;
    [Min(0)][SerializeField] private int amount;

    /// <summary>자원 종류입니다.</summary>
    public CurrencyType Type => type;

    /// <summary>0 이상으로 보정된 자원 수량입니다.</summary>
    public int Amount => Mathf.Max(0, amount);

    /// <summary>지정한 자원 종류와 수량으로 데이터를 생성합니다.</summary>
    public BattleResourceAmountData(CurrencyType type, int amount)
    {
        this.type = type;
        this.amount = Mathf.Max(0, amount);
    }

    /// <summary>현재 값을 복제한 새 자원 데이터를 반환합니다.</summary>
    public BattleResourceAmountData Clone()
    {
        return new BattleResourceAmountData(Type, Amount);
    }

    /// <summary>현재 수량에 지정한 값을 더하고 0 이상으로 보정합니다.</summary>
    public void Add(int value)
    {
        amount = Mathf.Max(0, amount + value);
    }
}

/// <summary>배틀 입장 시점에 확정하는 스쿼드원 한 명의 초기 데이터입니다.</summary>
[Serializable]
public sealed class BattleMemberEntryData
{
    [SerializeField] private string definitionId = string.Empty;
    [SerializeField] private string runtimeId = string.Empty;
    [SerializeField] private PlayableCharacterId characterId;
    [SerializeField] private string displayName = string.Empty;
    [Min(0)][SerializeField] private int currentHp;
    [Min(1)][SerializeField] private int maxHp = 1;
    [Min(0.0f)][SerializeField] private float injuryGauge;
    [Min(1.0f)][SerializeField] private float maxInjuryGauge = 100.0f;
    [SerializeField] private PlayerInjuryState injuryState;
    [SerializeField] private string weaponId = string.Empty;
    [Min(0)][SerializeField] private int magazineAmmo;
    [Min(0)][SerializeField] private int reserveAmmo;
    [SerializeField] private bool isPlayerSquadMember;

    /// <summary>영속 로스터에서 사용하는 캐릭터 정의 ID입니다.</summary>
    public string DefinitionId => definitionId ?? string.Empty;

    /// <summary>현재 배틀 씬 인스턴스를 식별하는 런타임 ID입니다.</summary>
    public string RuntimeId => runtimeId ?? string.Empty;

    /// <summary>플레이어블 캐릭터의 고정 ID입니다.</summary>
    public PlayableCharacterId CharacterId => characterId;

    /// <summary>결과 UI와 로그에 표시할 이름입니다.</summary>
    public string DisplayName => displayName ?? string.Empty;

    /// <summary>배틀 입장 시점의 현재 HP입니다.</summary>
    public int CurrentHp => Mathf.Clamp(currentHp, 0, MaxHp);

    /// <summary>배틀 입장 시점의 최대 HP입니다.</summary>
    public int MaxHp => Mathf.Max(1, maxHp);

    /// <summary>배틀 기준으로 환산된 누적 부상 게이지입니다.</summary>
    public float InjuryGauge => Mathf.Clamp(injuryGauge, 0.0f, MaxInjuryGauge);

    /// <summary>누적 부상 게이지의 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, maxInjuryGauge);

    /// <summary>배틀 입장 시점의 부상 단계입니다.</summary>
    public PlayerInjuryState InjuryState => injuryState;

    /// <summary>장착 무기의 정의 ID입니다.</summary>
    public string WeaponId => weaponId ?? string.Empty;

    /// <summary>배틀 입장 시점의 탄창 내 탄약 수입니다.</summary>
    public int MagazineAmmo => Mathf.Max(0, magazineAmmo);

    /// <summary>배틀 입장 시점의 예비 탄약 수입니다.</summary>
    public int ReserveAmmo => Mathf.Max(0, reserveAmmo);

    /// <summary>배틀 시작 시 직접 조작 대상으로 지정된 멤버인지 여부입니다.</summary>
    public bool IsPlayerSquadMember => isPlayerSquadMember;

    /// <summary>스쿼드원 한 명의 배틀 입장 값을 생성합니다.</summary>
    public BattleMemberEntryData(
        string definitionId,
        string runtimeId,
        PlayableCharacterId characterId,
        string displayName,
        int currentHp,
        int maxHp,
        float injuryGauge,
        float maxInjuryGauge,
        PlayerInjuryState injuryState,
        string weaponId,
        int magazineAmmo,
        int reserveAmmo,
        bool isPlayerSquadMember)
    {
        this.definitionId = definitionId?.Trim() ?? string.Empty;
        this.runtimeId = runtimeId?.Trim() ?? string.Empty;
        this.characterId = characterId;
        this.displayName = displayName?.Trim() ?? string.Empty;
        this.maxHp = Mathf.Max(1, maxHp);
        this.currentHp = Mathf.Clamp(currentHp, 0, this.maxHp);
        this.maxInjuryGauge = Mathf.Max(1.0f, maxInjuryGauge);
        this.injuryGauge = Mathf.Clamp(injuryGauge, 0.0f, this.maxInjuryGauge);
        this.injuryState = injuryState;
        this.weaponId = weaponId?.Trim() ?? string.Empty;
        this.magazineAmmo = Mathf.Max(0, magazineAmmo);
        this.reserveAmmo = Mathf.Max(0, reserveAmmo);
        this.isPlayerSquadMember = isPlayerSquadMember;
    }

    /// <summary>현재 값을 복제한 새 멤버 입장 데이터를 반환합니다.</summary>
    public BattleMemberEntryData Clone()
    {
        return new BattleMemberEntryData(
            DefinitionId,
            RuntimeId,
            CharacterId,
            DisplayName,
            CurrentHp,
            MaxHp,
            InjuryGauge,
            MaxInjuryGauge,
            InjuryState,
            WeaponId,
            MagazineAmmo,
            ReserveAmmo,
            IsPlayerSquadMember);
    }
}

/// <summary>셸터 또는 이전 씬에서 배틀 씬으로 전달하는 입장 스냅샷입니다.</summary>
[Serializable]
public sealed class BattleEntryData
{
    [SerializeField] private string battleId = string.Empty;
    [SerializeField] private string stageId = string.Empty;
    [SerializeField] private int randomSeed;
    [SerializeField] private long startedAtUnixMilliseconds;
    [SerializeField] private List<BattleMemberEntryData> members = new();
    [SerializeField] private List<BattleResourceAmountData> startingResources = new();

    /// <summary>출격 한 회를 구분하는 고유 ID입니다.</summary>
    public string BattleId => battleId ?? string.Empty;

    /// <summary>배틀이 진행되는 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>배틀의 결정적 랜덤 처리에 사용할 시드입니다.</summary>
    public int RandomSeed => randomSeed;

    /// <summary>출격 데이터를 만든 UTC 시각의 Unix 밀리초 값입니다.</summary>
    public long StartedAtUnixMilliseconds => Math.Max(0L, startedAtUnixMilliseconds);

    /// <summary>스쿼드 순서대로 확정된 멤버 입장 데이터입니다.</summary>
    public IReadOnlyList<BattleMemberEntryData> Members => members;

    /// <summary>배틀 입장 시점에 보유한 공용 자원 스냅샷입니다.</summary>
    public IReadOnlyList<BattleResourceAmountData> StartingResources => startingResources;

    /// <summary>배틀 식별 정보와 생성 시각으로 빈 입장 스냅샷을 만듭니다.</summary>
    public BattleEntryData(string battleId, string stageId, int randomSeed, long startedAtUnixMilliseconds)
    {
        this.battleId = battleId?.Trim() ?? string.Empty;
        this.stageId = stageId?.Trim() ?? string.Empty;
        this.randomSeed = randomSeed;
        this.startedAtUnixMilliseconds = Math.Max(0L, startedAtUnixMilliseconds);
    }

    /// <summary>입장 스냅샷의 마지막에 스쿼드원을 추가합니다.</summary>
    public void AddMember(BattleMemberEntryData member)
    {
        if (member != null)
        {
            members.Add(member.Clone());
        }
    }

    /// <summary>스쿼드 순서를 유지한 채 지정 위치의 멤버 초기값을 교체합니다.</summary>
    public void SetMemberAt(int index, BattleMemberEntryData member)
    {
        if (index < 0 || member == null)
        {
            return;
        }

        if (index < members.Count)
        {
            members[index] = member.Clone();
            return;
        }

        if (index == members.Count)
        {
            members.Add(member.Clone());
        }
    }

    /// <summary>입장 시점의 공용 자원 수량을 종류별로 누적합니다.</summary>
    public void AddStartingResource(CurrencyType type, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        BattleResourceAmountData existing = startingResources.Find(entry => entry.Type == type);
        if (existing != null)
        {
            existing.Add(amount);
            return;
        }

        startingResources.Add(new BattleResourceAmountData(type, amount));
    }

    /// <summary>입장 스냅샷 전체를 깊은 복사하여 반환합니다.</summary>
    public BattleEntryData Clone()
    {
        BattleEntryData clone = new BattleEntryData(BattleId, StageId, RandomSeed, StartedAtUnixMilliseconds);
        for (int i = 0; i < members.Count; i++)
        {
            clone.AddMember(members[i]);
        }

        for (int i = 0; i < startingResources.Count; i++)
        {
            BattleResourceAmountData resource = startingResources[i];
            if (resource != null)
            {
                clone.AddStartingResource(resource.Type, resource.Amount);
            }
        }

        return clone;
    }
}

/// <summary>배틀 진행 중 계속 갱신되는 스쿼드원 한 명의 상태입니다.</summary>
[Serializable]
public sealed class BattleMemberRuntimeData
{
    [SerializeField] private BattleMemberEntryData entryData;
    [Min(0)][SerializeField] private int currentHp;
    [Min(1)][SerializeField] private int maxHp = 1;
    [Min(0.0f)][SerializeField] private float injuryGauge;
    [Min(1.0f)][SerializeField] private float maxInjuryGauge = 100.0f;
    [SerializeField] private PlayerInjuryState injuryState;
    [SerializeField] private bool isDown;
    [SerializeField] private bool isCombatOut;
    [SerializeField] private string weaponId = string.Empty;
    [Min(0)][SerializeField] private int magazineAmmo;
    [Min(0)][SerializeField] private int magazineCapacity;
    [Min(0)][SerializeField] private int reserveAmmo;
    [Min(0)][SerializeField] private int maxReserveAmmo;
    [SerializeField] private bool isPlayerSquadMember;
    [Min(0)][SerializeField] private int killCount;

    /// <summary>이 런타임 상태의 기준이 된 입장 데이터입니다.</summary>
    public BattleMemberEntryData EntryData => entryData;

    /// <summary>현재 씬 인스턴스를 식별하는 런타임 ID입니다.</summary>
    public string RuntimeId => entryData?.RuntimeId ?? string.Empty;

    /// <summary>플레이어블 캐릭터의 고정 ID입니다.</summary>
    public PlayableCharacterId CharacterId => entryData?.CharacterId ?? PlayableCharacterId.Unknown;

    /// <summary>현재 HP입니다.</summary>
    public int CurrentHp => Mathf.Clamp(currentHp, 0, MaxHp);

    /// <summary>현재 적용 중인 최대 HP입니다.</summary>
    public int MaxHp => Mathf.Max(1, maxHp);

    /// <summary>현재 누적 부상 게이지입니다.</summary>
    public float InjuryGauge => Mathf.Clamp(injuryGauge, 0.0f, MaxInjuryGauge);

    /// <summary>현재 적용 중인 부상 게이지 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, maxInjuryGauge);

    /// <summary>현재 부상 단계입니다.</summary>
    public PlayerInjuryState InjuryState => injuryState;

    /// <summary>현재 다운 상태인지 여부입니다.</summary>
    public bool IsDown => isDown;

    /// <summary>현재 전투 이탈 상태인지 여부입니다.</summary>
    public bool IsCombatOut => isCombatOut;

    /// <summary>현재 장착한 무기의 정의 ID입니다.</summary>
    public string WeaponId => weaponId ?? string.Empty;

    /// <summary>현재 탄창에 남은 탄약 수입니다.</summary>
    public int MagazineAmmo => Mathf.Max(0, magazineAmmo);

    /// <summary>현재 무기의 탄창 최대 용량입니다.</summary>
    public int MagazineCapacity => Mathf.Max(0, magazineCapacity);

    /// <summary>현재 보유 중인 예비 탄약 수입니다.</summary>
    public int ReserveAmmo => Mathf.Max(0, reserveAmmo);

    /// <summary>현재 보유할 수 있는 예비 탄약 최대치입니다.</summary>
    public int MaxReserveAmmo => Mathf.Max(0, maxReserveAmmo);

    /// <summary>현재 직접 조작 중인 PlayerSquadMember인지 여부입니다.</summary>
    public bool IsPlayerSquadMember => isPlayerSquadMember;

    /// <summary>이 멤버가 확정한 적 처치 수입니다.</summary>
    public int KillCount => Mathf.Max(0, killCount);

    /// <summary>입장 데이터를 기준으로 멤버 런타임 상태를 생성합니다.</summary>
    public BattleMemberRuntimeData(BattleMemberEntryData source)
    {
        entryData = source?.Clone();
        currentHp = source?.CurrentHp ?? 0;
        maxHp = source?.MaxHp ?? 1;
        injuryGauge = source?.InjuryGauge ?? 0.0f;
        maxInjuryGauge = source?.MaxInjuryGauge ?? 100.0f;
        injuryState = source?.InjuryState ?? PlayerInjuryState.Normal;
        weaponId = source?.WeaponId ?? string.Empty;
        magazineAmmo = source?.MagazineAmmo ?? 0;
        reserveAmmo = source?.ReserveAmmo ?? 0;
        isPlayerSquadMember = source?.IsPlayerSquadMember ?? false;
    }

    /// <summary>씬에서 관찰한 생존 및 부상 상태를 반영합니다.</summary>
    public void SetSceneState(
        int hp,
        int hpMaximum,
        float gauge,
        float gaugeMaximum,
        PlayerInjuryState state,
        bool down,
        bool combatOut,
        string currentWeaponId,
        int currentMagazineAmmo,
        int currentMagazineCapacity,
        int currentReserveAmmo,
        int currentMaxReserveAmmo,
        bool playerSquadMember)
    {
        maxHp = Mathf.Max(1, hpMaximum);
        currentHp = Mathf.Clamp(hp, 0, maxHp);
        maxInjuryGauge = Mathf.Max(1.0f, gaugeMaximum);
        injuryGauge = Mathf.Clamp(gauge, 0.0f, maxInjuryGauge);
        injuryState = state;
        isDown = down;
        isCombatOut = combatOut;
        weaponId = currentWeaponId?.Trim() ?? string.Empty;
        magazineCapacity = Mathf.Max(0, currentMagazineCapacity);
        magazineAmmo = Mathf.Clamp(currentMagazineAmmo, 0, magazineCapacity);
        maxReserveAmmo = Mathf.Max(0, currentMaxReserveAmmo);
        reserveAmmo = Mathf.Clamp(currentReserveAmmo, 0, maxReserveAmmo);
        isPlayerSquadMember = playerSquadMember;
    }

    /// <summary>이 멤버의 적 처치 수를 1 증가시킵니다.</summary>
    public void RecordKill()
    {
        killCount++;
    }

    /// <summary>멤버 런타임 상태 전체를 깊은 복사하여 반환합니다.</summary>
    public BattleMemberRuntimeData Clone()
    {
        BattleMemberRuntimeData clone = new BattleMemberRuntimeData(entryData)
        {
            currentHp = CurrentHp,
            maxHp = MaxHp,
            injuryGauge = InjuryGauge,
            maxInjuryGauge = MaxInjuryGauge,
            injuryState = InjuryState,
            isDown = IsDown,
            isCombatOut = IsCombatOut,
            weaponId = WeaponId,
            magazineAmmo = MagazineAmmo,
            magazineCapacity = MagazineCapacity,
            reserveAmmo = ReserveAmmo,
            maxReserveAmmo = MaxReserveAmmo,
            isPlayerSquadMember = IsPlayerSquadMember,
            killCount = KillCount
        };
        return clone;
    }
}

/// <summary>배틀 씬이 소유하며 진행 중 계속 변경하는 전체 런타임 데이터입니다.</summary>
[Serializable]
public sealed class BattleRuntimeData
{
    [SerializeField] private BattlePhase phase;
    [SerializeField] private string battleId = string.Empty;
    [SerializeField] private string stageId = string.Empty;
    [Min(0.0f)][SerializeField] private float elapsedSeconds;
    [SerializeField] private bool missionCompleted;
    [Min(0)][SerializeField] private int totalEnemyCount;
    [Min(0)][SerializeField] private int aliveEnemyCount;
    [Min(0)][SerializeField] private int totalKillCount;
    [SerializeField] private List<BattleMemberRuntimeData> members = new();
    [SerializeField] private List<BattleResourceAmountData> acquiredResources = new();

    /// <summary>현재 배틀 생명주기 단계입니다.</summary>
    public BattlePhase Phase => phase;

    /// <summary>출격 한 회를 구분하는 고유 ID입니다.</summary>
    public string BattleId => battleId ?? string.Empty;

    /// <summary>현재 배틀의 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>배틀 진행 상태로 누적한 경과 시간입니다.</summary>
    public float ElapsedSeconds => Mathf.Max(0.0f, elapsedSeconds);

    /// <summary>현재 임무 목표를 달성했는지 여부입니다.</summary>
    public bool MissionCompleted => missionCompleted;

    /// <summary>배틀 시작 시점에 집계한 전체 적 수입니다.</summary>
    public int TotalEnemyCount => Mathf.Max(0, totalEnemyCount);

    /// <summary>현재 살아 있는 것으로 집계된 적 수입니다.</summary>
    public int AliveEnemyCount => Mathf.Max(0, aliveEnemyCount);

    /// <summary>스쿼드 전체의 적 처치 수입니다.</summary>
    public int TotalKillCount => Mathf.Max(0, totalKillCount);

    /// <summary>스쿼드원별 현재 런타임 상태입니다.</summary>
    public IReadOnlyList<BattleMemberRuntimeData> Members => members;

    /// <summary>이번 배틀에서 획득한 자원 수량입니다.</summary>
    public IReadOnlyList<BattleResourceAmountData> AcquiredResources => acquiredResources;

    /// <summary>입장 데이터와 시작 적 수로 런타임 상태를 초기화합니다.</summary>
    public void Initialize(BattleEntryData entryData, int enemyCount)
    {
        phase = BattlePhase.Ready;
        battleId = entryData?.BattleId ?? string.Empty;
        stageId = entryData?.StageId ?? string.Empty;
        elapsedSeconds = 0.0f;
        missionCompleted = false;
        totalEnemyCount = Mathf.Max(0, enemyCount);
        aliveEnemyCount = totalEnemyCount;
        totalKillCount = 0;
        members.Clear();
        acquiredResources.Clear();

        if (entryData != null)
        {
            for (int i = 0; i < entryData.Members.Count; i++)
            {
                members.Add(new BattleMemberRuntimeData(entryData.Members[i]));
            }
        }
    }

    /// <summary>준비된 배틀을 진행 상태로 전환합니다.</summary>
    public void StartBattle()
    {
        if (phase == BattlePhase.Ready)
        {
            phase = BattlePhase.Running;
        }
    }

    /// <summary>진행 중인 배틀의 값 변경을 멈추고 결과 확정 단계로 전환합니다.</summary>
    public bool BeginFinalization()
    {
        if (phase == BattlePhase.Uninitialized || phase == BattlePhase.Finalizing || phase == BattlePhase.Completed)
        {
            return false;
        }

        phase = BattlePhase.Finalizing;
        return true;
    }

    /// <summary>결과 스냅샷 생성이 끝난 배틀을 완료 상태로 전환합니다.</summary>
    public void CompleteFinalization()
    {
        if (phase == BattlePhase.Finalizing)
        {
            phase = BattlePhase.Completed;
        }
    }

    /// <summary>배틀 진행 중일 때만 경과 시간을 누적합니다.</summary>
    public void AddElapsedTime(float deltaSeconds)
    {
        if (phase == BattlePhase.Running && deltaSeconds > 0.0f)
        {
            elapsedSeconds += deltaSeconds;
        }
    }

    /// <summary>임무 달성 여부를 현재 런타임 상태에 기록합니다.</summary>
    public void SetMissionCompleted(bool value)
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        missionCompleted = value;
    }

    /// <summary>전체 적 처치 수를 증가시키고 생존 적 수를 감소시킵니다.</summary>
    public void RecordEnemyKill()
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        totalKillCount++;
        aliveEnemyCount = Mathf.Max(0, aliveEnemyCount - 1);
    }

    /// <summary>런타임 ID 또는 캐릭터 ID가 일치하는 멤버의 처치 수를 증가시킵니다.</summary>
    public void RecordMemberKill(string runtimeId, PlayableCharacterId characterId)
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        BattleMemberRuntimeData member = FindMember(runtimeId, characterId);
        member?.RecordKill();
    }

    /// <summary>런타임 ID 또는 캐릭터 ID가 일치하는 멤버의 현재 씬 상태를 갱신합니다.</summary>
    public void UpdateMemberState(
        string runtimeId,
        PlayableCharacterId characterId,
        int currentHp,
        int maxHp,
        float injuryGauge,
        float maxInjuryGauge,
        PlayerInjuryState injuryState,
        bool isDown,
        bool isCombatOut,
        string weaponId,
        int magazineAmmo,
        int magazineCapacity,
        int reserveAmmo,
        int maxReserveAmmo,
        bool isPlayerSquadMember)
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        BattleMemberRuntimeData member = FindMember(runtimeId, characterId);
        member?.SetSceneState(
            currentHp,
            maxHp,
            injuryGauge,
            maxInjuryGauge,
            injuryState,
            isDown,
            isCombatOut,
            weaponId,
            magazineAmmo,
            magazineCapacity,
            reserveAmmo,
            maxReserveAmmo,
            isPlayerSquadMember);
    }

    /// <summary>이번 배틀에서 획득한 자원 수량을 종류별로 누적합니다.</summary>
    public void RecordResource(CurrencyType type, int amount)
    {
        if (!CanAcceptRuntimeChanges() || amount <= 0)
        {
            return;
        }

        BattleResourceAmountData existing = acquiredResources.Find(entry => entry.Type == type);
        if (existing != null)
        {
            existing.Add(amount);
            return;
        }

        acquiredResources.Add(new BattleResourceAmountData(type, amount));
    }

    /// <summary>전체 배틀 런타임 상태를 깊은 복사하여 반환합니다.</summary>
    public BattleRuntimeData Clone()
    {
        BattleRuntimeData clone = new BattleRuntimeData
        {
            phase = Phase,
            battleId = BattleId,
            stageId = StageId,
            elapsedSeconds = ElapsedSeconds,
            missionCompleted = MissionCompleted,
            totalEnemyCount = TotalEnemyCount,
            aliveEnemyCount = AliveEnemyCount,
            totalKillCount = TotalKillCount
        };

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                clone.members.Add(members[i].Clone());
            }
        }

        for (int i = 0; i < acquiredResources.Count; i++)
        {
            if (acquiredResources[i] != null)
            {
                clone.acquiredResources.Add(acquiredResources[i].Clone());
            }
        }

        return clone;
    }

    /// <summary>현재 단계에서 진행 중 데이터 변경을 허용하는지 확인합니다.</summary>
    private bool CanAcceptRuntimeChanges()
    {
        return phase == BattlePhase.Ready || phase == BattlePhase.Running;
    }

    /// <summary>런타임 ID를 우선 사용하고 캐릭터 ID를 보조로 사용해 런타임 멤버를 찾습니다.</summary>
    private BattleMemberRuntimeData FindMember(string runtimeId, PlayableCharacterId characterId)
    {
        if (!string.IsNullOrWhiteSpace(runtimeId))
        {
            BattleMemberRuntimeData byRuntimeId = members.Find(member => member != null && member.RuntimeId == runtimeId);
            if (byRuntimeId != null)
            {
                return byRuntimeId;
            }
        }

        return characterId != PlayableCharacterId.Unknown
            ? members.Find(member => member != null && member.CharacterId == characterId)
            : null;
    }
}

/// <summary>귀환 정산에 저장하는 스쿼드원 한 명의 최종 결과입니다.</summary>
[Serializable]
public sealed class BattleMemberResultData
{
    [SerializeField] private BattleMemberRuntimeData runtimeData;

    /// <summary>외부 변경을 막기 위해 복제한 멤버 최종 상태를 반환합니다.</summary>
    public BattleMemberRuntimeData RuntimeData => runtimeData?.Clone();

    /// <summary>멤버 런타임 상태를 복제해 최종 결과로 고정합니다.</summary>
    public BattleMemberResultData(BattleMemberRuntimeData runtimeData)
    {
        this.runtimeData = runtimeData?.Clone();
    }

    /// <summary>멤버 최종 결과를 깊은 복사하여 반환합니다.</summary>
    public BattleMemberResultData Clone()
    {
        return new BattleMemberResultData(runtimeData);
    }
}

/// <summary>배틀 종료 후 GameDataManager와 저장 시스템에 전달할 최종 결과 스냅샷입니다.</summary>
[Serializable]
public sealed class BattleResultData
{
    [SerializeField] private string battleId = string.Empty;
    [SerializeField] private string stageId = string.Empty;
    [SerializeField] private BattleOutcome outcome;
    [SerializeField] private BattleEndReason endReason;
    [SerializeField] private bool missionCompleted;
    [Min(0.0f)][SerializeField] private float elapsedSeconds;
    [Min(0)][SerializeField] private int totalKillCount;
    [SerializeField] private List<BattleMemberResultData> members = new();
    [SerializeField] private List<BattleResourceAmountData> acquiredResources = new();

    /// <summary>종료된 출격 한 회의 고유 ID입니다.</summary>
    public string BattleId => battleId ?? string.Empty;

    /// <summary>종료된 배틀의 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>귀환 정산에 표시할 최종 성공·실패 결과입니다.</summary>
    public BattleOutcome Outcome => outcome;

    /// <summary>배틀이 종료된 직접적인 사유입니다.</summary>
    public BattleEndReason EndReason => endReason;

    /// <summary>배틀 종료 전에 임무 목표를 달성했는지 여부입니다.</summary>
    public bool MissionCompleted => missionCompleted;

    /// <summary>배틀 종료까지 누적된 경과 시간입니다.</summary>
    public float ElapsedSeconds => Mathf.Max(0.0f, elapsedSeconds);

    /// <summary>배틀 전체에서 확정된 적 처치 수입니다.</summary>
    public int TotalKillCount => Mathf.Max(0, totalKillCount);

    /// <summary>스쿼드원별 최종 결과입니다.</summary>
    public IReadOnlyList<BattleMemberResultData> Members => members;

    /// <summary>배틀에서 최종 획득한 자원 수량입니다.</summary>
    public IReadOnlyList<BattleResourceAmountData> AcquiredResources => acquiredResources;

    /// <summary>현재 런타임 상태와 종료 판정을 복제해 최종 결과를 생성합니다.</summary>
    public BattleResultData(BattleRuntimeData runtimeData, BattleOutcome outcome, BattleEndReason endReason)
    {
        battleId = runtimeData?.BattleId ?? string.Empty;
        stageId = runtimeData?.StageId ?? string.Empty;
        this.outcome = outcome;
        this.endReason = endReason;
        missionCompleted = runtimeData?.MissionCompleted ?? false;
        elapsedSeconds = runtimeData?.ElapsedSeconds ?? 0.0f;
        totalKillCount = runtimeData?.TotalKillCount ?? 0;

        if (runtimeData == null)
        {
            return;
        }

        for (int i = 0; i < runtimeData.Members.Count; i++)
        {
            members.Add(new BattleMemberResultData(runtimeData.Members[i]));
        }

        for (int i = 0; i < runtimeData.AcquiredResources.Count; i++)
        {
            acquiredResources.Add(runtimeData.AcquiredResources[i].Clone());
        }
    }

    /// <summary>최종 결과 전체를 깊은 복사하여 반환합니다.</summary>
    public BattleResultData Clone()
    {
        BattleResultData clone = new BattleResultData
        {
            battleId = BattleId,
            stageId = StageId,
            outcome = Outcome,
            endReason = EndReason,
            missionCompleted = MissionCompleted,
            elapsedSeconds = ElapsedSeconds,
            totalKillCount = TotalKillCount
        };

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                clone.members.Add(members[i].Clone());
            }
        }

        for (int i = 0; i < acquiredResources.Count; i++)
        {
            if (acquiredResources[i] != null)
            {
                clone.acquiredResources.Add(acquiredResources[i].Clone());
            }
        }

        return clone;
    }

    private BattleResultData()
    {
    }
}
