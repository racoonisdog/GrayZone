using System;

/// <summary>
/// 시설에 배치 가능한 인원 수와 정원 확장 규칙을 관리
/// </summary>
public class StaffAssignment
{
    /// <summary>현재 허용되는 최대 배치 인원</summary>
    public int MaxPeople { get; private set; }

    /// <summary>현재 배치 인원 현재 구현에서는 외부 집계 값이 주로 사용</summary>
    public int CurrentPeople { get; private set; }

    /// <summary>시설 파손 비율 현재 구현에서는 예약된 상태값</summary>
    public int BrokenPercentage { get; private set; }

    /// <summary>
    /// 초기 최대 인원을 설정
    /// </summary>
    /// <param name="maxPeople">초기 최대 인원 음수는 0으로 보정</param>
    public StaffAssignment(int maxPeople)
    {
        MaxPeople = Math.Max(0, maxPeople);
    }

    /// <summary>
    /// 현재 인원에 추가 인원을 배치해도 정원을 넘지 않는지 확인
    /// </summary>
    /// <param name="currentCount">현재 배치된 인원 수</param>
    /// <param name="amount">추가하려는 인원 수</param>
    /// <returns>배치 가능하면 <c>true</c></returns>
    public bool CanAssign(int currentCount, int amount)
    {
        return amount > 0 && currentCount + amount <= MaxPeople;
    }

    /// <summary>
    /// 업그레이드 등으로 최대 배치 인원을 증가.
    /// </summary>
    /// <param name="amount">증가시킬 정원 수</param>
    /// <returns>정원이 증가했으면 <c>true</c></returns>
    public bool TryUpgradeCapacity(int amount)
    {
        if (amount <= 0)
            return false;

        MaxPeople += amount;
        return true;
    }
}
