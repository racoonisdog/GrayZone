using System;
using UnityEngine;

/// <summary>
/// 셸터에서 현재 열려 있는 차단형 UI 종류
/// </summary>
public enum ShelterUIType
{
    /// <summary>열려 있는 차단형 UI가 없음</summary>
    None,

    /// <summary>의료 시설 UI가 열려 있음</summary>
    Medical
}

/// <summary>
/// 셸터 상호작용 프롬프트와 시설 UI 열기/닫기를 관리
/// </summary>
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

    /// <summary>현재 상호작용 프롬프트가 가리키는 대상 오브젝트</summary>
    public GameObject CurrentInteractionTarget => m_currentInteractionTarget;

    /// <summary>현재 열려 있는 차단형 UI 종류</summary>
    public ShelterUIType ActiveUI => m_activeUI;

    /// <summary>플레이어 조작을 막아야 하는 UI가 열려 있는지 여부</summary>
    public bool HasOpenBlockingUI => m_activeUI != ShelterUIType.None;

    /// <summary>활성 UI 종류가 변경될 때 발생</summary>
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

    /// <summary>
    /// 현재 상호작용 대상을 설정하고 프롬프트 표시 상태를 갱신
    /// </summary>
    /// <param name="target">새 상호작용 대상 없으면 <c>null</c></param>
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

    /// <summary>
    /// 지정한 대상에 대한 상호작용 프롬프트를 표시
    /// </summary>
    /// <param name="target">프롬프트가 가리킬 상호작용 대상</param>
    public void ShowInteraction(GameObject target)
    {
        // Later this can also update text/icon based on Door, Bed, Workbench, NPC, etc.
        m_currentInteractionTarget = target;
        SetInteractionUIActive(true);

        if (m_logMessages && target != null)
            Debug.Log($"[UI] Show Interaction : {target.name}", target);
    }

    /// <summary>
    /// 현재 상호작용 프롬프트를 숨기고 대상 정보를 비움.
    /// </summary>
    public void HideInteraction()
    {
        // Clear the current target and hide the already-created UI object.
        m_currentInteractionTarget = null;
        SetInteractionUIActive(false);


        // ToDo : 나중에 삭제
        if (m_logMessages)
            Debug.Log("[UI] Hide Interaction", this);
    }

    /// <summary>
    /// 현재 상호작용 대상의 시설 UI를 열기.
    /// </summary>
    /// <returns>열 수 있는 시설 UI를 찾고 열었으면 <c>true</c></returns>
    public bool TryOpenCurrentTargetUI()
    {
        return TryOpenTargetUI(m_currentInteractionTarget);
    }

    /// <summary>
    /// 지정한 대상에 연결된 시설 UI를 열기.
    /// </summary>
    /// <param name="target">시설 상호작용 대상</param>
    /// <returns>대상에 맞는 UI를 열었으면 <c>true</c></returns>
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

    /// <summary>
    /// 의료 시설 상호작용 지점에서 의료 UI를 열기.
    /// </summary>
    /// <param name="interactionPoint">의료 시설을 찾을 상호작용 지점</param>
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

    /// <summary>
    /// 의료 UI를 닫고 활성 UI 상태를 비움.
    /// </summary>
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
