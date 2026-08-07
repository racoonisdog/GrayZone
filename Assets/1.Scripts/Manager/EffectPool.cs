using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 일회성 시각 피드백의 예산 종류입니다.
/// </summary>
/// <remarks>
/// 종류를 둘로만 나눈 이유는 예산이 "씬이 얼마나 감당하는가"를 답하는 값이기 때문입니다.
/// 지형 탄흔과 혈흔을 따로 세면 총량이 여러 값의 합이 되어 한눈에 읽히지 않습니다.
/// </remarks>
public enum EffectCategory
{
    /// <summary>수명이 짧은 연출입니다. 임팩트 스파크, 피격 이펙트 등입니다.</summary>
    Effect = 0,

    /// <summary>표면에 오래 남는 자국입니다. 지형 탄흔과 혈흔이 함께 씁니다.</summary>
    Decal = 1,
}

/// <summary>
/// 일회성 시각 피드백 인스턴스를 재사용하고 종류별 개수를 제한하는 풀입니다.
/// </summary>
/// <remarks>
/// <para>
/// 두 가지를 함께 처리합니다. <b>개수 제한</b>은 수명만으로 막지 못하는 순간 누적을 막고,
/// <b>재사용</b>은 사격마다 생기는 <see cref="Object.Instantiate"/> 부담을 없앱니다.
/// 상한에 도달한 뒤에는 새로 만들지 않고 가장 오래된 것을 되돌려 쓰므로 할당이 사실상 사라집니다.
/// </para>
/// <para>
/// 수명은 프리팹이 아니라 이 풀이 셉니다. <see cref="Object.Destroy(Object, float)"/>로 맡기면
/// 풀이 인스턴스를 잃어버려 재사용할 수 없습니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class EffectPool : MonoBehaviour
{
    [Tooltip("동시에 존재할 수 있는 데칼 최대 개수입니다. 지형 탄흔과 혈흔이 이 예산을 나눠 씁니다. 0 이하면 제한하지 않습니다.")]
    [Min(0)]
    [SerializeField] private int m_maxDecals = 128;

    [Tooltip("동시에 존재할 수 있는 단명 이펙트 최대 개수입니다. 0 이하면 제한하지 않습니다.")]
    [Min(0)]
    [SerializeField] private int m_maxEffects = 64;

    [Tooltip("개인 이펙트 포함하기: 총기처럼 자체 풀을 가진 개인 이펙트의 활성 개수를 공용 이펙트 최대치에 포함합니다. 켜면 개인 활성 개수만큼 공용 풀이 사용할 수 있는 수량이 줄어듭니다.")]
    [InspectorName("개인 이펙트 포함하기")]
    [SerializeField] private bool m_includePersonalEffects = true;

    [Tooltip("표면에서 살짝 띄워 배치할 거리(m)입니다. 데칼이 표면과 겹쳐 지글거리는 것을 막습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_surfaceOffset = 0.002f;

    /// <summary>지금 살아 있는 인스턴스 하나의 상태입니다.</summary>
    private sealed class LiveEffect
    {
        public GameObject Instance;
        public GameObject Prefab;
        public float ExpiresAt;
        public bool HasLifetime;
    }

    /// <summary>종류별로 살아 있는 인스턴스입니다. 오래된 것이 앞에 옵니다.</summary>
    private readonly List<LiveEffect>[] m_live =
    {
        new List<LiveEffect>(64),
        new List<LiveEffect>(128),
    };

    /// <summary>프리팹별로 잠들어 있는 인스턴스입니다.</summary>
    private readonly Dictionary<GameObject, Stack<GameObject>> m_idle =
        new Dictionary<GameObject, Stack<GameObject>>();

    /// <summary>재사용 대기 중인 인스턴스를 모아 두는 런타임 컨테이너입니다.</summary>
    /// <remarks>
    /// 씬에 미리 두지 않고 코드가 만듭니다. 씬 배선에 의존하지 않고, 잠든 인스턴스가 하이어라키에
    /// 흩어지지 않도록 한곳에 격리하기 위해서입니다.
    /// </remarks>
    private Transform m_idleRoot;

    /// <summary>각 소유자가 자체 풀링 중인 활성 개인 이펙트 수입니다.</summary>
    private int m_livePersonalEffects;

    /// <summary>데칼 예산입니다.</summary>
    public int MaxDecals => m_maxDecals;

    /// <summary>단명 이펙트 예산입니다.</summary>
    public int MaxEffects => m_maxEffects;

    /// <summary>개인 이펙트를 공용 이펙트 최대치 계산에 포함하는지 여부입니다.</summary>
    public bool IncludePersonalEffects => m_includePersonalEffects;

    /// <summary>현재 각 소유자가 자체 풀링 중인 활성 개인 이펙트 수입니다.</summary>
    public int LivePersonalEffects => m_livePersonalEffects;

    /// <summary>개인 이펙트를 반영한 현재 공용 단명 이펙트 가용량입니다. 원래 최대치가 0 이하면 제한 없음 의미를 유지합니다.</summary>
    public int AvailableCommonEffectCapacity => ResolveCommonEffectBudget();

    /// <summary>지정한 종류로 지금 살아 있는 인스턴스 수입니다.</summary>
    public int GetLiveCount(EffectCategory category) => m_live[(int)category].Count;

    /// <summary>자체 풀에서 개인 이펙트 하나가 활성화됐음을 보고합니다.</summary>
    public void RegisterPersonalEffect()
    {
        m_livePersonalEffects++;
        TrimCommonEffectsToBudget();
    }

    /// <summary>자체 풀의 개인 이펙트 하나가 회수됐음을 보고합니다.</summary>
    public void UnregisterPersonalEffect()
    {
        m_livePersonalEffects = Mathf.Max(0, m_livePersonalEffects - 1);
    }

    /// <summary>
    /// 표면 법선에 맞춰 정렬한 인스턴스를 생성하거나 재사용합니다.
    /// </summary>
    /// <param name="prefab">생성할 프리팹입니다. null이면 아무 일도 하지 않습니다.</param>
    /// <param name="position">생성 위치입니다.</param>
    /// <param name="normal">표면 법선입니다.</param>
    /// <param name="lifetime">자동 회수까지의 시간(초)입니다. 0 이하면 예산에 밀릴 때까지 남습니다.</param>
    /// <param name="category">예산 종류입니다.</param>
    /// <param name="parent">따라다닐 부모입니다. 움직이는 대상에 붙일 때 지정합니다.</param>
    /// <returns>생성하거나 재사용한 인스턴스입니다. 프리팹이 없으면 null입니다.</returns>
    public GameObject SpawnAligned(
        GameObject prefab,
        Vector3 position,
        Vector3 normal,
        float lifetime,
        EffectCategory category,
        Transform parent = null)
    {
        Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;

        return Spawn(
            prefab,
            position + safeNormal * m_surfaceOffset,
            Quaternion.LookRotation(safeNormal),
            lifetime,
            category,
            parent);
    }

    /// <summary>
    /// 인스턴스를 생성하거나 재사용합니다.
    /// </summary>
    /// <param name="prefab">생성할 프리팹입니다. null이면 아무 일도 하지 않습니다.</param>
    /// <param name="position">생성 위치입니다.</param>
    /// <param name="rotation">생성 회전입니다.</param>
    /// <param name="lifetime">자동 회수까지의 시간(초)입니다. 0 이하면 예산에 밀릴 때까지 남습니다.</param>
    /// <param name="category">예산 종류입니다.</param>
    /// <param name="parent">따라다닐 부모입니다.</param>
    /// <returns>생성하거나 재사용한 인스턴스입니다. 프리팹이 없으면 null입니다.</returns>
    public GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifetime,
        EffectCategory category,
        Transform parent = null)
    {
        if (prefab == null)
        {
            return null;
        }

        List<LiveEffect> live = m_live[(int)category];
        int budget = category == EffectCategory.Decal ? m_maxDecals : ResolveCommonEffectBudget();

        if (category == EffectCategory.Effect && m_maxEffects > 0 && budget <= 0)
        {
            return null;
        }

        // 상한에 걸리면 가장 오래된 것을 먼저 되돌립니다. 그 인스턴스가 같은 프리팹이면 바로 다시 쓰입니다.
        while (budget > 0 && live.Count >= budget)
        {
            RecycleAt(live, 0);
        }

        GameObject instance = TakeIdle(prefab);

        if (instance == null)
        {
            instance = Instantiate(prefab);
        }

        Transform t = instance.transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(position, rotation);

        instance.SetActive(true);
        FeedbackPlaybackUtility.RestartPlayback(instance);

        live.Add(new LiveEffect
        {
            Instance = instance,
            Prefab = prefab,
            ExpiresAt = Time.time + lifetime,
            HasLifetime = lifetime > 0.0f,
        });

        return instance;
    }

    /// <summary>수명이 끝난 인스턴스를 회수합니다.</summary>
    private void Update()
    {
        float now = Time.time;

        for (int categoryIndex = 0; categoryIndex < m_live.Length; categoryIndex++)
        {
            List<LiveEffect> live = m_live[categoryIndex];

            // 뒤에서부터 훑습니다. 앞에서 지우면 남은 항목이 앞으로 밀려 인덱스가 어긋납니다.
            for (int i = live.Count - 1; i >= 0; i--)
            {
                LiveEffect entry = live[i];

                // 부모가 사라지면 인스턴스도 함께 파괴됩니다. 적 시체가 삭제될 때 붙어 있던 혈흔이 그렇습니다.
                if (entry.Instance == null)
                {
                    live.RemoveAt(i);
                    continue;
                }

                if (entry.HasLifetime && now >= entry.ExpiresAt)
                {
                    RecycleAt(live, i);
                }
            }
        }
    }

    /// <summary>지정한 위치의 인스턴스를 잠재우고 목록에서 뺍니다.</summary>
    private void RecycleAt(List<LiveEffect> live, int index)
    {
        LiveEffect entry = live[index];
        live.RemoveAt(index);

        if (entry.Instance == null)
        {
            return;
        }

        entry.Instance.SetActive(false);
        entry.Instance.transform.SetParent(ResolveIdleRoot(), false);

        if (!m_idle.TryGetValue(entry.Prefab, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            m_idle[entry.Prefab] = stack;
        }

        stack.Push(entry.Instance);
    }

    /// <summary>잠들어 있는 같은 프리팹의 인스턴스를 꺼냅니다. 없으면 null입니다.</summary>
    private GameObject TakeIdle(GameObject prefab)
    {
        if (!m_idle.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            return null;
        }

        while (stack.Count > 0)
        {
            GameObject instance = stack.Pop();

            // 씬 전환이나 부모 파괴로 함께 사라진 인스턴스가 남아 있을 수 있습니다.
            if (instance != null)
            {
                return instance;
            }
        }

        return null;
    }

    /// <summary>개인 이펙트 포함 설정을 반영한 공용 단명 이펙트 최대 개수를 계산합니다.</summary>
    private int ResolveCommonEffectBudget()
    {
        if (m_maxEffects <= 0)
        {
            return m_maxEffects;
        }

        return m_includePersonalEffects
            ? Mathf.Max(0, m_maxEffects - m_livePersonalEffects)
            : m_maxEffects;
    }

    /// <summary>개인 이펙트 증가로 줄어든 공용 가용량에 맞춰 오래된 공용 이펙트를 회수합니다.</summary>
    private void TrimCommonEffectsToBudget()
    {
        if (!m_includePersonalEffects || m_maxEffects <= 0)
        {
            return;
        }

        List<LiveEffect> effects = m_live[(int)EffectCategory.Effect];
        int budget = ResolveCommonEffectBudget();

        while (effects.Count > budget)
        {
            RecycleAt(effects, 0);
        }
    }

    /// <summary>잠든 인스턴스를 담을 컨테이너를 만들거나 가져옵니다.</summary>
    private Transform ResolveIdleRoot()
    {
        if (m_idleRoot != null)
        {
            return m_idleRoot;
        }

        GameObject root = new GameObject("EffectPool(Idle)");
        root.transform.SetParent(transform, false);
        root.SetActive(false);

        m_idleRoot = root.transform;
        return m_idleRoot;
    }
}
