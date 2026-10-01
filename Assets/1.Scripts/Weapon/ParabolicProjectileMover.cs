using UnityEngine;

/// <summary>
/// 물리 중력 대신 상승/하강 구간이 분리된 결정적 포물선 수식으로 투척물을 이동시킵니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ParabolicProjectileMover : MonoBehaviour
{
    private readonly RaycastHit[] m_hits = new RaycastHit[16];

    private ProjectileBase m_projectile;
    private Rigidbody m_rigidbody;
    private Transform m_owner;
    private Vector3 m_start;
    private Vector3 m_initialVelocity;
    private float m_ascentDownwardAcceleration;
    private float m_descentDownwardAcceleration;
    private float m_speedMultiplier;
    private bool m_hasPlannedCollision;
    private float m_plannedCollisionTime;
    private Vector3 m_plannedCollisionPosition;
    private Vector3 m_plannedContactPoint;
    private Vector3 m_plannedSurfaceNormal;
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
        float ascentDownwardAcceleration,
        float descentDownwardAcceleration,
        float elapsedTime)
    {
        float time = Mathf.Max(0.0f, elapsedTime);
        float ascentAcceleration = Mathf.Max(0.0f, ascentDownwardAcceleration);
        float descentAcceleration = Mathf.Max(0.0f, descentDownwardAcceleration);
        float apexTime = GetApexTime(initialVelocity.y, ascentAcceleration);

        if (apexTime <= 0.0f)
        {
            return start +
                initialVelocity * time +
                Vector3.down * (0.5f * descentAcceleration * time * time);
        }

        if (time <= apexTime)
        {
            return start +
                initialVelocity * time +
                Vector3.down * (0.5f * ascentAcceleration * time * time);
        }

        Vector3 apexPosition = start +
            initialVelocity * apexTime +
            Vector3.down * (0.5f * ascentAcceleration * apexTime * apexTime);
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(initialVelocity, Vector3.up);
        float descentTime = time - apexTime;
        return apexPosition +
            horizontalVelocity * descentTime +
            Vector3.down * (0.5f * descentAcceleration * descentTime * descentTime);
    }

    /// <summary>지정한 경과 시간의 포물선 속도를 계산합니다.</summary>
    public static Vector3 EvaluateVelocity(
        Vector3 initialVelocity,
        float ascentDownwardAcceleration,
        float descentDownwardAcceleration,
        float elapsedTime)
    {
        float time = Mathf.Max(0.0f, elapsedTime);
        float ascentAcceleration = Mathf.Max(0.0f, ascentDownwardAcceleration);
        float descentAcceleration = Mathf.Max(0.0f, descentDownwardAcceleration);
        float apexTime = GetApexTime(initialVelocity.y, ascentAcceleration);

        if (apexTime <= 0.0f)
        {
            return initialVelocity + Vector3.down * (descentAcceleration * time);
        }

        if (time <= apexTime)
        {
            return initialVelocity + Vector3.down * (ascentAcceleration * time);
        }

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(initialVelocity, Vector3.up);
        return horizontalVelocity + Vector3.down * (descentAcceleration * (time - apexTime));
    }

    private static float GetApexTime(float initialVerticalSpeed, float ascentDownwardAcceleration)
    {
        if (initialVerticalSpeed <= 0.0f || ascentDownwardAcceleration <= 0.0f)
        {
            return 0.0f;
        }

        return initialVerticalSpeed / ascentDownwardAcceleration;
    }

    /// <summary>생성 직후 이동에 필요한 경로와 충돌 조건을 설정합니다.</summary>
    public void Initialize(
        ProjectileBase projectile,
        Vector3 start,
        Vector3 initialVelocity,
        float ascentDownwardAcceleration,
        float descentDownwardAcceleration,
        float speedMultiplier,
        bool hasPlannedCollision,
        float plannedCollisionTime,
        Vector3 plannedCollisionPosition,
        Vector3 plannedContactPoint,
        Vector3 plannedSurfaceNormal,
        float collisionRadius,
        LayerMask collisionLayers,
        Transform owner)
    {
        m_projectile = projectile;
        m_rigidbody = GetComponent<Rigidbody>();
        m_owner = owner;
        m_start = start;
        m_initialVelocity = initialVelocity;
        m_ascentDownwardAcceleration = Mathf.Max(0.0f, ascentDownwardAcceleration);
        m_descentDownwardAcceleration = Mathf.Max(0.0f, descentDownwardAcceleration);
        m_speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        m_hasPlannedCollision = hasPlannedCollision;
        m_plannedCollisionTime = Mathf.Max(0.0f, plannedCollisionTime);
        m_plannedCollisionPosition = plannedCollisionPosition;
        m_plannedContactPoint = plannedContactPoint;
        m_plannedSurfaceNormal = plannedSurfaceNormal;
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
            m_projectile.ImpactAt(
                m_plannedCollisionPosition,
                m_plannedContactPoint,
                m_plannedSurfaceNormal);
            return;
        }

        Vector3 previous = m_rigidbody.position;
        float nextTime = m_elapsedTime + Time.fixedDeltaTime * m_speedMultiplier;
        bool reachedPlannedCollision = m_hasPlannedCollision && nextTime >= m_plannedCollisionTime;
        Vector3 next = reachedPlannedCollision
            ? m_plannedCollisionPosition
            : EvaluatePosition(
                m_start,
                m_initialVelocity,
                m_ascentDownwardAcceleration,
                m_descentDownwardAcceleration,
                nextTime);

        if (TryGetBlockingHit(previous, next, out RaycastHit hit))
        {
            Vector3 movement = next - previous;
            float distance = movement.magnitude;
            Vector3 centerAtHit = distance > 0.0001f
                ? previous + movement / distance * Mathf.Clamp(hit.distance, 0.0f, distance)
                : previous;

            m_rigidbody.position = centerAtHit;
            m_projectile.ImpactAt(centerAtHit, hit.point, hit.normal);
            return;
        }

        if (reachedPlannedCollision)
        {
            m_elapsedTime = m_plannedCollisionTime;
            m_rigidbody.position = m_plannedCollisionPosition;
            m_projectile.ImpactAt(
                m_plannedCollisionPosition,
                m_plannedContactPoint,
                m_plannedSurfaceNormal);
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
