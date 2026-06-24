using UnityEngine;

/// <summary>
/// Enemy가 추적할 스쿼드 멤버를 찾고 시야 판정을 처리하는 Module입니다.
/// </summary>
public class EnemyTargetSensor : MonoBehaviour
{
    [Header("Target")]
    /// <summary>시야 Raycast에서 장애물로 취급할 레이어입니다.</summary>
    [SerializeField] private LayerMask m_obstacleLayer;

    /// <summary>시야 판정 Raycast의 시작점입니다. 비어 있으면 Enemy 머리 높이의 기본 위치를 사용합니다.</summary>
    [SerializeField] private Transform m_eyePoint;

    /// <summary>추적 대상 검색을 다시 수행하는 최소 주기입니다.</summary>
    [SerializeField] private float m_targetRefreshInterval = 0.25f;

    [Header("Detect")]
    /// <summary>대상을 볼 수 있는 최대 거리입니다.</summary>
    [SerializeField] private float m_sightRange = 12f;

    /// <summary>Enemy 전방 기준 시야각입니다.</summary>
    [SerializeField] private float m_sightAngle = 120f;

    /// <summary>다음 대상 갱신이 가능해지는 시각입니다.</summary>
    private float m_nextRefreshTime;

    /// <summary>현재 선택된 추적 대상 스쿼드 멤버입니다.</summary>
    private SquadMemberController m_currentTarget;

    /// <summary>현재 추적 대상입니다. 대상이 더 이상 유효하지 않으면 null을 반환합니다.</summary>
    public SquadMemberController CurrentTarget => IsTargetValid(m_currentTarget) ? m_currentTarget : null;

    /// <summary>현재 추적 대상의 Transform입니다.</summary>
    public Transform CurrentTargetTransform => CurrentTarget != null ? CurrentTarget.transform : null;

    /// <summary>
    /// 지정된 주기마다 가장 가까운 유효 스쿼드 멤버를 다시 찾습니다.
    /// </summary>
    /// <param name="force">true이면 갱신 주기를 무시하고 즉시 다시 찾습니다.</param>
    public void RefreshTarget(bool force = false)
    {
        if (!force && Time.time < m_nextRefreshTime)
        {
            return;
        }

        m_nextRefreshTime = Time.time + Mathf.Max(0.01f, m_targetRefreshInterval);
        FindBestTarget();
    }

    /// <summary>
    /// 현재 추적 대상이 시야 각도와 장애물 판정을 통과하는지 확인합니다.
    /// </summary>
    public bool CanSeeCurrentTarget()
    {
        SquadMemberController targetMember = CurrentTarget;
        Transform targetTransform = CurrentTargetTransform;
        if (targetMember == null || targetTransform == null)
        {
            return false;
        }

        Vector3 origin = GetEyePosition();
        Vector3 target = targetTransform.position + Vector3.up;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (distance > m_sightRange)
        {
            return false;
        }

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        float angle = Vector3.Angle(transform.forward, direction.normalized);
        if (angle > m_sightAngle * 0.5f)
        {
            return false;
        }

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, m_sightRange, ~0))
        {
            SquadMemberController hitMember = hit.transform.GetComponentInParent<SquadMemberController>();
            if (hitMember != null && hitMember == targetMember)
            {
                return true;
            }

            if (((1 << hit.collider.gameObject.layer) & m_obstacleLayer) != 0)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 현재 유효한 스쿼드 멤버 중 Enemy와 가장 가까운 대상을 선택합니다.
    /// </summary>
    private void FindBestTarget()
    {
        SquadMemberController[] members = FindObjectsByType<SquadMemberController>(FindObjectsSortMode.None);

        float closestDistance = Mathf.Infinity;
        SquadMemberController closestMember = null;

        for (int i = 0; i < members.Length; i++)
        {
            SquadMemberController member = members[i];
            if (!IsTargetValid(member))
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, member.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestMember = member;
            }
        }

        m_currentTarget = closestMember;
    }

    /// <summary>
    /// 추적 대상으로 사용할 수 있는 살아 있는 스쿼드 멤버인지 확인합니다.
    /// </summary>
    private static bool IsTargetValid(SquadMemberController member)
    {
        return member != null && member.IsAlive && !member.IsDown;
    }

    /// <summary>
    /// 시야 판정에 사용할 눈 위치를 반환합니다.
    /// </summary>
    private Vector3 GetEyePosition()
    {
        return m_eyePoint != null
            ? m_eyePoint.position
            : transform.position + Vector3.up * 1.5f;
    }

    /// <summary>
    /// 선택된 Enemy의 시야 반경을 Scene 뷰에서 확인하기 위한 Gizmo입니다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, m_sightRange);
    }
}
