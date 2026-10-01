using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>방어전 한 판의 진행 단계입니다.</summary>
public enum DefensePhase
{
    /// <summary>입장 데이터가 아직 적용되지 않은 상태입니다.</summary>
    Uninitialized,

    /// <summary>입장 데이터와 웨이브 설정이 준비되어 시작 입력을 기다리는 상태입니다.</summary>
    Ready,

    /// <summary>웨이브 전투 구간입니다.</summary>
    Combat,

    /// <summary>웨이브 사이 휴식 구간입니다.</summary>
    Rest,

    /// <summary>마지막 웨이브를 막아 결과가 승리로 확정된 상태입니다.</summary>
    Victory,

    /// <summary>거점 파괴나 스쿼드 전멸로 결과가 패배로 확정된 상태입니다.</summary>
    Defeat
}

/// <summary>방어전의 최종 결과입니다.</summary>
public enum DefenseOutcome
{
    /// <summary>아직 결과가 확정되지 않았습니다.</summary>
    None,

    /// <summary>마지막 웨이브까지 막았습니다.</summary>
    Victory,

    /// <summary>마지막 웨이브 전에 패배했습니다.</summary>
    Defeat
}

/// <summary>
/// Scramble 시설에서 올린 필드 배치 업그레이드 5종의 레벨입니다.
/// </summary>
/// <remarks>
/// 값의 정본은 <see cref="GameDataManager"/>이고, 이 타입은 방어전 한 판 동안 쓰는 복사본입니다.
/// </remarks>
[Serializable]
public sealed class DefenseUpgradeLevels
{
    [SerializeField] private int trap;
    [SerializeField] private int spike;
    [SerializeField] private int explosive;
    [SerializeField] private int shooter;
    [SerializeField] private int wire;

    /// <summary>Trap 업그레이드 레벨입니다.</summary>
    public int Trap => trap;

    /// <summary>Spike 업그레이드 레벨입니다.</summary>
    public int Spike => spike;

    /// <summary>Explosive 업그레이드 레벨입니다.</summary>
    public int Explosive => explosive;

    /// <summary>Shooter 업그레이드 레벨입니다.</summary>
    public int Shooter => shooter;

    /// <summary>Wire 업그레이드 레벨입니다.</summary>
    public int Wire => wire;

    /// <summary>업그레이드 5종의 레벨로 만듭니다. 음수는 0으로 맞춥니다.</summary>
    public DefenseUpgradeLevels(int trap, int spike, int explosive, int shooter, int wire)
    {
        this.trap = Mathf.Max(0, trap);
        this.spike = Mathf.Max(0, spike);
        this.explosive = Mathf.Max(0, explosive);
        this.shooter = Mathf.Max(0, shooter);
        this.wire = Mathf.Max(0, wire);
    }

    /// <summary>모든 업그레이드가 0레벨인 값입니다.</summary>
    public static DefenseUpgradeLevels None => new DefenseUpgradeLevels(0, 0, 0, 0, 0);

    /// <summary>GameDataManager에 저장된 현재 업그레이드 레벨을 복사합니다.</summary>
    public static DefenseUpgradeLevels FromGameData(GameDataManager gameData)
    {
        if (gameData == null)
        {
            return None;
        }

        return new DefenseUpgradeLevels(
            gameData.TrapUpgradeLevel,
            gameData.SpikeUpgradeLevel,
            gameData.ExplosiveUpgradeLevel,
            gameData.ShooterUpgradeLevel,
            gameData.WireUpgradeLevel);
    }

    /// <summary>지정한 업그레이드 종류의 레벨을 반환합니다.</summary>
    public int GetLevel(ScrambleUpgradeType type)
    {
        return type switch
        {
            ScrambleUpgradeType.Trap => trap,
            ScrambleUpgradeType.Spike => spike,
            ScrambleUpgradeType.Explosive => explosive,
            ScrambleUpgradeType.Shooter => shooter,
            ScrambleUpgradeType.Wire => wire,
            _ => 0
        };
    }

    /// <summary>외부 변경과 분리된 복사본을 만듭니다.</summary>
    public DefenseUpgradeLevels Clone()
    {
        return new DefenseUpgradeLevels(trap, spike, explosive, shooter, wire);
    }
}

