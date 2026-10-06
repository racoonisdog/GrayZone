using FMODUnity;
using UnityEngine;

/// <summary>
/// Defense 진행 상태를 FMOD 셸터 방어 음악의 스템 구성으로 변환합니다.
/// </summary>
/// <remarks>
/// 웨이브 진행은 <see cref="DefenseManager"/>가 계속 소유합니다. 이 컴포넌트는 공개 상태를 읽기만 하며,
/// Synth는 항상 유지하고 Percussion, Strings, Guitar 파라미터를 한 마디 동안 함께 보간합니다.
/// 정리 구간은 직전 전투의 구성을 유지합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(DefenseManager))]
public sealed class DefenseMusicController : MonoBehaviour
{
    private const string PercussionParameter = "Percussion";
    private const string StringsParameter = "Strings";
    private const string GuitarParameter = "Guitar";

    private enum MusicPhase
    {
        InitialMaintenance,
        FirstCombat,
        MidMaintenance,
        ReinforcedCombat,
        Victory,
        Defeat,
    }

    [Header("References")]
    [SerializeField] private DefenseManager m_defenseManager;

    [SerializeField] private EventReference m_mainLoopEvent;

    [SerializeField] private EventReference m_victoryResolveEvent;

    [Header("Transition")]
    [Tooltip("100 BPM, 4/4 한 마디 길이입니다. 스템을 이 시간 동안 함께 전환합니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_layerTransitionDuration = 2.4f;

    [Tooltip("승리 시 현재 루프의 다음 마디 경계까지 기다릴 때 사용하는 길이(ms)입니다.")]
    [Min(1)]
    [SerializeField] private int m_barLengthMilliseconds = 2400;

    [Tooltip("패배 시 Resolve 없이 전체 음악을 빠르게 줄이는 시간입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_defeatFadeDuration = 0.5f;

    private FMOD.Studio.EventInstance m_mainLoopInstance;
    private FMOD.Studio.EventInstance m_resolveInstance;

    private bool m_hasObservedPhase;
    private MusicPhase m_observedPhase;
    private bool m_loggedMissingMainEvent;
    private bool m_loggedMissingResolveEvent;

    private float m_currentPercussion;
    private float m_currentStrings;
    private float m_currentGuitar;
    private float m_startPercussion;
    private float m_startStrings;
    private float m_startGuitar;
    private float m_targetPercussion;
    private float m_targetStrings;
    private float m_targetGuitar;
    private float m_layerTransitionElapsed;

    private bool m_victoryTransitionPending;
    private float m_victoryTransitionDelay;
    private bool m_defeatFadeActive;
    private float m_defeatFadeElapsed;

    private void Awake()
    {
        if (m_defenseManager == null)
        {
            m_defenseManager = GetComponent<DefenseManager>();
        }
    }

    private void Start()
    {
        if (m_defenseManager == null)
        {
            Debug.LogError("[DefenseMusicController] DefenseManager를 찾지 못해 음악 연동을 시작하지 않습니다.", this);
            enabled = false;
            return;
        }

        MusicPhase initialPhase = ResolvePhase();
        m_hasObservedPhase = true;
        m_observedPhase = initialPhase;
        EnterPhase(initialPhase, true);
    }

    private void Update()
    {
        ReleaseResolveWhenStopped();

        MusicPhase nextPhase = ResolvePhase();
        if (!m_hasObservedPhase || nextPhase != m_observedPhase)
        {
            m_hasObservedPhase = true;
            m_observedPhase = nextPhase;
            EnterPhase(nextPhase, false);
        }

        switch (m_observedPhase)
        {
            case MusicPhase.Victory:
                UpdateVictoryTransition();
                break;

            case MusicPhase.Defeat:
                UpdateDefeatFade();
                break;

            default:
                if (!m_mainLoopInstance.isValid())
                {
                    bool created = EnsureMainLoopStarted();
                    if (created)
                    {
                        SetLayerTarget(m_observedPhase, true);
                    }
                }

                UpdateLayerTransition();
                break;
        }
    }

    private void OnDestroy()
    {
        StopAndReleaseMainLoop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        StopAndReleaseResolve();
    }

    /// <summary>DefenseManager 공개 상태를 음악 구간으로 변환합니다.</summary>
    private MusicPhase ResolvePhase()
    {
        if (m_defenseManager.IsGameOver)
        {
            return MusicPhase.Defeat;
        }

        if (m_defenseManager.IsVictoryReady)
        {
            return MusicPhase.Victory;
        }

        if (m_defenseManager.IsPlaying || m_defenseManager.IsClearing)
        {
            return m_defenseManager.CurrentWave <= 1
                ? MusicPhase.FirstCombat
                : MusicPhase.ReinforcedCombat;
        }

        return m_defenseManager.CurrentWave <= 0
            ? MusicPhase.InitialMaintenance
            : MusicPhase.MidMaintenance;
    }

    /// <summary>새 음악 구간에 진입할 때 한 번만 필요한 처리를 합니다.</summary>
    private void EnterPhase(MusicPhase phase, bool snapLayers)
    {
        if (phase == MusicPhase.Victory)
        {
            m_defeatFadeActive = false;
            ScheduleVictoryTransition();
            return;
        }

        if (phase == MusicPhase.Defeat)
        {
            m_victoryTransitionPending = false;
            StopAndReleaseResolve();
            m_defeatFadeActive = true;
            m_defeatFadeElapsed = 0.0f;
            return;
        }

        m_victoryTransitionPending = false;
        m_defeatFadeActive = false;
        StopAndReleaseResolve();

        bool created = EnsureMainLoopStarted();
        if (m_mainLoopInstance.isValid())
        {
            m_mainLoopInstance.setVolume(1.0f);
        }

        SetLayerTarget(phase, snapLayers || created);
    }

    /// <summary>메인 루프를 한 번 만들고 재생합니다.</summary>
    /// <returns>이번 호출에서 새 인스턴스를 만들었으면 true입니다.</returns>
    private bool EnsureMainLoopStarted()
    {
        if (m_mainLoopInstance.isValid())
        {
            return false;
        }

        if (m_mainLoopEvent.IsNull || !RuntimeManager.IsInitialized)
        {
            if (m_mainLoopEvent.IsNull && !m_loggedMissingMainEvent)
            {
                m_loggedMissingMainEvent = true;
                Debug.LogWarning("[DefenseMusicController] 메인 FMOD 음악 이벤트가 지정되지 않았습니다.", this);
            }

            return false;
        }

        try
        {
            m_mainLoopInstance = RuntimeManager.CreateInstance(m_mainLoopEvent);
            if (!m_mainLoopInstance.isValid())
            {
                return false;
            }

            m_mainLoopInstance.setVolume(1.0f);
            m_mainLoopInstance.start();
            return true;
        }
        catch (EventNotFoundException exception)
        {
            if (!m_loggedMissingMainEvent)
            {
                m_loggedMissingMainEvent = true;
                Debug.LogWarning($"[DefenseMusicController] 메인 FMOD 음악 이벤트를 찾지 못했습니다: {exception.Message}", this);
            }

            return false;
        }
    }

    /// <summary>구간별 스템 목표값을 정합니다.</summary>
    private void SetLayerTarget(MusicPhase phase, bool snap)
    {
        float percussion;
        float strings;
        float guitar;

        switch (phase)
        {
            case MusicPhase.FirstCombat:
                percussion = 1.0f;
                strings = 1.0f;
                guitar = 0.0f;
                break;

            case MusicPhase.MidMaintenance:
                percussion = 1.0f;
                strings = 0.0f;
                guitar = 0.0f;
                break;

            case MusicPhase.ReinforcedCombat:
                percussion = 1.0f;
                strings = 1.0f;
                guitar = 1.0f;
                break;

            default:
                percussion = 0.0f;
                strings = 0.0f;
                guitar = 0.0f;
                break;
        }

        m_startPercussion = m_currentPercussion;
        m_startStrings = m_currentStrings;
        m_startGuitar = m_currentGuitar;
        m_targetPercussion = percussion;
        m_targetStrings = strings;
        m_targetGuitar = guitar;
        m_layerTransitionElapsed = 0.0f;

        if (snap)
        {
            m_currentPercussion = m_targetPercussion;
            m_currentStrings = m_targetStrings;
            m_currentGuitar = m_targetGuitar;
            ApplyLayerParameters();
        }
    }

    /// <summary>세 스템 파라미터를 같은 시간축에서 한 마디 동안 보간합니다.</summary>
    private void UpdateLayerTransition()
    {
        if (!m_mainLoopInstance.isValid())
        {
            return;
        }

        m_layerTransitionElapsed += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.01f, m_layerTransitionDuration);
        float t = Mathf.Clamp01(m_layerTransitionElapsed / duration);

        m_currentPercussion = Mathf.Lerp(m_startPercussion, m_targetPercussion, t);
        m_currentStrings = Mathf.Lerp(m_startStrings, m_targetStrings, t);
        m_currentGuitar = Mathf.Lerp(m_startGuitar, m_targetGuitar, t);
        ApplyLayerParameters();
    }

    /// <summary>현재 스템 값을 FMOD 메인 이벤트에 보냅니다.</summary>
    private void ApplyLayerParameters()
    {
        if (!m_mainLoopInstance.isValid())
        {
            return;
        }

        m_mainLoopInstance.setParameterByName(PercussionParameter, m_currentPercussion);
        m_mainLoopInstance.setParameterByName(StringsParameter, m_currentStrings);
        m_mainLoopInstance.setParameterByName(GuitarParameter, m_currentGuitar);
    }

    /// <summary>승리 Resolve를 현재 루프의 다음 100 BPM 마디 경계에 예약합니다.</summary>
    private void ScheduleVictoryTransition()
    {
        m_victoryTransitionPending = true;
        m_victoryTransitionDelay = 0.0f;

        if (!m_mainLoopInstance.isValid())
        {
            TriggerVictoryResolve();
            return;
        }

        if (m_mainLoopInstance.getTimelinePosition(out int timelinePosition) != FMOD.RESULT.OK)
        {
            TriggerVictoryResolve();
            return;
        }

        int barLength = Mathf.Max(1, m_barLengthMilliseconds);
        int positionInBar = timelinePosition % barLength;
        int remainingMilliseconds = positionInBar <= 50
            ? 0
            : barLength - positionInBar;
        m_victoryTransitionDelay = remainingMilliseconds * 0.001f;

        if (m_victoryTransitionDelay <= 0.0f)
        {
            TriggerVictoryResolve();
        }
    }

    private void UpdateVictoryTransition()
    {
        if (!m_victoryTransitionPending)
        {
            return;
        }

        m_victoryTransitionDelay -= Time.unscaledDeltaTime;
        if (m_victoryTransitionDelay <= 0.0f)
        {
            TriggerVictoryResolve();
        }
    }

    /// <summary>루프를 마디 경계에서 끊고 승리 Resolve 원샷을 시작합니다.</summary>
    private void TriggerVictoryResolve()
    {
        m_victoryTransitionPending = false;
        StopAndReleaseMainLoop(FMOD.Studio.STOP_MODE.IMMEDIATE);

        if (m_victoryResolveEvent.IsNull || !RuntimeManager.IsInitialized)
        {
            if (m_victoryResolveEvent.IsNull && !m_loggedMissingResolveEvent)
            {
                m_loggedMissingResolveEvent = true;
                Debug.LogWarning("[DefenseMusicController] 승리 Resolve FMOD 이벤트가 지정되지 않았습니다.", this);
            }

            return;
        }

        try
        {
            m_resolveInstance = RuntimeManager.CreateInstance(m_victoryResolveEvent);
            if (m_resolveInstance.isValid())
            {
                m_resolveInstance.start();
            }
        }
        catch (EventNotFoundException exception)
        {
            if (!m_loggedMissingResolveEvent)
            {
                m_loggedMissingResolveEvent = true;
                Debug.LogWarning($"[DefenseMusicController] 승리 Resolve FMOD 이벤트를 찾지 못했습니다: {exception.Message}", this);
            }
        }
    }

    /// <summary>패배 시 메인 루프 전체를 빠르게 줄인 뒤 정지합니다.</summary>
    private void UpdateDefeatFade()
    {
        if (!m_defeatFadeActive || !m_mainLoopInstance.isValid())
        {
            return;
        }

        m_defeatFadeElapsed += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.01f, m_defeatFadeDuration);
        float t = Mathf.Clamp01(m_defeatFadeElapsed / duration);
        m_mainLoopInstance.setVolume(1.0f - t);

        if (t >= 1.0f)
        {
            m_defeatFadeActive = false;
            StopAndReleaseMainLoop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        }
    }

    /// <summary>재생을 마친 Resolve 인스턴스를 해제합니다.</summary>
    private void ReleaseResolveWhenStopped()
    {
        if (!m_resolveInstance.isValid())
        {
            return;
        }

        if (m_resolveInstance.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state) == FMOD.RESULT.OK &&
            state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
        {
            m_resolveInstance.release();
            m_resolveInstance.clearHandle();
        }
    }

    private void StopAndReleaseMainLoop(FMOD.Studio.STOP_MODE stopMode)
    {
        if (!m_mainLoopInstance.isValid())
        {
            return;
        }

        m_mainLoopInstance.stop(stopMode);
        m_mainLoopInstance.release();
        m_mainLoopInstance.clearHandle();
    }

    private void StopAndReleaseResolve()
    {
        if (!m_resolveInstance.isValid())
        {
            return;
        }

        m_resolveInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        m_resolveInstance.release();
        m_resolveInstance.clearHandle();
    }
}
