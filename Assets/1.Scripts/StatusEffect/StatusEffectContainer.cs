using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>한 전투 대상에게 적용 중인 버프·디버프·상태이상의 런타임 인스턴스를 관리합니다.</summary>
[DisallowMultipleComponent]
public sealed class StatusEffectContainer : MonoBehaviour
{
    private sealed class Entry
    {
        public StatusEffectDefinitionSO Definition;
        public Faction SourceFaction;
        public GameObject Source;
        public int Stacks;
        public float ExpireTime;
        public float NextTickTime;
        public GameObject VisualInstance;
    }

    private sealed class OneShotVisual
    {
        public UnityEngine.Object Key;
        public StatusEffectVisualSO Definition;
        public GameObject Instance;
        public float ExpireTime;
    }

    private readonly List<Entry> m_entries = new List<Entry>();
    private readonly List<OneShotVisual> m_oneShotVisuals = new List<OneShotVisual>();
    private readonly Dictionary<GameObject, Stack<GameObject>> m_visualPool =
        new Dictionary<GameObject, Stack<GameObject>>();

    [Header("Recipient Visuals")]
    [Tooltip("Optional anchor for body status visuals. If empty, a renderer-bounds center anchor is created at runtime.")]
    [SerializeField] private Transform m_effectAnchor;

    private IDamageable m_damageable;
    private SquadMemberController m_squadMember;
    private Transform m_runtimeEffectAnchor;
    private Transform m_visualPoolRoot;

