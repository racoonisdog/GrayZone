/// <summary>schemaVersion 6 이하 NPC 저장 구조를 신규 캐릭터 데이터로 변환합니다.</summary>
public static class LegacyNpcSaveDataMapper
{
    /// <summary>NPC 정의 데이터의 초기 상태를 파일 저장 구조로 변환합니다.</summary>
    public static SaveData.NpcSaveData FromDefinition(PlayerbleCharacterDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        return FromRuntime(new ShelterMemberRuntimeData(definition));
    }

    /// <summary>공용 NPC 런타임 상태를 공용 캐릭터·총기 스냅샷이 포함된 파일 저장 구조로 변환합니다.</summary>
    public static SaveData.NpcSaveData FromRuntime(ShelterMemberRuntimeData runtimeData)
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
            currentHp = runtimeData.CurrentHp,
            // schemaVersion 4 이하 호환 필드는 최대값이 건강한 기존 회복 게이지 의미를 유지합니다.
            injuryGauge = runtimeData.MaxInjuryGauge - runtimeData.InjuryGauge,
            maxInjuryGauge = runtimeData.MaxInjuryGauge,
            isAssignedToShelter = runtimeData.IsAssignedToFacility,
            assignedRoomId = runtimeData.AssignedRoomId
        };
    }

    /// <summary>파일 저장 구조에서 공용 NPC 런타임 상태를 복원합니다.</summary>
    /// <remarks>schemaVersion 4 이하 데이터는 기존 평면 필드로 복원하고, 새 스냅샷이 있으면 그 값을 우선 적용합니다.</remarks>
    public static ShelterMemberRuntimeData ToRuntime(SaveData.NpcSaveData saveData)
    {
        if (saveData == null)
        {
            return null;
        }

        float maxInjuryGauge = saveData.maxInjuryGauge > 0.0f ? saveData.maxInjuryGauge : 100.0f;
        float injurySeverityGauge = UnityEngine.Mathf.Clamp(
            maxInjuryGauge - saveData.injuryGauge,
            0.0f,
            maxInjuryGauge);

        ShelterMemberRuntimeData runtimeData = new ShelterMemberRuntimeData(
            saveData.definitionId,
            saveData.type,
            saveData.maxHp,
            saveData.currentHp,
            saveData.isAssignedToShelter,
            saveData.assignedRoomId,
            injurySeverityGauge,
            maxInjuryGauge
            );

        CharacterSnapshotData snapshot = saveData.characterSnapshot;
        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            runtimeData.ApplySnapshot(snapshot);
        }

        return runtimeData;
    }
}
