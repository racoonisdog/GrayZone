using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 윤형 철조망입니다. 범위 안에 있는 적을 느리게 만들고 머무는 동안 지속 피해를 줍니다.
/// </summary>
/// <remarks>
/// 트리거 콜라이더가 필요합니다. 들어올 때 감속을 걸고 나갈 때 풉니다.
///
/// <b>감속을 값 저장/복원으로 하지 않는 이유</b>: 적의 FSM은 상태가 바뀔 때마다
/// <c>agent.speed</c>를 자기 기준값으로 다시 대입합니다. 들어올 때 속도를 깎아 두어도 다음 상태 전환에서
/// 원래대로 돌아옵니다. 그래서 <see cref="EnemyController.AddForceWalk"/>로 달리기를 막아
/// 속도를 구하는 지점 자체가 걷기 값을 내놓게 합니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public sealed class WireTrap : Trap
{
    /// <summary>범위 안에 있는 대상 하나의 상태입니다.</summary>
    private struct Occupant
    {
        /// <summary>피해를 넣을 대상입니다. 들어올 때 한 번만 찾아 둡니다.</summary>
        public IDamageable Damageable;

        /// <summary>다음 틱까지 남은 시간(초)입니다. Once 방식에서는 쓰지 않습니다.</summary>
        public float TickTimer;
    }

    [Header("Wire Trap")]
    [Tooltip("이 함정이 반응할 레이어입니다. 기본은 Enemy만 봅니다.")]
    [SerializeField] private LayerMask m_targetLayers = 1 << 9;

    [Tooltip("범위 안에 있는 동안 적용할 이동 속도 배율입니다. 0.5면 절반 속도이고 1이면 감속이 없습니다. 이동 속도와 애니메이션 재생 속도가 함께 줄어듭니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_moveSpeedMultiplier = 0.5f;

    [Tooltip("켜면 위 배율에 더해 달리기 자체를 막고 걷기 동작으로만 이동하게 합니다.")]
    [SerializeField] private bool m_forceWalk = false;

    /// <summary>지금 범위 안에 있는 적들입니다.</summary>
    private readonly Dictionary<EnemyController, Occupant> m_occupants =
        new Dictionary<EnemyController, Occupant>();

    /// <summary>이번 프레임에 처리할 키 목록입니다. 순회 도중 사전을 고치면 예외가 나므로 먼저 복사합니다.</summary>
    private readonly List<EnemyController> m_keyBuffer = new List<EnemyController>();

    /// <summary>이번 프레임에 범위에서 뺄 대상들입니다.</summary>
    private readonly List<EnemyController> m_removalBuffer = new List<EnemyController>();

    /// <summary>지금 범위 안에 잡혀 있는 적의 수입니다.</summary>
    public int OccupantCount => m_occupants.Count;

    /// <inheritdoc />
    protected override bool HasTargetInRange => m_occupants.Count > 0;

    /// <summary>
    /// 설치가 끝나는 순간 이미 범위 안에 있던 적을 잡습니다.
    /// </summary>
    /// <remarks>
    /// Unity는 콜라이더가 이미 겹쳐 있는 상태에서 조건이 바뀌었다고 <c>OnTriggerEnter</c>를 다시 보내지
    /// 않습니다. 이 처리가 없으면 적 위에 설치한 함정이 그 적이 한 번 나갔다 들어올 때까지 아무 일도
    /// 하지 않아 고장 난 것처럼 보입니다.
    /// </remarks>
    protected override void OnBuilt()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger == null)
        {
            return;
        }

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

    /// <summary>
    /// 트리거 콜라이더가 하나도 없으면 인스펙터에서 경고합니다.
    /// </summary>
    /// <remarks>
    /// <c>Reset</c>은 스크립트를 처음 붙일 때만 돌아서, 콜라이더를 나중에 따로 추가하면 <c>isTrigger</c>가
    /// 꺼진 채 남습니다. 그러면 감지가 조용히 동작하지 않아 원인을 찾기 어렵습니다.
    /// </remarks>
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
            $"[WireTrap] '{name}': 트리거 콜라이더가 없습니다. Collider의 Is Trigger를 켜야 감지가 동작합니다.",
            this);
    }

    private void OnTriggerEnter(Collider other)
    {
        // 청사진 상태에서는 효과가 없습니다. 콜라이더는 끄지 않습니다.
        // InteractionController가 이 콜라이더로 상호작용 대상을 찾기 때문에, 끄면 설치할 수가 없습니다.
        if (IsBlueprint || !IsTarget(other))
        {
            return;
        }

        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy == null || m_occupants.ContainsKey(enemy))
        {
            return;
        }

        IDamageable damageable = enemy.GetComponentInParent<IDamageable>()
            ?? enemy.GetComponentInChildren<IDamageable>(true);

        m_occupants.Add(enemy, new Occupant
        {
            Damageable = damageable,
            TickTimer = DamageInterval,
        });

        enemy.AddMoveSpeedMultiplier(this, m_moveSpeedMultiplier);
        if (m_forceWalk)
        {
            enemy.AddForceWalk(this);
        }

        // 방식과 무관하게 들어온 순간 한 번은 줍니다. Tick은 그 뒤로 간격마다 더 주는 것이고,
        // Once는 여기서 끝입니다. 밟자마자 아무 일도 안 일어나면 함정이 고장 난 것처럼 보입니다.
        if (damageable != null && !damageable.IsDead && m_damage > 0)
        {
            damageable.TakeDamage(m_damage, gameObject);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTarget(other))
        {
            return;
        }

        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy != null)
        {
            Release(enemy);
        }
    }

    /// <summary>
    /// Tick 방식일 때 범위 안의 대상에게 <see cref="Trap.DamageInterval"/>마다
    /// <see cref="Trap.m_damage"/>만큼 피해를 넣습니다. 진입 시 1회는 <c>OnTriggerEnter</c>가 이미 줬습니다.
    /// </summary>
    /// <remarks>
    /// 타이머는 대상마다 따로 둡니다. 들어온 시점이 제각각이라 하나로 묶으면 늦게 들어온 적이
    /// 바로 피해를 받는 일이 생깁니다.
    ///
    /// Once 방식이어도 이 함수는 계속 돕니다. 피해는 주지 않지만, 함정 안에서 죽어 풀로 돌아간 대상을
    /// 걸러내 감속이 남지 않게 해야 하기 때문입니다.
    /// </remarks>
    private void Update()
    {
        if (m_occupants.Count == 0)
        {
            return;
        }

        bool tickMode = m_damageMode == TrapDamageMode.Tick;
        float interval = DamageInterval;

        m_keyBuffer.Clear();
        foreach (EnemyController key in m_occupants.Keys)
        {
            m_keyBuffer.Add(key);
        }

        for (int i = 0; i < m_keyBuffer.Count; i++)
        {
            EnemyController enemy = m_keyBuffer[i];

            // 함정 안에서 죽어 풀로 돌아가면 OnTriggerExit이 오지 않을 수 있습니다.
            // 그대로 두면 죽은 대상에 계속 피해를 넣고 감속도 남으므로 여기서 걸러 냅니다.
            if (enemy == null || !enemy.isActiveAndEnabled)
            {
                m_removalBuffer.Add(enemy);
                continue;
            }

            Occupant occupant = m_occupants[enemy];
            if (occupant.Damageable == null || occupant.Damageable.IsDead)
            {
                m_removalBuffer.Add(enemy);
                continue;
            }

            if (!tickMode)
            {
                continue;
            }

            occupant.TickTimer -= Time.deltaTime;

            // 한 프레임이 간격보다 길어도 밀린 틱을 한 번에 몰아 주지 않고 한 번만 줍니다.
            // 프레임이 크게 튀었을 때 피해가 갑자기 몰리는 쪽이 더 나쁩니다.
            if (occupant.TickTimer <= 0.0f)
            {
                if (m_damage > 0)
                {
                    occupant.Damageable.TakeDamage(m_damage, gameObject);
                }

                occupant.TickTimer = interval;
            }

            m_occupants[enemy] = occupant;
        }

        for (int i = 0; i < m_removalBuffer.Count; i++)
        {
            Release(m_removalBuffer[i]);
        }

        m_removalBuffer.Clear();
    }

    /// <summary>함정이 꺼지거나 부서질 때 걸어 둔 감속을 전부 풉니다.</summary>
    private void OnDisable()
    {
        foreach (EnemyController enemy in m_occupants.Keys)
        {
            if (enemy != null)
            {
                enemy.RemoveMoveSpeedMultiplier(this);
                enemy.RemoveForceWalk(this);
            }
        }

        m_occupants.Clear();
        m_keyBuffer.Clear();
        m_removalBuffer.Clear();
    }

    /// <summary>이 콜라이더가 이 함정이 반응할 대상인지 여부입니다.</summary>
    private bool IsTarget(Collider other)
    {
        return other != null && (m_targetLayers.value & (1 << other.gameObject.layer)) != 0;
    }

    /// <summary>한 대상을 범위에서 제외하고 걸어 둔 감속을 풉니다.</summary>
    private void Release(EnemyController enemy)
    {
        if (enemy == null)
        {
            // Unity의 파괴된 객체는 == null이지만 Dictionary의 실제 참조 키는 남아 있습니다.
            if (!ReferenceEquals(enemy, null))
            {
                m_occupants.Remove(enemy);
            }
            return;
        }

        if (m_occupants.Remove(enemy))
        {
            enemy.RemoveMoveSpeedMultiplier(this);
            enemy.RemoveForceWalk(this);
        }
    }
}
