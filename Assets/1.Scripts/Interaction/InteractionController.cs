using System;
using UnityEngine;
using VInspector;

/// <summary>
/// 플레이어 조작 멤버의 상호작용 대상 탐지와 입력(탭/홀드) 처리를 담당하는 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 탐지는 근접 콘 방식입니다: 원점 반경 안에서 <see cref="IInteractable"/>를 찾고, 시야(카메라 정면) 기준 각도 콘 안에서
/// 가장 적합한 대상을 하나 고릅니다. 이 컴포넌트는 <b>상호작용 배선만</b> 담당하며, 실제 동작(부활·시설 사용 등)은
/// 각 <see cref="IInteractable"/> 구현체가 자기 도메인 로직으로 수행합니다.
/// <para>
/// 조작 멤버 판별은 같은 GameObject의 <see cref="PlayerInputController"/> 활성 여부로 합니다(비조작 멤버는 PlayerInputController가 꺼져 있어
/// 탐지/실행을 건너뜁니다). 탐지 방식을 정밀 조준까지 확장하려면 카메라 레이캐스트 결과를 우선하고 근접 탐지를 폴백으로 두는
/// 하이브리드로 넓힐 수 있습니다.
/// </para>
/// </remarks>
[RequireComponent(typeof(PlayerInputController))]
public class InteractionController : MonoBehaviour
{
    [Foldout("Detection")]
    [Tooltip("상호작용 후보를 탐지할 반경(m)입니다.")]
    [SerializeField] private float m_radius = 2.5f;

    [Tooltip("시야(카메라 정면) 기준 허용 각도(도)입니다. 이 각도 콘 안의 대상만 후보가 됩니다.")]
    [SerializeField] private float m_maxAngle = 60.0f;

    [Tooltip("각도 대비 거리 가중치입니다. 클수록 '가까운 것'보다 '정면에 가까운 것'을 더 우선합니다.")]
    [SerializeField] private float m_distanceWeight = 5.0f;

    [Tooltip("상호작용 후보 탐지에 사용할 레이어입니다. 비우면 전체를 검사합니다.")]
    [SerializeField] private LayerMask m_interactableLayers = ~0;

    [Tooltip("탐지 원점으로 사용할 Transform입니다. 비우면 자신의 Transform을 사용합니다.")]
    [SerializeField] private Transform m_originOverride;

    [Foldout("Debug")]
    [Tooltip("켜면 탐지 반경(오버랩 스피어)·정면 콘·현재 대상을 기즈모로 표시합니다. 선택하지 않아도 표시됩니다.")]
    [SerializeField] private bool m_debugDraw = true;

    [Tooltip("켜면 현재 대상 변경과 상호작용 실행을 콘솔에 로그로 남깁니다.")]
    [SerializeField] private bool m_debugLog = false;

    [Tooltip("디버그 콘을 그릴 때 사용할 테두리 선 개수입니다. 시각화 전용입니다.")]
    [SerializeField] private int m_debugConeSegments = 24;

    private const int MaxHits = 16;

    private PlayerInputController m_input;
    private Camera m_camera;
    private readonly Collider[] m_hits = new Collider[MaxHits];

    private IInteractable m_current;
    private bool m_prevPressed;
    private bool m_consumed;
    private bool m_holdStarted;
    private float m_holdTimer;

    /// <summary>현재 상호작용 대상입니다. 없으면 <c>null</c>입니다.</summary>
    public IInteractable Current => m_current;

    /// <summary>현재 홀드 진행도(0~1)입니다. 탭 대상이거나 대상이 없으면 0입니다. 프롬프트/진행바 UI에 사용합니다.</summary>
    public float HoldProgress01 { get; private set; }

    /// <summary>현재 상호작용 대상이 바뀔 때 발생합니다(없어지면 <c>null</c>). UI 프롬프트 갱신에 사용합니다.</summary>
    public event Action<IInteractable> OnCurrentChanged;

    private Vector3 Origin => (m_originOverride != null ? m_originOverride : transform).position;

    private Vector3 Facing => m_camera != null ? m_camera.transform.forward : transform.forward;

    private void Awake()
    {
        m_input = GetComponent<PlayerInputController>();
        m_camera = Camera.main;
    }

    private void OnDisable()
    {
        ResetState();
    }

    private void Update()
    {
        // 조작 멤버(입력 활성)일 때만 상호작용합니다. 그 외에는 상태를 정리합니다.
        if (m_input == null || !m_input.enabled)
        {
            if (m_current != null || m_holdTimer > 0.0f)
            {
                ResetState();
            }
            return;
        }

        if (m_camera == null)
        {
            m_camera = Camera.main;
        }

        SetCurrent(ShouldKeepActiveHoldTarget() ? m_current : FindBest());
        HandleInput();
    }

