using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터 씬에서 사용하는 공유 작업 데이터와 셸터 전용 작업 데이터를 소유하고 변경을 통지
/// </summary>
[DefaultExecutionOrder(-200)]
public class ShelterDataManager : MonoBehaviour
{
    /// <summary>현재 셸터 데이터 매니저 싱글톤 인스턴스</summary>
    public static ShelterDataManager Instance { get; private set; }

    [Header("Shelter Working Data")]
    [SerializeField] private SharedRuntimeData sharedWorkingData = new SharedRuntimeData();
    [SerializeField] private ShelterRuntimeData shelterData = new ShelterRuntimeData();

    /// <summary>셸터 작업 데이터가 변경됐을 때 발생</summary>
    public event Action ShelterDataChanged;

    private SharedRuntimeData SharedData
    {
        get
        {
            EnsureSharedWorkingData();
            return sharedWorkingData;
        }
    }

    private ShelterRuntimeData ShelterData
    {
        get
        {
            EnsureShelterData();
            return shelterData;
        }
    }

    private ResourceStorage Resources => SharedData.Resources;
    private NpcRoster NpcRoster => SharedData.NpcRoster;

    /// <summary>현재 보유 재화 수량 맵</summary>
    public IReadOnlyDictionary<CurrencyType, int> ResourceAmounts => Resources.Amounts;

    /// <summary>현재 셸터 작업 데이터의 NPC 목록</summary>
    public IReadOnlyList<NPCRuntimeData> Npcs => NpcRoster.All;

    /// <summary>현재 NPC 로스터 수</summary>
    public int RosterCount => SharedData.RosterCount;

    /// <summary>현재 소유한 전체 캐릭터 수</summary>
    public int TotalOwnedCharacterCount => SharedData.TotalOwnedCharacterCount;

    /// <summary>현재 플레이어블 캐릭터 수</summary>
    public int PlayableCharacterCount => SharedData.PlayableCharacterCount;

    /// <summary>현재 비플레이어 NPC 수</summary>
    public int NonPlayableNpcCount => SharedData.NonPlayableNpcCount;

    /// <summary>현재 전투 출격 스쿼드 NPC 정의 ID 목록</summary>
    public IReadOnlyList<string> BattleSquadNpcDefinitionIds => ShelterData.BattleSquadNpcDefinitionIds;

    /// <summary>현재 셸터 날짜</summary>
    public int CurrentDay => ShelterData.CurrentDay;