/// <summary>
/// 방어전 씬에 들어올 때 외부에서 받는 방어전 전용 입장 데이터입니다.
/// </summary>
/// <remarks>
/// 스쿼드·캐릭터·자원은 <see cref="FieldEntryData"/>가 이미 담고 있으므로 여기에 두지 않습니다.
/// 이 패킷은 셸터의 Scramble 시설에서 결정되는 값만 가집니다.
/// </remarks>
[Serializable]
public sealed class DefenseEntryData
{
    [SerializeField] private string stageId = string.Empty;
    [SerializeField] private DefenseUpgradeLevels upgrades = DefenseUpgradeLevels.None;
    [SerializeField] private int round = 1;
    [SerializeField] private DefenseStageSO stage;

    /// <summary>방어전이 진행되는 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>Scramble 시설에서 올린 업그레이드 레벨입니다.</summary>
    public DefenseUpgradeLevels Upgrades => upgrades ??= DefenseUpgradeLevels.None;

    /// <summary>이번 방어전 회차입니다. 1부터 시작합니다.</summary>
    public int Round => Mathf.Max(1, round);

    /// <summary>이번 방어전 구성입니다. 입장할 때 회차 표에서 정해지며, 없으면 null입니다.</summary>
    public DefenseStageSO Stage => stage;

    /// <summary>스테이지 ID와 업그레이드 레벨로 입장 데이터를 만듭니다.</summary>
    /// <param name="stageId">방어전이 진행되는 스테이지 ID입니다.</param>
    /// <param name="upgrades">Scramble 시설에서 올린 업그레이드 레벨입니다.</param>
    /// <param name="round">이번 방어전 회차입니다. 1보다 작으면 1로 봅니다.</param>
    /// <param name="stage">이번 방어전 구성입니다.</param>
    public DefenseEntryData(string stageId, DefenseUpgradeLevels upgrades, int round = 1, DefenseStageSO stage = null)
    {
        this.stageId = stageId?.Trim() ?? string.Empty;
        this.upgrades = upgrades?.Clone() ?? DefenseUpgradeLevels.None;
        this.round = Mathf.Max(1, round);
        this.stage = stage;
    }

    /// <summary>외부 변경과 분리된 복사본을 만듭니다.</summary>
    /// <remarks>방어전 SO는 공유 에셋이라 참조만 복사합니다.</remarks>
    public DefenseEntryData Clone()
    {
        return new DefenseEntryData(stageId, upgrades, round, stage);
    }
}

/// <summary>웨이브 하나의 진행 기록입니다.</summary>
[Serializable]
public sealed class DefenseWaveRecord
{
    [SerializeField] private int waveNumber;
    [SerializeField] private int killCount;
    [SerializeField] private float elapsedTime;
    [SerializeField] private bool cleared;

    /// <summary>1부터 시작하는 웨이브 번호입니다.</summary>
    public int WaveNumber => waveNumber;

    /// <summary>이 웨이브 전투 구간에서 처치한 적 수입니다.</summary>
    public int KillCount => killCount;

    /// <summary>이 웨이브 전투 구간에 머문 시간(초)입니다.</summary>
    public float ElapsedTime => elapsedTime;

    /// <summary>이 웨이브를 끝까지 막았는지 여부입니다.</summary>
    public bool Cleared => cleared;

    /// <summary>시작한 웨이브 번호로 빈 기록을 만듭니다.</summary>
    public DefenseWaveRecord(int waveNumber)
    {
        this.waveNumber = Mathf.Max(1, waveNumber);
    }

    /// <summary>웨이브가 끝난 시점의 처치 수와 경과 시간을 기록합니다.</summary>
    public void Close(int killCount, float elapsedTime, bool cleared)
    {
        this.killCount = Mathf.Max(0, killCount);
        this.elapsedTime = Mathf.Max(0.0f, elapsedTime);
        this.cleared = cleared;
    }

    /// <summary>외부 변경과 분리된 복사본을 만듭니다.</summary>
    public DefenseWaveRecord Clone()
    {
        DefenseWaveRecord copy = new DefenseWaveRecord(waveNumber);
        copy.Close(killCount, elapsedTime, cleared);
        return copy;
    }
}