    private bool ShouldKeepActiveHoldTarget()
    {
        if (!m_holdStarted || m_current == null || !m_current.CanInteract(gameObject))
        {
            return false;
        }

        if (m_current is not Component component || component == null)
        {
            return false;
        }

        float sqrDistance = (component.transform.position - Origin).sqrMagnitude;
        return sqrDistance <= m_radius * m_radius;
    }

    /// <summary>
    /// 근접 콘 방식으로 현재 시점 최적 상호작용 대상을 찾습니다.
    /// </summary>
    /// <returns>후보가 없으면 <c>null</c>입니다.</returns>
    private IInteractable FindBest()
    {
        Vector3 origin = Origin;
        Vector3 facing = Facing;

        int count = Physics.OverlapSphereNonAlloc(origin, m_radius, m_hits, m_interactableLayers, QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = m_hits[i];
            if (col == null)
            {
                continue;
            }

            IInteractable interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null)
            {
                continue;
            }

            Component comp = interactable as Component;
            if (comp == null || comp.gameObject == gameObject)
            {
                continue;
            }

            if (!interactable.CanInteract(gameObject))
            {
                continue;
            }

            Vector3 to = comp.transform.position - origin;
            float distance = to.magnitude;
            if (distance > m_radius)
            {
                continue;
            }

            float angle = to.sqrMagnitude > 0.0001f ? Vector3.Angle(facing, to) : 0.0f;
            if (angle > m_maxAngle)
            {
                continue;
            }

            // 각도(정면 근접) 우선, 거리 보조. 점수가 낮을수록 더 적합합니다.
            float score = angle + distance * m_distanceWeight;
            if (score < bestScore)
            {
                bestScore = score;
                best = interactable;
            }
        }

