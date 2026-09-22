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
/// 설치형 함정의 공통 기반입니다.
/// </summary>
/// <remarks>
/// 모든 함정이 공통으로 갖는 것만 둡니다. 내구도와 피해량, 그리고 피해를 주는 방식입니다.
/// 무엇을 어떻게 감지하고 피해 외에 어떤 효과를 주는지는 파생 함정이 정합니다.
///
/// 내구도를 여기에 둔 이유는 함정이 부서지는 개념을 나중에 어느 함정에나 붙일 수 있게 하기 위해서입니다.
/// 지금은 값만 들고 있고 실제 파괴 처리는 없습니다. 필요해지면 이 클래스에 <c>IDamageable</c>을 구현해
/// 파생 전체가 한 번에 따라오게 하는 것이 자연스럽습니다.
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
    [Tooltip("이 함정의 내구도입니다. 0 이하가 되면 부서진 것으로 봅니다. 파괴 처리는 아직 없습니다.")]
    [Min(0)]
    [SerializeField] protected int m_health = 100;

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

    /// <summary>이 함정의 현재 내구도입니다.</summary>
    public int Health => m_health;

    /// <summary>한 번에 주는 피해량입니다.</summary>
    public int Damage => m_damage;

    /// <summary>이 함정이 피해를 주는 방식입니다.</summary>
    public TrapDamageMode DamageMode => m_damageMode;

    /// <summary>Tick 방식에서 피해 사이의 간격(초)입니다.</summary>
    public float DamageInterval => Mathf.Max(0.01f, m_damageTickInterval);

    /// <summary>Tick 방식에서 1초당 피해 횟수입니다. 이전 API 호환용으로 유지합니다.</summary>
    public float DamageTickRate => 1.0f / DamageInterval;

    /// <summary>이 함정이 아직 멀쩡한지 여부입니다.</summary>
    public bool IsAlive => m_health > 0;

    /// <summary>설치가 끝나 실제로 작동하는 상태인지 여부입니다.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>아직 자원을 내지 않은 청사진 상태인지 여부입니다.</summary>
    public bool IsBlueprint => !IsBuilt;

    /// <summary>설치 비용입니다. 수량이 0이면 비용이 없는 것으로 봅니다.</summary>
    public ResourceCost BuildCost => new ResourceCost(m_buildCostResourceId, m_buildCostAmount);

    /// <summary>이 함정에 비용이 걸려 있는지 여부입니다.</summary>
    private bool HasBuildCost => m_buildCostAmount > 0 && !string.IsNullOrWhiteSpace(m_buildCostResourceId);

    protected virtual void Awake()
    {
        CacheVisuals();
        IsBuilt = m_startPlaced;
        ApplyBuildStateVisual();
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
    /// <remarks>이미 설치됐거나 자원이 모자라면 후보에서 빠집니다.</remarks>
    public bool CanInteract(GameObject interactor)
    {
        return IsBlueprint && CanPayBuildCost();
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
    /// 함정이 가진 콜라이더 모양 그대로 범위를 그립니다.
    /// </summary>
    /// <remarks>
    /// <c>OnDrawGizmosSelected</c>가 아니라 <c>OnDrawGizmos</c>인 이유는, 밟히는 순간을 보려면 선택하지 않은
    /// 상태에서도 보여야 하기 때문입니다. 함정은 여러 개를 깔아 두고 한꺼번에 지켜보게 됩니다.
    ///
    /// <c>bounds</c>(월드 AABB) 대신 콜라이더의 <c>center</c>/<c>size</c>와 트랜스폼 행렬을 쓰는 이유는
    /// 회전 때문입니다. AABB로 그리면 비스듬히 놓인 함정의 실제 범위와 그림이 어긋납니다.
    /// </remarks>
    private void OnDrawGizmos()
    {
        if (!m_drawRangeGizmo)
        {
            return;
        }

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
