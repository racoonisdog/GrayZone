using UnityEngine;

/// <summary>
/// Marks a collider as a specific hit area for hitscan feedback.
/// Damage values and headshot multipliers are owned by the weapon.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    [Tooltip("Whether hits on this collider count as headshots.")]
    [SerializeField] private bool m_isHeadshot = false;

    /// <summary>Whether hits on this collider should be treated as headshots.</summary>
    public bool IsHeadshot => m_isHeadshot;
}
