using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 분대원 상태 초상화(SquadStatus N)에 다운/사망 상태 필터와 구조 가능 시간 타이머를 반영합니다.
/// </summary>
/// <remarks>
/// 레이아웃(초상화·HP바·검정 배경 박스·필터·타이머)은 씬 계층에 미리 배치돼 있고, 이 컨트롤러는
/// 이름으로 자식을 찾아 값(필터 표시/색, 타이머 문자열)만 갱신합니다. 각 슬롯은 SquadManager의
/// 분대원 인덱스에 매핑되며, 슬롯 이름 뒤 숫자(예: "SquadStatus 1")를 인덱스로 사용합니다.
/// </remarks>
[DisallowMultipleComponent]
public class SquadStatusHudBinder : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        [Tooltip("SquadStatus 슬롯 루트입니다.")]
        public RectTransform root;

        [Tooltip("이 슬롯이 표시할 분대원 인덱스(SquadManager.SquadMembers 기준)입니다.")]
        public int memberIndex = 1;

        [System.NonSerialized] public Image filter;
        [System.NonSerialized] public TMP_Text timer;
        [System.NonSerialized] public bool resolved;
    }

    private const string FilterName = "Status_Filter";
    private const string TimerName = "ReviveTimer";
    private const string SlotPrefix = "SquadStatus";

    [Header("Squad")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Slots")]
    [Tooltip("비우면 자식에서 'SquadStatus N'을 자동으로 수집합니다.")]
    [SerializeField] private Slot[] m_slots;

    [Header("Filter Colors")]
    [Tooltip("다운 상태 반투명 필터 색입니다.")]
    [SerializeField] private Color m_downedColor = new Color(0.85f, 0.16f, 0.13f, 0.45f);

    [Tooltip("사망(전투 이탈) 상태 반투명 필터 색입니다.")]
    [SerializeField] private Color m_deadColor = new Color(0.0f, 0.0f, 0.0f, 0.65f);

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        UpdateSlots();
    }

    private void LateUpdate()
    {
        UpdateSlots();
    }

    private void CacheReferences()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_slots == null || m_slots.Length == 0)
        {
            AutoBuildSlots();
        }

        if (m_slots == null)
        {
            return;
        }

        foreach (Slot slot in m_slots)
        {
            ResolveSlot(slot);
        }
    }

    private void AutoBuildSlots()
    {
        var found = new System.Collections.Generic.List<Slot>();
        RectTransform[] children = GetComponentsInChildren<RectTransform>(true);

        foreach (RectTransform child in children)
        {
            if (child == null || !child.name.StartsWith(SlotPrefix))
            {
                continue;
            }

            var slot = new Slot
            {
                root = child,
                memberIndex = ParseTrailingIndex(child.name, found.Count + 1),
            };
            found.Add(slot);
        }

        m_slots = found.ToArray();
    }

    private static int ParseTrailingIndex(string name, int fallback)
    {
        int i = name.Length - 1;
        int end = i;
        while (i >= 0 && char.IsDigit(name[i]))
        {
            i--;
        }

        if (i == end)
        {
            return fallback;
        }

        string digits = name.Substring(i + 1);
        return int.TryParse(digits, out int value) ? value : fallback;
    }

    private static void ResolveSlot(Slot slot)
    {
        if (slot == null || slot.root == null || slot.resolved)
        {
            return;
        }

        Transform filter = slot.root.Find(FilterName);
        if (filter != null)
        {
            slot.filter = filter.GetComponent<Image>();
        }

        Transform timer = slot.root.Find(TimerName);
        if (timer != null)
        {
            slot.timer = timer.GetComponent<TMP_Text>();
        }

        slot.resolved = slot.filter != null || slot.timer != null;
    }

    private void UpdateSlots()
    {
        if (m_slots == null || m_squadManager == null)
        {
            return;
        }

        System.Collections.Generic.IReadOnlyList<SquadMemberController> members = m_squadManager.SquadMembers;

        foreach (Slot slot in m_slots)
        {
            if (slot == null || slot.root == null)
            {
                continue;
            }

            ResolveSlot(slot);

            SquadMemberController member = members != null
                && slot.memberIndex >= 0
                && slot.memberIndex < members.Count
                ? members[slot.memberIndex]
                : null;

            PlayerHealth health = member != null ? member.GetComponent<PlayerHealth>() : null;

            bool isDead = member != null && !member.IsAlive;
            bool isDowned = member != null
                && member.IsAlive
                && (member.IsDown || (health != null && health.IsDowned));

            ApplyFilter(slot.filter, isDead, isDowned);
            ApplyTimer(slot.timer, isDowned, health);
        }
    }

    private void ApplyFilter(Image filter, bool isDead, bool isDowned)
    {
        if (filter == null)
        {
            return;
        }

        if (isDead)
        {
            SetActive(filter.gameObject, true);
            filter.color = m_deadColor;
        }
        else if (isDowned)
        {
            SetActive(filter.gameObject, true);
            filter.color = m_downedColor;
        }
        else
        {
            SetActive(filter.gameObject, false);
        }
    }

    private static void ApplyTimer(TMP_Text timer, bool isDowned, PlayerHealth health)
    {
        if (timer == null)
        {
            return;
        }

        if (isDowned && health != null)
        {
            SetActive(timer.gameObject, true);
            timer.text = Mathf.CeilToInt(health.DownTimeRemaining).ToString();
        }
        else
        {
            SetActive(timer.gameObject, false);
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
