public enum NPCType
{
    Tanker,
    Healer,
    Dealer
}

// 시설 배치 역할(제네릭). ShelterMemberRuntimeData는 의무실 전용 개념을 몰라야 하므로 도메인 중립 이름을 쓴다.
public enum FacilityAssignmentKind
{
    None,
    Patient,
    Staff,
    Guest
}

