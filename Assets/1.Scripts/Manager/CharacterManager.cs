using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 접근의 진입점(파사드). Unity 수명주기·직렬화 필드·이벤트·싱글턴만 소유하고,
/// 실제 로직은 순수 클래스(CharacterEditPolicy / CharacterQueryService / CharacterEditor)에 위임한다.
/// 외부 public API 시그니처는 리팩터 이전과 동일하게 유지한다.
/// </summary>
public class CharacterManager : MonoBehaviour, ICharacterDataContext
{
    [SerializeField] private ShelterSceneDataManager dataSource;
    [SerializeField] private bool readOnlyMode;
    [SerializeField] private CharacterEditCapability enabledCapabilities = CharacterEditCapability.All;

    public static CharacterManager Instance { get; private set; }

    public event Action<ShelterMemberRuntimeData> CharacterChanged;
    public event Action CharactersChanged;

    private CharacterEditPolicy policy;
    private CharacterQueryService query;
    private CharacterEditor editor;

    private CharacterEditPolicy Policy => policy ??= new CharacterEditPolicy(this);
    private CharacterQueryService Query => query ??= new CharacterQueryService(this);
    private CharacterEditor Editor => editor ??= new CharacterEditor(this, Policy, Query);

    public IReadOnlyList<ShelterMemberRuntimeData> Characters => Query.Characters;
    public int CharacterCount => Query.CharacterCount;
    public bool IsReadOnly => readOnlyMode;
    public CharacterEditCapability EnabledCapabilities => enabledCapabilities;

    private ShelterSceneDataManager DataSource
    {
        get
        {
            if (dataSource == null)
                dataSource = ShelterSceneDataManager.Instance;

            return dataSource;
        }
    }

    // ── ICharacterDataContext (순수 클래스에 상태를 제공하는 통로) ─────────────
    ShelterSceneDataManager ICharacterDataContext.DataSource => DataSource;
    bool ICharacterDataContext.IsReadOnly => readOnlyMode;
    CharacterEditCapability ICharacterDataContext.EnabledCapabilities => enabledCapabilities;
    bool ICharacterDataContext.TryAddCharacter(ShelterMemberRuntimeData character)
        => DataSource != null && DataSource.TryAddCharacter(character);
    bool ICharacterDataContext.TryRemoveCharacter(string runtimeId)
        => DataSource != null && DataSource.TryRemoveCharacter(runtimeId);
    void ICharacterDataContext.NotifyCharacterChanged(ShelterMemberRuntimeData character) => NotifyCharacterChanged(character);
    void ICharacterDataContext.NotifyCharactersChanged() => NotifyCharactersChanged();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        enabledCapabilities = CharacterEditPolicy.NormalizeCapabilities(enabledCapabilities);

        if (dataSource == null)
            dataSource = ShelterSceneDataManager.Instance;
    }

    private void OnValidate()
    {
        enabledCapabilities = CharacterEditPolicy.NormalizeCapabilities(enabledCapabilities);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetDataSource(ShelterSceneDataManager source)
    {
        dataSource = source;
        CharactersChanged?.Invoke();
    }

    public void SetReadOnlyMode(bool value)
    {
        readOnlyMode = value;
    }

    public void SetEnabledCapabilities(CharacterEditCapability capabilities)
    {
        enabledCapabilities = capabilities;
    }

    // ── 조회 (CharacterQueryService 위임) ────────────────────────────────────
    public bool TryGetCharacter(string runtimeId, out ShelterMemberRuntimeData character)
        => Query.TryGetCharacter(runtimeId, out character);

    public void FillAllCharacters(List<ShelterMemberRuntimeData> results)
        => Query.FillAllCharacters(results);

    public void FillCharactersByType(NPCType type, List<ShelterMemberRuntimeData> results)
        => Query.FillCharactersByType(type, results);

    public void FillFacilityAssignableCharacters(string facilityId, List<ShelterMemberRuntimeData> results, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
        => Query.FillFacilityAssignableCharacters(facilityId, results, filter);

    public bool CanAssignToFacility(ShelterMemberRuntimeData character, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
        => Query.CanAssignToFacility(character, filter);

    // ── 변경 (CharacterEditor 위임) ──────────────────────────────────────────
    public bool TryRecruitCharacter(PlayableCharacterDefinition characterDefinition, out ShelterMemberRuntimeData character, out CharacterActionFailure failure)
        => Editor.TryRecruitCharacter(characterDefinition, out character, out failure);

    public bool TryRemoveCharacter(string runtimeId, out CharacterActionFailure failure)
        => Editor.TryRemoveCharacter(runtimeId, out failure);

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, FacilityAssignmentKind kind, out CharacterActionFailure failure)
        => Editor.TryAssignToFacility(runtimeId, facilityId, roomId, kind, out failure);

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out CharacterActionFailure failure)
        => Editor.TryAssignToFacility(runtimeId, facilityId, roomId, filter, kind, out failure);

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out ShelterMemberRuntimeData character, out CharacterActionFailure failure)
        => Editor.TryAssignToFacility(runtimeId, facilityId, roomId, filter, kind, out character, out failure);

    public bool TryReleaseFromFacility(string runtimeId, out CharacterActionFailure failure)
        => Editor.TryReleaseFromFacility(runtimeId, out failure);

    public bool TrySetCurrentHp(string runtimeId, int currentHp, out CharacterActionFailure failure)
        => Editor.TrySetCurrentHp(runtimeId, currentHp, out failure);

    public bool TrySetInjuryGauge(string runtimeId, float injuryGauge, out CharacterActionFailure failure)
        => Editor.TrySetInjuryGauge(runtimeId, injuryGauge, out failure);

    public bool TryApplyDamage(string runtimeId, int damage, out CharacterActionFailure failure)
        => Editor.TryApplyDamage(runtimeId, damage, out failure);

    public bool TryRecoverHp(string runtimeId, int amount, out CharacterActionFailure failure)
        => Editor.TryRecoverHp(runtimeId, amount, out failure);

    public bool TryReviveToPercent(string runtimeId, int percent, out CharacterActionFailure failure)
        => Editor.TryReviveToPercent(runtimeId, percent, out failure);

    public bool TryCompleteRecovery(string runtimeId, out CharacterActionFailure failure)
        => Editor.TryCompleteRecovery(runtimeId, out failure);

    public bool TryChangeWeapon(string runtimeId, Weapon weapon, out CharacterActionFailure failure)
        => Editor.TryChangeWeapon(runtimeId, weapon, out failure);

    public bool TryChangeWeaponPart(string runtimeId, WeaponPart part, out CharacterActionFailure failure)
        => Editor.TryChangeWeaponPart(runtimeId, part, out failure);

    public bool TryUpgradeEquipment(string runtimeId, string equipmentId, out CharacterActionFailure failure)
        => Editor.TryUpgradeEquipment(runtimeId, equipmentId, out failure);

    public bool TryChangeSkill(string runtimeId, string skillId, out CharacterActionFailure failure)
        => Editor.TryChangeSkill(runtimeId, skillId, out failure);

    private void NotifyCharacterChanged(ShelterMemberRuntimeData character)
    {
        DataSource?.MarkDirty();
        CharacterChanged?.Invoke(character);
        CharactersChanged?.Invoke();
    }

    private void NotifyCharactersChanged()
    {
        DataSource?.MarkDirty();
        CharactersChanged?.Invoke();
    }
}
