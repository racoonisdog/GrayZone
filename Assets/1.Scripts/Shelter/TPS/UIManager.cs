using UnityEngine;

//ToDo : UI종류가 많아질 경우 enum으로 분류해서 가지기

public class UIManager : MonoBehaviour
{
    [Header("Interaction UI")]
    [SerializeField] private GameObject m_interactionUI;
    [SerializeField] private bool m_hideInteractionUIOnAwake = true;

    [Header("Medical UI")]
    [SerializeField] private MedicalUI m_medicalUI;

    [Header("Debug")]
    [SerializeField] private bool m_logMessages = true;

    private GameObject m_currentInteractionTarget;

    public GameObject CurrentInteractionTarget => m_currentInteractionTarget;

    private void Awake()
    {
        // The manager object should stay active; only the assigned UI object is hidden.
        if (m_hideInteractionUIOnAwake)
            HideInteraction();
    }

    public void SetInteractionTarget(GameObject target)
    {
        if (target != m_currentInteractionTarget)
            CloseMedicalUI();

        // PlayerInteractor calls this whenever its CurrentTarget changes.
        if (target != null)
        {
            ShowInteraction(target);
            return;
        }

        HideInteraction();
    }

    public void ShowInteraction(GameObject target)
    {
        // Later this can also update text/icon based on Door, Bed, Workbench, NPC, etc.
        m_currentInteractionTarget = target;
        SetInteractionUIActive(true);

        if (m_logMessages && target != null)
            Debug.Log($"[UI] Show Interaction : {target.name}", target);
    }

    public void HideInteraction()
    {
        // Clear the current target and hide the already-created UI object.
        m_currentInteractionTarget = null;
        SetInteractionUIActive(false);


        // ToDo : 나중에 삭제
        if (m_logMessages)
            Debug.Log("[UI] Hide Interaction", this);
    }

    public bool TryOpenCurrentTargetUI()
    {
        return TryOpenTargetUI(m_currentInteractionTarget);
    }

    public bool TryOpenTargetUI(GameObject target)
    {
        if (!TryGetFacilityInteractionPoint(target, out FacilityInteractionPoint interactionPoint))
            return false;

        switch (interactionPoint.InteractionType)
        {
            case FacilityInteractionType.Medical:
                OpenMedicalUI(interactionPoint);
                return true;
            default:
                return false;
        }
    }

    public void OpenMedicalUI(FacilityInteractionPoint interactionPoint)
    {
        if (m_medicalUI == null)
        {
            Debug.LogWarning("[UI] Medical UI is not assigned.", this);
            return;
        }

        if (interactionPoint == null)
            return;

        SetInteractionUIActive(false);
        m_medicalUI.Open(interactionPoint);

        if (m_logMessages)
            Debug.Log($"[UI] Open Medical UI : {interactionPoint.name}", interactionPoint);
    }

    public void CloseMedicalUI()
    {
        if (m_medicalUI != null)
            m_medicalUI.Close();
    }

    private void SetInteractionUIActive(bool active)
    {
        if (m_interactionUI == null)
        {
            Debug.LogWarning("[UI] Interaction UI is not assigned.", this);
            return;
        }

        if (m_interactionUI == gameObject && !active)
        {
            Debug.LogWarning("[UI] Do not assign the UIManager object itself. Assign a child UI panel instead.", this);
            return;
        }

        if (m_interactionUI.activeSelf == active)
            return;

        m_interactionUI.SetActive(active);
    }

    private bool TryGetFacilityInteractionPoint(GameObject target, out FacilityInteractionPoint interactionPoint)
    {
        interactionPoint = null;
        if (target == null)
            return false;

        if (target.TryGetComponent(out interactionPoint))
            return true;

        interactionPoint = target.GetComponentInParent<FacilityInteractionPoint>();
        if (interactionPoint != null)
            return true;

        interactionPoint = target.GetComponentInChildren<FacilityInteractionPoint>();
        return interactionPoint != null;
    }
}
