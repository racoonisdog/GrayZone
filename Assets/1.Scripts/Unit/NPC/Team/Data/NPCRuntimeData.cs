using System;
using UnityEngine;

/// <summary>
/// GameDataManager와 셸터 씬 작업 패킷이 공통으로 사용하는 영속 캐릭터 런타임 상태입니다.
/// </summary>
/// <remarks>
/// 셸터 부상 게이지는 최대값이 건강한 방향이며, 공용 <see cref="CharacterSnapshotData"/>에서는
/// 0이 건강한 부상 심각도 방향으로 변환해 전달합니다.
/// </remarks>
public class NPCRuntimeData
{
    /// <summary>원본 NPC ScriptableObject입니다. 저장 데이터에서 복원된 경우 비어 있을 수 있습니다.</summary>
    public NPCChar NPCData { get; }

    /// <summary>현재 셸터 기준 부상 단계입니다.</summary>
    public NPCInjuryState CurrentInjuryState { get; private set; }

    /// <summary>현재 셸터 시설에서 맡고 있는 배치 역할입니다.</summary>
    public FacilityAssignmentKind AssignmentKind { get; private set; }

    private readonly string definitionId;
    private readonly NPCType npcType;
    private int maxHp;
    private float maxInjuryGauge;
    private PlayableCharacterId characterId;
    private string displayName = string.Empty;
    private int reliability;
    private WeaponSnapshotData weaponSnapshot = new();
    private float m_InjuryGauge;
    private bool IsAssignedToShelter { get; set; }
    private string AssignedRoomId { get; set; }
    private int CurrentHp { get; set; }

    /// <summary>보유 캐릭터 목록과 저장 데이터에서 사용하는 영속 캐릭터 정의 ID입니다.</summary>
    public string DefinitionId => string.IsNullOrWhiteSpace(definitionId) ? string.Empty : definitionId;

    /// <summary>셸터에서 사용하는 NPC 역할 종류입니다.</summary>
    public NPCType Type => NPCData != null ? NPCData.Type : npcType;

    /// <summary>업그레이드 결과가 반영된 현재 최대 HP입니다.</summary>
    public int MaxHp => Mathf.Max(1, maxHp);

    /// <summary>현재 HP가 0 이하인지 여부입니다.</summary>
    public bool IsDead => CurrentHp <= 0;

    /// <summary>최대값이 건강한 방향인 셸터 회복 게이지입니다.</summary>
    public float InjuryGauge => Mathf.Clamp(m_InjuryGauge, 0.0f, MaxInjuryGauge);

    /// <summary>현재 셸터 회복 게이지의 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, maxInjuryGauge);

    /// <summary>GameDataManager와 BattleSceneDataManager가 동일하게 사용하는 캐릭터·총기 스냅샷입니다.</summary>
    public CharacterSnapshotData Snapshot => CreateSnapshot();

    /// <summary>NPC 정의 데이터로 새 영속 런타임 상태를 생성합니다.</summary>
    public NPCRuntimeData(NPCChar npcData)
    {
        NPCData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        definitionId = string.IsNullOrWhiteSpace(npcData.DefinitionId)
            ? npcData.name ?? string.Empty
            : npcData.DefinitionId.Trim();
        npcType = npcData.Type;
        maxHp = npcData.MaxHP;
        maxInjuryGauge = npcData.MaxInjuryGauge;
        displayName = npcData.name ?? string.Empty;
        reliability = Mathf.Clamp(npcData.likeability, 0, 100);
        ResetToBaseState();
    }

    /// <summary>파일 저장값으로 영속 런타임 상태를 복원합니다.</summary>
    public NPCRuntimeData(
        string definitionId,
        NPCType type,
        int maxHp,
        int currentHp,
        bool isAssignedToShelter,
        string assignedRoomId,
        float injuryGauge,
        float maxInjuryGauge)
    {
        this.definitionId = definitionId?.Trim() ?? string.Empty;
        npcType = type;
        this.maxHp = Mathf.Max(1, maxHp);
        this.maxInjuryGauge = Mathf.Max(1.0f, maxInjuryGauge);
        displayName = this.definitionId;
        IsAssignedToShelter = isAssignedToShelter;
        AssignedRoomId = assignedRoomId?.Trim() ?? string.Empty;
        m_InjuryGauge = Mathf.Clamp(injuryGauge, 0.0f, this.maxInjuryGauge);
        CurrentInjuryState = NpcInjuryStateRule.FromGauge(m_InjuryGauge, this.maxInjuryGauge);
        SetCurrentHp(currentHp);
    }

