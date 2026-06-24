using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShelterRuntimeData
{
    public const int MinBattleSquadSize = 1;
    public const int MaxBattleSquadSize = 3;

    public SharedRuntimeData sharedData = new SharedRuntimeData();
    public int currentDay = 1;
    public List<string> battleSquadNpcRuntimeIds = new List<string>();
    public List<FacilityRuntimeState> facilityStates = new List<FacilityRuntimeState>();

    public SharedRuntimeData SharedData
    {
        get
        {
            sharedData ??= new SharedRuntimeData();
            return sharedData;
        }
    }

    public ResourceStorage Resources => SharedData.Resources;
    public NpcRoster NpcRoster => SharedData.NpcRoster;
    public int RosterCount => SharedData.RosterCount;
    public int TotalOwnedCharacterCount => SharedData.TotalOwnedCharacterCount;
    public int PlayableCharacterCount => SharedData.PlayableCharacterCount;
    public int NonPlayableNpcCount => SharedData.NonPlayableNpcCount;
    public int ShelterStability => Mathf.Clamp(SharedData.shelterStability, 0, 100);
    public IReadOnlyList<string> BattleSquadNpcRuntimeIds => battleSquadNpcRuntimeIds;

    public void EnsureRuntimeContainers()
    {
        currentDay = Mathf.Max(1, currentDay);
        battleSquadNpcRuntimeIds ??= new List<string>();
        facilityStates ??= new List<FacilityRuntimeState>();
        SharedData.EnsureRuntimeContainers();
        NormalizeBattleSquad();

        for (int i = facilityStates.Count - 1; i >= 0; i--)
        {
            if (facilityStates[i] == null)
                facilityStates.RemoveAt(i);
            else
                facilityStates[i].EnsureValid();
        }
    }

    public ShelterRuntimeData Clone()
    {
        EnsureRuntimeContainers();

        ShelterRuntimeData clone = new ShelterRuntimeData
        {
            sharedData = SharedData.Clone(),
            currentDay = currentDay,
            battleSquadNpcRuntimeIds = new List<string>(battleSquadNpcRuntimeIds),
            facilityStates = CloneFacilityStates(facilityStates)
        };

        return clone;
    }

    public void CopyFrom(ShelterRuntimeData source)
    {
        if (source == null)
            return;

        source.EnsureRuntimeContainers();
        EnsureRuntimeContainers();

        SharedData.CopyFrom(source.SharedData);
        currentDay = Mathf.Max(1, source.currentDay);
        battleSquadNpcRuntimeIds = new List<string>(source.battleSquadNpcRuntimeIds);
        NormalizeBattleSquad();
        facilityStates = CloneFacilityStates(source.facilityStates);
    }

    public void CopySharedFrom(SharedRuntimeData source)
    {
        SharedData.CopyFrom(source);
    }

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

    public bool TrySetBattleSquad(IEnumerable<string> runtimeIds)
    {
        if (runtimeIds == null)
            return false;

        List<string> normalizedIds = new List<string>();
        foreach (string runtimeId in runtimeIds)
        {
            if (string.IsNullOrWhiteSpace(runtimeId))
                continue;

            string trimmedRuntimeId = runtimeId.Trim();
            if (normalizedIds.Contains(trimmedRuntimeId))
                continue;

            if (!NpcRoster.Contains(trimmedRuntimeId))
                return false;

            normalizedIds.Add(trimmedRuntimeId);
            if (normalizedIds.Count > MaxBattleSquadSize)
                return false;
        }

        if (normalizedIds.Count < MinBattleSquadSize)
            return false;

        battleSquadNpcRuntimeIds = normalizedIds;
        return true;
    }

    public void ClearBattleSquad()
    {
        battleSquadNpcRuntimeIds.Clear();
    }

    public void RemoveNpcReferences(string runtimeId)
    {
        SharedData.RemoveNpcReferences(runtimeId);

        if (string.IsNullOrWhiteSpace(runtimeId))
            return;

        battleSquadNpcRuntimeIds.RemoveAll(id => id == runtimeId.Trim());
    }

    private void NormalizeBattleSquad()
    {
        for (int i = battleSquadNpcRuntimeIds.Count - 1; i >= 0; i--)
        {
            string runtimeId = battleSquadNpcRuntimeIds[i];
            if (string.IsNullOrWhiteSpace(runtimeId))
            {
                battleSquadNpcRuntimeIds.RemoveAt(i);
                continue;
            }

            battleSquadNpcRuntimeIds[i] = runtimeId.Trim();
        }

        for (int i = battleSquadNpcRuntimeIds.Count - 1; i >= 0; i--)
        {
            if (battleSquadNpcRuntimeIds.IndexOf(battleSquadNpcRuntimeIds[i]) != i)
            {
                battleSquadNpcRuntimeIds.RemoveAt(i);
            }
        }

        if (battleSquadNpcRuntimeIds.Count > MaxBattleSquadSize)
        {
            battleSquadNpcRuntimeIds.RemoveRange(MaxBattleSquadSize, battleSquadNpcRuntimeIds.Count - MaxBattleSquadSize);
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