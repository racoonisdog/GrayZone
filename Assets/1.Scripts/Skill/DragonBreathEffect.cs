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

    [Tooltip("용숨결탄 발사 시 총구에서 재생할 시각 효과 프리팹입니다(총구 화염과 원뿔형 스파크 확산). " +
             "비어 있으면 임시 파티클을 생성합니다.")]
    [SerializeField] private GameObject m_visualEffectPrefab;

    [Tooltip("시각 효과 프리팹에 줄 회전(도)입니다. 발사 방향(+Z) 기준입니다. " +
             "Fire 계열 VFX는 위(+Y)로 솟으므로 X 90을 줘서 눕혀야 총구 앞으로 퍼집니다. +Z로 만든 프리팹은 0으로 둡니다.")]
    [SerializeField] private Vector3 m_visualEffectRotation = new Vector3(90.0f, 0.0f, 0.0f);

    [Tooltip("불길이 닿은 지점에서 재생할 착탄 스파크 프리팹입니다(선택). +Z가 맞은 면의 바깥쪽을 향하도록 생성합니다. " +
             "비어 있으면 착탄 효과를 재생하지 않습니다.")]
    [SerializeField] private GameObject m_impactEffectPrefab;

    [Tooltip("착탄 효과를 낼 지점을 찾는 발사 방향 레이의 길이(m)입니다. 피해 판정 길이와 별개인 시각 효과용 값입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactRange = 12.0f;

    [Tooltip("불길이 착탄 지점까지 날아가는 속도(m/s)입니다. 거리 ÷ 속도만큼 착탄 효과를 늦게 재생합니다. 0이면 지연 없이 바로 재생합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactTravelSpeed = 60.0f;

    /// <summary>지운 뒤 남은 파티클이 갑자기 끊기지 않도록 수명에 더하는 여유(초)입니다.</summary>
    private const float VisualCleanupMargin = 0.1f;

    /// <summary>착탄 효과가 맞은 면 속에 묻히지 않도록 면 바깥으로 띄우는 거리(m)입니다.</summary>
    private const float ImpactSurfaceOffset = 0.05f;

    private readonly RaycastHit[] m_impactHits = new RaycastHit[16];

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

        if (m_visualEffectPrefab != null)
        {
            Quaternion muzzleRotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(m_visualEffectRotation);
            SpawnDetachedVisual(m_visualEffectPrefab, origin, muzzleRotation, 0.0f);
        }

        if (m_impactEffectPrefab != null)
        {
            SpawnImpactVisual(origin, direction.normalized);
        }
        else if (m_particles != null)
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
        Vector3 halfExtents = new Vector3(radius, radius, range * 0.5f);
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        Collider[] overlaps = Physics.OverlapBox(
            center,
            halfExtents,
            rotation,
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

        // 화염 범위 안의 폭발물 함정도 불이 붙어 터집니다. 피해 레이어와 상관없이 따로 찾습니다.
        ExplosionDamage.TriggerChainDetonation(
            center,
            halfExtents,
            rotation,
            candidate => IntersectsCylinder(candidate, origin, direction, radius, range),
            m_attacker);
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

    /// <summary>
    /// 발사 위치와 방향에 시각 효과를 새로 만들고 총과 분리해 둡니다. 다 타면 스스로 지워집니다.
    /// </summary>
    /// <remarks>
    /// 총의 자식으로 두면 쏜 뒤 캐릭터가 돌거나 반동으로 총이 흔들릴 때 이미 나간 불길까지 같이 휘어집니다.
    /// 피해 판정은 발사 순간 한 번에 끝나므로, 불길이 휘면 판정 범위와 화면이 어긋납니다. 그래서 발사 순간의
    /// 자리에 고정합니다. 매 발 새로 만들어 연속 발사해도 앞 불길이 끊기지 않습니다.
    ///
    /// 반복 재생으로 만든 VFX(Fire 계열 모닥불)는 반복을 끄고 판정 시간 동안만 내보냅니다. 처음부터 한 번만
    /// 재생하도록 만든 VFX는 만든 그대로의 길이를 씁니다. 이미 나간 파티클은 수명을 다 채운 뒤 오브젝트와 함께 지워집니다.
    /// </remarks>
    private void SpawnDetachedVisual(GameObject prefab, Vector3 position, Quaternion rotation, float extraDelay)
    {
        GameObject visual = Instantiate(prefab, position, rotation);
        visual.name = prefab.name;

        ParticleSystem[] systems = visual.GetComponentsInChildren<ParticleSystem>(true);

        // 서브 이미터는 부모 파티클이 대신 내보냅니다. 직접 재생하면 생성 위치에서 따로 한 번 더 터집니다.
        HashSet<ParticleSystem> subEmitters = CollectSubEmitters(systems);

        float endTime = 0.0f;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (subEmitters.Contains(system))
            {
                continue;
            }

            // 재생 중에는 길이·반복·지연을 바꿀 수 없어 먼저 멈춥니다.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            if (main.loop)
            {
                main.loop = false;
                main.duration = Lifetime;
            }

            if (extraDelay > 0.0f)
            {
                ParticleSystem.MinMaxCurve delay = main.startDelay;
                main.startDelay = delay.mode == ParticleSystemCurveMode.TwoConstants
                    ? new ParticleSystem.MinMaxCurve(delay.constantMin + extraDelay, delay.constantMax + extraDelay)
                    : new ParticleSystem.MinMaxCurve(GetCurveMax(delay) + extraDelay);
            }

            float systemEnd = GetCurveMax(main.startDelay) + main.duration + GetCurveMax(main.startLifetime);
            endTime = Mathf.Max(endTime, systemEnd + GetLongestSubEmitterLifetime(system));
        }

        for (int i = 0; i < systems.Length; i++)
        {
            if (!subEmitters.Contains(systems[i]))
            {
                systems[i].Play(false);
            }
        }

        Destroy(visual, endTime + VisualCleanupMargin);
    }

    /// <summary>
    /// 발사 방향으로 레이를 쏴 불길이 처음 닿는 면을 찾고, 그 자리에 착탄 스파크를 불길이 도착하는 시간에 맞춰 재생합니다.
    /// </summary>
    /// <remarks>
    /// 시각 효과 전용입니다. 피해는 <see cref="ApplyCylinderOverlapDamage"/>가 따로 처리하므로 이 레이는 피해와 무관합니다.
    /// 쏜 사람의 콜라이더와 트리거는 건너뜁니다. 사거리 안에 아무것도 없으면 착탄 효과를 내지 않습니다.
    /// </remarks>
    private void SpawnImpactVisual(Vector3 origin, Vector3 direction)
    {
        if (m_impactRange <= 0.0f)
        {
            return;
        }

        int count = Physics.RaycastNonAlloc(
            origin, direction, m_impactHits, m_impactRange, m_damageLayerMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        RaycastHit nearest = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = m_impactHits[i];
            if (m_attacker != null && hit.collider.transform.IsChildOf(m_attacker.transform))
            {
                continue;
            }

            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                found = true;
            }
        }

        if (!found)
        {
            return;
        }

        // 총구와 겹쳐 시작한 콜라이더는 normal이 0이라 발사 반대 방향을 대신 씁니다.
        Vector3 normal = nearest.normal.sqrMagnitude > 0.0f ? nearest.normal : -direction;
        Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? -direction : Vector3.up;
        Vector3 position = nearest.distance > 0.0f ? nearest.point + normal * ImpactSurfaceOffset : origin;
        float delay = m_impactTravelSpeed > 0.0f ? nearest.distance / m_impactTravelSpeed : 0.0f;

        SpawnDetachedVisual(m_impactEffectPrefab, position, Quaternion.LookRotation(normal, up), delay);
    }

    private static HashSet<ParticleSystem> CollectSubEmitters(ParticleSystem[] systems)
    {
        HashSet<ParticleSystem> result = new HashSet<ParticleSystem>();
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.SubEmittersModule subModule = systems[i].subEmitters;
            if (!subModule.enabled)
            {
                continue;
            }

            for (int j = 0; j < subModule.subEmittersCount; j++)
            {
                ParticleSystem sub = subModule.GetSubEmitterSystem(j);
                if (sub != null)
                {
                    result.Add(sub);
                }
            }
        }

        return result;
    }

    /// <summary>부모 파티클이 마지막 순간에 서브 이미터를 터뜨려도 그 파티클이 다 탈 때까지 남겨 두기 위한 시간입니다.</summary>
    private static float GetLongestSubEmitterLifetime(ParticleSystem system)
    {
        ParticleSystem.SubEmittersModule subModule = system.subEmitters;
        if (!subModule.enabled)
        {
            return 0.0f;
        }

        float longest = 0.0f;
        for (int i = 0; i < subModule.subEmittersCount; i++)
        {
            ParticleSystem sub = subModule.GetSubEmitterSystem(i);
            if (sub != null)
            {
                ParticleSystem.MainModule subMain = sub.main;
                longest = Mathf.Max(longest, subMain.duration + GetCurveMax(subMain.startLifetime));
            }
        }

        return longest;
    }

    private static float GetCurveMax(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return curve.constant;
            case ParticleSystemCurveMode.TwoConstants:
                return curve.constantMax;
            default:
                // 곡선 모드는 곡선 값이 0~1이고 실제 값은 배율이 곱해진 것이라 배율을 상한으로 봅니다.
                return curve.curveMultiplier;
        }
    }

    /// <summary>시각 효과 프리팹이 없을 때 쓸 주황색 원뿔형 임시 파티클을 런타임에 구성합니다.</summary>
    private void EnsureTemporaryParticles()
    {
        // 프리팹이 있으면 발사할 때마다 따로 만들어 분리하므로(SpawnDetachedVisual) 미리 만들어 둘 것이 없습니다.
        if (m_particles != null || m_visualEffectPrefab != null)
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
