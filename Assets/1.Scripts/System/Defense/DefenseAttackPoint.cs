using UnityEngine;

/// <summary>
/// 방어 목표(정문 등)에서 적이 붙어서 때릴 위치 하나입니다.
/// </summary>
/// <remarks>
/// 방어 목표의 자식으로 두면 부모의 <see cref="DefenseEventHealth"/>에 스스로 등록합니다. 체력과 피격 판정은 그대로
/// 방어 목표가 가지고, 이 컴포넌트는 "어디에 서서 때릴지"만 정합니다. 큰 목표를 가장 가까운 표면 한 점으로만 때리면
/// 같은 경로로 온 적이 한곳에 모이기 때문에 둡니다.
///
/// 적은 가까이 왔을 때 이 포인트들 가운데 하나를 고릅니다(<see cref="DefenseEventHealth.PickAttackPoint"/>).
/// 예약이 아니라 지금 이 포인트를 고른 적의 수만 셉니다. 고를 때 붐비는 포인트일수록 덜 고르게 하는 데만 씁니다.
///
/// 위치는 목표 표면에 두고, 높이는 상관없습니다. 거리와 방향은 수평으로만 계산합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseAttackPoint : MonoBehaviour
{
    [Tooltip("적이 이 포인트에서 이 거리(m, 수평) 안에 들어오면 공격을 시작합니다. 손이 목표 표면에 닿는 거리로 맞춥니다. " +
             "적의 공격 시작 거리보다 크면 적의 값을 씁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_attackStartDistance = 0.5f;

    [Tooltip("선택하지 않아도 Scene 뷰에 포인트를 표시할지 여부입니다. 에디터 표시 전용입니다.")]
    [SerializeField] private bool m_alwaysDrawGizmo = true;

    /// <summary>이 포인트가 속한 방어 목표입니다. 활성화될 때 부모에서 찾습니다.</summary>
    private DefenseEventHealth m_owner;

    /// <summary>지금 이 포인트를 고른 적의 수입니다.</summary>
    private int m_occupantCount;

    /// <summary>이 포인트가 속한 방어 목표입니다.</summary>
    public DefenseEventHealth Owner => m_owner;

    /// <summary>지금 이 포인트를 고른 적의 수입니다.</summary>
    public int OccupantCount => m_occupantCount;

    /// <summary>이 포인트에서 공격을 시작하는 수평 거리(m)입니다.</summary>
    public float AttackStartDistance => m_attackStartDistance;

    /// <summary>적이 다가가고 바라볼 월드 위치입니다.</summary>
    public Vector3 Position => transform.position;

    /// <summary>이 포인트를 고른 적을 하나 셉니다.</summary>
    public void AddOccupant()
    {
        m_occupantCount++;
    }

    /// <summary>이 포인트를 놓은 적을 하나 뺍니다.</summary>
    public void RemoveOccupant()
    {
        m_occupantCount = Mathf.Max(0, m_occupantCount - 1);
    }

    private void OnEnable()
    {
        m_owner = GetComponentInParent<DefenseEventHealth>();
        m_owner?.RegisterAttackPoint(this);
    }

    private void OnDisable()
    {
        m_owner?.UnregisterAttackPoint(this);
        m_occupantCount = 0;
    }

    private void OnDrawGizmos()
    {
        if (m_alwaysDrawGizmo)
        {
            DrawGizmo();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!m_alwaysDrawGizmo)
        {
            DrawGizmo();
        }
    }

    private void DrawGizmo()
    {
        Gizmos.color = new Color(1.0f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.25f);
        Gizmos.color = new Color(1.0f, 0.45f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, m_attackStartDistance);
    }
}
