using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>
/// 셸터 씬의 사운드 창구입니다. 게임 코드는 <see cref="ShelterSoundId"/>로만 재생을 요청합니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>흐름</b>: 게임 코드 → <c>ShelterAudio.Play(상황 ID)</c> → <see cref="ShelterSoundCatalog"/>에서 이벤트 조회
/// → <see cref="SoundManager"/> → FMOD.
/// </para>
/// <para>
/// - 카탈로그 칸이 비어 있으면 조용히 무시합니다. 사운드를 받기 전에 재생 지점을 먼저 심어 둘 수 있습니다.<br/>
/// - 씬에 <see cref="SoundManager"/>가 없으면 FMOD <see cref="RuntimeManager"/>로 직접 재생합니다.<br/>
/// - 뱅크에 이벤트가 없으면(빌드 누락) 경고를 이벤트당 한 번만 남기고 계속 진행합니다.<br/>
/// - 씬에 이 컴포넌트가 없으면(예: 방어전 씬) 모든 정적 호출은 아무 일도 하지 않습니다.
/// </para>
/// <para>
/// BGM과 환경음은 씬 시작 시 카탈로그 값으로 재생하고, 이 컴포넌트가 파괴될 때(씬 전환) 멈춥니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class ShelterAudio : MonoBehaviour
{
    [Tooltip("상황 ID와 FMOD 이벤트를 연결한 카탈로그 에셋입니다.")]
    [SerializeField] private ShelterSoundCatalog m_catalog;

    [Tooltip("씬 시작 시 카탈로그의 BGM과 환경음을 자동 재생합니다. 인트로 컷신이 재생되면 컷신이 끝나거나 스킵된 뒤에 시작합니다. 끄면 StartSceneAudio()를 직접 호출해야 합니다.")]
    [SerializeField] private bool m_playSceneAudioOnStart = true;

    [Tooltip("이 컷신이 재생 중이면 끝날 때까지 BGM 시작을 미룹니다. 비어 있으면 씬에서 자동으로 찾습니다.")]
    [SerializeField] private ShelterIntroCutscene m_introCutscene;

    [Tooltip("에디터에서 비어 있는 상황 칸이 호출되면 상황당 한 번 로그를 남깁니다. 어디에 사운드가 필요한지 기획에 전달할 때 씁니다.")]
    [SerializeField] private bool m_logEmptySlotsInEditor = true;

    private static ShelterAudio s_instance;

    private readonly HashSet<ShelterSoundId> m_loggedEmptySlots = new HashSet<ShelterSoundId>();
    private readonly HashSet<FMOD.GUID> m_loggedMissingEvents = new HashSet<FMOD.GUID>();

    private EventInstance m_ambienceInstance;

    /// <summary>SoundManager가 없어 직접 만든 BGM 인스턴스입니다. SoundManager가 있으면 쓰지 않습니다.</summary>
    private EventInstance m_fallbackMusicInstance;

    private bool m_ownsManagedMusic;

    /// <summary>현재 씬의 셸터 사운드 창구입니다. 없으면 null입니다.</summary>
    public static ShelterAudio Instance => s_instance;

    /// <summary>연결된 카탈로그입니다.</summary>
    public ShelterSoundCatalog Catalog => m_catalog;

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning("[ShelterAudio] 씬에 ShelterAudio가 두 개 있습니다. 나중 것은 비활성화합니다.", this);
            enabled = false;
            return;
        }

        s_instance = this;

        if (m_catalog == null)
        {
            Debug.LogWarning("[ShelterAudio] ShelterSoundCatalog가 연결되지 않아 셸터 사운드가 재생되지 않습니다.", this);
        }
    }

    private void Start()
    {
        if (s_instance == this && m_playSceneAudioOnStart)
        {
            StartCoroutine(StartSceneAudioAfterIntro());
        }
    }

    /// <summary>
    /// 인트로 컷신이 재생되면 끝나거나 스킵될 때까지 기다렸다가 BGM·환경음을 시작합니다.
    /// </summary>
    /// <remarks>
    /// 컷신 재생 여부는 <see cref="ShelterFlowController"/>의 Start(실행 순서 100)에서 정해지므로,
    /// 이 컴포넌트의 Start 시점에는 아직 알 수 없습니다. 한 프레임 뒤에 확인합니다.
    /// 재진입처럼 컷신이 없으면 바로 시작합니다.
    /// </remarks>
    private System.Collections.IEnumerator StartSceneAudioAfterIntro()
    {
        yield return null;

        if (m_introCutscene == null)
        {
            m_introCutscene = FindFirstObjectByType<ShelterIntroCutscene>();
        }

        while (m_introCutscene != null && m_introCutscene.IsPlaying)
        {
            yield return null;
        }

        StartSceneAudio();
    }

    private void OnDestroy()
    {
        if (s_instance != this)
        {
            return;
        }

        StopSceneAudio(true);
        s_instance = null;
    }

    #region Static API

    /// <summary>위치와 상관없는 2D 소리(UI, 알림 등)를 한 번 재생합니다.</summary>
    public static void Play(ShelterSoundId id)
    {
        if (s_instance != null)
        {
            s_instance.PlayOneShotInternal(id, Vector3.zero, null);
        }
    }

    /// <summary>지정한 월드 위치에서 3D 소리를 한 번 재생합니다. 재생 후 위치는 따라가지 않습니다.</summary>
    public static void PlayAt(ShelterSoundId id, Vector3 position)
    {
        if (s_instance != null)
        {
            s_instance.PlayOneShotInternal(id, position, null);
        }
    }

    /// <summary>오브젝트를 따라가는 3D 소리를 한 번 재생합니다.</summary>
    public static void PlayAttached(ShelterSoundId id, GameObject owner)
    {
        if (s_instance != null && owner != null)
        {
            s_instance.PlayOneShotInternal(id, owner.transform.position, owner);
        }
    }

    /// <summary>
    /// 루프·파라미터 제어가 필요한 소리를 재생하고 인스턴스를 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 돌려받은 인스턴스는 <see cref="StopLoop"/>로 멈춥니다. 직접 <c>release()</c>하지 마십시오.
    /// <paramref name="owner"/>를 넘기면 위치를 따라가고, SoundManager가 있으면 그 오브젝트가 파괴될 때 자동으로 멈춥니다.
    /// 칸이 비어 있으면 무효한 인스턴스(<c>isValid() == false</c>)를 돌려줍니다.
    /// </remarks>
    public static EventInstance PlayLoop(ShelterSoundId id, GameObject owner = null)
    {
        return s_instance != null ? s_instance.PlayLoopInternal(id, owner) : default;
    }

    /// <summary><see cref="PlayLoop"/>로 받은 인스턴스를 멈춥니다.</summary>
    public static void StopLoop(EventInstance instance, bool allowFadeOut = true)
    {
        if (!instance.isValid())
        {
            return;
        }

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.Stop(instance, allowFadeOut);
            return;
        }

        StopAndRelease(instance, allowFadeOut);
    }

    #endregion

    #region Scene Audio (BGM / Ambience)

    /// <summary>카탈로그의 BGM과 환경음을 재생합니다. 이미 재생 중이면 다시 시작하지 않습니다.</summary>
    public void StartSceneAudio()
    {
        if (m_catalog == null || !SoundManager.IsReady)
        {
            return;
        }

        StartMusic(m_catalog.Music);

        EventReference ambience = m_catalog.Ambience;
        if (!ambience.IsNull && !m_ambienceInstance.isValid())
        {
            EventInstance created = default;
            TryRun(ambience, () => created = CreateAndStart(ambience, null));
            m_ambienceInstance = created;
        }
    }

    /// <summary>이 컴포넌트가 시작한 BGM과 환경음을 멈춥니다.</summary>
    public void StopSceneAudio(bool allowFadeOut = true)
    {
        if (m_ambienceInstance.isValid())
        {
            StopLoop(m_ambienceInstance, allowFadeOut);
            m_ambienceInstance = default;
        }

        if (m_ownsManagedMusic && SoundManager.Instance != null)
        {
            SoundManager.Instance.StopMusic(allowFadeOut);
        }

        m_ownsManagedMusic = false;

        if (m_fallbackMusicInstance.isValid())
        {
            StopAndRelease(m_fallbackMusicInstance, allowFadeOut);
            m_fallbackMusicInstance = default;
        }
    }

    private void StartMusic(EventReference music)
    {
        if (music.IsNull)
        {
            return;
        }

        SoundManager manager = SoundManager.Instance;
        if (manager != null)
        {
            TryRun(music, () => manager.PlayMusic(music));
            m_ownsManagedMusic = true;
            return;
        }

        if (m_fallbackMusicInstance.isValid())
        {
            return;
        }

        EventInstance created = default;
        TryRun(music, () =>
        {
            created = RuntimeManager.CreateInstance(music);
            created.start();
        });
        m_fallbackMusicInstance = created;
    }

    #endregion

    #region Internal

    private void PlayOneShotInternal(ShelterSoundId id, Vector3 position, GameObject owner)
    {
        if (!TryResolve(id, out EventReference eventReference))
        {
            return;
        }

        TryRun(eventReference, () =>
        {
            SoundManager manager = SoundManager.Instance;
            if (owner != null)
            {
                if (manager != null) manager.PlayOneShotAttached(eventReference, owner);
                else RuntimeManager.PlayOneShotAttached(eventReference, owner);
                return;
            }

            if (manager != null) manager.PlayOneShot(eventReference, position);
            else RuntimeManager.PlayOneShot(eventReference, position);
        });
    }

    private EventInstance PlayLoopInternal(ShelterSoundId id, GameObject owner)
    {
        if (!TryResolve(id, out EventReference eventReference))
        {
            return default;
        }

        EventInstance instance = default;
        TryRun(eventReference, () => instance = CreateAndStart(eventReference, owner));
        return instance;
    }

    /// <summary>SoundManager가 있으면 수명 관리를 맡기고, 없으면 직접 만들어 재생합니다.</summary>
    private static EventInstance CreateAndStart(EventReference eventReference, GameObject owner)
    {
        if (SoundManager.Instance != null)
        {
            return SoundManager.Instance.Play(eventReference, owner);
        }

        EventInstance instance = RuntimeManager.CreateInstance(eventReference);
        if (owner != null)
        {
            RuntimeManager.AttachInstanceToGameObject(instance, owner);
        }

        instance.start();
        return instance;
    }

    private static void StopAndRelease(EventInstance instance, bool allowFadeOut)
    {
        instance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
        instance.release();
        RuntimeManager.DetachInstanceFromGameObject(instance);
    }

    private bool TryResolve(ShelterSoundId id, out EventReference eventReference)
    {
        eventReference = default;
        if (id == ShelterSoundId.None || !SoundManager.IsReady)
        {
            return false;
        }

        if (m_catalog != null && m_catalog.TryGetEvent(id, out eventReference))
        {
            return true;
        }

#if UNITY_EDITOR
        if (m_logEmptySlotsInEditor && m_loggedEmptySlots.Add(id))
        {
            Debug.Log($"[ShelterAudio] 사운드 미지정 상황이 호출됐습니다: {id}. ShelterSoundCatalog에서 이벤트를 연결하면 재생됩니다.", this);
        }
#endif
        return false;
    }

    /// <summary>뱅크에 이벤트가 없을 때의 예외를 이벤트당 한 번만 경고로 바꿉니다.</summary>
    private void TryRun(EventReference eventReference, Action action)
    {
        try
        {
            action();
        }
        catch (EventNotFoundException exception)
        {
            if (m_loggedMissingEvents.Add(eventReference.Guid))
            {
                Debug.LogWarning($"[ShelterAudio] FMOD 뱅크에서 이벤트를 찾지 못했습니다. FMOD Studio Build 후 Refresh Banks가 필요합니다.\n{exception.Message}", this);
            }
        }
    }

    #endregion
}
