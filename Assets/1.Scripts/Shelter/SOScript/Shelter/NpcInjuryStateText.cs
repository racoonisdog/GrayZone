using UnityEngine;

/// <summary>
/// NPC 부상 상태 enum을 UI 표시용 한글 단어로 변환
/// </summary>
public static class NpcInjuryStateText
{
    /// <summary>
    /// 부상 상태의 표시 문자열을 반환
    /// </summary>
    /// <param name="s">변환할 NPC 부상 상태</param>
    /// <returns>UI에 표시할 한글 상태명</returns>
    public static string ToWord(NPCInjuryState s) => s switch
    {
        NPCInjuryState.Healthy => "건강",
        NPCInjuryState.LightInjury => "경상",
        NPCInjuryState.HeavyInjury => "중상",
        NPCInjuryState.NearDeath => "치명상",
    };
}
