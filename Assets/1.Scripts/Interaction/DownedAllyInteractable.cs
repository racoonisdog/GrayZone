using UnityEngine;
using VInspector;

/// <summary>
/// Exposes a downed squad member as a revive target for InteractionController.
/// </summary>
[DisallowMultipleComponent]
public class DownedAllyInteractable : MonoBehaviour, IInteractable, IHoldInteractable
{
    private const string DetectionTriggerName = "ReviveInteractionTrigger";
    private const float DefaultReviveHoldDuration = 2.0f;
    private const float HoldCompleteThreshold = 0.999f;

    [Foldout("Revive Options")]
    [Tooltip("Prompt text shown by interaction UI.")]
    [SerializeField] private string m_prompt = "\uAD6C\uC870";

    [Tooltip("Seconds the interaction key must be held to complete revive.")]
    [SerializeField] private float m_holdDuration = 2.0f;

    [Tooltip("If enabled, a member cannot revive itself.")]
    [SerializeField] private bool m_requireDifferentInteractor = true;

    [Foldout("Detection")]
    [Tooltip("Always-enabled trigger used by InteractionController. This stays active even when AI control disables CharacterController.")]
    [SerializeField] private SphereCollider m_detectionTrigger;

    [Tooltip("Radius of the revive detection trigger.")]
    [SerializeField] private float m_detectionTriggerRadius = 0.75f;

    [Tooltip("Local center of the revive detection trigger.")]
    [SerializeField] private Vector3 m_detectionTriggerCenter = new Vector3(0.0f, 0.9f, 0.0f);

    [Foldout("References")]
    [Tooltip("State controller for this downed member. Auto-filled from the same GameObject when empty.")]
    [SerializeField] private SquadMemberController m_memberController;

    [Tooltip("Health component for this downed member. Auto-filled from the same GameObject when empty.")]
    [SerializeField] private PlayerHealth m_playerHealth;

    private GameObject m_activeInteractor;
    private SquadMemberController m_activeInteractorMember;
    private ThirdPersonController m_activeInteractorThirdPerson;
    private bool m_holdActive;
    private float m_holdProgress01;

    public float HoldDuration => m_holdDuration > 0.0f
        ? m_holdDuration
        : DefaultReviveHoldDuration;

    public bool IsReviveHoldActive => m_holdActive;

    public float ReviveHoldProgress01 => m_holdProgress01;

    public PlayerHealth TargetHealth => m_playerHealth;

    public SquadMemberController TargetMember => m_memberController;

    private void Reset()
    {
        AutoFindReferences();
        EnsureDetectionTrigger();
    }

    private void Awake()
    {
        AutoFindReferences();
        EnsureDetectionTrigger();
    }

    private void OnDisable()
    {
        EndHold(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (m_holdDuration <= 0.0f)
        {
            m_holdDuration = DefaultReviveHoldDuration;
        }
        m_detectionTriggerRadius = Mathf.Max(0.05f, m_detectionTriggerRadius);
        AutoFindReferences();
        ConfigureDetectionTrigger();
    }
#endif

    public bool CanInteract(GameObject interactor)
    {
        AutoFindReferences();

        if (m_memberController == null || m_playerHealth == null)
        {
            return false;
        }

        if (!m_memberController.IsAlive || !m_memberController.IsDown)
        {
            return false;
        }

        if (m_playerHealth.IsDead || !m_playerHealth.IsDowned || m_playerHealth.CurrentHP > 0)
        {
            return false;
        }

        SquadMemberController interactorMember = interactor != null
            ? interactor.GetComponentInParent<SquadMemberController>()
            : null;

        if (m_requireDifferentInteractor && interactorMember == m_memberController)
        {
            return false;
        }

        if (interactorMember != null && (!interactorMember.IsAlive || interactorMember.IsDown))
        {
            return false;
        }

        return true;
    }

    public string GetPrompt()
    {
        return m_prompt;
    }

    public void BeginHold(GameObject interactor)
    {
        if (!CanInteract(interactor))
        {
            return;
        }

        m_holdActive = true;
        m_holdProgress01 = 0.0f;
        m_activeInteractor = interactor;
        m_activeInteractorMember = interactor != null
            ? interactor.GetComponentInParent<SquadMemberController>()
            : null;
        m_activeInteractorThirdPerson = interactor != null
            ? interactor.GetComponentInParent<ThirdPersonController>()
            : null;

        m_playerHealth.SetDownTimerPaused(true);
        m_memberController.SetAssistedStandingAnimator(true);

        if (m_activeInteractorMember != null)
        {
            m_activeInteractorMember.SetInteractionLocked(true);
            m_activeInteractorMember.SetReviveInteractionAnimator(true);
        }

        // 구조 시작 시 구조자가 피구조자를 바라보도록 몸을 돌립니다.
        FaceInteractorToTarget(interactor);

        // 몸이 돌아가도 화면(카메라)은 E를 누르기 시작한 시점 그대로 고정합니다.
        LockCameraToHoldStart();
    }

    /// <summary>
    /// 구조 홀드 중 카메라가 <see cref="FaceInteractorToTarget"/>의 몸 회전에 끌려가지 않도록,
    /// 저장된 조준 yaw/pitch로 카메라 타겟의 절대 회전만 다시 눌러줍니다.
    /// </summary>
    private void LockCameraToHoldStart()
    {
        if (m_activeInteractorThirdPerson != null)
        {
            m_activeInteractorThirdPerson.ReapplyCameraRotation();
        }
    }

    /// <summary>
    /// 구조자(<paramref name="interactor"/>)의 몸을 이 다운된 아군 쪽으로(수평면) 향하게 회전시킵니다.
    /// </summary>
    private void FaceInteractorToTarget(GameObject interactor)
    {
        if (interactor == null)
        {
            return;
        }

        Vector3 toTarget = transform.position - interactor.transform.position;
        toTarget.y = 0.0f;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return;
        }

        interactor.transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
    }

