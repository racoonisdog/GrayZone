using System.Collections.Generic;
using Unity.Profiling;

/// <summary>
/// 스태프 명단 항목의 임시 자리표시자 타입
/// </summary>
public struct Temp
{

}

/// <summary>
/// 시설 스태프 목록을 보관하는 임시 로스터
/// </summary>
public class StaffRoster
{
    private readonly List<Temp> _staff = new();

    /// <summary>현재 등록된 스태프 항목 목록</summary>
    public IReadOnlyList<Temp> Staff => _staff;

    /// <summary>현재 등록된 스태프 수</summary>
    public int Count => _staff.Count;
}
