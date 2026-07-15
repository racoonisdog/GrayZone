using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// GameDataManager에서 받은 셸터 씬 전용 작업 데이터를 소유하고 변경을 통지
/// </summary>
[DefaultExecutionOrder(-200)]
public class ShelterSceneDataManager : MonoBehaviour
{
    /// <summary>현재 셸터 데이터 매니저 싱글톤 인스턴스</summary>
    public static ShelterSceneDataManager Instance { get; private set; }

    [Header("Shelter Runtime Data")]
    [FormerlySerializedAs("shelterData")]
    [SerializeField] private ShelterRuntimeData m_runtimeData = new ShelterRuntimeData();

    /// <summary>셸터 작업 데이터가 변경됐을 때 발생</summary>
    public event Action ShelterDataChanged;

    private ShelterRuntimeData RuntimeData
    {
        get
        {
            EnsureRuntimeData();
            return m_runtimeData;
        }
    }

    private ResourceStorage Resources => RuntimeData.Resources;

    /// <summary>현재 보유 재화 수량 맵</summary>
    public IReadOnlyDictionary<CurrencyType, int> ResourceAmounts => Resources.Amounts;

    /// <summary>현재 셸터 씬의 캐릭터 작업 목록</summary>
    public IReadOnlyList<ShelterMemberRuntimeData> Characters => RuntimeData.Characters;

    public int CharacterCount => RuntimeData.CharacterCount;

    /// <summary>현재 소유한 전체 캐릭터 수</summary>
    public int TotalOwnedCharacterCount => RuntimeData.TotalOwnedCharacterCount;

    /// <summary>현재 플레이어블 캐릭터 수</summary>
    public int PlayableCharacterCount => RuntimeData.PlayableCharacterCount;

    /// <summary>현재 비플레이어 NPC 수</summary>
    public int NonPlayableNpcCount => RuntimeData.NonPlayableNpcCount;

    /// <summary>현재 전투 출격 스쿼드 캐릭터 런타임 ID 목록</summary>
    public IReadOnlyList<string> BattleSquadRuntimeIds => RuntimeData.BattleSquadRuntimeIds;

    /// <summary>현재 셸터 날짜</summary>
    public int CurrentDay => RuntimeData.CurrentDay;

