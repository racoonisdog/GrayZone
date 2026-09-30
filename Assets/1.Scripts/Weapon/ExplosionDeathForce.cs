using UnityEngine;

/// <summary>
/// 폭발 피해로 사망한 적 오브젝트 전체를 폭발 중심에서 바깥쪽으로 밀어냅니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ExplosionDeathForce : MonoBehaviour
{
    [Tooltip("사망한 적 오브젝트 전체에 전달할 폭발 충격량입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impulse = 600.0f;

    [Tooltip("충격량을 전체 오브젝트의 발사 속도로 환산할 때 사용할 유효 질량입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_effectiveMass = 60.0f;

    [Tooltip("전체 오브젝트가 폭발로 얻을 수 있는 최대 속도입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_maxLaunchSpeed = 8.0f;

    [Tooltip("폭발 중심에서 적으로 향하는 방향에 추가할 위쪽 성분입니다. 0이면 두 점을 잇는 방향만 사용합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_upwardBias = 0.0f;

    /// <summary>폭발 피해로 사망한 Enemy라면 루트 Rigidbody를 이용해 오브젝트 전체를 밀어냅니다.</summary>
    public bool TryApply(IDamageable target, Vector3 explosionCenter)
    {
        if (target == null || !target.IsDead || target.Faction != Faction.Enemy)
        {
            return false;
        }

        if (!(target is Component targetComponent))
        {
            return false;
        }

        EnemyController enemy = targetComponent.GetComponent<EnemyController>();
        enemy ??= targetComponent.GetComponentInParent<EnemyController>(true);
        if (enemy == null)
        {
            return false;
        }

        Rigidbody body = enemy.GetComponent<Rigidbody>();
        if (body == null)
        {
            return false;
        }

        Collider bodyCollider = enemy.GetComponent<Collider>();
        Vector3 enemyPosition = body.worldCenterOfMass;
        Vector3 direction = enemyPosition - explosionCenter;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = Vector3.up;
        }
        else
        {
            direction.Normalize();
        }

        if (m_upwardBias > 0.0f)
        {
            direction = (direction + Vector3.up * m_upwardBias).normalized;
        }

        // 실제 뼈 Rigidbody/Joint 래그돌이 구성되면 아래 기존 경로를 다시 사용할 수 있습니다.
        // return enemy.ApplyKnockback(
        //     direction,
        //     enemyPosition,
        //     Mathf.Max(0.0f, m_impulse),
        //     null);

        float launchSpeed = Mathf.Min(
            Mathf.Max(0.0f, m_impulse) / Mathf.Max(0.1f, m_effectiveMass),
            Mathf.Max(0.0f, m_maxLaunchSpeed));
        if (launchSpeed <= 0.0f)
        {
            return false;
        }

        ExplosionWholeBodyMotion motion = enemy.GetComponent<ExplosionWholeBodyMotion>();
        if (motion == null)
        {
            motion = enemy.gameObject.AddComponent<ExplosionWholeBodyMotion>();
        }

        return motion.Launch(body, bodyCollider, direction * launchSpeed);
    }
}

/// <summary>폭발 사망 연출 동안 적 루트 Rigidbody를 물리 오브젝트로 전환하고 풀 회수 시 복원합니다.</summary>
[DisallowMultipleComponent]
internal sealed class ExplosionWholeBodyMotion : MonoBehaviour
{
    private Rigidbody m_body;
    private Collider m_bodyCollider;
    private bool m_originalIsKinematic;
    private bool m_originalUseGravity;
    private bool m_originalColliderEnabled;
    private RigidbodyConstraints m_originalConstraints;
    private int m_originalLayer;
    private bool m_hasSnapshot;

    public bool Launch(Rigidbody body, Collider bodyCollider, Vector3 velocityChange)
    {
        if (body == null || velocityChange.sqrMagnitude <= Mathf.Epsilon)
        {
            return false;
        }

        if (!m_hasSnapshot)
        {
            m_body = body;
            m_bodyCollider = bodyCollider;
            m_originalIsKinematic = body.isKinematic;
            m_originalUseGravity = body.useGravity;
            m_originalConstraints = body.constraints;
            m_originalColliderEnabled = bodyCollider != null && bodyCollider.enabled;
            m_originalLayer = gameObject.layer;
            m_hasSnapshot = true;
        }

        int corpseLayer = LayerMask.NameToLayer("Corpse");
        if (corpseLayer >= 0)
        {
            gameObject.layer = corpseLayer;
        }

        if (m_bodyCollider != null)
        {
            m_bodyCollider.enabled = true;
        }

        m_body.constraints = RigidbodyConstraints.None;
        m_body.useGravity = true;
        m_body.isKinematic = false;
        m_body.WakeUp();
        m_body.AddForce(velocityChange, ForceMode.VelocityChange);
        return true;
    }

    private void OnDisable()
    {
        RestoreOriginalState();
    }

    private void RestoreOriginalState()
    {
        if (!m_hasSnapshot || m_body == null)
        {
            return;
        }

        m_body.isKinematic = true;
        m_body.linearVelocity = Vector3.zero;
        m_body.angularVelocity = Vector3.zero;
        m_body.constraints = m_originalConstraints;
        m_body.useGravity = m_originalUseGravity;
        m_body.isKinematic = m_originalIsKinematic;

        if (m_bodyCollider != null)
        {
            m_bodyCollider.enabled = m_originalColliderEnabled;
        }

        gameObject.layer = m_originalLayer;
        m_hasSnapshot = false;
    }
}