    /// <summary>원본 NPC 정의값을 기준으로 HP, 부상 및 시설 배치 상태를 초기화합니다.</summary>
    public void ResetToBaseState()
    {
        CurrentHp = MaxHp;
        m_InjuryGauge = Mathf.Clamp(NPCData != null ? NPCData.InjuryGauge : m_InjuryGauge, 0.0f, MaxInjuryGauge);
        CurrentInjuryState = NpcInjuryStateRule.FromGauge(m_InjuryGauge, MaxInjuryGauge);
        ReleaseFromShelter();
    }

    /// <summary>현재 셸터 기준 부상 단계를 반환합니다.</summary>
    public NPCInjuryState GetCurrentInjuryState() => CurrentInjuryState;

    /// <summary>현재 셸터 시설에 배치되어 있는지 여부를 반환합니다.</summary>
    public bool GetIsAssignedToShelter() => IsAssignedToShelter;

    /// <summary>현재 배치된 셸터 방 ID를 반환합니다.</summary>
    public string GetAssignedRoomId() => AssignedRoomId;

    /// <summary>현재 HP를 반환합니다.</summary>
    public int GetCurrentHp() => CurrentHp;

    /// <summary>지정한 셸터 방과 역할에 캐릭터를 배치합니다.</summary>
    public bool AssignToShelter(string shelterId, string roomId, FacilityAssignmentKind kind)
    {
        if (string.IsNullOrWhiteSpace(shelterId)
            || string.IsNullOrWhiteSpace(roomId)
            || kind == FacilityAssignmentKind.None)
        {
            return false;
        }

        string normalizedRoomId = roomId.Trim();
        if (IsAssignedToShelter && AssignedRoomId == normalizedRoomId && AssignmentKind == kind)
        {
            return false;
        }

        IsAssignedToShelter = true;
        AssignedRoomId = normalizedRoomId;
        AssignmentKind = kind;
        return true;
    }

    /// <summary>현재 셸터 시설 배치를 해제합니다.</summary>
    public bool ReleaseFromShelter()
    {
        if (!IsAssignedToShelter
            && string.IsNullOrEmpty(AssignedRoomId)
            && AssignmentKind == FacilityAssignmentKind.None)
        {
            return false;
        }

        IsAssignedToShelter = false;
        AssignedRoomId = string.Empty;
        AssignmentKind = FacilityAssignmentKind.None;
        return true;
    }

    /// <summary>현재 HP를 0과 최대 HP 사이로 보정해 설정합니다.</summary>
    public bool SetCurrentHp(int value)
    {
        int clampedValue = Mathf.Clamp(value, 0, MaxHp);
        if (CurrentHp == clampedValue)
        {
            return false;
        }

        CurrentHp = clampedValue;
        return true;
    }

    /// <summary>현재 셸터 기준 부상 단계를 직접 설정합니다.</summary>
    public bool SetInjuryState(NPCInjuryState state)
    {
        if (CurrentInjuryState == state)
        {
            return false;
        }

        CurrentInjuryState = state;
        return true;
    }

    /// <summary>최대값이 건강한 방향인 셸터 회복 게이지를 설정합니다.</summary>
    public bool SetInjuryGauge(float value)
    {
        float clamped = Mathf.Clamp(value, 0.0f, MaxInjuryGauge);
        if (Mathf.Approximately(m_InjuryGauge, clamped))
        {
            return false;
        }

        m_InjuryGauge = clamped;
        return true;
    }

    /// <summary>현재 셸터 회복 게이지를 기준으로 부상 단계를 다시 계산합니다.</summary>
    /// <remarks>완치 또는 수동 해제 시점에만 호출하고 치료 진행 중 매일 호출하지 않습니다.</remarks>
    public bool RefreshInjuryStateFromGauge()
    {
        return SetInjuryState(NpcInjuryStateRule.FromGauge(m_InjuryGauge, MaxInjuryGauge));
    }

    /// <summary>HP와 부상 게이지를 최대값으로 회복합니다.</summary>
    public bool CompleteRecovery()
    {
        bool hpChanged = SetCurrentHp(MaxHp);
        bool injuryChanged = SetInjuryState(NPCInjuryState.Healthy);
        bool gaugeChanged = SetInjuryGauge(MaxInjuryGauge);
        return hpChanged || injuryChanged || gaugeChanged;
    }

