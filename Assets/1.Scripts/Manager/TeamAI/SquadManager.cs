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

    /// <summary>현재 플레이어가 직접 조작 중인 멤버 인덱스입니다.</summary>
    public int CurrentMemberIndex => m_currentMemberIndex;

    /// <summary>관리 중인 스쿼드 멤버 목록입니다.</summary>
    public IReadOnlyList<SquadMemberController> SquadMembers => m_squadMembers;

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

        // follower, input, animation 상태를 한 번 더 강제로 동기화합니다.
        yield return null;
        ForceRefreshMembers();
        UpdateCameraTarget();

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
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            return;
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
                SwitchToMember(nextIndex);
                return;
            }
        }
        while (nextIndex != startIndex);
    }

    /// <summary>
    /// 지정한 인덱스의 스쿼드 멤버로 조작 대상을 변경합니다.
    /// </summary>
    /// <param name="index">전환할 스쿼드 멤버 인덱스입니다.</param>
    public void SwitchToMember(int index)
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
            return;
        }

        SquadMemberController previousMember = CurrentMember;
        SquadMemberController nextMember = m_squadMembers[index];

        if (previousMember != null)
        {
            previousMember.SetPlayerControlled(false);
        }

        m_currentMemberIndex = index;

        if (nextMember != null)
        {
            nextMember.SetPlayerControlled(true);
        }

        UpdateCameraTarget();
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
        m_squadMembers = members ?? new List<SquadMemberController>();
        RemoveNullMembers();
        NormalizeMemberIndex();
        UpdateCameraTarget();
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
