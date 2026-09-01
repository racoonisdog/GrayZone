using System.Text;
using UnityEngine;

/// <summary>
/// 인벤토리 키로 스쿼드 공용 인벤토리 내용을 화면에 띄우는 임시 확인용 화면입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>이것은 정식 인벤토리 UI가 아닙니다.</b> 공용 `인벤토리 시스템` §5·§7·§8·§11이 정의한 화면
/// (슬롯 그리드, 드래그 앤 드롭, 박스 연동, UI 표시 중 캐릭터 행동 제어)은 아직 없습니다.
/// 이 컴포넌트는 보유·획득 슬라이스가 실제로 도는지 눈으로 확인하기 위한 것이며, 정식 UI가 들어오면 지웁니다.
/// 그래서 <c>OnGUI</c>로 그립니다 — 캔버스·프리팹·에셋을 만들지 않아 나중에 삭제할 때 잔재가 남지 않습니다.
/// </para>
/// <para>
/// 조작 캐릭터가 아니라 <b>스쿼드</b>에서 입력을 읽습니다. 인벤토리는 스쿼드에 하나뿐이고(§3.1)
/// 캐릭터를 교체해도 같은 인벤토리를 봐야 하므로, 특정 멤버에 붙지 않고 현재 조작 멤버를 매 프레임 따라갑니다.
/// </para>
/// <para>
/// 표시 중에도 게임은 멈추지 않습니다(§6). 이 화면은 입력을 막지 않으므로 §7의 행동 제한도 적용되지 않습니다.
/// 그 제어는 정식 UI의 책임입니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class FieldInventoryDebugView : MonoBehaviour
{
    [Tooltip("인벤토리를 소유한 매니저입니다. 비어 있으면 같은 오브젝트와 씬에서 찾습니다.")]
    [SerializeField] private SquadInventoryManager m_inventoryManager;

    [Tooltip("상호작용 대상이 바뀔 때마다 콘솔에 남깁니다. 줍기 프롬프트 UI가 아직 없어 대상 포착 여부를 확인할 방법이 이것뿐입니다.")]
    [SerializeField] private bool m_logInteractionTarget = true;

    private bool m_isOpen;
    private bool m_prevInventoryPressed;
    private string m_lastLoggedPrompt = string.Empty;
    private readonly StringBuilder m_builder = new StringBuilder(512);
    private GUIStyle m_style;

    private void Awake()
    {
        if (m_inventoryManager == null)
        {
            m_inventoryManager = GetComponent<SquadInventoryManager>()
                ?? FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        }
    }

    private void Update()
    {
        PlayerInputController input = ResolvePlayerInput();
        if (input == null)
        {
            return;
        }

        bool pressed = input.Inventory;
        if (pressed && !m_prevInventoryPressed)
        {
            m_isOpen = !m_isOpen;
        }

        m_prevInventoryPressed = pressed;

        if (m_logInteractionTarget)
        {
            LogInteractionTargetChange(input);
        }
    }

    /// <summary>
    /// 조작 멤버의 상호작용 대상이 바뀌면 한 번 남깁니다.
    /// </summary>
    /// <remarks>
    /// 프롬프트 UI 소비자가 아직 없어(<c>InteractionController.OnCurrentChanged</c> 구독자 0개)
    /// 대상을 잡았는지 화면으로는 알 수 없습니다. 줍기가 안 될 때 "대상을 못 잡은 것"인지
    /// "잡았는데 실행이 안 된 것"인지 가르는 것이 이 로그의 목적입니다.
    /// </remarks>
    private void LogInteractionTargetChange(PlayerInputController input)
    {
        InteractionController interaction = input.GetComponent<InteractionController>();
        string prompt = interaction != null && interaction.Current != null
            ? interaction.Current.GetPrompt()
            : string.Empty;

        if (prompt == m_lastLoggedPrompt)
        {
            return;
        }

        m_lastLoggedPrompt = prompt;
        Debug.Log(string.IsNullOrEmpty(prompt)
            ? "[FieldInventoryDebugView] 상호작용 대상 없음"
            : $"[FieldInventoryDebugView] 상호작용 대상: {prompt} (E 키)");
    }

    /// <summary>현재 조작 중인 멤버의 입력 컨트롤러를 찾습니다.</summary>
    private static PlayerInputController ResolvePlayerInput()
    {
        SquadManager squad = SquadManager.Instance;
        SquadMemberController member = squad != null ? squad.PlayerSquadMember : null;
        return member != null ? member.GetComponent<PlayerInputController>() : null;
    }

    private void OnGUI()
    {
        if (!m_isOpen || m_inventoryManager == null)
        {
            return;
        }

        m_style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };

        SlotContainer<string> inventory = m_inventoryManager.Inventory;
        m_builder.Clear();
        m_builder.AppendLine($"스쿼드 공용 인벤토리  ({inventory.SlotCount}칸, 해금 {inventory.UnlockedCount})");
        m_builder.AppendLine("─────────────────────────────");

        int usedSlots = 0;
        for (int i = 0; i < inventory.SlotCount; i++)
        {
            if (inventory.IsEmpty(i))
            {
                continue;
            }

            usedSlots++;
            string id = inventory.GetContent(i);
            string displayName = m_inventoryManager.TryGetSlotDefinition(i, out ItemDefinition definition)
                ? definition.DisplayName
                : id;

            m_builder.AppendLine($"[{i:00}] {displayName} x{inventory.GetQuantity(i)}");
        }

        if (usedSlots == 0)
        {
            m_builder.AppendLine("(비어 있음)");
        }

        GUI.Box(new Rect(20.0f, 20.0f, 320.0f, 40.0f + usedSlots * 20.0f + 40.0f), string.Empty);
        GUI.Label(new Rect(32.0f, 30.0f, 300.0f, 400.0f), m_builder.ToString(), m_style);
    }
}
