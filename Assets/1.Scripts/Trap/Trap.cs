using UnityEngine;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 함정이 피해를 주는 방식입니다.
/// </summary>
public enum TrapDamageMode
{
    /// <summary>범위에 들어온 순간 한 번만 피해를 줍니다. 계속 머물러도 추가 피해는 없습니다.</summary>
    Once,

    /// <summary>들어온 순간 한 번 주고, 머무는 동안 틱 간격마다 반복해서 더 줍니다.</summary>
    Tick,
}

/// <summary>
/// 다 쓴 함정을 언제 다시 설치할 수 있는지에 대한 규칙입니다.
/// </summary>
/// <remarks>
/// "다 썼다"는 것은 함정마다 다릅니다. 지뢰는 터진 순간이고, 철조망은 내구도가 다 닳은 순간입니다.
/// 어느 쪽이든 그 뒤 언제 다시 청사진으로 돌아오는지를 이 값이 정합니다.
///
/// 순서를 바꾸지 마세요. 이 필드가 생기기 전에 저장된 프리팹·씬에는 값이 적혀 있지 않아, 불러올 때
/// 첫 번째 값(0)으로 들어옵니다. C# 필드 초기값은 그 경우를 덮지 못합니다. 그래서 첫 번째 값이
/// 곧 "적어 두지 않았을 때의 값"이고, <c>m_rebuildPolicy</c>의 초기값도 여기에 맞춰 두었습니다.
/// </remarks>
public enum TrapRebuildPolicy
{
    /// <summary>전투 도중에도 곧바로 다시 설치할 수 있습니다. 자원만 있으면 웨이브 중에 복구됩니다.</summary>
    /// <remarks>
    /// 열거형은 순서(정수)로 직렬화되므로 이름만 바꾸면 이미 저장된 값은 그대로 이 항목을 가리킵니다.
    /// 순서를 바꾸거나 사이에 새 항목을 끼우면 저장된 값의 의미가 달라집니다.
    /// </remarks>
    Always,

    /// <summary>웨이브 사이 휴식 구간이 시작되면 다시 설치할 수 있습니다. 전투 중에는 복구할 수 없습니다.</summary>
    OnRest,

    /// <summary>이번 방어전에서는 다시 설치할 수 없습니다. 다음 방어전이 시작될 때 복구됩니다.</summary>
    NextDefense,
}

/// <summary>
/// 설치형 함정의 공통 기반입니다.
/// </summary>
/// <remarks>
/// 모든 함정이 공통으로 갖는 것만 둡니다. 내구도와 피해량, 피해를 주는 방식, 그리고 다 쓴 뒤
/// 언제 다시 설치할 수 있는지입니다. 무엇을 어떻게 감지하고 피해 외에 어떤 효과를 주는지는
/// 파생 함정이 정합니다.
///
/// 내구도가 0이 되면 <see cref="Deplete"/>가 함정을 청사진 상태로 되돌립니다. 언제 다시 설치할 수
/// 있는지는 <see cref="TrapRebuildPolicy"/>가 정합니다. 무엇이 내구도를 깎는지는 파생이 정합니다.
/// 지뢰는 터질 때 한 번에 다 쓰고, 철조망은 적이 들어올 때마다 조금씩 깎입니다.
///
/// 아직 <c>IDamageable</c>은 구현하지 않았습니다. 총에 맞아 부서지는 규칙이 정해지면 이 클래스에
/// 구현해 파생 전체가 한 번에 따라오게 하는 것이 자연스럽습니다.
/// </remarks>
public abstract class Trap : MonoBehaviour, IInteractable
{
    [Header("Build")]
    [Tooltip("켜면 시작할 때 이미 설치된 상태입니다. 끄면 청사진 상태로 시작하며, 자원을 내고 설치하기 전까지 작동하지 않습니다.")]
    [FormerlySerializedAs("m_startAsBlueprint")]
    [SerializeField] private bool m_startPlaced = false;

    [Tooltip("설치할 때 소모할 자원입니다. 셸터에 이미 정의된 자원 중에서 고릅니다.")]
    [Variants(
        ResourceIds.Food,
        ResourceIds.Fuel,
        ResourceIds.FacilityUpgradePart,
        ResourceIds.UpgradePartMaterial,
        ResourceIds.WeaponPartMaterial,
        ResourceIds.MedicineMaterial)]
    [SerializeField] private string m_buildCostResourceId = ResourceIds.UpgradePartMaterial;