/// <summary>
/// 방어전이 진행되는 동안 <see cref="DefenseManager"/>가 갱신하는 방어전 전용 런타임 데이터입니다.
/// </summary>
[Serializable]
public sealed class DefenseRuntimeData
{
    [SerializeField] private DefensePhase phase = DefensePhase.Uninitialized;
    [SerializeField] private int totalWaveCount;
    [SerializeField] private int currentWave;
    [SerializeField] private float elapsedTime;
    [SerializeField] private List<DefenseWaveRecord> waves = new();

    // 진행 중인 웨이브의 시작 기준값입니다. 웨이브별 처치 수와 시간은 종료 시점과의 차이로 구합니다.
    [SerializeField] private int waveStartKillCount;
    [SerializeField] private float waveStartElapsedTime;

    /// <summary>현재 진행 단계입니다.</summary>
    public DefensePhase Phase => phase;

    /// <summary>이 방어전의 총 웨이브 수입니다.</summary>
    public int TotalWaveCount => totalWaveCount;

    /// <summary>진행 중이거나 마지막으로 시작한 웨이브 번호입니다. 시작 전에는 0입니다.</summary>
    public int CurrentWave => currentWave;

    /// <summary>방어전 시작 뒤 흐른 시간(초)입니다.</summary>
    public float ElapsedTime => elapsedTime;

    /// <summary>시작한 웨이브 순서대로 쌓인 기록입니다.</summary>
    public IReadOnlyList<DefenseWaveRecord> Waves => waves;

    /// <summary>끝까지 막은 웨이브 수입니다.</summary>
    public int ClearedWaveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < waves.Count; i++)
            {
                if (waves[i] != null && waves[i].Cleared)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>결과가 승리 또는 패배로 확정되었는지 여부입니다.</summary>
    public bool IsFinished => phase == DefensePhase.Victory || phase == DefensePhase.Defeat;

    /// <summary>방어전 시작 뒤 결과 확정 전까지 시간이 흐르는 상태인지 여부입니다.</summary>
    public bool IsRunning => phase == DefensePhase.Combat || phase == DefensePhase.Rest;

    /// <summary>웨이브 설정을 받아 시작 대기 상태로 초기화합니다.</summary>
    public void Initialize(int totalWaveCount)
    {
        this.totalWaveCount = Mathf.Max(1, totalWaveCount);
        currentWave = 0;
        elapsedTime = 0.0f;
        waves.Clear();
        waveStartKillCount = 0;
        waveStartElapsedTime = 0.0f;
        phase = DefensePhase.Ready;
    }

    /// <summary>진행 중일 때만 경과 시간을 누적합니다.</summary>
    public void AddElapsedTime(float deltaTime)
    {
        if (IsRunning && deltaTime > 0.0f)
        {
            elapsedTime += deltaTime;
        }
    }

    /// <summary>새 웨이브 전투 구간을 시작합니다.</summary>
    public bool BeginWave(int waveNumber, int currentKillCount)
    {
        if (phase == DefensePhase.Uninitialized || IsFinished)
        {
            return false;
        }

        currentWave = Mathf.Clamp(waveNumber, 1, totalWaveCount);
        waveStartKillCount = Mathf.Max(0, currentKillCount);
        waveStartElapsedTime = elapsedTime;
        waves.Add(new DefenseWaveRecord(currentWave));
        phase = DefensePhase.Combat;
        return true;
    }

    /// <summary>진행 중인 웨이브를 막아 낸 것으로 닫고 휴식 구간으로 넘어갑니다.</summary>
    public bool BeginRest(int currentKillCount)
    {
        if (phase != DefensePhase.Combat)
        {
            return false;
        }

        CloseCurrentWave(currentKillCount, true);
        phase = DefensePhase.Rest;
        return true;
    }

    /// <summary>마지막 웨이브를 막아 결과를 승리로 확정합니다.</summary>
    public bool CompleteVictory(int currentKillCount)
    {
        if (phase != DefensePhase.Combat)
        {
            return false;
        }

        CloseCurrentWave(currentKillCount, true);
        phase = DefensePhase.Victory;
        return true;
    }

    /// <summary>진행 중인 웨이브를 막지 못한 것으로 닫고 결과를 패배로 확정합니다.</summary>
    public bool CompleteDefeat(int currentKillCount)
    {
        if (phase == DefensePhase.Uninitialized || IsFinished)
        {
            return false;
        }

        if (phase == DefensePhase.Combat)
        {
            CloseCurrentWave(currentKillCount, false);
        }

        phase = DefensePhase.Defeat;
        return true;
    }

    /// <summary>외부 변경과 분리된 복사본을 만듭니다.</summary>
    public DefenseRuntimeData Clone()
    {
        DefenseRuntimeData copy = new DefenseRuntimeData
        {
            phase = phase,
            totalWaveCount = totalWaveCount,
            currentWave = currentWave,
            elapsedTime = elapsedTime,
            waveStartKillCount = waveStartKillCount,
            waveStartElapsedTime = waveStartElapsedTime
        };

        for (int i = 0; i < waves.Count; i++)
        {
            if (waves[i] != null)
            {
                copy.waves.Add(waves[i].Clone());
            }
        }

        return copy;
    }

    private void CloseCurrentWave(int currentKillCount, bool cleared)
    {
        if (waves.Count == 0)
        {
            return;
        }

        waves[waves.Count - 1].Close(
            currentKillCount - waveStartKillCount,
            elapsedTime - waveStartElapsedTime,
            cleared);
    }
}

