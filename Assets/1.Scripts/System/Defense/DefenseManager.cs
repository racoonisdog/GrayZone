using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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
    [Tooltip("귀환 구역을 배치할 기준점입니다. 비어 있으면 현재 DefenseEventHealth 거점을 사용합니다.")]
    [SerializeField] private Transform m_returnPointAnchor;

    [Tooltip("기준점의 로컬 좌표계에서 귀환 구역을 생성할 위치 오프셋입니다. z 양수는 거점 앞 방향입니다.")]
    [SerializeField] private Vector3 m_returnPointLocalOffset = new Vector3(0.0f, 0.0f, 3.0f);

    [Tooltip("생성할 귀환 구역의 트리거 크기입니다.")]
    [SerializeField] private Vector3 m_returnPointTriggerSize = new Vector3(4.0f, 2.0f, 4.0f);

    [Tooltip("선택한 프리팹이 있으면 이를 생성합니다. 비워 두면 EscapeSystem이 든 기본 트리거 구역을 런타임에 만듭니다.")]
    [SerializeField] private GameObject m_returnPointPrefab;

    [Tooltip("방어전 승리 상태를 기록할 필드 데이터 매니저입니다. 비워 두면 런타임에 찾습니다.")]
    [SerializeField] private FieldSceneDataManager m_fieldSceneDataManager;

    [Header("Defense HUD")]
    [Tooltip("전투 또는 휴식의 남은 시간을 분:초로 표시할 텍스트입니다. 비어 있으면 타이머 표시는 생략합니다.")]
    [SerializeField] private TMP_Text m_timerText;

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

    [Header("Temporary Start Input")]
    [Tooltip("방어전 시작 전, 상호작용 대상이 없는 곳에서 상호작용키를 홀드하면 방어전을 임시로 시작합니다.")]
    [SerializeField] private bool m_allowEmptySpaceHoldStart = true;

    [Tooltip("허공 상호작용 홀드로 방어전을 시작하기까지 필요한 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_emptySpaceHoldStartDuration = 3.0f;

    [Tooltip("임시 시작 입력을 읽을 플레이어 입력입니다. 비워 두면 활성 플레이어를 런타임에 찾습니다.")]
    [SerializeField] private PlayerInputController m_startInput;

    [Tooltip("허공 여부를 확인할 상호작용 컨트롤러입니다. 비워 두면 활성 플레이어의 컨트롤러를 런타임에 찾습니다.")]
    [SerializeField] private InteractionController m_startInteraction;

    /// <summary>Defense 게임이 시작될 때 발생합니다. 튜토리얼 안내처럼 시작 시점에 붙는 UI가 구독합니다.</summary>
    /// <remarks>
    /// <see cref="StartDefenseGame"/>를 다시 부르면 재시작으로 보고 매번 발생합니다.
    /// 구독자가 중복 표시를 원하지 않으면 자기 쪽에서 한 번만 처리하도록 판단합니다.
    /// </remarks>
    public event Action OnDefenseStarted;

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

    /// <summary>승리 후 런타임에 만든 귀환 구역 인스턴스입니다.</summary>
    private GameObject m_activeReturnPoint;

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
        DestroyActiveReturnPoint();
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
        DestroyActiveReturnPoint();
        SetSpawnPointsEnabled(false);
        RefreshTimerText();
        HideWaveStartMessage();
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
        CreateReturnPoint();
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

        ResolveStartInputReferences();
        if (m_startInput == null || !m_startInput.isActiveAndEnabled
            || (m_startInteraction != null && m_startInteraction.Current != null)
            || !m_startInput.Interact)
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

    /// <summary>Inspector 참조가 비어 있을 때 현재 조작 가능한 플레이어의 입력 컴포넌트를 찾습니다.</summary>
    private void ResolveStartInputReferences()
    {
        if (m_startInput == null || !m_startInput.isActiveAndEnabled)
        {
            m_startInput = FindFirstObjectByType<PlayerInputController>();
        }

        if (m_startInteraction == null || !m_startInteraction.isActiveAndEnabled)
        {
            m_startInteraction = m_startInput != null
                ? m_startInput.GetComponent<InteractionController>()
                : FindFirstObjectByType<InteractionController>();
        }
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

    /// <summary>승리 시 귀환 정산을 시작할 EscapeSystem 트리거 구역을 생성합니다.</summary>
    private void CreateReturnPoint()
    {
        DestroyActiveReturnPoint();

        Transform anchor = ResolveReturnPointAnchor();
        Vector3 position = anchor.TransformPoint(m_returnPointLocalOffset);
        Quaternion rotation = Quaternion.Euler(0.0f, anchor.eulerAngles.y, 0.0f);

        m_activeReturnPoint = m_returnPointPrefab != null
            ? Instantiate(m_returnPointPrefab, position, rotation)
            : CreateDefaultReturnPoint(position, rotation);

        EnsureReturnPointComponents(m_activeReturnPoint);
    }

    /// <summary>Inspector 기준점이 비어 있으면 현재 방어 거점을 귀환 구역 기준점으로 사용합니다.</summary>
    private Transform ResolveReturnPointAnchor()
    {
        if (m_returnPointAnchor != null)
        {
            return m_returnPointAnchor;
        }

        DefenseEventHealth defenseTarget = FindFirstObjectByType<DefenseEventHealth>();
        return defenseTarget != null ? defenseTarget.transform : transform;
    }

    /// <summary>별도 프리팹이 없을 때 기본 귀환 트리거를 구성합니다.</summary>
    private GameObject CreateDefaultReturnPoint(Vector3 position, Quaternion rotation)
    {
        GameObject returnPoint = new GameObject("Defense Return Zone");
        returnPoint.transform.SetPositionAndRotation(position, rotation);
        return returnPoint;
    }

    /// <summary>프리팹/기본 생성물 어느 쪽이든 탈출 정산에 필요한 최소 컴포넌트를 보장합니다.</summary>
    private void EnsureReturnPointComponents(GameObject returnPoint)
    {
        if (returnPoint == null)
        {
            return;
        }

        Collider trigger = returnPoint.GetComponentInChildren<Collider>();
        if (trigger == null)
        {
            BoxCollider boxCollider = returnPoint.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = m_returnPointTriggerSize;
        }
        else
        {
            trigger.isTrigger = true;
        }

        Rigidbody body = returnPoint.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = returnPoint.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;

        if (returnPoint.GetComponentInChildren<EscapeSystem>() == null)
        {
            returnPoint.AddComponent<EscapeSystem>();
        }
    }

    /// <summary>이 매니저가 이전에 만든 귀환 구역 인스턴스만 정리합니다.</summary>
    private void DestroyActiveReturnPoint()
    {
        if (m_activeReturnPoint == null)
        {
            return;
        }

        Destroy(m_activeReturnPoint);
        m_activeReturnPoint = null;
    }

    /// <summary>현재 전투 또는 휴식의 남은 시간을 분:초 형식으로 HUD에 반영합니다.</summary>
    private void RefreshTimerText()
    {
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
        m_returnPointTriggerSize.x = Mathf.Max(0.01f, m_returnPointTriggerSize.x);
        m_returnPointTriggerSize.y = Mathf.Max(0.01f, m_returnPointTriggerSize.y);
        m_returnPointTriggerSize.z = Mathf.Max(0.01f, m_returnPointTriggerSize.z);
    }
}
