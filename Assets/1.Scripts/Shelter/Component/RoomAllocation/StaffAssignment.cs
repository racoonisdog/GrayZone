using System;

public class StaffAssignment
{
    public int MaxPeople { get; private set; }
    public int CurrentPeople { get; private set; }

    public int BrokenPercentage { get; private set; }

    //생성시 초기값 설정
    public StaffAssignment(int maxPeople)
    {
        MaxPeople = Math.Max(0, maxPeople);
    }

    //투입 인원이 제한을 넘는지 확인
    public bool CanAssign(int currentCount, int amount)
    {
        return amount > 0 && currentCount + amount <= MaxPeople;
    }

    //최대 인원 증가 함수 ( 업그레이드 용 )
    public bool TryUpgradeCapacity(int amount)
    {
        if (amount <= 0)
            return false;

        MaxPeople += amount;
        return true;
    }
}
