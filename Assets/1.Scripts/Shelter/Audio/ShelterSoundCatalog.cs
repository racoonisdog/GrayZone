using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

/// <summary>
/// 셸터 상황 ID(<see cref="ShelterSoundId"/>)와 FMOD 이벤트를 연결하는 표입니다.
/// </summary>
/// <remarks>
/// <para>
/// 기획에서 사운드를 받으면 FMOD Studio에 이벤트를 만들고 Build한 뒤, 이 에셋의 해당 칸에 이벤트만 고르면 됩니다.
/// 코드는 고치지 않습니다.
/// </para>
/// <para>
/// 에디터에서 에셋을 열면 enum에 있는 모든 상황이 자동으로 한 줄씩 채워집니다.
/// 비어 있는 칸은 "아직 사운드를 받지 않은 상황"이라는 뜻이며, 재생 요청이 와도 조용히 무시됩니다.
/// </para>
/// </remarks>
[CreateAssetMenu(menuName = "GrayZone/Audio/Shelter Sound Catalog", fileName = "ShelterSoundCatalog")]
public sealed class ShelterSoundCatalog : ScriptableObject
{
    [Serializable]
    private struct Entry
    {
        [Tooltip("이 줄이 담당하는 상황입니다.")]
        public ShelterSoundId Id;

        [Tooltip("이 상황에서 재생할 FMOD 이벤트입니다. 비워 두면 소리가 나지 않습니다.")]
        public EventReference Event;

        [Tooltip("기획 전달 메모입니다. 예: 어떤 느낌, 길이, 2D/3D 여부. 게임에는 영향이 없습니다.")]
        public string Note;
    }

    [Header("BGM / 환경음")]
    [Tooltip("셸터 씬에 들어오면 재생할 배경 음악입니다. 비워 두면 BGM 없이 진행합니다.")]
    [SerializeField] private EventReference m_music;

    [Tooltip("셸터 씬 동안 계속 깔리는 환경음 루프입니다(발전기, 바람 등). 비워 두면 재생하지 않습니다.")]
    [SerializeField] private EventReference m_ambience;

    [Header("상황별 효과음")]
    [SerializeField] private Entry[] m_entries = Array.Empty<Entry>();

    private Dictionary<ShelterSoundId, EventReference> m_lookup;

    /// <summary>셸터 배경 음악 이벤트입니다. 비어 있을 수 있습니다.</summary>
    public EventReference Music => m_music;

    /// <summary>셸터 환경음 루프 이벤트입니다. 비어 있을 수 있습니다.</summary>
    public EventReference Ambience => m_ambience;

    /// <summary>상황에 연결된 이벤트를 찾습니다.</summary>
    /// <returns>칸이 채워져 있으면 true입니다. 비어 있거나 없는 상황이면 false입니다.</returns>
    public bool TryGetEvent(ShelterSoundId id, out EventReference eventReference)
    {
        if (m_lookup == null)
        {
            BuildLookup();
        }

        if (id != ShelterSoundId.None
            && m_lookup.TryGetValue(id, out eventReference)
            && !eventReference.IsNull)
        {
            return true;
        }

        eventReference = default;
        return false;
    }

    private void OnEnable()
    {
        m_lookup = null;
    }

    private void BuildLookup()
    {
        Entry[] entries = m_entries ?? Array.Empty<Entry>();
        m_lookup = new Dictionary<ShelterSoundId, EventReference>(entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            // 같은 상황이 두 줄이면 위쪽 줄을 씁니다.
            if (!m_lookup.ContainsKey(entries[i].Id))
            {
                m_lookup.Add(entries[i].Id, entries[i].Event);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_lookup = null;
        AddMissingEntries();
    }

    /// <summary>enum에 새로 추가된 상황을 표 끝에 빈 줄로 붙입니다. 기존 줄과 순서는 건드리지 않습니다.</summary>
    private void AddMissingEntries()
    {
        if (m_entries == null)
        {
            m_entries = Array.Empty<Entry>();
        }

        var existing = new HashSet<ShelterSoundId>();
        for (int i = 0; i < m_entries.Length; i++)
        {
            existing.Add(m_entries[i].Id);
        }

        List<Entry> added = null;
        foreach (ShelterSoundId id in Enum.GetValues(typeof(ShelterSoundId)))
        {
            if (id == ShelterSoundId.None || existing.Contains(id))
            {
                continue;
            }

            added ??= new List<Entry>(m_entries);
            added.Add(new Entry { Id = id });
        }

        if (added != null)
        {
            m_entries = added.ToArray();
        }
    }
#endif
}
