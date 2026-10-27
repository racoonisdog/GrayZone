using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 분대원별 스킬 쿨타임 아이콘과 상태효과(버프·디버프) 줄을 초상화 옆에, 조작 중인 대원의 스킬을 화면 하단 가운데에 표시합니다.
/// </summary>
/// <remarks>
/// 레이아웃은 씬에 미리 배치돼 있고(Figma <c>Reviving HUD_Description_Test02</c>의 Heal/Dragon Breath Cool Time UI,
/// Status UI, 하단 가운데 Narin Speed UI), 이 컴포넌트는 값(게이지, 숫자, 아이콘, 표시 여부)만 채웁니다.
///
/// 조작 중인 대원은 초상화 옆 스킬 아이콘을 숨기고 하단 가운데 큰 아이콘으로 보여 줍니다. 다른 대원으로 바꾸면
/// 그 대원의 아이콘이 가운데로 가고, 이전 대원의 아이콘은 초상화 옆에 다시 나타납니다.
///
/// 게이지는 아이콘 뒤 원형 링(Image Filled / Radial 360)입니다. 스킬 아이콘은 쿨타임만 표시해 쿨타임 중에 차오르고(쿨타임 숫자와
/// 어두운 덮개를 함께 표시), 상태효과는 남은 지속 시간만큼 줄어듭니다. 스킬 효과의 지속 시간은 스킬 아이콘이 아니라 상태효과 줄이 보여 줍니다.
///
/// 상태효과 줄은 처음 받은 순서대로 왼쪽 칸부터 채웁니다(<see cref="StatusEffectContainer.GetActiveEffects"/>).
/// 칸은 미리 배치된 개수만큼만 보여 주고, 넘치는 효과는 표시하지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class SquadAbilityHud : MonoBehaviour
{
    [System.Serializable]
    private sealed class SkillIconView
    {
        [Tooltip("아이콘 묶음 루트입니다. 스킬이 없거나 숨길 때 이 오브젝트를 끕니다.")]
        public GameObject root;

        [Tooltip("바탕과 아이콘을 묶은 CanvasGroup입니다. 쿨타임 중에는 알파를 낮춥니다(Figma 50%). 선택 항목입니다.")]
        public CanvasGroup body;

        [Tooltip("아이콘 뒤 원형 게이지(Image Filled / Radial 360)입니다. 선택 항목입니다.")]
        public Image gauge;

        [Tooltip("스킬 아이콘 이미지입니다. 하단 가운데 아이콘은 조작 대원에 맞춰 스프라이트를 바꿉니다.")]
        public Image icon;

        [Tooltip("쿨타임 중에 아이콘을 덮는 어두운 원입니다. 선택 항목입니다.")]
        public GameObject cooldownCover;

        [Tooltip("쿨타임 남은 초를 표시할 텍스트입니다. 선택 항목입니다.")]
        public TMP_Text cooldownText;

        [Tooltip("아이콘 하단 가운데에 스킬 키(예: C)를 표시할 텍스트입니다. 조작 대원 아이콘(하단 가운데)에만 연결합니다. 선택 항목입니다.")]
        public TMP_Text keyLabel;
    }

    [System.Serializable]
    private sealed class StatusSlotView
    {
        [Tooltip("칸 루트입니다. 비어 있는 칸은 이 오브젝트를 끕니다.")]
        public GameObject root;

        [Tooltip("남은 지속 시간 게이지(Image Filled / Radial 360)입니다.")]
        public Image gauge;

        [Tooltip("상태효과 아이콘 이미지입니다.")]
        public Image icon;

        [Tooltip("칸 우측 하단에 작게 쓰는 숫자(예: 청솔 특수탄 남은 발수)입니다. 선택 항목이며, 숫자가 없는 상태면 숨깁니다.")]
        public TMP_Text count;
    }

    /// <summary>상태 줄 한 칸에 그릴 내용입니다. 상태효과와 스킬 고유 상태를 같은 모양으로 다룹니다.</summary>
    private struct StatusItem
    {
        /// <summary>순서를 기억하는 기준입니다. 상태효과는 정의, 스킬 상태는 스킬 컴포넌트입니다.</summary>
        public Object key;
        public Sprite icon;
        public Sprite gauge;
        public float fill;
        public int count;
        public bool beneficial;
    }

    [System.Serializable]
    private sealed class MemberView
    {
        [Tooltip("이 칸에 표시할 캐릭터입니다.")]
        public PlayerbleCharacterId characterId = PlayerbleCharacterId.Unknown;

        [Tooltip("캐릭터 ID가 비어 있는 데이터(씬에서 바로 시작해 출격 정보가 없는 경우)를 이 칸에 붙일 때 쓰는 분대 인덱스입니다. 음수면 쓰지 않습니다.")]
        public int fallbackSquadIndex = -1;

        [Tooltip("초상화 옆 스킬 아이콘입니다. 이 대원을 조작하는 동안에는 숨깁니다.")]
        public SkillIconView skillIcon = new SkillIconView();

        [Tooltip("하단 가운데 큰 아이콘에 쓸 이 대원의 스킬 아이콘입니다. 비어 있으면 초상화 옆 아이콘의 스프라이트를 씁니다.")]
        public Sprite controlledSkillIcon;

        [Tooltip("하단 가운데 큰 아이콘 게이지에 쓸 링 이미지입니다. 비어 있으면 초상화 옆 아이콘 게이지의 스프라이트를 씁니다.")]
        public Sprite controlledSkillGauge;

        [Tooltip("스킬 고유 상태(CharacterSkill.TryGetHudStatus, 예: 청솔 특수탄 장전)를 버프 줄에 띄울 때 쓸 아이콘입니다. 비어 있으면 띄우지 않습니다.")]
        public Sprite skillStatusIcon;

        [Tooltip("스킬 고유 상태 칸의 게이지 링입니다. 비어 있으면 버프 기본 링을 씁니다.")]
        public Sprite skillStatusGauge;

        [Tooltip("버프 줄(위) 칸입니다. 왼쪽 칸부터 처음 받은 순서대로 채웁니다.")]
        public StatusSlotView[] buffSlots = new StatusSlotView[0];

        [Tooltip("디버프 줄(아래) 칸입니다. 왼쪽 칸부터 처음 받은 순서대로 채웁니다.")]
        public StatusSlotView[] debuffSlots = new StatusSlotView[0];

        [System.NonSerialized] public PlayerbleUnitData cachedMember;
        [System.NonSerialized] public CharacterSkill cachedSkill;
        [System.NonSerialized] public StatusEffectContainer cachedStatus;

        /// <summary>상태 칸을 처음 본 순서입니다. 끝난 상태는 빠지고 새 상태는 뒤에 붙어, 먼저 받은 것이 왼쪽에 남습니다.</summary>
        [System.NonSerialized] public readonly List<Object> order = new();
    }

    [Header("Squad")]
    [Tooltip("분대 데이터와 조작 대상을 제공하는 SquadManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Members")]
    [Tooltip("캐릭터별 스킬 아이콘과 상태효과 줄입니다.")]
    [SerializeField] private MemberView[] m_members = new MemberView[0];

    [Header("Controlled Skill")]
    [Tooltip("조작 중인 대원의 스킬을 크게 보여 주는 하단 가운데 아이콘입니다.")]
    [SerializeField] private SkillIconView m_controlledSkill = new SkillIconView();

    [Tooltip("쿨타임 중 스킬 바탕·아이콘의 알파입니다. Figma 쿨타임 표시는 50%입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_cooldownBodyAlpha = 0.5f;

    [Header("Status")]
    [Tooltip("상태효과 정의에 게이지 이미지가 없을 때 버프 줄에 쓸 링입니다.")]
    [SerializeField] private Sprite m_defaultBuffGauge;

    [Tooltip("상태효과 정의에 게이지 이미지가 없을 때 디버프 줄에 쓸 링입니다.")]
    [SerializeField] private Sprite m_defaultDebuffGauge;

    private readonly List<StatusEffectContainer.ActiveEffect> m_effectBuffer = new();
    private readonly List<StatusEffectContainer.ActiveEffect> m_mergedBuffer = new();
    private readonly List<StatusItem> m_itemBuffer = new();

    /// <summary>텍스트별로 마지막에 쓴 숫자입니다.</summary>
    private readonly Dictionary<TMP_Text, int> m_shownNumbers = new();

    private void Awake()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }
    }

    private void LateUpdate()
    {
        IReadOnlyList<PlayerbleUnitData> sources = m_squadManager != null ? m_squadManager.PlayerDataSources : null;
        PlayerbleUnitData controlled = SquadHudSlotOrder.ResolveControlled(sources);
        MemberView controlledView = null;

        foreach (MemberView view in m_members)
        {
            if (view == null)
            {
                continue;
            }

            PlayerbleUnitData member = SquadProfileHud.FindMember(sources, view.characterId, view.fallbackSquadIndex);
            ResolveMemberComponents(view, member);

            bool isControlled = member != null && member == controlled;
            if (isControlled)
            {
                controlledView = view;
            }

            // 조작 중인 대원의 스킬은 하단 가운데로 옮겨 가므로 초상화 옆에서는 숨깁니다.
            bool showPortraitSkill = member != null && view.cachedSkill != null && !isControlled;
            SetActive(view.skillIcon.root, showPortraitSkill);
            if (showPortraitSkill)
            {
                ApplySkill(view.skillIcon, view.cachedSkill);
            }

            ApplyStatus(view);
        }

        UpdateControlledSkill(controlledView);
    }

    /// <summary>대원이 바뀌었을 때만 스킬과 상태효과 컴포넌트를 다시 찾습니다.</summary>
    private static void ResolveMemberComponents(MemberView view, PlayerbleUnitData member)
    {
        if (view.cachedMember == member)
        {
            return;
        }

        view.cachedMember = member;
        view.cachedSkill = null;
        view.cachedStatus = null;
        if (member == null)
        {
            return;
        }

        // 스킬과 상태효과는 분대원 루트에 붙습니다(SquadManager.GetSkill, StatusEffectContainer.GetOrAdd와 같은 기준).
        view.cachedSkill = member.GetComponentInParent<CharacterSkill>();
        view.cachedStatus = member.GetComponentInParent<StatusEffectContainer>();
        if (view.cachedStatus == null)
        {
            view.cachedStatus = member.GetComponentInChildren<StatusEffectContainer>();
        }
    }

    private void UpdateControlledSkill(MemberView controlledView)
    {
        CharacterSkill skill = controlledView != null ? controlledView.cachedSkill : null;
        bool visible = skill != null;
        SetActive(m_controlledSkill.root, visible);
        if (!visible)
        {
            return;
        }

        Sprite icon = controlledView.controlledSkillIcon != null
            ? controlledView.controlledSkillIcon
            : controlledView.skillIcon.icon != null ? controlledView.skillIcon.icon.sprite : null;
        Sprite gauge = controlledView.controlledSkillGauge != null
            ? controlledView.controlledSkillGauge
            : controlledView.skillIcon.gauge != null ? controlledView.skillIcon.gauge.sprite : null;

        if (m_controlledSkill.icon != null && icon != null && m_controlledSkill.icon.sprite != icon)
        {
            m_controlledSkill.icon.sprite = icon;
        }

        if (m_controlledSkill.gauge != null && gauge != null && m_controlledSkill.gauge.sprite != gauge)
        {
            m_controlledSkill.gauge.sprite = gauge;
        }

        if (m_controlledSkill.keyLabel != null && m_squadManager != null)
        {
            // 키 바인딩이 바뀌어도 따라가도록 SquadManager의 스킬 키를 그대로 씁니다.
            string key = m_squadManager.SkillKey.ToString();
            if (m_controlledSkill.keyLabel.text != key)
            {
                m_controlledSkill.keyLabel.text = key;
            }
        }

        ApplySkill(m_controlledSkill, skill);
    }

    /// <summary>
    /// 스킬 상태에 맞춰 게이지·덮개·숫자를 채웁니다.
    /// </summary>
    /// <remarks>
    /// 스킬 아이콘은 쿨타임만 표시합니다(사용자 확정 2026-10-26). 효과 지속 시간은 상태효과 줄(버프)이 따로 보여 줍니다.
    /// 쿨타임 중: 게이지가 다시 쓸 수 있을 때까지 차오르고, 어두운 덮개와 남은 초를 보여 줍니다.
    /// 그 밖(준비됨, 효과 지속 중): 게이지와 덮개를 숨겨 아이콘만 보입니다.
    /// </remarks>
    private void ApplySkill(SkillIconView view, CharacterSkill skill)
    {
        float cooldownRemaining = skill.CooldownRemaining;
        bool isCoolingDown = cooldownRemaining > 0.0f;

        if (view.gauge != null)
        {
            SetActive(view.gauge.gameObject, isCoolingDown);
            float duration = skill.CooldownDuration;
            view.gauge.fillAmount = isCoolingDown && duration > 0.0f
                ? Mathf.Clamp01(1.0f - cooldownRemaining / duration)
                : 0.0f;
        }

        SetActive(view.cooldownCover, isCoolingDown);

        if (view.body != null)
        {
            view.body.alpha = isCoolingDown ? m_cooldownBodyAlpha : 1.0f;
        }

        if (view.cooldownText != null)
        {
            SetActive(view.cooldownText.gameObject, isCoolingDown);
            if (isCoolingDown)
            {
                SetText(view.cooldownText, Mathf.CeilToInt(cooldownRemaining));
            }
        }
    }

    /// <summary>
    /// 적용 중인 상태효과와 스킬 고유 상태를 버프·디버프 줄에 처음 받은 순서대로 채우고, 남는 칸은 끕니다.
    /// </summary>
    /// <remarks>
    /// 상태효과는 컨테이너 목록 순서로, 스킬 고유 상태는 나타난 순간 뒤에 붙습니다. 그 순서를 칸마다 기억해 두어,
    /// 나중에 받은 상태가 먼저 받은 상태의 왼쪽으로 끼어들지 않게 합니다.
    /// </remarks>
    private void ApplyStatus(MemberView view)
    {
        m_itemBuffer.Clear();
        bool alive = view.cachedMember != null && view.cachedMember.IsAlive;
        if (alive && view.cachedStatus != null)
        {
            view.cachedStatus.GetActiveEffects(m_effectBuffer);
            m_mergedBuffer.Clear();
            MergeByDefinition(m_effectBuffer, m_mergedBuffer);
            for (int i = 0; i < m_mergedBuffer.Count; i++)
            {
                StatusEffectContainer.ActiveEffect effect = m_mergedBuffer[i];
                bool beneficial = effect.Definition.IsBeneficial;
                m_itemBuffer.Add(new StatusItem
                {
                    key = effect.Definition,
                    icon = effect.Definition.HudIcon,
                    gauge = effect.Definition.HudGauge != null
                        ? effect.Definition.HudGauge
                        : beneficial ? m_defaultBuffGauge : m_defaultDebuffGauge,
                    fill = Mathf.Clamp01(effect.Remaining / effect.Definition.Duration),
                    count = -1,
                    beneficial = beneficial,
                });
            }
        }

        if (alive
            && view.cachedSkill != null
            && view.skillStatusIcon != null
            && view.cachedSkill.TryGetHudStatus(out float skillFill, out int skillCount))
        {
            m_itemBuffer.Add(new StatusItem
            {
                key = view.cachedSkill,
                icon = view.skillStatusIcon,
                gauge = view.skillStatusGauge != null ? view.skillStatusGauge : m_defaultBuffGauge,
                fill = skillFill,
                count = skillCount,
                beneficial = true,
            });
        }

        SortByFirstSeen(view.order, m_itemBuffer);
        FillRow(view.buffSlots, true);
        FillRow(view.debuffSlots, false);
    }

    /// <summary>끝난 상태를 순서 목록에서 빼고 새 상태를 뒤에 붙인 다음, 그 순서로 칸 목록을 정렬합니다.</summary>
    /// <remarks>매 프레임 도는 코드라 람다(클로저 할당) 없이 반복문으로 처리합니다. 칸 수가 적어(줄당 4칸) 삽입 정렬로 충분합니다.</remarks>
    private static void SortByFirstSeen(List<Object> order, List<StatusItem> items)
    {
        for (int i = order.Count - 1; i >= 0; i--)
        {
            if (order[i] == null || IndexOfKey(items, order[i]) < 0)
            {
                order.RemoveAt(i);
            }
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (!order.Contains(items[i].key))
            {
                order.Add(items[i].key);
            }
        }

        for (int i = 1; i < items.Count; i++)
        {
            StatusItem current = items[i];
            int rank = order.IndexOf(current.key);
            int j = i - 1;
            while (j >= 0 && order.IndexOf(items[j].key) > rank)
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = current;
        }
    }

    private static int IndexOfKey(List<StatusItem> items, Object key)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].key == key)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 같은 효과가 출처별로 여러 건 쌓여 있으면 한 칸으로 합칩니다. 순서는 처음 나온 위치, 남은 시간은 가장 긴 값입니다.
    /// HUD 아이콘이 없는 효과는 표시하지 않으므로 여기서 뺍니다.
    /// </summary>
    private static void MergeByDefinition(
        List<StatusEffectContainer.ActiveEffect> source,
        List<StatusEffectContainer.ActiveEffect> merged)
    {
        for (int i = 0; i < source.Count; i++)
        {
            StatusEffectContainer.ActiveEffect effect = source[i];
            if (effect.Definition.HudIcon == null)
            {
                continue;
            }

            int existing = -1;
            for (int k = 0; k < merged.Count; k++)
            {
                if (merged[k].Definition == effect.Definition)
                {
                    existing = k;
                    break;
                }
            }

            if (existing < 0)
            {
                merged.Add(effect);
                continue;
            }

            StatusEffectContainer.ActiveEffect previous = merged[existing];
            merged[existing] = new StatusEffectContainer.ActiveEffect(
                previous.Definition,
                Mathf.Max(previous.Remaining, effect.Remaining),
                previous.Stacks + effect.Stacks);
        }
    }

    private void FillRow(StatusSlotView[] slots, bool beneficial)
    {
        if (slots == null)
        {
            return;
        }

        int slotIndex = 0;
        for (int i = 0; i < m_itemBuffer.Count && slotIndex < slots.Length; i++)
        {
            StatusItem item = m_itemBuffer[i];
            if (item.beneficial != beneficial)
            {
                continue;
            }

            StatusSlotView slot = slots[slotIndex++];
            if (slot == null)
            {
                continue;
            }

            SetActive(slot.root, true);
            if (slot.icon != null && slot.icon.sprite != item.icon)
            {
                slot.icon.sprite = item.icon;
            }

            if (slot.gauge != null)
            {
                if (item.gauge != null && slot.gauge.sprite != item.gauge)
                {
                    slot.gauge.sprite = item.gauge;
                }

                slot.gauge.fillAmount = item.fill;
            }

            if (slot.count != null)
            {
                bool hasCount = item.count >= 0;
                SetActive(slot.count.gameObject, hasCount);
                if (hasCount)
                {
                    SetText(slot.count, item.count);
                }
            }
        }

        for (; slotIndex < slots.Length; slotIndex++)
        {
            SetActive(slots[slotIndex]?.root, false);
        }
    }

    /// <summary>
    /// 숫자가 바뀔 때만 텍스트를 다시 씁니다. 같은 값으로 매 프레임 문자열을 만들거나 텍스트 메시를 다시 만들지 않기 위해서입니다.
    /// </summary>
    private void SetText(TMP_Text text, int value)
    {
        if (m_shownNumbers.TryGetValue(text, out int shown) && shown == value)
        {
            return;
        }

        m_shownNumbers[text] = value;
        text.text = value.ToString();
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
