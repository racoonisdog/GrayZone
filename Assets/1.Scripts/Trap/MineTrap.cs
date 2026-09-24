using UnityEngine;
using VInspector;

/// <summary>
/// 지뢰입니다. 밟으면 잠시 뒤 사방으로 터져 범위 안의 대상에게 한 번씩 피해를 줍니다.
/// </summary>
/// <remarks>
/// 피해 범위는 폭발 지점을 중심으로 한 원통입니다. 구가 아니라 원통인 이유는 수평 거리와 높이를
/// 따로 잡아야 하기 때문입니다. 발밑에서 터진 폭발이 위층까지 닿으면 안 됩니다.
///
/// 밟는 판정은 이 오브젝트의 트리거 콜라이더이고, 피해 범위는 아래 수치입니다. 보통 피해 범위가
/// 훨씬 넓으므로 둘을 같게 맞추지 마세요.
/// </remarks>
public sealed class MineTrap : ExplosiveTrap
{
    [Header("Mine")]
    [Tooltip("폭발 피해를 줄 원통의 수평 반지름(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionRadius = 4.0f;

    [Tooltip("폭발 피해 원통의 전체 높이(m)입니다. 폭발 지점을 중심으로 위아래 절반씩 적용됩니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionHeight = 4.0f;

    [Tooltip("폭발 중심을 지뢰 위치에서 얼마나 옮길지입니다. 바닥에 붙은 지뢰의 원통 아래 절반이 땅에 묻히는 것을 피할 때 Y를 올립니다.")]
    [SerializeField] private Vector3 m_explosionCenterOffset = new Vector3(0.0f, 1.0f, 0.0f);

    [Header("Mine Debug")]
    [Tooltip("켜면 Scene View에 폭발 피해 범위를 그립니다. 표시 전용이라 동작에는 영향이 없습니다.")]
    [SerializeField] private bool m_drawExplosionGizmo = true;

    [Tooltip("폭발 범위를 그릴 색입니다.")]
    [ShowIf(nameof(m_drawExplosionGizmo))]
    [SerializeField] private Color m_explosionGizmoColor = new Color(1.0f, 0.45f, 0.1f, 0.7f);
    [EndIf]

    /// <summary>실제로 폭발이 일어나는 월드 좌표입니다.</summary>
    /// <remarks>
    /// 오프셋에 스케일을 곱하지 않는 이유는, 프리팹을 크게 키워 배치했을 때 피해 범위까지 함께
    /// 커지면 기획 수치와 실제가 어긋나기 때문입니다. 방향만 트랜스폼을 따릅니다.
    /// </remarks>
    public Vector3 ExplosionCenter => transform.position + transform.rotation * m_explosionCenterOffset;

    /// <summary>폭발 피해 원통의 수평 반지름입니다.</summary>
    public float ExplosionRadius => m_explosionRadius;

    /// <inheritdoc />
    protected override int ApplyExplosionDamage()
    {
        return ExplosionDamage.DetonateCylinder(
            ExplosionCenter,
            m_explosionRadius,
            m_explosionHeight,
            m_damage,
            m_damageTargetLayers,
            gameObject,
            m_showExplosionRangeVisual);
    }

    /// <inheritdoc />
    protected override GameObject CreateExplosionRangePreview()
    {
        return ExplosionDamage.CreateCylinderRangeVisual(
            ExplosionCenter,
            m_explosionRadius,
            m_explosionHeight);
    }

    /// <inheritdoc />
    protected override void OnDrawTrapGizmos()
    {
        if (!m_drawExplosionGizmo || m_explosionRadius <= 0.0f || m_explosionHeight <= 0.0f)
        {
            return;
        }

        Vector3 center = ExplosionCenter;
        float halfHeight = m_explosionHeight * 0.5f;
        Vector3 bottom = center + Vector3.down * halfHeight;
        Vector3 top = center + Vector3.up * halfHeight;

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = m_explosionGizmoColor;

        DrawWireCircle(bottom, m_explosionRadius);
        DrawWireCircle(top, m_explosionRadius);

        // 위아래 원만 그리면 높이가 어디까지인지 읽히지 않아 네 방향의 기둥을 함께 긋습니다.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * m_explosionRadius;
            Gizmos.DrawLine(bottom + offset, top + offset);
        }

        Gizmos.matrix = previous;
    }

    /// <summary>월드 XZ 평면에 놓인 원을 그립니다.</summary>
    private static void DrawWireCircle(Vector3 center, float radius, int segments = 32)
    {
        Vector3 previousPoint = center + new Vector3(radius, 0.0f, 0.0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2.0f;
            Vector3 point = center + new Vector3(
                Mathf.Cos(angle) * radius,
                0.0f,
                Mathf.Sin(angle) * radius);

            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }
}
