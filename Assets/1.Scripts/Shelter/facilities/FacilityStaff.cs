using System.Collections.Generic;

/// <summary>
/// 한 시설에 배치된 NPC 스태프 목록과 정원 규칙을 관리
/// </summary>
public class FacilityStaff
{
    private readonly List<NPCRuntimeData> assigned = new List<NPCRuntimeData>();
    private readonly StaffAssignment slots;

    /// <summary>
    /// 최대 스태프 수를 지정해 시설 스태프 관리 객체를 생성
    /// </summary>
    /// <param name="maxStaff">초기 최대 스태프 수</param>
    public FacilityStaff(int maxStaff)
    {
        slots = new StaffAssignment(maxStaff);
    }

    /// <summary>현재 배치된 NPC 목록 외부에서는 읽기 전용으로만 사용</summary>
    public IReadOnlyList<NPCRuntimeData> Assigned => assigned;

    /// <summary>현재 배치된 스태프 수</summary>
    public int CurrentCount => assigned.Count;

    /// <summary>현재 허용되는 최대 스태프 수</summary>
    public int MaxCount => slots.MaxPeople;

    /// <summary>현재 시설에 배치 가능한 빈자리가 있는지 반환</summary>
    public bool HasSpace => slots.CanAssign(assigned.Count, 1);

    /// <summary>
    /// 지정한 NPC가 이미 배치되어 있는지 확인
    /// </summary>
    /// <param name="staff">검사할 NPC 런타임 데이터</param>
    /// <returns>이미 배치되어 있으면 <c>true</c></returns>
    public bool Contains(NPCRuntimeData staff)
    {
        return staff != null && assigned.Contains(staff);
    }

    /// <summary>
    /// 스태프를 배치
    /// </summary>
    /// <param name="staff">배치할 NPC 런타임 데이터</param>
    /// <returns>명단이 실제로 변경되어 새로 추가됐으면 <c>true</c></returns>
    public bool TryAssign(NPCRuntimeData staff)
    {
        if (staff == null) return false;
        if (assigned.Contains(staff)) return false;
        if (!slots.CanAssign(assigned.Count, 1)) return false;

        assigned.Add(staff);
        return true;
    }

    /// <summary>
    /// 스태프 배치를 해제
    /// </summary>
    /// <param name="staff">해제할 NPC 런타임 데이터</param>
    /// <returns>실제로 명단에서 제거됐으면 <c>true</c></returns>
    public bool TryRelease(NPCRuntimeData staff)
    {
        return staff != null && assigned.Remove(staff);
    }

    /// <summary>
    /// 시설 업그레이드 등으로 스태프 정원을 증가.
    /// </summary>
    /// <param name="amount">증가시킬 정원 수</param>
    /// <returns>정원이 증가했으면 <c>true</c></returns>
    public bool TryUpgradeCapacity(int amount)
    {
        return slots.TryUpgradeCapacity(amount);
    }
}
