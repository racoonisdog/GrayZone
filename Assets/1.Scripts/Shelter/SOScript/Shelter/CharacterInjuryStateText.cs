using UnityEngine;

/// <summary>
/// NPC 부상 상태 enum을 UI 표시용 한글 단어로 변환
/// </summary>
public static class CharacterInjuryStateText
{
    /// <summary>
    /// 부상 상태의 표시 문자열을 반환
    /// </summary>
    /// <param name="s">변환할 NPC 부상 상태</param>
    /// <returns>UI에 표시할 한글 상태명</returns>
    public static string ToWord(CharacterInjuryState s) => s switch
    {
        CharacterInjuryState.Normal => "건강",
        CharacterInjuryState.Minor => "경상",
        CharacterInjuryState.Serious => "중상",
        CharacterInjuryState.Critical => "치명상",
        _ => "알 수 없음",
    };
}
