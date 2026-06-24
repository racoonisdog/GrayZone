/// <summary>
/// 유닛의 전투 진영입니다.
/// </summary>
/// <remarks>
/// 각 상수값은 ProjectSettings의 레이어 번호와 동일하게 맞춰져 있습니다
/// (Player=8, Enemy=9, NPC=10). 덕분에 <see cref="HealthSystemBase"/>는
/// 진영이 <see cref="None"/>일 때 <c>gameObject.layer</c> 값을 그대로 진영으로 환산할 수 있습니다.
/// 레이어 번호를 바꾸면 이 값도 함께 맞춰야 합니다.
/// </remarks>
public enum Faction
{
    /// <summary>미지정입니다. 이 경우 레이어 번호에서 진영을 추론합니다.</summary>
    None = 0,

    /// <summary>플레이어/아군 진영입니다. (레이어 8)</summary>
    Player = 8,

    /// <summary>적 진영입니다. (레이어 9)</summary>
    Enemy = 9,

    /// <summary>중립 NPC 진영입니다. (레이어 10)</summary>
    NPC = 10,
}