    public void UpdateHold(GameObject interactor, float progress01)
    {
        if (!m_holdActive)
        {
            BeginHold(interactor);
        }

        if (!m_holdActive)
        {
            return;
        }

        if (!CanInteract(interactor))
        {
            CancelHold(interactor);
            return;
        }

        m_holdProgress01 = Mathf.Clamp01(progress01);
        m_playerHealth.SetDownTimerPaused(true);
        FaceInteractorToTarget(interactor);
        LockCameraToHoldStart();

        if (m_activeInteractorMember != null)
        {
            m_activeInteractorMember.RefreshInteractionLock();
        }
    }

    public void CancelHold(GameObject interactor)
    {
        EndHold(false);
    }

    public void CompleteHold(GameObject interactor)
    {
        EndHold(true);
    }

    public void Interact(GameObject interactor)
    {
        if (!HasCompletedRequiredHold())
        {
            return;
        }

        if (!CanInteract(interactor))
        {
            return;
        }

        if (!m_playerHealth.ReviveFromDown())
        {
            return;
        }

        m_memberController.SetAlive(true);
        m_memberController.SetDown(false);
    }

    private void AutoFindReferences()
    {
        if (m_memberController == null)
        {
            m_memberController = GetComponent<SquadMemberController>();
        }

        if (m_playerHealth == null)
        {
            m_playerHealth = GetComponent<PlayerHealth>();
        }
    }

    private bool HasCompletedRequiredHold()
    {
        return HoldDuration <= 0.0f
            || (m_holdActive && m_holdProgress01 >= HoldCompleteThreshold);
    }

    private void EnsureDetectionTrigger()
    {
        if (m_detectionTrigger == null)
        {
            m_detectionTrigger = FindExistingDetectionTrigger();
        }

        if (m_detectionTrigger == null)
        {
            GameObject triggerObject = new GameObject(DetectionTriggerName);
            triggerObject.transform.SetParent(transform, false);
            m_detectionTrigger = triggerObject.AddComponent<SphereCollider>();
        }

        ConfigureDetectionTrigger();
    }

    private SphereCollider FindExistingDetectionTrigger()
    {
        Transform triggerTransform = transform.Find(DetectionTriggerName);
        if (triggerTransform == null)
        {
            return null;
        }

        return triggerTransform.GetComponent<SphereCollider>();
    }

    private void ConfigureDetectionTrigger()
    {
        if (m_detectionTrigger == null)
        {
            return;
        }

        Transform triggerTransform = m_detectionTrigger.transform;
        if (triggerTransform != transform)
        {
            triggerTransform.SetParent(transform, false);
            triggerTransform.localPosition = Vector3.zero;
            triggerTransform.localRotation = Quaternion.identity;
            triggerTransform.localScale = Vector3.one;
        }

        m_detectionTrigger.gameObject.name = DetectionTriggerName;
        m_detectionTrigger.gameObject.layer = gameObject.layer;
        m_detectionTrigger.gameObject.SetActive(true);
        m_detectionTrigger.enabled = true;
        m_detectionTrigger.isTrigger = true;
        m_detectionTrigger.center = m_detectionTriggerCenter;
        m_detectionTrigger.radius = Mathf.Max(0.05f, m_detectionTriggerRadius);
    }

    private void EndHold(bool completed)
    {
        if (m_playerHealth != null)
        {
            m_playerHealth.SetDownTimerPaused(false);
        }

        // 부활 횟수 초과로 구조 시도가 되살리지 못하고 전투 이탈(사망)로 끝난 경우, 기립 처리를 하면
        // 방금 재생된 사망 애니메이션(DoDeath)을 로코모션으로 덮어써버립니다. 그 경우엔 건너뜁니다.
        bool revivedAlive = completed && m_playerHealth != null && !m_playerHealth.IsDead;

        if (m_memberController != null && revivedAlive)
        {
            m_memberController.CompleteAssistedStandingAnimator();
        }
        else if (m_memberController != null && !completed)
        {
            m_memberController.SetAssistedStandingAnimator(false);
        }

        if (m_activeInteractorMember != null)
        {
            m_activeInteractorMember.SetReviveInteractionAnimator(false);
            m_activeInteractorMember.SetInteractionLocked(false);
        }

        m_activeInteractor = null;
        m_activeInteractorMember = null;
        m_activeInteractorThirdPerson = null;
        m_holdActive = false;
        m_holdProgress01 = completed ? 1.0f : 0.0f;
    }
}