    [Tooltip("설치에 소모할 자원의 수량입니다. 0이면 자원 없이 설치됩니다.")]
    [Min(0)]
    [SerializeField] private int m_buildCostAmount = 1;

    [Tooltip("청사진 상태에서 UI에 표시할 문구입니다. 비용은 뒤에 자동으로 붙습니다.")]
    [SerializeField] private string m_buildPrompt = "설치";

    [Tooltip("설치에 필요한 홀드 시간(초)입니다. 0이면 누르는 즉시 설치됩니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_buildHoldDuration = 0.0f;

    [Foldout("Build Visual")]
    [Tooltip("청사진 상태 동안 이 함정의 모든 렌더러에 입힐 머테리얼입니다. 비워 두면 외형이 바뀌지 않습니다.")]
    [SerializeField] private Material m_blueprintMaterial;

    [Tooltip("청사진 상태에서 그림자를 끌지 여부입니다. 반투명한 설계도가 진한 그림자를 드리우면 어색합니다.")]
    [SerializeField] private bool m_disableShadowWhileBlueprint = true;

    [Tooltip("청사진 상태에서만 켜 둘 오브젝트입니다. 머테리얼 교체로 부족할 때만 씁니다.")]
    [SerializeField] private GameObject m_blueprintVisualRoot;

    [Tooltip("설치된 뒤에만 켜 둘 오브젝트입니다. 머테리얼 교체로 부족할 때만 씁니다.")]
    [SerializeField] private GameObject m_builtVisualRoot;
    [EndFoldout]

    /// <summary>머테리얼을 되돌리기 위해 기억해 두는 원래 구성입니다.</summary>
    /// <remarks>
    /// 렌더러마다 슬롯 수가 다를 수 있어 배열째 보관합니다. 청사진 머테리얼을 덮어쓰기 전에 한 번만 잡습니다.
    /// 이미 덮어쓴 뒤에 다시 잡으면 청사진 머테리얼이 원본으로 기억되어 설치해도 파랗게 남습니다.
    /// </remarks>
    private Renderer[] m_visualRenderers;
    private Material[][] m_originalMaterials;
    private UnityEngine.Rendering.ShadowCastingMode[] m_originalShadowModes;

    [Header("Trap")]
    [Tooltip("이 함정의 최대 내구도입니다. 0 이하로 닳으면 다 쓴 것으로 보고 청사진 상태로 돌아갑니다. 다시 설치하면 이 값으로 회복됩니다.")]
    [Min(0)]
    [FormerlySerializedAs("m_health")]
    [SerializeField] protected int m_maxHealth = 100;

    [Tooltip("지금 남은 내구도입니다. 확인용이라 여기서 고쳐도 설치·피해 경로를 거치지 않아 상태가 어긋납니다.")]
    [ReadOnly]
    [SerializeField] private int m_currentHealth;

    [Tooltip("다 쓴 함정을 언제 다시 설치할 수 있는지입니다. Always는 전투 중에도 바로, OnRest는 휴식 구간부터, NextDefense는 다음 방어전부터입니다.")]
    [SerializeField] protected TrapRebuildPolicy m_rebuildPolicy = TrapRebuildPolicy.Always;

    [Tooltip("한 번에 주는 피해량입니다. Tick 방식이면 1틱당 피해량입니다.")]
    [Min(0)]
    [SerializeField] protected int m_damage = 1;

    [Tooltip("피해를 주는 방식입니다. 어느 쪽이든 들어온 순간 한 번은 줍니다. Tick은 머무는 동안 추가로 반복합니다.")]
    [SerializeField] protected TrapDamageMode m_damageMode = TrapDamageMode.Tick;

    [Tooltip("Tick 방식에서 피해를 주는 간격(초)입니다. 0.5면 0.5초마다 한 번씩 m_damage만큼 들어갑니다. 진입 시 1회는 이 값과 무관하게 항상 들어갑니다.")]
    [ShowIf(nameof(m_damageMode), TrapDamageMode.Tick)]
    [Min(0.01f)]
    [FormerlySerializedAs("m_damageTickRate")]
    [SerializeField] protected float m_damageTickInterval = 1.0f;
    [EndIf]

    /// <summary>지금 남은 내구도입니다. 다시 설치하면 <see cref="MaxHealth"/>로 회복됩니다.</summary>
    public int Health => m_currentHealth;

    /// <summary>다시 설치했을 때 회복되는 최대 내구도입니다.</summary>
    public int MaxHealth => m_maxHealth;

    /// <summary>한 번에 주는 피해량입니다.</summary>
    public int Damage => m_damage;

    /// <summary>이 함정이 피해를 주는 방식입니다.</summary>
    public TrapDamageMode DamageMode => m_damageMode;

    /// <summary>Tick 방식에서 피해 사이의 간격(초)입니다.</summary>
    public float DamageInterval => Mathf.Max(0.01f, m_damageTickInterval);

    /// <summary>Tick 방식에서 1초당 피해 횟수입니다. 이전 API 호환용으로 유지합니다.</summary>
    public float DamageTickRate => 1.0f / DamageInterval;

    /// <summary>이 함정이 아직 멀쩡한지 여부입니다.</summary>
    public bool IsAlive => m_currentHealth > 0;

    /// <summary>설치가 끝나 실제로 작동하는 상태인지 여부입니다.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>아직 자원을 내지 않은 청사진 상태인지 여부입니다.</summary>
    public bool IsBlueprint => !IsBuilt;

    /// <summary>
    /// 다 써서 아직 다시 설치할 수 없는 상태인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 청사진과 다릅니다. 청사진은 자원만 내면 지을 수 있지만, 이 상태는 <see cref="TrapRebuildPolicy"/>가
    /// 정한 시점이 올 때까지 자원이 있어도 지을 수 없습니다.
    /// </remarks>
    public bool IsDepleted { get; private set; }


    /// <summary>휴식/다음 방어전 알림을 받기 위해 구독해 둔 방어전 매니저입니다.</summary>
    private DefenseManager m_defenseManager;

    /// <summary>모든 함정이 함께 쓰는 방어전 매니저 참조입니다.</summary>
    /// <remarks>
    /// 한 씬에 함정이 수십 개 깔리는데 각자 <c>FindFirstObjectByType</c>을 부르면 같은 탐색을 그만큼
    /// 반복합니다. 씬이 바뀌어 매니저가 사라지면 Unity의 null 비교가 참이 되므로 다시 찾습니다.
    /// </remarks>
    private static DefenseManager s_sharedDefenseManager;

    /// <summary>설치 비용입니다. 수량이 0이면 비용이 없는 것으로 봅니다.</summary>
    public ResourceCost BuildCost => new ResourceCost(m_buildCostResourceId, m_buildCostAmount);

    /// <summary>이 함정에 비용이 걸려 있는지 여부입니다.</summary>
    private bool HasBuildCost => m_buildCostAmount > 0 && !string.IsNullOrWhiteSpace(m_buildCostResourceId);

    protected virtual void Awake()
    {
        CacheVisuals();
        m_currentHealth = m_maxHealth;
        IsBuilt = m_startPlaced;
        ApplyBuildStateVisual();
    }

    /// <summary>
    /// 방어전 매니저의 휴식/시작 알림을 구독합니다.
    /// </summary>
    /// <remarks>
    /// 매니저가 없는 씬(셸터 등)에서도 함정은 동작해야 하므로 없으면 조용히 넘어갑니다. 대신 그런
    /// 씬에서는 <see cref="TrapRebuildPolicy.OnRest"/>와 <see cref="TrapRebuildPolicy.NextDefense"/>가
    /// 영영 복구되지 않습니다. 그 씬에서는 <see cref="TrapRebuildPolicy.Always"/>를 쓰세요.
    /// </remarks>
    protected virtual void OnEnable()
    {
        if (s_sharedDefenseManager == null)
        {
            s_sharedDefenseManager = FindFirstObjectByType<DefenseManager>(FindObjectsInactive.Include);
        }

        m_defenseManager = s_sharedDefenseManager;
        if (m_defenseManager == null)
        {
            return;
        }

        m_defenseManager.OnRestStarted += HandleRestStarted;
        m_defenseManager.OnDefenseStarted += HandleDefenseStarted;
    }

    protected virtual void OnDisable()
    {
        if (m_defenseManager == null)
        {
            return;
        }

        m_defenseManager.OnRestStarted -= HandleRestStarted;
        m_defenseManager.OnDefenseStarted -= HandleDefenseStarted;
    }

    /// <summary>휴식이 시작되면 그때 복구하기로 한 함정만 되살립니다.</summary>
    private void HandleRestStarted()
    {
        if (m_rebuildPolicy == TrapRebuildPolicy.OnRest)
        {
            Rearm();
        }
    }

    /// <summary>새 방어전이 시작되면 다 쓴 함정을 모두 되살립니다.</summary>
    /// <remarks>
    /// <see cref="TrapRebuildPolicy.NextDefense"/>만이 아니라 전부 되살립니다. 방어전이 새로 시작되면
    /// 어떤 정책이든 이전 전투에서 쓴 결과를 끌고 갈 이유가 없습니다.
    /// </remarks>
    private void HandleDefenseStarted()
    {
        Rearm();
    }

    /// <summary>청사진 머테리얼로 덮기 전에 원래 머테리얼과 그림자 설정을 기억해 둡니다.</summary>
    private void CacheVisuals()
    {
        m_visualRenderers = GetComponentsInChildren<Renderer>(true);
        m_originalMaterials = new Material[m_visualRenderers.Length][];
        m_originalShadowModes = new UnityEngine.Rendering.ShadowCastingMode[m_visualRenderers.Length];

        for (int i = 0; i < m_visualRenderers.Length; i++)
        {
            m_originalMaterials[i] = m_visualRenderers[i].sharedMaterials;
            m_originalShadowModes[i] = m_visualRenderers[i].shadowCastingMode;
        }
    }

    // ===== IInteractable =====

    /// <inheritdoc />
    public float HoldDuration => m_buildHoldDuration;

    /// <inheritdoc />
    /// <remarks>이미 설치됐거나, 다 써서 아직 복구 시점이 오지 않았거나, 자원이 모자라면 후보에서 빠집니다.</remarks>
    public bool CanInteract(GameObject interactor)
    {
        return IsBlueprint && !IsDepleted && CanPayBuildCost();
    }

    /// <inheritdoc />
    public string GetPrompt()
    {
        return HasBuildCost
            ? $"{m_buildPrompt} ({m_buildCostResourceId} {m_buildCostAmount})"
            : m_buildPrompt;
    }

    /// <inheritdoc />
    public void Interact(GameObject interactor)
    {
        if (IsBuilt || !TryPayBuildCost())
        {
            return;
        }

        Build();
    }

    /// <summary>
    /// 자원을 실제로 내지 않고 지금 낼 수 있는지만 확인합니다.
    /// </summary>
    /// <remarks>
    /// 확인과 차감을 나눈 이유는 UI가 버튼을 미리 비활성화할 수 있어야 하고, 실행 도중 모자라도
    /// 일부만 빠져나간 상태가 남지 않아야 하기 때문입니다. <c>IFacilityService</c>가 쓰는 방식과 같습니다.
    /// </remarks>
    private bool CanPayBuildCost()
    {
        if (!HasBuildCost)
        {
            return true;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        return storage != null && storage.CanSpendResource(BuildCost);
    }

    /// <summary>설치 비용을 차감합니다. 모자라면 아무것도 차감하지 않고 <c>false</c>를 돌려줍니다.</summary>
    private bool TryPayBuildCost()
    {
        if (!HasBuildCost)
        {
            return true;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null)
        {
            Debug.LogWarning(
                $"[Trap] '{name}': 자원 보관소를 찾지 못해 설치할 수 없습니다. " +
                "이 씬에 ShelterSceneDataManager가 있는지 확인하세요.",
                this);
            return false;
        }

        return storage.TrySpendResource(BuildCost);
    }

    /// <summary>청사진을 설치 완료 상태로 바꿉니다. 비용은 이미 치른 뒤에 부릅니다.</summary>
    public void Build()
    {
        if (IsBuilt)
        {
            return;
        }

        IsBuilt = true;
        ApplyBuildStateVisual();
        OnBuilt();
    }

    /// <summary>설치가 끝난 순간 파생 함정이 자기 효과를 켜도록 부릅니다.</summary>
    protected virtual void OnBuilt()
    {
    }

    /// <summary>
    /// 내구도를 깎습니다. 0 이하가 되면 <see cref="Deplete"/>로 이어집니다.
    /// </summary>
    /// <param name="amount">깎을 양입니다. 0 이하면 아무 일도 하지 않습니다.</param>
    /// <returns>이번 호출로 함정이 다 쓰였으면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 설치되지 않은 함정은 깎이지 않습니다. 청사진을 밟는다고 닳으면 안 됩니다.
    /// </remarks>
    public bool TakeDurabilityDamage(int amount)
    {
        if (amount <= 0 || !IsBuilt || IsDepleted)
        {
            return false;
        }

        m_currentHealth -= amount;
        if (m_currentHealth > 0)
        {
            return false;
        }

        m_currentHealth = 0;
        Deplete();
        return true;
    }

    /// <summary>
    /// 이 함정을 다 쓴 것으로 처리합니다. 청사진 상태로 돌아가고, 재설치 시점은 정책이 정합니다.
    /// </summary>
    /// <remarks>
    /// 내구도와 무관하게 한 번에 다 쓰는 함정(지뢰처럼 터지면 끝)도 이 메서드를 직접 부릅니다.
    /// <see cref="TrapRebuildPolicy.Always"/>면 이 자리에서 바로 복구되므로
    /// <see cref="IsDepleted"/>가 남지 않습니다.
    /// </remarks>
    public void Deplete()
    {
        if (IsDepleted)
        {
            return;
        }

        IsDepleted = true;
        m_currentHealth = 0;
        IsBuilt = false;
        ApplyBuildStateVisual();
        OnDepleted();

        if (m_rebuildPolicy == TrapRebuildPolicy.Always)
        {
            Rearm();
        }
    }

    /// <summary>다 쓴 함정을 다시 설치할 수 있는 상태로 되돌립니다. 내구도도 최대치로 회복됩니다.</summary>
    /// <remarks>설치까지 해 주지는 않습니다. 자원을 내고 짓는 것은 여전히 플레이어의 몫입니다.</remarks>
    public void Rearm()
    {
        if (!IsDepleted)
        {
            return;
        }

        IsDepleted = false;
        m_currentHealth = m_maxHealth;
        ApplyBuildStateVisual();
        OnRearmed();
    }

    /// <summary>다 쓴 순간 파생 함정이 자기 효과를 끄도록 부릅니다.</summary>
    /// <remarks>
    /// 이 시점에 이미 <see cref="IsBuilt"/>는 거짓입니다. 걸어 둔 지속 효과를 여기서 풀어야 합니다.
    /// 풀지 않으면 함정이 꺼졌는데 적이 계속 느려진 채로 남습니다.
    /// </remarks>
    protected virtual void OnDepleted()
    {
    }

    /// <summary>다시 설치할 수 있게 된 순간 파생 함정이 한 번만 쓰는 상태를 초기화하도록 부릅니다.</summary>
    protected virtual void OnRearmed()
    {
    }

    /// <summary>
    /// 현재 상태에 맞는 외형을 적용합니다.
    /// </summary>
    /// <remarks>
    /// 머테리얼을 통째로 갈아 끼우는 이유는, 불투명 머테리얼을 <c>MaterialPropertyBlock</c>으로
    /// 반투명하게 만들 수 없기 때문입니다. 블렌드 모드·ZWrite·렌더 큐는 머테리얼에 속한 값이라
    /// 프로퍼티만 바꿔서는 통하지 않습니다. 같은 메시를 그대로 쓰므로 별도 청사진 메시를 만들 필요는 없습니다.
    /// </remarks>
    private void ApplyBuildStateVisual()
    {
        if (m_blueprintMaterial != null && m_visualRenderers != null)
        {
            for (int i = 0; i < m_visualRenderers.Length; i++)
            {
                Renderer renderer = m_visualRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (IsBlueprint)
                {
                    // 슬롯 수를 원본과 맞춰야 서브메시가 빠지지 않습니다.
                    var swapped = new Material[m_originalMaterials[i].Length];
                    for (int slot = 0; slot < swapped.Length; slot++)
                    {
                        swapped[slot] = m_blueprintMaterial;
                    }

                    renderer.sharedMaterials = swapped;
                    if (m_disableShadowWhileBlueprint)
                    {
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
                else
                {
                    renderer.sharedMaterials = m_originalMaterials[i];
                    renderer.shadowCastingMode = m_originalShadowModes[i];
                }
            }
        }

        if (m_blueprintVisualRoot != null)
        {
            m_blueprintVisualRoot.SetActive(IsBlueprint);
        }

        if (m_builtVisualRoot != null)
        {
            m_builtVisualRoot.SetActive(IsBuilt);
        }
    }

    [Header("Debug")]
    [Tooltip("켜면 Scene View에 함정 범위를 그립니다. 대상이 범위 안에 있으면 색이 바뀝니다. 표시 전용이라 동작에는 영향이 없습니다.")]
    [SerializeField] private bool m_drawRangeGizmo = true;

    [Tooltip("범위 안에 아무도 없을 때의 색입니다.")]
    [ShowIf(nameof(m_drawRangeGizmo))]
    [SerializeField] private Color m_idleGizmoColor = new Color(0.2f, 0.9f, 1.0f, 0.5f);

    [Tooltip("범위 안에 대상이 있을 때의 색입니다.")]
    [SerializeField] private Color m_activeGizmoColor = new Color(1.0f, 0.15f, 0.1f, 0.8f);
    [EndIf]

    /// <summary>
    /// 지금 이 함정이 대상을 물고 있는지 여부입니다. 기즈모 색을 정하는 데 씁니다.
    /// </summary>
    /// <remarks>
    /// 무엇을 "밟은 상태"로 볼지는 함정마다 다르므로 파생이 답합니다. 기본은 항상 거짓입니다.
    /// </remarks>
    protected virtual bool HasTargetInRange => false;

    /// <summary>
    /// 파생 함정이 자기만의 범위를 덧그릴 자리입니다. 기본은 아무것도 그리지 않습니다.
    /// </summary>
    /// <remarks>
    /// <c>OnDrawGizmos</c>를 파생에서 다시 선언하면 기반과 파생 중 어느 쪽이 불릴지 보장되지 않습니다.
    /// 그래서 Unity가 부르는 진입점은 기반이 하나만 갖고, 파생은 이 훅을 재정의합니다.
    /// 감지 범위(콜라이더)와 효과 범위가 다른 함정이 있어서 필요합니다. 예를 들어 지뢰는 밟는 판정이
    /// 콜라이더이고 피해 범위는 그보다 훨씬 넓습니다.
    /// </remarks>
    protected virtual void OnDrawTrapGizmos()
    {
    }

    /// <summary>
    /// 함정 범위를 그립니다. 공통 콜라이더 범위를 먼저 그리고 파생 함정의 범위를 덧그립니다.
    /// </summary>
    /// <remarks>
    /// <c>OnDrawGizmosSelected</c>가 아니라 <c>OnDrawGizmos</c>인 이유는, 밟히는 순간을 보려면 선택하지 않은
    /// 상태에서도 보여야 하기 때문입니다. 함정은 여러 개를 깔아 두고 한꺼번에 지켜보게 됩니다.
    /// </remarks>
    private void OnDrawGizmos()
    {
        if (m_drawRangeGizmo)
        {
            DrawColliderRangeGizmo();
        }

        OnDrawTrapGizmos();
    }

    /// <summary>
    /// 함정이 가진 콜라이더 모양 그대로 범위를 그립니다.
    /// </summary>
    /// <remarks>
    /// <c>bounds</c>(월드 AABB) 대신 콜라이더의 <c>center</c>/<c>size</c>와 트랜스폼 행렬을 쓰는 이유는
    /// 회전 때문입니다. AABB로 그리면 비스듬히 놓인 함정의 실제 범위와 그림이 어긋납니다.
    /// </remarks>
    private void DrawColliderRangeGizmo()
    {
        bool active = HasTargetInRange;
        Color color = active ? m_activeGizmoColor : m_idleGizmoColor;
        Matrix4x4 previous = Gizmos.matrix;

        foreach (Collider collider in GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            Gizmos.color = color;

            if (collider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);

                // 밟힌 순간은 선만으로는 눈에 잘 안 띄어서 안쪽도 옅게 채웁니다.
                if (active)
                {
                    Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.25f);
                    Gizmos.DrawCube(box.center, box.size);
                }
            }
            else if (collider is SphereCollider sphere)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else
            {
                // 그 밖의 모양은 정확히 그릴 수단이 없어 월드 AABB로 대신합니다.
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
            }
        }

        Gizmos.matrix = previous;
    }
}
