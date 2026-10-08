using UnityEngine;

/// <summary>
/// 나린 전술 가속 스킬(<see cref="NarinTeamHasteSkill"/>)에 주입할 순수 수치 밸런스 데이터를 보관합니다.
/// </summary>
/// <remarks>
/// 필드 이름은 "{스크립트 이름}_{필드 이름}" 규칙을 따르며 BindManager가 같은 규칙으로 짝을 찾습니다.
/// 쿨타임은 기반 클래스 <see cref="CharacterSkill"/>에 선언되어 있어 접두사가 CharacterSkill입니다.
/// 가속 수치와 지속시간은 상태효과 SO(NarinTeamHaste)가 소유하므로 여기에 두지 않습니다.
/// 값의 정본은 SO이며 CSV는 내보낸 스냅샷입니다.
/// </remarks>
[CreateAssetMenu(fileName = "NarinTeamHasteSkillSO", menuName = "GrayZone/Balance/Skill/NarinTeamHasteSkillSO")]
public sealed class NarinTeamHasteSkillSO : ScriptableObject, IBalanceTableData
{
    [Header("CharacterSkill")]
    [Tooltip("스킬을 다시 사용할 수 있을 때까지의 시간(초)입니다. (0 이상)")]
    [SerializeField] private float CharacterSkill_m_cooldownDuration = 30f;
}
