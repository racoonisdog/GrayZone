using System;
using UnityEngine;

public class NPCRuntimeData
{
    public NPCChar NPCData { get; }
    public string RuntimeId { get; }
    public NPCInjuryState CurrentInjuryState { get; private set; }

    private bool IsAssignedToShelter { get; set; }
    private string AssignedRoomId { get; set; }
    private int CurrentHp { get; set; }

    public int HealthPercent => MaxHp <= 0 ? 0 : CurrentHp * 100 / MaxHp;
    public string DefinitionId => NPCData.DefinitionId;
    public int MaxHp => NPCData != null ? NPCData.maxHP : 1;
    public bool IsDead => CurrentHp <= 0;

    public NPCRuntimeData(NPCChar npcData, string runtimeId = null)
    {
        NPCData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        RuntimeId = string.IsNullOrWhiteSpace(runtimeId) ? npcData.DefinitionId : runtimeId;
        ResetToBaseState();
    }

    public void ResetToBaseState()
    {
        CurrentHp = MaxHp;
        CurrentInjuryState = NPCInjuryState.Healthy;
        ReleaseFromShelter();
    }

    public NPCInjuryState GetHealthState(NPCHealthRuleSO rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        return rule.Evaluate(CurrentHp, MaxHp);
    }

    public NPCInjuryState GetCurrentInjuryState() => CurrentInjuryState;
    public bool GetIsAssignedToShelter() => IsAssignedToShelter;
    public string GetAssignedRoomId() => AssignedRoomId;
    public int GetCurrentHp() => CurrentHp;

    public bool AssignToShelter(string shelterId, string roomId)
    {
        if (string.IsNullOrWhiteSpace(shelterId) || string.IsNullOrWhiteSpace(roomId))
            return false;

        IsAssignedToShelter = true;
        AssignedRoomId = roomId.Trim();
        return true;
    }

    public void ReleaseFromShelter()
    {
        IsAssignedToShelter = false;
        AssignedRoomId = string.Empty;
    }

    public void SetCurrentHp(int value)
    {
        CurrentHp = Mathf.Clamp(value, 0, MaxHp);
    }

    public void SetInjuryState(NPCInjuryState state)
    {
        CurrentInjuryState = state;
    }

    public void CompleteRecovery()
    {
        SetCurrentHp(MaxHp);
        SetInjuryState(NPCInjuryState.Healthy);
    }

    public bool ApplyDamage(int damage)
    {
        if (damage <= 0)
            return false;

        SetCurrentHp(CurrentHp - damage);
        return true;
    }

    public bool RecoverHp(int amount)
    {
        if (amount <= 0)
            return false;

        SetCurrentHp(CurrentHp + amount);
        return true;
    }

    public bool ReviveToPercent(int percent)
    {
        if (percent <= 0)
            return false;

        int healAmount = MaxHp * percent / 100;
        SetCurrentHp(CurrentHp + healAmount);
        return true;
    }
}
