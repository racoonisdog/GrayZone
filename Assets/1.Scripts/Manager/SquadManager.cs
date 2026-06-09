using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class SquadManager : MonoBehaviour
{
    [Header("Squad Members")]
    [SerializeField] private List<SquadMemberController> squadMembers = new List<SquadMemberController>();

    [Header("Current Control")]
    [SerializeField] private int currentMemberIndex = 0;

    [Header("Camera")]
    [SerializeField] private CinemachineCamera followCamera;
    [SerializeField] private CinemachineCamera aimCamera;

    [Header("Input")]
    [SerializeField] private Key nextMemberKey = Key.Tab;
    [SerializeField] private Key member1Key = Key.Digit1;
    [SerializeField] private Key member2Key = Key.Digit2;
    [SerializeField] private Key member3Key = Key.Digit3;

    public int CurrentMemberIndex => currentMemberIndex;

    public SquadMemberController CurrentMember
    {
        get
        {
            if (squadMembers == null || squadMembers.Count == 0) return null;
            if (currentMemberIndex < 0 || currentMemberIndex >= squadMembers.Count) return null;
            return squadMembers[currentMemberIndex];
        }
    }

    private IEnumerator Start()
    {
        // 시작 시 모두 비조작 상태로 내려서 초기화
        for (int i = 0; i < squadMembers.Count; i++)
        {
            if (squadMembers[i] == null) continue;
            squadMembers[i].SetPlayerControlled(false);
        }

        UpdateCameraTarget();

        // PlayerInput / Controller / Agent 초기화 대기
        yield return null;
        yield return null;

        // 현재 멤버만 조작 상태로 올림
        if (CurrentMember != null)
        {
            CurrentMember.SetPlayerControlled(true);
        }

        UpdateCameraTarget();

        // follower / input / animation 상태 한 번 더 강제 적용
        yield return null;

        for (int i = 0; i < squadMembers.Count; i++)
        {
            if (squadMembers[i] == null) continue;
            squadMembers[i].ForceRefreshState();
        }

        UpdateCameraTarget();
    }

    private void Update()
    {
        HandleSwitchInput();
    }

    private void HandleSwitchInput()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[nextMemberKey].wasPressedThisFrame)
        {
            SwitchToNextMember();
        }

        if (Keyboard.current[member1Key].wasPressedThisFrame)
        {
            SwitchToMember(0);
        }

        if (Keyboard.current[member2Key].wasPressedThisFrame)
        {
            SwitchToMember(1);
        }

        if (Keyboard.current[member3Key].wasPressedThisFrame)
        {
            SwitchToMember(2);
        }
    }

    public void SwitchToNextMember()
    {
        if (squadMembers == null || squadMembers.Count == 0) return;

        int startIndex = currentMemberIndex;
        int nextIndex = currentMemberIndex;

        do
        {
            nextIndex++;
            if (nextIndex >= squadMembers.Count)
                nextIndex = 0;

            if (CanSwitchTo(nextIndex))
            {
                SwitchToMember(nextIndex);
                return;
            }

        } while (nextIndex != startIndex);
    }

    public void SwitchToMember(int index)
    {
        if (squadMembers == null || squadMembers.Count == 0) return;
        if (index < 0 || index >= squadMembers.Count) return;
        if (!CanSwitchTo(index)) return;
        if (index == currentMemberIndex) return;

        SquadMemberController previousMember = CurrentMember;
        SquadMemberController nextMember = squadMembers[index];

        if (previousMember != null)
        {
            previousMember.SetPlayerControlled(false);
        }

        currentMemberIndex = index;

        if (nextMember != null)
        {
            nextMember.SetPlayerControlled(true);
        }

        UpdateCameraTarget();
    }

    private bool CanSwitchTo(int index)
    {
        if (index < 0 || index >= squadMembers.Count) return false;

        SquadMemberController member = squadMembers[index];
        if (member == null) return false;
        if (!member.IsAlive) return false;
        if (member.IsDown) return false;

        return true;
    }

    private void UpdateCameraTarget()
    {
        if (CurrentMember == null) return;

        Transform target = CurrentMember.CameraTarget;

        if (followCamera != null)
        {
            followCamera.Follow = target;
            followCamera.LookAt = target;
        }

        if (aimCamera != null)
        {
            aimCamera.Follow = target;
            aimCamera.LookAt = target;
        }
    }
}