using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 스쿼드 공용 인벤토리를 슬롯 그리드로 투영하는 필드 인벤토리 화면입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>층 경계</b>: 이 컴포넌트는 <b>투영과 입력</b>만 맡습니다. 슬롯 판정은 <see cref="SlotContainer{T}"/>가,
/// 인벤토리 고유 규칙(드래그 드롭 판정, 정렬 기준, 버리기, 창고 이관)은 <c>System/Slot/Field</c>의 확장이
/// 소유합니다. 세 층이 아는 것이 서로 다릅니다 — 코어는 아이템 ID 문자열만, 확장은 거기에
/// <see cref="ItemDefinitionCatalog"/>까지, 이 층은 거기에 Unity까지.
/// </para>
/// <para>
/// <b>칸 수는 <see cref="SlotContainer{T}.SlotCount"/>로 만듭니다.</b>
/// <see cref="SlotContainer{T}.UnlockedCount"/>는 해금된 <i>개수</i>이지 앞에서부터의 <i>범위</i>가 아닙니다
/// (<see cref="SlotContainer{T}.TryUnlock"/>은 임의 인덱스를 열 수 있어 3번이 잠긴 채 7번이 열릴 수 있습니다).
/// 전체 칸만큼 뷰를 한 번 만들어 두면 해금 시 그 칸의 자물쇠만 벗기면 되고 재생성이 없습니다.
/// </para>
/// <para>
/// <b>갱신은 프레임 끝에 한 번</b>입니다. 바뀐 인덱스를 모아 두었다가 <see cref="LateUpdate"/>에서 그립니다.
/// 정렬 같은 확장이 <c>TrySwap</c>을 여러 번 부르면 <c>SlotChanged</c>가 그 횟수만큼 발생하는데,
/// 확장은 <c>BulkChanged</c>를 발생시킬 수 없기 때문입니다(코어의 event라 클래스 밖에서 호출 불가).
/// 코어에 배치 모드를 여는 대신 소비 측에서 병합합니다. 덤으로 이벤트 핸들러 안에서 컨테이너를
/// 다시 만지는 재진입도 피합니다.
/// </para>
/// <para>
/// <b>토글 키를 <see cref="PlayerInputController"/>가 아니라 액션에서 직접 읽는 이유</b>:
/// 화면을 열 때 §7에 따라 <see cref="FieldManager.SetInputMode"/>로 캐릭터 입력을 막는데, 그 게이트가
/// <see cref="PlayerInputController"/>의 입력 프로퍼티를 전부 <c>false</c>로 만들어 <b>같은 키로 창을 닫을 수 없게</b>
/// 됩니다. 인벤토리 키는 캐릭터 조작이 아니라 UI 입력이므로 게이트 대상이 아닙니다.
/// 대가로 액션 이름 <c>"Inventory"</c>를 이 파일도 알게 되며, 이 중복이 부담스러워지면
/// <see cref="PlayerInputController"/>에 게이트를 무시하는 읽기 경로를 두는 편이 낫습니다.
/// </para>
/// <para>
/// <b>범위</b>: 읽기 전용 표시까지입니다. 드래그 앤 드롭(§11·§12), 툴팁(§8), 버리기(§14),
/// 인벤토리 박스 연동(§5.2)은 아직 없습니다. 기존 <see cref="FieldInventoryDebugView"/>는
/// 이 화면이 씬에 배선되어 검증될 때까지 남겨 두고, 그 뒤에 지웁니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class FieldInventoryUI : MonoBehaviour
{
    [Header("Ownership")]
    [Tooltip("인벤토리를 소유한 매니저입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadInventoryManager m_inventoryManager;

    [Tooltip("입력 모드 전환에 쓰는 필드 매니저입니다. 비어 있으면 씬에서 찾습니다. 없으면 캐릭터 입력을 막지 못한 채로 창만 열립니다.")]
    [SerializeField] private FieldManager m_fieldManager;

    [Header("View")]
    [Tooltip("창 전체의 루트입니다. 이 오브젝트를 켜고 끄는 것이 곧 여닫기입니다.")]
    [SerializeField] private GameObject m_viewRoot;

    [Tooltip("슬롯 칸을 생성할 부모입니다. Grid Layout Group을 붙여 두면 정렬은 uGUI가 맡습니다.")]
    [SerializeField] private RectTransform m_gridRoot;

    [Tooltip("칸 하나를 그리는 프리팹입니다. 없으면 그리드를 만들 수 없어 창이 비어 보입니다.")]
    [SerializeField] private FieldInventorySlotView m_slotViewPrefab;

    [Header("Behaviour")]
    [Tooltip("창이 열려 있는 동안 인게임 HUD(조준선·탄약 등)를 내립니다.")]
    [SerializeField] private bool m_hideHudWhileOpen = true;

    [Tooltip("창을 열 때 캐릭터 입력을 UI 모드로 막습니다. 공용 문서 §7의 행동 제한입니다.")]
    [SerializeField] private bool m_blockGameplayInputWhileOpen = true;

    private readonly List<FieldInventorySlotView> m_slotViews = new List<FieldInventorySlotView>();
    private readonly HashSet<int> m_dirtySlots = new HashSet<int>();

    private bool m_isOpen;
    private bool m_isAllDirty;
    private bool m_isTogglePressed;
    private bool m_isSubscribed;
    private bool m_isHudHiddenByThis;
    private bool m_isSetupErrorLogged;

#if ENABLE_INPUT_SYSTEM
    private PlayerInputController m_cachedInputOwner;
    private InputAction m_cachedToggleAction;
#endif

    /// <summary>창이 열려 있는지 여부입니다.</summary>
    public bool IsOpen => m_isOpen;

    private void Awake()
    {
        if (m_inventoryManager == null)
            m_inventoryManager = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);

        if (m_fieldManager == null)
            m_fieldManager = FindFirstObjectByType<FieldManager>(FindObjectsInactive.Include);

        if (m_viewRoot != null)
            m_viewRoot.SetActive(false);
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();

        // 비활성화되면서 숨김 요청이 남으면 HUD가 영영 안 돌아옵니다. 사유별로 세는 쪽이라 반드시 짝을 맞춥니다.
        ReleaseHudHide();
    }

    private void Update()
    {
        if (!TryReadToggleInput(out bool pressed))
            return;

        if (pressed && !m_isTogglePressed)
            Toggle();

        m_isTogglePressed = pressed;
    }

    private void LateUpdate()
    {
        if (!m_isOpen)
            return;

        FlushDirtySlots();
    }

    /// <summary>창을 열거나 닫습니다.</summary>
    public void Toggle()
    {
        SetOpen(!m_isOpen);
    }

    /// <summary>창의 열림 상태를 지정합니다.</summary>
    /// <param name="open">열면 <c>true</c>입니다.</param>
    public void SetOpen(bool open)
    {
        if (open == m_isOpen)
            return;

        if (open && !CanOpen())
            return;

        m_isOpen = open;

        if (open)
        {
            EnsureSlotViews();

            // 닫혀 있는 동안 들어온 변경은 개별로 추적하지 않았으므로 전체를 다시 그립니다.
            m_isAllDirty = true;
            FlushDirtySlots();
        }

        if (m_viewRoot != null)
            m_viewRoot.SetActive(open);

        ApplyHudHide(open);
        ApplyInputMode(open);
    }

    /// <summary>
    /// 창을 열어도 되는 상태인지 확인합니다.
    /// </summary>
    /// <remarks>
    /// 결과 화면이나 게임오버가 이미 입력을 UI 모드로 바꿔 둔 상태에서 인벤토리가 열리면,
    /// 인벤토리를 닫을 때 <see cref="InputMode.Gameplay"/>로 되돌리면서 그 화면들의 입력 차단까지 풀어 버립니다.
    /// 겹침 자체를 막는 것이 이 슬라이스에서 가장 싼 방어입니다.
    /// </remarks>
    private bool CanOpen()
    {
        if (m_inventoryManager == null)
        {
            LogSetupErrorOnce("SquadInventoryManager를 찾지 못해 인벤토리 화면을 열 수 없습니다.");
            return false;
        }

        if (m_blockGameplayInputWhileOpen
            && m_fieldManager != null
            && m_fieldManager.CurrentInputMode == InputMode.UI)
        {
            return false;
        }

        return true;
    }

    private void Subscribe()
    {
        if (m_isSubscribed || m_inventoryManager == null)
            return;

        m_inventoryManager.SlotChanged += HandleSlotChanged;
        m_inventoryManager.BulkChanged += HandleBulkChanged;
        m_isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!m_isSubscribed || m_inventoryManager == null)
            return;

        m_inventoryManager.SlotChanged -= HandleSlotChanged;
        m_inventoryManager.BulkChanged -= HandleBulkChanged;
        m_isSubscribed = false;
    }

    /// <summary>
    /// 바뀐 칸을 표시만 해 둡니다.
    /// </summary>
    /// <remarks>
    /// 여기서 바로 그리지 않는 이유는 두 가지입니다. 한 연산이 여러 칸을 바꾸면 통지도 여러 번 오고,
    /// 이 핸들러는 컨테이너 연산 도중에 동기적으로 실행됩니다. 그리기를 프레임 끝으로 미루면
    /// 중복 갱신이 사라지고, 그리는 시점에는 연산이 이미 끝나 있습니다.
    /// </remarks>
    private void HandleSlotChanged(int index)
    {
        if (m_isAllDirty)
            return;

        m_dirtySlots.Add(index);
    }

    private void HandleBulkChanged()
    {
        m_isAllDirty = true;
        m_dirtySlots.Clear();
    }

    private void FlushDirtySlots()
    {
        if (!m_isAllDirty && m_dirtySlots.Count == 0)
            return;

        if (m_inventoryManager == null || m_slotViews.Count == 0)
        {
            m_isAllDirty = false;
            m_dirtySlots.Clear();
            return;
        }

        if (m_isAllDirty)
        {
            for (int i = 0; i < m_slotViews.Count; i++)
                RefreshSlot(i);
        }
        else
        {
            foreach (int index in m_dirtySlots)
                RefreshSlot(index);
        }

        m_isAllDirty = false;
        m_dirtySlots.Clear();
    }

    private void RefreshSlot(int index)
    {
        if (index < 0 || index >= m_slotViews.Count)
            return;

        FieldInventorySlotView view = m_slotViews[index];
        if (view == null)
            return;

        SlotContainer<string> inventory = m_inventoryManager.Inventory;

        if (!inventory.IsUnlocked(index))
        {
            view.ShowLocked();
            return;
        }

        if (inventory.IsEmpty(index))
        {
            view.ShowEmpty();
            return;
        }

        string itemId = inventory.GetContent(index);
        Sprite icon = null;
        string displayName = itemId;

        if (m_inventoryManager.TryGetSlotDefinition(index, out ItemDefinition definition) && definition != null)
        {
            icon = definition.Icon;
            displayName = definition.DisplayName;
        }

        view.ShowItem(icon, displayName, inventory.GetQuantity(index));
    }

    /// <summary>
    /// 전체 칸 수만큼 슬롯 뷰를 한 번만 만듭니다.
    /// </summary>
    /// <remarks>
    /// 용량은 해금으로만 늘어나고 <see cref="SlotContainer{T}.SlotCount"/>는 생성 이후 변하지 않으므로,
    /// 여기서 만든 뷰는 재생성이 필요 없습니다.
    /// </remarks>
    private void EnsureSlotViews()
    {
        if (m_slotViews.Count > 0)
            return;

        if (m_slotViewPrefab == null || m_gridRoot == null)
        {
            LogSetupErrorOnce("슬롯 프리팹 또는 그리드 루트가 배선되지 않아 인벤토리 칸을 만들 수 없습니다.");
            return;
        }

        int slotCount = m_inventoryManager.Inventory.SlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            FieldInventorySlotView view = Instantiate(m_slotViewPrefab, m_gridRoot);
            view.Initialize(i);
            m_slotViews.Add(view);
        }
    }

    private void ApplyHudHide(bool open)
    {
        if (!m_hideHudWhileOpen)
            return;

        if (open)
        {
            if (m_isHudHiddenByThis || FieldHudVisibility.Instance == null)
                return;

            FieldHudVisibility.Instance.PushHide(this);
            m_isHudHiddenByThis = true;
            return;
        }

        ReleaseHudHide();
    }

    private void ReleaseHudHide()
    {
        if (!m_isHudHiddenByThis)
            return;

        if (FieldHudVisibility.Instance != null)
            FieldHudVisibility.Instance.PopHide(this);

        m_isHudHiddenByThis = false;
    }

    private void ApplyInputMode(bool open)
    {
        if (!m_blockGameplayInputWhileOpen || m_fieldManager == null)
            return;

        InputMode mode = open ? InputMode.UI : InputMode.Gameplay;
        if (m_fieldManager.SetInputMode(mode) != 1)
        {
            Debug.LogWarning(
                $"[FieldInventoryUI] 입력 모드를 {mode}로 바꾸지 못했습니다. 화면 표시는 그대로 진행합니다.",
                this);
        }
    }

    /// <summary>
    /// 현재 조작 멤버의 인벤토리 토글 입력을 읽습니다.
    /// </summary>
    /// <remarks>
    /// 조작 멤버가 바뀌면 액션 인스턴스도 바뀌므로 소유자를 기억해 두고 달라졌을 때만 다시 찾습니다.
    /// </remarks>
    /// <param name="pressed">현재 눌린 상태입니다.</param>
    /// <returns>입력을 읽을 수 있으면 <c>true</c>입니다.</returns>
    private bool TryReadToggleInput(out bool pressed)
    {
        pressed = false;

#if ENABLE_INPUT_SYSTEM
        SquadManager squad = SquadManager.Instance;
        SquadMemberController member = squad != null ? squad.PlayerSquadMember : null;
        PlayerInputController owner = member != null ? member.GetComponent<PlayerInputController>() : null;
        if (owner == null)
            return false;

        if (owner != m_cachedInputOwner)
        {
            m_cachedInputOwner = owner;
            m_cachedToggleAction = null;

            PlayerInput playerInput = owner.GetComponent<PlayerInput>();
            if (playerInput != null && playerInput.actions != null)
                m_cachedToggleAction = playerInput.actions.FindAction("Inventory", false);
        }

        if (m_cachedToggleAction == null)
            return false;

        pressed = m_cachedToggleAction.IsPressed();
        return true;
#else
        return false;
#endif
    }

    private void LogSetupErrorOnce(string message)
    {
        if (m_isSetupErrorLogged)
            return;

        m_isSetupErrorLogged = true;
        Debug.LogError($"[FieldInventoryUI] {message}", this);
    }
}
