using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VInspector;

/// <summary>
/// Defense 라운드의 플레이/휴식 상태와 스폰 포인트 활성화를 관리합니다.
/// </summary>
/// <remarks>
/// 라운드 진행 규칙과 UI가 읽을 런타임 카운트만 소유합니다. 적 생산 규칙은 각
/// <see cref="EnemySpawnPoint"/>와 <see cref="EnemySpawnEntrySO"/>가 소유합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseManager : MonoBehaviour
{
    [Header("Defense Round")]
    [Tooltip("Defense 게임을 시작한 뒤 스포너를 활성화해 둘 라운드 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_roundDuration = 60.0f;

    [Tooltip("라운드가 끝난 뒤 다음 라운드를 시작하기 전 스포너를 비활성화해 둘 휴식 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_restDuration = 10.0f;

    [Tooltip("방어전에 포함할 웨이브 수입니다. 마지막 웨이브가 끝나면 휴식으로 넘어가지 않고 귀환 구역을 생성합니다.")]
    [Min(1)]
    [SerializeField] private int m_totalWaveCount = 3;

    [Tooltip("라운드 시작/휴식 전환 때 켜고 끌 EnemySpawnPoint 목록입니다. 비어 있는 항목은 무시합니다.")]
    [SerializeField] private List<EnemySpawnPoint> m_spawnPoints = new List<EnemySpawnPoint>();

    [Header("Victory Return Zone")]
    [Tooltip("씬에 미리 배치해 둔 귀환 구역입니다. 평소에는 꺼 두고, 마지막 웨이브를 막으면 켭니다. 위치는 씬에서 이 오브젝트를 직접 옮겨 정합니다.")]
    [SerializeField] private GameObject m_returnPoint;

    [Tooltip("방어전 승리 상태를 기록할 필드 데이터 매니저입니다. 비워 두면 런타임에 찾습니다.")]
    [SerializeField] private FieldSceneDataManager m_fieldSceneDataManager;

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

    [Header("Defense Start Input")]
    [Tooltip("방어전 시작 전, 현재 스쿼드 조작 멤버가 상호작용 대상이 없는 곳에서 상호작용키를 홀드하면 방어전을 시작합니다.")]
    [SerializeField] private bool m_allowEmptySpaceHoldStart = true;

    [Tooltip("현재 스쿼드 조작 멤버가 허공 상호작용 홀드로 방어전을 시작하기까지 필요한 시간(초)입니다.")]
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

    /// <summary>현재 라운드 플레이 구간인지 여부입니다. false이면 휴식 구간입니다.</summary>
    private bool m_isPlaying;

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

    /// <summary>이 방어전에 지정된 총 웨이브 수입니다.</summary>
    public int TotalWaveCount => m_totalWaveCount;

    /// <summary>현재 라운드에 남은 시간(초)입니다. 플레이 구간이 아니면 0입니다.</summary>
    public float RoundTimer => IsPlaying ? m_roundTimer : 0.0f;

    /// <summary>현재 휴식에 남은 시간(초)입니다. 휴식 구간이 아니면 0입니다.</summary>
    public float RestTimer => m_isGameStarted && !m_isPlaying && !m_isVictoryReady ? m_restTimer : 0.0f;

    /// <summary>이 매니저가 제어하는 스폰 포인트 목록입니다.</summary>
    public IReadOnlyList<EnemySpawnPoint> SpawnPoints => m_spawnPoints;

    /// <summary>방어전 시작 전 허공 상호작용 홀드 진행도입니다.</summary>
    public float EmptySpaceHoldStartProgress => m_isGameStarted
        ? 0.0f
        : Mathf.Clamp01(m_emptySpaceHoldStartTimer / m_emptySpaceHoldStartDuration);

    private void Awake()
    {
        // 시작 전 상태는 조작을 막지 않되, 이 매니저가 소유한 적 생산만 확실히 차단합니다.
        m_isGameStarted = false;
        m_isPlaying = false;
        m_isVictoryReady = false;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        SetSpawnPointsEnabled(false);
        RefreshTimerText();
        HideWaveStartMessage();
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

        float remainingDelta = Time.deltaTime;
        while (remainingDelta > 0.0f)
        {
            if (m_isPlaying)
            {
                if (m_roundTimer > remainingDelta)
                {
                    m_roundTimer -= remainingDelta;
                    RefreshTimerText();
                    return;
                }

                remainingDelta -= m_roundTimer;
                m_roundTimer = 0.0f;
                if (m_currentWave >= m_totalWaveCount)
                {
                    BeginVictory();
                    return;
                }

                BeginRest();
            }
            else
            {
                if (m_restTimer > remainingDelta)
                {
                    m_restTimer -= remainingDelta;
                    RefreshTimerText();
                    return;
                }

                remainingDelta -= m_restTimer;
                m_restTimer = 0.0f;
                BeginRound();
            }
        }
    }

    /// <summary>Defense 게임을 시작하고 첫 웨이브 타이머를 시작합니다.</summary>
    /// <remarks>이미 실행 중이면 현재 웨이브와 휴식 카운트를 초기화하고 첫 웨이브부터 다시 시작합니다.</remarks>
    [ContextMenu("Start Defense")]
    public void StartDefense()
    {
        m_isGameStarted = true;
        m_isVictoryReady = false;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        SetReturnPointActive(false);
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
        m_isVictoryReady = false;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        SetReturnPointActive(false);
        SetSpawnPointsEnabled(false);
        RefreshTimerText();
        HideWaveStartMessage();
    }

    [Foldout("Debug")]
    [Button("라운드 스킵")]
    /// <summary>
    /// 지금 구간의 남은 시간을 무시하고 다음 구간으로 넘어갑니다.
    /// </summary>
    /// <remarks>
    /// 검증용입니다. 전투 중이면 휴식으로(마지막 웨이브였다면 승리로), 휴식 중이면 다음 전투로 넘어갑니다.
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
            if (m_currentWave >= m_totalWaveCount)
            {
                BeginVictory();
                return;
            }

            BeginRest();
            return;
        }

        m_restTimer = 0.0f;
        BeginRound();
    }

    [Foldout("Debug")]
    [Tooltip("켜면 라운드 스킵이 필드에 남은 적을 함께 치웁니다. 끄면 적을 둔 채로 구간만 넘깁니다. 휴식으로 넘어갔는데 지난 전투의 적이 돌아다니는 상태를 피하려면 켜 두세요.")]
    [SerializeField] private bool m_debugSkipClearsEnemies = true;

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
            EnemySpawnPoint spawnPoint = m_spawnPoints[i];
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
        m_roundTimer = Mathf.Max(0.01f, m_roundDuration);
        m_restTimer = 0.0f;
        SetSpawnPointsEnabled(true);
        RefreshTimerText();
        ShowWaveStartMessage();
    }

    /// <summary>플레이 라운드를 끝내고 휴식 구간을 시작합니다.</summary>
    private void BeginRest()
    {
        m_isPlaying = false;
        m_roundTimer = 0.0f;
        m_restTimer = Mathf.Max(0.01f, m_restDuration);
        SetSpawnPointsEnabled(false);
        RefreshTimerText();
        OnRestStarted?.Invoke();
    }

    /// <summary>마지막 웨이브를 완료하고, 정산 전 귀환 구역 진입 대기 상태로 전환합니다.</summary>
    private void BeginVictory()
    {
        m_isPlaying = false;
        m_isVictoryReady = true;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        SetSpawnPointsEnabled(false);
        RefreshTimerText();
        HideWaveStartMessage();

        ResolveFieldSceneDataManager()?.SetMissionCompleted(true);
        SetReturnPointActive(true);
        OnDefenseVictoryReady?.Invoke();
    }

    /// <summary>할당된 모든 스폰 포인트의 신규 생산 허용 상태를 바꿉니다.</summary>
    private void SetSpawnPointsEnabled(bool isEnabled)
    {
        if (m_spawnPoints == null)
        {
            return;
        }

        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            m_spawnPoints[i]?.SetSpawnEnabled(isEnabled);
        }
    }

    /// <summary>
    /// 임시 시작 규칙입니다. 플레이어가 상호작용 대상으로 조준하지 않은 상태에서만
    /// 상호작용키를 일정 시간 유지하면 <see cref="StartDefense"/>를 호출합니다.
    /// </summary>
    private void UpdateEmptySpaceHoldStart()
    {
        if (!m_allowEmptySpaceHoldStart)
        {
            m_emptySpaceHoldStartTimer = 0.0f;
            return;
        }

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

    /// <summary>승리 상태와 동일한 필드 데이터 매니저 참조를 확보합니다.</summary>
    private FieldSceneDataManager ResolveFieldSceneDataManager()
    {
        if (m_fieldSceneDataManager == null)
        {
            m_fieldSceneDataManager = FindFirstObjectByType<FieldSceneDataManager>();
        }

        return m_fieldSceneDataManager;
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

        m_phaseLabelText.text = m_isPlaying ? m_combatPhaseLabel : m_restPhaseLabel;
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

        int remaining = Mathf.Max(0, m_totalWaveCount - m_currentWave + (m_isPlaying ? 1 : 0));
        m_remainingWaveText.text = string.Format(m_remainingWaveFormat, remaining, m_totalWaveCount);
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
        m_totalWaveCount = Mathf.Max(1, m_totalWaveCount);
        m_waveStartMessageHoldDuration = Mathf.Max(0.0f, m_waveStartMessageHoldDuration);
        m_waveStartMessageFadeDuration = Mathf.Max(0.0f, m_waveStartMessageFadeDuration);
        m_emptySpaceHoldStartDuration = Mathf.Max(0.01f, m_emptySpaceHoldStartDuration);
    }
}
