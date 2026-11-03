using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public const int CurrentSchemaVersion = 14;

    public int schemaVersion = CurrentSchemaVersion;
    public ShelterCheckpointId shelterCheckpointId = ShelterCheckpointId.BeforeDefense1;

    /// <summary>방어전 클리어 횟수입니다. 다음 방어전 회차(클리어 횟수 + 1)와 일차 표시의 기준입니다.</summary>
    public int defenseClearCount;

    /// <summary>셸터 안내 진행 단계입니다.</summary>
    public ShelterFlowState shelterFlowState = ShelterFlowState.NotStarted;

    public int trapUpgradeLevel;
    public int spikeUpgradeLevel;
    public int explosiveUpgradeLevel;
    public int shooterUpgradeLevel;
    public int wireUpgradeLevel;

    /// <summary>일반 시설(FacilityManager 관리)의 해금·업그레이드 레벨입니다. 비어 있으면 각 시설 기본값으로 시작합니다.</summary>
    public List<FacilityRuntimeState> facilities = new List<FacilityRuntimeState>();

    public List<ResourceAmountData> resources = new List<ResourceAmountData>();
    public List<CharacterStateSaveData> characters = new List<CharacterStateSaveData>();

    [Serializable]
    public class ResourceAmountData
    {
        public string resourceId = string.Empty;
        public int amount;
    }

    [Serializable]
    public class CharacterStateSaveData
    {
        public PlayerbleCharacterId characterId;
        public int currentHp;
        public int maxHp = 1;
        public float injurySeverityGauge;
        public float maxInjuryGauge = 100.0f;
        public CharacterInjuryState injuryState;
        public bool isDown;
        public bool isCombatOut;
    }
}
