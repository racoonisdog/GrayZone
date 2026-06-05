using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WeaponPart", menuName = "Scriptable Objects/WeaponPart")]
public class WeaponPart : ScriptableObject
{
    public string partId;
    public PartType partType;
    public string partName;

    public List<StatModifier> modifiers = new List<StatModifier>();
}