    /// <summary>현재 셸터 안정도</summary>
    public int ShelterStability => RuntimeData.ShelterStability;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureRuntimeData();

        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.RegisterShelterSceneDataManager(this);
        }
        else
        {
            Debug.LogWarning("[ShelterSceneDataManager] GameDataManager.Instance is null.");
        }
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.UnregisterShelterSceneDataManager(this);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 글로벌 게임 데이터 매니저에서 셸터 씬이 사용할 작업 데이터만 복사
    /// </summary>
    public void InitializeFromGameData()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterSceneDataManager] GameDataManager.Instance is null.");
            return;
        }

        Initialize(GameDataManager.Instance.CreateShelterEntryData());
    }

    /// <summary>셸터 진입 패킷으로 씬 런타임 데이터를 초기화합니다.</summary>
    public void Initialize(ShelterEntryData entryData)
    {
        if (entryData == null)
        {
            Debug.LogWarning("[ShelterSceneDataManager] Entry data is null.");
            return;
        }

        ApplyRuntimeSnapshot(entryData.CreateRuntimeData());
    }

    /// <summary>
    /// 현재 셸터 작업 데이터를 글로벌 게임 데이터 매니저로 동기화
    /// </summary>
    /// <returns>동기화에 성공하면 <c>true</c></returns>
    public bool PushToDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterSceneDataManager] GameDataManager.Instance is null.");
            return false;
        }

        return GameDataManager.Instance.SyncFromShelter(this);
    }

    /// <summary>
    /// 셸터 전용 작업 데이터의 복제 스냅샷을 생성
    /// </summary>
    /// <returns>현재 셸터 전용 데이터 스냅샷</returns>
    public ShelterRuntimeData CreateRuntimeSnapshot()
    {
        return RuntimeData.Clone();
    }

    /// <summary>
    /// 셸터 전용 데이터 스냅샷을 현재 작업 데이터에 적용
    /// </summary>
    /// <param name="snapshot">적용할 셸터 전용 데이터 스냅샷</param>
    public void ApplyRuntimeSnapshot(ShelterRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[ShelterSceneDataManager] Snapshot is null.");
            return;
        }

        RuntimeData.CopyFrom(snapshot);
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
        FacilityRuntimeState state = RuntimeData.GetOrCreateFacilityState(facilityId, isUnlockedByDefault);
        NotifyShelterDataChanged();
        return state;
    }

    /// <summary>
    /// 기존 캐릭터 정의 에셋을 Battle 공용 스냅샷 기반 셸터 캐릭터로 영입
    /// </summary>
    /// <param name="characterDefinition">영입할 캐릭터 정의 데이터</param>
    /// <param name="character">생성된 셸터 캐릭터 런타임 데이터</param>
    /// <returns>영입에 성공하면 <c>true</c></returns>
    public bool TryRecruitCharacter(PlayableCharacterDefinition characterDefinition, out ShelterMemberRuntimeData character)
    {
        character = null;
        if (characterDefinition == null)
            return false;

        ShelterMemberRuntimeData created = new ShelterMemberRuntimeData(characterDefinition);
        bool result = RuntimeData.AddCharacter(created);
        if (!result)
            return false;

        character = created;
        NotifyShelterDataChanged();
        return true;
    }

    /// <summary>
    /// 캐릭터 런타임 ID로 셸터 캐릭터를 조회
    /// </summary>
    /// <param name="runtimeId">조회할 캐릭터 런타임 ID</param>
    /// <param name="character">조회된 셸터 캐릭터 런타임 데이터</param>
    /// <returns>캐릭터를 찾았으면 <c>true</c></returns>
    public bool TryGetCharacter(string runtimeId, out ShelterMemberRuntimeData character)
    {
        return RuntimeData.TryGetCharacter(runtimeId, out character);
    }

    /// <summary>
    /// 캐릭터를 목록에서 제거하고 관련 참조를 정리
    /// </summary>
    /// <param name="runtimeId">제거할 캐릭터 런타임 ID</param>
    /// <returns>제거에 성공하면 <c>true</c></returns>
    public bool TryRemoveCharacter(string runtimeId)
    {
        if (!RuntimeData.RemoveCharacter(runtimeId))
            return false;

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
    /// 전투 출격 스쿼드 캐릭터 목록을 한 번에 교체
    /// </summary>
    /// <param name="runtimeIds">스쿼드에 포함할 캐릭터 런타임 ID 목록</param>
    /// <returns>유효한 스쿼드로 설정됐으면 <c>true</c></returns>
    public bool TrySetBattleSquad(IEnumerable<string> runtimeIds)
    {
        if (!CanUseBattleSquad(runtimeIds))
            return false;

        bool result = RuntimeData.TrySetBattleSquad(runtimeIds);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 전투 출격 스쿼드에 캐릭터를 추가
    /// </summary>
    /// <param name="runtimeId">추가할 캐릭터 런타임 ID</param>
    /// <returns>추가됐거나 이미 포함되어 있으면 <c>true</c></returns>
    public bool TryAddBattleSquadCharacter(string runtimeId)
    {
        if (!CanUseBattleSquadCharacter(runtimeId))
            return false;

        bool result = RuntimeData.TryAddBattleSquadCharacter(runtimeId);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 전투 출격 스쿼드에서 캐릭터를 제거
    /// </summary>
    /// <param name="runtimeId">제거할 캐릭터 런타임 ID</param>
    /// <returns>제거에 성공하면 <c>true</c></returns>
    public bool TryRemoveBattleSquadCharacter(string runtimeId)
    {
        bool result = RuntimeData.TryRemoveBattleSquadCharacter(runtimeId);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    /// <summary>
    /// 전투 출격 스쿼드 목록을 모두 비움.
    /// </summary>
    public void ClearBattleSquad()
    {
        RuntimeData.ClearBattleSquad();
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 현재 셸터 날짜를 설정
    /// </summary>
    /// <param name="day">새 날짜 최소 1일로 보정</param>
    public void SetCurrentDay(int day)
    {
        RuntimeData.SetCurrentDay(day);
        NotifyShelterDataChanged();
    }

    /// <summary>
    /// 셸터 안정도를 설정
    /// </summary>
    /// <param name="stability">새 안정도 0~100으로 보정</param>
    public void SetShelterStability(int stability)
    {
        RuntimeData.SetShelterStability(stability);
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

    private bool CanUseBattleSquad(IEnumerable<string> runtimeIds)
    {
        if (runtimeIds == null)
            return false;

        foreach (string runtimeId in runtimeIds)
        {
            if (string.IsNullOrWhiteSpace(runtimeId))
                continue;

            if (!CanUseBattleSquadCharacter(runtimeId))
                return false;
        }

        return true;
    }

    private bool CanUseBattleSquadCharacter(string runtimeId)
    {
        return !string.IsNullOrWhiteSpace(runtimeId) && RuntimeData.TryGetCharacter(runtimeId, out _);
    }

    private void EnsureRuntimeData()
    {
        m_runtimeData ??= new ShelterRuntimeData();
        m_runtimeData.EnsureRuntimeContainers();
    }
}
