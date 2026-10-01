using System.Collections.Generic;
using UnityEngine;

/// <summary>짧게 활성화되어 용숨결 시각 효과와 전방 원통형 피해를 함께 처리하는 재사용 프리팹 컴포넌트입니다.</summary>
[DisallowMultipleComponent]
public sealed class DragonBreathEffect : MonoBehaviour
{
    private const string BurningEffectResourcePath = "StatusEffects/Burning";

    [Tooltip("총구를 중심으로 퍼지는 원통형 피해 판정의 반지름(m)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_damageRadius = 1.0f;

    [Tooltip("총구 앞쪽으로 뻗는 원통형 피해 판정의 길이(m)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_damageRange = 4.0f;

    [Tooltip("한 번 활성화될 때 대상 하나에 적용할 피해입니다.")]
    [Min(1)]
    [SerializeField] private int m_damage = 10;

    [Tooltip("판정과 임시 불꽃 이펙트가 유지되는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_lifetime = 0.18f;

    [Tooltip("용숨결 피해 후보를 검색할 레이어입니다. 실제 피해 여부는 진영 규칙이 한 번 더 검사합니다.")]
    [SerializeField] private LayerMask m_damageLayerMask = ~0;

    [Tooltip("용숨결 직격 피해가 성립한 대상에게 적용할 화상 상태이상입니다. 비어 있으면 Resources 기본 에셋을 사용합니다.")]
    [SerializeField] private StatusEffectDefinitionSO m_burningEffect;

    [Tooltip("켜면 발사 여부와 오브젝트 선택 여부에 관계없이 Scene 뷰에 원통형 판정 범위를 계속 표시합니다.")]
    [SerializeField] private bool m_drawDebugRange = true;

    private readonly HashSet<IDamageable> m_damagedTargets = new HashSet<IDamageable>();
    private ParticleSystem m_particles;
    private Material m_runtimeParticleMaterial;
    private Faction m_ownerFaction;
    private GameObject m_attacker;
    private float m_disableTime;

    public int Damage => Mathf.Max(1, m_damage);
    public float DamageRadius => Mathf.Max(0.01f, m_damageRadius);
    public float DamageRange => Mathf.Max(0.01f, m_damageRange);
    public float Lifetime => Mathf.Max(0.01f, m_lifetime);
    public bool DrawDebugRange => m_drawDebugRange;

    private void Awake()
    {
        EnsureTemporaryParticles();
    }

    private void Update()
    {
        if (Time.time >= m_disableTime)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (m_runtimeParticleMaterial != null)
        {
            Destroy(m_runtimeParticleMaterial);
        }
    }

    /// <summary>총구 위치와 실제 발사 방향으로 이펙트를 재사용하고 즉시 중첩 판정을 수행합니다.</summary>
    public void Play(Vector3 origin, Vector3 direction, Faction ownerFaction, GameObject attacker)
    {
        if (direction.sqrMagnitude <= 0.0f)
        {
            return;
        }

        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
        gameObject.SetActive(true);
        EnsureTemporaryParticles();

        m_ownerFaction = ownerFaction;
        m_attacker = attacker;
        m_damagedTargets.Clear();
        m_disableTime = Time.time + Lifetime;

        if (m_particles != null)
        {
            m_particles.Clear(true);
            m_particles.Play(true);
        }

        // 사격 프레임에만 Overlap을 실행하므로 생성 전에 범위 안에 있던 대상도 확실히 잡습니다.
        Physics.SyncTransforms();
        ApplyCylinderOverlapDamage(origin, direction.normalized);

        if (m_drawDebugRange)
        {
            DrawDebugCylinder(origin, direction.normalized, DamageRadius, DamageRange, Lifetime + 0.25f);
        }
    }

    /// <summary>OverlapBox로 후보를 모은 뒤 축 방향과 반지름을 검사해 원통 내부 대상만 피해 처리합니다.</summary>
    private void ApplyCylinderOverlapDamage(Vector3 origin, Vector3 direction)
    {
        float radius = DamageRadius;
        float range = DamageRange;
        Vector3 center = origin + direction * (range * 0.5f);
        Collider[] overlaps = Physics.OverlapBox(
            center,
            new Vector3(radius, radius, range * 0.5f),
            Quaternion.LookRotation(direction, Vector3.up),
            m_damageLayerMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider candidate = overlaps[i];
            if (candidate == null || !IntersectsCylinder(candidate, origin, direction, radius, range))
            {
                continue;
            }

            TryDamage(candidate);
        }
    }

    private static bool IntersectsCylinder(Collider candidate, Vector3 origin, Vector3 direction, float radius, float range)
    {
        float centerDistance = Mathf.Clamp(Vector3.Dot(candidate.bounds.center - origin, direction), 0.0f, range);
        Vector3 axisPoint = origin + direction * centerDistance;
        Vector3 closest = candidate.ClosestPoint(axisPoint);
        float axial = Vector3.Dot(closest - origin, direction);
        if (axial < 0.0f || axial > range)
        {
            return false;
        }

        Vector3 radial = closest - (origin + direction * axial);
        return radial.sqrMagnitude <= radius * radius;
    }

    private void TryDamage(Collider other)
    {
        IDamageable target = other != null ? other.GetComponentInParent<IDamageable>() : null;
        if (target == null || !m_damagedTargets.Add(target))
        {
            return;
        }

        if (!CombatDamage.TryApplyDamage(target, m_ownerFaction, Damage, m_attacker) || target.IsDead)
        {
            return;
        }

        StatusEffectDefinitionSO burning = ResolveBurningEffect();
        if (burning != null)
        {
            StatusEffectContainer.GetOrAdd(target)?.Apply(burning, m_ownerFaction, m_attacker);
        }
    }

    private StatusEffectDefinitionSO ResolveBurningEffect()
    {
        if (m_burningEffect == null)
        {
            m_burningEffect = Resources.Load<StatusEffectDefinitionSO>(BurningEffectResourcePath);
        }

        return m_burningEffect;
    }

    private static void DrawDebugCylinder(Vector3 origin, Vector3 direction, float radius, float range, float duration)
    {
        Vector3 end = origin + direction * range;
        Vector3 right = Vector3.Cross(direction, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(direction, Vector3.forward);
        }

        right.Normalize();
        Vector3 up = Vector3.Cross(right, direction).normalized;
        const int segments = 20;
        Color color = new Color(1.0f, 0.35f, 0.02f, 1.0f);

        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI * 2.0f * i / segments;
            float a1 = Mathf.PI * 2.0f * (i + 1) / segments;
            Vector3 offset0 = (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
            Vector3 offset1 = (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
            Debug.DrawLine(origin + offset0, origin + offset1, color, duration, false);
            Debug.DrawLine(end + offset0, end + offset1, color, duration, false);

            if (i % 5 == 0)
            {
                Debug.DrawLine(origin + offset0, end + offset0, color, duration, false);
            }
        }
    }

    /// <summary>최종 VFX가 들어오기 전까지 사용할 주황색 원뿔형 파티클을 런타임에 구성합니다.</summary>
    private void EnsureTemporaryParticles()
    {
        if (m_particles != null)
        {
            return;
        }

        Transform existing = transform.Find("TemporaryFlameParticles");
        bool createdParticleObject = existing == null;
        GameObject particleObject = existing != null ? existing.gameObject : new GameObject("TemporaryFlameParticles");
        particleObject.transform.SetParent(transform, false);
        m_particles = particleObject.GetComponent<ParticleSystem>();
        if (m_particles == null)
        {
            m_particles = particleObject.AddComponent<ParticleSystem>();
        }

        m_particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = m_particles.main;
        main.duration = Lifetime;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(12.0f, 22.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1.0f, 0.18f, 0.01f, 0.95f),
            new Color(1.0f, 0.82f, 0.08f, 0.95f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 96;
        main.playOnAwake = false;

        ParticleSystem.EmissionModule emission = m_particles.emission;
        emission.rateOverTime = 0.0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 64) });

        ParticleSystem.ShapeModule shape = m_particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12.0f;
        shape.radius = 0.12f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = m_particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.0f, 0.9f, 0.2f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.08f, 0.01f), 1.0f),
            },
            new[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.0f, 1.0f),
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer = m_particles.GetComponent<ParticleSystemRenderer>();
        if (particleRenderer != null && createdParticleObject)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader != null)
            {
                m_runtimeParticleMaterial = new Material(shader)
                {
                    name = "DragonBreathTemporaryMaterial",
                };
                particleRenderer.sharedMaterial = m_runtimeParticleMaterial;
            }
        }
    }
}
