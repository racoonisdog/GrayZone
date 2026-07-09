using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 스쿼드 멤버의 현재 조작 대상과 카메라 타겟 전환을 관리하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 지정된 입력 키를 통해 조작 중인 스쿼드 멤버를 전환하고,
/// 현재 멤버의 카메라 타겟을 follow/aim 카메라에 반영합니다.
/// </remarks>
public class SquadManager : MonoBehaviour
{
    [Foldout("Squad Options")]
    [Tooltip("관리할 스쿼드 멤버 목록입니다.")]
    [FormerlySerializedAs("squadMembers")]
    [SerializeField] private List<SquadMemberController> m_squadMembers = new List<SquadMemberController>();

    [Tooltip("현재 플레이어가 직접 조작 중인 멤버 인덱스입니다.")]
    [FormerlySerializedAs("currentMemberIndex")]
    [SerializeField] private int m_currentMemberIndex;

    [Tooltip("스쿼드 멤버 순서와 같은 플레이어 공개 데이터 목록입니다.")]
    [SerializeField] private List<PlayerbleUnitData> m_playerDataSources = new List<PlayerbleUnitData>();

    [Foldout("Switch Options")]
    [Tooltip("카메라 타겟을 새 멤버로 넘기는 대신 현재 조작 멤버와 전환 대상 멤버의 Transform을 스왑하는 전환 방식을 사용할지 여부입니다.")]
    [SerializeField] private bool m_swapMemberTransforms;

    [Tooltip("현재 조작 멤버가 사망해 자동 전환될 때도 Transform 스왑 방식을 사용할지 여부입니다.")]
    [SerializeField] private bool m_swapMemberTransformsOnDeath;

    [Tooltip("멤버 전환 직후 각 멤버의 제어 주체, 위치, NavMeshAgent, Animator 상태를 콘솔에 출력합니다.")]
    [SerializeField] private bool m_logSwitchDebug = true;

    [Foldout("Camera Options")]
    [Tooltip("현재 멤버를 따라가는 기본 카메라입니다.")]
    [FormerlySerializedAs("followCamera")]
    [SerializeField] private CinemachineCamera m_followCamera;

    [Tooltip("현재 멤버를 따라가는 조준 카메라입니다.")]
    [FormerlySerializedAs("aimCamera")]
    [SerializeField] private CinemachineCamera m_aimCamera;

