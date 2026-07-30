using System.Collections;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 스쿼드 멤버의 PlayerSquadMember 역할과 카메라 타겟 전환을 관리하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 지정된 입력 키를 통해 조작 중인 스쿼드 멤버를 전환하고,
/// PlayerSquadMember의 카메라 타겟을 follow/aim 카메라에 반영합니다.
/// </remarks>
public class SquadManager : MonoBehaviour
{
    private static SquadManager s_instance;

    /// <summary>현재 씬의 인스턴스입니다. 스쿼드가 없는 씬이면 <c>null</c>입니다.</summary>
    /// <remarks>씬에 속하므로 씬 전환과 함께 사라집니다. 셸터처럼 스쿼드가 없는 씬에서는 없는 것이 정상입니다.</remarks>
    public static SquadManager Instance => s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    [Foldout("Squad Options")]
    [Tooltip("관리할 스쿼드 멤버 목록입니다.")]
    [FormerlySerializedAs("squadMembers")]
    [SerializeField] private List<SquadMemberController> m_squadMembers = new List<SquadMemberController>();

    [Tooltip("현재 PlayerSquadMember의 스쿼드 목록 인덱스입니다.")]
    [FormerlySerializedAs("currentMemberIndex")]
    [FormerlySerializedAs("m_currentMemberIndex")]
    [SerializeField] private int m_playerSquadMemberIndex;

    [Tooltip("스쿼드 멤버 순서와 같은 플레이어 공개 데이터 목록입니다.")]
    [SerializeField] private List<PlayerbleUnitData> m_playerDataSources = new List<PlayerbleUnitData>();

    [Foldout("Switch Options")]
    [Tooltip("카메라 타겟을 새 멤버로 넘기는 대신 PlayerSquadMember와 전환 대상 멤버의 Transform을 스왑하는 전환 방식을 사용할지 여부입니다.")]
    [SerializeField] private bool m_swapMemberTransforms;

    [Tooltip("PlayerSquadMember가 사망해 자동 전환될 때도 Transform 스왑 방식을 사용할지 여부입니다.")]
    [SerializeField] private bool m_swapMemberTransformsOnDeath;

    [Tooltip("전환 입력을 다시 받기까지 기다리는 시간(초)입니다. 연타로 전환이 겹쳐 쌓이는 것을 막습니다. 사망 자동 전환에는 적용하지 않습니다.")]
    [SerializeField] private float m_switchInputCooldown = 0.1f;

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

    /// <summary>전환 입력을 다시 받을 수 있는 시각입니다.</summary>
    private float m_nextSwitchInputTime;

    private bool m_hasInitialized;
    private bool m_squadEliminationNotified;
    private readonly List<SquadMemberController> m_subscribedMemberDeathEvents = new List<SquadMemberController>();

    /// <summary>조작 가능한 스쿼드원이 한 명도 남지 않아 게임오버 조건이 성립했을 때 발생합니다.</summary>
    public event Action OnSquadEliminated;

    /// <summary>현재 PlayerSquadMember의 스쿼드 목록 인덱스입니다.</summary>
    public int PlayerSquadMemberIndex => m_playerSquadMemberIndex;

    /// <summary>멤버 전환 시 카메라 타겟 전환 대신 Transform 스왑 방식을 사용할지 여부입니다.</summary>
    public bool SwapMemberTransforms => m_swapMemberTransforms;

    /// <summary>PlayerSquadMember 사망으로 자동 전환될 때 Transform 스왑 방식을 사용할지 여부입니다.</summary>
    public bool SwapMemberTransformsOnDeath => m_swapMemberTransformsOnDeath;

    /// <summary>관리 중인 스쿼드 멤버 목록입니다.</summary>
    public IReadOnlyList<SquadMemberController> SquadMembers => m_squadMembers;

    /// <summary>스쿼드 멤버 순서에 대응하는 플레이어 공개 데이터 목록입니다.</summary>
    public IReadOnlyList<PlayerbleUnitData> PlayerDataSources => m_playerDataSources;

    /// <summary>현재 PlayerSquadMember에 대응하는 플레이어 공개 데이터입니다.</summary>
    public PlayerbleUnitData PlayerSquadMemberData => GetPlayerData(m_playerSquadMemberIndex);

    /// <summary>현재 플레이어가 직접 조작 중인 스쿼드 멤버입니다.</summary>
    public SquadMemberController PlayerSquadMember
    {
        get
        {
            if (m_squadMembers == null || m_squadMembers.Count == 0)
            {
                return null;
            }

            if (m_playerSquadMemberIndex < 0 || m_playerSquadMemberIndex >= m_squadMembers.Count)
            {
                return null;
            }

            return m_squadMembers[m_playerSquadMemberIndex];
        }
    }

