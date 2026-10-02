using System.Collections.Generic;
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
/// 고른 적 가운데 앞에서 때릴 수 있는 수는 <see cref="Capacity"/>로 정하고, 나머지는 포인트 뒤쪽 대기 줄에 섭니다.
/// 같은 점으로 여러 마리가 비집고 들어가면 서로 밀며 떨리기 때문입니다. 앞자리가 비면 가장 가까운 대기 적이 들어갑니다.
///
/// 이 오브젝트의 정면(파란 축)이 목표 바깥쪽을 향하게 둡니다. 대기 줄은 그 방향으로 뒤에 섭니다.
/// 높이는 상관없습니다. 거리와 방향은 수평으로만 계산합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseAttackPoint : MonoBehaviour
{
    [Tooltip("이 포인트 앞에서 동시에 때릴 수 있는 적의 수입니다. 넘치는 적은 뒤쪽 대기 줄에서 기다립니다.")]
    [Min(1)]
    [SerializeField] private int m_capacity = 2;

    [Tooltip("앞자리 적이 멈춰 설 거리(m, 수평)입니다. 피격 영역이 목표 앞으로 나와 있으면 그만큼 떨어져 서도 맞습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_standDistance = 1.0f;

    [Tooltip("적이 이 포인트에서 이 거리(m, 수평) 안에 들어오면 공격을 시작합니다. 적의 공격 시작 거리보다 크면 적의 값을 씁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_attackStartDistance = 1.2f;

    [Tooltip("대기 줄 첫 줄이 포인트에서 떨어진 거리(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waitDistance = 2.5f;

    [Tooltip("대기 줄이 한 줄 늘 때마다 더 뒤로 물러나는 거리(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waitRowSpacing = 1.2f;

    [Tooltip("같은 대기 줄에 선 적끼리의 좌우 간격(m)입니다. 좁으면 서로 밀며 떨립니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waitSlotSpacing = 1.2f;

    [Tooltip("대기 위치를 좌우로 흩뜨리는 최대 거리(m)입니다. 줄이 기계적으로 보이지 않게 합니다. 좌우 간격의 절반보다 작게 둡니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waitLateralJitter = 0.35f;

    [Tooltip("선택하지 않아도 Scene 뷰에 포인트를 표시할지 여부입니다. 에디터 표시 전용입니다.")]
    [SerializeField] private bool m_alwaysDrawGizmo = true;

    /// <summary>이 포인트가 속한 방어 목표입니다. 활성화될 때 부모에서 찾습니다.</summary>
    private DefenseEventHealth m_owner;

    /// <summary>앞에서 때리는 적입니다. <see cref="m_capacity"/>를 넘지 않습니다.</summary>
    private readonly List<EnemyController> m_holders = new List<EnemyController>();

    /// <summary>앞자리가 나기를 기다리는 적입니다. 들어온 순서대로 줄 번호가 정해집니다.</summary>
    private readonly List<EnemyController> m_waiters = new List<EnemyController>();

    /// <summary>이 포인트가 속한 방어 목표입니다.</summary>
    public DefenseEventHealth Owner => m_owner;

    /// <summary>지금 이 포인트를 고른 적의 수(앞자리 + 대기)입니다.</summary>
    public int OccupantCount => m_holders.Count + m_waiters.Count;

    /// <summary>앞에서 동시에 때릴 수 있는 적의 수입니다.</summary>
    public int Capacity => m_capacity;

    /// <summary>앞자리가 비어 있는지 여부입니다.</summary>
    public bool HasFreeSlot => m_holders.Count < m_capacity;

    /// <summary>앞자리 적이 멈춰 설 수평 거리(m)입니다.</summary>
    public float StandDistance => m_standDistance;

    /// <summary>이 포인트에서 공격을 시작하는 수평 거리(m)입니다.</summary>
    public float AttackStartDistance => m_attackStartDistance;

    /// <summary>적이 다가가고 바라볼 월드 위치입니다.</summary>
    public Vector3 Position => transform.position;

    /// <summary>목표 바깥쪽을 향하는 수평 방향입니다. 대기 줄을 세울 때 씁니다.</summary>
    public Vector3 Outward
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0.0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    /// <summary>적을 이 포인트에 넣습니다. 앞자리가 있으면 앞자리, 없으면 대기 줄 끝입니다.</summary>
    public void AddOccupant(EnemyController enemy)
    {
        if (enemy == null || m_holders.Contains(enemy) || m_waiters.Contains(enemy))
        {
            return;
        }

        if (m_holders.Count < m_capacity)
        {
            m_holders.Add(enemy);
        }
        else
        {
            m_waiters.Add(enemy);
        }
    }

    /// <summary>적을 이 포인트에서 뺍니다. 앞자리가 비면 가장 가까운 대기 적을 올립니다.</summary>
    public void RemoveOccupant(EnemyController enemy)
    {
        if (m_holders.Remove(enemy))
        {
            PromoteWaiter();
            return;
        }

        m_waiters.Remove(enemy);
    }

    /// <summary>지정한 적이 앞자리에 있는지 여부입니다.</summary>
    public bool IsHolder(EnemyController enemy)
    {
        return m_holders.Contains(enemy);
    }

    /// <summary>
    /// 지정한 적이 기다릴 월드 위치를 돌려줍니다.
    /// </summary>
    /// <param name="enemy">대기 줄에 있는 적입니다.</param>
    /// <param name="lateralSeed">-1~1 사이의 개체별 고정 값입니다. 좌우로 흩뜨리는 데 씁니다.</param>
    /// <remarks>앞자리 수만큼 한 줄에 서고, 그보다 많으면 한 줄씩 뒤로 물러납니다.</remarks>
    public Vector3 GetWaitPosition(EnemyController enemy, float lateralSeed)
    {
        int index = Mathf.Max(0, m_waiters.IndexOf(enemy));
        int perRow = Mathf.Max(1, m_capacity);
        int row = index / perRow;
        int slot = index % perRow;
        // 같은 줄 안에서는 자리를 일정 간격으로 나눕니다. 무작위로만 흩으면 두 마리가 거의 같은 자리를 받아 서로 밉니다.
        float slotOffset = (slot - (perRow - 1) * 0.5f) * m_waitSlotSpacing;
        Vector3 outward = Outward;
        Vector3 lateral = Vector3.Cross(Vector3.up, outward);
        return Position
            + outward * (m_waitDistance + row * m_waitRowSpacing)
            + lateral * (slotOffset + Mathf.Clamp(lateralSeed, -1.0f, 1.0f) * m_waitLateralJitter);
    }

    private void PromoteWaiter()
    {
        if (m_waiters.Count == 0 || m_holders.Count >= m_capacity)
        {
            return;
        }

        int best = -1;
        float bestDistance = float.PositiveInfinity;
        for (int i = m_waiters.Count - 1; i >= 0; i--)
        {
            EnemyController waiter = m_waiters[i];
            if (waiter == null)
            {
                m_waiters.RemoveAt(i);
                continue;
            }

            float distance = (waiter.transform.position - Position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        if (best < 0)
        {
            return;
        }

        m_holders.Add(m_waiters[best]);
        m_waiters.RemoveAt(best);
    }

    private void OnEnable()
    {
        m_owner = GetComponentInParent<DefenseEventHealth>();
        m_owner?.RegisterAttackPoint(this);
    }

    private void OnDisable()
    {
        m_owner?.UnregisterAttackPoint(this);
        m_holders.Clear();
        m_waiters.Clear();
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
        Gizmos.color = new Color(1.0f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Outward * m_waitDistance);
    }
}
