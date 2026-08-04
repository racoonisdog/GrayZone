using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 필드 씬의 인게임 HUD 표시를 한 곳에서 소유합니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>현재 쓰이지 않습니다.</b> 결과·게임오버 화면이 조준선을 덮는 문제는 UI 레이어로 처리했습니다
/// (<c>CrosshairPanel</c>의 sortingOrder를 −1로 두어 uGUI 캔버스 아래에 깔았습니다).
/// 무엇이 무엇 위에 오는지는 UI 시스템이 원래 담당하는 축이라, 그쪽에서 선언하는 편이 읽기 쉽습니다.
/// </para>
/// <para>
/// 이 클래스는 <b>표시 자체를 꺼야 할 때</b>를 위해 남겨 둡니다. 레이어로 가리는 것과 달라지는 경우가 둘 있습니다.
/// 반투명·부분 UI는 가려도 아래가 비치고, 가려진 HUD는 계속 갱신됩니다 - 결과창이 떠 있는 동안에도
/// <c>AimController</c>가 매 프레임 탄퍼짐을 넣고 재장전 깜빡임 알파가 돌아갑니다.
/// 쓰려면 이 컴포넌트를 씬에 붙이고 덮는 UI에서 <see cref="PushHide"/>·<see cref="PopHide"/>를 부르면 됩니다.
/// </para>
/// <para>
/// 결과 화면처럼 화면을 덮는 UI가 떴을 때 조준선·탄약 게이지 같은 인게임 표시를 내립니다.
/// </para>
/// <para>
/// 숨김 요청을 <b>사유별로 셉니다.</b> bool 하나로 두면 전체화면 UI가 겹칠 때
/// 먼저 닫힌 쪽이 아직 열려 있는 UI 위로 HUD를 되살립니다. 결과 화면과 게임오버가
/// 겹치는 경우가 실제로 있고, 같은 모양의 문제를 F9·F10 디버그 창의 커서 소유권에서 이미 겪었습니다.
/// </para>
/// <para>
/// 이벤트 구독 대신 직접 호출로 둡니다. 각 HUD가 스스로 구독하는 방식은 씬 전환과
/// 비활성 오브젝트에서 생명주기를 관리해야 하고, "지금 누가 HUD를 숨겼는지" 추적이 어렵습니다.
/// 여기서는 <see cref="ActiveHideReasons"/>로 바로 확인할 수 있습니다.
/// </para>
/// <para>
/// 입력 모드(<see cref="InputMode"/>)에 얹지 않았습니다. 디버그 트레이너도 UI 모드로 바꾸는데,
/// 조준선을 조절하려고 연 창에서 조준선이 사라지면 안 되기 때문입니다. 축이 다릅니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class FieldHudVisibility : MonoBehaviour
{
    private static FieldHudVisibility s_instance;

    /// <summary>현재 씬의 인스턴스입니다. 없으면 null입니다.</summary>
    public static FieldHudVisibility Instance => s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    [Header("References")]
    [Tooltip("조준선 컨트롤러입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private CrosshairController m_crosshair;

    [Tooltip("함께 숨길 인게임 HUD 루트 오브젝트들입니다. 체력·탄약 등 화면에 상주하는 표시를 넣습니다.")]
    [SerializeField] private List<GameObject> m_hudRoots = new List<GameObject>();

    /// <summary>지금 숨김을 요청하고 있는 사유들입니다.</summary>
    private readonly HashSet<Object> m_hideReasons = new HashSet<Object>();

    /// <summary>숨김을 요청한 사유의 수입니다. 0이면 HUD가 보입니다.</summary>
    public int ActiveHideReasons => m_hideReasons.Count;

    /// <summary>지금 HUD가 숨겨져 있는지 여부입니다.</summary>
    public bool IsHidden => m_hideReasons.Count > 0;

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning($"[FieldHudVisibility] 씬에 둘 이상 있습니다. '{name}'은 무시됩니다.", this);
            return;
        }

        s_instance = this;

        if (m_crosshair == null)
        {
            m_crosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    /// <summary>
    /// HUD를 숨기도록 요청합니다.
    /// </summary>
    /// <param name="reason">요청 주체입니다. 같은 주체가 여러 번 불러도 한 번으로 셉니다.</param>
    /// <remarks>
    /// 요청한 주체가 반드시 <see cref="PopHide"/>로 되돌려야 합니다.
    /// 되돌리지 않으면 HUD가 계속 숨겨진 채로 남습니다.
    /// </remarks>
    public void PushHide(Object reason)
    {
        if (reason == null)
        {
            Debug.LogWarning("[FieldHudVisibility] 사유 없이 숨김을 요청했습니다. 되돌릴 수 없어 무시합니다.", this);
            return;
        }

        if (m_hideReasons.Add(reason))
        {
            Apply();
        }
    }

    /// <summary>HUD 숨김 요청을 취소합니다.</summary>
    /// <param name="reason">앞서 <see cref="PushHide"/>에 넘긴 것과 같은 주체입니다.</param>
    public void PopHide(Object reason)
    {
        if (reason == null)
        {
            return;
        }

        if (m_hideReasons.Remove(reason))
        {
            Apply();
        }
    }

    /// <summary>
    /// 모든 숨김 요청을 버립니다.
    /// </summary>
    /// <remarks>
    /// 요청 주체가 되돌리지 못한 채 파괴된 경우를 위한 복구 수단입니다.
    /// 정상 경로에서는 쓰지 않습니다.
    /// </remarks>
    public void ClearAllHides()
    {
        if (m_hideReasons.Count == 0)
        {
            return;
        }

        m_hideReasons.Clear();
        Apply();
    }

    /// <summary>현재 요청 수에 맞춰 HUD 표시를 반영합니다.</summary>
    private void Apply()
    {
        bool hidden = IsHidden;

        // 조준선은 SetActive로 끄지 않습니다. AimController가 매 프레임 표시 상태를 다시 지정하므로
        // 컨트롤러 안의 억제 플래그가 그 요청을 이겨야 합니다.
        if (m_crosshair != null)
        {
            m_crosshair.SetSuppressed(hidden);
        }

        for (int i = 0; i < m_hudRoots.Count; i++)
        {
            GameObject root = m_hudRoots[i];
            if (root != null)
            {
                root.SetActive(!hidden);
            }
        }
    }
}
