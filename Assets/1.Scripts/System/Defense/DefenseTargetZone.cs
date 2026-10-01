using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방어 대상 가까이에서 혼합형 적이 멀리 있는 공격자에게 끌려가지 않게 하는 구역입니다.
/// </summary>
/// <remarks>
/// 혼합형(<see cref="EnemyDefenseDisposition.TargetUntilAttacked"/>) 적이 이 구역 안에 있을 때, 구역 밖에 있는 스쿼드원이
/// 쏘면 전환하지 않고 방어 대상을 계속 공격합니다. 쏜 캐릭터도 구역 안에 있으면 평소처럼 전환합니다.
///
/// 판정은 물리 이벤트가 아니라 위에서 본 상자 안에 있는지(높이 무시)로 계산합니다. 구역 콜라이더가 얇아서 트리거로
/// 판정하면 적의 높이에 따라 들어와도 잡히지 않을 수 있기 때문입니다. 같은 오브젝트의 <see cref="BoxCollider"/>를
/// 구역 모양으로 쓰며, 총알이나 이동을 막지 않도록 트리거로 둡니다. 콜라이더가 없으면 Transform 스케일을 크기로 씁니다.
/// 회전된 상자도 맞게 판정합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseTargetZone : MonoBehaviour
{
    /// <summary>활성화된 구역 목록입니다.</summary>
    private static readonly List<DefenseTargetZone> s_zones = new List<DefenseTargetZone>();

    [Tooltip("구역 모양으로 쓸 박스 콜라이더입니다. 비워 두면 같은 오브젝트에서 찾습니다. 총알과 이동을 막지 않도록 트리거로 바뀝니다.")]
    [SerializeField] private BoxCollider m_shape;

    [Tooltip("선택하지 않아도 Scene 뷰에 구역을 표시할지 여부입니다. 에디터 표시 전용입니다.")]
    [SerializeField] private bool m_alwaysDrawGizmo = true;

    /// <summary>
    /// 적이 어떤 구역 안에 있고 공격자가 그 구역 밖에 있어서, 공격자에게 전환하지 말아야 하는지 판단합니다.
    /// </summary>
    /// <param name="enemyPosition">피격된 적의 위치입니다.</param>
    /// <param name="attackerPosition">쏜 캐릭터의 위치입니다.</param>
    /// <returns>전환하지 말아야 하면 true입니다. 구역이 없거나 적이 어느 구역에도 없으면 false입니다.</returns>
    public static bool ShouldIgnoreAttacker(Vector3 enemyPosition, Vector3 attackerPosition)
    {
        for (int i = s_zones.Count - 1; i >= 0; i--)
        {
            DefenseTargetZone zone = s_zones[i];
            if (zone == null)
            {
                s_zones.RemoveAt(i);
                continue;
            }

            if (zone.Contains(enemyPosition) && !zone.Contains(attackerPosition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>위에서 본 구역 상자 안에 지정 위치가 있는지 확인합니다. 높이는 보지 않습니다.</summary>
    /// <param name="worldPosition">확인할 월드 위치입니다.</param>
    public bool Contains(Vector3 worldPosition)
    {
        GetLocalBox(out Vector3 center, out Vector3 size);
        Vector3 local = transform.InverseTransformPoint(worldPosition) - center;
        return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
    }

    /// <summary>플레이 시작 시 등록 목록을 비웁니다. 도메인 리로드를 끈 경우 이전 세션의 항목이 남지 않게 합니다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetZones()
    {
        s_zones.Clear();
    }

    private void OnEnable()
    {
        ResolveShape();
        if (!s_zones.Contains(this))
        {
            s_zones.Add(this);
        }
    }

    private void OnDisable()
    {
        s_zones.Remove(this);
    }

    /// <summary>모양 콜라이더를 찾아 트리거로 둡니다.</summary>
    private void OnValidate()
    {
        ResolveShape();
    }

    private void ResolveShape()
    {
        if (m_shape == null)
        {
            m_shape = GetComponent<BoxCollider>();
        }

        if (m_shape != null && !m_shape.isTrigger)
        {
            m_shape.isTrigger = true;
        }
    }

    /// <summary>로컬 공간의 구역 상자 중심과 크기입니다. 콜라이더가 없으면 원점 기준 1×1×1 상자입니다(스케일로 크기 조절).</summary>
    private void GetLocalBox(out Vector3 center, out Vector3 size)
    {
        if (m_shape != null)
        {
            center = m_shape.center;
            size = m_shape.size;
            return;
        }

        center = Vector3.zero;
        size = Vector3.one;
    }

    private void OnDrawGizmos()
    {
        if (m_alwaysDrawGizmo)
        {
            DrawZone(new Color(1.0f, 0.3f, 0.3f, 0.35f));
        }
    }

    private void OnDrawGizmosSelected()
    {
        DrawZone(new Color(1.0f, 0.3f, 0.3f, 0.9f));
    }

    private void DrawZone(Color color)
    {
        GetLocalBox(out Vector3 center, out Vector3 size);
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, new Vector3(size.x, Mathf.Max(0.05f, size.y), size.z));
        Gizmos.matrix = previous;
    }
}
