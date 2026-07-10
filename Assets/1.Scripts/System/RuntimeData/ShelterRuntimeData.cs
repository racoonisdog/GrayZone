using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터 씬 전용 런타임 상태를 담는 직렬화 가능한 데이터 모델
/// </summary>
[Serializable]
public class ShelterRuntimeData
{
    /// <summary>전투 출격 스쿼드의 최소 인원 수</summary>
    public const int MinBattleSquadSize = 1;

    /// <summary>전투 출격 스쿼드의 최대 인원 수</summary>
    public const int MaxBattleSquadSize = 3;

    [SerializeField] private int currentDay = 1;
    [SerializeField] private List<string> battleSquadNpcDefinitionIds = new List<string>();
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new List<FacilityRuntimeState>();

    /// <summary>현재 셸터 날짜</summary>
    public int CurrentDay => Mathf.Max(1, currentDay);

    /// <summary>전투 출격 스쿼드 NPC 정의 ID 목록</summary>
    public IReadOnlyList<string> BattleSquadNpcDefinitionIds => battleSquadNpcDefinitionIds;

    /// <summary>시설별 런타임 상태 목록</summary>
    public IReadOnlyList<FacilityRuntimeState> FacilityStates => facilityStates;

    /// <summary>
    /// 런타임 컨테이너를 생성하고 저장 데이터에서 들어올 수 있는 잘못된 값을 보정
    /// </summary>
    public void EnsureRuntimeContainers()
    {
        currentDay = Mathf.Max(1, currentDay);
        battleSquadNpcDefinitionIds ??= new List<string>();
        facilityStates ??= new List<FacilityRuntimeState>();
        NormalizeBattleSquad();

        for (int i = facilityStates.Count - 1; i >= 0; i--)
        {
            if (facilityStates[i] == null)
                facilityStates.RemoveAt(i);
            else
                facilityStates[i].EnsureValid();
        }
    }

    /// <summary>
    /// 현재 셸터 전용 데이터를 깊은 복사
    /// </summary>
    /// <returns>현재 값으로 생성된 새 <see cref="ShelterRuntimeData"/></returns>
    public ShelterRuntimeData Clone()
    {
        EnsureRuntimeContainers();

        ShelterRuntimeData clone = new ShelterRuntimeData
        {
            currentDay = CurrentDay,
            battleSquadNpcDefinitionIds = new List<string>(battleSquadNpcDefinitionIds),
            facilityStates = CloneFacilityStates(facilityStates)
        };

        return clone;
    }

    /// <summary>
    /// 다른 셸터 런타임 데이터의 값을 현재 인스턴스에 복사
    /// </summary>
    /// <param name="source">복사할 원본 셸터 데이터</param>
    public void CopyFrom(ShelterRuntimeData source)
    {
        if (source == null)
            return;

        source.EnsureRuntimeContainers();
        EnsureRuntimeContainers();

        SetCurrentDay(source.CurrentDay);
        battleSquadNpcDefinitionIds = new List<string>(source.battleSquadNpcDefinitionIds);
        NormalizeBattleSquad();
        facilityStates = CloneFacilityStates(source.facilityStates);
    }

    /// <summary>
    /// 현재 셸터 날짜를 설정
    /// </summary>
    /// <param name="day">새 날짜 최소 1일로 보정</param>
    public void SetCurrentDay(int day)
    {
        currentDay = Mathf.Max(1, day);
    }

    /// <summary>
    /// 저장 데이터에서 읽은 날짜, 전투 스쿼드, 시설 상태를 런타임 상태에 적용
    /// </summary>
    /// <param name="day">저장된 셸터 날짜</param>
    /// <param name="battleSquadDefinitionIds">저장된 전투 스쿼드 NPC 정의 ID 목록</param>
    /// <param name="savedFacilityStates">저장된 시설 상태 목록</param>
    public void ApplySavedState(int day, IEnumerable<string> battleSquadDefinitionIds, IEnumerable<FacilityRuntimeState> savedFacilityStates)
    {
        SetCurrentDay(day);

        battleSquadNpcDefinitionIds.Clear();
        if (battleSquadDefinitionIds != null)
        {
            foreach (string definitionId in battleSquadDefinitionIds)
            {
                TryAddBattleSquadNpc(definitionId);
            }
        }

        facilityStates.Clear();
        if (savedFacilityStates != null)
        {
            foreach (FacilityRuntimeState state in savedFacilityStates)
            {
                if (state == null)
                    continue;

                state.EnsureValid();
                if (string.IsNullOrWhiteSpace(state.facilityId))
                    continue;

                facilityStates.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
            }
        }
    }

