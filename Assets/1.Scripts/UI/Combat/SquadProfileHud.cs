using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 분대원 세 명의 프로필 칸을 캐릭터별 고정 위치에 표시하고, 조작 중인 대원 칸만 선택 배경과 함께 키웁니다.
/// </summary>
/// <remarks>
/// 레이아웃은 씬에 미리 배치돼 있고(Figma <c>Reviving HUD_Description_Test01</c>의 Seoha / Chungsol / Narin 그룹),
/// 이 컴포넌트는 값(작은 체력 게이지, 선택 보간, 다운·사망 필터, 구조 가능 시간)만 갱신합니다.
///
/// 칸 주인은 분대 인덱스가 아니라 <see cref="PlayerbleCharacterId"/>로 정합니다. 조작 대상이 바뀌어도 칸은
/// 움직이지 않고 강조만 옮겨 가야 해서, 조작 멤버를 빼고 순서를 다시 매기는 <see cref="SquadHudSlotOrder"/>의
/// 팀 슬롯 규칙은 쓰지 않습니다. 조작 대상 판정(<see cref="SquadHudSlotOrder.ResolveControlled"/>)만 공유합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class SquadProfileHud : MonoBehaviour
{
    [System.Serializable]
    private sealed class ProfileSlot
    {
        [Tooltip("이 칸에 표시할 캐릭터입니다.")]
        public PlayerbleCharacterId characterId = PlayerbleCharacterId.Unknown;

        [Tooltip("캐릭터 ID가 비어 있는 데이터(씬에서 바로 시작해 출격 정보가 없는 경우)를 이 칸에 붙일 때 쓰는 분대 인덱스입니다. 음수면 쓰지 않습니다.")]
        public int fallbackSquadIndex = -1;

        [Tooltip("초상화와 작은 체력바를 함께 담은 칸 루트입니다. 피벗(가운데)을 기준으로 칸 전체가 커지고 작아집니다.")]
        public RectTransform profileRoot;

        [Tooltip("선택 상태 묶음('~ select')입니다. 조작 중일 때 알파가 1로 올라갑니다.")]
        public CanvasGroup selectRoot;

        [Tooltip("선택 배경 이미지(Select_character)입니다. 선택 보간에 맞춰 커지고 작아집니다.")]
        public RectTransform selectBackground;

        [Tooltip("선택 상태 초상화(~_Selected)입니다. 선택 보간에 맞춰 기본 초상화 크기에서 원래 크기로 커집니다.")]
        public RectTransform selectedPortrait;

        [Tooltip("기본 초상화 묶음('~ portrait base')입니다. 선택되면 알파가 0으로 내려갑니다.")]
        public CanvasGroup baseRoot;

        [Tooltip("작은 체력 게이지의 채움 Image(Filled)입니다.")]
        public Image hpGaugeFill;

        [Tooltip("다운·사망 상태 반투명 필터입니다. 선택 사항입니다.")]
        public Image statusFilter;

        [Tooltip("다운 중 남은 구조 가능 시간(초)을 표시할 텍스트입니다. 선택 사항입니다.")]
        public TMP_Text reviveTimer;

        /// <summary>현재 선택 보간 값입니다. 0은 기본 칸, 1은 선택 칸입니다.</summary>
        [System.NonSerialized] public float selection = -1.0f;
    }

    [Header("Squad")]
    [Tooltip("분대 데이터와 조작 대상을 제공하는 SquadManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Slots")]
    [Tooltip("캐릭터별 프로필 칸 목록입니다. 각 칸의 위치는 고정이고 강조만 옮겨 갑니다.")]
    [SerializeField] private ProfileSlot[] m_slots = new ProfileSlot[0];

    [Header("Selection")]
    [Tooltip("선택 강조가 켜지고 꺼지는 데 걸리는 시간(초)입니다. 0이면 바로 바뀝니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_selectionDuration = 0.18f;

    [Tooltip("조작 중인 칸 전체(초상화와 체력바)의 배율입니다. 선택되지 않은 칸은 1입니다.")]
    [Range(1.0f, 2.0f)]
    [SerializeField] private float m_selectedProfileScale = 1.15f;

    [Tooltip("선택되지 않은 칸에서 선택 배경의 배율입니다. 1에 가까울수록 배경이 커지는 폭이 작습니다.")]
    [Range(0.1f, 1.0f)]
    [SerializeField] private float m_unselectedBackgroundScale = 0.67f;

    [Header("Filter Colors")]
    [Tooltip("다운 상태 반투명 필터 색입니다.")]
    [SerializeField] private Color m_downedColor = new Color(0.85f, 0.16f, 0.13f, 0.45f);

    [Tooltip("사망(전투 이탈) 상태 반투명 필터 색입니다.")]
    [SerializeField] private Color m_deadColor = new Color(0.0f, 0.0f, 0.0f, 0.65f);

    /// <summary>선택 보간이 한 프레임에 진행할 수 있는 최대 시간(초)입니다.</summary>
    /// <remarks>
    /// 대원 전환 프레임은 카메라·AI 재설정 때문에 1초 가까이 걸리기도 합니다(실측 0.9초). 그대로 쓰면
    /// 0.18초짜리 보간이 그 한 프레임에 끝나 크기 변화가 보이지 않으므로 프레임당 진행량을 묶습니다.
    /// </remarks>
    private const float MaxSelectionStepSeconds = 1.0f / 30.0f;

    private readonly Dictionary<RectTransform, float> m_selectedPortraitBaseScale = new();

    // 칸을 아래로 정렬할 때 쓰는 씬 배치 위치(위→아래)와 그 순서의 칸 목록입니다.
    private readonly List<Vector2> m_slotPositionsTopToBottom = new();
    private readonly List<ProfileSlot> m_slotOrderTopToBottom = new();
    private readonly List<ProfileSlot> m_visibleSlots = new();
    private bool m_slotPositionsCaptured;

    private void Awake()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }
    }

    private void OnEnable()
    {
        // 다시 켜질 때는 보간 없이 현재 상태로 맞춥니다. 꺼져 있던 동안의 전환이 뒤늦게 재생되지 않게 합니다.
        foreach (ProfileSlot slot in m_slots)
        {
            if (slot != null)
            {
                slot.selection = -1.0f;
            }
        }

        UpdateSlots(0.0f);
    }

    private void LateUpdate()
    {
        UpdateSlots(Time.unscaledDeltaTime);
    }

    private void UpdateSlots(float deltaTime)
    {
        IReadOnlyList<PlayerbleUnitData> sources = m_squadManager != null ? m_squadManager.PlayerDataSources : null;
        PlayerbleUnitData controlled = SquadHudSlotOrder.ResolveControlled(sources);
        bool hasSquad = sources != null && sources.Count > 0;

        CaptureSlotPositions();
        m_visibleSlots.Clear();

        foreach (ProfileSlot slot in m_slots)
        {
            if (slot == null)
            {
                continue;
            }

            PlayerbleUnitData member = FindMember(sources, slot);

            // 출격하지 않은 캐릭터(전투 이탈 후 셸터에서 살리지 않음 등)의 칸은 숨깁니다. 칸이 캐릭터마다 고정이라
            // 그대로 두면 빠진 대원의 초상화가 남습니다. 분대 정보가 아직 없을 때는 건드리지 않습니다.
            if (slot.profileRoot != null && hasSquad)
            {
                SetActive(slot.profileRoot.gameObject, member != null);
            }

            if (member == null && hasSquad)
            {
                continue;
            }

            m_visibleSlots.Add(slot);
            bool isSelected = member != null && member == controlled;
            UpdateSelection(slot, isSelected ? 1.0f : 0.0f, deltaTime);
            ApplyHealth(slot, member);
            ApplyStatus(slot, member);
        }

        AlignVisibleSlotsToBottom();
    }

    /// <summary>씬에 배치된 칸 위치를 위에서 아래 순서로 한 번 기억합니다.</summary>
    private void CaptureSlotPositions()
    {
        if (m_slotPositionsCaptured)
        {
            return;
        }

        m_slotPositionsCaptured = true;
        foreach (ProfileSlot slot in m_slots)
        {
            if (slot?.profileRoot != null)
            {
                m_slotPositionsTopToBottom.Add(slot.profileRoot.anchoredPosition);
                m_slotOrderTopToBottom.Add(slot);
            }
        }

        // 칸 배열 순서가 화면 순서와 다를 수 있어 y 좌표(위가 큼)로 정렬합니다.
        m_slotPositionsTopToBottom.Sort((a, b) => b.y.CompareTo(a.y));
        m_slotOrderTopToBottom.Sort((a, b) => b.profileRoot.anchoredPosition.y.CompareTo(a.profileRoot.anchoredPosition.y));
    }

    /// <summary>
    /// 보이는 칸을 원래 순서대로 아래쪽 자리부터 채웁니다. 출격 인원이 적으면 위쪽 자리가 비게 됩니다.
    /// </summary>
    /// <remarks>
    /// 인원 부재(전투 이탈 후 회복하지 않음 등)로 풀 스쿼드가 아닐 때 빈 자리가 중간이나 아래에 생기지 않게
    /// 남은 초상화를 아래로 붙입니다. 세 명이 모두 있으면 각 칸이 원래 자리에 그대로 놓입니다.
    /// </remarks>
    private void AlignVisibleSlotsToBottom()
    {
        int total = m_slotPositionsTopToBottom.Count;
        if (total == 0)
        {
            return;
        }

        int visibleCount = 0;
        foreach (ProfileSlot slot in m_slotOrderTopToBottom)
        {
            if (m_visibleSlots.Contains(slot))
            {
                visibleCount++;
            }
        }

        int position = total - visibleCount;
        foreach (ProfileSlot slot in m_slotOrderTopToBottom)
        {
            if (!m_visibleSlots.Contains(slot))
            {
                continue;
            }

            Vector2 target = m_slotPositionsTopToBottom[Mathf.Clamp(position, 0, total - 1)];
            if (slot.profileRoot.anchoredPosition != target)
            {
                slot.profileRoot.anchoredPosition = target;
            }

            position++;
        }
    }

    /// <summary>칸 주인을 캐릭터 ID로 찾고, 없으면 ID가 비어 있는 데이터 가운데 지정 분대 인덱스를 씁니다.</summary>
    /// <remarks>
    /// 출격 화면을 거쳐 들어오면 스냅샷이 캐릭터 ID를 채우지만, 방어전 씬을 에디터에서 바로 시작하면 ID가
    /// Unknown으로 남습니다. 그때도 칸이 비지 않도록 인덱스로 대신 붙입니다. ID가 있는 데이터는 인덱스로
    /// 붙이지 않습니다. 다른 캐릭터 칸에 잘못 붙는 것을 막기 위해서입니다.
    /// </remarks>
    private static PlayerbleUnitData FindMember(IReadOnlyList<PlayerbleUnitData> sources, ProfileSlot slot)
    {
        if (sources == null)
        {
            return null;
        }

        if (slot.characterId != PlayerbleCharacterId.Unknown)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                PlayerbleUnitData data = sources[i];
                if (data != null && data.CharacterId == slot.characterId)
                {
                    return data;
                }
            }
        }

        if (slot.fallbackSquadIndex >= 0 && slot.fallbackSquadIndex < sources.Count)
        {
            PlayerbleUnitData data = sources[slot.fallbackSquadIndex];
            if (data != null && data.CharacterId == PlayerbleCharacterId.Unknown)
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>선택 보간 값을 목표 쪽으로 옮기고 배경·초상화 크기와 알파에 반영합니다.</summary>
    /// <remarks>
    /// 처음 갱신(<c>selection &lt; 0</c>)에서는 바로 목표 값으로 맞춥니다. 시작할 때 나린 칸이 작은 상태에서
    /// 커지는 연출이 보이지 않게 하기 위해서입니다. 일시정지 메뉴(timeScale 0)에서도 끝까지 돌도록
    /// unscaledDeltaTime을 씁니다.
    /// </remarks>
    private void UpdateSelection(ProfileSlot slot, float target, float deltaTime)
    {
        float previous = slot.selection;
        if (previous < 0.0f || m_selectionDuration <= 0.0f)
        {
            slot.selection = target;
        }
        else
        {
            float step = Mathf.Min(deltaTime, MaxSelectionStepSeconds) / m_selectionDuration;
            slot.selection = Mathf.MoveTowards(previous, target, step);
        }

        if (Mathf.Approximately(previous, slot.selection))
        {
            return;
        }

        float eased = Mathf.SmoothStep(0.0f, 1.0f, slot.selection);

        if (slot.profileRoot != null)
        {
            float scale = Mathf.Lerp(1.0f, m_selectedProfileScale, eased);
            slot.profileRoot.localScale = new Vector3(scale, scale, 1.0f);
        }

        if (slot.selectRoot != null)
        {
            slot.selectRoot.alpha = eased;
            SetActive(slot.selectRoot.gameObject, slot.selection > 0.0f);
        }

        if (slot.baseRoot != null)
        {
            slot.baseRoot.alpha = 1.0f - eased;
            SetActive(slot.baseRoot.gameObject, slot.selection < 1.0f);
        }

        if (slot.selectBackground != null)
        {
            float scale = Mathf.Lerp(m_unselectedBackgroundScale, 1.0f, eased);
            slot.selectBackground.localScale = new Vector3(scale, scale, 1.0f);
        }

        if (slot.selectedPortrait != null)
        {
            float startScale = ResolveSelectedPortraitStartScale(slot);
            float scale = Mathf.Lerp(startScale, 1.0f, eased);
            slot.selectedPortrait.localScale = new Vector3(scale, scale, 1.0f);
        }
    }

    /// <summary>선택 초상화가 기본 초상화와 같은 크기에서 출발하도록 시작 배율을 구합니다.</summary>
    private float ResolveSelectedPortraitStartScale(ProfileSlot slot)
    {
        if (m_selectedPortraitBaseScale.TryGetValue(slot.selectedPortrait, out float cached))
        {
            return cached;
        }

        float result = 1.0f;
        RectTransform baseRect = slot.baseRoot != null ? slot.baseRoot.transform as RectTransform : null;
        if (baseRect != null)
        {
            float selectedWidth = slot.selectedPortrait.rect.width;
            if (selectedWidth > 0.0f)
            {
                result = Mathf.Clamp(baseRect.rect.width / selectedWidth, 0.1f, 1.0f);
            }
        }

        m_selectedPortraitBaseScale[slot.selectedPortrait] = result;
        return result;
    }

    private static void ApplyHealth(ProfileSlot slot, PlayerbleUnitData member)
    {
        if (slot.hpGaugeFill == null)
        {
            return;
        }

        int maxHp = member != null ? member.MaxHp : 0;
        float normalized = maxHp > 0 ? Mathf.Clamp01((float)member.CurrentHp / maxHp) : 0.0f;
        slot.hpGaugeFill.fillAmount = normalized;
    }

    private void ApplyStatus(ProfileSlot slot, PlayerbleUnitData member)
    {
        bool isDead = member != null && !member.IsAlive;

        // IsDown(SquadMemberController)과 IsHealthDowned(PlayerHealth)는 별개 신호라 둘을 함께 봅니다.
        bool isDowned = member != null && member.IsAlive && (member.IsDown || member.IsHealthDowned);

        if (slot.statusFilter != null)
        {
            SetActive(slot.statusFilter.gameObject, isDead || isDowned);
            if (isDead || isDowned)
            {
                slot.statusFilter.color = isDead ? m_deadColor : m_downedColor;
            }
        }

        if (slot.reviveTimer != null)
        {
            SetActive(slot.reviveTimer.gameObject, isDowned);
            if (isDowned)
            {
                slot.reviveTimer.text = Mathf.CeilToInt(member.DownTimeRemaining).ToString();
            }
        }
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
