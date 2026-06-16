using UnityEngine;

public class MedicalInteractionController : MonoBehaviour
{
    public enum MedicalInteractionEvent
    {
        AssignPatient,
        ReleasePatient,
        AssignStaff,
        ReleaseStaff
    }

    [Header("References")]
    [SerializeField] private MedicalManager medicalManager;

    [Header("Interaction")]
    [SerializeField] private MedicalInteractionEvent defaultEvent = MedicalInteractionEvent.AssignPatient;
    [SerializeField] private string targetNpcRuntimeId = string.Empty;
    [SerializeField] private bool useControlledNpcWhenTargetEmpty = true;

    public MedicalManager Manager => medicalManager;
    public string TargetNpcRuntimeId => targetNpcRuntimeId;

    private void Reset()
    {
        medicalManager = GetComponent<MedicalManager>();
    }

    private void Awake()
    {
        if (medicalManager == null)
        {
            medicalManager = GetComponent<MedicalManager>();
        }
    }

    public void SetTargetNpcRuntimeId(string runtimeId)
    {
        targetNpcRuntimeId = runtimeId ?? string.Empty;
    }

    public void InvokeDefaultInteraction()
    {
        TrySendDefault();
    }

    public bool TrySendDefault()
    {
        return TrySend(defaultEvent);
    }

    public bool TrySend(MedicalInteractionEvent interactionEvent)
    {
        return TryResolveTarget(out NPCRuntimeData target) && TrySend(interactionEvent, target);
    }

    public bool TrySend(MedicalInteractionEvent interactionEvent, string runtimeId)
    {
        return TryResolveTarget(runtimeId, out NPCRuntimeData target) && TrySend(interactionEvent, target);
    }

    public bool TrySend(MedicalInteractionEvent interactionEvent, NPCRuntimeData target)
    {
        if (medicalManager == null || target == null)
        {
            return false;
        }

        switch (interactionEvent)
        {
            case MedicalInteractionEvent.AssignPatient:
                return medicalManager.TryAssignPatient(target);
            case MedicalInteractionEvent.ReleasePatient:
                return medicalManager.TryReleasePatient(target);
            case MedicalInteractionEvent.AssignStaff:
                return medicalManager.TryAssignStaff(target);
            case MedicalInteractionEvent.ReleaseStaff:
                return medicalManager.TryReleaseStaff(target);
            default:
                return false;
        }
    }

    private bool TryResolveTarget(out NPCRuntimeData target)
    {
        string runtimeId = targetNpcRuntimeId;
        if (string.IsNullOrWhiteSpace(runtimeId) && useControlledNpcWhenTargetEmpty && GameDataManager.Instance != null)
        {
            runtimeId = GameDataManager.Instance.ControlledNpcRuntimeId;
        }

        return TryResolveTarget(runtimeId, out target);
    }

    private bool TryResolveTarget(string runtimeId, out NPCRuntimeData target)
    {
        target = null;

        if (GameDataManager.Instance == null || string.IsNullOrWhiteSpace(runtimeId))
        {
            return false;
        }

        return GameDataManager.Instance.TryGetNpc(runtimeId, out target);
    }
}
