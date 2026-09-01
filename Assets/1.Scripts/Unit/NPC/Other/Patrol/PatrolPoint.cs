using UnityEngine;

public enum PatrolTurnDirection
{
    None,
    Left180,
    Right180
}

public sealed class PatrolPoint : MonoBehaviour
{
    [SerializeField] private PatrolTurnDirection m_turnDirection;

    public PatrolTurnDirection TurnDirection => m_turnDirection;
}