    /// <summary>현재 HP에 지정한 피해량을 적용합니다.</summary>
    public bool ApplyDamage(int damage)
    {
        return damage > 0 && SetCurrentHp(CurrentHp - damage);
    }

    /// <summary>현재 HP를 지정한 값만큼 회복합니다.</summary>
    public bool RecoverHp(int amount)
    {
        return amount > 0 && SetCurrentHp(CurrentHp + amount);
    }

    /// <summary>최대 HP의 지정 비율만큼 현재 HP를 회복합니다.</summary>
    public bool ReviveToPercent(int percent)
    {
        if (percent <= 0)
        {
            return false;
        }

        return SetCurrentHp(CurrentHp + MaxHp * percent / 100);
    }

    /// <summary>현재 캐릭터·총기 상태를 공용 스냅샷 구조로 깊은 복사하여 반환합니다.</summary>
    public CharacterSnapshotData CreateSnapshot()
    {
        float injurySeverity = Mathf.Clamp(MaxInjuryGauge - InjuryGauge, 0.0f, MaxInjuryGauge);
        return new CharacterSnapshotData(
            DefinitionId,
            string.Empty,
            characterId,
            Type,
            displayName,
            reliability,
            CurrentHp,
            MaxHp,
            injurySeverity,
            MaxInjuryGauge,
            ConvertToPlayerInjuryState(CurrentInjuryState),
            false,
            IsDead,
            false,
            0,
            weaponSnapshot);
    }

    /// <summary>배틀 씬에서 반환한 공용 스냅샷을 현재 영속 캐릭터 상태에 반영합니다.</summary>
    /// <remarks>시설 배치 상태와 배틀 전용 런타임 ID, 조작 주체, 처치 수는 반영하지 않습니다.</remarks>
    public bool ApplySnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null
            || (!string.IsNullOrWhiteSpace(snapshot.DefinitionId) && snapshot.DefinitionId != DefinitionId))
        {
            return false;
        }

        maxHp = snapshot.MaxHp;
        maxInjuryGauge = snapshot.MaxInjuryGauge;
        characterId = snapshot.CharacterId;
        displayName = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? DefinitionId : snapshot.DisplayName;
        reliability = snapshot.Reliability;
        weaponSnapshot = snapshot.Weapon;
        CurrentHp = snapshot.IsCombatOut ? 0 : snapshot.CurrentHp;
        m_InjuryGauge = Mathf.Clamp(MaxInjuryGauge - snapshot.InjurySeverityGauge, 0.0f, MaxInjuryGauge);
        CurrentInjuryState = NpcInjuryStateRule.FromGauge(m_InjuryGauge, MaxInjuryGauge);
        return true;
    }

    /// <summary>셸터 장비 변경 결과로 사용할 총기 스냅샷을 깊은 복사하여 설정합니다.</summary>
    public bool SetWeaponSnapshot(WeaponSnapshotData snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        weaponSnapshot = snapshot.Clone();
        return true;
    }

    /// <summary>시설 배치 정보를 포함한 현재 런타임 데이터 전체를 깊은 복사하여 반환합니다.</summary>
    public NPCRuntimeData Clone()
    {
        return new NPCRuntimeData(
            DefinitionId,
            Type,
            MaxHp,
            CurrentHp,
            IsAssignedToShelter,
            AssignedRoomId,
            InjuryGauge,
            MaxInjuryGauge)
        {
            CurrentInjuryState = CurrentInjuryState,
            AssignmentKind = AssignmentKind,
            characterId = characterId,
            displayName = displayName,
            reliability = reliability,
            weaponSnapshot = weaponSnapshot?.Clone() ?? new WeaponSnapshotData()
        };
    }

    /// <summary>셸터 부상 단계를 배틀과 공용 스냅샷에서 사용하는 부상 단계로 변환합니다.</summary>
    private static PlayerInjuryState ConvertToPlayerInjuryState(NPCInjuryState state)
    {
        return state switch
        {
            NPCInjuryState.Healthy => PlayerInjuryState.Normal,
            NPCInjuryState.LightInjury => PlayerInjuryState.Minor,
            NPCInjuryState.HeavyInjury => PlayerInjuryState.Serious,
            NPCInjuryState.NearDeath => PlayerInjuryState.Critical,
            NPCInjuryState.Dead => PlayerInjuryState.Critical,
            _ => PlayerInjuryState.Normal
        };
    }

}
