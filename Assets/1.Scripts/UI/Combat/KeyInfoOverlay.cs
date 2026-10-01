using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 조작키 안내(KeyInfo) 이미지를 화면 가운데에 띄우고, 떠 있는 동안 게임 시간을 멈춥니다.
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: 이 컴포넌트는 UI를 만들지 않고, 씬에 미리 배치된 자식 'Panel'을 켜고 끕니다.
/// 크기와 위치는 씬에서 정합니다(현재 화면의 2/3, 가운데).
///
/// 시간 정지는 <see cref="GameplayPauseMenuController"/>와 같은 방식입니다: 열 때 <see cref="Time.timeScale"/>을 저장하고 0으로,
/// 닫을 때 저장한 값으로 되돌립니다. 그래서 방어전 라운드 타이머도 함께 멈춥니다.
/// 다른 UI가 이미 시간을 멈춘 상태(일시정지 메뉴 등)에서는 열지 않습니다. 둘이 서로의 저장값을 덮어쓰지 않게 하기 위해서입니다.
///
/// Escape로도 닫습니다. 닫기는 LateUpdate에서 처리합니다. 같은 프레임의 Update에서 일시정지 메뉴가
/// 아직 멈춘 시간을 보고 열리지 않게 하기 위해서입니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class KeyInfoOverlay : MonoBehaviour
{
    private const string PanelName = "Panel";

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("조작키 안내 패널 루트입니다. 비어 있으면 자식 'Panel'을 찾습니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Header("Input")]
    [Tooltip("안내를 열고 닫는 키입니다. 튜토리얼 1페이지 문구의 키 아이콘(H)과 맞춰 둡니다.")]
    [SerializeField] private Key m_toggleKey = Key.H;

    [Tooltip("켜면 Escape로도 닫습니다.")]
    [SerializeField] private bool m_closeWithEscape = true;

    [Header("Behaviour")]
    [Tooltip("켜면 안내가 떠 있는 동안 Time.timeScale을 0으로 만들어 게임 시간을 멈춥니다.")]
    [SerializeField] private bool m_pauseTime = true;

    private SquadManager m_squadManager;
    private float m_previousTimeScale = 1.0f;
    private bool m_isOpen;
    private bool m_closeRequested;

    /// <summary>안내가 현재 떠 있는지 여부입니다.</summary>
    public bool IsOpen => m_isOpen;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        SetPanelVisible(false);
    }

    private void OnDisable()
    {
        if (m_isOpen)
        {
            Close();
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (m_toggleKey != Key.None && keyboard[m_toggleKey].wasPressedThisFrame)
        {
            if (m_isOpen)
            {
                m_closeRequested = true;
            }
            else
            {
                Open();
            }

            return;
        }

        if (m_isOpen && m_closeWithEscape && keyboard.escapeKey.wasPressedThisFrame)
        {
            m_closeRequested = true;
        }
    }

    private void LateUpdate()
    {
        if (!m_closeRequested)
        {
            return;
        }

        m_closeRequested = false;
        Close();
    }

    /// <summary>
    /// 안내를 엽니다. 이미 열려 있거나 다른 UI가 시간을 멈춘 상태면 아무것도 하지 않습니다.
    /// </summary>
    public void Open()
    {
        if (m_isOpen || (m_pauseTime && Time.timeScale <= 0.0f))
        {
            return;
        }

        m_isOpen = true;
        m_previousTimeScale = Time.timeScale;
        if (m_pauseTime)
        {
            Time.timeScale = 0.0f;
        }

        // 시간이 멈춰도 마우스 시점 회전은 프레임 시간을 쓰지 않아 계속 돌아갑니다. 그래서 입력도 함께 막습니다.
        SetGameplayInputEnabled(false);
        SetPanelVisible(true);
    }

    /// <summary>안내를 닫고 저장해 둔 게임 시간으로 되돌립니다.</summary>
    public void Close()
    {
        if (!m_isOpen)
        {
            return;
        }

        m_isOpen = false;
        SetPanelVisible(false);
        if (m_pauseTime)
        {
            Time.timeScale = m_previousTimeScale;
        }

        SetGameplayInputEnabled(true);
    }

    private void SetGameplayInputEnabled(bool enabled)
    {
        if (m_squadManager == null)
        {
            m_squadManager = SquadManager.Instance != null
                ? SquadManager.Instance
                : FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        }

        if (m_squadManager != null)
        {
            m_squadManager.ApplyInputModeToSquad(enabled);
        }
    }

    private void AutoFindReferences()
    {
        if (m_panelRoot == null)
        {
            Transform panel = transform.Find(PanelName);
            if (panel != null)
            {
                m_panelRoot = panel.gameObject;
            }
        }
    }

    private void SetPanelVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }
}