    /// <summary>지정한 스쿼드 목록 인덱스에 대응하는 플레이어 공개 데이터를 반환합니다.</summary>
    /// <param name="index">조회할 스쿼드 목록 인덱스입니다.</param>
    /// <returns>대응하는 데이터가 없으면 null입니다.</returns>
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
        // 스쿼드 구성은 씬에 하나만 있어야 합니다. 중복이 있으면 조작 캐릭터가 둘로 갈립니다.
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning("[SquadManager] 씬에 이미 인스턴스가 있어 중복된 쪽을 제거합니다.", this);
            Destroy(gameObject);
            return;
        }

        s_instance = this;

        AutoFindReferences();
        NormalizeMemberIndex();
        ApplyInitialMemberRolePresets();
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
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
        UpdateCameraTarget();

        // Awake에서 역할 프리셋을 이미 적용했습니다. PlayerInput, Controller, NavMeshAgent의 초기 활성 상태가
        // 안정화될 때까지 대기한 뒤 현재 상태를 한 번 더 확인합니다.
        yield return null;
        yield return null;

        UpdateCameraTarget();
        RefreshPlayerSquadMemberWeaponUI();

        // follower, input, animation 상태를 한 번 더 강제로 동기화합니다.
        yield return null;
        ForceRefreshMembers();
        UpdateCameraTarget();
        RefreshPlayerSquadMemberWeaponUI();

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
            m_playerSquadMemberIndex = 0;
            return;
        }

        m_playerSquadMemberIndex = Mathf.Clamp(m_playerSquadMemberIndex, 0, m_squadMembers.Count - 1);
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

        if (Time.time < m_nextSwitchInputTime)
        {
            return;
        }

        if (Keyboard.current[m_nextMemberKey].wasPressedThisFrame)
        {
            RequestSwitchByInput(-1);
        }

        if (Keyboard.current[m_member1Key].wasPressedThisFrame)
        {
            RequestSwitchByInput(0);
        }

        if (Keyboard.current[m_member2Key].wasPressedThisFrame)
        {
            RequestSwitchByInput(1);
        }

        if (Keyboard.current[m_member3Key].wasPressedThisFrame)
        {
            RequestSwitchByInput(2);
        }
    }

    /// <summary>
    /// 입력으로 요청한 전환을 처리하고 다음 입력까지의 간격을 둡니다.
    /// </summary>
    /// <param name="index">전환할 멤버 인덱스이며, -1이면 다음 멤버로 넘깁니다.</param>
    /// <remarks>
    /// 간격을 두는 것은 연타로 전환이 짧은 시간에 쌓이는 것을 막기 위해서입니다.
    /// 전환마다 위치가 맞바뀌므로 반복되면 무슨 일이 일어났는지 보이지 않고,
    /// 전환 직후 밀림이 남아 있는 동안 다시 전환되면 그 영향이 누적됩니다.
    ///
    /// 사망으로 인한 자동 전환에는 걸지 않습니다. 그쪽까지 막으면 조작할 캐릭터가 없는 시간이 생깁니다.
    /// 실제로 전환이 일어났을 때만 간격을 두는 것은, 같은 멤버를 다시 누르거나 전환할 수 없는 대상을 눌렀을 때
    /// 아무 일도 없었는데 입력이 씹히는 것처럼 느껴지지 않게 하기 위해서입니다.
    /// </remarks>
    private void RequestSwitchByInput(int index)
    {
        bool switched = index < 0
            ? TrySwitchToNextMember(m_swapMemberTransforms)
            : TrySwitchToMember(index);

        if (switched)
        {
            m_nextSwitchInputTime = Time.time + Mathf.Max(0f, m_switchInputCooldown);
        }
    }

    /// <summary>지정한 인덱스로 전환을 시도하고 실제로 바뀌었는지 알려 줍니다.</summary>
    /// <param name="index">전환할 스쿼드 멤버 인덱스입니다.</param>
    /// <returns>전환이 일어났으면 true입니다.</returns>
    private bool TrySwitchToMember(int index)
    {
        if (m_squadMembers == null || index < 0 || index >= m_squadMembers.Count)
        {
            return false;
        }

        if (index == m_playerSquadMemberIndex || !CanSwitchTo(index))
        {
            return false;
        }

        SwitchToMember(index, m_swapMemberTransforms);
        return true;
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

        int startIndex = m_playerSquadMemberIndex;
        int nextIndex = m_playerSquadMemberIndex;

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

        if (index == m_playerSquadMemberIndex)
        {
            UpdateCameraTarget();
            RefreshPlayerSquadMemberWeaponUI();
            return;
        }

        SquadMemberController previousMember = PlayerSquadMember;
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
            previousMember.SetPlayerSquadMember(false);
        }

        m_playerSquadMemberIndex = index;

        if (nextMember != null)
        {
            nextMember.SetPlayerSquadMember(true);

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
        RefreshPlayerSquadMemberWeaponUI();

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

        bool wasPlayerSquadMember = member == PlayerSquadMember;
        bool switched = !wasPlayerSquadMember || TrySwitchToNextMember(m_swapMemberTransformsOnDeath, false);

        member.ClearDeadControlState();

        if (wasPlayerSquadMember && !switched && m_logSwitchDebug)
        {
            Debug.Log("[SquadManager] No available squad member remains after current member death.", this);
        }

        NotifySquadEliminatedIfNeeded();
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
        if (member == null || member != PlayerSquadMember)
        {
            return;
        }

        bool switched = TrySwitchToNextMember(m_swapMemberTransformsOnDeath, false);

        if (!switched && m_logSwitchDebug)
        {
            Debug.Log("[SquadManager] No available squad member remains after current member down.", this);
        }

        NotifySquadEliminatedIfNeeded();
    }

    /// <summary>조작 가능한 멤버가 한 명도 남지 않았으면 전멸 이벤트를 한 번만 발생시킵니다.</summary>
    private void NotifySquadEliminatedIfNeeded()
    {
        if (m_squadEliminationNotified || m_squadMembers == null || m_squadMembers.Count == 0)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            if (CanSwitchTo(i))
            {
                return;
            }
        }

        m_squadEliminationNotified = true;
        OnSquadEliminated?.Invoke();
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
    /// 시작 시 스쿼드 인덱스 기준으로 각 멤버의 역할별 초기 프리셋을 적용합니다.
    /// </summary>
    private void ApplyInitialMemberRolePresets()
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

            ApplyInitialRolePreset(m_squadMembers[i], i == m_playerSquadMemberIndex);
        }
    }

    /// <summary>
    /// 필드 씬에서 새 플레이어블 캐릭터를 만든 직후 호출할 역할별 초기 프리셋 진입점입니다.
    /// </summary>
    /// <remarks>
    /// 이 메서드는 스쿼드 목록 등록을 대신하지 않습니다. 생성 시스템은 멤버를 목록에 등록한 뒤,
    /// 해당 멤버의 첫 역할에 맞춰 이 메서드를 호출합니다.
    /// </remarks>
    /// <param name="member">초기화할 플레이어블 스쿼드 멤버입니다.</param>
    /// <param name="isPlayerSquadMember">첫 프레임부터 직접 조작할 멤버이면 true입니다.</param>
    public void ApplyInitialRolePreset(SquadMemberController member, bool isPlayerSquadMember)
    {
        if (member == null)
        {
            return;
        }

        if (isPlayerSquadMember)
        {
            member.ApplyPlayerInitialSetup();
        }
        else
        {
            member.ApplyAiInitialSetup();
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
    private void RefreshPlayerSquadMemberWeaponUI()
    {
        if (PlayerSquadMember != null)
        {
            PlayerSquadMember.RefreshWeaponUI();
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

        // 두 멤버를 한 명씩 끝까지 옮기면 안 됩니다.
        // 먼저 옮긴 멤버가 아직 자리를 비우지 않은 상대 위에 겹쳐 놓이고,
        // 그 상태로 CharacterController를 다시 켜면 유니티가 겹침을 풀려고 캐릭터를 밀어냅니다.
        // 그래서 둘 다 끈 뒤에 위치를 정하고, 자리를 다 잡은 다음에 함께 켭니다.
        CharacterController previousController = previousMember.GetComponent<CharacterController>();
        CharacterController nextController = nextMember.GetComponent<CharacterController>();

        bool previousControllerWasEnabled = previousController != null && previousController.enabled;
        bool nextControllerWasEnabled = nextController != null && nextController.enabled;

        if (previousControllerWasEnabled)
        {
            previousController.enabled = false;
        }

        if (nextControllerWasEnabled)
        {
            nextController.enabled = false;
        }

        PlaceMember(previousMember, nextPosition, nextRotation);
        PlaceMember(nextMember, previousPosition, previousRotation);

        if (previousControllerWasEnabled)
        {
            previousController.enabled = true;
        }

        if (nextControllerWasEnabled)
        {
            nextController.enabled = true;
        }

        nextMember.SyncCameraTargetRotation(previousCameraTargetRotation);

        static void PlaceMember(SquadMemberController member, Vector3 position, Quaternion rotation)
        {
            Transform memberTransform = member.transform;
            UnityEngine.AI.NavMeshAgent navMeshAgent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();

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
        string playerSquadMemberName = PlayerSquadMember != null ? PlayerSquadMember.name : "null";

        Debug.Log(
            $"[SquadSwitchDebug] {label} previous={previousName} next={nextName} playerSquadMember={playerSquadMemberName} playerSquadMemberIndex={m_playerSquadMemberIndex} swap={m_swapMemberTransforms}",
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
            float speed = animator != null ? animator.GetFloat("MoveSpeed") : 0.0f;
            float motionSpeed = animator != null ? animator.GetFloat("MotionSpeed") : 0.0f;
            Vector3 agentVelocity = agentEnabled ? navMeshAgent.velocity : Vector3.zero;

            Debug.Log(
                $"[SquadSwitchDebug] {label} [{i}] name={member.name} playerSquadMember={member.IsPlayerSquadMember} pos={member.transform.position} " +
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
        if (PlayerSquadMember == null)
        {
            return;
        }

        Transform target = PlayerSquadMember.CameraTarget;

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
        RefreshPlayerSquadMemberWeaponUI();
    }

    /// <summary>
    /// 현재 조작 멤버 인덱스를 설정합니다.
    /// </summary>
    /// <param name="value">설정할 멤버 인덱스입니다.</param>
    public void SetPlayerSquadMemberIndex(int value)
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            m_playerSquadMemberIndex = 0;
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
