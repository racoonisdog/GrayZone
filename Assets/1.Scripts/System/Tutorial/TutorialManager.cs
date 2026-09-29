using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VInspector;

/// <summary>
/// 튜토리얼 페이지 순서와 넘김 조건, 페이지별 입력 잠금을 관리합니다.
/// </summary>
/// <remarks>
/// 페이지는 한 방향으로 한 번만 진행합니다. 게임 코드는 조건을 채웠을 때
/// <see cref="NotifyEventEnd"/>에 페이지 번호를 넘깁니다. 번호는 부르는 쪽 인스펙터에 둡니다.
/// 현재 페이지와 번호가 같고 그 페이지가 이벤트를 기다리는 중일 때만 잠금이 풀립니다.
/// 범위를 벗어난 번호(0 미만 또는 <see cref="PageCount"/> 이상)는 오류로 기록합니다.
///
/// 이벤트가 페이지보다 먼저 오는 경우는 따로 기억하지 않습니다. 그 페이지에 도착하기 전의 페이지에서
/// 관련 입력을 닫아(<see cref="TutorialPage.LockedInputs"/>) 플레이어가 먼저 해 버릴 수 없게 합니다.
///
/// 방어전 이벤트(시작, 휴식, 승리)는 이 매니저가 <see cref="DefenseManager"/>를 구독해 넘깁니다.
/// 그래서 <see cref="DefenseManager"/>는 튜토리얼을 알 필요가 없습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialManager : MonoBehaviour
{
    private const int NoPage = -1;

    private static TutorialManager s_instance;

    [Header("References")]
    [Tooltip("페이지를 그릴 공용 템플릿입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseTutorialOverlay m_view;

    [Tooltip("방어전 이벤트를 받을 매니저입니다. 비어 있으면 씬에서 찾고, 없으면 방어전 이벤트는 오지 않습니다.")]
    [SerializeField] private DefenseManager m_defenseManager;

    [Header("Flow")]
    [Tooltip("켜면 씬 시작 시 첫 페이지부터 튜토리얼을 시작합니다. 끄면 Begin()을 불러야 시작합니다.")]
    [SerializeField] private bool m_playOnStart = true;

    [Tooltip("튜토리얼 페이지 목록입니다. 위에서부터 0번, 1번… 순서로 진행합니다.")]
    [SerializeField] private List<TutorialPage> m_pages = new();

    [Tooltip("InteractTap 넘김에서 탭으로 인정하는 최대 누름 시간(초)입니다. 이보다 길게 누르면 넘어가지 않습니다. " +
             "트랩 설치 홀드(기본 1초)보다 짧아야 두 입력이 겹치지 않습니다.")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float m_tapMaxSeconds = 0.3f;

    [Header("Defense Events")]
    [Tooltip("방어전이 시작되면 이벤트를 보낼 페이지 번호입니다. -1이면 보내지 않습니다.")]
    [Min(NoPage)]
    [SerializeField] private int m_defenseStartPage = NoPage;

    [Tooltip("라운드 사이 휴식이 시작되면 이벤트를 보낼 페이지 번호입니다. 매 라운드 발생합니다. -1이면 보내지 않습니다.")]
    [Min(NoPage)]
    [SerializeField] private int m_restStartPage = NoPage;

    [Tooltip("승리 조건을 채우면 이벤트를 보낼 페이지 번호입니다. -1이면 보내지 않습니다.")]
    [Min(NoPage)]
    [SerializeField] private int m_victoryReadyPage = NoPage;

    [Header("Current")]
    [Tooltip("현재 표시 중인 페이지 번호입니다. 진행 중이 아니면 -1입니다.")]
    [ReadOnly][SerializeField] private int m_currentIndex = NoPage;

    /// <summary>현재 페이지의 이벤트 잠금이 풀렸는지 여부입니다.</summary>
    private bool m_eventResolved;

    /// <summary>현재 페이지의 넘김 키를 유지한 시간(초)입니다.</summary>
    private float m_keyHoldTimer;

    private const float NoPress = -1.0f;

    /// <summary>InteractTap 판정용으로 상호작용을 누른 시각입니다. 누르지 않았으면 <see cref="NoPress"/>입니다.</summary>
    private float m_tapPressTime = NoPress;

    /// <summary>씬에 있는 활성 튜토리얼 매니저입니다. 없으면 null입니다.</summary>
    public static TutorialManager Instance => s_instance;

    /// <summary>페이지 수입니다. 이벤트로 넘길 수 있는 번호는 0부터 이 값 미만까지입니다.</summary>
    public int PageCount => m_pages.Count;

    /// <summary>튜토리얼이 진행 중인지 여부입니다.</summary>
    public bool IsRunning => m_currentIndex >= 0 && m_currentIndex < m_pages.Count;

    /// <summary>현재 페이지 번호입니다. 진행 중이 아니면 -1입니다.</summary>
    public int CurrentIndex => IsRunning ? m_currentIndex : NoPage;

    /// <summary>
    /// 현재 페이지 넘김 키의 유지 진행도(0~1)입니다. <see cref="TutorialAdvanceInput.KeyHold"/>가 아니거나 아직 이벤트를 기다리는 중이면 0입니다.
    /// </summary>
    public float KeyHoldProgress
    {
        get
        {
            if (!IsRunning
                || m_pages[m_currentIndex].AdvanceInput != TutorialAdvanceInput.KeyHold
                || !IsInputCounting(m_pages[m_currentIndex]))
            {
                return 0.0f;
            }

            float hold = m_pages[m_currentIndex].HoldSeconds;
            return hold <= 0.0f ? 0.0f : Mathf.Clamp01(m_keyHoldTimer / hold);
        }
    }

    /// <summary>페이지가 넘어갈 때 새 페이지 번호로 발생합니다.</summary>
    public event Action<int> OnPageChanged;

    /// <summary>마지막 페이지 조건까지 해결돼 튜토리얼이 끝났을 때 발생합니다. <see cref="End"/>로 끝낼 때는 발생하지 않습니다.</summary>
    public event Action OnCompleted;

    /// <summary>
    /// 게임 코드에서 튜토리얼 이벤트를 보낼 때 씁니다. 부르는 쪽 인스펙터의 페이지 번호를 그대로 넘기면 됩니다.
    /// </summary>
    /// <param name="pageIndex">이벤트를 받을 페이지 번호입니다. 음수면 "보내지 않음"으로 보고 아무것도 하지 않습니다.</param>
    /// <remarks>씬에 매니저가 없으면 아무것도 하지 않습니다. 튜토리얼이 없는 씬에서도 같은 코드를 쓸 수 있게 하기 위해서입니다.</remarks>
    public static void NotifyEventEnd(int pageIndex)
    {
        if (pageIndex < 0 || s_instance == null)
        {
            return;
        }

        s_instance.TutorialEventEnd(pageIndex);
    }

    /// <summary>
    /// 지정한 페이지의 이벤트가 끝났음을 알립니다. 그 페이지가 현재 페이지이고 이벤트를 기다리는 중이면 잠금을 풉니다.
    /// </summary>
    /// <remarks>
    /// 넘김 입력이 없는 페이지는 바로 다음 페이지로 넘어갑니다. 넘김 입력이 있으면 이때부터 입력을 셉니다.
    /// 범위를 벗어난 번호는 오류로 기록합니다. 현재 페이지가 아닌 번호는 무시합니다.
    /// </remarks>
    public void TutorialEventEnd(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= m_pages.Count)
        {
            Debug.LogError($"[{nameof(TutorialManager)}] 페이지 번호 {pageIndex}은(는) 범위를 벗어났습니다. 0~{m_pages.Count - 1}만 쓸 수 있습니다.", this);
            return;
        }

        if (!IsRunning || pageIndex != m_currentIndex || m_eventResolved)
        {
            return;
        }

        TutorialPage page = m_pages[m_currentIndex];
        if (!page.WaitEvent)
        {
            Debug.LogWarning($"[{nameof(TutorialManager)}] {pageIndex}번 페이지는 이벤트 대기가 꺼져 있어 이벤트를 무시합니다.", this);
            return;
        }

        m_eventResolved = true;
        m_keyHoldTimer = 0.0f;
        m_tapPressTime = NoPress;

        if (!page.WaitInput)
        {
            Advance();
            return;
        }

        // 이벤트로 조건이 해결됐으니 닫아 둔 입력을 엽니다. 상호작용 튜토리얼은 여기서 F가 열립니다.
        ApplyInputState();
    }

    /// <summary>첫 페이지부터 튜토리얼을 시작합니다. 페이지가 없으면 아무것도 하지 않습니다.</summary>
    public void Begin()
    {
        if (m_pages.Count == 0)
        {
            return;
        }

        EnterPage(0);
    }

    /// <summary>
    /// 튜토리얼을 즉시 끝내고 안내를 내립니다. 닫아 둔 입력도 모두 엽니다. <see cref="OnCompleted"/>는 발생하지 않습니다.
    /// </summary>
    public void End()
    {
        m_currentIndex = NoPage;
        ApplyInputState();
        ResolveView()?.Hide();
    }

    private void Reset()
    {
        // 컴포넌트를 처음 붙였을 때 기존 한 장짜리 안내와 같은 동작이 되도록 기본 페이지를 넣습니다.
        m_pages = new List<TutorialPage>
        {
            TutorialPage.CreateEventPage(
                "감염체가 몰려옵니다.\n거점을 지키세요.\n\n라운드가 끝나면 잠시 휴식이 주어집니다.",
                "[F] 3초 유지 — 방어전 시작"),
        };
        m_defenseStartPage = 0;
        ResolveReferences();
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning($"{nameof(TutorialManager)}가 씬에 둘 이상 있습니다. 먼저 등록된 쪽을 씁니다.", this);
            return;
        }

        s_instance = this;
        ResolveReferences();
    }

    private void OnEnable()
    {
        if (s_instance == null)
        {
            s_instance = this;
        }

        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();

        // 매니저가 꺼지면 입력을 풀어 줄 쪽이 없어지므로 여기서 엽니다.
        if (s_instance == this)
        {
            PlayerInputController.SetInputLock(PlayerInputLock.None);
            InteractionController.SetTargetingSuppressed(false);
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void Start()
    {
        if (!m_playOnStart)
        {
            ResolveView()?.Hide();
            return;
        }

        Begin();

        // UI 루트가 늦게 켜지면 시작 이벤트를 놓칠 수 있어, 이미 시작된 상태면 여기서 한 번 알려줍니다.
        if (m_defenseManager != null && m_defenseManager.IsGameStarted)
        {
            NotifyEventEnd(m_defenseStartPage);
        }
    }

    private void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        TutorialPage page = m_pages[m_currentIndex];
        if (!IsInputCounting(page))
        {
            return;
        }

        switch (page.AdvanceInput)
        {
            case TutorialAdvanceInput.InteractTap:
                // 탭은 뗄 때 판정하므로 상호작용 입력이 이미 풀려 있습니다. 억제할 것이 없습니다.
                if (IsInteractTapMet())
                {
                    Advance();
                }
                break;

            case TutorialAdvanceInput.KeyHold:
                if (IsKeyConditionMet(page))
                {
                    SuppressActiveInteract();
                    Advance();
                }
                break;
        }
    }

    private void OnValidate()
    {
        if (m_pages == null)
        {
            return;
        }

        for (int i = 0; i < m_pages.Count; i++)
        {
            TutorialPage page = m_pages[i];
            page.Sanitize();
            m_pages[i] = page;
        }

        WarnIfInvalidEventPage(nameof(m_defenseStartPage), m_defenseStartPage);
        WarnIfInvalidEventPage(nameof(m_restStartPage), m_restStartPage);
        WarnIfInvalidEventPage(nameof(m_victoryReadyPage), m_victoryReadyPage);
    }

    private void WarnIfInvalidEventPage(string fieldName, int pageIndex)
    {
        if (pageIndex < 0)
        {
            return;
        }

        if (pageIndex >= m_pages.Count)
        {
            Debug.LogWarning($"[{nameof(TutorialManager)}] {fieldName}={pageIndex}이(가) 페이지 수({m_pages.Count})를 넘습니다.", this);
            return;
        }

        if (!m_pages[pageIndex].WaitEvent)
        {
            Debug.LogWarning($"[{nameof(TutorialManager)}] {fieldName}={pageIndex}번 페이지의 이벤트 대기가 꺼져 있어 이벤트가 무시됩니다.", this);
        }
    }

    /// <summary>현재 페이지에서 넘김 입력을 셀 차례인지 여부입니다. 이벤트 조건이 있으면 이벤트가 온 뒤부터입니다.</summary>
    private bool IsInputCounting(TutorialPage page)
    {
        return page.WaitInput && (!page.WaitEvent || m_eventResolved);
    }

    /// <summary>
    /// 상호작용 액션을 짧게 눌렀다 뗐는지 판정합니다. 뗄 때 판정합니다.
    /// </summary>
    /// <remarks>
    /// 누를 때 판정하면 트랩 설치처럼 길게 누르는 상호작용을 시작하는 순간 페이지가 넘어갑니다.
    /// 그래서 <see cref="m_tapMaxSeconds"/>보다 짧게 누르고 뗐을 때만 인정합니다.
    /// 입력을 세기 시작한 뒤에 누른 것만 인정합니다. 이전부터 누르고 있던 입력은 누른 시각이 없어 무시됩니다.
    /// </remarks>
    private bool IsInteractTapMet()
    {
        InputAction action = ResolveActiveInteractionAction();
        if (action == null)
        {
            m_tapPressTime = NoPress;
            return false;
        }

        if (action.WasPressedThisFrame())
        {
            m_tapPressTime = Time.unscaledTime;
            return false;
        }

        if (m_tapPressTime < 0.0f)
        {
            return false;
        }

        float held = Time.unscaledTime - m_tapPressTime;
        if (held > m_tapMaxSeconds)
        {
            // 탭 한계를 넘긴 순간 이번 누름은 홀드로 봅니다. 떼도 넘어가지 않습니다.
            m_tapPressTime = NoPress;
            return false;
        }

        if (action.WasReleasedThisFrame())
        {
            m_tapPressTime = NoPress;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 현재 조작 멤버의 상호작용 액션을 가져옵니다. 키가 아니라 액션을 읽어 키 바인딩 변경을 따라갑니다.
    /// </summary>
    /// <remarks>
    /// <see cref="PlayerInputController.Interact"/>를 쓰지 않는 이유: 그 값은 입력 잠금과 억제의 영향을 받습니다.
    /// 넘김 판정은 플레이어가 실제로 눌렀는지만 봐야 합니다.
    /// </remarks>
    private static InputAction ResolveActiveInteractionAction()
    {
        SquadMemberController activeMember = SquadManager.Instance?.PlayerSquadMember;
        if (activeMember == null)
        {
            return null;
        }

        PlayerInput playerInput = activeMember.GetComponent<PlayerInput>();
        if (playerInput == null || playerInput.actions == null)
        {
            return null;
        }

        return playerInput.actions.FindAction("Interaction", false)
            ?? playerInput.actions.FindAction("Interact", false);
    }

    private bool IsKeyConditionMet(TutorialPage page)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || page.Key == Key.None)
        {
            return false;
        }

        var control = keyboard[page.Key];

        // 유지 시간이 0이면 누르는 순간만 인정합니다. 이전 페이지에서 누르고 있던 입력이 이어지지 않게 하기 위해서입니다.
        if (page.HoldSeconds <= 0.0f)
        {
            return control.wasPressedThisFrame;
        }

        if (!control.isPressed)
        {
            m_keyHoldTimer = 0.0f;
            return false;
        }

        m_keyHoldTimer += Time.deltaTime;
        return m_keyHoldTimer >= page.HoldSeconds;
    }

    private void Advance()
    {
        int next = m_currentIndex + 1;
        if (next >= m_pages.Count)
        {
            m_currentIndex = NoPage;
            ApplyInputState();
            ResolveView()?.Hide();
            OnCompleted?.Invoke();
            return;
        }

        EnterPage(next);
    }

    private void EnterPage(int index)
    {
        m_currentIndex = index;
        m_eventResolved = false;
        m_keyHoldTimer = 0.0f;
        m_tapPressTime = NoPress;

        ApplyInputState();
        ResolveView()?.Show(m_pages[index]);
        OnPageChanged?.Invoke(index);
    }

    /// <summary>
    /// 현재 페이지 상태에 맞게 입력 잠금과 상호작용 우선권을 적용합니다. 진행 중이 아니면 모두 풉니다.
    /// </summary>
    /// <remarks>
    /// 잠금: 페이지가 닫아 둔 입력은 이벤트가 해결되면 엽니다.
    /// 우선권: 조건이 상호작용 입력인 페이지에서는 상호작용 대상 탐지를 멈춰, 그 입력이 튜토리얼 조건에 먼저 쓰이게 합니다.
    /// </remarks>
    private void ApplyInputState()
    {
        PlayerInputLock locks = PlayerInputLock.None;
        bool claimInteract = false;
        if (IsRunning)
        {
            TutorialPage page = m_pages[m_currentIndex];
            bool conditionResolved = page.WaitEvent && m_eventResolved;
            if (!conditionResolved)
            {
                locks = page.LockedInputs;
            }

            claimInteract = UsesInteractForCondition(page, m_currentIndex);
        }

        PlayerInputController.SetInputLock(locks);

        bool wasClaimed = InteractionController.IsTargetingSuppressed;
        InteractionController.SetTargetingSuppressed(claimInteract);

        // 우선권을 푸는 순간 F를 누르고 있으면, 그 누름이 새 입력처럼 보여 눈앞의 트랩 설치로 이어집니다.
        // 뗄 때까지 상호작용을 막아 조건을 채운 입력이 다른 동작으로 넘어가지 않게 합니다.
        if (wasClaimed && !claimInteract)
        {
            SuppressActiveInteract();
        }
    }

    /// <summary>
    /// 이 페이지의 조건 입력이 상호작용 키인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// InteractTap 페이지는 입력을 세는 동안만 해당합니다. 이벤트를 기다리는 동안에는 플레이어가 그 이벤트를 위해
    /// 다른 상호작용을 해야 할 수 있어서입니다.
    /// 방어전 시작 페이지는 <see cref="DefenseManager"/>의 상호작용 홀드로 시작하므로 페이지 전체에서 해당합니다.
    /// </remarks>
    private bool UsesInteractForCondition(TutorialPage page, int index)
    {
        if (page.AdvanceInput == TutorialAdvanceInput.InteractTap && IsInputCounting(page))
        {
            return true;
        }

        return index == m_defenseStartPage && page.WaitEvent && !m_eventResolved;
    }

    /// <summary>
    /// 페이지를 넘긴 키가 상호작용 키와 같을 때, 그 입력이 상호작용이나 방어전 시작 홀드로 이어지지 않게 막습니다.
    /// </summary>
    /// <remarks>
    /// 넘김 키가 상호작용 키와 다르면 상호작용이 눌려 있지 않으므로, 이 억제는 다음에 읽을 때 스스로 풀립니다.
    /// </remarks>
    private static void SuppressActiveInteract()
    {
        SquadMemberController activeMember = SquadManager.Instance?.PlayerSquadMember;
        if (activeMember == null)
        {
            return;
        }

        PlayerInputController input = activeMember.GetComponent<PlayerInputController>();
        if (input != null)
        {
            input.SuppressInteractUntilRelease();
        }
    }

    private void Subscribe()
    {
        ResolveReferences();
        if (m_defenseManager == null)
        {
            return;
        }

        Unsubscribe();
        m_defenseManager.OnDefenseStarted += HandleDefenseStarted;
        m_defenseManager.OnRestStarted += HandleRestStarted;
        m_defenseManager.OnDefenseVictoryReady += HandleVictoryReady;
    }

    private void Unsubscribe()
    {
        if (m_defenseManager == null)
        {
            return;
        }

        m_defenseManager.OnDefenseStarted -= HandleDefenseStarted;
        m_defenseManager.OnRestStarted -= HandleRestStarted;
        m_defenseManager.OnDefenseVictoryReady -= HandleVictoryReady;
    }

    private void HandleDefenseStarted() => NotifyEventEnd(m_defenseStartPage);

    private void HandleRestStarted() => NotifyEventEnd(m_restStartPage);

    private void HandleVictoryReady() => NotifyEventEnd(m_victoryReadyPage);

    private DefenseTutorialOverlay ResolveView()
    {
        if (m_view == null)
        {
            m_view = FindFirstObjectByType<DefenseTutorialOverlay>(FindObjectsInactive.Include);
        }

        return m_view;
    }

    private void ResolveReferences()
    {
        ResolveView();

        if (m_defenseManager == null)
        {
            m_defenseManager = FindFirstObjectByType<DefenseManager>();
        }
    }
}
