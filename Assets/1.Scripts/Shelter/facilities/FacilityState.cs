/// <summary>
/// 시설 정의와 해당 시설의 런타임 상태를 함께 묶는 읽기 모델
/// </summary>
public sealed class FacilityState
{
    /// <summary>시설의 정적 정의 데이터</summary>
    public FacilityDefinition Definition { get; }

    /// <summary>저장 대상이 되는 시설 런타임 상태</summary>
    public FacilityRuntimeState RuntimeState { get; }

    /// <summary>시설 고정 식별자</summary>
    public string FacilityId => RuntimeState.facilityId;

    /// <summary>시설 해금 여부</summary>
    public bool IsUnlocked => RuntimeState.isUnlocked;

    /// <summary>시설 업그레이드 레벨</summary>
    public int UpgradeLevel => RuntimeState.upgradeLevel;

    /// <summary>
    /// 시설 정의와 런타임 상태를 연결하고 식별자/레벨을 보정
    /// </summary>
    /// <param name="definition">시설 정적 정의</param>
    /// <param name="runtimeState">시설 런타임 상태</param>
    public FacilityState(FacilityDefinition definition, FacilityRuntimeState runtimeState)
    {
        if (definition == null)
            throw new System.ArgumentNullException(nameof(definition));

        if (runtimeState == null)
            throw new System.ArgumentNullException(nameof(runtimeState));

        Definition = definition;
        RuntimeState = runtimeState;

        if (string.IsNullOrWhiteSpace(RuntimeState.facilityId))
            RuntimeState.facilityId = definition.FacilityId ?? string.Empty;

        RuntimeState.EnsureValid(definition.FacilityId);
    }

    /// <summary>시설을 해금 상태로 변경</summary>
    public void Unlock() => RuntimeState.isUnlocked = true;

    /// <summary>
    /// 시설 업그레이드 레벨을 설정
    /// </summary>
    /// <param name="level">적용할 업그레이드 레벨 음수는 0으로 보정</param>
    public void SetUpgradeLevel(int level)
    {
        RuntimeState.upgradeLevel = System.Math.Max(0, level);
    }
}
