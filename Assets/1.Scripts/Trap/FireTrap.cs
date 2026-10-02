using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 화염병이 터진 자리에 남는 화염 지대입니다. 범위 안에 머무는 대상에게 일정 간격으로 화염 피해를 줍니다.
/// </summary>
/// <remarks>
/// 트리거 콜라이더가 필요합니다. <see cref="TemporaryTrapProjectile"/>의 함정 프리팹으로 지정하면
/// 충돌 지점에 생성되어 곧바로 설치 완료 상태가 됩니다.
///
/// <b>피해 계산</b>: 기반의 <c>m_damage</c>/<c>m_damageMode</c>/<c>m_damageTickInterval</c>은 쓰지 않고
/// 이 클래스의 초당 피해량과 틱 간격을 씁니다. 1틱 피해는 <c>초당 피해 × 간격</c>이고, 정수로 떨어지지
/// 않는 나머지는 대상마다 모아 두었다가 다음 틱에 얹습니다. 반올림으로 버리면 초당 피해가 설정값보다
/// 줄거나 늘어납니다.
///
/// <b>지속 시간</b>: 설치가 끝난 순간부터 셉니다. 시간이 다 되면 게임 오브젝트를 제거합니다.
/// 화염 지대는 재설치 대상이 아니므로 내구도와 재설치 정책은 쓰지 않습니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public sealed class FireTrap : Trap
{
    /// <inheritdoc />
    protected override string DefaultDisplayName => "화염 지대";

    /// <inheritdoc />
    protected override string DefaultDescription => "범위 안에 머무는 적에게 지속 화염 피해를 줍니다";

    /// <summary>범위 안에 있는 대상 하나의 상태입니다.</summary>
    private sealed class Occupant
    {
        /// <summary>이 대상의 콜라이더 중 지금 범위 안에 들어와 있는 것들입니다.</summary>
        /// <remarks>
        /// 래그돌처럼 콜라이더가 여럿인 대상은 콜라이더마다 Enter/Exit이 옵니다. 하나만 나가도 풀어 버리면
        /// 몸이 아직 불 위에 있는데 피해가 끊깁니다. 마지막 콜라이더가 나갈 때 풉니다.
        /// </remarks>
        public readonly HashSet<Collider> Colliders = new HashSet<Collider>();

        /// <summary>다음 틱까지 남은 시간(초)입니다.</summary>
        public float TickTimer;

        /// <summary>정수 피해로 넣지 못하고 남은 소수점 피해입니다.</summary>
        public float PendingDamage;
    }

    [Header("Fire Trap")]
    [Tooltip("화염 지대를 지나간 것으로 볼 레이어입니다. 이 레이어의 콜라이더가 범위 안에 있으면 피해를 받습니다. 기본은 Enemy만 봅니다.")]
    [SerializeField] private LayerMask m_targetLayers = 1 << 9;

    [Tooltip("화염 지대가 지속될 시간(초)입니다. 설치가 끝난 순간부터 세며, 다 되면 화염 지대가 사라집니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_duration = 5.0f;

    [Tooltip("범위 안에 머무는 동안 받는 초당 피해량입니다. 1틱 피해는 이 값에 틱 간격을 곱한 값입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_damagePerSecond = 10.0f;

    [Tooltip("피해를 주는 간격(초)입니다. 0.5면 0.5초마다 (초당 피해 × 0.5)만큼 들어갑니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_fireTickInterval = 0.5f;

    [Tooltip("켜면 범위에 들어온 순간 1틱 분량의 피해를 바로 줍니다. 끄면 첫 틱 간격이 지난 뒤부터 피해가 들어갑니다.")]
    [SerializeField] private bool m_damageOnEnter = true;

    [Tooltip("켜면 범위 안의 폭발물 함정(지뢰·클레이모어 등)에 불이 붙어 연쇄로 터집니다. 점화 순간과 이후 피해 간격마다 확인합니다.")]
    [SerializeField] private bool m_igniteExplosives = true;

    /// <summary>다음 연쇄 점화 확인까지 남은 시간(초)입니다.</summary>
    private float m_chainTimer;

    /// <summary>지금 범위 안에 있는 대상들입니다.</summary>
    private readonly Dictionary<IDamageable, Occupant> m_occupants =
        new Dictionary<IDamageable, Occupant>();

    /// <summary>이번 프레임에 처리할 키 목록입니다. 순회 도중 사전을 고치면 예외가 나므로 먼저 복사합니다.</summary>
    private readonly List<IDamageable> m_keyBuffer = new List<IDamageable>();

    /// <summary>이번 프레임에 범위에서 뺄 대상들입니다.</summary>
    private readonly List<IDamageable> m_removalBuffer = new List<IDamageable>();

    /// <summary>화염 지대가 사라지기까지 남은 시간(초)입니다. 설치 전에는 의미가 없습니다.</summary>
    private float m_remainingTime;

    /// <summary>점화되어 지속 시간이 흐르고 있는지 여부입니다.</summary>
    private bool m_isIgnited;

    /// <summary>화염 지대가 사라지기까지 남은 시간(초)입니다.</summary>
    public float RemainingTime => m_remainingTime;

    /// <summary>초당 피해량입니다.</summary>
    public float DamagePerSecond => m_damagePerSecond;

    /// <summary>피해를 주는 간격(초)입니다.</summary>
    public float FireTickInterval => Mathf.Max(0.01f, m_fireTickInterval);

    /// <summary>지금 범위 안에 잡혀 있는 대상의 수입니다.</summary>
    public int OccupantCount => m_occupants.Count;

    /// <inheritdoc />
    protected override bool HasTargetInRange => m_occupants.Count > 0;

    /// <inheritdoc />
    protected override void OnBuilt()
    {
        Ignite();
    }

    /// <summary>
    /// 설치된 상태로 시작한 화염 지대를 점화합니다.
    /// </summary>
    /// <remarks>
    /// <c>m_startPlaced</c>가 켜져 있으면 <c>Trap.Awake</c>가 곧바로 설치 완료로 만들어 버려,
    /// 뒤이어 <see cref="Trap.Build"/>를 불러도 <see cref="OnBuilt"/>가 오지 않습니다.
    /// 화염병이 생성하는 화염 지대가 이 경우라 여기서 직접 점화합니다.
    /// </remarks>
    private void Start()
    {
        if (IsBuilt)
        {
            Ignite();
        }
    }

    /// <summary>
    /// 지속 시간을 시작하고, 점화 순간 이미 범위 안에 있던 대상을 잡습니다.
    /// </summary>
    /// <remarks>
    /// Unity는 콜라이더가 이미 겹쳐 있는 상태에서 조건이 바뀌었다고 <c>OnTriggerEnter</c>를 다시 보내지
    /// 않습니다. 화염병은 적 발밑에 떨어지는 경우가 많아 이 처리가 없으면 첫 피해가 빠집니다.
    /// 설치 경로와 <see cref="Start"/> 경로가 겹쳐도 한 번만 점화합니다.
    /// </remarks>
    private void Ignite()
    {
        if (m_isIgnited)
        {
            return;
        }

        m_isIgnited = true;
        m_remainingTime = Mathf.Max(0.01f, m_duration);

        Collider trigger = GetComponent<Collider>();
        if (trigger == null)
        {
            return;
        }

        IgniteExplosivesInRange(trigger);
        m_chainTimer = FireTickInterval;

        Bounds bounds = trigger.bounds;
        Collider[] hits = Physics.OverlapBox(
            bounds.center,
            bounds.extents,
            transform.rotation,
            m_targetLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            OnTriggerEnter(hits[i]);
        }
    }

    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    /// <summary>트리거 콜라이더가 하나도 없으면 인스펙터에서 경고합니다.</summary>
    private void OnValidate()
    {
        foreach (Collider collider in GetComponents<Collider>())
        {
            if (collider != null && collider.isTrigger)
            {
                return;
            }
        }

        Debug.LogWarning(
            $"[FireTrap] '{name}': 트리거 콜라이더가 없습니다. Collider의 Is Trigger를 켜야 감지가 동작합니다.",
            this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsBlueprint || !IsTarget(other))
        {
            return;
        }

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null || damageable.IsDead)
        {
            return;
        }

        if (m_occupants.TryGetValue(damageable, out Occupant occupant))
        {
            occupant.Colliders.Add(other);
            return;
        }

        occupant = new Occupant { TickTimer = FireTickInterval };
        occupant.Colliders.Add(other);
        m_occupants.Add(damageable, occupant);

        if (m_damageOnEnter)
        {
            ApplyTickDamage(damageable, occupant);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTarget(other))
        {
            return;
        }

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null || !m_occupants.TryGetValue(damageable, out Occupant occupant))
        {
            return;
        }

        occupant.Colliders.Remove(other);
        if (occupant.Colliders.Count == 0)
        {
            m_occupants.Remove(damageable);
        }
    }

    /// <summary>
    /// 지속 시간을 줄이고, 범위 안의 대상에게 틱 간격마다 피해를 넣습니다.
    /// </summary>
    /// <remarks>
    /// 타이머는 대상마다 따로 둡니다. 들어온 시점이 제각각이라 하나로 묶으면 늦게 들어온 대상이
    /// 바로 피해를 받는 일이 생깁니다.
    /// </remarks>
    private void Update()
    {
        if (!IsBuilt || !m_isIgnited)
        {
            return;
        }

        m_remainingTime -= Time.deltaTime;
        if (m_remainingTime <= 0.0f)
        {
            Expire();
            return;
        }

        // 폭발물은 피해 대상이 아니라 m_occupants에 잡히지 않으므로 따로 주기적으로 찾습니다.
        // 불길이 남아 있는 동안 새로 설치된 함정도 불이 붙어야 합니다.
        m_chainTimer -= Time.deltaTime;
        if (m_chainTimer <= 0.0f)
        {
            IgniteExplosivesInRange(GetComponent<Collider>());
            m_chainTimer = FireTickInterval;
        }

        if (m_occupants.Count == 0)
        {
            return;
        }

        float interval = FireTickInterval;

        m_keyBuffer.Clear();
        foreach (IDamageable key in m_occupants.Keys)
        {
            m_keyBuffer.Add(key);
        }

        for (int i = 0; i < m_keyBuffer.Count; i++)
        {
            IDamageable damageable = m_keyBuffer[i];
            Occupant occupant = m_occupants[damageable];

            // 범위 안에서 죽거나 풀로 돌아가면 OnTriggerExit이 오지 않을 수 있어 여기서 걸러 냅니다.
            if (!IsAliveTarget(damageable) || !HasActiveCollider(occupant))
            {
                m_removalBuffer.Add(damageable);
                continue;
            }

            occupant.TickTimer -= Time.deltaTime;

            // 한 프레임이 간격보다 길어도 밀린 틱을 몰아 주지 않고 한 번만 줍니다.
            if (occupant.TickTimer <= 0.0f)
            {
                ApplyTickDamage(damageable, occupant);
                occupant.TickTimer = interval;
            }
        }

        for (int i = 0; i < m_removalBuffer.Count; i++)
        {
            m_occupants.Remove(m_removalBuffer[i]);
        }

        m_removalBuffer.Clear();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ClearOccupants();
    }

    /// <inheritdoc />
    protected override void OnDepleted()
    {
        ClearOccupants();
    }

    /// <summary>1틱 분량의 피해를 넣습니다. 정수로 떨어지지 않는 나머지는 다음 틱으로 넘깁니다.</summary>
    private void ApplyTickDamage(IDamageable damageable, Occupant occupant)
    {
        occupant.PendingDamage += m_damagePerSecond * FireTickInterval;

        int amount = Mathf.FloorToInt(occupant.PendingDamage);
        if (amount <= 0)
        {
            return;
        }

        occupant.PendingDamage -= amount;
        damageable.TakeDamage(amount, gameObject);
    }

    /// <summary>화염 지대 범위 안의 폭발물 함정에 불이 붙었음을 알립니다.</summary>
    /// <remarks>
    /// 판정 범위는 트리거 콜라이더의 경계 상자입니다. 피해 판정과 레이어가 달라서(함정은 Trap 레이어)
    /// 트리거 이벤트로는 잡히지 않습니다. 이미 터졌거나 청사진인 함정은 받는 쪽이 무시합니다.
    /// </remarks>
    private void IgniteExplosivesInRange(Collider trigger)
    {
        if (!m_igniteExplosives || trigger == null)
        {
            return;
        }

        Bounds bounds = trigger.bounds;
        ExplosionDamage.TriggerChainDetonation(
            bounds.center,
            bounds.extents,
            Quaternion.identity,
            candidate => candidate.bounds.Intersects(bounds),
            gameObject);
    }

    /// <summary>지속 시간이 끝난 화염 지대를 정리하고 제거합니다.</summary>
    private void Expire()
    {
        ClearOccupants();
        enabled = false;
        Destroy(gameObject);
    }

    private void ClearOccupants()
    {
        m_occupants.Clear();
        m_keyBuffer.Clear();
        m_removalBuffer.Clear();
    }

    /// <summary>이 콜라이더가 이 함정이 반응할 레이어인지 여부입니다.</summary>
    private bool IsTarget(Collider other)
    {
        return other != null && (m_targetLayers.value & (1 << other.gameObject.layer)) != 0;
    }

    /// <summary>파괴되지 않았고 죽지도 않은 대상인지 여부입니다.</summary>
    private static bool IsAliveTarget(IDamageable damageable)
    {
        // 인터페이스로 들고 있으면 Unity의 null 비교가 적용되지 않아 파괴된 컴포넌트를 따로 거릅니다.
        if (damageable is Component component && (component == null || !component.gameObject.activeInHierarchy))
        {
            return false;
        }

        return damageable != null && !damageable.IsDead;
    }

    /// <summary>범위 안에 들어와 있던 콜라이더 중 아직 켜져 있는 것이 있는지 확인합니다.</summary>
    private static bool HasActiveCollider(Occupant occupant)
    {
        occupant.Colliders.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        return occupant.Colliders.Count > 0;
    }
}
