using UnityEngine;

/// <summary>무기 행동 타이밍에 맞춰 사운드·머즐·트레이서·탄피 피드백을 출력합니다.</summary>
/// <remarks>사격 가능 여부와 타이밍은 <see cref="Gun"/>이 결정하며, 이 컴포넌트는 전달받은 결과를 표현하기만 합니다.</remarks>
[DisallowMultipleComponent]
public sealed class WeaponFeedbackEmitter : MonoBehaviour
{
    [Tooltip("무기 피드백 효과음을 재생할 전용 3D AudioSource입니다. 비어 있으면 런타임에 자식 오브젝트로 생성합니다.")]
    [SerializeField] private AudioSource m_audioSource;

    [Tooltip("무기 피드백 효과음의 기본 음량입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_volume = 1.0f;

    [Tooltip("매 재생마다 적용할 무작위 피치 편차입니다. 0이면 원본 피치로 재생합니다.")]
    [Range(0.0f, 0.25f)]
    [SerializeField] private float m_pitchVariation = 0.03f;

    [Tooltip("무기 효과음이 최대 크기로 들리는 거리(m)입니다.")]
    [SerializeField] private float m_minDistance = 1.0f;

    [Tooltip("무기 효과음이 감쇠되어 들리는 최대 거리(m)입니다.")]
    [SerializeField] private float m_maxDistance = 55.0f;

    private int m_lastShotIndex = -1;
    private int m_lastDryFireIndex = -1;
    private int m_lastReloadIndex = -1;

    private void Awake()
    {
        EnsureAudioSource();
    }

    /// <summary>성공한 한 발의 사운드와 시각 피드백을 출력합니다.</summary>
    public void PlayShot(
        WeaponFeedbackSO feedback,
        Transform muzzleSocket,
        Transform shellSocket,
        Vector3 tracerStart,
        Vector3 tracerEnd)
    {
        if (feedback == null)
        {
            return;
        }

        PlayLocal(feedback.ShotSounds, ref m_lastShotIndex);

        if (muzzleSocket != null)
        {
            FeedbackPlaybackUtility.SpawnPrefab(
                feedback.MuzzleEffectPrefab,
                muzzleSocket.position,
                muzzleSocket.rotation,
                feedback.MuzzleEffectLifetime,
                muzzleSocket);
        }

        FeedbackPlaybackUtility.SpawnTracer(
            feedback.TracerEffectPrefab,
            tracerStart,
            tracerEnd,
            feedback.TracerEffectLifetime);

        if (shellSocket != null)
        {
            FeedbackPlaybackUtility.SpawnPrefab(
                feedback.ShellPrefab,
                shellSocket.position,
                shellSocket.rotation,
                feedback.ShellLifetime);
        }
    }

    /// <summary>사격이 탄약 부족 등으로 막힌 순간의 드라이 사운드를 출력합니다.</summary>
    public void PlayDryFire(WeaponFeedbackSO feedback)
    {
        if (feedback != null)
        {
            PlayLocal(feedback.DryFireSounds, ref m_lastDryFireIndex);
        }
    }

    /// <summary>재장전 시작 사운드를 출력합니다.</summary>
    public void PlayReload(WeaponFeedbackSO feedback)
    {
        if (feedback != null)
        {
            PlayLocal(feedback.ReloadSounds, ref m_lastReloadIndex);
        }
    }

    private void PlayLocal(System.Collections.Generic.IReadOnlyList<AudioClip> clips, ref int lastIndex)
    {
        AudioSource source = EnsureAudioSource();
        if (source == null || !FeedbackPlaybackUtility.TryPickClip(clips, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        source.pitch = 1.0f + Random.Range(-m_pitchVariation, m_pitchVariation);
        source.PlayOneShot(clip, m_volume);
    }

    private AudioSource EnsureAudioSource()
    {
        if (m_audioSource == null)
        {
            GameObject sourceObject = new GameObject("Weapon Feedback Audio");
            sourceObject.transform.SetParent(transform, false);
            m_audioSource = sourceObject.AddComponent<AudioSource>();
        }

        m_audioSource.playOnAwake = false;
        m_audioSource.loop = false;
        m_audioSource.spatialBlend = 1.0f;
        m_audioSource.dopplerLevel = 0.0f;
        m_audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        m_audioSource.minDistance = Mathf.Max(0.01f, m_minDistance);
        m_audioSource.maxDistance = Mathf.Max(m_audioSource.minDistance, m_maxDistance);

        FieldAudioSystem fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioFeedback : null;
        if (fieldAudio != null && fieldAudio.OutputMixerGroup != null)
        {
            m_audioSource.outputAudioMixerGroup = fieldAudio.OutputMixerGroup;
        }

        return m_audioSource;
    }
}
