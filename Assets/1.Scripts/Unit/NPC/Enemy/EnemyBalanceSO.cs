using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// CSV/XLSX로 조정할 기본 적 1종의 순수 밸런스 수치를 보관합니다.
/// </summary>
/// <remarks>
/// Unity 오브젝트 및 미디어 참조를 포함하지 않습니다. 이 타입의 직렬화 필드는 모두
/// 기획 테이블 입출력 대상이며, 런타임 상태는 각 Enemy 컴포넌트가 별도로 소유합니다.
/// 수치의 정본은 공용 `적 시스템` 문서이며, 여기 기본값은 문서의 수치가 확정되기 전 임시값입니다.
/// 아직 BindManager 파이프라인으로 이관하지 않아 범위 보정을 getter가 담당합니다.
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

    [Header("Idle Variation")]
    [Tooltip("대기 동작 혼합 비율의 최솟값입니다. 개체마다 이 범위에서 한 번 뽑아 고정합니다.")]
    [SerializeField] private float m_idleTypeMin = 0f;

    [Tooltip("대기 동작 혼합 비율의 최댓값입니다. 최솟값과 같으면 모든 개체가 같은 대기 동작을 씁니다.")]
    [SerializeField] private float m_idleTypeMax = 1f;

    [Header("Detection")]
    [Tooltip("시야 판정을 다시 수행하는 주기(초)입니다. 최소값은 0.01초입니다.")]
    [FormerlySerializedAs("m_targetRefreshInterval")]
    [SerializeField] private float m_perceptionInterval = 0.25f;

    [Tooltip("시야로 대상을 감지할 수 있는 최대 거리(m)입니다.")]
    [SerializeField] private float m_sightRange = 12f;

    [Tooltip("적 전방을 기준으로 사용하는 전체 시야각(도)입니다. 0~360 범위입니다.")]
    [SerializeField] private float m_sightAngle = 120f;

    [Tooltip("대상을 처음 발견한 뒤 추격을 시작하기 전 경계 시간(초)입니다.")]
    [SerializeField] private float m_alertDuration = 0.5f;

    [Tooltip("시야에서 벗어난 뒤에도 대상의 실시간 위치를 계속 아는 시간(초)입니다. 끝나면 마지막 확인 위치만 남습니다.")]
    [SerializeField] private float m_loseSightDelay = 2f;

    [Header("Noise")]
    // 이전 이름은 m_noiseDetectionThreshold(감지 기준값 0.15)였습니다. 의미가 "기준"에서 "배수"로 뒤집혀
    // [FormerlySerializedAs]를 붙이지 않았습니다. 붙이면 0.15가 배수로 들어와 도달 거리의 15%만 듣는
    // 사실상 귀머거리가 되는데, 컴파일도 통과하고 에러도 없어 조용히 어긋납니다.
    [Tooltip("소음의 도달 거리에 곱하는 청각 배수입니다. 1이면 도달 거리 그대로, 1보다 크면 더 멀리 듣습니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseHearingMultiplier = 1f;

    [Tooltip("소음 인지 게이지가 이 값에 닿으면 소음 위치를 추적하기 시작합니다. 낮으면 금방 알아챕니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseAwarenessThreshold = 1f;

    [Tooltip("소음 인지 게이지가 초당 줄어드는 양입니다. 소음이 끊기면 이 속도로 빠져 결국 경계를 풉니다. 걷기 소음의 초당 증가량보다 크면 걸어서는 절대 들키지 않습니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseAwarenessDecayPerSecond = 0.15f;

    [Tooltip("소음 위치로 이동할 때의 속도(m/s)입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseChaseSpeed = 2f;

    [Tooltip("소음 위치에 도착했다고 볼 거리(m)입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseArriveDistance = 1.5f;

    [Tooltip("소음 위치 도착 후 주변을 수색하는 전체 시간(초)입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseSearchDuration = 8f;

    [Tooltip("소음 수색 중 배회할 반경(m)입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseSearchRadius = 5f;

    [Header("Howl")]
    [Tooltip("하울링이 전달되는 고정 반경(m)입니다. 벽이나 엄폐물은 판정에 쓰지 않습니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_howlRadius = 25f;

    [Tooltip("하울링 시작 후 실제로 전파가 확정되는 시점(초)입니다. 이 시점 전에 사망하면 전파가 취소됩니다. 클립 이벤트가 오면 그쪽이 우선합니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_howlBroadcastTime = 1.2f;

    [Tooltip("하울링 행동 전체 길이(초)입니다. 끝나면 다음 행동을 고릅니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_howlDuration = 3f;

    [Header("Target Selection")]
    [Tooltip("현재 대상을 다시 고를지 판단하는 주기(초)입니다. 이 주기 자체가 대상의 최소 유지 시간이 됩니다.")]
    [SerializeField] private float m_targetReevaluateInterval = 1f;

    [Tooltip("새 후보가 현재 대상보다 이만큼(m) 더 가까워야 대상을 바꿉니다. 경계에서 대상이 떨리는 것을 막습니다.")]
    [SerializeField] private float m_targetSwitchPathDistanceDelta = 2f;

    [Header("Hit Reaction")]
    [Tooltip("피격 상태에서 이동과 상태 전환을 잠그는 시간(초)입니다.")]
    [SerializeField] private float m_hitStunDuration = 0.35f;

    [Tooltip("연속 피격 시 피격 상태를 다시 시작할 수 있는 최소 간격(초)입니다.")]
    [SerializeField] private float m_hitStunCooldown = 0.2f;

    [Header("Attack")]
    [Tooltip("공격을 시작할 수 있는 대상과의 최대 거리(m)입니다. 실제 판정 범위와는 별개의 시작 조건입니다.")]
    [SerializeField] private float m_attackRange = 1.8f;

    [Tooltip("공격을 시작할 수 있는 정면 기준 허용 방향각(도)입니다. 이 밖의 대상에게는 공격을 시작하지 않습니다.")]
    [SerializeField] private float m_attackStartAngle = 70f;

    [Tooltip("공격 시작 후 이 시점(초)에 방향을 확정합니다. 이후에는 대상을 따라 회전하지 않습니다.")]
    [SerializeField] private float m_attackDirectionLockTime = 0.25f;

    [Tooltip("공격 시작 후 이 시점(초)에 공간 판정을 수행합니다. 방향 고정 시점보다 뒤여야 합니다.")]
    [SerializeField] private float m_attackImpactTime = 0.45f;

    [Tooltip("판정 후 다음 행동까지의 후딜레이(초)입니다. 이 값이 곧 공격 간격이며 별도 쿨다운은 두지 않습니다.")]
    [SerializeField] private float m_attackRecoveryDuration = 0.75f;

    [Tooltip("근접 공격이 적중했을 때 적용하는 기본 피해량입니다.")]
    [SerializeField] private int m_attackDamage = 1;

    [Tooltip("한 번의 공격으로 피해를 줄 수 있는 최대 캐릭터 수입니다.")]
    [SerializeField] private int m_attackMaxTargets = 1;

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

    /// <summary>시야 판정 갱신 주기(초)입니다.</summary>
    public float PerceptionInterval => Mathf.Max(0.01f, m_perceptionInterval);

    /// <summary>현재 대상 재평가 주기(초)입니다.</summary>
    public float TargetReevaluateInterval => Mathf.Max(0.01f, m_targetReevaluateInterval);

    /// <summary>대상을 교체하는 데 필요한 경로 거리 차이(m)입니다.</summary>
    public float TargetSwitchPathDistanceDelta => Mathf.Max(0f, m_targetSwitchPathDistanceDelta);

    /// <summary>대기 동작 혼합 비율의 최솟값입니다.</summary>
    public float IdleTypeMin => Mathf.Min(m_idleTypeMin, m_idleTypeMax);

    /// <summary>대기 동작 혼합 비율의 최댓값입니다.</summary>
    /// <remarks>뒤집혀 적혀 있으면 둘을 바꿔 읽습니다. 뒤집힌 범위로는 값이 나오지 않기 때문입니다.</remarks>
    public float IdleTypeMax => Mathf.Max(m_idleTypeMin, m_idleTypeMax);

    /// <summary>소음의 도달 거리에 곱하는 청각 배수입니다.</summary>
    public float NoiseHearingMultiplier => Mathf.Max(0f, m_noiseHearingMultiplier);

    /// <summary>소음 인지 게이지의 한계치입니다.</summary>
    /// <remarks>0이면 소음을 듣는 즉시 알아채므로 최소값을 두지 않습니다.</remarks>
    public float NoiseAwarenessThreshold => Mathf.Max(0f, m_noiseAwarenessThreshold);

    /// <summary>소음 인지 게이지의 초당 감소량입니다.</summary>
    public float NoiseAwarenessDecayPerSecond => Mathf.Max(0f, m_noiseAwarenessDecayPerSecond);

    /// <summary>소음 위치로 이동할 때의 속도(m/s)입니다.</summary>
    public float NoiseChaseSpeed => Mathf.Max(0f, m_noiseChaseSpeed);

    /// <summary>소음 위치에 도착했다고 볼 거리(m)입니다.</summary>
    public float NoiseArriveDistance => Mathf.Max(0.1f, m_noiseArriveDistance);

    /// <summary>소음 수색 전체 시간(초)입니다.</summary>
    public float NoiseSearchDuration => Mathf.Max(0f, m_noiseSearchDuration);

    /// <summary>소음 수색 중 배회할 반경(m)입니다.</summary>
    public float NoiseSearchRadius => Mathf.Max(0.1f, m_noiseSearchRadius);

    /// <summary>하울링이 전달되는 고정 반경(m)입니다.</summary>
    public float HowlRadius => Mathf.Max(0f, m_howlRadius);

    /// <summary>하울링 전파가 확정되는 시점(초)입니다.</summary>
    public float HowlBroadcastTime => Mathf.Max(0f, m_howlBroadcastTime);

    /// <summary>하울링 행동 전체 길이(초)입니다. 전파 시점보다 짧아지지 않습니다.</summary>
    public float HowlDuration => Mathf.Max(HowlBroadcastTime, m_howlDuration);

    /// <summary>시야 감지 거리(m)입니다.</summary>
    public float SightRange => Mathf.Max(0f, m_sightRange);

    /// <summary>전체 시야각(도)입니다.</summary>
    public float SightAngle => Mathf.Clamp(m_sightAngle, 0f, 360f);

    /// <summary>최초 감지 후 경계 시간(초)입니다.</summary>
    public float AlertDuration => Mathf.Max(0f, m_alertDuration);

    /// <summary>시야에서 벗어난 뒤 실시간 위치를 계속 아는 시간(초)입니다.</summary>
    public float LoseSightDelay => Mathf.Max(0f, m_loseSightDelay);

    /// <summary>피격 상태 유지 시간(초)입니다.</summary>
    public float HitStunDuration => Mathf.Max(0f, m_hitStunDuration);

    /// <summary>피격 상태 재진입 최소 간격(초)입니다.</summary>
    public float HitStunCooldown => Mathf.Max(0f, m_hitStunCooldown);

    /// <summary>공격 시작 거리(m)입니다.</summary>
    public float AttackRange => Mathf.Max(0f, m_attackRange);

    /// <summary>공격 시작 허용 방향각(도)입니다.</summary>
    public float AttackStartAngle => Mathf.Clamp(m_attackStartAngle, 0f, 360f);

    /// <summary>공격 시작 기준 방향 고정 시점(초)입니다.</summary>
    public float AttackDirectionLockTime => Mathf.Max(0f, m_attackDirectionLockTime);

    /// <summary>공격 시작 기준 판정 시점(초)입니다. 방향 고정 시점보다 앞설 수 없습니다.</summary>
    public float AttackImpactTime => Mathf.Max(AttackDirectionLockTime, m_attackImpactTime);

    /// <summary>판정 후 후딜레이(초)이며 곧 공격 간격입니다.</summary>
    public float AttackRecoveryDuration => Mathf.Max(0f, m_attackRecoveryDuration);

    /// <summary>근접 공격 기본 피해량입니다.</summary>
    public int AttackDamage => Mathf.Max(0, m_attackDamage);

    /// <summary>한 번의 공격이 피해를 줄 수 있는 최대 캐릭터 수입니다.</summary>
    public int AttackMaxTargets => Mathf.Max(1, m_attackMaxTargets);

}
