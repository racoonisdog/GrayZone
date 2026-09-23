using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 충돌하거나 신관 시간이 끝났을 때 주변 적에게 한 번씩 고정 피해를 주는 폭발 투사체입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ExplosiveProjectile : MonoBehaviour
{
    private const float ExplosionRangeVisualDuration = 1.0f;
    private const float ExplosionRangeVisualAlpha = 0.2f;

    [Tooltip("폭발 범위 안의 각 대상에게 적용할 고정 피해입니다.")]
    [Min(0)]
    [SerializeField] private int m_damage = 10;

    [Tooltip("폭발 순간 적을 검색할 원통의 수평 반지름입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionRadius = 5.0f;

    [Tooltip("폭발 피해 원통의 전체 높이입니다. 폭발 지점을 중심으로 위아래 절반씩 적용됩니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionHeight = 20.0f;

    [Tooltip("충돌하지 않았을 때 자동으로 폭발하기까지의 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_fuseTime = 10.0f;

    [Tooltip("폭발 피해 후보로 검색할 Collider Layer입니다.")]
    [SerializeField] private LayerMask m_damageTargetLayers;

    [Tooltip("접촉 폭발과 투척 경로 충돌 판정에서 무시할 상대 Layer입니다.")]
    [SerializeField] private LayerMask m_contactExplosionExcludeLayers;

    private float m_elapsedTime;
    private bool m_hasExploded;
    private Rigidbody m_rigidbody;

    /// <summary>충돌하지 않았을 때 자동 폭발할 때까지의 시간입니다.</summary>
    public float FuseTime => m_fuseTime;

    /// <summary>폭발 피해 원통의 수평 반지름입니다.</summary>
    public float ExplosionRadius => m_explosionRadius;

    /// <summary>접촉 폭발과 투척 경로 충돌에서 무시할 Layer입니다.</summary>
    public LayerMask ContactExplosionExcludeLayers => m_contactExplosionExcludeLayers;

    private void Reset()
    {
        m_damageTargetLayers = LayerMask.GetMask("Enemy", "EnemyHitbox");
    }

    private void Awake()
    {
        m_rigidbody = GetComponent<Rigidbody>();

        if (m_damageTargetLayers.value == 0)
        {
            m_damageTargetLayers = LayerMask.GetMask("Enemy", "EnemyHitbox");
        }
    }

    private void Update()
    {
        m_elapsedTime += Time.deltaTime;

        if (m_elapsedTime >= m_fuseTime)
        {
            Detonate();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        int otherLayerMask = 1 << collision.gameObject.layer;
        if ((m_contactExplosionExcludeLayers.value & otherLayerMask) != 0)
        {
            return;
        }

        Detonate();
    }

    /// <summary>
    /// 포물선 이동 종료나 스윕 충돌처럼 외부 이동 컴포넌트가 폭발을 요청할 때 사용하는 진입점입니다.
    /// </summary>
    public void Detonate()
    {
        Vector3 explosionCenter = m_rigidbody != null
            ? m_rigidbody.position
            : transform.position;
        DetonateAt(explosionCenter);
    }

    /// <summary>지정한 물리 좌표를 중심으로 폭발 피해를 적용합니다.</summary>
    public void DetonateAt(Vector3 explosionCenter)
    {
        if (m_hasExploded)
        {
            return;
        }

        m_hasExploded = true;

        HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();
        float explosionRadius = Mathf.Max(0.0f, m_explosionRadius);
        float halfExplosionHeight = Mathf.Max(0.0f, m_explosionHeight) * 0.5f;

        ShowExplosionRangeVisual(
            explosionCenter,
            explosionRadius,
            halfExplosionHeight * 2.0f);

        if (explosionRadius > 0.0f && halfExplosionHeight > 0.0f)
        {
            Collider[] colliders = Physics.OverlapBox(
                explosionCenter,
                new Vector3(explosionRadius, halfExplosionHeight, explosionRadius),
                Quaternion.identity,
                m_damageTargetLayers,
                QueryTriggerInteraction.Collide);

            foreach (Collider targetCollider in colliders)
            {
                if (!IntersectsExplosionCylinder(
                    targetCollider,
                    explosionCenter,
                    explosionRadius,
                    halfExplosionHeight))
                {
                    continue;
                }

                IDamageable target = targetCollider.GetComponentInParent<IDamageable>();
                if (target == null || !damagedTargets.Add(target))
                {
                    continue;
                }

                target.TakeDamage(m_damage, null);
            }
        }

        Destroy(gameObject);
    }

    private static void ShowExplosionRangeVisual(
        Vector3 center,
        float radius,
        float height)
    {
        if (radius <= 0.0f || height <= 0.0f)
        {
            return;
        }

        Shader rangeShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (rangeShader == null)
        {
            return;
        }

        GameObject rangeVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rangeVisual.name = "ExplosionDamageRangeVisual";
        rangeVisual.hideFlags = HideFlags.HideAndDontSave;
        rangeVisual.transform.position = center;
        rangeVisual.transform.rotation = Quaternion.identity;
        rangeVisual.transform.localScale = new Vector3(
            radius * 2.0f,
            height * 0.5f,
            radius * 2.0f);

        Collider rangeCollider = rangeVisual.GetComponent<Collider>();
        rangeCollider.enabled = false;
        Destroy(rangeCollider);

        Color rangeColor = new Color(1.0f, 0.35f, 0.05f, ExplosionRangeVisualAlpha);
        Material rangeMaterial = new Material(rangeShader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Transparent
        };
        rangeMaterial.SetColor("_BaseColor", rangeColor);
        rangeMaterial.SetFloat("_Surface", 1.0f);
        rangeMaterial.SetFloat("_Blend", 0.0f);
        rangeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        rangeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        rangeMaterial.SetFloat("_ZWrite", 0.0f);
        rangeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        Renderer rangeRenderer = rangeVisual.GetComponent<Renderer>();
        rangeRenderer.sharedMaterial = rangeMaterial;
        rangeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        rangeRenderer.receiveShadows = false;

        Destroy(rangeMaterial, ExplosionRangeVisualDuration);
        Destroy(rangeVisual, ExplosionRangeVisualDuration);
    }

    private static bool IntersectsExplosionCylinder(
        Collider targetCollider,
        Vector3 center,
        float radius,
        float halfHeight)
    {
        float minY = center.y - halfHeight;
        float maxY = center.y + halfHeight;
        float axisY = Mathf.Clamp(targetCollider.bounds.center.y, minY, maxY);
        Vector3 axisPoint = new Vector3(center.x, axisY, center.z);
        Vector3 closestPoint = targetCollider.ClosestPoint(axisPoint);

        if (closestPoint.y < minY || closestPoint.y > maxY)
        {
            return false;
        }

        Vector2 horizontalOffset = new Vector2(
            closestPoint.x - center.x,
            closestPoint.z - center.z);
        return horizontalOffset.sqrMagnitude <= radius * radius;
    }
}
