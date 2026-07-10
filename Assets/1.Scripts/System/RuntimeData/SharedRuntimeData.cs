using System;
using UnityEngine;

/// <summary>
/// 씬 사이에서 공유되는 런타임 기준 데이터를 담는 직렬화 가능한 데이터 모델
/// </summary>
[Serializable]
public class SharedRuntimeData
{
    [SerializeField] private string lastStageId = string.Empty;
    [SerializeField] private int shelterStability = 100;
    [SerializeField] private int playableCharacterCount;
    [SerializeField] private int npcCount;

    private ResourceStorage resources;
    private NpcRoster npcRoster;

    /// <summary>마지막으로 진입한 스테이지 ID</summary>
    public string LastStageId => lastStageId ?? string.Empty;

    /// <summary>현재 셸터 안정도</summary>
    public int ShelterStability => Mathf.Clamp(shelterStability, 0, 100);

    /// <summary>현재 NPC 로스터 수</summary>
    public int RosterCount => NpcRoster.Count;

    /// <summary>플레이어블 캐릭터와 비플레이어 NPC를 합친 전체 소유 캐릭터 수</summary>
    public int TotalOwnedCharacterCount => PlayableCharacterCount + NonPlayableNpcCount;

    /// <summary>현재 플레이어블 캐릭터 수</summary>
    public int PlayableCharacterCount => Mathf.Max(0, playableCharacterCount);

    /// <summary>현재 비플레이어 NPC 수</summary>
    public int NonPlayableNpcCount => Mathf.Max(0, npcCount);

    /// <summary>공유 재화 저장소</summary>
    public ResourceStorage Resources
    {
        get
        {
            resources ??= new ResourceStorage();
            return resources;
        }
    }

    /// <summary>공유 NPC 로스터</summary>
    public NpcRoster NpcRoster
    {
        get
        {
            npcRoster ??= new NpcRoster();
            return npcRoster;
        }
    }

    /// <summary>
    /// 런타임 컨테이너를 생성하고 저장 데이터에서 들어올 수 있는 잘못된 값을 보정
    /// </summary>
    public void EnsureRuntimeContainers()
    {
        lastStageId ??= string.Empty;
        shelterStability = Mathf.Clamp(shelterStability, 0, 100);
        playableCharacterCount = Mathf.Max(0, playableCharacterCount);
        npcCount = Mathf.Max(0, npcCount);

        _ = Resources;
        _ = NpcRoster;
    }

    /// <summary>
    /// 현재 공유 데이터를 깊은 복사
    /// </summary>
    /// <returns>현재 값으로 생성된 새 <see cref="SharedRuntimeData"/></returns>
    public SharedRuntimeData Clone()
    {
        EnsureRuntimeContainers();

        SharedRuntimeData clone = new SharedRuntimeData
        {
            lastStageId = LastStageId,
            shelterStability = ShelterStability,
            playableCharacterCount = PlayableCharacterCount,
            npcCount = NonPlayableNpcCount
        };

        clone.Resources.CopyFrom(Resources);
        foreach (NPCRuntimeData npc in NpcRoster.All)
        {
            clone.NpcRoster.Add(npc?.Clone());
        }

        return clone;
    }

    /// <summary>
    /// 다른 공유 런타임 데이터의 값을 현재 인스턴스에 복사
    /// </summary>
    /// <param name="source">복사할 원본 공유 데이터</param>
    public void CopyFrom(SharedRuntimeData source)
    {
        if (source == null)
            return;

        source.EnsureRuntimeContainers();
        EnsureRuntimeContainers();

        SetLastStageId(source.LastStageId);
        SetShelterStability(source.ShelterStability);
        SetOwnedCharacterCounts(source.PlayableCharacterCount, source.NonPlayableNpcCount);

        Resources.CopyFrom(source.Resources);

        NpcRoster.Clear();
        foreach (NPCRuntimeData npc in source.NpcRoster.All)
        {
            NpcRoster.Add(npc?.Clone());
        }
    }

    /// <summary>
    /// 마지막으로 진입한 스테이지 ID를 설정
    /// </summary>
    /// <param name="stageId">새 스테이지 ID</param>
    public void SetLastStageId(string stageId)
    {
        lastStageId = stageId ?? string.Empty;
    }

    /// <summary>
    /// 셸터 안정도를 설정
    /// </summary>
    /// <param name="stability">새 안정도 0~100으로 보정</param>
    public void SetShelterStability(int stability)
    {
        shelterStability = Mathf.Clamp(stability, 0, 100);
    }

    /// <summary>
    /// 소유 캐릭터 수 집계를 설정
    /// </summary>
    /// <param name="playableCount">플레이어블 캐릭터 수</param>
    /// <param name="nonPlayableNpcCount">비플레이어 NPC 수</param>
    public void SetOwnedCharacterCounts(int playableCount, int nonPlayableNpcCount)
    {
        playableCharacterCount = Mathf.Max(0, playableCount);
        npcCount = Mathf.Max(0, nonPlayableNpcCount);
    }

    /// <summary>
    /// 현재 로스터 수를 플레이어블 캐릭터 수로 반영하고 비플레이어 NPC 수는 0으로 설정
    /// </summary>
    public void RefreshCountsFromRosterAsPlayable()
    {
        SetOwnedCharacterCounts(NpcRoster.Count, 0);
    }

    /// <summary>
    /// NPC 제거 시 공유 데이터 안의 해당 NPC 참조를 정리
    /// </summary>
    /// <param name="definitionId">정리할 NPC 정의 ID</param>
    public void RemoveNpcReferences(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return;
    }
}
