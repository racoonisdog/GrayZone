using UnityEngine;

/// <summary>
/// CSV/XLSX로 조정할 기본 적 1종의 순수 밸런스 수치를 보관합니다.
/// </summary>
/// <remarks>
/// Unity 오브젝트 및 미디어 참조를 포함하지 않습니다. 이 타입의 직렬화 필드는 모두
/// 기획 테이블 입출력 대상이며, 런타임 상태는 각 Enemy 컴포넌트가 별도로 소유합니다.
/// </remarks>
[CreateAssetMenu(fileName = "EnemyBalance", menuName = "GrayZone/Enemy/Enemy Balance")]
public sealed class EnemyBalanceSO : ScriptableObject, IBalanceTableData
{
    [Header("Identity")]
    [Tooltip("테이블과 런타임에서 적 종류를 식별하는 고정 ID입니다. 에셋 이름과 별도로 유지합니다.")]
    [SerializeField] private string m_enemyId = "enemy.basic";

    [Header("Health")]
    [Tooltip("적의 최대 HP입니다. 최소값은 1입니다.")]
    [SerializeField] private int m_maxHp = 10;

    [Header("Movement")]
    [Tooltip("스폰 위치를 기준으로 배회 목적지를 고르는 반경(m)입니다.")]
    [SerializeField] private float m_wanderRadius = 8f;

    [Tooltip("배회 목적지를 다시 선택하는 기본 주기(초)입니다.")]
    [SerializeField] private float m_wanderInterval = 3f;

    [Tooltip("배회 상태의 이동 속도(m/s)입니다.")]
    [SerializeField] private float m_wanderSpeed = 1.2f;

    [Tooltip("추적 상태의 이동 속도(m/s)입니다.")]
    [SerializeField] private float m_chaseSpeed = 3.2f;

    [Tooltip("대상 방향으로 회전할 때 사용하는 보간 속도입니다.")]
    [SerializeField] private float m_rotationSpeed = 8f;

    [Header("Detection")]
    [Tooltip("추적 대상을 다시 평가하는 최소 주기(초)입니다. 최소값은 0.01초입니다.")]
    [SerializeField] private float m_targetRefreshInterval = 0.25f;

    [Tooltip("시야로 대상을 감지할 수 있는 최대 거리(m)입니다.")]
    [SerializeField] private float m_sightRange = 12f;

    [Tooltip("적 전방을 기준으로 사용하는 전체 시야각(도)입니다. 0~360 범위입니다.")]
    [SerializeField] private float m_sightAngle = 120f;

    [Tooltip("대상을 처음 발견한 뒤 추격을 시작하기 전 경계 시간(초)입니다.")]
    [SerializeField] private float m_alertDuration = 0.5f;

    [Tooltip("대상을 마지막으로 본 뒤 추적을 포기할 때까지의 지연 시간(초)입니다.")]
    [SerializeField] private float m_loseSightDelay = 2f;

    [Header("Hit Reaction")]
    [Tooltip("피격 상태에서 이동과 상태 전환을 잠그는 시간(초)입니다.")]
    [SerializeField] private float m_hitStunDuration = 0.35f;

    [Tooltip("연속 피격 시 피격 상태를 다시 시작할 수 있는 최소 간격(초)입니다.")]
    [SerializeField] private float m_hitStunCooldown = 0.2f;

    [Header("Attack")]
    [Tooltip("공격을 시작할 수 있는 대상과의 최대 거리(m)입니다.")]
    [SerializeField] private float m_attackRange = 1.8f;

    [Tooltip("한 번 공격한 뒤 다음 공격까지 기다리는 시간(초)입니다.")]
    [SerializeField] private float m_attackCooldown = 1.2f;

    [Tooltip("공격 애니메이션 동안 상태 전환을 잠그는 시간(초)입니다.")]
    [SerializeField] private float m_attackLockDuration = 0.9f;

    [Tooltip("근접 공격이 적중했을 때 적용하는 기본 피해량입니다.")]
    [SerializeField] private int m_attackDamage = 1;

    [Tooltip("근접 공격 판정 구체의 반지름(m)입니다.")]
    [SerializeField] private float m_attackRadius = 1f;

    [Header("Death")]
    [Tooltip("사망 상태 진입 후 적 오브젝트를 제거하기까지 기다리는 시간(초)입니다.")]
    [SerializeField] private float m_destroyDelay = 3f;

    /// <summary>테이블과 런타임에서 사용하는 고정 적 ID입니다.</summary>
    public string EnemyId => string.IsNullOrWhiteSpace(m_enemyId) ? name : m_enemyId.Trim();

    /// <summary>적의 최대 HP입니다.</summary>
    public int MaxHp => Mathf.Max(1, m_maxHp);

    /// <summary>배회 목적지 선택 반경(m)입니다.</summary>
    public float WanderRadius => Mathf.Max(0f, m_wanderRadius);

    /// <summary>배회 목적지 재선택 주기(초)입니다.</summary>
    public float WanderInterval => Mathf.Max(0f, m_wanderInterval);

    /// <summary>배회 이동 속도(m/s)입니다.</summary>
    public float WanderSpeed => Mathf.Max(0f, m_wanderSpeed);

    /// <summary>추적 이동 속도(m/s)입니다.</summary>
    public float ChaseSpeed => Mathf.Max(0f, m_chaseSpeed);

    /// <summary>대상 방향 회전 보간 속도입니다.</summary>
    public float RotationSpeed => Mathf.Max(0f, m_rotationSpeed);

    /// <summary>추적 대상 재평가 주기(초)입니다.</summary>
    public float TargetRefreshInterval => Mathf.Max(0.01f, m_targetRefreshInterval);

    /// <summary>시야 감지 거리(m)입니다.</summary>
    public float SightRange => Mathf.Max(0f, m_sightRange);

    /// <summary>전체 시야각(도)입니다.</summary>
    public float SightAngle => Mathf.Clamp(m_sightAngle, 0f, 360f);

    /// <summary>최초 감지 후 경계 시간(초)입니다.</summary>
    public float AlertDuration => Mathf.Max(0f, m_alertDuration);

    /// <summary>시야 상실 후 추적 포기 지연 시간(초)입니다.</summary>
    public float LoseSightDelay => Mathf.Max(0f, m_loseSightDelay);

    /// <summary>피격 상태 유지 시간(초)입니다.</summary>
    public float HitStunDuration => Mathf.Max(0f, m_hitStunDuration);

    /// <summary>피격 상태 재진입 최소 간격(초)입니다.</summary>
    public float HitStunCooldown => Mathf.Max(0f, m_hitStunCooldown);

    /// <summary>공격 시작 거리(m)입니다.</summary>
    public float AttackRange => Mathf.Max(0f, m_attackRange);

    /// <summary>공격 간 쿨다운(초)입니다.</summary>
    public float AttackCooldown => Mathf.Max(0f, m_attackCooldown);

    /// <summary>공격 중 상태 잠금 시간(초)입니다.</summary>
    public float AttackLockDuration => Mathf.Max(0f, m_attackLockDuration);

    /// <summary>근접 공격 기본 피해량입니다.</summary>
    public int AttackDamage => Mathf.Max(0, m_attackDamage);

    /// <summary>근접 공격 판정 반지름(m)입니다.</summary>
    public float AttackRadius => Mathf.Max(0f, m_attackRadius);

    /// <summary>사망 후 오브젝트 제거 지연 시간(초)입니다.</summary>
    public float DestroyDelay => Mathf.Max(0f, m_destroyDelay);

}