    /// <summary>현재 셸터 안정도</summary>
    public int ShelterStability => SharedData.ShelterStability;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureSharedWorkingData();
        EnsureShelterData();

        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.RegisterShelterDataManager(this);
        }
        else
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
        }
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.UnregisterShelterDataManager(this);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 글로벌 게임 데이터 매니저의 공유 데이터 스냅샷만 셸터 작업 데이터로 복사
    /// </summary>
    public void CopySharedDataFromGameDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return;
        }

        ApplySharedSnapshot(GameDataManager.Instance.CreateSharedSnapshot());
    }

    /// <summary>
    /// 글로벌 게임 데이터 매니저의 공유 데이터와 셸터 전용 데이터 스냅샷을 모두 복사
    /// </summary>
    public void CopyFromDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return;
        }

        ApplySharedSnapshot(GameDataManager.Instance.CreateSharedSnapshot());
        ApplyShelterSnapshot(GameDataManager.Instance.CreateShelterSnapshot());
    }

    /// <summary>
    /// 현재 셸터 작업 데이터를 글로벌 게임 데이터 매니저로 동기화
    /// </summary>
    /// <returns>동기화에 성공하면 <c>true</c></returns>
    public bool PushToDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return false;
        }

        return GameDataManager.Instance.SyncFromShelter(this);
    }

    /// <summary>
    /// 셸터 전용 작업 데이터의 복제 스냅샷을 생성
    /// </summary>
    /// <returns>현재 셸터 전용 데이터 스냅샷</returns>
    public ShelterRuntimeData CreateShelterSnapshot()
    {
        return ShelterData.Clone();
    }

    /// <summary>
    /// 공유 작업 데이터의 복제 스냅샷을 생성
    /// </summary>
    /// <returns>현재 공유 데이터 스냅샷</returns>
    public SharedRuntimeData CreateSharedSnapshot()
    {
        return SharedData.Clone();
    }

    /// <summary>
    /// 셸터 전용 데이터 스냅샷을 현재 작업 데이터에 적용
    /// </summary>
    /// <param name="snapshot">적용할 셸터 전용 데이터 스냅샷</param>
    public void ApplyShelterSnapshot(ShelterRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[ShelterDataManager] Snapshot is null.");
            return;
        }

        ShelterData.CopyFrom(snapshot);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 공유 데이터 스냅샷을 현재 작업 데이터에 적용
    /// </summary>
    /// <param name="snapshot">적용할 공유 데이터 스냅샷</param>
    public void ApplySharedSnapshot(SharedRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[ShelterDataManager] Shared snapshot is null.");
            return;
        }

        SharedData.CopyFrom(snapshot);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 시설 상태를 조회하거나 없으면 기본 해금 상태로 생성
    /// </summary>
    /// <param name="facilityId">조회할 시설 ID</param>
    /// <param name="isUnlockedByDefault">새로 만들 때 적용할 기본 해금 여부</param>
    /// <returns>시설 런타임 상태 시설 ID가 비어 있으면 <c>null</c></returns>
    public FacilityRuntimeState GetOrCreateFacilityState(string facilityId, bool isUnlockedByDefault)
    {
        FacilityRuntimeState state = ShelterData.GetOrCreateFacilityState(facilityId, isUnlockedByDefault);
        NotifyShelterDataChanged();
        return state;
    }

    /// <summary>
    /// NPC 정의 데이터를 셸터 로스터에 런타임 NPC로 영입
    /// </summary>
    /// <param name="npcData">영입할 NPC 정의 데이터</param>
    /// <param name="runtimeNpc">생성되거나 기존에 매칭된 런타임 NPC</param>
    /// <returns>영입에 성공하면 <c>true</c></returns>
    public bool TryRecruitNpc(NPCChar npcData, out NPCRuntimeData runtimeNpc)
    {
        bool result = NpcRoster.TryAdd(npcData, out runtimeNpc);
        if (result)
        {
            SharedData.RefreshCountsFromRosterAsPlayable();
            NotifyShelterDataChanged();
        }

        return result;
    }

    /// <summary>
    /// NPC 정의 ID로 런타임 NPC를 조회
    /// </summary>
    /// <param name="definitionId">조회할 NPC 정의 ID</param>
    /// <param name="runtimeNpc">조회된 런타임 NPC</param>
    /// <returns>NPC를 찾았으면 <c>true</c></returns>
    public bool TryGetNpc(string definitionId, out NPCRuntimeData runtimeNpc)
    {
        return NpcRoster.TryGet(definitionId, out runtimeNpc);
    }

    /// <summary>
    /// NPC를 로스터에서 제거하고 관련 참조를 정리
    /// </summary>
    /// <param name="definitionId">제거할 NPC 정의 ID</param>
    /// <returns>제거에 성공하면 <c>true</c></returns>
    public bool TryRemoveNpc(string definitionId)
    {
        if (!NpcRoster.Remove(definitionId))
        {
            return false;
        }

        SharedData.RemoveNpcReferences(definitionId);
        ShelterData.RemoveNpcReferences(definitionId);
        SharedData.RefreshCountsFromRosterAsPlayable();
        NotifyShelterDataChanged();
        return true;
    }

    /// <summary>
    /// 지정한 재화의 현재 보유량을 반환
    /// </summary>
    /// <param name="type">조회할 재화 타입</param>
    /// <returns>현재 보유량</returns>
    public int GetResourceAmount(CurrencyType type)
    {
        return Resources.GetAmount(type);
    }

    /// <summary>
    /// 단일 재화 비용을 지불할 수 있는지 검사
    /// </summary>
    /// <param name="cost">검사할 재화 비용</param>
    /// <returns>지불 가능하면 <c>true</c></returns>
    public bool CanSpendResource(CurrencyCost cost)
    {
        return Resources.CanSpend(cost);
    }

    /// <summary>
    /// 비용 묶음 전체를 지불할 수 있는지 검사
    /// </summary>
    /// <param name="costBundle">검사할 비용 묶음</param>
    /// <returns>모든 비용을 지불 가능하면 <c>true</c></returns>
    public bool CanSpendResources(CostBundle costBundle)
    {
        if (costBundle == null || costBundle.IsFree)
            return true;

        foreach (CurrencyCost cost in costBundle.Costs)
        {
            if (!CanSpendResource(cost))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 단일 재화 비용을 차감
    /// </summary>
    /// <param name="cost">차감할 재화 비용</param>
    /// <returns>차감에 성공하면 <c>true</c></returns>
    public bool TrySpendResource(CurrencyCost cost)
    {
        bool result = Resources.TrySpend(cost);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 비용 묶음 전체를 차감
    /// </summary>
    /// <param name="costBundle">차감할 비용 묶음</param>
    /// <returns>모든 비용 차감에 성공하면 <c>true</c></returns>
    public bool TrySpendResources(CostBundle costBundle)
    {
        if (!CanSpendResources(costBundle))
            return false;

        if (costBundle == null || costBundle.IsFree)
            return true;

        foreach (CurrencyCost cost in costBundle.Costs)
        {
            Resources.TrySpend(cost);
        }

        NotifyShelterDataChanged();
        return true;
    }

    /// <summary>
    /// 지정한 재화를 추가
    /// </summary>
    /// <param name="type">추가할 재화 타입</param>
    /// <param name="amount">추가할 수량</param>
    /// <returns>추가에 성공하면 <c>true</c></returns>
    public bool TryAddResource(CurrencyType type, int amount)
    {
        bool result = Resources.Add(type, amount);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 지정한 재화 보유량을 직접 설정
    /// </summary>
    /// <param name="type">설정할 재화 타입</param>
    /// <param name="amount">새 보유량</param>
    public void SetResourceAmount(CurrencyType type, int amount)
    {
        Resources.SetAmount(type, amount);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 전투 출격 스쿼드 NPC 목록을 한 번에 교체
    /// </summary>
    /// <param name="definitionIds">스쿼드에 포함할 NPC 정의 ID 목록</param>
    /// <returns>유효한 스쿼드로 설정됐으면 <c>true</c></returns>
    public bool TrySetBattleSquad(IEnumerable<string> definitionIds)
    {
        if (!CanUseBattleSquad(definitionIds))
            return false;

        bool result = ShelterData.TrySetBattleSquad(definitionIds);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 전투 출격 스쿼드에 NPC를 추가
    /// </summary>
    /// <param name="definitionId">추가할 NPC 정의 ID</param>
    /// <returns>추가됐거나 이미 포함되어 있으면 <c>true</c></returns>
    public bool TryAddBattleSquadNpc(string definitionId)
    {
        if (!CanUseBattleSquadNpc(definitionId))
            return false;

        bool result = ShelterData.TryAddBattleSquadNpc(definitionId);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 전투 출격 스쿼드에서 NPC를 제거
    /// </summary>
    /// <param name="definitionId">제거할 NPC 정의 ID</param>
    /// <returns>제거에 성공하면 <c>true</c></returns>
    public bool TryRemoveBattleSquadNpc(string definitionId)
    {
        bool result = ShelterData.TryRemoveBattleSquadNpc(definitionId);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 전투 출격 스쿼드 목록을 모두 비움.
    /// </summary>
    public void ClearBattleSquad()
    {
        ShelterData.ClearBattleSquad();
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 소유 캐릭터 수 집계를 직접 설정
    /// </summary>
    /// <param name="playableCount">플레이어블 캐릭터 수</param>
    /// <param name="nonPlayableNpcCount">비플레이어 NPC 수</param>
    public void SetOwnedCharacterCounts(int playableCount, int nonPlayableNpcCount)
    {
        SharedData.SetOwnedCharacterCounts(playableCount, nonPlayableNpcCount);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 현재 로스터를 기준으로 플레이어블 캐릭터 수 집계를 다시 계산
    /// </summary>
    public void RefreshCountsFromRosterAsPlayable()
    {
        SharedData.RefreshCountsFromRosterAsPlayable();
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 마지막으로 진입한 스테이지 ID를 설정
    /// </summary>
    /// <param name="stageId">새 마지막 스테이지 ID</param>
    public void SetLastStageId(string stageId)
    {
        SharedData.SetLastStageId(stageId);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 현재 셸터 날짜를 설정
    /// </summary>
    /// <param name="day">새 날짜 최소 1일로 보정</param>
    public void SetCurrentDay(int day)
    {
        ShelterData.SetCurrentDay(day);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 셸터 안정도를 설정
    /// </summary>
    /// <param name="stability">새 안정도 0~100으로 보정</param>
    public void SetShelterStability(int stability)
    {
        SharedData.SetShelterStability(stability);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 외부 직접 상태 변경 알림
    /// </summary>
    public void MarkDirty()
    {
        NotifyShelterDataChanged();
    }

    private void NotifyShelterDataChanged()
    {
        ShelterDataChanged?.Invoke();
    }

    private bool CanUseBattleSquad(IEnumerable<string> definitionIds)
    {
        if (definitionIds == null)
            return false;

        foreach (string definitionId in definitionIds)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                continue;

            if (!CanUseBattleSquadNpc(definitionId))
                return false;
        }

        return true;
    }

    private bool CanUseBattleSquadNpc(string definitionId)
    {
        return !string.IsNullOrWhiteSpace(definitionId) && NpcRoster.Contains(definitionId);
    }

    private void EnsureSharedWorkingData()
    {
        sharedWorkingData ??= new SharedRuntimeData();
        sharedWorkingData.EnsureRuntimeContainers();
    }

    private void EnsureShelterData()
    {
        shelterData ??= new ShelterRuntimeData();
        shelterData.EnsureRuntimeContainers();
    }
}
