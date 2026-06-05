using System.Collections.Generic;
using Unity.Profiling;

public struct Temp
{

}

public class StaffRoster
{
    private readonly List<Temp> _staff = new();
    public IReadOnlyList<Temp> Staff => _staff;

    public int Count => _staff.Count;
}
