using UnityEngine;

public class FacilityInteractionPoint : MonoBehaviour
{
    [Header("Facility")]
    [SerializeField] private FacilityInteractionType interactionType = FacilityInteractionType.None;
    [SerializeField] private GameObject facilityRoot;

    public FacilityInteractionType InteractionType => interactionType;
    public GameObject FacilityRoot => facilityRoot != null ? facilityRoot : gameObject;

    private void Reset()
    {
        facilityRoot = gameObject;
    }

    public bool TryGetFacility<T>(out T facility) where T : Component
    {
        facility = null;

        GameObject root = FacilityRoot;
        if (root != null)
            facility = root.GetComponentInParent<T>();

        if (facility == null)
            facility = GetComponentInParent<T>();

        return facility != null;
    }
}
