using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 방어전 공격로(터널·다리) 위치를 화면 마커(Battle Mark)로 알려 줍니다. 휴식 중에는 노란색으로 보이고,
/// 공세 시작 몇 초 전부터 노랑↔빨강으로 점점 빠르게 깜빡이며, 공세가 시작되면 빨간색으로 잠시 멈춰 있다가 사라지고,
/// 다음 휴식에 노란색으로 다시 나타납니다.
/// </summary>
/// <remarks>
/// 위치 계산과 화면 밖 방향 화살표는 셸터의 방어전 NPC 안내와 같은 <see cref="ObjectiveIndicatorController"/>가 맡고,
/// 이 컴포넌트는 표시 여부, 투명도, 색만 정합니다. 레이아웃(마커 이미지, 색 마스크, 화살표)은 씬에 미리 배치돼 있습니다.
///
/// 다음 공세에 이 공격로로 적이 나오지 않으면(방어전 SO에서 그 웨이브를 쉬면) 마커를 띄우지 않습니다.
/// 다음 웨이브 번호는 <see cref="DefenseManager.CurrentWave"/>(지금까지 시작한 웨이브 수)를 0부터 센 값으로 씁니다.
/// 튜토리얼처럼 시간이 멈춘 동안에도 나타나고 사라지도록 실제 시간으로 셉니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseRouteMarker : MonoBehaviour
{
    [Header("References")]
    [Tooltip("마커 위치와 화면 밖 화살표를 그리는 안내 컴포넌트입니다. 비어 있으면 이 오브젝트에서 찾습니다.")]
    [SerializeField] private ObjectiveIndicatorController m_indicator;

    [Tooltip("이 마커가 알리는 공격로의 스폰 지점입니다. 하나라도 다음 공세에 쓰이면 마커를 띄웁니다.")]
    [SerializeField] private EnemyDefenseSpawnPoint[] m_spawnPoints = new EnemyDefenseSpawnPoint[0];

    [Tooltip("마커와 화살표 전체의 투명도를 조절할 CanvasGroup입니다.")]
    [SerializeField] private CanvasGroup m_fadeGroup;

    [Tooltip("색을 바꿀 이미지입니다(Battle Mark의 색 마스크, 화면 밖 화살표 등).")]
    [SerializeField] private Graphic[] m_tintTargets = new Graphic[0];

    [Tooltip("공세 진행 상태를 읽을 DefenseManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseManager m_defenseManager;

    [Header("Color")]
    [Tooltip("휴식 중 기본 색입니다(Battle Mark UI_1의 노란 테두리).")]
    [SerializeField] private Color m_idleColor = new Color32(253, 186, 1, 255);

    [Tooltip("깜빡일 때 오가는 색입니다(Battle Mark UI_2의 빨간 테두리).")]
    [SerializeField] private Color m_alertColor = new Color32(255, 18, 0, 255);

    [Header("Timing")]
    [Tooltip("공세 시작 몇 초 전부터 깜빡일지입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_alertLeadSeconds = 5.0f;

    [Tooltip("깜빡이기 시작할 때 노랑→빨강→노랑 한 번에 걸리는 시간(초)입니다. 공세가 다가올수록 아래 값까지 빨라집니다.")]
    [Min(0.05f)]
    [FormerlySerializedAs("m_blinkPeriod")]
    [SerializeField] private float m_blinkPeriodStart = 0.8f;

    [Tooltip("공세 직전(남은 0초)의 깜빡임 한 번 시간(초)입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_blinkPeriodEnd = 0.15f;

    [Tooltip("공세가 시작된 뒤 빨간색으로 멈춰 있다가 사라지기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_combatHoldSeconds = 2.0f;

    [Tooltip("나타나고 사라지는 데 걸리는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_fadeDuration = 0.5f;

    private float m_alpha;

    /// <summary>깜빡임 진행 위치입니다. 주기가 바뀌어도 색이 튀지 않도록 시간 대신 위상을 누적합니다.</summary>
    private float m_blinkPhase;

    /// <summary>공세 시작 뒤 빨간색으로 남겨 둘 남은 시간(초)입니다.</summary>
    private float m_combatHoldRemaining;

    /// <summary>지난 프레임에 휴식 표시 중이었는지 여부입니다. 공세 시작 순간을 알아내려고 둡니다.</summary>
    private bool m_wasShowing;

    private bool m_indicatorVisible;

    private void Awake()
    {
        if (m_indicator == null)
        {
            m_indicator = GetComponent<ObjectiveIndicatorController>();
        }

        if (m_defenseManager == null)
        {
            m_defenseManager = FindFirstObjectByType<DefenseManager>();
        }

        m_alpha = 0.0f;
        ApplyAlpha();
        ApplyColor(m_idleColor);
        SetIndicatorVisible(false);
    }

    private void Update()
    {
        bool show = ShouldShow();
        float deltaTime = Time.unscaledDeltaTime;

        // 휴식 표시 중에 공세가 시작되면 깜빡임을 끝내고 빨간색으로 잠시 둔 뒤 사라집니다.
        if (m_wasShowing && !show && m_defenseManager != null && m_defenseManager.IsPlaying)
        {
            m_combatHoldRemaining = m_combatHoldSeconds;
            m_blinkPhase = 0.0f;
        }

        m_wasShowing = show;
        bool holdingAlert = !show && m_combatHoldRemaining > 0.0f;
        if (holdingAlert)
        {
            m_combatHoldRemaining -= deltaTime;
        }

        bool visible = show || holdingAlert;
        m_alpha = Mathf.MoveTowards(m_alpha, visible ? 1.0f : 0.0f, deltaTime / m_fadeDuration);
        ApplyAlpha();
        SetIndicatorVisible(m_alpha > 0.0f);

        if (holdingAlert)
        {
            ApplyColor(m_alertColor);
            return;
        }

        // 사라지는 동안에는 마지막 색(빨강)을 그대로 둡니다.
        if (!show)
        {
            return;
        }

        if (IsAlertPhase(out float remaining))
        {
            // 남은 시간이 줄수록 주기를 줄입니다. 위상을 누적하므로 주기가 바뀌어도 색이 끊기지 않습니다.
            float closeness = 1.0f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, m_alertLeadSeconds));
            float period = Mathf.Lerp(m_blinkPeriodStart, m_blinkPeriodEnd, closeness);
            m_blinkPhase += deltaTime * 2.0f / period;
            float t = Mathf.PingPong(m_blinkPhase, 1.0f);
            ApplyColor(Color.Lerp(m_idleColor, m_alertColor, Mathf.SmoothStep(0.0f, 1.0f, t)));
        }
        else
        {
            m_blinkPhase = 0.0f;
            ApplyColor(m_idleColor);
        }
    }

    /// <summary>휴식(정비) 구간이고, 다음 공세에 이 공격로가 쓰이면 true입니다.</summary>
    private bool ShouldShow()
    {
        if (m_defenseManager == null || !m_defenseManager.IsResting)
        {
            return false;
        }

        return IsRouteUsedInNextWave();
    }

    /// <summary>공세 시작까지 남은 정비 시간이 깜빡임 시작 시간 이하인지 여부입니다. 시작 전(타이머가 돌지 않을 때)은 false입니다.</summary>
    private bool IsAlertPhase(out float remaining)
    {
        remaining = m_defenseManager.RestTimer;
        if (!m_defenseManager.IsGameStarted)
        {
            return false;
        }

        return remaining > 0.0f && remaining <= m_alertLeadSeconds;
    }

    private bool IsRouteUsedInNextWave()
    {
        DefenseStageSO stage = m_defenseManager.DefenseStage;
        if (stage == null)
        {
            // 방어전 구성을 아직 모르면(씬 단독 실행 직후 등) 공격로가 쓰인다고 보고 띄웁니다.
            return true;
        }

        int nextWaveIndex = m_defenseManager.CurrentWave;
        for (int i = 0; i < m_spawnPoints.Length; i++)
        {
            EnemyDefenseSpawnPoint spawnPoint = m_spawnPoints[i];
            if (spawnPoint != null && stage.GetWave(spawnPoint.RouteId, nextWaveIndex) != null)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyAlpha()
    {
        if (m_fadeGroup != null)
        {
            m_fadeGroup.alpha = m_alpha;
        }
    }

    private void ApplyColor(Color color)
    {
        for (int i = 0; i < m_tintTargets.Length; i++)
        {
            if (m_tintTargets[i] != null)
            {
                m_tintTargets[i].color = color;
            }
        }
    }

    private void SetIndicatorVisible(bool visible)
    {
        if (m_indicator == null || m_indicatorVisible == visible)
        {
            return;
        }

        m_indicatorVisible = visible;
        m_indicator.SetVisible(visible);
    }
}
