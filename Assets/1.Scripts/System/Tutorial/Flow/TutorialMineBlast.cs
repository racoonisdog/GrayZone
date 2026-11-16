using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 시네마틱용 지뢰 폭발 연출입니다. 이 오브젝트 위치에 지뢰를 잠깐 만들어 설치하고, 둘레에 체력 1인 적을 스폰한 뒤 지뢰를 터뜨립니다.
/// </summary>
/// <remarks>
/// Timeline의 Signal에서 <see cref="Play"/>를 부릅니다. 폭발 피해는 지뢰의 평소 피해 경로를 그대로 쓰므로,
/// 적은 일반 사망(래그돌·시체 유지 시간) 규칙대로 쓰러집니다. 지뢰는 터진 뒤 정한 시간이 지나면 없앱니다.
/// </remarks>
public sealed class TutorialMineBlast : MonoBehaviour
{
    /// <summary>지뢰가 터지는 순간 알립니다. 튜토리얼이 폭발에 맞춰 대원을 쓰러뜨릴 때 씁니다.</summary>
    public static event System.Action Exploded;

    [Tooltip("잠깐 만들어 터뜨릴 지뢰 프리팹입니다.")]
    [SerializeField] private MineTrap m_minePrefab;

    [Tooltip("폭발 범위 안에 스폰할 적 프리팹입니다.")]
    [SerializeField] private EnemyController m_enemyPrefab;

    [Tooltip("스폰할 적 수입니다.")]
    [Min(0)]
    [SerializeField] private int m_enemyCount = 3;

    [Tooltip("이 위치 둘레로 적을 흩어 놓을 반경(m)입니다. 지뢰 폭발 반경보다 작아야 모두 휘말립니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_enemySpreadRadius = 1.5f;

    [Tooltip("스폰한 적의 체력입니다.")]
    [Min(1)]
    [SerializeField] private int m_enemyHp = 1;

    [Tooltip("스폰한 적이 죽은 뒤 시체를 남겨 둘 시간(초)입니다. 사고 현장에 도착할 때까지 보이도록 적 종류별 설정보다 길게 둡니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_corpseLifetime = 120.0f;

    [Tooltip("적 위치를 NavMesh 위로 붙일 때 찾을 반경(m)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_navMeshSampleRadius = 2.0f;

    [Tooltip("지뢰가 터진 뒤 지뢰 오브젝트를 없앨 때까지의 시간(초)입니다. 터지는 순간 꺼 두므로 화면에는 보이지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_mineLifetime = 3.0f;

    /// <summary>적 스폰과 지뢰 설치·폭발을 시작합니다. Signal에서 부릅니다.</summary>
    public void Play()
    {
        if (m_minePrefab == null)
        {
            Debug.LogWarning($"[TutorialMineBlast] '{name}'에 지뢰 프리팹이 비어 있습니다.", this);
            return;
        }

        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        Vector3 center = transform.position;
        EnemyController[] enemies = new EnemyController[m_enemyPrefab != null ? m_enemyCount : 0];

        // 처치 수 집계는 적이 켜지는 순간 구독하므로, 꺼진 부모 아래에 만들어 집계 제외를 먼저 정한 뒤 꺼냅니다.
        // 튜토리얼 적 목록에 넣어 두면 튜토리얼이 끝날 때 시체까지 함께 치웁니다.
        TutorialEvents events = FindFirstObjectByType<TutorialEvents>();
        GameObject inactiveParent = new GameObject("TutorialMineBlast_Spawn");
        inactiveParent.SetActive(false);
        for (int i = 0; i < enemies.Length; i++)
        {
            Vector2 offset = Random.insideUnitCircle * m_enemySpreadRadius;
            Vector3 position = center + new Vector3(offset.x, 0.0f, offset.y);
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, m_navMeshSampleRadius, NavMesh.AllAreas))
            {
                position = hit.position;
            }

            EnemyController enemy = Instantiate(m_enemyPrefab, position, Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f), inactiveParent.transform);
            enemy.CorpseLifetimeOverride = m_corpseLifetime;
            EnemyHealth health = enemy.GetComponentInChildren<EnemyHealth>(true);
            if (health != null)
            {
                health.ExcludeFromCombatStats = true;
            }

            enemy.transform.SetParent(null, true);
            enemies[i] = enemy;
            if (events != null)
            {
                events.TrackSpawnedEnemy(enemy);
            }
        }

        Destroy(inactiveParent);

        MineTrap mine = Instantiate(m_minePrefab, center, transform.rotation);

        // 적과 지뢰의 Start(체력 초기화, 함정 설정)가 끝난 뒤 체력을 맞추고 터뜨립니다.
        yield return null;

        foreach (EnemyController enemy in enemies)
        {
            if (enemy != null && enemy.Health != null)
            {
                enemy.Health.SetCurrentHP(m_enemyHp);
            }
        }

        if (mine != null)
        {
            mine.gameObject.SetActive(true);
            mine.Build();
            mine.Explode();
            Exploded?.Invoke();

            // 터진 지뢰는 청사진으로 돌아가 보이므로 바로 끕니다. 폭발 이펙트와 소리는 지뢰 밖에 따로 생겨 그대로 재생됩니다.
            mine.gameObject.SetActive(false);
            Destroy(mine.gameObject, m_mineLifetime);
        }
    }
}