    public int Count => m_entries.Count;
    public int ActiveVisualCount
    {
        get
        {
            int count = m_oneShotVisuals.Count;
            for (int i = 0; i < m_entries.Count; i++)
            {
                if (m_entries[i].VisualInstance != null && m_entries[i].VisualInstance.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int PooledVisualCount
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<GameObject, Stack<GameObject>> pair in m_visualPool)
            {
                count += pair.Value.Count;
            }

            return count;
        }
    }

    public float MovementSpeedMultiplier { get; private set; } = 1.0f;
    public float ActionSpeedMultiplier { get; private set; } = 1.0f;

    /// <summary>효과 추가·갱신·중첩·제거 시 UI가 목록을 다시 읽도록 알립니다.</summary>
    public event Action<StatusEffectContainer> OnChanged;

    private void Awake()
    {
        ResolveOwner();
    }

    private void Update()
    {
        if (m_entries.Count == 0 && m_oneShotVisuals.Count == 0)
        {
            return;
        }

        ResolveOwner();
        if (m_damageable != null && m_damageable.IsDead)
        {
            Clear();
            return;
        }

        float now = Time.time;
        bool changed = false;
        for (int i = m_entries.Count - 1; i >= 0; i--)
        {
            Entry entry = m_entries[i];
            ApplyExpiredTicks(entry, now);

            if (now >= entry.ExpireTime || (m_damageable != null && m_damageable.IsDead))
            {
                RecycleVisual(entry.Definition != null ? entry.Definition.RecipientVisual : null, entry.VisualInstance);
                m_entries.RemoveAt(i);
                changed = true;
            }
        }

        for (int i = m_oneShotVisuals.Count - 1; i >= 0; i--)
        {
            OneShotVisual visual = m_oneShotVisuals[i];
            if (now < visual.ExpireTime && (m_damageable == null || !m_damageable.IsDead))
            {
                continue;
            }

            RecycleVisual(visual.Definition, visual.Instance);
            m_oneShotVisuals.RemoveAt(i);
        }

        if (changed)
        {
            RecalculateModifiers();
            OnChanged?.Invoke(this);
        }
    }

    /// <summary>효과를 추가하거나 정의된 중첩 규칙에 따라 기존 효과를 갱신합니다.</summary>
    public bool Apply(StatusEffectDefinitionSO definition, Faction sourceFaction, GameObject source = null)
    {
        if (definition == null)
        {
            return false;
        }

        ResolveOwner();
        if (m_damageable != null && m_damageable.IsDead)
        {
            return false;
        }

        float now = Time.time;
        Entry entry = FindEntry(definition, source);
        if (entry == null)
        {
            entry = new Entry
            {
                Definition = definition,
                SourceFaction = sourceFaction,
                Source = source,
                Stacks = 1,
                ExpireTime = now + definition.Duration,
                NextTickTime = now + definition.TickInterval,
            };
            entry.VisualInstance = SpawnVisual(definition.RecipientVisual);
            m_entries.Add(entry);
        }
        else
        {
            entry.SourceFaction = sourceFaction;
            entry.Source = source;
            entry.ExpireTime = now + definition.Duration;
            if (definition.StackPolicy == StatusEffectStackPolicy.StackIntensity)
            {
                entry.Stacks = Mathf.Min(definition.MaxStacks, entry.Stacks + 1);
            }

            if (entry.VisualInstance == null)
            {
                entry.VisualInstance = SpawnVisual(definition.RecipientVisual);
            }
            else if (definition.RecipientVisual != null && definition.RecipientVisual.RestartOnStatusRefresh)
            {
                FeedbackPlaybackUtility.RestartPlayback(entry.VisualInstance);
            }
        }

        RecalculateModifiers();
        OnChanged?.Invoke(this);
        return true;
    }

    public bool Has(StatusEffectDefinitionSO definition)
    {
        return definition != null && m_entries.Exists(entry => entry.Definition == definition);
    }

    public int GetStacks(StatusEffectDefinitionSO definition)
    {
        int stacks = 0;
        for (int i = 0; i < m_entries.Count; i++)
        {
            if (m_entries[i].Definition == definition)
            {
                stacks += m_entries[i].Stacks;
            }
        }

        return stacks;
    }

    public float GetRemaining(StatusEffectDefinitionSO definition)
    {
        float remaining = 0.0f;
        for (int i = 0; i < m_entries.Count; i++)
        {
            if (m_entries[i].Definition == definition)
            {
                remaining = Mathf.Max(remaining, m_entries[i].ExpireTime - Time.time);
            }
        }

        return Mathf.Max(0.0f, remaining);
    }

    public bool Remove(StatusEffectDefinitionSO definition)
    {
        int removed = 0;
        for (int i = m_entries.Count - 1; i >= 0; i--)
        {
            Entry entry = m_entries[i];
            if (entry.Definition != definition)
            {
                continue;
            }

            RecycleVisual(definition != null ? definition.RecipientVisual : null, entry.VisualInstance);
            m_entries.RemoveAt(i);
            removed++;
        }

        if (removed <= 0)
        {
            return false;
        }

        RecalculateModifiers();
        OnChanged?.Invoke(this);
        return true;
    }

    public void Clear()
    {
        if (m_entries.Count == 0 && m_oneShotVisuals.Count == 0)
        {
            return;
        }

        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            RecycleVisual(entry.Definition != null ? entry.Definition.RecipientVisual : null, entry.VisualInstance);
        }

        for (int i = 0; i < m_oneShotVisuals.Count; i++)
        {
            RecycleVisual(m_oneShotVisuals[i].Definition, m_oneShotVisuals[i].Instance);
        }

        m_entries.Clear();
        m_oneShotVisuals.Clear();
        RecalculateModifiers();
        OnChanged?.Invoke(this);
    }

    /// <summary>Plays a pooled body visual without adding a persistent gameplay status.</summary>
    public GameObject PlayVisual(StatusEffectVisualSO visual, float lifetime = -1.0f)
    {
        GameObject instance = SpawnVisual(visual);
        if (instance == null)
        {
            return null;
        }

        m_oneShotVisuals.Add(new OneShotVisual
        {
            Key = null,
            Definition = visual,
            Instance = instance,
            ExpireTime = Time.time + (lifetime > 0.0f ? lifetime : visual.OneShotLifetime),
        });
        return instance;
    }