    [Foldout("Input Options")]
    [Tooltip("다음 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("nextMemberKey")]
    [SerializeField] private Key m_nextMemberKey = Key.Tab;

    [Tooltip("첫 번째 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("member1Key")]
    [SerializeField] private Key m_member1Key = Key.Digit1;

    [Tooltip("두 번째 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("member2Key")]
    [SerializeField] private Key m_member2Key = Key.Digit2;

    [Tooltip("세 번째 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("member3Key")]
    [SerializeField] private Key m_member3Key = Key.Digit3;

    private bool m_hasInitialized;
    private readonly List<SquadMemberController> m_subscribedMemberDeathEvents = new List<SquadMemberController>();

    /// <summary>현재 플레이어가 직접 조작 중인 멤버 인덱스입니다.</summary>
    public int CurrentMemberIndex => m_currentMemberIndex;

    /// <summary>멤버 전환 시 카메라 타겟 전환 대신 Transform 스왑 방식을 사용할지 여부입니다.</summary>
    public bool SwapMemberTransforms => m_swapMemberTransforms;

    /// <summary>현재 조작 멤버 사망으로 자동 전환될 때 Transform 스왑 방식을 사용할지 여부입니다.</summary>
    public bool SwapMemberTransformsOnDeath => m_swapMemberTransformsOnDeath;

    /// <summary>관리 중인 스쿼드 멤버 목록입니다.</summary>
    public IReadOnlyList<SquadMemberController> SquadMembers => m_squadMembers;

    public IReadOnlyList<PlayerbleUnitData> PlayerDataSources => m_playerDataSources;

    public PlayerbleUnitData CurrentPlayerData => GetPlayerData(m_currentMemberIndex);

    /// <summary>현재 플레이어가 직접 조작 중인 스쿼드 멤버입니다.</summary>
    public SquadMemberController CurrentMember
    {
        get
        {
            if (m_squadMembers == null || m_squadMembers.Count == 0)
            {
                return null;
            }

            if (m_currentMemberIndex < 0 || m_currentMemberIndex >= m_squadMembers.Count)
            {
                return null;
            }

            return m_squadMembers[m_currentMemberIndex];
        }
    }

    public PlayerbleUnitData GetPlayerData(int index)
    {
        SyncPlayerDataSources();

        if (m_playerDataSources == null || index < 0 || index >= m_playerDataSources.Count)
        {
            return null;
        }

        return m_playerDataSources[index];
    }

    /// <summary>다음 멤버로 전환하는 입력 키입니다.</summary>
    public Key NextMemberKey => m_nextMemberKey;

    /// <summary>첫 번째 멤버로 전환하는 입력 키입니다.</summary>
    public Key Member1Key => m_member1Key;

    /// <summary>두 번째 멤버로 전환하는 입력 키입니다.</summary>
    public Key Member2Key => m_member2Key;

    /// <summary>세 번째 멤버로 전환하는 입력 키입니다.</summary>
    public Key Member3Key => m_member3Key;

    /// <summary>
    /// Inspector에서 컴포넌트가 추가되거나 Reset될 때 현재 씬 기준으로 참조를 자동 보정합니다.
    /// </summary>
    private void Reset()
    {
        AutoFindReferences();
        NormalizeMemberIndex();
    }

    /// <summary>
    /// 런타임 시작 시 필요한 참조를 보정하고 현재 멤버 인덱스를 유효 범위로 보정합니다.
    /// </summary>
    private void Awake()
    {
        AutoFindReferences();
        NormalizeMemberIndex();
    }

    private void OnEnable()
    {
        AutoFindReferences();
        SubscribeMemberDeathEvents();
    }

    private void OnDisable()
    {
        UnsubscribeMemberDeathEvents();
    }

    /// <summary>
    /// 스쿼드 멤버들의 조작 상태를 초기화한 뒤 현재 멤버만 직접 조작 상태로 전환합니다.
    /// </summary>
    /// <returns>Unity 코루틴 실행을 위한 IEnumerator입니다.</returns>
    private IEnumerator Start()
    {
        SetAllMembersPlayerControlled(false);
        UpdateCameraTarget();

        // PlayerInput, Controller, NavMeshAgent의 초기 활성 상태가 안정화될 때까지 대기합니다.
        yield return null;
        yield return null;

        if (CurrentMember != null)
        {
            CurrentMember.SetPlayerControlled(true);
        }

        UpdateCameraTarget();
        RefreshCurrentMemberWeaponUI();

        // follower, input, animation 상태를 한 번 더 강제로 동기화합니다.
        yield return null;
        ForceRefreshMembers();
        UpdateCameraTarget();
        RefreshCurrentMemberWeaponUI();

        m_hasInitialized = true;
    }

    /// <summary>
    /// 매 프레임 스쿼드 멤버 전환 입력을 처리합니다.
    /// </summary>
    private void Update()
    {
        if (!m_hasInitialized)
        {
            return;
        }

        HandleSwitchInput();
    }

    /// <summary>
    /// 씬 또는 자식 오브젝트에서 필요한 참조를 자동 탐색합니다.
    /// </summary>
    private void AutoFindReferences()
    {
        if (m_squadMembers == null)
        {
            m_squadMembers = new List<SquadMemberController>();
        }

        if (m_squadMembers.Count == 0)
        {
            GetComponentsInChildren(true, m_squadMembers);
        }

        RemoveNullMembers();
        SyncPlayerDataSources();
    }

    /// <summary>
    /// 멤버 목록에서 null 항목을 제거합니다.
    /// </summary>
    private void RemoveNullMembers()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = m_squadMembers.Count - 1; i >= 0; i--)
        {
            if (m_squadMembers[i] == null)
            {
                m_squadMembers.RemoveAt(i);
            }
        }
    }

    private void SyncPlayerDataSources()
    {
        if (m_playerDataSources == null)
        {
            m_playerDataSources = new List<PlayerbleUnitData>();
        }

        int memberCount = m_squadMembers != null ? m_squadMembers.Count : 0;
        while (m_playerDataSources.Count < memberCount)
        {
            m_playerDataSources.Add(null);
        }

        while (m_playerDataSources.Count > memberCount)
        {
            m_playerDataSources.RemoveAt(m_playerDataSources.Count - 1);
        }

        for (int i = 0; i < memberCount; i++)
        {
            SquadMemberController member = m_squadMembers[i];
            PlayerbleUnitData currentData = m_playerDataSources[i];
            if (currentData == null || member == null || currentData.gameObject != member.gameObject)
            {
                m_playerDataSources[i] = member != null ? member.GetComponent<PlayerbleUnitData>() : null;
            }
        }
    }

    /// <summary>
    /// 현재 멤버 인덱스를 멤버 목록의 유효 범위 안으로 보정합니다.
    /// </summary>
    private void NormalizeMemberIndex()
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            m_currentMemberIndex = 0;
            return;
        }

        m_currentMemberIndex = Mathf.Clamp(m_currentMemberIndex, 0, m_squadMembers.Count - 1);
    }

    /// <summary>
    /// 키보드 입력을 확인하여 현재 조작 멤버를 전환합니다.
    /// </summary>
    private void HandleSwitchInput()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current[m_nextMemberKey].wasPressedThisFrame)
        {
            SwitchToNextMember();
        }

        if (Keyboard.current[m_member1Key].wasPressedThisFrame)
        {
            SwitchToMember(0);
        }

        if (Keyboard.current[m_member2Key].wasPressedThisFrame)
        {
            SwitchToMember(1);
        }

        if (Keyboard.current[m_member3Key].wasPressedThisFrame)
        {
            SwitchToMember(2);
        }
    }

    /// <summary>
    /// 현재 멤버 다음에 있는 전환 가능한 멤버로 조작 대상을 변경합니다.
    /// </summary>
    public void SwitchToNextMember()
    {
        TrySwitchToNextMember(m_swapMemberTransforms);
    }

    private bool TrySwitchToNextMember(bool useTransformSwap, bool logDebug = true)
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            return false;
        }

        int startIndex = m_currentMemberIndex;
        int nextIndex = m_currentMemberIndex;

        do
        {
            nextIndex++;

            if (nextIndex >= m_squadMembers.Count)
            {
                nextIndex = 0;
            }

            if (CanSwitchTo(nextIndex))
            {
                SwitchToMember(nextIndex, useTransformSwap, logDebug);
                return true;
            }
        }
        while (nextIndex != startIndex);

        return false;
    }

