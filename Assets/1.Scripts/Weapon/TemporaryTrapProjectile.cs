using UnityEngine;

/// <summary>
/// 충돌 지점에 지정한 함정을 즉시 설치하고, 일정 시간이 지나면 제거하는 투척물입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public sealed class TemporaryTrapProjectile : ProjectileBase
{
    [Tooltip("충돌 지점에 생성할 Trap 계열 프리팹입니다. 생성 직후 비용 없이 설치 완료 상태가 됩니다.")]
    [SerializeField] private Trap m_trapPrefab;

    [Tooltip("생성된 함정을 유지할 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_trapLifetime = 10.0f;

    [Tooltip("충돌하지 않은 투척물을 제거하기까지의 최대 비행 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_maxFlightTime = 10.0f;

    [Tooltip("투척 경로와 실제 충돌 대상에서 제외할 Layer입니다.")]
    [SerializeField] private LayerMask m_contactExcludeLayers = 1 << 8;

    private bool m_hasDeployed;
    private float m_elapsedTime;

    /// <inheritdoc />
    public override float FlightTimeLimit => Mathf.Max(0.01f, m_maxFlightTime);

    /// <inheritdoc />
    public override LayerMask ContactExcludeLayers => m_contactExcludeLayers;

    private void Update()
    {
        m_elapsedTime += Time.deltaTime;
        if (!m_hasDeployed && m_elapsedTime >= FlightTimeLimit)
        {
            Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (m_hasDeployed)
        {
            return;
        }

        int otherLayerMask = 1 << collision.gameObject.layer;
        if ((m_contactExcludeLayers.value & otherLayerMask) != 0)
        {
            return;
        }

        Vector3 contactPoint = transform.position;
        Vector3 normal = Vector3.up;

        if (collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            contactPoint = contact.point;
            normal = contact.normal;
        }

        ImpactAt(transform.position, contactPoint, normal);
    }

    /// <inheritdoc />
    public override void ImpactAt(
        Vector3 projectilePosition,
        Vector3 contactPoint,
        Vector3 surfaceNormal)
    {
        DeployAt(contactPoint, surfaceNormal);
    }

    /// <summary>지정한 위치와 표면 방향에 함정을 생성합니다.</summary>
    public void DeployAt(Vector3 position, Vector3 surfaceNormal)
    {
        if (m_hasDeployed)
        {
            return;
        }

        m_hasDeployed = true;

        if (m_trapPrefab == null)
        {
            Debug.LogWarning($"[{name}] 생성할 함정 Prefab이 지정되지 않았습니다.", this);
            Destroy(gameObject);
            return;
        }

        Quaternion rotation = ResolveSurfaceRotation(surfaceNormal);
        Trap trap = Instantiate(m_trapPrefab, position, rotation);
        trap.Build();

        Destroy(trap.gameObject, Mathf.Max(0.01f, m_trapLifetime));
        Destroy(gameObject);
    }

    private Quaternion ResolveSurfaceRotation(Vector3 surfaceNormal)
    {
        Vector3 up = surfaceNormal.sqrMagnitude > 0.0001f
            ? surfaceNormal.normalized
            : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(transform.right, up);
        }

        return forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, up)
            : Quaternion.FromToRotation(Vector3.up, up);
    }
}
