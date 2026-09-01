using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 플레이어블 캐릭터 한 명의 정체성과 출격 시작 상태를 보관하는 정의 데이터입니다.
/// </summary>
/// <remarks>
/// 여기 값은 <b>첫 스냅샷을 만들 때만</b> 쓰입니다. 이후 진행 중의 실제 상태는
/// <c>GameDataManager</c>가 소유하므로, 이 에셋을 다시 읽으면 진행 상황이 덮입니다.
/// <para>
/// 게터가 값을 보정해 돌려주는 이유는 Inspector에 잘못 적힌 값이 그대로 스냅샷으로 흘러가지 않게 하는 것입니다.
/// 이름 계열은 비어 있으면 에셋 이름으로 대체해, 배선 누락이 빈 문자열로 조용히 넘어가지 않게 합니다.
/// </para>
/// </remarks>
[CreateAssetMenu(fileName = "PlayerbleCharacterDefinition", menuName = "Scriptable Objects/PlayerbleCharacterDefinition")]
public class PlayerbleCharacterDefinition : ScriptableObject
{
    [Header("Character Identity")]
    [Tooltip("저장과 조회에 사용하는 고정 정의 ID입니다. 비워 두면 에셋 이름을 씁니다.")]
    [FormerlySerializedAs("<DefinitionId>k__BackingField")]
    [SerializeField] private string definitionId = string.Empty;

    [Tooltip("같은 정의로 여러 개체를 만들 때 개체를 구분하는 런타임 ID입니다. 비워 두면 정의 ID를 씁니다.")]
    [SerializeField] private string runtimeId = string.Empty;

    [Tooltip("코드에서 캐릭터를 가리키는 열거값입니다.")]
    [SerializeField] private PlayerbleCharacterId characterId = PlayerbleCharacterId.Unknown;

    [Tooltip("UI에 표시할 이름입니다. 비워 두면 에셋 이름을 씁니다.")]
    [SerializeField] private string displayName = string.Empty;

    [Tooltip("캐릭터 분류입니다. 셸터 배치와 출격 가능 여부 판단에 씁니다.")]
    [FormerlySerializedAs("Type")]
    [SerializeField] private NPCType npcType;

    [Header("Initial Character State")]
    [Tooltip("초기 신뢰도입니다. 0~100 범위로 유지됩니다.")]
    [FormerlySerializedAs("likeability")]
    [Range(0, 100)]
    [SerializeField] private int reliability;

    [Tooltip("초기 최대 HP입니다. 최소 1입니다.")]
    [FormerlySerializedAs("maxHP")]
    [Min(1)]
    [SerializeField] private int maxHp = 1;

    [FormerlySerializedAs("InjuryGauge")]
    [Tooltip("0이 완치이고 최대값이 가장 심한 부상인 초기 부상 심각도 게이지입니다.")]
    [SerializeField] private float injuryGauge;

    [Tooltip("부상 심각도 게이지의 최대값입니다. 최소 1입니다.")]
    [FormerlySerializedAs("maxInjugryGauge")]
    [SerializeField] private float maxInjuryGauge = 100f;

    [Tooltip("출격 시작 시 들고 있을 무기 구성입니다.")]
    [SerializeField] private WeaponSnapshotData weaponSnapshot = new();

    /// <summary>저장과 조회에 사용하는 고정 정의 ID입니다. 비어 있으면 에셋 이름을 씁니다.</summary>
    public string DefinitionId => string.IsNullOrWhiteSpace(definitionId) ? name : definitionId.Trim();

    /// <summary>개체를 구분하는 런타임 ID입니다. 비어 있으면 <see cref="DefinitionId"/>를 씁니다.</summary>
    public string RuntimeId => string.IsNullOrWhiteSpace(runtimeId) ? DefinitionId : runtimeId.Trim();

    /// <summary>코드에서 캐릭터를 가리키는 열거값입니다.</summary>
    public PlayerbleCharacterId CharacterId => characterId;

    /// <summary>UI에 표시할 이름입니다. 비어 있으면 에셋 이름을 씁니다.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();

    /// <summary>캐릭터 분류입니다.</summary>
    public NPCType NpcType => npcType;

    /// <summary>초기 신뢰도입니다. 0~100으로 보정해 돌려줍니다.</summary>
    public int Reliability => Mathf.Clamp(reliability, 0, 100);

    /// <summary>초기 최대 HP입니다. 최소 1로 보정해 돌려줍니다.</summary>
    public int MaxHp => Mathf.Max(1, maxHp);

    /// <summary>초기 부상 게이지입니다. 0과 최대값 사이로 보정해 돌려줍니다.</summary>
    public float InjuryGauge => Mathf.Clamp(injuryGauge, 0f, MaxInjuryGauge);

    /// <summary>부상 게이지 최대값입니다. 최소 1로 보정해 돌려줍니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1f, maxInjuryGauge);

    /// <summary>이 정의로 출격 시작 시점의 캐릭터 스냅샷을 만듭니다.</summary>
    /// <returns>Field와 Shelter가 공통으로 복사해 쓰는 캐릭터 스냅샷입니다.</returns>
    /// <remarks>부상 단계는 게이지에서 <see cref="CharacterInjuryStateRule"/>로 환산합니다.</remarks>
    public CharacterSnapshotData CreateSnapshot()
    {
        return new CharacterSnapshotData(
            DefinitionId,
            RuntimeId,
            CharacterId,
            NpcType,
            DisplayName,
            Reliability,
            MaxHp,
            MaxHp,
            InjuryGauge,
            MaxInjuryGauge,
            CharacterInjuryStateRule.FromGauge(InjuryGauge, MaxInjuryGauge),
            false,
            false,
            false,
            0,
            weaponSnapshot);
    }

    private void OnValidate()
    {
        definitionId = definitionId?.Trim() ?? string.Empty;
        runtimeId = runtimeId?.Trim() ?? string.Empty;
        displayName = displayName?.Trim() ?? string.Empty;
        reliability = Mathf.Clamp(reliability, 0, 100);
        maxHp = Mathf.Max(1, maxHp);
        maxInjuryGauge = Mathf.Max(1f, maxInjuryGauge);
        injuryGauge = Mathf.Clamp(injuryGauge, 0f, maxInjuryGauge);
        weaponSnapshot ??= new WeaponSnapshotData();
    }
}