    /// <summary>
    /// Plays or refreshes a keyed body visual. Reapplying the same buff extends one instance
    /// instead of stacking duplicate visuals.
    /// </summary>
    public GameObject PlayOrRefreshVisual(
        UnityEngine.Object key,
        StatusEffectVisualSO visual,
        float lifetime = -1.0f)
    {
        if (key == null)
        {
            return PlayVisual(visual, lifetime);
        }

        float resolvedLifetime = lifetime > 0.0f
            ? lifetime
            : visual != null ? visual.OneShotLifetime : 0.0f;

        for (int i = 0; i < m_oneShotVisuals.Count; i++)
        {
            OneShotVisual active = m_oneShotVisuals[i];
            if (active.Key != key)
            {
                continue;
            }

            active.ExpireTime = Time.time + resolvedLifetime;
            if (active.Definition == visual && active.Instance != null)
            {
                if (visual != null && visual.RestartOnStatusRefresh)
                {
                    FeedbackPlaybackUtility.RestartPlayback(active.Instance);
                }

                return active.Instance;
            }

            RecycleVisual(active.Definition, active.Instance);
            active.Definition = visual;
            active.Instance = SpawnVisual(visual);
            return active.Instance;
        }

        GameObject instance = SpawnVisual(visual);
        if (instance == null)
        {
            return null;
        }

        m_oneShotVisuals.Add(new OneShotVisual
        {
            Key = key,
            Definition = visual,
            Instance = instance,
            ExpireTime = Time.time + resolvedLifetime,
        });
        return instance;
    }

    /// <summary>Stops and pools every transient body visual registered with the supplied key.</summary>
    public bool StopVisual(UnityEngine.Object key)
    {
        if (key == null)
        {
            return false;
        }

        bool stopped = false;
        for (int i = m_oneShotVisuals.Count - 1; i >= 0; i--)
        {
            OneShotVisual active = m_oneShotVisuals[i];
            if (active.Key != key)
            {
                continue;
            }

            RecycleVisual(active.Definition, active.Instance);
            m_oneShotVisuals.RemoveAt(i);
            stopped = true;
        }

        return stopped;
    }

    /// <summary>효과를 받을 GameObject에서 컨테이너를 찾고, 없으면 런타임에 하나 추가합니다.</summary>
    public static StatusEffectContainer GetOrAdd(GameObject owner)
    {
        if (owner == null)
        {
            return null;
        }

        StatusEffectContainer container = owner.GetComponent<StatusEffectContainer>();
        return container != null ? container : owner.AddComponent<StatusEffectContainer>();
    }

    /// <summary>피해 대상 컴포넌트가 있는 GameObject에 컨테이너를 찾거나 추가합니다.</summary>
    public static StatusEffectContainer GetOrAdd(IDamageable target)
    {
        Component component = target as Component;
        return component != null ? GetOrAdd(component.gameObject) : null;
    }

    private Entry FindEntry(StatusEffectDefinitionSO definition, GameObject source)
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            if (entry.Definition != definition)
            {
                continue;
            }

