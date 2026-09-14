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
    private Vector3 m_initialVelocity;
    private float m_downwardAcceleration;
    private bool m_hasPlannedCollision;
    private float m_plannedCollisionTime;
    private Vector3 m_plannedCollisionPosition;
    private float m_collisionRadius;
    private int m_collisionLayers;
    private float m_elapsedTime;
    private bool m_initialized;

    /// <summary>
    /// 경로 표시와 실제 이동이 함께 사용하는 포물선 위치 계산입니다.
    /// </summary>
    public static Vector3 EvaluatePosition(
        Vector3 start,
        Vector3 initialVelocity,
        float downwardAcceleration,
        float elapsedTime)
    {
        float time = Mathf.Max(0.0f, elapsedTime);
        return start +
            initialVelocity * time +
            Vector3.down * (0.5f * Mathf.Max(0.0f, downwardAcceleration) * time * time);
    }

    /// <summary>지정한 경과 시간의 포물선 속도를 계산합니다.</summary>
    public static Vector3 EvaluateVelocity(
        Vector3 initialVelocity,
        float downwardAcceleration,
        float elapsedTime)
    {
        float time = Mathf.Max(0.0f, elapsedTime);
        return initialVelocity + Vector3.down * (Mathf.Max(0.0f, downwardAcceleration) * time);
    }

    /// <summary>생성 직후 이동에 필요한 경로와 충돌 조건을 설정합니다.</summary>
    public void Initialize(
        ExplosiveProjectile projectile,
        Vector3 start,
        Vector3 initialVelocity,
        float downwardAcceleration,
        bool hasPlannedCollision,
        float plannedCollisionTime,
        Vector3 plannedCollisionPosition,
        float collisionRadius,
        LayerMask collisionLayers,
        Transform owner)
    {
        m_projectile = projectile;
        m_rigidbody = GetComponent<Rigidbody>();
        m_owner = owner;
        m_start = start;
        m_initialVelocity = initialVelocity;
        m_downwardAcceleration = Mathf.Max(0.0f, downwardAcceleration);
        m_hasPlannedCollision = hasPlannedCollision;
        m_plannedCollisionTime = Mathf.Max(0.0f, plannedCollisionTime);
        m_plannedCollisionPosition = plannedCollisionPosition;
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

        if (m_hasPlannedCollision && m_plannedCollisionTime <= 0.0f)
        {
            m_rigidbody.position = m_plannedCollisionPosition;
            m_projectile.DetonateAt(m_plannedCollisionPosition);
            return;
        }

        Vector3 previous = m_rigidbody.position;
        float nextTime = m_elapsedTime + Time.fixedDeltaTime;
        bool reachedPlannedCollision = m_hasPlannedCollision && nextTime >= m_plannedCollisionTime;
        Vector3 next = reachedPlannedCollision
            ? m_plannedCollisionPosition
            : EvaluatePosition(m_start, m_initialVelocity, m_downwardAcceleration, nextTime);

        if (TryGetBlockingHit(previous, next, out RaycastHit hit))
        {
            Vector3 movement = next - previous;
            float distance = movement.magnitude;
            Vector3 centerAtHit = distance > 0.0001f
                ? previous + movement / distance * Mathf.Clamp(hit.distance, 0.0f, distance)
                : previous;

            m_rigidbody.position = centerAtHit;
            m_projectile.DetonateAt(centerAtHit);
            return;
        }

        if (reachedPlannedCollision)
        {
            m_elapsedTime = m_plannedCollisionTime;
            m_rigidbody.position = m_plannedCollisionPosition;
            m_projectile.DetonateAt(m_plannedCollisionPosition);
            return;
        }

        m_elapsedTime = nextTime;
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
