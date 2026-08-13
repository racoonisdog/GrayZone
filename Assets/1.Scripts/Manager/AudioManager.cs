using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 동시 발음 천장에 부딪혔을 때 어떤 소리를 먼저 지키고 어떤 소리를 먼저 생략할지 정하는 등급입니다.
/// </summary>
/// <remarks>
/// <para>
/// Unity는 <c>Project Settings > Audio > Real Voice Count</c>를 넘는 보이스를 가청도와
/// <see cref="AudioSource.priority"/>로 골라 가상화(무음화)합니다. 등급을 지정하지 않으면 모든 소리가
/// 기본값 128로 동률이 되어, 무엇이 씹히는지를 우리가 아니라 Unity가 임의로 정합니다.
/// </para>
/// <para>
/// 등급이 위에 있을수록 먼저 지킵니다. 아래 순서는 "씹히면 플레이어가 무엇을 잃는가"로 정했습니다.
/// 장식은 잃어도 되고, 조작 결과와 게임플레이 정보는 잃으면 안 됩니다.
/// </para>
/// </remarks>
public enum AudioPriorityClass
{
    /// <summary>배경 음악입니다. 루프라 한 번 끊기면 스스로 복구되지 않으므로 가장 먼저 지킵니다.</summary>
    Music = 0,

    /// <summary>플레이어가 직접 조작하는 멤버의 사격·장전·피격 사운드입니다. 씹히면 입력이 먹혔는지 알 수 없습니다.</summary>
    PlayerCritical = 1,

    /// <summary>동행 AI 멤버의 전투 사운드입니다. 아군 교전 상황 파악에 쓰이므로 플레이어 다음으로 지킵니다.</summary>
    AllyCombat = 2,

    /// <summary>감염체의 경계·사망처럼 게임플레이 정보를 담은 사운드입니다.</summary>
    EnemyCritical = 3,

    /// <summary>감염체의 배회·추적처럼 개체 수에 비례해 늘어나는 사운드입니다.</summary>
    EnemyAmbient = 4,

    /// <summary>표면 탄흔음처럼 장식에 해당하는 사운드입니다. 가장 먼저 생략합니다.</summary>
    SurfaceDecor = 5,
}

