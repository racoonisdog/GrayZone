using UnityEngine;

/// <summary>
/// Describes a reusable visual that is attached around the recipient of a status effect.
/// The runtime instance is owned and reused by <see cref="StatusEffectContainer"/>.
/// </summary>
[CreateAssetMenu(fileName = "StatusVisual_Name", menuName = "GrayZone/Status Effect/Body Visual")]
public sealed class StatusEffectVisualSO : ScriptableObject
{
    [Header("Prefab")]
    [Tooltip("Effect prefab shown around the recipient's body.")]
    [SerializeField] private GameObject m_prefab;

    [Header("Local Transform")]
    [Tooltip("Local offset from the recipient's status-effect anchor.")]
    [SerializeField] private Vector3 m_localPosition;
    [SerializeField] private Vector3 m_localEulerAngles;
    [Tooltip("Multiplier applied on top of the prefab's original local scale.")]
    [SerializeField] private Vector3 m_localScaleMultiplier = Vector3.one;

    [Header("Playback")]
    [Tooltip("Default duration used when this visual is played without a persistent status.")]
    [Min(0.01f)]
    [SerializeField] private float m_oneShotLifetime = 1.5f;
    [Tooltip("Restart particles when an existing status refreshes its duration.")]
    [SerializeField] private bool m_restartOnStatusRefresh;

    public GameObject Prefab => m_prefab;
    public Vector3 LocalPosition => m_localPosition;
    public Quaternion LocalRotation => Quaternion.Euler(m_localEulerAngles);
    public Vector3 LocalScaleMultiplier => m_localScaleMultiplier;
    public float OneShotLifetime => Mathf.Max(0.01f, m_oneShotLifetime);
    public bool RestartOnStatusRefresh => m_restartOnStatusRefresh;

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_localScaleMultiplier.x = Mathf.Max(0.0f, m_localScaleMultiplier.x);
        m_localScaleMultiplier.y = Mathf.Max(0.0f, m_localScaleMultiplier.y);
        m_localScaleMultiplier.z = Mathf.Max(0.0f, m_localScaleMultiplier.z);
        m_oneShotLifetime = Mathf.Max(0.01f, m_oneShotLifetime);
    }
#endif
}
