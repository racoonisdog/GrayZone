using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터 목표의 현재 단계와 단계 전이만 관리하는 씬 흐름 컨트롤러입니다.
/// 시설 UI, 방어전, 목표 표시의 세부 동작은 각 시스템에 위임합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShelterFlowController : MonoBehaviour
{
    public enum FlowState
    {
        GuideToFirstFacility,
        WaitingForDefenseCompletion,
        GuideToReturnFacility,
        Completed,
    }

    [Serializable]
    private sealed class ObjectiveTarget
    {
        [Tooltip("목표를 안내하는 화살표 컨트롤러")]
        [SerializeField] private ObjectiveIndicatorController m_indicator;

        [Tooltip("목표 시설에 속한 상호작용 지점들.")]
        [SerializeField] private List<FacilityUIInteractable> m_interactionPoints = new();

        public void SetIndicatorVisible(bool visible)
        {
            if (m_indicator != null)
                m_indicator.SetVisible(visible);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            for (int i = 0; i < m_interactionPoints.Count; i++)
            {
                FacilityUIInteractable interactionPoint = m_interactionPoints[i];
                if (interactionPoint != null)
                    interactionPoint.enabled = enabled;
            }
        }

        public void Subscribe(Action<FacilityUIInteractable> listener)
        {
            for (int i = 0; i < m_interactionPoints.Count; i++)
            {
                FacilityUIInteractable interactionPoint = m_interactionPoints[i];
                if (interactionPoint != null)
                    interactionPoint.Interacted += listener;
            }
        }

        public void Unsubscribe(Action<FacilityUIInteractable> listener)
        {
            for (int i = 0; i < m_interactionPoints.Count; i++)
            {
                FacilityUIInteractable interactionPoint = m_interactionPoints[i];
                if (interactionPoint != null)
                    interactionPoint.Interacted -= listener;
            }
        }
    }

    [Header("Scene Facilities")]
    [Tooltip("진행 단계에 따라 허용 여부를 바꿀 Scene의 모든 시설 상호작용 지점")]
    [SerializeField] private List<FacilityUIInteractable> m_allFacilityInteractionPoints = new();

    [Header("Objective Steps")]
    [SerializeField] private ObjectiveTarget m_firstFacilityObjective = new();
    [SerializeField] private ObjectiveTarget m_returnFacilityObjective = new();

    [Header("Startup")]
    [Tooltip("Scene 시작 시 첫 번째 시설 안내 단계부터 자동으로 시작함")]
    [SerializeField] private bool m_startAutomatically = true;

    private bool m_hasStarted;
    private FlowState m_currentState;

    /// <summary>현재 셸터 목표 진행 단계입니다.</summary>
    public FlowState CurrentState => m_currentState;

    /// <summary>진행 단계가 변경되었을 때 발생합니다.</summary>
    public event Action<FlowState> StateChanged;

    private void OnEnable()
    {
        m_firstFacilityObjective.Subscribe(HandleFirstFacilityInteracted);
        m_returnFacilityObjective.Subscribe(HandleReturnFacilityInteracted);

        if (m_hasStarted)
            ApplyCurrentState();
    }

    private void Start()
    {
        if (m_startAutomatically && !m_hasStarted)
            StartFlow();
    }

    private void OnDisable()
    {
        m_firstFacilityObjective.Unsubscribe(HandleFirstFacilityInteracted);
        m_returnFacilityObjective.Unsubscribe(HandleReturnFacilityInteracted);
    }

    /// <summary>첫 번째 시설 안내 단계부터 흐름을 시작하거나 초기화합니다.</summary>
    public void StartFlow()
    {
        m_hasStarted = true;
        EnterState(FlowState.GuideToFirstFacility, true);
    }

    /// <summary>방어전 시스템이 전투 완료 시 호출할 진입점입니다.</summary>
    public void NotifyDefenseCompleted()
    {
        if (!m_hasStarted || m_currentState != FlowState.WaitingForDefenseCompletion)
            return;

        EnterState(FlowState.GuideToReturnFacility);
    }

    /// <summary>첫 번째 목표 시설 상호작용 완료를 외부에서 직접 통지할 때 사용합니다.</summary>
    public void NotifyFirstFacilityInteracted()
    {
        if (!m_hasStarted || m_currentState != FlowState.GuideToFirstFacility)
            return;

        EnterState(FlowState.WaitingForDefenseCompletion);
    }

    /// <summary>귀환 목표 시설 상호작용 완료를 외부에서 직접 통지할 때 사용합니다.</summary>
    public void NotifyReturnFacilityInteracted()
    {
        if (!m_hasStarted || m_currentState != FlowState.GuideToReturnFacility)
            return;

        EnterState(FlowState.Completed);
    }

    private void HandleFirstFacilityInteracted(FacilityUIInteractable interactionPoint)
    {
        NotifyFirstFacilityInteracted();
    }

    private void HandleReturnFacilityInteracted(FacilityUIInteractable interactionPoint)
    {
        NotifyReturnFacilityInteracted();
    }

    private void EnterState(FlowState nextState, bool forceApply = false)
    {
        if (!forceApply && m_currentState == nextState)
            return;

        m_currentState = nextState;
        ApplyCurrentState();
        StateChanged?.Invoke(m_currentState);
    }

    private void ApplyCurrentState()
    {
        SetAllFacilityInteractionsEnabled(false);
        m_firstFacilityObjective.SetInteractionEnabled(false);
        m_returnFacilityObjective.SetInteractionEnabled(false);
        m_firstFacilityObjective.SetIndicatorVisible(false);
        m_returnFacilityObjective.SetIndicatorVisible(false);

        switch (m_currentState)
        {
            case FlowState.GuideToFirstFacility:
                m_firstFacilityObjective.SetInteractionEnabled(true);
                m_firstFacilityObjective.SetIndicatorVisible(true);
                break;

            case FlowState.WaitingForDefenseCompletion:
                m_firstFacilityObjective.SetInteractionEnabled(true);
                break;

            case FlowState.GuideToReturnFacility:
                m_returnFacilityObjective.SetInteractionEnabled(true);
                m_returnFacilityObjective.SetIndicatorVisible(true);
                break;

            case FlowState.Completed:
                SetAllFacilityInteractionsEnabled(true);
                m_firstFacilityObjective.SetInteractionEnabled(true);
                m_returnFacilityObjective.SetInteractionEnabled(true);
                break;
        }
    }

    private void SetAllFacilityInteractionsEnabled(bool enabled)
    {
        for (int i = 0; i < m_allFacilityInteractionPoints.Count; i++)
        {
            FacilityUIInteractable interactionPoint = m_allFacilityInteractionPoints[i];
            if (interactionPoint != null)
                interactionPoint.enabled = enabled;
        }
    }
}