/// <summary>
/// 필드에서 발생하는 독립적인 3D one-shot 사운드의 AudioSource 풀과 동시 발음 상한을 관리합니다.
/// </summary>
/// <remarks>
/// <para>
/// 어떤 행동에서 어떤 소리를 낼지는 무기·감염체·표면 시스템이 결정합니다.
/// 이 컴포넌트는 전달받은 클립을 지정된 월드 위치에서 재생하는 공간 출력 정책만 담당합니다.
/// </para>
/// <para>
/// 여기에는 두 개의 서로 다른 상한이 걸립니다. 하나는 이 풀의 <c>최대 보이스</c>이고, 다른 하나는
/// Unity 자체의 <c>Real Voice Count</c>입니다. 후자를 넘기면 우리가 자르지 않아도 Unity가 알아서
/// 무음화하므로, 풀 상한만 관리하는 것으로는 "이 소리는 반드시 들려야 한다"를 보장할 수 없습니다.
/// 그래서 재생 요청마다 <see cref="AudioPriorityClass"/>를 받아 두 상한 모두에 같은 우선순위를 적용합니다.
/// </para>
/// <para>
/// 무기·감염체처럼 소리가 대상을 따라 움직여야 하거나 채널을 독점해야 하는 주체는 이 풀을 쓰지 않고
/// 자기 <see cref="AudioSource"/>를 가집니다(개인 사운드). 그 개수를 이 매니저에 등록하면
/// <c>개인 사운드 포함하기</c> 옵션이 공용 풀의 가용량을 그만큼 줄여, 씬 전체 동시 발음 총량이 잡힙니다.
/// <see cref="EffectPool"/>의 <c>개인 이펙트 포함하기</c>와 같은 회계 방식입니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    private sealed class Voice
    {
        public AudioSource Source;
        public float StartedAt;
        public AudioPriorityClass PriorityClass;
    }

    /// <summary>
    /// <see cref="AudioPriorityClass"/>를 <see cref="AudioSource.priority"/> 값으로 옮기는 표입니다.
    /// </summary>
    /// <remarks>
    /// Unity의 <c>priority</c>는 <b>작을수록 먼저 지켜집니다</b>(0이 최우선, 255가 최하, 기본값 128).
    /// 등급 순서와 방향이 같아 헷갈리기 쉬우므로 표로 고정해 둡니다. 등급 사이에 간격을 둔 것은
    /// 나중에 등급을 추가할 때 기존 값을 다시 매기지 않아도 되게 하기 위해서입니다.
    /// </remarks>
    private static readonly int[] s_unityPriorities =
    {
        0,   // Music
        32,  // PlayerCritical
        64,  // AllyCombat
        96,  // EnemyCritical
        160, // EnemyAmbient
        224, // SurfaceDecor
    };

    [Tooltip("동시에 유지할 필드 위치형 one-shot AudioSource의 최대 개수입니다. Project Settings의 Real Voice Count보다 낮게 두어 개인 사운드 몫을 남겨야 합니다.")]
    [SerializeField] private int m_maxWorldVoices = 32;

    [Tooltip("개인 사운드 포함하기: 무기·감염체처럼 자체 AudioSource를 가진 개인 사운드의 활성 개수를 위 최대치에 포함합니다. 켜면 개인 활성 개수만큼 공용 풀이 쓸 수 있는 수량이 줄어듭니다.")]
    [InspectorName("개인 사운드 포함하기")]
    [SerializeField] private bool m_includePersonalVoices = true;

    [Tooltip("위치형 one-shot 사운드가 최대 크기로 들리는 기본 거리(m)입니다.")]
    [SerializeField] private float m_minDistance = 1.0f;

    [Tooltip("위치형 one-shot 사운드가 감쇠되어 들리는 기본 최대 거리(m)입니다.")]
    [SerializeField] private float m_maxDistance = 40.0f;

    [Tooltip("필드 위치형 효과음을 보낼 AudioMixer 그룹입니다. 비어 있으면 AudioSource 기본 출력을 사용합니다.")]
    [SerializeField] private AudioMixerGroup m_outputMixerGroup;

    private readonly List<Voice> m_voices = new List<Voice>();

    /// <summary>자체 AudioSource로 개인 사운드를 내는 주체들이 등록한 소스입니다.</summary>
    private readonly List<AudioSource> m_personalSources = new List<AudioSource>();

    /// <summary>필드 효과음 AudioSource에 공통 적용할 AudioMixer 출력 그룹입니다.</summary>
    public AudioMixerGroup OutputMixerGroup => m_outputMixerGroup;

    /// <summary>개인 사운드를 공용 보이스 최대치 계산에 포함하는지 여부입니다.</summary>
    public bool IncludePersonalVoices => m_includePersonalVoices;

    /// <summary>지금 실제로 소리를 내고 있는 개인 사운드 수입니다.</summary>
    public int LivePersonalVoices => CountLivePersonalVoices();

    /// <summary>개인 사운드를 반영한 현재 공용 위치형 보이스 가용량입니다.</summary>
    public int AvailableWorldVoiceCapacity => ResolveWorldVoiceBudget();

    /// <summary>등급에 해당하는 <see cref="AudioSource.priority"/> 값입니다. 작을수록 먼저 지켜집니다.</summary>
    public static int ResolveUnityPriority(AudioPriorityClass priorityClass)
    {
        int index = Mathf.Clamp((int)priorityClass, 0, s_unityPriorities.Length - 1);
        return s_unityPriorities[index];
    }

    /// <summary>
    /// 자체 AudioSource로 개인 사운드를 내는 주체가 자기 소스를 등록합니다.
    /// </summary>
    /// <remarks>
    /// 재생 시작·종료를 쌍으로 보고받지 않고 소스 자체를 등록받는 이유는 <see cref="AudioSource.PlayOneShot"/>에
    /// 완료 통보가 없기 때문입니다. 쌍을 직접 맞추면 한쪽이 누락되는 순간 개수가 영구히 어긋나지만,
    /// 소스를 들고 있으면 <see cref="AudioSource.isPlaying"/>으로 매번 실제 상태를 세면 되므로 어긋날 수 없습니다.
    /// </remarks>
    public void RegisterPersonalSource(AudioSource source)
    {
        if (source == null || m_personalSources.Contains(source))
        {
            return;
        }

        m_personalSources.Add(source);
    }

    /// <summary>등록한 개인 사운드 소스를 해제합니다. 개체가 사라질 때 호출합니다.</summary>
    public void UnregisterPersonalSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        m_personalSources.Remove(source);
    }

    /// <summary>
    /// 개인 사운드 소스에 이 매니저의 공간 출력 정책과 우선순위를 적용합니다.
    /// </summary>
    /// <param name="source">정책을 적용할 3D 위치형 소스입니다.</param>
    /// <param name="priorityClass">천장에 걸렸을 때의 보호 등급입니다.</param>
    /// <param name="minDistance">이 주체가 요구하는 감쇠 시작 거리(m)입니다.</param>
    /// <param name="maxDistance">이 주체가 요구하는 소멸 거리(m)입니다.</param>
    /// <remarks>
    /// 거리는 주체별 게임플레이 값이라 인자로 받습니다. 감염체의 도달 거리는 그 개체의 시야 반경과 짝을 이루므로
    /// 매니저가 일괄로 정하면 "보이기 전에 들린다"를 개체마다 맞출 수 없습니다.
    /// 반대로 믹서 그룹·감쇠 곡선·우선순위는 주체가 정할 이유가 없어 여기서 일괄 적용합니다.
    /// 배경 음악처럼 2D로 재생해야 하는 소리에는 쓰지 마십시오. 이 메서드는 위치형 소스 전용입니다.
    /// </remarks>
    public void ApplyWorldSourcePolicy(
        AudioSource source,
        AudioPriorityClass priorityClass,
        float minDistance,
        float maxDistance)
    {
        if (source == null)
        {
            return;
        }

        source.playOnAwake = false;
        source.spatialBlend = 1.0f;
        source.dopplerLevel = 0.0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
        source.priority = ResolveUnityPriority(priorityClass);

        if (m_outputMixerGroup != null)
        {
            source.outputAudioMixerGroup = m_outputMixerGroup;
        }
    }

    /// <summary>지정한 월드 위치에서 한 번 재생하고, 필요하면 비어 있는 음성을 재사용합니다.</summary>
    /// <param name="clip">재생할 클립입니다. <c>null</c>이면 아무 일도 하지 않습니다.</param>
    /// <param name="position">소리가 발생한 월드 위치입니다. 재생 이후 따라 움직이지 않습니다.</param>
    /// <param name="priorityClass">예산이 부족할 때 무엇을 밀어내고 무엇에 양보할지 정하는 등급입니다.</param>
    /// <param name="volume">0~1 음량입니다.</param>
    /// <param name="pitch">재생 피치입니다.</param>
    /// <returns>유효한 클립을 재생 요청했으면 true입니다. 예산 부족으로 생략했으면 false입니다.</returns>
    public bool PlayOneShotAt(
        AudioClip clip,
        Vector3 position,
        AudioPriorityClass priorityClass,
        float volume = 1.0f,
        float pitch = 1.0f)
    {
        if (clip == null || m_maxWorldVoices <= 0)
        {
            return false;
        }

        Voice voice = AcquireVoice(priorityClass);
        if (voice == null)
        {
            return false;
        }

        AudioSource source = voice.Source;
        source.transform.position = position;
        source.outputAudioMixerGroup = m_outputMixerGroup;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Clamp(pitch, 0.1f, 3.0f);
        source.priority = ResolveUnityPriority(priorityClass);
        source.clip = clip;
        source.Play();
        voice.StartedAt = Time.unscaledTime;
        voice.PriorityClass = priorityClass;
        return true;
    }

    /// <summary>
    /// 이번 재생에 쓸 보이스를 확보합니다. 예산이 찼으면 자기보다 중요하지 않은 것만 밀어냅니다.
    /// </summary>
    /// <returns>쓸 보이스입니다. 살아 있는 모든 소리가 이번 요청보다 중요하면 <c>null</c>입니다.</returns>
    /// <remarks>
    /// 예전에는 무조건 가장 오래된 보이스를 교체했습니다. 그러면 표면 탄흔음이 연발로 밀려들 때
    /// 그보다 오래된 감염체 사망음이 밀려나, 장식이 게임플레이 정보를 덮어씁니다.
    /// 동급끼리는 여전히 최고령을 밀어냅니다. 같은 등급이면 새 소리가 더 최근 사건이기 때문입니다.
    /// </remarks>
    private Voice AcquireVoice(AudioPriorityClass priorityClass)
    {
        int budget = ResolveWorldVoiceBudget();
        if (budget <= 0)
        {
            return null;
        }

        int playing = 0;
        for (int i = 0; i < m_voices.Count; i++)
        {
            if (m_voices[i].Source.isPlaying)
            {
                playing++;
            }
        }

        if (playing < budget)
        {
            for (int i = 0; i < m_voices.Count; i++)
            {
                if (!m_voices[i].Source.isPlaying)
                {
                    return m_voices[i];
                }
            }

            Voice created = CreateVoice(m_voices.Count);
            m_voices.Add(created);
            return created;
        }

        Voice victim = null;
        for (int i = 0; i < m_voices.Count; i++)
        {
            Voice candidate = m_voices[i];
            if (!candidate.Source.isPlaying)
            {
                return candidate;
            }

            // 값이 작을수록 중요합니다. 나보다 중요한 소리는 밀어내지 않습니다.
            if ((int)candidate.PriorityClass < (int)priorityClass)
            {
                continue;
            }

            if (victim == null || candidate.StartedAt < victim.StartedAt)
            {
                victim = candidate;
            }
        }

        if (victim == null)
        {
            return null;
        }

        victim.Source.Stop();
        return victim;
    }

    private Voice CreateVoice(int index)
    {
        GameObject voiceObject = new GameObject($"World OneShot {index:00}");
        voiceObject.transform.SetParent(transform, false);

        AudioSource source = voiceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1.0f;
        source.dopplerLevel = 0.0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = m_minDistance;
        source.maxDistance = Mathf.Max(m_minDistance, m_maxDistance);
        source.outputAudioMixerGroup = m_outputMixerGroup;

        return new Voice
        {
            Source = source,
            StartedAt = float.NegativeInfinity,
            PriorityClass = AudioPriorityClass.SurfaceDecor,
        };
    }

    /// <summary>개인 사운드를 반영한 공용 보이스 예산입니다.</summary>
    private int ResolveWorldVoiceBudget()
    {
        if (!m_includePersonalVoices)
        {
            return m_maxWorldVoices;
        }

        return Mathf.Max(0, m_maxWorldVoices - CountLivePersonalVoices());
    }

    /// <summary>
    /// 등록된 개인 소스 중 지금 실제로 소리를 내고 있는 수를 셉니다.
    /// </summary>
    /// <remarks>
    /// 파괴된 소스는 이 자리에서 목록에서 걷어냅니다. 개체가 사라질 때
    /// <see cref="UnregisterPersonalSource"/>를 부르지 못하는 경로가 있어도 개수가 새지 않게 하려는 것입니다.
    /// </remarks>
    private int CountLivePersonalVoices()
    {
        int live = 0;
        for (int i = m_personalSources.Count - 1; i >= 0; i--)
        {
            AudioSource source = m_personalSources[i];
            if (source == null)
            {
                m_personalSources.RemoveAt(i);
                continue;
            }

            if (source.isPlaying)
            {
                live++;
            }
        }

        return live;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_maxWorldVoices = Mathf.Max(0, m_maxWorldVoices);
        m_minDistance = Mathf.Max(0.01f, m_minDistance);
        m_maxDistance = Mathf.Max(m_minDistance, m_maxDistance);
    }
#endif
}
