using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VInspector;

/// <summary>
/// Defense 라운드의 전투/정리/휴식 진행과 스폰 포인트 제어를 관리합니다.
/// </summary>
/// <remarks>
/// 라운드 진행 규칙과 UI가 읽을 런타임 카운트만 소유합니다. 무엇이 나올지는 방어전 SO와 스포너가 정합니다.
///
/// 한 웨이브는 전투 → 정리 → 휴식 순서입니다. 전투 시간이 끝나면 새 스폰만 멈추고, 이 매니저의 스포너가 내보낸
/// 살아 있는 적이 0이 되면 휴식으로 넘어갑니다(함정 설치가 휴식에만 가능해서, 적이 남은 채 넘어가면 전투와 설치를
/// 동시에 해야 하기 때문입니다). 마지막 웨이브는 정리가 끝나면 휴식 없이 귀환 구역을 엽니다.
///
/// 무엇이 나올지는 방어전 SO가 정하고, 방어전 SO는 입장할 때 회차 표에서 정해집니다(<see cref="DefenseSceneDataManager.DefenseStage"/>). 이 매니저는 전투가 시작될 때마다 방어전 스포너(<see cref="EnemyDefenseSpawnPoint"/>)에
/// 공격로 이름별 웨이브 SO를 넘겨 실행시키고, 총 웨이브 수도 방어전 SO를 따릅니다. 방어전 SO가 없으면 시작하지 않습니다.
/// 전투·휴식 시간은 이 매니저 값을 모든 웨이브, 모든 공격로에 적용합니다.
/// 설계 근거: privateDoc `DEFENSE_WAVE_SPAWN_SPEC_KR.md` §4.4, §5.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseManager : MonoBehaviour
{
    /// <summary>Shooter 업그레이드 한 레벨에서 함께 배치되는 좌우 지정사수 한 쌍입니다.</summary>
    /// <remarks>
    /// 기획 기준(2026-09-29): 레벨마다 좌우 한 명씩, 2명이 추가됩니다. 1레벨은 좌우 2명(A_01, B_01),
    /// 2레벨은 좌우 4명(A_01, B_01, A_02, B_02)입니다. 네 명 모두 같은 지정사수 기능(사격·장전·애니메이션)을 쓰고
    /// 모델만 다를 예정이므로, 칸에는 <see cref="DefenseMarksmanController"/>가 붙은 NPC를 넣습니다.
    /// </remarks>
    [Serializable]
    public struct MarksmanTier
    {
        [Tooltip("이 레벨에서 켤 왼쪽 지정사수입니다. 비어 있으면 무시합니다.")]
        public GameObject Left;

        [Tooltip("이 레벨에서 켤 오른쪽 지정사수입니다. 비어 있으면 무시합니다.")]
        public GameObject Right;
    }


    [Header("Defense Round")]
    [Tooltip("전투 구간 시간(초)입니다. 모든 웨이브, 모든 공격로에 같이 적용합니다. 이 시간이 끝나면 새 스폰만 멈추고 남은 적 정리로 넘어갑니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_roundDuration = 60.0f;

    [Tooltip("남은 적을 모두 정리한 뒤 다음 전투까지의 휴식 시간(초)입니다. 함정은 이 구간에만 설치할 수 있습니다. 마지막 웨이브 뒤에는 휴식이 없습니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_restDuration = 10.0f;

    [Tooltip("이 매니저가 웨이브를 실행시킬 방어전 스포너 목록입니다. 남은 적 수도 이 목록의 스포너가 내보낸 적만 셉니다. 비어 있는 항목은 무시합니다.")]
    [SerializeField] private List<EnemyDefenseSpawnPoint> m_spawnPoints = new List<EnemyDefenseSpawnPoint>();

    [Header("Victory Return Zone")]
    [Tooltip("씬에 미리 배치해 둔 귀환 구역입니다. 평소에는 꺼 두고, 마지막 웨이브를 막으면 켭니다. 위치는 씬에서 이 오브젝트를 직접 옮겨 정합니다.")]
    [SerializeField] private GameObject m_returnPoint;

    [Tooltip("웨이브 진행과 방어전 결과를 기록할 방어전 데이터 매니저입니다. 비워 두면 런타임에 찾습니다.")]
    [SerializeField] private DefenseSceneDataManager m_defenseSceneDataManager;

    [Header("Marksman Placement")]
    [Tooltip("Shooter 업그레이드 레벨별 지정사수 배치입니다. 첫 칸이 1레벨, 둘째 칸이 2레벨입니다. " +
             "레벨이 N이면 1~N번째 칸의 좌우 지정사수를 모두 켜고 나머지는 끕니다. " +
             "비워 두면 이 매니저는 지정사수를 건드리지 않습니다.")]
    [SerializeField] private MarksmanTier[] m_marksmanTiers = Array.Empty<MarksmanTier>();

    [Header("Defense HUD")]
    [Tooltip("전투 또는 휴식의 남은 시간을 분:초로 표시할 텍스트입니다. 비어 있으면 타이머 표시는 생략합니다.")]
    [SerializeField] private TMP_Text m_timerText;

    [Tooltip("지금이 전투 구간인지 휴식 구간인지 표시할 텍스트입니다. 비어 있으면 구간 표시는 생략합니다.")]
    [SerializeField] private TMP_Text m_phaseLabelText;

    [Tooltip("남은 웨이브 수를 표시할 텍스트입니다. 비어 있으면 남은 웨이브 표시는 생략합니다.")]
    [SerializeField] private TMP_Text m_remainingWaveText;

    [Tooltip("남은 웨이브 표시 형식입니다. {0}에 남은 수, {1}에 전체 수가 들어갑니다.")]
    [SerializeField] private string m_remainingWaveFormat = "남은 라운드 {0}";

    [Tooltip("전투 구간에 표시할 문구입니다.")]
    [SerializeField] private string m_combatPhaseLabel = "전투시간";

    [Tooltip("휴식 구간에 표시할 문구입니다.")]
    [SerializeField] private string m_restPhaseLabel = "휴식시간";

    [Tooltip("전투 시간이 끝나고 남은 적을 정리하는 동안 구간 라벨에 표시할 형식입니다. {0}에 남은 적 수가 들어갑니다.")]
    [SerializeField] private string m_clearingPhaseLabelFormat = "남은 적 {0}";

    [Tooltip("웨이브 시작 알림을 표시할 텍스트입니다. 비어 있으면 시작 알림은 생략합니다.")]
    [SerializeField] private TMP_Text m_waveStartMessageText;

    [Tooltip("웨이브 시작 알림의 투명도를 제어할 CanvasGroup입니다. 비어 있으면 텍스트 알파만 바꿉니다.")]
    [SerializeField] private CanvasGroup m_waveStartMessageCanvasGroup;

    [Tooltip("웨이브 시작 때 표시할 알림 문구입니다.")]
    [SerializeField] private string m_waveStartMessage = "전투가 시작됩니다";

    [Tooltip("웨이브 시작 알림이 불투명하게 유지되는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waveStartMessageHoldDuration = 0.5f;

    [Tooltip("웨이브 시작 알림이 완전히 사라질 때까지 페이드아웃하는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waveStartMessageFadeDuration = 1.0f;

    [Header("Clearing Safety")]
    [Tooltip("정리 구간이 시작되고 이 시간(초)이 지나도 적이 남아 있으면, 남은 적을 모두 플레이어 우선으로 바꿔 지금 조작 중인 캐릭터 쪽으로 끌어옵니다. 0이면 끌어오지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_clearingPullDelay = 20.0f;

    [Tooltip("정리 구간이 시작되고 이 시간(초)이 지나면, 카메라에 보이지 않는 남은 적을 풀로 되돌립니다. 처치로 세지 않습니다. 0이면 회수하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_clearingOffscreenDespawnDelay = 40.0f;

    [Tooltip("정리 구간이 시작되고 이 시간(초)이 지나면, 보이는지와 관계없이 남은 적을 모두 풀로 되돌려 정리를 끝냅니다. 보이는 곳에 갇힌 적 때문에 진행이 멈추는 것을 막습니다. 0이면 강제로 끝내지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_clearingForceEndDelay = 60.0f;

    [Header("Defense Start Input")]
    [Tooltip("방어전 시작 전, 현재 스쿼드 조작 멤버가 상호작용 대상이 없는 곳에서 상호작용키를 이 시간(초)만큼 홀드하면 방어전을 시작합니다. " +
             "튜토리얼 여부와 관계없이 매 방어전 같은 규칙입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_emptySpaceHoldStartDuration = 3.0f;

    /// <summary>Defense 게임이 시작될 때 발생합니다. 튜토리얼 안내처럼 시작 시점에 붙는 UI가 구독합니다.</summary>
    /// <remarks>
    /// <see cref="StartDefenseGame"/>를 다시 부르면 재시작으로 보고 매번 발생합니다.
    /// 구독자가 중복 표시를 원하지 않으면 자기 쪽에서 한 번만 처리하도록 판단합니다.
    /// </remarks>
    public event Action OnDefenseStarted;

    /// <summary>플레이 라운드가 끝나고 휴식 구간이 시작될 때 발생합니다.</summary>
    /// <remarks>
    /// 휴식 동안에만 할 수 있는 정비 행동이 구독합니다. 예를 들어 다 쓴 함정은 이 시점에 다시
    /// 설치할 수 있게 됩니다. 마지막 웨이브 뒤에는 휴식으로 넘어가지 않으므로 발생하지 않습니다.
    /// </remarks>
    public event Action OnRestStarted;

    /// <summary>마지막 웨이브를 막아 귀환 구역이 준비되었을 때 발생합니다.</summary>
    public event Action OnDefenseVictoryReady;

    /// <summary>게임이 시작되어 라운드 매니저가 타이머를 갱신 중인지 여부입니다.</summary>
    private bool m_isGameStarted;

    /// <summary>현재 라운드 플레이 구간인지 여부입니다. false이면 정리 또는 휴식 구간입니다.</summary>
    private bool m_isPlaying;

    /// <summary>전투 시간이 끝나 새 스폰을 멈추고, 남은 적이 0이 되기를 기다리는 구간인지 여부입니다.</summary>
    private bool m_isClearing;

    /// <summary>정리 구간 라벨을 마지막으로 갱신할 때의 남은 적 수입니다. 바뀔 때만 다시 그리기 위해서입니다.</summary>
    private int m_lastShownRemainingEnemyCount = -1;

    /// <summary>이번 정리 구간이 시작된 뒤 지난 시간(초)입니다.</summary>
    private float m_clearingElapsed;

    /// <summary>이번 정리 구간에서 남은 적 끌어오기를 이미 했는지 여부입니다.</summary>
    private bool m_clearingPullApplied;

    /// <summary>화면 밖 회수를 다음에 검사할 정리 경과 시간(초)입니다. 매 프레임 검사하지 않기 위해서입니다.</summary>
    private float m_nextOffscreenCheckTime;

    /// <summary>안전장치가 남은 적을 모을 때 쓰는 재사용 목록입니다.</summary>
    private readonly List<EnemyController> m_clearingEnemyBuffer = new List<EnemyController>();

    /// <summary>화면 밖 회수를 검사하는 간격(초)입니다.</summary>
    private const float OffscreenCheckInterval = 1.0f;

    /// <summary>마지막 웨이브를 완료하여 귀환 구역 진입만 기다리는 상태인지 여부입니다.</summary>
    private bool m_isVictoryReady;

    /// <summary>현재 라운드에 남은 시간(초)입니다.</summary>
    private float m_roundTimer;

    /// <summary>현재 휴식 시간에 남은 시간(초)입니다.</summary>
    private float m_restTimer;

    /// <summary>웨이브 시작 알림의 남은 표시 시간(초)입니다.</summary>
    private float m_waveStartMessageRemaining;

    /// <summary>허공 상호작용키를 연속해서 누른 시간(초)입니다.</summary>
    private float m_emptySpaceHoldStartTimer;

    /// <summary>현재 실행 중인 웨이브 번호입니다. 첫 웨이브는 1입니다.</summary>
    private int m_currentWave;


    /// <summary>Defense 게임이 시작되었는지 여부입니다.</summary>
    public bool IsGameStarted => m_isGameStarted;

    /// <summary>현재 적 스폰이 허용된 라운드 플레이 구간인지 여부입니다.</summary>
    public bool IsPlaying => m_isGameStarted && m_isPlaying;

    /// <summary>마지막 웨이브 완료 후 귀환 구역 진입을 기다리는 상태인지 여부입니다.</summary>
    public bool IsVictoryReady => m_isGameStarted && m_isVictoryReady;

    /// <summary>현재 진행 중이거나 마지막으로 시작한 웨이브 번호입니다.</summary>
    public int CurrentWave => m_currentWave;

    /// <summary>이 방어전의 총 웨이브 수입니다. 방어전 SO가 없으면 0입니다.</summary>
    public int TotalWaveCount => m_activeStage != null ? m_activeStage.TotalWaveCount : 0;

    /// <summary>이번 방어전 구성입니다.</summary>
    public DefenseStageSO DefenseStage => m_activeStage;

    /// <summary>이번 방어전 구성입니다. 입장 데이터에서 가져오며, 방어전을 시작할 때마다 다시 읽습니다.</summary>
    private DefenseStageSO m_activeStage;

    /// <summary>현재 라운드에 남은 시간(초)입니다. 플레이 구간이 아니면 0입니다.</summary>
    public float RoundTimer => IsPlaying ? m_roundTimer : 0.0f;

    /// <summary>현재 휴식에 남은 시간(초)입니다. 휴식 구간이 아니면 0입니다.</summary>
    public float RestTimer => IsResting ? m_restTimer : 0.0f;

    /// <summary>전투 시간이 끝나 남은 적을 정리하는 구간인지 여부입니다.</summary>
    public bool IsClearing => m_isGameStarted && m_isClearing;

    /// <summary>휴식 구간인지 여부입니다. 방어전 시작 전도 포함합니다.</summary>
    /// <remarks>
    /// 방어전 시작 전(시작 홀드를 기다리는 시간)도 휴식으로 취급합니다(사용자 확정 2026-10-01). 그 시간에도 함정을 설치할 수 있어야
    /// 하기 때문입니다. 시작 전에는 휴식 타이머가 돌지 않으므로 <see cref="RestTimer"/>는 0입니다.
    /// </remarks>
    public bool IsResting => !m_isPlaying && !m_isClearing && !m_isVictoryReady;

    /// <summary>
    /// 지금 함정을 설치할 수 있는 구간인지 여부입니다. 휴식 구간(방어전 시작 전 포함)입니다.
    /// </summary>
    /// <remarks>
    /// 전투, 남은 적 정리, 승리 뒤에는 닫힙니다. 상시 설치 함정(재설치 정책 Always)은 이 값과 관계없이 설치할 수 있습니다.
    /// 구간이 바뀔 때마다 씬의 함정에 알립니다(<see cref="Trap.SetBuildWindowOpen"/>).
    /// </remarks>
    public bool IsTrapBuildWindowOpen => IsResting;

    /// <summary>설치 구간을 알릴 씬의 함정 목록입니다. 처음 알릴 때 한 번 찾습니다.</summary>
    private Trap[] m_traps;

    /// <summary>이 매니저의 스포너가 내보내 아직 살아 있는 적 수입니다. 시체는 세지 않습니다.</summary>
    public int RemainingEnemyCount => CountManagedLiveEnemies();

    /// <summary>이 매니저가 제어하는 스폰 포인트 목록입니다.</summary>
    public IReadOnlyList<EnemyDefenseSpawnPoint> SpawnPoints => m_spawnPoints;

    /// <summary>방어전 시작 전 허공 상호작용 홀드 진행도입니다.</summary>
    public float EmptySpaceHoldStartProgress => m_isGameStarted
        ? 0.0f
        : Mathf.Clamp01(m_emptySpaceHoldStartTimer / m_emptySpaceHoldStartDuration);

    private void Awake()
    {
        // 시작 전 상태는 조작을 막지 않되, 이 매니저가 소유한 적 생산만 확실히 차단합니다.
        m_isGameStarted = false;
        m_isPlaying = false;
        m_isClearing = false;
        m_isVictoryReady = false;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        RefreshTimerText();
        HideWaveStartMessage();
    }

    private void Start()
    {
        // DefenseSceneDataManager는 실행 순서상 먼저 Start까지 마쳐 입장 데이터를 갖고 있습니다.
        if (ResolveActiveStage() != null)
        {
            ResolveDefenseSceneDataManager()?.ConfigureWaves(TotalWaveCount);
        }
        else
        {
            Debug.LogError("[DefenseManager] 이번 방어전 구성(방어전 SO)을 정하지 못했습니다. DefenseSceneDataManager의 Defense Schedule을 확인하세요. 이대로는 방어전을 시작할 수 없습니다.", this);
        }

        ApplyMarksmanPlacement();
    }

    /// <summary>
    /// 이번 방어전 구성을 입장 데이터에서 가져옵니다.
    /// </summary>
    /// <remarks>
    /// 방어전 SO는 이 매니저가 들고 있지 않습니다. 회차마다 달라야 하므로, 입장할 때 <see cref="DefenseSceneDataManager"/>가
    /// 회차 표에서 골라 입장 데이터에 고정한 값을 씁니다(<see cref="DefenseSceneDataManager.DefenseStage"/>).
    /// </remarks>
    private DefenseStageSO ResolveActiveStage()
    {
        DefenseSceneDataManager defenseData = ResolveDefenseSceneDataManager();
        m_activeStage = defenseData != null ? defenseData.DefenseStage : null;
        return m_activeStage;
    }

    private void Update()
    {
        UpdateWaveStartMessage();

        if (!m_isGameStarted)
        {
            UpdateEmptySpaceHoldStart();
            return;
        }

        if (m_isVictoryReady)
        {
            return;
        }

        if (m_isClearing)
        {
            UpdateClearing();
            return;
        }

        float remainingDelta = Time.deltaTime;
        if (m_isPlaying)
        {
            if (m_roundTimer > remainingDelta)
            {
                m_roundTimer -= remainingDelta;
                RefreshTimerText();
                return;
            }

            // 전투 시간이 끝나도 바로 휴식으로 가지 않습니다. 남은 적 수는 다음 프레임부터 정리 구간이 셉니다.
            m_roundTimer = 0.0f;
            BeginClearing();
            return;
        }

        if (m_restTimer > remainingDelta)
        {
            m_restTimer -= remainingDelta;
            RefreshTimerText();
            return;
        }

        m_restTimer = 0.0f;
        BeginRound();
    }

    /// <summary>Defense 게임을 시작하고 첫 웨이브 타이머를 시작합니다.</summary>
    /// <remarks>이미 실행 중이면 현재 웨이브와 휴식 카운트를 초기화하고 첫 웨이브부터 다시 시작합니다.</remarks>
    [ContextMenu("Start Defense")]
    public void StartDefense()
    {
        if (ResolveActiveStage() == null)
        {
            Debug.LogError("[DefenseManager] 이번 방어전 구성(방어전 SO)이 없어 방어전을 시작하지 않습니다. DefenseSceneDataManager의 Defense Schedule을 확인하세요.", this);
            return;
        }

        m_isGameStarted = true;
        m_isClearing = false;
        m_isVictoryReady = false;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        SetReturnPointActive(false);
        // 재시작이면 앞 판의 웨이브 기록을 비우고 다시 시작합니다.
        ResolveDefenseSceneDataManager()?.ConfigureWaves(TotalWaveCount);
        PrepareStageSpawners();
        BeginRound();
        OnDefenseStarted?.Invoke();
    }

    /// <summary>기존 Inspector 및 외부 호출 호환성을 위해 Defense 시작을 전달합니다.</summary>
    [ContextMenu("Start Defense Game")]
    public void StartDefenseGame()
    {
        StartDefense();
    }

    /// <summary>Defense 게임을 중지하고 이 매니저가 제어한 스포너를 끕니다.</summary>
    [ContextMenu("Stop Defense Game")]
    public void StopDefenseGame()
    {
        m_isGameStarted = false;
        m_isPlaying = false;
        m_isClearing = false;
        m_isVictoryReady = false;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        SetReturnPointActive(false);
        EndStageCombat();
        RefreshTimerText();
        HideWaveStartMessage();
        RefreshTrapBuildWindow();
    }

    /// <summary>
    /// 지금 구간이 함정 설치 구간인지 씬의 모든 함정에 알립니다.
    /// </summary>
    /// <remarks>
    /// 상시 설치가 아닌 함정은 설치 구간에만 청사진을 보이고 설치를 받습니다. 이미 설치된 함정은 그대로 둡니다.
    /// 함정 목록은 처음 부를 때 찾습니다. 함정은 씬에 미리 배치되므로 진행 중에 새로 생기지 않습니다.
    /// 진행 중에 켜진 함정은 스스로 <see cref="IsTrapBuildWindowOpen"/>을 읽어 맞춥니다.
    /// </remarks>
    private void RefreshTrapBuildWindow()
    {
        m_traps ??= FindObjectsByType<Trap>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        bool isOpen = IsTrapBuildWindowOpen;
        for (int i = 0; i < m_traps.Length; i++)
        {
            if (m_traps[i] != null)
            {
                m_traps[i].SetBuildWindowOpen(isOpen);
            }
        }
    }

    [Foldout("Debug")]
    [Button("라운드 스킵")]
    /// <summary>
    /// 지금 구간의 남은 시간을 무시하고 다음 구간으로 넘어갑니다.
    /// </summary>
    /// <remarks>
    /// 검증용입니다. 전투 중이면 정리로, 정리 중이면 휴식으로(마지막 웨이브였다면 승리로), 휴식 중이면 다음 전투로
    /// 넘어갑니다. 적을 치우는 옵션이 켜져 있으면 전투를 넘길 때 정리도 곧바로 끝납니다.
    /// 타이머가 자연히 끝났을 때와 같은 경로를 타므로 <see cref="OnRestStarted"/> 같은 알림도 똑같이
    /// 발생합니다. 따로 처리했다면 버튼으로 넘어갈 때와 시간으로 넘어갈 때가 달라져 검증이 무의미해집니다.
    ///
    /// 방어전이 시작되지 않았거나 이미 승리 상태면 아무 일도 하지 않습니다.
    /// </remarks>
    public void SkipRound()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DefenseManager] 라운드 스킵은 Play Mode에서만 동작합니다.", this);
            return;
        }

        if (!m_isGameStarted)
        {
            Debug.LogWarning("[DefenseManager] 방어전이 시작되지 않아 건너뛸 라운드가 없습니다.", this);
            return;
        }

        if (m_isVictoryReady)
        {
            Debug.LogWarning("[DefenseManager] 이미 마지막 웨이브를 끝낸 상태라 건너뛸 라운드가 없습니다.", this);
            return;
        }

        if (m_debugSkipClearsEnemies)
        {
            int despawnedCount = DespawnManagedEnemies();
            Debug.Log($"[DefenseManager] 라운드 스킵: 남은 적 {despawnedCount}마리를 치웠습니다.", this);
        }

        if (m_isPlaying)
        {
            m_roundTimer = 0.0f;
            BeginClearing();
            return;
        }

        if (m_isClearing)
        {
            FinishClearing();
            return;
        }

        m_restTimer = 0.0f;
        BeginRound();
    }

    [Foldout("Debug")]
    [Tooltip("켜면 라운드 스킵이 필드에 남은 적을 함께 치웁니다. 끄면 적을 둔 채로 구간만 넘깁니다. 휴식으로 넘어갔는데 지난 전투의 적이 돌아다니는 상태를 피하려면 켜 두세요.")]
    [SerializeField] private bool m_debugSkipClearsEnemies = true;

    [Foldout("Debug")]
    [Tooltip("0 이상이면 Shooter 업그레이드 레벨 대신 이 값으로 지정사수를 배치합니다. -1이면 입장 데이터 값을 씁니다. " +
             "셸터를 거치지 않고 이 씬만 실행해 배치를 확인할 때 씁니다.")]
    [Min(-1)]
    [SerializeField] private int m_debugMarksmanLevelOverride = -1;

    /// <summary>
    /// 이 매니저가 제어하는 스폰 포인트가 내보낸 적을 모두 풀로 되돌립니다.
    /// </summary>
    /// <returns>치운 적의 수입니다.</returns>
    /// <remarks>
    /// 죽이는 것이 아니라 없던 일로 하는 것이라 처치 수가 오르지 않습니다. 목록 밖 스폰 포인트가
    /// 내보낸 적은 이 매니저 소유가 아니므로 건드리지 않습니다.
    /// </remarks>
    private int DespawnManagedEnemies()
    {
        if (m_spawnPoints == null)
        {
            return 0;
        }

        int despawnedCount = 0;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            EnemyDefenseSpawnPoint spawnPoint = m_spawnPoints[i];
            if (spawnPoint == null)
            {
                continue;
            }

            despawnedCount += spawnPoint.DespawnActiveEnemies();
        }

        return despawnedCount;
    }

    /// <summary>다음 플레이 라운드를 시작합니다.</summary>
    private void BeginRound()
    {
        m_currentWave++;
        m_isPlaying = true;
        m_isClearing = false;
        m_roundTimer = Mathf.Max(0.01f, m_roundDuration);
        m_restTimer = 0.0f;
        BeginStageCombat();
        RefreshTrapBuildWindow();
        RefreshTimerText();
        ShowWaveStartMessage();
        ResolveDefenseSceneDataManager()?.RecordWaveStarted(m_currentWave);
    }

    /// <summary>전투 시간을 끝내고 새 스폰을 멈춘 뒤, 남은 적 정리 구간을 시작합니다.</summary>
    private void BeginClearing()
    {
        m_isPlaying = false;
        m_isClearing = true;
        m_roundTimer = 0.0f;
        m_lastShownRemainingEnemyCount = -1;
        m_clearingElapsed = 0.0f;
        m_clearingPullApplied = false;
        m_nextOffscreenCheckTime = m_clearingOffscreenDespawnDelay;
        EndStageCombat();
        RefreshTimerText();
    }

    /// <summary>남은 적이 0이 되면 정리를 끝냅니다. 그 전까지는 남은 적 수를 갱신하고 안전장치를 시간에 맞춰 적용합니다.</summary>
    /// <remarks>
    /// 안전장치는 정리 구간 시작부터 셉니다(명세 C4). 끌어오기 → 화면 밖 회수 → 강제 종료 순서이며, 각각 0이면 꺼집니다.
    /// 지스타 시연에서 갇힌 적 한 마리 때문에 휴식이 시작되지 않는 일을 막기 위한 장치입니다.
    /// </remarks>
    private void UpdateClearing()
    {
        m_clearingElapsed += Time.deltaTime;

        int remaining = CountManagedLiveEnemies();
        if (remaining > 0)
        {
            ApplyClearingSafety();
            remaining = CountManagedLiveEnemies();
        }

        if (remaining <= 0)
        {
            FinishClearing();
            return;
        }

        if (remaining != m_lastShownRemainingEnemyCount)
        {
            m_lastShownRemainingEnemyCount = remaining;
            RefreshPhaseLabel();
        }
    }

    /// <summary>정리 경과 시간에 맞춰 끌어오기, 화면 밖 회수, 강제 종료를 적용합니다.</summary>
    private void ApplyClearingSafety()
    {
        if (m_clearingForceEndDelay > 0.0f && m_clearingElapsed >= m_clearingForceEndDelay)
        {
            int despawned = DespawnRemainingEnemies(null, "강제 종료");
            Debug.LogWarning($"[DefenseManager] 정리 구간이 {m_clearingForceEndDelay:0}초를 넘어 남은 적 {despawned}마리를 회수하고 정리를 끝냅니다.", this);
            return;
        }

        if (!m_clearingPullApplied && m_clearingPullDelay > 0.0f && m_clearingElapsed >= m_clearingPullDelay)
        {
            m_clearingPullApplied = true;
            PullRemainingEnemiesToPlayer();
        }

        if (m_clearingOffscreenDespawnDelay > 0.0f && m_clearingElapsed >= m_nextOffscreenCheckTime)
        {
            m_nextOffscreenCheckTime = m_clearingElapsed + OffscreenCheckInterval;
            Camera viewCamera = Camera.main;
            if (viewCamera != null)
            {
                DespawnRemainingEnemies(enemy => !IsVisibleToCamera(viewCamera, enemy.transform.position), "화면 밖 회수");
            }
        }
    }

    /// <summary>남은 적을 모두 플레이어 우선으로 바꿔 지금 조작 중인 캐릭터 쪽으로 끌어옵니다.</summary>
    private void PullRemainingEnemiesToPlayer()
    {
        CollectRemainingEnemies();
        for (int i = 0; i < m_clearingEnemyBuffer.Count; i++)
        {
            m_clearingEnemyBuffer[i].ForcePursuePlayer();
        }

        Debug.LogWarning(
            $"[DefenseManager] 정리 구간이 {m_clearingPullDelay:0}초를 넘어 남은 적 {m_clearingEnemyBuffer.Count}마리를 플레이어 쪽으로 끌어옵니다. " +
            DescribeEnemies(m_clearingEnemyBuffer), this);
        m_clearingEnemyBuffer.Clear();
    }

    /// <summary>
    /// 남은 적 가운데 조건에 맞는 적을 풀로 되돌리고, 어떤 적이 어디서 회수됐는지 경고로 남깁니다.
    /// </summary>
    /// <param name="shouldDespawn">회수할 적이면 true입니다. null이면 전부입니다.</param>
    /// <param name="reason">경고에 남길 사유입니다.</param>
    /// <returns>회수한 수입니다.</returns>
    /// <remarks>회수된 위치는 적이 갇히는 곳을 찾는 단서라 이름과 좌표를 함께 남깁니다.</remarks>
    private int DespawnRemainingEnemies(Predicate<EnemyController> shouldDespawn, string reason)
    {
        CollectRemainingEnemies();
        if (shouldDespawn != null)
        {
            m_clearingEnemyBuffer.RemoveAll(enemy => !shouldDespawn(enemy));
        }

        if (m_clearingEnemyBuffer.Count == 0)
        {
            return 0;
        }

        string description = DescribeEnemies(m_clearingEnemyBuffer);
        int despawned = 0;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            if (m_spawnPoints[i] != null)
            {
                despawned += m_spawnPoints[i].DespawnLiveEnemies(enemy => m_clearingEnemyBuffer.Contains(enemy));
            }
        }

        Debug.LogWarning($"[DefenseManager] 정리 안전장치({reason}): 남은 적 {despawned}마리를 회수했습니다. {description}", this);
        m_clearingEnemyBuffer.Clear();
        return despawned;
    }

    /// <summary>이 매니저의 스포너가 내보내 살아 있는 적을 재사용 목록에 모읍니다.</summary>
    private void CollectRemainingEnemies()
    {
        m_clearingEnemyBuffer.Clear();
        if (m_spawnPoints == null)
        {
            return;
        }

        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            m_spawnPoints[i]?.CollectLiveEnemies(m_clearingEnemyBuffer);
        }
    }

    /// <summary>위치가 카메라 화면 안(앞쪽)에 있는지 확인합니다. 가림은 보지 않습니다.</summary>
    private static bool IsVisibleToCamera(Camera viewCamera, Vector3 worldPosition)
    {
        Vector3 viewport = viewCamera.WorldToViewportPoint(worldPosition);
        return viewport.z > 0.0f && viewport.x >= 0.0f && viewport.x <= 1.0f && viewport.y >= 0.0f && viewport.y <= 1.0f;
    }

    /// <summary>적 이름과 위치를 한 줄로 만듭니다. 경고에 남겨 적이 갇히는 곳을 찾는 데 씁니다.</summary>
    private static string DescribeEnemies(List<EnemyController> enemies)
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < enemies.Count; i++)
        {
            Vector3 p = enemies[i].transform.position;
            builder.Append(i == 0 ? string.Empty : ", ").Append(enemies[i].name).Append($" ({p.x:0.#}, {p.y:0.#}, {p.z:0.#})");
        }

        return builder.ToString();
    }

    /// <summary>정리를 끝내고, 마지막 웨이브면 승리로, 아니면 휴식으로 넘어갑니다.</summary>
    private void FinishClearing()
    {
        m_isClearing = false;
        if (m_currentWave >= TotalWaveCount)
        {
            BeginVictory();
            return;
        }

        BeginRest();
    }

    /// <summary>남은 적을 정리한 뒤 휴식 구간을 시작합니다.</summary>
    private void BeginRest()
    {
        m_isPlaying = false;
        m_isClearing = false;
        m_roundTimer = 0.0f;
        m_restTimer = Mathf.Max(0.01f, m_restDuration);
        RefreshTimerText();
        ResolveDefenseSceneDataManager()?.RecordRestStarted();

        // 휴식 복구 정책(OnRest) 함정이 먼저 되살아난 뒤 설치 구간을 엽니다. 순서가 바뀌어도 결과는 같지만,
        // 복구와 표시가 한 번에 맞춰지도록 알림을 먼저 보냅니다.
        OnRestStarted?.Invoke();
        RefreshTrapBuildWindow();
    }

    /// <summary>마지막 웨이브를 완료하고, 정산 전 귀환 구역 진입 대기 상태로 전환합니다.</summary>
    private void BeginVictory()
    {
        m_isPlaying = false;
        m_isClearing = false;
        m_isVictoryReady = true;
        EndStageCombat();
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        RefreshTimerText();
        HideWaveStartMessage();

        RecordVictory();
        SetReturnPointActive(true);
        RefreshTrapBuildWindow();
        OnDefenseVictoryReady?.Invoke();
    }

    /// <summary>
    /// 공격로 이름을 검사하고, 방어전 스포너마다 그 공격로가 쓰는 항목으로 풀을 준비시킵니다.
    /// </summary>
    /// <remarks>
    /// 이름이 맞지 않아도 진행은 멈추지 않고 경고만 남깁니다. 방어전 SO에 없는 공격로의 스포너는 모든 웨이브를 쉽니다.
    /// </remarks>
    private void PrepareStageSpawners()
    {
        if (m_activeStage == null || m_spawnPoints == null)
        {
            return;
        }

        var problems = new List<string>();
        m_activeStage.CollectProblems(problems);

        var sceneRoutes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            EnemyDefenseSpawnPoint defenseSpawnPoint = m_spawnPoints[i];
            if (defenseSpawnPoint == null)
            {
                continue;
            }

            string routeId = defenseSpawnPoint.RouteId;
            if (string.IsNullOrEmpty(routeId))
            {
                problems.Add($"스포너 '{defenseSpawnPoint.name}'의 공격로 이름이 비어 있어 모든 웨이브를 쉽니다.");
            }
            else if (!m_activeStage.HasRoute(routeId))
            {
                problems.Add($"스포너 '{defenseSpawnPoint.name}'의 공격로 '{routeId}'가 방어전 SO '{m_activeStage.name}'에 없어 모든 웨이브를 쉽니다.");
            }
            else
            {
                sceneRoutes.Add(routeId);
            }

            defenseSpawnPoint.PrepareForStage(m_activeStage);
        }

        IReadOnlyList<DefenseStageSO.Route> routes = m_activeStage.Routes;
        for (int i = 0; i < routes.Count; i++)
        {
            string routeId = routes[i].RouteId;
            if (!string.IsNullOrEmpty(routeId) && !sceneRoutes.Contains(routeId))
            {
                problems.Add($"방어전 SO '{m_activeStage.name}'의 공격로 '{routeId}'를 가진 스포너가 이 매니저 목록에 없어 그 공격로는 나오지 않습니다.");
            }
        }

        for (int i = 0; i < problems.Count; i++)
        {
            Debug.LogWarning($"[DefenseManager] {problems[i]}", this);
        }
    }

    /// <summary>방어전 스포너마다 이번 웨이브에 자기 공격로가 실행할 웨이브 SO를 넘겨 전투를 시작시킵니다.</summary>
    /// <remarks>웨이브 SO가 없는 공격로(빈 칸, 목록보다 뒤)는 null을 받아 이번 전투를 쉽니다.</remarks>
    private void BeginStageCombat()
    {
        if (m_activeStage == null || m_spawnPoints == null)
        {
            return;
        }

        int waveIndex = m_currentWave - 1;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            EnemyDefenseSpawnPoint defenseSpawnPoint = m_spawnPoints[i];
            if (defenseSpawnPoint != null)
            {
                defenseSpawnPoint.BeginCombat(m_activeStage.GetWave(defenseSpawnPoint.RouteId, waveIndex));
            }
        }
    }

    /// <summary>방어전 스포너의 전투를 끝냅니다. 새 그룹을 만들지 않고, 기다리던 그룹은 버립니다.</summary>
    private void EndStageCombat()
    {
        if (m_spawnPoints == null)
        {
            return;
        }

        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            m_spawnPoints[i]?.EndCombat();
        }
    }

    /// <summary>이 매니저의 스포너가 내보내 아직 살아 있는 적 수를 셉니다. 시체는 세지 않습니다.</summary>
    private int CountManagedLiveEnemies()
    {
        if (m_spawnPoints == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            if (m_spawnPoints[i] != null)
            {
                count += m_spawnPoints[i].ActiveEnemyCount;
            }
        }

        return count;
    }

    /// <summary>
    /// 방어전 시작 규칙입니다. 플레이어가 상호작용 대상으로 조준하지 않은 상태에서만
    /// 상호작용키를 일정 시간 유지하면 <see cref="StartDefense"/>를 호출합니다.
    /// </summary>
    /// <remarks>
    /// 튜토리얼 여부와 관계없이 매 방어전 같은 규칙입니다(사용자 확정 2026-10-01). 끄는 옵션은 두지 않습니다.
    /// 다른 시작 경로가 없어서, 끄면 방어전을 시작할 방법이 없어지기 때문입니다.
    /// 시작 전 시간은 휴식으로 취급하므로 이 동안 함정을 설치할 수 있습니다(<see cref="IsResting"/>).
    /// </remarks>
    private void UpdateEmptySpaceHoldStart()
    {

        if (!TryGetActiveSquadStartInput(out PlayerInputController startInput,
                out InteractionController startInteraction)
            || (startInteraction != null && startInteraction.Current != null)
            || !startInput.Interact)
        {
            m_emptySpaceHoldStartTimer = 0.0f;
            return;
        }

        m_emptySpaceHoldStartTimer += Time.deltaTime;
        if (m_emptySpaceHoldStartTimer >= m_emptySpaceHoldStartDuration)
        {
            StartDefense();
        }
    }

    /// <summary>
    /// 현재 스쿼드가 직접 조작 중인 멤버에게서 방어전 시작 입력과 상호작용 상태를 가져옵니다.
    /// </summary>
    /// <remarks>
    /// 시작 입력을 Inspector에 고정하면 스쿼드 전환 뒤 비활성 멤버의 입력을 계속 읽게 됩니다.
    /// 따라서 매 프레임 <see cref="SquadManager.PlayerSquadMember"/>를 정본으로 사용합니다.
    /// </remarks>
    private static bool TryGetActiveSquadStartInput(
        out PlayerInputController startInput,
        out InteractionController startInteraction)
    {
        startInput = null;
        startInteraction = null;

        SquadMemberController activeMember = SquadManager.Instance?.PlayerSquadMember;
        if (activeMember == null || !activeMember.IsPlayerSquadMember)
        {
            return false;
        }

        startInput = activeMember.GetComponent<PlayerInputController>();
        startInteraction = activeMember.GetComponent<InteractionController>();
        return startInput != null && startInput.isActiveAndEnabled;
    }

    /// <summary>
    /// Shooter 업그레이드 레벨에 맞춰 지정사수를 켜고 끕니다.
    /// </summary>
    /// <remarks>
    /// 레벨은 <see cref="DefenseSceneDataManager"/>의 입장 데이터에서 읽습니다. 씬 도착 뒤 전역 데이터가 바뀌어도
    /// 이번 판의 배치는 출격 시점 값으로 고정됩니다. 레벨이 목록 길이보다 크면 목록 전체를 켭니다.
    /// </remarks>
    private void ApplyMarksmanPlacement()
    {
        if (m_marksmanTiers == null || m_marksmanTiers.Length == 0)
        {
            return;
        }

        int level = ResolveMarksmanLevel();
        for (int i = 0; i < m_marksmanTiers.Length; i++)
        {
            bool active = i < level;
            SetMarksmanActive(m_marksmanTiers[i].Left, active);
            SetMarksmanActive(m_marksmanTiers[i].Right, active);
        }
    }

    private int ResolveMarksmanLevel()
    {
        if (m_debugMarksmanLevelOverride >= 0)
        {
            return m_debugMarksmanLevelOverride;
        }

        DefenseSceneDataManager defenseData = ResolveDefenseSceneDataManager();
        if (defenseData != null)
        {
            return defenseData.GetUpgradeLevel(ScrambleUpgradeType.Shooter);
        }

        Debug.LogWarning("[DefenseManager] DefenseSceneDataManager가 없어 GameDataManager의 Shooter 레벨로 지정사수를 배치합니다.", this);
        return GameDataManager.Instance != null ? GameDataManager.Instance.ShooterUpgradeLevel : 0;
    }

    private static void SetMarksmanActive(GameObject marksman, bool active)
    {
        if (marksman != null && marksman.activeSelf != active)
        {
            marksman.SetActive(active);
        }
    }

    /// <summary>웨이브 진행을 기록할 방어전 데이터 매니저 참조를 확보합니다.</summary>
    private DefenseSceneDataManager ResolveDefenseSceneDataManager()
    {
        if (m_defenseSceneDataManager == null)
        {
            m_defenseSceneDataManager = DefenseSceneDataManager.Instance != null
                ? DefenseSceneDataManager.Instance
                : FindFirstObjectByType<DefenseSceneDataManager>();
        }

        return m_defenseSceneDataManager;
    }

    /// <summary>방어전 승리를 데이터 매니저에 기록합니다.</summary>
    /// <remarks>
    /// 임무 완료 표시는 <see cref="DefenseSceneDataManager.CompleteVictory"/>가 필드 데이터 매니저로 넘깁니다.
    /// 방어전 데이터 매니저가 아직 씬에 배치되지 않은 동안에는 예전처럼 필드 데이터 매니저에 직접 표시해,
    /// 귀환 정산이 Success로 나오는 기존 동작을 유지합니다.
    /// </remarks>
    private void RecordVictory()
    {
        DefenseSceneDataManager defenseData = ResolveDefenseSceneDataManager();
        if (defenseData != null)
        {
            defenseData.CompleteVictory();
            return;
        }

        Debug.LogWarning("[DefenseManager] DefenseSceneDataManager가 없어 필드 데이터 매니저에 임무 완료만 표시합니다.", this);
        FieldSceneDataManager fieldData = FieldSceneDataManager.Instance != null
            ? FieldSceneDataManager.Instance
            : FindFirstObjectByType<FieldSceneDataManager>();
        fieldData?.SetMissionCompleted(true);
    }

    /// <summary>미리 배치해 둔 귀환 구역을 켭니다.</summary>
    /// <remarks>
    /// 런타임에 만들지 않고 씬 오브젝트를 켜고 끄기만 합니다. 위치·크기·모양을 씬에서 눈으로 보고
    /// 끌어다 맞출 수 있어야 하는데, 생성 방식은 기준점과 오프셋 숫자로만 정해져 실제 자리가 실행 전에는
    /// 보이지 않았습니다. 트리거 구성도 프리팹이 이미 갖고 있으므로 코드가 다시 보장할 이유가 없습니다.
    /// </remarks>
    private void SetReturnPointActive(bool isActive)
    {
        if (m_returnPoint == null)
        {
            if (isActive)
            {
                Debug.LogWarning("[DefenseManager] 귀환 구역이 비어 있어 켜지 못했습니다. 인스펙터에서 지정하세요.", this);
            }

            return;
        }

        if (m_returnPoint.activeSelf != isActive)
        {
            m_returnPoint.SetActive(isActive);
        }
    }

    /// <summary>현재 전투 또는 휴식의 남은 시간을 분:초 형식으로 HUD에 반영합니다.</summary>
    private void RefreshTimerText()
    {
        // 구간 라벨과 남은 웨이브도 여기서 함께 갱신합니다. 호출부가 일곱 곳이라 따로 부르게 두면 새 경로가
        // 생길 때마다 한쪽만 빠져 표시가 어긋납니다. 타이머 참조가 없어도 둘은 갱신해야 하므로 null 검사보다 앞입니다.
        RefreshPhaseLabel();
        RefreshRemainingWaveText();

        if (m_timerText == null)
        {
            return;
        }

        bool shouldShow = m_isGameStarted && !m_isVictoryReady;
        if (m_timerText.gameObject.activeSelf != shouldShow)
        {
            m_timerText.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        float remaining = m_isPlaying ? m_roundTimer : m_restTimer;
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        m_timerText.text = $"{minutes:00}:{seconds:00}";
    }

    /// <summary>
    /// 지금이 전투 구간인지 휴식 구간인지 표시합니다.
    /// </summary>
    /// <remarks>
    /// 타이머와 표시 조건을 같게 둡니다. 숫자만 남고 무엇을 세는 시간인지 사라지거나, 반대로 라벨만
    /// 남는 상태가 생기지 않게 하기 위함입니다. 그래서 <see cref="RefreshTimerText"/>와 같은 곳에서 함께 부릅니다.
    /// </remarks>
    private void RefreshPhaseLabel()
    {
        if (m_phaseLabelText == null)
        {
            return;
        }

        bool shouldShow = m_isGameStarted && !m_isVictoryReady;
        if (m_phaseLabelText.gameObject.activeSelf != shouldShow)
        {
            m_phaseLabelText.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        if (m_isPlaying)
        {
            m_phaseLabelText.text = m_combatPhaseLabel;
        }
        else if (m_isClearing)
        {
            // 전투 타이머가 00:00인데 휴식이 시작되지 않는 이유를 보여줍니다.
            m_phaseLabelText.text = string.Format(m_clearingPhaseLabelFormat, CountManagedLiveEnemies());
        }
        else
        {
            m_phaseLabelText.text = m_restPhaseLabel;
        }
    }

    /// <summary>
    /// 남은 웨이브 수를 표시합니다.
    /// </summary>
    /// <remarks>
    /// 진행 중인 웨이브를 아직 남은 것으로 셉니다. 1/3 전투 중에 "남은 라운드 2"가 되면 지금 막고 있는
    /// 웨이브가 셈에서 빠져 하나 적게 보입니다. 휴식 구간에서는 그 웨이브가 이미 끝났으므로 자연히 줄어듭니다.
    ///
    /// 타이머·구간 라벨과 표시 조건을 같게 둡니다. 셋이 한 줄에 놓이는데 조건이 갈리면 일부만 남습니다.
    /// </remarks>
    private void RefreshRemainingWaveText()
    {
        if (m_remainingWaveText == null)
        {
            return;
        }

        bool shouldShow = m_isGameStarted && !m_isVictoryReady;
        if (m_remainingWaveText.gameObject.activeSelf != shouldShow)
        {
            m_remainingWaveText.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        // 정리 중인 웨이브도 아직 끝나지 않았으므로 남은 것으로 셉니다.
        int totalWaveCount = TotalWaveCount;
        int remaining = Mathf.Max(0, totalWaveCount - m_currentWave + (m_isPlaying || m_isClearing ? 1 : 0));
        m_remainingWaveText.text = string.Format(m_remainingWaveFormat, remaining, totalWaveCount);
    }

    /// <summary>웨이브 시작 알림을 표시하고 설정된 유지·페이드 시간을 시작합니다.</summary>
    private void ShowWaveStartMessage()
    {
        if (m_waveStartMessageText == null)
        {
            return;
        }

        m_waveStartMessageText.text = m_waveStartMessage;
        SetWaveStartMessageVisible(true);
        SetWaveStartMessageAlpha(1.0f);

        m_waveStartMessageRemaining = m_waveStartMessageHoldDuration + m_waveStartMessageFadeDuration;
        if (m_waveStartMessageRemaining <= 0.0f)
        {
            HideWaveStartMessage();
        }
    }

    /// <summary>웨이브 시작 알림의 유지·페이드 상태를 매 프레임 갱신합니다.</summary>
    private void UpdateWaveStartMessage()
    {
        if (m_waveStartMessageRemaining <= 0.0f)
        {
            return;
        }

        // 게임 시간 배율과 무관하게 플레이어가 읽을 수 있는 실제 시간으로 알림을 페이드합니다.
        m_waveStartMessageRemaining = Mathf.Max(0.0f, m_waveStartMessageRemaining - Time.unscaledDeltaTime);
        if (m_waveStartMessageRemaining <= 0.0f)
        {
            HideWaveStartMessage();
            return;
        }

        if (m_waveStartMessageRemaining <= m_waveStartMessageFadeDuration
            && m_waveStartMessageFadeDuration > 0.0f)
        {
            SetWaveStartMessageAlpha(m_waveStartMessageRemaining / m_waveStartMessageFadeDuration);
        }
    }

    /// <summary>웨이브 시작 알림을 즉시 숨기고 페이드 상태를 초기화합니다.</summary>
    private void HideWaveStartMessage()
    {
        m_waveStartMessageRemaining = 0.0f;
        SetWaveStartMessageAlpha(0.0f);
        SetWaveStartMessageVisible(false);
    }

    /// <summary>웨이브 시작 알림 루트의 활성 상태를 변경합니다.</summary>
    private void SetWaveStartMessageVisible(bool visible)
    {
        GameObject root = m_waveStartMessageCanvasGroup != null
            ? m_waveStartMessageCanvasGroup.gameObject
            : m_waveStartMessageText != null ? m_waveStartMessageText.gameObject : null;

        if (root != null && root.activeSelf != visible)
        {
            root.SetActive(visible);
        }
    }

    /// <summary>웨이브 시작 알림의 알파값을 CanvasGroup 또는 텍스트에 반영합니다.</summary>
    private void SetWaveStartMessageAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (m_waveStartMessageCanvasGroup != null)
        {
            m_waveStartMessageCanvasGroup.alpha = alpha;
            return;
        }

        if (m_waveStartMessageText != null)
        {
            Color color = m_waveStartMessageText.color;
            color.a = alpha;
            m_waveStartMessageText.color = color;
        }
    }

    private void OnValidate()
    {
        m_roundDuration = Mathf.Max(0.01f, m_roundDuration);
        m_restDuration = Mathf.Max(0.01f, m_restDuration);
        m_clearingPullDelay = Mathf.Max(0.0f, m_clearingPullDelay);
        m_clearingOffscreenDespawnDelay = Mathf.Max(0.0f, m_clearingOffscreenDespawnDelay);
        m_clearingForceEndDelay = Mathf.Max(0.0f, m_clearingForceEndDelay);
        m_waveStartMessageHoldDuration = Mathf.Max(0.0f, m_waveStartMessageHoldDuration);
        m_waveStartMessageFadeDuration = Mathf.Max(0.0f, m_waveStartMessageFadeDuration);
        m_emptySpaceHoldStartDuration = Mathf.Max(0.01f, m_emptySpaceHoldStartDuration);
    }
}
