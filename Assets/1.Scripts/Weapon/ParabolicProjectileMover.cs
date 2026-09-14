using UnityEngine;

/// <summary>
/// 물리 중력 대신 하나의 결정적 포물선 수식으로 폭발 투사체를 이동시킵니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ParabolicProjectileMover : MonoBehaviour
{
    private readonly RaycastHit[] m_hits = new RaycastHit[16];

    private ExplosiveProjectile m_projectile;
    private Rigidbody m_rigidbody;
    private Transform m_owner;
    private Vector3 m_start;
    private Vector3 m_end;
    private float m_arcHeight;
    private float m_duration;
    private float m_plannedEndNormalizedTime;
    private Vector3 m_plannedEndPosition;
    private float m_collisionRadius;
    private int m_collisionLayers;
    private float m_elapsedTime;
    private bool m_initialized;

    /// <summary>
    /// 경로 표시와 실제 이동이 함께 사용하는 포물선 위치 계산입니다.
    /// </summary>
    public static Vector3 EvaluatePosition(Vector3 start, Vector3 end, float arcHeight, float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        Vector3 straightPosition = Vector3.Lerp(start, end, t);
        float verticalOffset = 4.0f * arcHeight * t * (1.0f - t);
        return straightPosition + Vector3.up * verticalOffset;
    }

    /// <summary>생성 직후 이동에 필요한 경로와 충돌 조건을 설정합니다.</summary>
    public void Initialize(
        ExplosiveProjectile projectile,
        Vector3 start,
        Vector3 end,
        float arcHeight,
        float duration,
        float plannedEndNormalizedTime,
        Vector3 plannedEndPosition,
        float collisionRadius,
        LayerMask collisionLayers,
        Transform owner)
    {
        m_projectile = projectile;
        m_rigidbody = GetComponent<Rigidbody>();
        m_owner = owner;
        m_start = start;
        m_end = end;
        m_arcHeight = Mathf.Max(0.0f, arcHeight);
        m_duration = Mathf.Max(0.01f, duration);
        m_plannedEndNormalizedTime = Mathf.Clamp01(plannedEndNormalizedTime);
        m_plannedEndPosition = plannedEndPosition;
        m_collisionRadius = Mathf.Max(0.0f, collisionRadius);
        m_collisionLayers = collisionLayers;
        m_elapsedTime = 0.0f;
        m_initialized = true;

        m_rigidbody.position = start;
    }

    private void FixedUpdate()
    {
        if (!m_initialized || m_projectile == null)
        {
            return;
        }

        if (m_plannedEndNormalizedTime <= 0.0f)
        {
            m_rigidbody.position = m_plannedEndPosition;
            m_projectile.Detonate();
            return;
        }

        Vector3 previous = m_rigidbody.position;
        float plannedEndTime = m_duration * m_plannedEndNormalizedTime;
        m_elapsedTime = Mathf.Min(m_elapsedTime + Time.fixedDeltaTime, plannedEndTime);
        float t = m_elapsedTime / m_duration;
        bool reachedPlannedEnd = t >= m_plannedEndNormalizedTime;
        Vector3 next = reachedPlannedEnd
            ? m_plannedEndPosition
            : EvaluatePosition(m_start, m_end, m_arcHeight, t);

        if (TryGetBlockingHit(previous, next, out RaycastHit hit))
        {
            Vector3 movement = next - previous;
            float distance = movement.magnitude;
            Vector3 centerAtHit = distance > 0.0001f
                ? previous + movement / distance * Mathf.Clamp(hit.distance, 0.0f, distance)
                : previous;

            m_rigidbody.position = centerAtHit;
            m_projectile.Detonate();
            return;
        }

        if (reachedPlannedEnd)
        {
            m_rigidbody.position = m_plannedEndPosition;
            m_projectile.Detonate();
            return;
        }

        m_rigidbody.MovePosition(next);
    }

    private bool TryGetBlockingHit(Vector3 start, Vector3 end, out RaycastHit nearestHit)
    {
        nearestHit = default;
        Vector3 movement = end - start;
        float distance = movement.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            m_collisionRadius,
            movement / distance,
            m_hits,
            distance,
            m_collisionLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = m_hits[i];
            if (candidate.collider == null || IsIgnored(candidate.collider.transform))
            {
                continue;
            }

            if (candidate.distance < nearestDistance)
            {
                nearestDistance = candidate.distance;
                nearestHit = candidate;
                found = true;
            }
        }

        return found;
    }

    private bool IsIgnored(Transform hitTransform)
    {
        bool belongsToOwner = m_owner != null &&
            (hitTransform == m_owner || hitTransform.IsChildOf(m_owner));
        bool belongsToProjectile = hitTransform == transform || hitTransform.IsChildOf(transform);
        return belongsToOwner || belongsToProjectile;
    }
}