/// <summary>
/// 방어전 결과가 확정된 순간의 방어전 전용 값을 고정한 스냅샷입니다.
/// </summary>
/// <remarks>
/// 캐릭터 부상·처치·획득 자원 같은 공통 정산은 <see cref="FieldResultData"/>가 담당합니다.
/// </remarks>
[Serializable]
public sealed class DefenseResultData
{
    [SerializeField] private string stageId = string.Empty;
    [SerializeField] private DefenseOutcome outcome;
    [SerializeField] private int totalWaveCount;
    [SerializeField] private int clearedWaveCount;
    [SerializeField] private float elapsedTime;
    [SerializeField] private DefenseUpgradeLevels upgrades = DefenseUpgradeLevels.None;
    [SerializeField] private List<DefenseWaveRecord> waves = new();

    /// <summary>방어전이 진행된 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>방어전 최종 결과입니다.</summary>
    public DefenseOutcome Outcome => outcome;

    /// <summary>이 방어전의 총 웨이브 수입니다.</summary>
    public int TotalWaveCount => totalWaveCount;

    /// <summary>끝까지 막은 웨이브 수입니다.</summary>
    public int ClearedWaveCount => clearedWaveCount;

    /// <summary>방어전 시작부터 결과 확정까지 걸린 시간(초)입니다.</summary>
    public float ElapsedTime => elapsedTime;

    /// <summary>이번 방어전에 적용된 Scramble 업그레이드 레벨입니다.</summary>
    public DefenseUpgradeLevels Upgrades => upgrades ??= DefenseUpgradeLevels.None;

    /// <summary>웨이브별 기록입니다.</summary>
    public IReadOnlyList<DefenseWaveRecord> Waves => waves;

    /// <summary>입장 데이터와 확정된 런타임 데이터로 결과 스냅샷을 만듭니다.</summary>
    public DefenseResultData(DefenseEntryData entry, DefenseRuntimeData runtime, DefenseOutcome outcome)
    {
        stageId = entry?.StageId ?? string.Empty;
        upgrades = entry?.Upgrades.Clone() ?? DefenseUpgradeLevels.None;
        this.outcome = outcome;

        if (runtime == null)
        {
            return;
        }

        totalWaveCount = runtime.TotalWaveCount;
        clearedWaveCount = runtime.ClearedWaveCount;
        elapsedTime = runtime.ElapsedTime;
        for (int i = 0; i < runtime.Waves.Count; i++)
        {
            if (runtime.Waves[i] != null)
            {
                waves.Add(runtime.Waves[i].Clone());
            }
        }
    }

    private DefenseResultData()
    {
    }

    /// <summary>외부 변경과 분리된 복사본을 만듭니다.</summary>
    public DefenseResultData Clone()
    {
        DefenseResultData copy = new DefenseResultData
        {
            stageId = stageId,
            outcome = outcome,
            totalWaveCount = totalWaveCount,
            clearedWaveCount = clearedWaveCount,
            elapsedTime = elapsedTime,
            upgrades = Upgrades.Clone()
        };

        for (int i = 0; i < waves.Count; i++)
        {
            if (waves[i] != null)
            {
                copy.waves.Add(waves[i].Clone());
            }
        }

        return copy;
    }
}
