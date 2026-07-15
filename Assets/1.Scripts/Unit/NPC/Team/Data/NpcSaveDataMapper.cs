/// <summary>NPC 런타임 데이터와 파일 저장 구조 사이의 변환을 담당합니다.</summary>
public static class NpcSaveDataMapper
{
    /// <summary>NPC 정의 데이터의 초기 상태를 파일 저장 구조로 변환합니다.</summary>
    public static SaveData.NpcSaveData FromNpcChar(NPCChar npcChar)
    {
        if (npcChar == null)
        {
            return null;
        }

        return FromRuntime(new NPCRuntimeData(npcChar));
    }

    /// <summary>공용 NPC 런타임 상태를 공용 캐릭터·총기 스냅샷이 포함된 파일 저장 구조로 변환합니다.</summary>
    public static SaveData.NpcSaveData FromRuntime(NPCRuntimeData runtimeData)
    {
        if (runtimeData == null)
        {
            return null;
        }

        return new SaveData.NpcSaveData
        {
            characterSnapshot = runtimeData.Snapshot,
            definitionId = runtimeData.DefinitionId,
            type = runtimeData.Type,
            maxHp = runtimeData.MaxHp,
            currentHp = runtimeData.GetCurrentHp(),
            injuryGauge = runtimeData.InjuryGauge,
            maxInjuryGauge = runtimeData.MaxInjuryGauge,
            isAssignedToShelter = runtimeData.GetIsAssignedToShelter(),
            assignedRoomId = runtimeData.GetAssignedRoomId()
        };
    }

    /// <summary>파일 저장 구조에서 공용 NPC 런타임 상태를 복원합니다.</summary>
    /// <remarks>schemaVersion 4 이하 데이터는 기존 평면 필드로 복원하고, 새 스냅샷이 있으면 그 값을 우선 적용합니다.</remarks>
    public static NPCRuntimeData ToRuntime(SaveData.NpcSaveData saveData)
    {
        if (saveData == null)
        {
            return null;
        }

        NPCRuntimeData runtimeData = new NPCRuntimeData(
            saveData.definitionId,
            saveData.type,
            saveData.maxHp,
            saveData.currentHp,
            saveData.isAssignedToShelter,
            saveData.assignedRoomId,
            saveData.injuryGauge,
            saveData.maxInjuryGauge
            );

        CharacterSnapshotData snapshot = saveData.characterSnapshot;
        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            runtimeData.ApplySnapshot(snapshot);
        }

        return runtimeData;
    }
}