        return best;
    }

    /// <summary>
    /// 현재 대상을 갱신하고, 바뀌었으면 홀드 상태를 초기화하며 이벤트를 발생시킵니다.
    /// </summary>
    /// <param name="next">이번 프레임에 선택된 대상입니다.</param>
    private void SetCurrent(IInteractable next)
    {
        if (ReferenceEquals(next, m_current))
        {
            return;
        }

        CancelActiveHold();

        m_current = next;
        m_holdTimer = 0.0f;
        HoldProgress01 = 0.0f;
        // m_consumed는 여기서 리셋하지 않는다. 한 번 누름으로 실행(예: 구조 완료)한 뒤 버튼을 계속 누른 채
        // 다음 대상으로 옮겨가면 자동으로 다시 채워지는 문제를 막기 위해, 버튼을 뗄 때(!pressed)만 리셋한다.
        OnCurrentChanged?.Invoke(m_current);

        if (m_debugLog)
        {
            Debug.Log($"[Interaction] focus → {Describe(m_current)}", this);
        }
    }

    /// <summary>
    /// 상호작용 입력의 탭/홀드 타이밍을 처리하고 조건 충족 시 실행합니다.
    /// </summary>
    private void HandleInput()
    {
        bool pressed = m_input.Interact;
        bool justPressed = pressed && !m_prevPressed;
        m_prevPressed = pressed;

        if (m_current == null)
        {
            CancelActiveHold();
            m_holdTimer = 0.0f;
            HoldProgress01 = 0.0f;
            if (!pressed)
            {
                m_consumed = false;
            }
            return;
        }

        // 떼면 홀드 상태와 1회 소비 플래그를 초기화합니다.
        if (!pressed)
        {
            CancelActiveHold();
            m_holdTimer = 0.0f;
            HoldProgress01 = 0.0f;
            m_consumed = false;
            return;
        }

        // 한 번 실행한 누름은 뗄 때까지 재실행하지 않습니다(연타/홀드 반복 방지).
        if (m_consumed)
        {
            return;
        }

        float hold = Mathf.Max(0.0f, m_current.HoldDuration);

        if (hold <= 0.0f)
        {
            // 탭: 이번 프레임에 새로 눌렸을 때만 실행합니다.
            if (justPressed)
            {
                m_consumed = Execute();
            }
            return;
        }

        // 홀드: 누르고 있는 동안 누적하고, 목표 시간을 채우면 실행합니다.
        if (m_holdTimer <= 0.0f && !m_holdStarted && !justPressed)
        {
            return;
        }

        BeginActiveHold();
        m_holdTimer += Time.deltaTime;
        HoldProgress01 = Mathf.Clamp01(m_holdTimer / hold);
        UpdateActiveHold();

        if (m_holdTimer >= hold)
        {
            bool executed = Execute();
            if (executed)
            {
                m_consumed = true;
                HoldProgress01 = 1.0f;
                CompleteActiveHold();
            }
            else
            {
                CancelActiveHold();
                HoldProgress01 = 0.0f;
            }
        }
    }

    /// <summary>
    /// 실행 직전 조건을 재확인하고 상호작용을 실행합니다.
    /// </summary>
    private bool Execute()
    {
        if (m_current == null || !m_current.CanInteract(gameObject))
        {
            return false;
        }

        IInteractable target = m_current;
        target.Interact(gameObject);

        if (m_debugLog)
        {
            Debug.Log($"[Interaction] interact ✔ {Describe(target)} (by {gameObject.name})", this);
        }

        return true;
    }

    private void BeginActiveHold()
    {
        if (m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            return;
        }

        m_holdStarted = true;
        holdInteractable.BeginHold(gameObject);
    }

    private void UpdateActiveHold()
    {
        if (!m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            return;
        }

        holdInteractable.UpdateHold(gameObject, HoldProgress01);
    }

    private void CancelActiveHold()
    {
        if (!m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            m_holdStarted = false;
            return;
        }

        holdInteractable.CancelHold(gameObject);
        m_holdStarted = false;
    }

    private void CompleteActiveHold()
    {
        if (!m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            m_holdStarted = false;
            return;
        }

        holdInteractable.CompleteHold(gameObject);
        m_holdStarted = false;
    }

    /// <summary>
    /// 디버그 로그용으로 상호작용 대상을 사람이 읽기 좋은 문자열로 만듭니다.
    /// </summary>
    /// <param name="interactable">설명할 대상입니다. <c>null</c>이면 "none"을 반환합니다.</param>
    /// <returns>대상 GameObject 이름과 프롬프트를 담은 설명 문자열입니다.</returns>
    private static string Describe(IInteractable interactable)
    {
        if (interactable == null)
        {
            return "none";
        }

        string name = interactable is Component comp && comp != null ? comp.gameObject.name : interactable.GetType().Name;
        return $"{name} (\"{interactable.GetPrompt()}\", hold={interactable.HoldDuration:0.##}s)";
    }

    /// <summary>
    /// 현재 대상과 홀드/입력 상태를 초기화합니다.
    /// </summary>
    private void ResetState()
    {
        CancelActiveHold();

        if (m_current != null)
        {
            m_current = null;
            OnCurrentChanged?.Invoke(null);
        }

        m_holdTimer = 0.0f;
        HoldProgress01 = 0.0f;
        m_prevPressed = false;
        m_consumed = false;
    }

    private void OnDrawGizmos()
    {
        if (!m_debugDraw)
        {
            return;
        }

        Vector3 origin = Origin;

        // 탐지 반경(오버랩 스피어).
        Gizmos.color = new Color(0.0f, 1.0f, 1.0f, 0.25f);
        Gizmos.DrawWireSphere(origin, m_radius);

        // 정면 콘: 경계 원(rim) + 몇 개의 모서리 레이.
        DrawDebugCone(origin, Facing, m_maxAngle, m_radius);

        // 현재 대상 강조.
        if (m_current is Component comp && comp != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, comp.transform.position);
            Gizmos.DrawWireSphere(comp.transform.position, 0.25f);
        }
    }

    /// <summary>
    /// 원점에서 지정 방향으로 펼쳐진 반각 <paramref name="halfAngle"/>의 콘을 기즈모로 그립니다(시각화 전용).
    /// </summary>
    private void DrawDebugCone(Vector3 origin, Vector3 facing, float halfAngle, float radius)
    {
        if (facing.sqrMagnitude < 0.0001f || radius <= 0.0f)
        {
            return;
        }

        facing.Normalize();
        Vector3 axis = Vector3.Cross(facing, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
        {
            axis = Vector3.Cross(facing, Vector3.right);
        }
        axis.Normalize();

        Vector3 edge = Quaternion.AngleAxis(Mathf.Clamp(halfAngle, 0.0f, 179.0f), axis) * facing;
        int segments = Mathf.Max(3, m_debugConeSegments);

        Gizmos.color = new Color(0.1f, 0.9f, 1.0f, 0.6f);
        Vector3 prev = origin + Quaternion.AngleAxis(0.0f, facing) * edge * radius;
        for (int i = 1; i <= segments; i++)
        {
            float roll = 360.0f * i / segments;
            Vector3 dir = Quaternion.AngleAxis(roll, facing) * edge;
            Vector3 point = origin + dir * radius;

            Gizmos.DrawLine(prev, point);                 // 콘 경계 원
            if (i % Mathf.Max(1, segments / 4) == 0)
            {
                Gizmos.DrawLine(origin, point);           // 모서리 레이(4개 정도)
            }

            prev = point;
        }

        // 정면 중심선.
        Gizmos.color = new Color(0.1f, 0.9f, 1.0f, 0.9f);
        Gizmos.DrawLine(origin, origin + facing * radius);
    }
}
