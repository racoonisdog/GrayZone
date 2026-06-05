using UnityEngine;


// ToDo : Framework추가할때 SO가 아닌 내 자체 로직쪽으로 변경 필요
[CreateAssetMenu(fileName = "NPCHealthRuleSO", menuName = "Scriptable Objects/NPCHealthRuleSO")]
public class NPCHealthRuleSO : ScriptableObject
{
    [SerializeField, Range(0, 100)] private int nearDeathBelow = 30;
    [SerializeField, Range(0, 100)] private int heavyInjuryBelow = 70;
    [SerializeField, Range(0, 100)] private int lightInjuryBelow = 100;

    public NPCInjuryState Evaluate(int currentHp, int maxHp)
    {
        if (maxHp <= 0 || currentHp <= 0)
            return NPCInjuryState.Dead;

        int percent = currentHp * 100 / maxHp;

        if (percent < nearDeathBelow)
            return NPCInjuryState.NearDeath;

        if (percent < heavyInjuryBelow)
            return NPCInjuryState.HeavyInjury;

        if (percent < lightInjuryBelow)
            return NPCInjuryState.LightInjury;

        return NPCInjuryState.Healthy;
    }
}
