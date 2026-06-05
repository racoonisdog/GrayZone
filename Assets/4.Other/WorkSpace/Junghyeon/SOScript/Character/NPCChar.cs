using UnityEngine;

[CreateAssetMenu(fileName = "NPCChar", menuName = "Scriptable Objects/NPCChar")]
public class NPCChar : ScriptableObject
{
    [Min(1)] public int maxHP;
    public NPCType Type;
    public int likeability;
    public NPCInjuryState Health;

    [field: SerializeField] public string DefinitionId { get; private set; }
    [field: SerializeField] public string Prefab { get; private set; }

    public int MaxHP => Mathf.Max(1, maxHP);

    private void OnValidate()
    {
        maxHP = Mathf.Max(1, maxHP);
    }
}