            if (definition.StackPolicy != StatusEffectStackPolicy.IndependentBySource || entry.Source == source)
            {
                return entry;
            }
        }

        return null;
    }

    private void ApplyExpiredTicks(Entry entry, float now)
    {
        StatusEffectDefinitionSO definition = entry.Definition;
        if (definition == null || !definition.HasPeriodicDamage || m_damageable == null)
        {
            return;
        }

        while (entry.NextTickTime <= now && entry.NextTickTime <= entry.ExpireTime)
        {
            int damage = definition.PeriodicDamage * Mathf.Max(1, entry.Stacks);
            if (!CombatDamage.TryApplyDamage(m_damageable, entry.SourceFaction, damage, entry.Source))
            {
                break;
            }

            entry.NextTickTime += definition.TickInterval;
        }
    }

    private void RecalculateModifiers()
    {
        float movement = 1.0f;
        float action = 1.0f;
        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            if (entry.Definition == null)
            {
                continue;
            }

            movement *= Mathf.Pow(entry.Definition.MovementSpeedMultiplier, entry.Stacks);
            action *= Mathf.Pow(entry.Definition.ActionSpeedMultiplier, entry.Stacks);
        }

        MovementSpeedMultiplier = Mathf.Max(0.01f, movement);
        ActionSpeedMultiplier = Mathf.Max(0.01f, action);

        if (m_squadMember != null)
        {
            m_squadMember.SetSpeedPercent(MovementSpeedMultiplier * 100.0f, ActionSpeedMultiplier * 100.0f);
        }
    }

    private void ResolveOwner()
    {
        if (m_damageable == null)
        {
            m_damageable = GetComponent<IDamageable>()
                ?? GetComponentInChildren<IDamageable>(true)
                ?? GetComponentInParent<IDamageable>();
        }

        if (m_squadMember == null)
        {
            m_squadMember = GetComponent<SquadMemberController>()
                ?? GetComponentInParent<SquadMemberController>();
        }
    }

    private GameObject SpawnVisual(StatusEffectVisualSO visual)
    {
        GameObject prefab = visual != null ? visual.Prefab : null;
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = TakeVisual(prefab);
        if (instance == null)
        {
            instance = Instantiate(prefab);
        }

        Transform instanceTransform = instance.transform;
        instanceTransform.SetParent(ResolveEffectAnchor(), false);
        instanceTransform.localPosition = visual.LocalPosition;
        instanceTransform.localRotation = visual.LocalRotation;
        instanceTransform.localScale = Vector3.Scale(prefab.transform.localScale, visual.LocalScaleMultiplier);
        instance.SetActive(true);
        FeedbackPlaybackUtility.RestartPlayback(instance);
        return instance;
    }

    private GameObject TakeVisual(GameObject prefab)
    {
        if (!m_visualPool.TryGetValue(prefab, out Stack<GameObject> idle))
        {
            return null;
        }

        while (idle.Count > 0)
        {
            GameObject instance = idle.Pop();
            if (instance != null)
            {
                return instance;
            }
        }

        return null;
    }

    private void RecycleVisual(StatusEffectVisualSO visual, GameObject instance)
    {
        GameObject prefab = visual != null ? visual.Prefab : null;
        if (prefab == null || instance == null)
        {
            return;
        }

        instance.SetActive(false);
        instance.transform.SetParent(ResolveVisualPoolRoot(), false);
        if (!m_visualPool.TryGetValue(prefab, out Stack<GameObject> idle))
        {
            idle = new Stack<GameObject>();
            m_visualPool.Add(prefab, idle);
        }

        idle.Push(instance);
    }

    private Transform ResolveEffectAnchor()
    {
        if (m_effectAnchor != null)
        {
            return m_effectAnchor;
        }

        if (m_runtimeEffectAnchor != null)
        {
            return m_runtimeEffectAnchor;
        }

        GameObject anchorObject = new GameObject("StatusEffectAnchor");
        m_runtimeEffectAnchor = anchorObject.transform;
        m_runtimeEffectAnchor.SetParent(transform, false);

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return m_runtimeEffectAnchor;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        m_runtimeEffectAnchor.position = bounds.center;
        return m_runtimeEffectAnchor;
    }

    private Transform ResolveVisualPoolRoot()
    {
        if (m_visualPoolRoot != null)
        {
            return m_visualPoolRoot;
        }

        GameObject root = new GameObject("StatusEffectVisualPool(Idle)");
        m_visualPoolRoot = root.transform;
        m_visualPoolRoot.SetParent(transform, false);
        root.SetActive(false);
        return m_visualPoolRoot;
    }
}