    /// <summary>
    /// 지정한 인덱스의 스쿼드 멤버로 조작 대상을 변경합니다.
    /// </summary>
    /// <param name="index">전환할 스쿼드 멤버 인덱스입니다.</param>
    public void SwitchToMember(int index)
    {
        SwitchToMember(index, m_swapMemberTransforms);
    }

    private void SwitchToMember(int index, bool useTransformSwap, bool logDebug = true)
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            return;
        }

        if (index < 0 || index >= m_squadMembers.Count)
        {
            return;
        }

        if (!CanSwitchTo(index))
        {
            return;
        }

        if (index == m_currentMemberIndex)
        {
            UpdateCameraTarget();
            RefreshCurrentMemberWeaponUI();
            return;
        }

        SquadMemberController previousMember = CurrentMember;
        SquadMemberController nextMember = m_squadMembers[index];
        bool carrySwitchState = useTransformSwap && previousMember != null && nextMember != null;
        SquadMemberController.SwitchCarryoverState switchState = carrySwitchState
            ? previousMember.CaptureSwitchCarryoverState()
            : default;
        SquadFollowerAI.FollowCarryoverState followState = carrySwitchState
            ? nextMember.CaptureFollowCarryoverState()
            : default;

        if (previousMember != null)
        {
            previousMember.SetPlayerControlled(false);
        }

        m_currentMemberIndex = index;

        if (nextMember != null)
        {
            nextMember.SetPlayerControlled(true);

            if (carrySwitchState)
            {
                nextMember.ApplySwitchCarryoverState(switchState);
            }
        }

        if (useTransformSwap)
        {
            ApplyMemberTransformSwap(previousMember, nextMember);

            if (previousMember != null)
            {
                previousMember.ApplyFollowCarryoverState(followState);
            }
        }

        UpdateCameraTarget();
        RefreshCurrentMemberWeaponUI();

        if (useTransformSwap)
        {
            CancelCameraDamping();
        }

        if (logDebug && m_logSwitchDebug)
        {
            StartCoroutine(LogSwitchDebug(previousMember, nextMember));
        }
    }

    private void SubscribeMemberDeathEvents()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            SquadMemberController member = m_squadMembers[i];
            if (member == null || m_subscribedMemberDeathEvents.Contains(member))
            {
                continue;
            }

            member.OnMemberDied += HandleMemberDied;
            member.OnMemberDowned += HandleMemberDowned;
            m_subscribedMemberDeathEvents.Add(member);
        }
    }

    private void UnsubscribeMemberDeathEvents()
    {
        for (int i = 0; i < m_subscribedMemberDeathEvents.Count; i++)
        {
            SquadMemberController member = m_subscribedMemberDeathEvents[i];
            if (member != null)
            {
                member.OnMemberDied -= HandleMemberDied;
                member.OnMemberDowned -= HandleMemberDowned;
            }
        }

        m_subscribedMemberDeathEvents.Clear();
    }

    private void HandleMemberDied(SquadMemberController member)
    {
        if (member == null)
        {
            return;
        }

        bool wasCurrentMember = member == CurrentMember;
        bool switched = !wasCurrentMember || TrySwitchToNextMember(m_swapMemberTransformsOnDeath, false);

        member.ClearDeadControlState();

        if (wasCurrentMember && !switched && m_logSwitchDebug)
        {
            Debug.Log("[SquadManager] No available squad member remains after current member death.", this);
        }
    }

    /// <summary>
    /// 멤버가 다운(빈사) 상태가 되면, 그 멤버가 현재 조작 대상일 때 조작 가능한 다른 멤버로 자동 전환합니다.
    /// </summary>
    /// <remarks>
    /// 다운은 사망과 달리 구조로 복귀할 수 있으므로 사망 정리(ClearDeadControlState)는 수행하지 않습니다.
    /// 조작 권한 해제는 다운 진입 시 <see cref="SquadMemberController"/>가 이미 처리합니다.
    /// </remarks>
    private void HandleMemberDowned(SquadMemberController member)
    {
        if (member == null || member != CurrentMember)
        {
            return;
        }

        bool switched = TrySwitchToNextMember(m_swapMemberTransformsOnDeath, false);

        if (!switched && m_logSwitchDebug)
        {
            Debug.Log("[SquadManager] No available squad member remains after current member down.", this);
        }
    }

    /// <summary>
    /// 지정한 인덱스의 스쿼드 멤버가 조작 대상으로 전환 가능한지 확인합니다.
    /// </summary>
    /// <param name="index">검사할 스쿼드 멤버 인덱스입니다.</param>
    /// <returns>전환 가능하면 true입니다.</returns>
    private bool CanSwitchTo(int index)
    {
        if (m_squadMembers == null || index < 0 || index >= m_squadMembers.Count)
        {
            return false;
        }

        SquadMemberController member = m_squadMembers[index];

        if (member == null)
        {
            return false;
        }

        if (!member.IsAlive)
        {
            return false;
        }

        if (member.IsDown)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 모든 스쿼드 멤버의 직접 조작 상태를 일괄 설정합니다.
    /// </summary>
    /// <param name="value">직접 조작 상태로 설정하려면 true입니다.</param>
    private void SetAllMembersPlayerControlled(bool value)
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            if (m_squadMembers[i] == null)
            {
                continue;
            }

            m_squadMembers[i].SetPlayerControlled(value);
        }
    }

    /// <summary>
    /// 모든 스쿼드 멤버의 입력, AI, 애니메이션 상태를 현재 조작 상태에 맞게 강제로 갱신합니다.
    /// </summary>
    private void ForceRefreshMembers()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            if (m_squadMembers[i] == null)
            {
                continue;
            }

            m_squadMembers[i].ForceRefreshState();
        }
    }

    /// <summary>
    /// 현재 조작 중인 멤버의 무기 탄약 UI를 현재 무기 상태로 갱신합니다.
    /// </summary>
    private void RefreshCurrentMemberWeaponUI()
    {
        if (CurrentMember != null)
        {
            CurrentMember.RefreshWeaponUI();
        }
    }

    /// <summary>
    /// 스쿼드 멤버 전환 시 이전 멤버와 다음 멤버의 월드 위치와 회전을 맞바꿉니다.
    /// </summary>
    /// <param name="previousMember">전환 전 조작 중이던 스쿼드 멤버입니다.</param>
    /// <param name="nextMember">전환 후 조작할 스쿼드 멤버입니다.</param>
    private void ApplyMemberTransformSwap(SquadMemberController previousMember, SquadMemberController nextMember)
    {
        if (previousMember == null || nextMember == null)
        {
            return;
        }

        Transform previousTransform = previousMember.transform;
        Transform nextTransform = nextMember.transform;
        Transform previousCameraTarget = previousMember.CameraTarget;

        Vector3 previousPosition = previousTransform.position;
        Quaternion previousRotation = previousTransform.rotation;
        Quaternion previousCameraTargetRotation = previousCameraTarget.rotation;

        Vector3 nextPosition = nextTransform.position;
        Quaternion nextRotation = nextTransform.rotation;

        MoveMemberTo(previousMember, nextPosition, nextRotation);
        MoveMemberTo(nextMember, previousPosition, previousRotation);
        nextMember.SyncCameraTargetRotation(previousCameraTargetRotation);

        static void MoveMemberTo(SquadMemberController member, Vector3 position, Quaternion rotation)
        {
            Transform memberTransform = member.transform;
            CharacterController characterController = member.GetComponent<CharacterController>();
            UnityEngine.AI.NavMeshAgent navMeshAgent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();

            bool wasCharacterControllerEnabled = characterController != null && characterController.enabled;

            if (wasCharacterControllerEnabled)
            {
                characterController.enabled = false;
            }

            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.Warp(position);
                navMeshAgent.ResetPath();
                memberTransform.rotation = rotation;
            }
            else
            {
                memberTransform.SetPositionAndRotation(position, rotation);
            }

            if (wasCharacterControllerEnabled)
            {
                characterController.enabled = true;
            }
        }
    }

    /// <summary>
    /// 멤버 Transform 스왑 직후 Cinemachine의 이전 추적 상태를 초기화해 카메라 보간 이동을 막습니다.
    /// </summary>
    private void CancelCameraDamping()
    {
        if (m_followCamera != null)
        {
            m_followCamera.CancelDamping(true);
        }

        if (m_aimCamera != null)
        {
            m_aimCamera.CancelDamping(true);
        }
    }

    /// <summary>
    /// 멤버 전환 후 여러 시점의 상태를 콘솔에 출력해 공중/낙하 상태가 어느 멤버에 남는지 확인합니다.
    /// </summary>
    /// <param name="previousMember">전환 전 플레이어 조작 멤버입니다.</param>
    /// <param name="nextMember">전환 후 플레이어 조작 멤버입니다.</param>
    private IEnumerator LogSwitchDebug(SquadMemberController previousMember, SquadMemberController nextMember)
    {
        LogSwitchDebugSnapshot("immediate", previousMember, nextMember);

        yield return null;
        LogSwitchDebugSnapshot("next-frame", previousMember, nextMember);

        yield return new WaitForSeconds(0.25f);
        LogSwitchDebugSnapshot("after-0.25s", previousMember, nextMember);

        yield return new WaitForSeconds(0.75f);
        LogSwitchDebugSnapshot("after-1.00s", previousMember, nextMember);
    }

    /// <summary>
    /// 현재 스쿼드 멤버들의 런타임 상태를 한 번 출력합니다.
    /// </summary>
    /// <param name="label">출력 시점 라벨입니다.</param>
    /// <param name="previousMember">전환 전 플레이어 조작 멤버입니다.</param>
    /// <param name="nextMember">전환 후 플레이어 조작 멤버입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogSwitchDebugSnapshot(string label, SquadMemberController previousMember, SquadMemberController nextMember)
    {
        string previousName = previousMember != null ? previousMember.name : "null";
        string nextName = nextMember != null ? nextMember.name : "null";
        string currentName = CurrentMember != null ? CurrentMember.name : "null";

        Debug.Log(
            $"[SquadSwitchDebug] {label} previous={previousName} next={nextName} current={currentName} currentIndex={m_currentMemberIndex} swap={m_swapMemberTransforms}",
            this);

        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            SquadMemberController member = m_squadMembers[i];

            if (member == null)
            {
                Debug.Log($"[SquadSwitchDebug] {label} [{i}] null", this);
                continue;
            }

            CharacterController characterController = member.GetComponent<CharacterController>();
            UnityEngine.AI.NavMeshAgent navMeshAgent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();
            ThirdPersonController thirdPersonController = member.GetComponent<ThirdPersonController>();
            Animator animator = member.GetComponent<Animator>();

            bool agentEnabled = navMeshAgent != null && navMeshAgent.enabled;
            bool agentOnNavMesh = agentEnabled && navMeshAgent.isOnNavMesh;
            bool grounded = animator != null && animator.GetBool("IsGrounded");
            bool jump = animator != null && animator.GetBool("IsJump");
            bool freeFall = animator != null && animator.GetBool("IsFreeFall");
            float speed = animator != null ? animator.GetFloat("Speed") : 0.0f;
            float motionSpeed = animator != null ? animator.GetFloat("MotionSpeed") : 0.0f;
            Vector3 agentVelocity = agentEnabled ? navMeshAgent.velocity : Vector3.zero;

            Debug.Log(
                $"[SquadSwitchDebug] {label} [{i}] name={member.name} player={member.IsPlayerControlled} pos={member.transform.position} " +
                $"cc={(characterController != null && characterController.enabled)} tpc={(thirdPersonController != null && thirdPersonController.enabled)} " +
                $"agent={agentEnabled} onNav={agentOnNavMesh} agentVel={agentVelocity} " +
                $"grounded={grounded} jump={jump} freeFall={freeFall} speed={speed:F3} motion={motionSpeed:F3}",
                member);
        }
    }

    /// <summary>
    /// 현재 조작 중인 스쿼드 멤버의 카메라 타겟을 follow/aim 카메라에 반영합니다.
    /// </summary>
    private void UpdateCameraTarget()
    {
        if (CurrentMember == null)
        {
            return;
        }

        Transform target = CurrentMember.CameraTarget;

        if (target == null)
        {
            return;
        }

        if (m_followCamera != null)
        {
            m_followCamera.Follow = target;
            m_followCamera.LookAt = target;
        }

        if (m_aimCamera != null)
        {
            m_aimCamera.Follow = target;
            m_aimCamera.LookAt = target;
        }
    }

    /// <summary>
    /// 스쿼드 멤버 목록을 새 목록으로 교체합니다.
    /// </summary>
    /// <param name="members">새 스쿼드 멤버 목록입니다.</param>
    public void SetSquadMembers(List<SquadMemberController> members)
    {
        UnsubscribeMemberDeathEvents();
        m_squadMembers = members ?? new List<SquadMemberController>();
        RemoveNullMembers();
        SyncPlayerDataSources();
        NormalizeMemberIndex();
        SubscribeMemberDeathEvents();
        UpdateCameraTarget();
        RefreshCurrentMemberWeaponUI();
    }

    /// <summary>
    /// 현재 조작 멤버 인덱스를 설정합니다.
    /// </summary>
    /// <param name="value">설정할 멤버 인덱스입니다.</param>
    public void SetCurrentMemberIndex(int value)
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            m_currentMemberIndex = 0;
            return;
        }

        SwitchToMember(Mathf.Clamp(value, 0, m_squadMembers.Count - 1));
    }

    /// <summary>
    /// 다음 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetNextMemberKey(Key value)
    {
        m_nextMemberKey = value;
    }

    /// <summary>
    /// 첫 번째 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetMember1Key(Key value)
    {
        m_member1Key = value;
    }

    /// <summary>
    /// 두 번째 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetMember2Key(Key value)
    {
        m_member2Key = value;
    }

    /// <summary>
    /// 세 번째 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetMember3Key(Key value)
    {
        m_member3Key = value;
    }
}
