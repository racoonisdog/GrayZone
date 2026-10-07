using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>
/// 방어전 감염체 보컬의 우선순위, 동시 재생 수, 군중음을 중앙에서 관리합니다.
/// </summary>
/// <remarks>
/// 씬에 미리 배치할 수도 있고, 첫 감염체가 활성화될 때 없으면 런타임에 자동 생성됩니다.
/// 방어전 웨이브 로직은 수정하지 않고 <see cref="DefenseManager"/>의 공개 상태만 읽습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemyVocalDirector : MonoBehaviour
{
    private const string IndividualGrowlEventPath = "event:/Character/Enemy/Vocal/IndividualGrowl";
    private const string HordeWallaEventPath = "event:/Character/Enemy/Vocal/HordeWalla";

    private sealed class ActiveIndividualVoice
    {
        public EnemyVocalEmitter Source;
        public EventInstance Instance;
    }

    private static EnemyVocalDirector s_instance;
    private static bool s_loggedMissingIndividualEvent;
    private static bool s_loggedMissingWallaEvent;

    [Header("Selection")]
    [Tooltip("보컬 후보와 재생 종료 상태를 다시 계산하는 간격입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_evaluationInterval = 0.2f;

    [Tooltip("일반 감염체 개체 보컬의 최대 동시 재생 수입니다.")]
    [Min(1)]
    [SerializeField] private int m_maxIndividualVoices = 2;

    [Tooltip("개체 보컬을 재생할 최대 청취 거리입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_individualMaximumDistance = 18.0f;

    [Tooltip("하나의 감염체가 다시 개체 보컬 후보가 되기까지의 무작위 대기 범위입니다.")]
    [SerializeField] private Vector2 m_individualCooldownRange = new Vector2(6.0f, 10.0f);

    [Tooltip("서로 다른 개체 보컬 시작 사이의 무작위 대기 범위입니다.")]
    [SerializeField] private Vector2 m_globalIndividualGapRange = new Vector2(0.8f, 1.2f);

    [Header("Horde Walla")]
    [Tooltip("이 수 이상의 중거리 접근 적이 있을 때 군중음을 사용할 수 있습니다.")]
    [Min(1)]
    [SerializeField] private int m_wallaMinimumEnemyCount = 4;

    [Tooltip("군중음 집계에 포함할 최소 거리입니다. 가까운 적은 개체 보컬이 담당합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_wallaMinimumDistance = 18.0f;

    [Tooltip("군중음 집계에 포함할 최대 거리입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_wallaMaximumDistance = 45.0f;

    [Tooltip("군중음 한 번이 끝난 뒤 다음 재생을 허용하기까지의 무작위 대기 범위입니다.")]
    [SerializeField] private Vector2 m_wallaGapRange = new Vector2(2.0f, 5.0f);

    [Header("Mix")]
    [Tooltip("개체 그로울 선형 볼륨입니다. 0.8은 약 -2 dB입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_individualVolume = 0.8f;

    [Tooltip("군중음 선형 볼륨입니다. 0.4는 약 -8 dB입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_wallaVolume = 0.4f;

    [Tooltip("개체음에 적용할 최대 피치 변화량입니다. 단위는 semitone입니다.")]
    [Range(0.0f, 2.0f)]
    [SerializeField] private float m_individualPitchVariationSemitones = 1.0f;

    private readonly List<EnemyVocalEmitter> m_emitters = new();
    private readonly List<ActiveIndividualVoice> m_activeIndividualVoices = new();

    private DefenseManager m_defenseManager;
    private Transform m_listener;
    private Transform m_wallaAnchor;
    private EventInstance m_wallaInstance;
    private float m_nextEvaluationTime;
    private float m_nextIndividualVoiceTime;
    private float m_nextWallaTime;

    internal static void Register(EnemyVocalEmitter emitter)
    {
        if (emitter == null)
        {
            return;
        }

        GetOrCreate().RegisterInternal(emitter);
    }

    internal static void Unregister(EnemyVocalEmitter emitter)
    {
        if (s_instance != null)
        {
            s_instance.UnregisterInternal(emitter);
        }
    }

    private static EnemyVocalDirector GetOrCreate()
    {
        if (s_instance != null)
        {
            return s_instance;
        }

        s_instance = FindFirstObjectByType<EnemyVocalDirector>();
        if (s_instance != null)
        {
            return s_instance;
        }

        GameObject directorObject = new GameObject("Enemy Vocal Director");
        return directorObject.AddComponent<EnemyVocalDirector>();
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(this);
            return;
        }

        s_instance = this;
        m_defenseManager = DefenseManager.Instance != null
            ? DefenseManager.Instance
            : FindFirstObjectByType<DefenseManager>();

        GameObject anchorObject = new GameObject("Horde Walla Anchor");
        m_wallaAnchor = anchorObject.transform;
        m_wallaAnchor.SetParent(transform, false);
        m_nextWallaTime = Time.time + Random.Range(0.5f, 1.5f);
    }

    private void Update()
    {
        RefreshFinishedVoices();

        if (Time.time < m_nextEvaluationTime)
        {
            return;
        }

        m_nextEvaluationTime = Time.time + Mathf.Max(0.05f, m_evaluationInterval);

        if (!IsVocalPhaseActive() || !FMODUnity.RuntimeManager.IsInitialized)
        {
            StopAllVoices();
            return;
        }

        if (!TryResolveListener(out Vector3 listenerPosition))
        {
            return;
        }

        TryStartIndividualVoice(listenerPosition);
        EvaluateWalla(listenerPosition);
    }

    private void OnDestroy()
    {
        StopAllVoices();
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void RegisterInternal(EnemyVocalEmitter emitter)
    {
        if (!m_emitters.Contains(emitter))
        {
            m_emitters.Add(emitter);
        }
    }

    private void UnregisterInternal(EnemyVocalEmitter emitter)
    {
        m_emitters.Remove(emitter);

        for (int i = m_activeIndividualVoices.Count - 1; i >= 0; --i)
        {
            ActiveIndividualVoice voice = m_activeIndividualVoices[i];
            if (voice.Source != emitter)
            {
                continue;
            }

            StopAndRelease(ref voice.Instance, FMOD.Studio.STOP_MODE.IMMEDIATE);
            m_activeIndividualVoices.RemoveAt(i);
        }

        if (emitter != null)
        {
            emitter.HasActiveIndividualVoice = false;
        }
    }

    private bool IsVocalPhaseActive()
    {
        if (m_defenseManager == null)
        {
            m_defenseManager = DefenseManager.Instance != null
                ? DefenseManager.Instance
                : FindFirstObjectByType<DefenseManager>();
        }

        return m_defenseManager != null
            && (m_defenseManager.IsPlaying || m_defenseManager.IsClearing);
    }

    private bool TryResolveListener(out Vector3 listenerPosition)
    {
        if (m_listener == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                // 일부 방어전 씬에는 Unity AudioListener만 있고 FMOD Studio Listener가 없습니다.
                // 씬 파일을 수정하지 않고 런타임에 보완해 3D 보컬 위치가 카메라를 기준으로 계산되게 합니다.
                if (!mainCamera.TryGetComponent(out FMODUnity.StudioListener _))
                {
                    mainCamera.gameObject.AddComponent<FMODUnity.StudioListener>();
                }

                m_listener = mainCamera.transform;
            }
        }

        listenerPosition = m_listener != null ? m_listener.position : Vector3.zero;
        return m_listener != null;
    }

    private void TryStartIndividualVoice(Vector3 listenerPosition)
    {
        if (m_activeIndividualVoices.Count >= Mathf.Max(1, m_maxIndividualVoices)
            || Time.time < m_nextIndividualVoiceTime)
        {
            return;
        }

        EnemyVocalEmitter best = null;
        float bestScore = float.NegativeInfinity;
        float maximumDistance = Mathf.Max(1.0f, m_individualMaximumDistance);

        for (int i = m_emitters.Count - 1; i >= 0; --i)
        {
            EnemyVocalEmitter emitter = m_emitters[i];
            if (emitter == null)
            {
                m_emitters.RemoveAt(i);
                continue;
            }

            if (emitter.HasActiveIndividualVoice
                || Time.time < emitter.NextIndividualVoiceTime
                || !emitter.TryGetMovingCandidate(out Vector3 position, out float moveSpeed))
            {
                continue;
            }

            float distance = Vector3.Distance(listenerPosition, position);
            if (distance > maximumDistance)
            {
                continue;
            }

            float combatBonus = emitter.Controller != null
                && emitter.Controller.Current == emitter.Controller.Combat
                ? 8.0f
                : 0.0f;
            float score = (maximumDistance - distance) * 4.0f + moveSpeed * 2.0f + combatBonus;
            if (score > bestScore)
            {
                bestScore = score;
                best = emitter;
            }
        }

        if (best != null && PlayIndividualVoice(best))
        {
            best.NextIndividualVoiceTime = Time.time + SampleRange(m_individualCooldownRange, 6.0f, 10.0f);
            m_nextIndividualVoiceTime = Time.time + SampleRange(m_globalIndividualGapRange, 0.8f, 1.2f);
        }
    }

    private bool PlayIndividualVoice(EnemyVocalEmitter emitter)
    {
        try
        {
            EventInstance instance = FMODUnity.RuntimeManager.CreateInstance(IndividualGrowlEventPath);
            if (!instance.isValid())
            {
                return false;
            }

            FMODUnity.RuntimeManager.AttachInstanceToGameObject(instance, emitter.gameObject);
            instance.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, 2.0f);
            instance.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, 25.0f);
            instance.setVolume(m_individualVolume);

            float semitones = Random.Range(
                -m_individualPitchVariationSemitones,
                m_individualPitchVariationSemitones);
            instance.setPitch(Mathf.Pow(2.0f, semitones / 12.0f));

            if (instance.start() != FMOD.RESULT.OK)
            {
                instance.release();
                instance.clearHandle();
                return false;
            }

            emitter.HasActiveIndividualVoice = true;
            m_activeIndividualVoices.Add(new ActiveIndividualVoice
            {
                Source = emitter,
                Instance = instance,
            });
            return true;
        }
        catch (EventNotFoundException exception)
        {
            if (!s_loggedMissingIndividualEvent)
            {
                s_loggedMissingIndividualEvent = true;
                Debug.LogWarning(
                    $"[EnemyVocalDirector] 개체 그로울 FMOD 이벤트를 찾지 못했습니다: {exception.Message}",
                    this);
            }

            return false;
        }
    }

    private void EvaluateWalla(Vector3 listenerPosition)
    {
        Vector3 centroid = Vector3.zero;
        int candidateCount = 0;
        float minimumDistance = Mathf.Max(0.0f, m_wallaMinimumDistance);
        float maximumDistance = Mathf.Max(minimumDistance + 1.0f, m_wallaMaximumDistance);

        for (int i = m_emitters.Count - 1; i >= 0; --i)
        {
            EnemyVocalEmitter emitter = m_emitters[i];
            if (emitter == null)
            {
                m_emitters.RemoveAt(i);
                continue;
            }

            if (!emitter.TryGetMovingCandidate(out Vector3 position, out _))
            {
                continue;
            }

            float distance = Vector3.Distance(listenerPosition, position);
            if (distance < minimumDistance || distance > maximumDistance)
            {
                continue;
            }

            centroid += position;
            candidateCount++;
        }

        if (candidateCount < Mathf.Max(1, m_wallaMinimumEnemyCount))
        {
            return;
        }

        centroid /= candidateCount;
        if (m_wallaAnchor != null)
        {
            m_wallaAnchor.position = centroid;
        }

        if (!m_wallaInstance.isValid() && Time.time >= m_nextWallaTime)
        {
            StartWalla();
        }
    }

    private void StartWalla()
    {
        try
        {
            m_wallaInstance = FMODUnity.RuntimeManager.CreateInstance(HordeWallaEventPath);
            if (!m_wallaInstance.isValid())
            {
                return;
            }

            FMODUnity.RuntimeManager.AttachInstanceToGameObject(m_wallaInstance, m_wallaAnchor.gameObject);
            m_wallaInstance.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, 3.0f);
            m_wallaInstance.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, 45.0f);
            m_wallaInstance.setVolume(m_wallaVolume);

            if (m_wallaInstance.start() != FMOD.RESULT.OK)
            {
                m_wallaInstance.release();
                m_wallaInstance.clearHandle();
            }
        }
        catch (EventNotFoundException exception)
        {
            m_wallaInstance.clearHandle();
            if (!s_loggedMissingWallaEvent)
            {
                s_loggedMissingWallaEvent = true;
                Debug.LogWarning(
                    $"[EnemyVocalDirector] 군중 그로울 FMOD 이벤트를 찾지 못했습니다: {exception.Message}",
                    this);
            }
        }
    }

    private void RefreshFinishedVoices()
    {
        for (int i = m_activeIndividualVoices.Count - 1; i >= 0; --i)
        {
            ActiveIndividualVoice voice = m_activeIndividualVoices[i];
            if (!HasStopped(voice.Instance))
            {
                continue;
            }

            if (voice.Source != null)
            {
                voice.Source.HasActiveIndividualVoice = false;
            }

            StopAndRelease(ref voice.Instance, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            m_activeIndividualVoices.RemoveAt(i);
        }

        if (m_wallaInstance.isValid() && HasStopped(m_wallaInstance))
        {
            StopAndRelease(ref m_wallaInstance, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            m_nextWallaTime = Time.time + SampleRange(m_wallaGapRange, 2.0f, 5.0f);
        }
    }

    private void StopAllVoices()
    {
        for (int i = m_activeIndividualVoices.Count - 1; i >= 0; --i)
        {
            ActiveIndividualVoice voice = m_activeIndividualVoices[i];
            if (voice.Source != null)
            {
                voice.Source.HasActiveIndividualVoice = false;
            }

            StopAndRelease(ref voice.Instance, FMOD.Studio.STOP_MODE.IMMEDIATE);
        }

        m_activeIndividualVoices.Clear();
        StopAndRelease(ref m_wallaInstance, FMOD.Studio.STOP_MODE.IMMEDIATE);
        m_nextWallaTime = Time.time + SampleRange(m_wallaGapRange, 2.0f, 5.0f);
    }

    private static bool HasStopped(EventInstance instance)
    {
        if (!instance.isValid())
        {
            return true;
        }

        return instance.getPlaybackState(out PLAYBACK_STATE state) != FMOD.RESULT.OK
            || state == PLAYBACK_STATE.STOPPED;
    }

    private static void StopAndRelease(ref EventInstance instance, FMOD.Studio.STOP_MODE stopMode)
    {
        if (!instance.isValid())
        {
            instance.clearHandle();
            return;
        }

        instance.stop(stopMode);
        instance.release();
        instance.clearHandle();
    }

    private static float SampleRange(Vector2 range, float fallbackMinimum, float fallbackMaximum)
    {
        float minimum = Mathf.Max(0.0f, Mathf.Min(range.x, range.y));
        float maximum = Mathf.Max(minimum, Mathf.Max(range.x, range.y));
        if (maximum <= 0.0f)
        {
            minimum = fallbackMinimum;
            maximum = fallbackMaximum;
        }

        return Random.Range(minimum, maximum);
    }
}
