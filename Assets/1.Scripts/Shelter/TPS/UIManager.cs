using System;
using UnityEngine;

//ToDo : UI종류가 많아질 경우 enum으로 분류해서 가지기
public enum ShelterUIType
{
    None,
    Medical
}

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
    private ShelterUIType m_activeUI = ShelterUIType.None;

    public GameObject CurrentInteractionTarget => m_currentInteractionTarget;
    public ShelterUIType ActiveUI => m_activeUI;
    public bool HasOpenBlockingUI => m_activeUI != ShelterUIType.None;

    public event Action<ShelterUIType> ActiveUIChanged;

    private void Awake()
    {
        // The manager object should stay active; only the assigned UI object is hidden.
        if (m_hideInteractionUIOnAwake)
            HideInteraction();
    }

    private void OnEnable()
    {
        if (m_medicalUI != null)
            m_medicalUI.Closed += HandleMedicalUIClosed;
    }

    private void OnDisable()
    {
        if (m_medicalUI != null)
            m_medicalUI.Closed -= HandleMedicalUIClosed;

        SetActiveUI(ShelterUIType.None);
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

        if (!interactionPoint.TryGetFacility(out MedicalManager medicalManager))
        {
            Debug.LogWarning("[UI] Medical Manager is not found.", interactionPoint);
            return;
        }

        SetInteractionUIActive(false);
        m_medicalUI.Open(medicalManager);
        SetActiveUI(ShelterUIType.Medical);

        if (m_logMessages)
            Debug.Log($"[UI] Open Medical UI : {interactionPoint.name}", interactionPoint);
    }

    //ToDo : 추상화를 이용 ( 인터페이스 만들어서 각 UI들이 상속, UIManger에서는 인터페이스를 기준으로 
    //Close Open 등을 배열로 관리하도록 )
    public void CloseMedicalUI()
    {
        if (m_medicalUI != null)
            m_medicalUI.Close();

        SetActiveUI(ShelterUIType.None);
    }

    private void HandleMedicalUIClosed()
    {
        if (m_activeUI == ShelterUIType.Medical)
            SetActiveUI(ShelterUIType.None);
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

    private void SetActiveUI(ShelterUIType activeUI)
    {
        if (m_activeUI == activeUI)
            return;

        m_activeUI = activeUI;
        ActiveUIChanged?.Invoke(m_activeUI);
    }
}
