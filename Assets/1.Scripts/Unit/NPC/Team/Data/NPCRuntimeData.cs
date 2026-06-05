using System;
using UnityEngine;

//ToDo : NPCRegistry 싱글톤 고려
public class NPCRuntimeData
{
    //ToDo : 레벨시스템은 없지만 스태미나, 특수 능력치 향상 기능이 필요하면 추가
    public NPCChar NPCData { get; }

    public string RuntimeId { get; }
    //public NPCInjuryState CurrentInjuryState { get; private set; }
    private bool IsAssignedToShelter { get; set; }
    //private string AssignedShelterId { get; set; }
    private string AssignedRoomId { get; set; }
    private int CurrentHp { get; set; }

    public int HealthPercent => MaxHp <= 0 ? 0 : CurrentHp * 100 / MaxHp;

    public string DefinitionId => NPCData.DefinitionId;

    public int MaxHp => NPCData != null ? NPCData.maxHP : 1;
    public bool IsDead => CurrentHp <= 0;


    //생성자
    public NPCRuntimeData(NPCChar npcData, string runtimeId = null)
    {
        NPCData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        RuntimeId = string.IsNullOrWhiteSpace(runtimeId) ? npcData.DefinitionId : runtimeId;
        ResetToBaseState();
    }

    //현재상태 갱신
    public void ResetToBaseState()
    {
        CurrentHp = MaxHp;
        ReleaseFromShelter();
    }

    //캐릭터 자신이 부상을 들고있는게 아니라 외부에서 요청시 자신의 상태를 던져주는 형태
    public NPCInjuryState GetHealthState(NPCHealthRuleSO rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        return rule.Evaluate(CurrentHp, MaxHp);
    }

    //public NPCInjuryState GetCurrentInjuryState() => CurrentInjuryState;
    public bool GetIsAssignedToShelter() => IsAssignedToShelter;
    //public string GetAssignedShelterId() => AssignedShelterId;
    public string GetAssignedRoomId() => AssignedRoomId;
    public int GetCurrentHp() => CurrentHp;


    //ToDo : 셸터는 하나만 존재하는가? ( 하나만 존재하면 매개변수 하나 필요없음 )
    //룸에 들어가는 함수/ 이것도 필요한지 검토 필요
    public bool AssignToShelter(string shelterId, string roomId)
    {
        if (string.IsNullOrWhiteSpace(shelterId) || string.IsNullOrWhiteSpace(roomId))
        {
            return false;
        }

        IsAssignedToShelter = true;
        //AssignedShelterId = shelterId.Trim();
        AssignedRoomId = roomId.Trim();
        return true;
    }

    //룸에서 나올때 함수
    public void ReleaseFromShelter()
    {
        IsAssignedToShelter = false;
        //AssignedShelterId = string.Empty;
        AssignedRoomId = string.Empty;
    }

    //현재 HP 설정함수 ( HP 업그레이드 또는 아이템 효과로 인한 hp 상승시 )
    public void SetCurrentHp(int value)
    {
        CurrentHp = Mathf.Clamp(value, 0, MaxHp);
    }

    //현재 HP 설정함수 ( 전투, 배치된 셸터가 파손되었을때 용도 )
    public bool ApplyDamage(int damage)
    {
        if (damage <= 0)
        {
            return false;
        }

        SetCurrentHp(CurrentHp - damage);
        return true;
    }

    //회복 함수
    public bool RecoverHp(int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        SetCurrentHp(CurrentHp + amount);
        return true;
    }

    public bool ReviveToPercent(int Percent)
    {
        if(Percent <= 0)
        {
            return false;
        }

        int healamount = MaxHp * Percent / 100;

        SetCurrentHp(CurrentHp + healamount);
        return true;
    }

}
