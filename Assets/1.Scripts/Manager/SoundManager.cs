using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>
/// 옵션 슬라이더 단위의 볼륨 카테고리입니다.
/// </summary>
/// <remarks>
/// 설정 코드(<see cref="GameSettingManager"/>)는 이 enum만 알고, 실제 FMOD 경로(<c>vca:/BGM</c> 등)는
/// <see cref="SoundManager"/>의 인스펙터 설정에만 둡니다. FMOD Studio에서 VCA/Bus 이름이나 구조가 바뀌어도
/// 설정 코드를 고치지 않고 인스펙터 경로만 바꾸면 되도록 하기 위해서입니다.
/// </remarks>
public enum SoundCategory
{
    /// <summary>전체 볼륨입니다. 모든 소리에 곱해집니다.</summary>
    Master = 0,

    /// <summary>배경 음악 볼륨입니다.</summary>
    Bgm = 1,

    /// <summary>효과음 볼륨입니다. 무기·캐릭터·적·환경음 등이 여기에 속합니다.</summary>
    Sfx = 2,
}

/// <summary>
/// 게임 코드와 FMOD 사이의 얇은 재생 창구입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>역할 분담</b><br/>
/// - FMOD Studio(사운드 디자이너): 소리 내용, 랜덤 변주, 3D 감쇠, Bus/이펙트, VCA 배정, 동시 재생 수·우선순위.<br/>
/// - FMOD 런타임(<see cref="RuntimeManager"/>): 시스템 초기화·매 프레임 업데이트, 뱅크 로딩, 실제 믹싱과 출력.<br/>
/// - 이 컴포넌트: "언제 무엇을 틀고 언제 정리할지". 재생 요청, 루프 인스턴스 생명주기,
///   일시정지·스냅샷·글로벌 파라미터 전달, BGM 교체, 카테고리 볼륨 적용.<br/>
/// - <see cref="GameSettingManager"/>: 볼륨 "값"의 저장·로드. 적용은 <see cref="SetVolume"/>을 호출합니다.
/// </para>
/// <para>
/// <b>사운드 종류는 등록하지 않습니다.</b><br/>
/// 이벤트 목록은 FMOD Studio 프로젝트와 뱅크가 이미 갖고 있습니다. 어떤 이벤트를 쓸지는
/// 무기 데이터·컴포넌트가 <see cref="EventReference"/> 필드로 직접 들고 있다가 이 매니저에 넘깁니다.
/// </para>
/// <para>
/// <b>하지 않는 일</b><br/>
/// 동시 재생 수 제한, 우선순위, 풀링은 FMOD 이벤트 설정(Max Instances, Stealing, Priority)과
/// 가상 보이스가 처리하므로 여기서 다시 구현하지 않습니다.
/// 적 AI의 청각 판정도 이 매니저와 묶지 않습니다. 볼륨을 0으로 해도 적은 소리를 들어야 하기 때문입니다.
/// </para>
/// <para>
/// <b>볼륨 폴백</b><br/>
/// 카테고리 볼륨은 VCA를 우선 사용하고, VCA가 아직 뱅크에 없으면 같은 카테고리의 Bus로 대신 적용합니다.
/// FMOD Studio에서 VCA를 추가하기 전에도 Master 볼륨(<c>bus:/</c>)은 동작하게 하기 위해서입니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class SoundManager : MonoBehaviour
{
    /// <summary>
    /// 볼륨 카테고리 하나를 FMOD의 어떤 VCA(또는 Bus)에 적용할지 정하는 인스펙터 설정입니다.
    /// </summary>
    [Serializable]
    private sealed class CategoryRoute
    {
        [Tooltip("이 경로가 담당하는 볼륨 카테고리입니다.")]
        public SoundCategory Category;

        [Tooltip("옵션 볼륨을 적용할 VCA 경로입니다. 예: vca:/BGM")]
        public string VcaPath;

        [Tooltip("VCA가 뱅크에 없을 때 대신 볼륨을 적용할 Bus 경로입니다. 예: bus:/BGM")]
        public string FallbackBusPath;
    }

    /// <summary>
    /// <see cref="Play"/>로 만든 루프 인스턴스와, 그 수명을 따라갈 소유 오브젝트입니다.
    /// </summary>
    /// <remarks>
    /// <see cref="HasOwner"/>를 따로 두는 이유: Unity 오브젝트는 파괴되면 <c>== null</c>이 참이 되므로,
    /// "처음부터 소유자가 없었다"와 "소유자가 파괴됐다"를 <see cref="Owner"/>만으로는 구분할 수 없습니다.
    /// </remarks>
    private struct TrackedInstance
    {
        public EventInstance Instance;
        public GameObject Owner;
        public bool HasOwner;
    }

    /// <summary>씬에 하나만 존재하는 사운드 매니저입니다. 없으면 null입니다.</summary>
    public static SoundManager Instance { get; private set; }

    [Header("Volume Routes")]
    [Tooltip("볼륨 카테고리별 VCA/Bus 경로입니다. FMOD Studio에서 VCA 이름을 바꿨다면 여기만 고치면 됩니다.")]
    [SerializeField] private CategoryRoute[] m_categoryRoutes =
    {
        new CategoryRoute { Category = SoundCategory.Master, VcaPath = "vca:/Master", FallbackBusPath = "bus:/" },
        new CategoryRoute { Category = SoundCategory.Bgm, VcaPath = "vca:/BGM", FallbackBusPath = "bus:/BGM" },
        new CategoryRoute { Category = SoundCategory.Sfx, VcaPath = "vca:/SFX", FallbackBusPath = "bus:/SFX" },
    };

    [Header("Pause")]
    [Tooltip("게임 일시정지 때 멈출 Bus 경로입니다. UI Bus는 넣지 않아야 메뉴 소리가 계속 납니다. 하나도 찾지 못하면 Master Bus(bus:/)를 멈춥니다.")]
    [SerializeField] private string[] m_pauseBusPaths = { "bus:/SFX", "bus:/BGM" };

    /// <summary>
    /// 이 매니저가 해제 책임을 지는 루프 인스턴스 목록입니다.
    /// OneShot은 FMOD가 스스로 해제하므로 여기에 넣지 않습니다.
    /// </summary>
    private readonly List<TrackedInstance> m_trackedInstances = new List<TrackedInstance>();

    /// <summary>
    /// 켜져 있는 스냅샷입니다. 스냅샷도 FMOD에서는 이벤트 인스턴스이므로,
    /// 끌 때 같은 인스턴스를 찾아 멈추려면 이벤트 GUID로 보관해야 합니다.
    /// </summary>
    private readonly Dictionary<FMOD.GUID, EventInstance> m_activeSnapshots = new Dictionary<FMOD.GUID, EventInstance>();

    /// <summary>
    /// 카테고리별로 마지막에 적용한 볼륨입니다. 인덱스는 <see cref="SoundCategory"/> 값과 같습니다.
    /// FMOD의 VCA/Bus 값을 다시 읽지 않고 UI 표시용으로 바로 돌려주기 위한 캐시입니다.
    /// </summary>
    private readonly float[] m_categoryVolumes = { 1f, 1f, 1f };

    /// <summary>현재 재생 중인 BGM 인스턴스입니다. BGM은 동시에 하나만 둡니다.</summary>
    private EventInstance m_music;

    /// <summary>현재 BGM의 이벤트 GUID입니다. 같은 곡을 다시 요청했을 때 처음부터 재시작하지 않으려고 보관합니다.</summary>
    private FMOD.GUID m_musicGuid;

    private bool m_isPaused;

    /// <summary>현재 일시정지 상태입니다.</summary>
    public bool IsPaused => m_isPaused;

    /// <summary>현재 추적 중인 루프 인스턴스 수입니다. 정리 누락(계속 늘어나기만 하는지)을 확인할 때 씁니다.</summary>
    public int TrackedInstanceCount => m_trackedInstances.Count;

    private void Awake()
    {
        // 중복 매니저는 오브젝트째 지우지 않고 컴포넌트만 제거합니다.
        // 테스트 씬에서 다른 매니저가 붙은 오브젝트에 함께 올려 두는 경우를 고려한 것입니다.
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        // 매니저가 사라질 때 들고 있던 인스턴스를 즉시 정리합니다.
        // 정리하지 않으면 FMOD 쪽에 인스턴스가 남아 소리가 계속 나거나 메모리가 새게 됩니다.
        StopAllLoops(false);
        StopMusic(false);
        ClearSnapshots();
        Instance = null;
    }

    /// <summary>
    /// 추적 중인 루프 인스턴스를 매 프레임 점검해 정리합니다.
    /// </summary>
    /// <remarks>
    /// 정리를 호출자에게만 맡기면 오브젝트 파괴 경로(사망, 풀 반환, 씬 전환 등)마다 release 누락이 생기기 쉽습니다.
    /// 그래서 아래 세 경우를 이 매니저가 직접 처리합니다.<br/>
    /// 1) 이미 무효가 된 핸들: 목록에서만 뺍니다.<br/>
    /// 2) 소유 오브젝트가 파괴됨: 페이드아웃으로 멈추고 해제합니다.<br/>
    /// 3) 스스로 재생이 끝남(STOPPED): 해제합니다. <see cref="Stop"/>을 호출한 경우도 여기서 마무리됩니다.
    /// </remarks>
    private void Update()
    {
        // 순회 중 제거하므로 뒤에서부터 돕니다.
        for (int i = m_trackedInstances.Count - 1; i >= 0; i--)
        {
            TrackedInstance tracked = m_trackedInstances[i];

            if (!tracked.Instance.isValid())
            {
                m_trackedInstances.RemoveAt(i);
                continue;
            }

            if (tracked.HasOwner && tracked.Owner == null)
            {
                ReleaseInstance(tracked.Instance, true);
                m_trackedInstances.RemoveAt(i);
                continue;
            }

            tracked.Instance.getPlaybackState(out PLAYBACK_STATE state);
            if (state == PLAYBACK_STATE.STOPPED)
            {
                ReleaseInstance(tracked.Instance, false);
                m_trackedInstances.RemoveAt(i);
            }
        }
    }

    #region Playback

    /// <summary>
    /// 지정한 월드 위치에서 한 번 재생하고 자동으로 해제되는 사운드입니다.
    /// </summary>
    /// <remarks>
    /// 위치가 고정된 짧은 소리(탄착음, 폭발음 등)에 씁니다. 재생 후 위치가 따라 움직이지 않습니다.
    /// 반환값이 없으므로 재생 도중 멈추거나 파라미터를 바꿀 수 없습니다. 그게 필요하면 <see cref="Play"/>를 씁니다.
    /// </remarks>
    /// <param name="eventReference">재생할 FMOD 이벤트입니다. 인스펙터에서 선택한 값을 넘깁니다.</param>
    /// <param name="position">소리가 나는 월드 위치입니다. 2D 이벤트라면 위치는 무시됩니다.</param>
    public void PlayOneShot(EventReference eventReference, Vector3 position)
    {
        if (!IsPlayable(eventReference))
        {
            return;
        }

        RuntimeManager.PlayOneShot(eventReference, position);
    }

    /// <summary>
    /// 오브젝트를 따라 움직이며 한 번 재생하고 자동으로 해제되는 사운드입니다.
    /// </summary>
    /// <remarks>
    /// 움직이는 대상이 내는 짧은 소리(발소리, 사격음, 피격 비명 등)에 씁니다.
    /// 소유 오브젝트가 재생 도중 파괴되면 마지막 위치에서 끝까지 재생됩니다.
    /// </remarks>
    /// <param name="eventReference">재생할 FMOD 이벤트입니다.</param>
    /// <param name="owner">소리가 따라갈 오브젝트입니다. null이면 재생하지 않습니다.</param>
    public void PlayOneShotAttached(EventReference eventReference, GameObject owner)
    {
        if (!IsPlayable(eventReference) || owner == null)
        {
            return;
        }

        RuntimeManager.PlayOneShotAttached(eventReference, owner);
    }

    /// <summary>
    /// 루프나 파라미터 제어가 필요한 사운드를 재생하고 인스턴스를 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 발전기 소음, 엔진음, 연사음처럼 길게 이어지거나 재생 중 파라미터를 바꿔야 하는 소리에 씁니다.<br/>
    /// - <paramref name="owner"/>를 넘기면 위치를 따라가고, 소유 오브젝트가 파괴되면 자동으로 멈추고 해제합니다.<br/>
    /// - 소유자 없이 재생했다면 <see cref="Stop"/>으로 직접 멈춰야 합니다. 멈춘 뒤 해제는 이 매니저가 처리합니다.<br/>
    /// - 돌려받은 인스턴스에 직접 <c>release()</c>를 호출하지 마세요. 해제 책임은 이 매니저에 있습니다.
    /// </remarks>
    /// <param name="eventReference">재생할 FMOD 이벤트입니다.</param>
    /// <param name="owner">소리가 따라가고 수명을 함께할 오브젝트입니다. null이면 위치 없이(2D 또는 원점) 재생합니다.</param>
    /// <returns>파라미터 변경·정지에 쓸 인스턴스입니다. 재생에 실패하면 무효한 기본값입니다.</returns>
    public EventInstance Play(EventReference eventReference, GameObject owner = null)
    {
        if (!IsPlayable(eventReference))
        {
            return default;
        }

        EventInstance instance = RuntimeManager.CreateInstance(eventReference);
        if (owner != null)
        {
            // 매 프레임 owner의 위치·속도를 인스턴스의 3D 속성에 반영하도록 RuntimeManager에 맡깁니다.
            RuntimeManager.AttachInstanceToGameObject(instance, owner);
        }

        instance.start();
        m_trackedInstances.Add(new TrackedInstance
        {
            Instance = instance,
            Owner = owner,
            HasOwner = owner != null,
        });

        return instance;
    }

    /// <summary>
    /// <see cref="Play"/>로 받은 인스턴스를 멈춥니다. 해제는 다음 프레임 <see cref="Update"/>에서 처리됩니다.
    /// </summary>
    /// <param name="instance">멈출 인스턴스입니다. 이미 무효하면 아무것도 하지 않습니다.</param>
    /// <param name="allowFadeOut">true면 FMOD 이벤트에 설정된 페이드아웃(AHDSR 릴리즈)을 거쳐 멈춥니다.</param>
    public void Stop(EventInstance instance, bool allowFadeOut = true)
    {
        if (!instance.isValid())
        {
            return;
        }

        instance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
    }

    /// <summary>
    /// 추적 중인 루프 인스턴스를 모두 멈추고 해제합니다.
    /// </summary>
    /// <remarks>
    /// 씬 전환이나 필드 종료처럼 "지금 나는 소리를 전부 정리"해야 할 때 씁니다.
    /// BGM과 스냅샷은 씬을 넘어 유지될 수 있으므로 여기서 건드리지 않습니다.
    /// </remarks>
    public void StopAllLoops(bool allowFadeOut = true)
    {
        for (int i = 0; i < m_trackedInstances.Count; i++)
        {
            ReleaseInstance(m_trackedInstances[i].Instance, allowFadeOut);
        }

        m_trackedInstances.Clear();
    }

    #endregion

    #region Music

    /// <summary>
    /// BGM을 교체합니다. 같은 이벤트가 이미 재생 중이면 다시 시작하지 않습니다.
    /// </summary>
    /// <remarks>
    /// 이전 곡은 페이드아웃으로 멈추고 새 곡을 바로 시작합니다.
    /// 곡 사이의 자연스러운 크로스페이드나 구간 전환(예: Assault_Incoming → Resolve)은
    /// FMOD 이벤트 안의 트랜지션으로 만들고, 코드는 <see cref="SetMusicParameter"/>로 큐만 보내는 쪽을 권장합니다.
    /// </remarks>
    /// <param name="musicReference">재생할 음악 이벤트입니다.</param>
    /// <param name="allowFadeOut">이전 곡을 페이드아웃으로 멈출지 여부입니다.</param>
    public void PlayMusic(EventReference musicReference, bool allowFadeOut = true)
    {
        if (!IsPlayable(musicReference))
        {
            return;
        }

        // 같은 곡을 다시 요청하면 무시합니다. 씬 진입마다 PlayMusic을 불러도 음악이 끊기지 않게 하기 위해서입니다.
        if (m_music.isValid() && m_musicGuid.Equals(musicReference.Guid))
        {
            return;
        }

        StopMusic(allowFadeOut);

        m_music = RuntimeManager.CreateInstance(musicReference);
        m_musicGuid = musicReference.Guid;
        m_music.start();
    }

    /// <summary>현재 BGM을 멈추고 해제합니다.</summary>
    /// <param name="allowFadeOut">true면 이벤트에 설정된 페이드아웃을 거쳐 멈춥니다.</param>
    public void StopMusic(bool allowFadeOut = true)
    {
        if (!m_music.isValid())
        {
            return;
        }

        ReleaseInstance(m_music, allowFadeOut);
        m_music = default;
        m_musicGuid = default;
    }

    /// <summary>
    /// 현재 BGM 이벤트의 로컬 파라미터를 바꿉니다. 음악 구간 전환 큐에 씁니다.
    /// </summary>
    /// <param name="parameterName">FMOD Studio에서 해당 음악 이벤트에 정의한 파라미터 이름입니다.</param>
    /// <param name="value">설정할 값입니다.</param>
    public void SetMusicParameter(string parameterName, float value)
    {
        if (m_music.isValid())
        {
            m_music.setParameterByName(parameterName, value);
        }
    }

    #endregion

    #region Game State

    /// <summary>
    /// 게임 일시정지 상태를 FMOD에 반영합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="Time.timeScale"/>은 FMOD에 영향을 주지 않으므로, 일시정지 처리 쪽에서 이 함수를 같이 불러야 합니다.
    /// <see cref="m_pauseBusPaths"/>에 있는 Bus만 멈추므로 UI Bus는 계속 재생됩니다.
    /// 지정한 Bus를 하나도 찾지 못하면(아직 FMOD에 SFX/BGM Bus를 만들지 않은 단계) Master Bus 전체를 멈춥니다.
    /// </remarks>
    /// <param name="paused">true면 멈추고, false면 이어서 재생합니다.</param>
    public void SetPaused(bool paused)
    {
        m_isPaused = paused;

        bool pausedAny = false;
        for (int i = 0; i < m_pauseBusPaths.Length; i++)
        {
            if (TryGetBus(m_pauseBusPaths[i], out Bus bus))
            {
                bus.setPaused(paused);
                pausedAny = true;
            }
        }

        if (!pausedAny)
        {
            RuntimeManager.PauseAllEvents(paused);
        }
    }

    /// <summary>
    /// 스냅샷(일시정지 필터, 저체력 먹먹함, 실내 반향 등)을 켜거나 끕니다.
    /// </summary>
    /// <remarks>
    /// 스냅샷은 FMOD Studio에서 만든 "믹서 상태 프리셋"입니다. 켜져 있는 동안 지정한 Bus/VCA 값과 이펙트를 덮어씁니다.
    /// 이미 켜진 스냅샷을 다시 켜거나, 꺼진 스냅샷을 다시 꺼도 아무 일도 일어나지 않습니다.
    /// </remarks>
    /// <param name="snapshotReference">켜거나 끌 스냅샷 이벤트(<c>snapshot:/...</c>)입니다.</param>
    /// <param name="active">true면 켜고, false면 끕니다.</param>
    public void SetSnapshot(EventReference snapshotReference, bool active)
    {
        if (!IsPlayable(snapshotReference))
        {
            return;
        }

        FMOD.GUID guid = snapshotReference.Guid;
        bool isActive = m_activeSnapshots.TryGetValue(guid, out EventInstance snapshot);

        if (active && !isActive)
        {
            snapshot = RuntimeManager.CreateInstance(snapshotReference);
            snapshot.start();
            m_activeSnapshots.Add(guid, snapshot);
        }
        else if (!active && isActive)
        {
            // 페이드아웃을 허용해 스냅샷에 설정된 해제 시간만큼 믹서가 부드럽게 원래 상태로 돌아가게 합니다.
            ReleaseInstance(snapshot, true);
            m_activeSnapshots.Remove(guid);
        }
    }

    /// <summary>
    /// FMOD Studio에 Global로 정의된 파라미터 값을 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 방어 단계, 위협도, 시간대처럼 여러 이벤트가 함께 반응해야 하는 게임 상태를 넘길 때 씁니다.
    /// 이벤트 하나에만 해당하는 로컬 파라미터는 <see cref="Play"/>로 받은 인스턴스의
    /// <c>setParameterByName</c>을 직접 호출합니다.
    /// </remarks>
    /// <param name="parameterName">FMOD Studio에서 Global로 만든 파라미터 이름입니다.</param>
    /// <param name="value">설정할 값입니다.</param>
    public void SetGlobalParameter(string parameterName, float value)
    {
        FMOD.RESULT result = RuntimeManager.StudioSystem.setParameterByName(parameterName, value);
        if (result != FMOD.RESULT.OK)
        {
            Debug.LogWarning($"[SoundManager] Global parameter '{parameterName}' 설정 실패: {result}");
        }
    }

    #endregion

    #region Volume

    /// <summary>
    /// 카테고리 볼륨(0~1)을 적용합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="GameSettingManager"/>가 저장된 값을 불러오거나 옵션 슬라이더가 움직일 때 호출합니다.
    /// 값은 선형 배율(0 = 무음, 1 = 원래 크기)이며, FMOD의 VCA/Bus <c>setVolume</c>도 같은 선형 값을 받습니다.
    /// 적용 순서는 VCA → 폴백 Bus이며, 둘 다 없으면 경고만 남기고 값은 캐시에 보관합니다.
    /// </remarks>
    /// <param name="category">적용할 볼륨 카테고리입니다.</param>
    /// <param name="volume01">0~1 범위의 볼륨입니다. 범위를 벗어나면 잘라냅니다.</param>
    public void SetVolume(SoundCategory category, float volume01)
    {
        float volume = Mathf.Clamp01(volume01);
        m_categoryVolumes[(int)category] = volume;

        if (!TryResolveRoute(category, out CategoryRoute route))
        {
            Debug.LogWarning($"[SoundManager] {category} 볼륨 경로가 설정되지 않았습니다.");
            return;
        }

        if (TryGetVca(route.VcaPath, out VCA vca))
        {
            vca.setVolume(volume);
            return;
        }

        if (TryGetBus(route.FallbackBusPath, out Bus bus))
        {
            bus.setVolume(volume);
            return;
        }

        Debug.LogWarning($"[SoundManager] {category} 볼륨을 적용할 VCA({route.VcaPath})와 Bus({route.FallbackBusPath})를 모두 찾지 못했습니다.");
    }

    /// <summary>마지막으로 적용한 카테고리 볼륨(0~1)입니다. 옵션 UI 초기값 표시용입니다.</summary>
    public float GetVolume(SoundCategory category)
    {
        return m_categoryVolumes[(int)category];
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 이벤트가 비어 있는지 확인합니다. 인스펙터에서 이벤트를 지정하지 않은 채 재생을 요청한 경우를 걸러냅니다.
    /// </summary>
    private static bool IsPlayable(EventReference eventReference)
    {
        if (eventReference.IsNull)
        {
            Debug.LogWarning("[SoundManager] 비어 있는 EventReference로 재생을 요청했습니다.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 인스턴스를 멈추고 해제 예약한 뒤, 오브젝트 부착 정보를 정리합니다.
    /// </summary>
    private static void ReleaseInstance(EventInstance instance, bool allowFadeOut)
    {
        if (!instance.isValid())
        {
            return;
        }

        instance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
        // release는 즉시 파괴가 아니라 "멈춘 뒤 해제 예약"이므로 페이드아웃은 끝까지 재생됩니다.
        instance.release();
        // RuntimeManager가 더 이상 이 인스턴스의 3D 위치를 갱신하지 않도록 부착 목록에서 뺍니다.
        RuntimeManager.DetachInstanceFromGameObject(instance);
    }

    /// <summary>켜져 있는 스냅샷을 모두 즉시 끕니다. 매니저 파괴 시 정리용입니다.</summary>
    private void ClearSnapshots()
    {
        foreach (EventInstance snapshot in m_activeSnapshots.Values)
        {
            ReleaseInstance(snapshot, false);
        }

        m_activeSnapshots.Clear();
    }

    /// <summary>인스펙터 설정에서 카테고리에 해당하는 경로를 찾습니다.</summary>
    private bool TryResolveRoute(SoundCategory category, out CategoryRoute route)
    {
        for (int i = 0; i < m_categoryRoutes.Length; i++)
        {
            if (m_categoryRoutes[i].Category == category)
            {
                route = m_categoryRoutes[i];
                return true;
            }
        }

        route = null;
        return false;
    }

    // RuntimeManager.GetVCA/GetBus는 경로가 없으면 예외를 던지므로, VCA를 아직 만들지 않은 단계에서도
    // 조용히 폴백할 수 있게 StudioSystem을 직접 조회합니다.

    /// <summary>VCA 경로를 조회합니다. 뱅크에 없으면 false를 돌려줍니다.</summary>
    private static bool TryGetVca(string path, out VCA vca)
    {
        vca = default;
        return !string.IsNullOrEmpty(path)
            && RuntimeManager.StudioSystem.getVCA(path, out vca) == FMOD.RESULT.OK;
    }

    /// <summary>Bus 경로를 조회합니다. 뱅크에 없으면 false를 돌려줍니다.</summary>
    private static bool TryGetBus(string path, out Bus bus)
    {
        bus = default;
        return !string.IsNullOrEmpty(path)
            && RuntimeManager.StudioSystem.getBus(path, out bus) == FMOD.RESULT.OK;
    }

    #endregion
}
