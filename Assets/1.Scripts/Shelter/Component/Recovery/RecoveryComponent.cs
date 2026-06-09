using UnityEngine;

public class RecoveryComponent
{
    public bool Heal(NPCRuntimeData target, int amount)
    {
        return target.RecoverHp(amount);
    }

    public bool Revive(NPCRuntimeData target, int hpPercent)
    {
        return target.ReviveToPercent(hpPercent);
    }
}
