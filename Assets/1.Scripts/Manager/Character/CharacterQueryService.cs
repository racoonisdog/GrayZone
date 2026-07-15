using System;
using System.Collections.Generic;

/// <summary>
/// 캐릭터 조회/필터 전담. 데이터 원천(ShelterSceneDataManager)을 컨텍스트로부터 얻어 읽기 전용 조회를 제공한다.
/// 데이터를 변경하지 않는다(변경은 CharacterEditor 담당).
/// </summary>
public sealed class CharacterQueryService
{
    private static readonly ShelterMemberRuntimeData[] EmptyCharacters = Array.Empty<ShelterMemberRuntimeData>();

    private readonly ICharacterDataContext context;

    public CharacterQueryService(ICharacterDataContext context)
    {
        this.context = context;
    }

    public IReadOnlyList<ShelterMemberRuntimeData> Characters =>
        context.DataSource != null ? context.DataSource.Characters : EmptyCharacters;

    public int CharacterCount => context.DataSource != null ? context.DataSource.CharacterCount : 0;

    public bool TryGetCharacter(string runtimeId, out ShelterMemberRuntimeData character)
    {
        character = null;
        return context.DataSource != null && context.DataSource.TryGetCharacter(runtimeId, out character);
    }

    public void FillAllCharacters(List<ShelterMemberRuntimeData> results)
    {
        FillCharacters(results, CharacterAssignmentFilter.Any);
    }

    public void FillCharactersByType(NPCType type, List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        foreach (ShelterMemberRuntimeData character in Characters)
        {
            if (character != null && character.Type == type)
                results.Add(character);
        }
    }

    public void FillFacilityAssignableCharacters(string facilityId, List<ShelterMemberRuntimeData> results, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
    {
        if (string.IsNullOrWhiteSpace(facilityId))
            throw new ArgumentException("Facility id is required.", nameof(facilityId));

        FillCharacters(results, filter);
    }

    public bool CanAssignToFacility(ShelterMemberRuntimeData character, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
    {
        return MatchesFilter(character, filter);
    }

    // Editor의 배치 판정에서도 재사용하므로 public.
    public bool MatchesFilter(ShelterMemberRuntimeData character, CharacterAssignmentFilter filter)
    {
        if (character == null)
            return false;

        bool alive = !character.IsDead;
        bool injured = character.InjuryState != PlayerInjuryState.Normal; // enum 기준: 건강 아니면 부상
        bool available = !character.IsAssignedToFacility;

        switch (filter)
        {
            case CharacterAssignmentFilter.Any:
                return true;
            case CharacterAssignmentFilter.AliveOnly:
                return alive;
            case CharacterAssignmentFilter.AvailableAlive:
                return alive && available;
            case CharacterAssignmentFilter.Injured:
                return injured;
            default:
                return false;
        }
    }

    private void FillCharacters(List<ShelterMemberRuntimeData> results, CharacterAssignmentFilter filter)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        foreach (ShelterMemberRuntimeData character in Characters)
        {
            if (MatchesFilter(character, filter))
                results.Add(character);
        }
    }
}
