/// <summary>
/// 캐릭터 변경(커맨드) 전담. 정책(허용 판정)과 조회(대상/필터)를 소비해 실제 변경을 실행하고,
/// 변경 발생 시 컨텍스트를 통해 통지(이벤트 발행)를 위임한다.
/// </summary>
public sealed class CharacterEditor
{
    private readonly ICharacterDataContext context;
    private readonly CharacterEditPolicy policy;
    private readonly CharacterQueryService query;

    public CharacterEditor(ICharacterDataContext context, CharacterEditPolicy policy, CharacterQueryService query)
    {
        this.context = context;
        this.policy = policy;
        this.query = query;
    }

    public bool TryRecruitCharacter(PlayableCharacterDefinition characterDefinition, out ShelterMemberRuntimeData character, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;
        character = null;

        if (!policy.CanUse(CharacterEditCapability.RosterChange, out failure))
            return false;

        if (characterDefinition == null)
        {
            failure = CharacterActionFailure.InvalidCharacterDefinition;
            return false;
        }

        ShelterMemberRuntimeData created = new ShelterMemberRuntimeData(characterDefinition);
        if (!context.TryAddCharacter(created))
        {
            failure = CharacterActionFailure.CharacterAddFailed;
            return false;
        }

        character = created;
        context.NotifyCharactersChanged();
        return true;
    }

    public bool TryRemoveCharacter(string runtimeId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.RosterChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out _)
            || !context.TryRemoveCharacter(runtimeId))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        context.NotifyCharactersChanged();
        return true;
    }

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, FacilityAssignmentKind kind, out CharacterActionFailure failure)
    {
        return TryAssignToFacility(runtimeId, facilityId, roomId, CharacterAssignmentFilter.AvailableAlive, kind, out failure);
    }

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out CharacterActionFailure failure)
    {
        return TryAssignToFacility(runtimeId, facilityId, roomId, filter, kind, out _, out failure);
    }

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out ShelterMemberRuntimeData character, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;
        character = null;

        if (!policy.CanUse(CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (string.IsNullOrWhiteSpace(facilityId))
        {
            failure = CharacterActionFailure.InvalidFacilityId;
            return false;
        }

        if (!query.TryGetCharacter(runtimeId, out character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (!query.MatchesFilter(character, filter))
        {
            failure = character.IsAssignedToFacility
                ? CharacterActionFailure.AlreadyAssigned
                : CharacterActionFailure.CharacterNotEligible;
            return false;
        }

        string normalizedFacilityId = facilityId.Trim();
        string normalizedRoomId = string.IsNullOrWhiteSpace(roomId) ? normalizedFacilityId : roomId.Trim();
        if (!character.AssignToFacility(normalizedFacilityId, normalizedRoomId, kind))
        {
            failure = CharacterActionFailure.InvalidFacilityId;
            return false;
        }

        context.NotifyCharacterChanged(character);
        return true;
    }

    public bool TryReleaseFromFacility(string runtimeId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (!character.IsAssignedToFacility)
        {
            failure = CharacterActionFailure.NotAssignedToFacility;
            return false;
        }

        character.ReleaseFromFacility();
        context.NotifyCharacterChanged(character);
        return true;
    }

    public bool TrySetCurrentHp(string runtimeId, int currentHp, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.SetCurrentHp(currentHp);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TrySetInjuryGauge(string runtimeId, float injuryGauge, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.SetInjuryGauge(injuryGauge);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryApplyDamage(string runtimeId, int damage, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (damage <= 0)
        {
            failure = CharacterActionFailure.InvalidHealthChange;
            return false;
        }

        bool changed = character.ApplyDamage(damage);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryRecoverHp(string runtimeId, int amount, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (amount <= 0)
        {
            failure = CharacterActionFailure.InvalidHealthChange;
            return false;
        }

        bool changed = character.RecoverHp(amount);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryReviveToPercent(string runtimeId, int percent, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (percent <= 0)
        {
            failure = CharacterActionFailure.InvalidHealthChange;
            return false;
        }

        bool changed = character.ReviveToPercent(percent);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryCompleteRecovery(string runtimeId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange | CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.CompleteRecovery();
        if (character.IsAssignedToFacility)
        {
            character.ReleaseFromFacility();
            changed = true;
        }

        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryChangeWeapon(string runtimeId, Weapon weapon, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.EquipmentChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (weapon == null)
        {
            failure = CharacterActionFailure.InvalidEquipment;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    public bool TryChangeWeaponPart(string runtimeId, WeaponPart part, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.EquipmentChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (part == null)
        {
            failure = CharacterActionFailure.InvalidEquipment;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    public bool TryUpgradeEquipment(string runtimeId, string equipmentId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.EquipmentUpgrade, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (string.IsNullOrWhiteSpace(equipmentId))
        {
            failure = CharacterActionFailure.InvalidEquipment;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    public bool TryChangeSkill(string runtimeId, string skillId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.SkillChange, out failure))
            return false;

        if (!query.TryGetCharacter(runtimeId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (string.IsNullOrWhiteSpace(skillId))
        {
            failure = CharacterActionFailure.InvalidSkill;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    private void NotifyChangedIfNeeded(ShelterMemberRuntimeData character, bool changed)
    {
        if (changed)
            context.NotifyCharacterChanged(character);
    }
}