    /// <summary>
    /// 시설 상태를 조회하거나 없으면 기본 해금 상태로 새로 생성
    /// </summary>
    /// <param name="facilityId">조회할 시설 ID</param>
    /// <param name="isUnlockedByDefault">새로 만들 때 사용할 기본 해금 여부</param>
    /// <returns>시설 상태 시설 ID가 비어 있으면 <c>null</c></returns>
    public FacilityRuntimeState GetOrCreateFacilityState(string facilityId, bool isUnlockedByDefault)
    {
        EnsureRuntimeContainers();

        string normalizedFacilityId = string.IsNullOrWhiteSpace(facilityId) ? string.Empty : facilityId.Trim();
        if (string.IsNullOrEmpty(normalizedFacilityId))
            return null;

        foreach (FacilityRuntimeState state in facilityStates)
        {
            if (state == null)
                continue;

            state.EnsureValid();
            if (state.facilityId == normalizedFacilityId)
                return state;
        }

        FacilityRuntimeState created = new FacilityRuntimeState(normalizedFacilityId, isUnlockedByDefault);
        facilityStates.Add(created);
        return created;
    }

    /// <summary>
    /// 전투 출격 스쿼드 목록을 교체
    /// </summary>
    /// <param name="definitionIds">새 스쿼드 NPC 정의 ID 목록</param>
    /// <returns>최소/최대 인원 규칙을 만족해 교체됐으면 <c>true</c></returns>
    public bool TrySetBattleSquad(IEnumerable<string> definitionIds)
    {
        if (definitionIds == null)
            return false;

        List<string> normalizedIds = new List<string>();
        foreach (string definitionId in definitionIds)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                continue;

            string trimmedDefinitionId = definitionId.Trim();
            if (normalizedIds.Contains(trimmedDefinitionId))
                continue;

            normalizedIds.Add(trimmedDefinitionId);
            if (normalizedIds.Count > MaxBattleSquadSize)
                return false;
        }

        if (normalizedIds.Count < MinBattleSquadSize)
            return false;

        battleSquadNpcDefinitionIds = normalizedIds;
        return true;
    }

    /// <summary>
    /// 전투 출격 스쿼드에 NPC를 추가
    /// </summary>
    /// <param name="definitionId">추가할 NPC 정의 ID</param>
    /// <returns>추가됐거나 이미 포함되어 있으면 <c>true</c></returns>
    public bool TryAddBattleSquadNpc(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return false;

        string trimmedDefinitionId = definitionId.Trim();
        if (battleSquadNpcDefinitionIds.Contains(trimmedDefinitionId))
            return true;

        if (battleSquadNpcDefinitionIds.Count >= MaxBattleSquadSize)
            return false;

        battleSquadNpcDefinitionIds.Add(trimmedDefinitionId);
        return true;
    }

    /// <summary>
    /// 전투 출격 스쿼드에서 NPC를 제거
    /// </summary>
    /// <param name="definitionId">제거할 NPC 정의 ID</param>
    /// <returns>최소 인원 규칙을 유지하면서 제거됐으면 <c>true</c></returns>
    public bool TryRemoveBattleSquadNpc(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return false;

        if (battleSquadNpcDefinitionIds.Count <= MinBattleSquadSize)
            return false;

        return battleSquadNpcDefinitionIds.Remove(definitionId.Trim());
    }

    /// <summary>
    /// 전투 출격 스쿼드 목록을 비움.
    /// </summary>
    public void ClearBattleSquad()
    {
        battleSquadNpcDefinitionIds.Clear();
    }

    /// <summary>
    /// NPC 제거 시 셸터 전용 데이터 안의 해당 NPC 참조를 정리
    /// </summary>
    /// <param name="definitionId">정리할 NPC 정의 ID</param>
    public void RemoveNpcReferences(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return;

        battleSquadNpcDefinitionIds.RemoveAll(id => id == definitionId.Trim());
    }

    private void NormalizeBattleSquad()
    {
        for (int i = battleSquadNpcDefinitionIds.Count - 1; i >= 0; i--)
        {
            string definitionId = battleSquadNpcDefinitionIds[i];
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                battleSquadNpcDefinitionIds.RemoveAt(i);
                continue;
            }

            battleSquadNpcDefinitionIds[i] = definitionId.Trim();
        }

        for (int i = battleSquadNpcDefinitionIds.Count - 1; i >= 0; i--)
        {
            if (battleSquadNpcDefinitionIds.IndexOf(battleSquadNpcDefinitionIds[i]) != i)
            {
                battleSquadNpcDefinitionIds.RemoveAt(i);
            }
        }

        if (battleSquadNpcDefinitionIds.Count > MaxBattleSquadSize)
        {
            battleSquadNpcDefinitionIds.RemoveRange(MaxBattleSquadSize, battleSquadNpcDefinitionIds.Count - MaxBattleSquadSize);
        }
    }

    private static List<FacilityRuntimeState> CloneFacilityStates(List<FacilityRuntimeState> source)
    {
        List<FacilityRuntimeState> clone = new List<FacilityRuntimeState>();
        if (source == null)
            return clone;

        foreach (FacilityRuntimeState state in source)
        {
            if (state == null)
                continue;

            state.EnsureValid();
            clone.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
        }

        return clone;
    }
}
