using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 방어전에서 하나의 적 프리팹을 어떤 생산 규칙으로 운용할지 정의하는 전용 설정 에셋입니다.
/// </summary>
/// <remarks>
/// 적의 방어전 성향, AI, 밸런스와 래그돌 설정은 <see cref="EnemyController"/> 프리팹이 소유합니다.
/// 이 에셋은 해당 프리팹의 생산 수량, 주기, 최대 수용량과 생성별 Walk/Run 속도 범위를 정의합니다.
/// </remarks>
[CreateAssetMenu(fileName = "EnemyDefenseSO_Name_Type", menuName = "GrayZone/Defense/Enemy Defense Spawn Config")]
public sealed class EnemySpawnEntrySO : ScriptableObject
{
    [Header("Enemy")]
    [Tooltip("이 생산 항목이 풀링하고 생성할 EnemyController 프리팹입니다. 성향과 전투 설정은 프리팹의 직렬화 값을 그대로 사용합니다.")]
    [SerializeField] private EnemyController m_enemyPrefab;

    [Header("Production")]
    [Tooltip("생산 주기가 도달했을 때 동시에 활성화하려는 적 수입니다. 남은 수용량이나 사용 가능한 풀 적이 부족하면 가능한 수만 생성합니다.")]
    [Min(1)]
    [SerializeField] private int m_spawnCount = 1;

    [Tooltip("다음 배치 생산을 시도하기까지 기다릴 시간(초)입니다. 0.01초보다 작게 설정할 수 없습니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_productionInterval = 5.0f;

    [Tooltip("스폰 포인트가 작동을 시작하거나 다시 활성화된 뒤 첫 생산까지 기다릴 시간(초)입니다. 첫 생산 이후에는 Production Interval을 사용합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_initialSpawnDelay;

    [Tooltip("이 항목이 동시에 활성화할 수 있는 적 최대 수입니다. 이를 참조하는 스폰 포인트는 게임 시작 시 이 수만큼 비활성 풀을 준비합니다.")]
    [Min(1)]
    [SerializeField] private int m_maxCapacity = 10;

    [Header("Movement")]
    [Tooltip("적이 전장에 생성될 때 한 번 선정할 개체별 걷기 이동 속도의 최솟값(m/s)입니다. Always Run이 켜져 있으면 사용하지 않습니다.")]
    [Min(0.0f)]
    [FormerlySerializedAs("m_moveSpeedMin")]
    [SerializeField] private float m_walkSpeedMin = 3.2f;

    [Tooltip("적이 전장에 생성될 때 한 번 선정할 개체별 걷기 이동 속도의 최댓값(m/s)입니다. 최솟값보다 작게 입력하면 최솟값으로 보정됩니다.")]
    [Min(0.0f)]
    [FormerlySerializedAs("m_moveSpeedMax")]
    [SerializeField] private float m_walkSpeedMax = 3.2f;

    [Tooltip("적이 전장에 생성될 때 한 번 선정할 개체별 달리기 이동 속도의 최솟값(m/s)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_runSpeedMin = 3.2f;

    [Tooltip("적이 전장에 생성될 때 한 번 선정할 개체별 달리기 이동 속도의 최댓값(m/s)입니다. 최솟값보다 작게 입력하면 최솟값으로 보정됩니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_runSpeedMax = 3.2f;

    /// <summary>이 생산 항목이 생성할 적 프리팹입니다.</summary>
    public EnemyController EnemyPrefab => m_enemyPrefab;

    /// <summary>한 생산 주기에 동시에 활성화하려는 적 수입니다.</summary>
    public int SpawnCount => m_spawnCount;

    /// <summary>다음 배치 생산까지의 대기 시간(초)입니다.</summary>
    public float ProductionInterval => m_productionInterval;

    /// <summary>스폰 포인트가 작동을 시작한 뒤 첫 생산까지의 대기 시간(초)입니다.</summary>
    public float InitialSpawnDelay => m_initialSpawnDelay;

    /// <summary>이 프리팹 항목이 동시에 점유할 수 있는 최대 풀 수용량입니다.</summary>
    public int MaxCapacity => m_maxCapacity;

    /// <summary>개체별 걷기 이동 속도 범위의 최솟값(m/s)입니다.</summary>
    public float WalkSpeedMin => m_walkSpeedMin;

    /// <summary>개체별 걷기 이동 속도 범위의 최댓값(m/s)입니다.</summary>
    public float WalkSpeedMax => m_walkSpeedMax;

    /// <summary>개체별 달리기 이동 속도 범위의 최솟값(m/s)입니다.</summary>
    public float RunSpeedMin => m_runSpeedMin;

    /// <summary>개체별 달리기 이동 속도 범위의 최댓값(m/s)입니다.</summary>
    public float RunSpeedMax => m_runSpeedMax;

    /// <summary>새로 생성되는 한 개체가 사용할 걷기 이동 속도를 균등 분포로 선정합니다.</summary>
    public float SampleWalkSpeed()
    {
        return UnityEngine.Random.Range(m_walkSpeedMin, m_walkSpeedMax);
    }

    /// <summary>새로 생성되는 한 개체가 사용할 달리기 이동 속도를 균등 분포로 선정합니다.</summary>
    public float SampleRunSpeed()
    {
        return UnityEngine.Random.Range(m_runSpeedMin, m_runSpeedMax);
    }

    /// <summary>Play Mode 중 이 에셋의 Inspector 값이 바뀌었음을 구독자에게 알립니다.</summary>
    public event Action<EnemySpawnEntrySO> ConfigurationChanged;

    /// <summary>Inspector에서 입력한 생산 수치가 유효한 하한을 지키도록 보정합니다.</summary>
    private void OnValidate()
    {
        m_spawnCount = Mathf.Max(1, m_spawnCount);
        m_productionInterval = Mathf.Max(0.01f, m_productionInterval);
        m_initialSpawnDelay = Mathf.Max(0.0f, m_initialSpawnDelay);
        m_maxCapacity = Mathf.Max(1, m_maxCapacity);
        m_walkSpeedMin = Mathf.Max(0.0f, m_walkSpeedMin);
        m_walkSpeedMax = Mathf.Max(m_walkSpeedMin, m_walkSpeedMax);
        m_runSpeedMin = Mathf.Max(0.0f, m_runSpeedMin);
        m_runSpeedMax = Mathf.Max(m_runSpeedMin, m_runSpeedMax);

        if (Application.isPlaying)
        {
            ConfigurationChanged?.Invoke(this);
        }
    }
}
