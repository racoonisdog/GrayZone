using UnityEngine;

/// <summary>
/// 셸터에서 NPC 회복 명령을 실행하는 얇은 회복 컴포넌트
/// </summary>
public class RecoveryComponent
{
    /// <summary>
    /// 대상 NPC의 HP를 지정 수치만큼 회복
    /// </summary>
    /// <param name="target">회복 대상 NPC 런타임 데이터</param>
    /// <param name="amount">회복할 HP 수치</param>
    /// <returns>회복 처리가 성공하면 <c>true</c></returns>
    public bool Heal(ShelterMemberRuntimeData target, int amount)
    {
        return target.RecoverHp(amount);
    }

    /// <summary>
    /// 쓰러진 NPC를 최대 HP 비율 기준으로 부활
    /// </summary>
    /// <param name="target">부활 대상 NPC 런타임 데이터</param>
    /// <param name="hpPercent">부활 후 적용할 HP 비율</param>
    /// <returns>부활 처리가 성공하면 <c>true</c></returns>
    public bool Revive(ShelterMemberRuntimeData target, int hpPercent)
    {
        return target.ReviveToPercent(hpPercent);
    }
}
