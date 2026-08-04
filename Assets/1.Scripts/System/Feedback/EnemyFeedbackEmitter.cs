using System.Collections.Generic;
using UnityEngine;

/// <summary>감염체의 상태 진입과 피격·사망 사건을 공간 피드백으로 출력합니다.</summary>
[DisallowMultipleComponent]
public sealed class EnemyFeedbackEmitter : MonoBehaviour
{
    [Tooltip("감염체의 대기·경계·추적·공격 행동 사운드를 재생할 전용 3D AudioSource입니다. 비어 있으면 런타임에 생성합니다.")]
    [SerializeField] private AudioSource m_actionAudioSource;

    [Tooltip("감염체 행동 사운드의 기본 음량입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_volume = 1.0f;

    [Tooltip("매 행동 사운드에 적용할 무작위 피치 편차입니다.")]
    [Range(0.0f, 0.25f)]
    [SerializeField] private float m_pitchVariation = 0.04f;

    [Tooltip("감염체 행동 사운드가 최대 크기로 들리는 거리(m)입니다.")]
    [SerializeField] private float m_minDistance = 1.0f;

    [Tooltip("감염체 행동 사운드가 감쇠되어 들리는 최대 거리(m)입니다.")]
    [SerializeField] private float m_maxDistance = 30.0f;

    private int m_lastIdleIndex = -1;
    private int m_lastAlertIndex = -1;
    private int m_lastChaseIndex = -1;
    private int m_lastAttackIndex = -1;
    private int m_lastHitIndex = -1;
    private int m_lastDeathIndex = -1;

    private void Awake()
    {
        EnsureActionAudioSource();
    }

    /// <summary>배회 상태에 진입한 순간의 행동 사운드를 출력합니다.</summary>
    public void PlayIdle(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.IdleSounds : null, ref m_lastIdleIndex, false);

    /// <summary>교전 상태에 처음 진입한 순간의 경계 사운드를 출력합니다.</summary>
    public void PlayAlert(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.AlertSounds : null, ref m_lastAlertIndex, true);

    /// <summary>추적 상태에 진입한 순간의 행동 사운드를 출력합니다.</summary>
    public void PlayChase(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.ChaseSounds : null, ref m_lastChaseIndex, false);

    /// <summary>공격 동작을 시작한 순간의 행동 사운드를 출력합니다.</summary>
    public void PlayAttack(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.AttackSounds : null, ref m_lastAttackIndex, true);

    /// <summary>실제 피격 위치에서 피격 이펙트·사운드·혈흔을 출력합니다.</summary>
    public void PlayHit(EnemyFeedbackSO feedback, Vector3 point, Vector3 normal, Transform hitTransform)
    {
        if (feedback == null)
        {
            return;
        }

        FeedbackPlaybackUtility.SpawnAligned(
            feedback.HitEffectPrefab,
            point,
            normal,
            feedback.HitEffectLifetime,
            parent: hitTransform);
        FeedbackPlaybackUtility.SpawnAligned(
            feedback.BloodDecalPrefab,
            point,
            normal,
            feedback.BloodDecalLifetime,
            parent: hitTransform);

        PlayWorld(feedback.HitSounds, ref m_lastHitIndex, point);
    }

    /// <summary>사망 위치에서 사망 사운드를 독립 one-shot으로 출력합니다.</summary>
    public void PlayDeath(EnemyFeedbackSO feedback)
    {
        if (m_actionAudioSource != null)
        {
            m_actionAudioSource.Stop();
        }

        PlayWorld(feedback != null ? feedback.DeathSounds : null, ref m_lastDeathIndex, transform.position);
    }

    private void PlayLocal(IReadOnlyList<AudioClip> clips, ref int lastIndex, bool interruptCurrent)
    {
        AudioSource source = EnsureActionAudioSource();
        if (source == null || (source.isPlaying && !interruptCurrent))
        {
            return;
        }

        if (!FeedbackPlaybackUtility.TryPickClip(clips, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        if (interruptCurrent)
        {
            source.Stop();
        }

        source.pitch = 1.0f + Random.Range(-m_pitchVariation, m_pitchVariation);
        source.PlayOneShot(clip, m_volume);
    }

    private void PlayWorld(IReadOnlyList<AudioClip> clips, ref int lastIndex, Vector3 position)
    {
        if (!FeedbackPlaybackUtility.TryPickClip(clips, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        float pitch = 1.0f + Random.Range(-m_pitchVariation, m_pitchVariation);
        FieldAudioSystem fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioFeedback : null;
        if (fieldAudio != null && fieldAudio.PlayOneShotAt(clip, position, m_volume, pitch))
        {
            return;
        }

        AudioSource source = EnsureActionAudioSource();
        if (source != null)
        {
            source.pitch = pitch;
            source.PlayOneShot(clip, m_volume);
        }
    }

    private AudioSource EnsureActionAudioSource()
    {
        if (m_actionAudioSource == null)
        {
            GameObject sourceObject = new GameObject("Enemy Feedback Audio");
            sourceObject.transform.SetParent(transform, false);
            m_actionAudioSource = sourceObject.AddComponent<AudioSource>();
        }

        m_actionAudioSource.playOnAwake = false;
        m_actionAudioSource.loop = false;
        m_actionAudioSource.spatialBlend = 1.0f;
        m_actionAudioSource.dopplerLevel = 0.0f;
        m_actionAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        m_actionAudioSource.minDistance = Mathf.Max(0.01f, m_minDistance);
        m_actionAudioSource.maxDistance = Mathf.Max(m_actionAudioSource.minDistance, m_maxDistance);

        FieldAudioSystem fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioFeedback : null;
        if (fieldAudio != null && fieldAudio.OutputMixerGroup != null)
        {
            m_actionAudioSource.outputAudioMixerGroup = fieldAudio.OutputMixerGroup;
        }

        return m_actionAudioSource;
    }
}
