using UnityEngine;

/// <summary>
/// 서하 응급 치료 스킬(<see cref="SeoHaTeamHealSkill"/>)에 주입할 순수 수치 밸런스 데이터를 보관합니다.
/// </summary>
/// <remarks>
/// 필드 이름은 "{스크립트 이름}_{필드 이름}" 규칙을 따르며 BindManager가 같은 규칙으로 짝을 찾습니다.
/// 쿨타임은 기반 클래스 <see cref="CharacterSkill"/>에 선언되어 있어 접두사가 CharacterSkill입니다.
/// 값의 정본은 SO이며 CSV는 내보낸 스냅샷입니다. 허용 범위는 스크립트 쪽 특성([Range], [Min])을 따릅니다.
/// </remarks>
[CreateAssetMenu(fileName = "SeoHaTeamHealSkillSO", menuName = "GrayZone/Balance/Skill/SeoHaTeamHealSkillSO")]
public sealed class SeoHaTeamHealSkillSO : ScriptableObject, IBalanceTableData
{
    [Header("CharacterSkill")]
    [Tooltip("스킬을 다시 사용할 수 있을 때까지의 시간(초)입니다. (0 이상)")]
    [SerializeField] private float CharacterSkill_m_cooldownDuration = 30f;

    [Header("SeoHaTeamHealSkill")]
    [Tooltip("각 팀원의 최대 HP에서 회복할 비율(%)입니다. 30이면 최대 HP의 30%를 회복합니다. (범위 1~100)")]
    [SerializeField] private float SeoHaTeamHealSkill_m_healPercent = 30f;

    [Tooltip("다운된 대상을 스킬로 원격 구조했을 때 부여할 체력입니다. 일반 전체 회복량과 별도입니다. (1 이상)")]
    [SerializeField] private int SeoHaTeamHealSkill_m_skillReviveHp = 20;
}
