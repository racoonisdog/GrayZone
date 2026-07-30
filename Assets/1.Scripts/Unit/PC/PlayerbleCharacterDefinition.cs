using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "PlayerbleCharacterDefinition", menuName = "Scriptable Objects/PlayerbleCharacterDefinition")]
public class PlayerbleCharacterDefinition : ScriptableObject
{
    [Header("Character Identity")]
    [FormerlySerializedAs("<DefinitionId>k__BackingField")]
    [SerializeField] private string definitionId = string.Empty;
    [SerializeField] private string runtimeId = string.Empty;
    [SerializeField] private PlayerbleCharacterId characterId = PlayerbleCharacterId.Unknown;
    [SerializeField] private string displayName = string.Empty;
    [FormerlySerializedAs("Type")]
    [SerializeField] private NPCType npcType;

    [Header("Initial Character State")]
    [FormerlySerializedAs("likeability")]
    [Range(0, 100)]
    [SerializeField] private int reliability;
    [FormerlySerializedAs("maxHP")]
    [Min(1)]
    [SerializeField] private int maxHp = 1;
    [FormerlySerializedAs("InjuryGauge")]
    [Tooltip("0이 완치이고 최대값이 가장 심한 부상인 초기 부상 심각도 게이지입니다.")]
    [SerializeField] private float injuryGauge;
    [FormerlySerializedAs("maxInjugryGauge")]
    [SerializeField] private float maxInjuryGauge = 100f;
    [SerializeField] private WeaponSnapshotData weaponSnapshot = new();

    public string DefinitionId => string.IsNullOrWhiteSpace(definitionId) ? name : definitionId.Trim();
    public string RuntimeId => string.IsNullOrWhiteSpace(runtimeId) ? DefinitionId : runtimeId.Trim();
    public PlayerbleCharacterId CharacterId => characterId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
    public NPCType NpcType => npcType;
    public int Reliability => Mathf.Clamp(reliability, 0, 100);
    public int MaxHp => Mathf.Max(1, maxHp);
    public float InjuryGauge => Mathf.Clamp(injuryGauge, 0f, MaxInjuryGauge);
    public float MaxInjuryGauge => Mathf.Max(1f, maxInjuryGauge);

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
