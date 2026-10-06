using UnityEngine;

/// <summary>
/// 청솔 화염 탄환 스킬(<see cref="ChungSolDragonBreathSkill"/>)과 그 용숨결 판정(<see cref="DragonBreathEffect"/>)에
/// 주입할 순수 수치 밸런스 데이터를 보관합니다.
/// </summary>
/// <remarks>
/// 필드 이름은 "{스크립트 이름}_{필드 이름}" 규칙을 따르며 BindManager가 같은 규칙으로 짝을 찾습니다.
/// 스킬 컴포넌트가 이 SO를 자기 자신과 용숨결 효과 인스턴스에 각각 주입합니다. 접두사가 받는 타입 이름이라
/// 한 SO에 두 컴포넌트 값이 있어도 겹치지 않습니다.
/// 시각 효과 프리팹, 화상 상태효과, 피격 레이어 같은 참조와 표현 값은 담지 않습니다(효과 프리팹이 소유).
/// 값의 정본은 SO이며 CSV는 내보낸 스냅샷입니다.
/// </remarks>
[CreateAssetMenu(fileName = "ChungSolDragonBreathSkillSO", menuName = "GrayZone/Balance/Skill/ChungSolDragonBreathSkillSO")]
public sealed class ChungSolDragonBreathSkillSO : ScriptableObject, IBalanceTableData
{
    // ───────────── 스킬 ─────────────

    [Header("CharacterSkill")]
    [Tooltip("스킬을 다시 사용할 수 있을 때까지의 시간(초)입니다. (0 이상)")]
    [SerializeField] private float CharacterSkill_m_cooldownDuration = 30f;

    [Header("ChungSolDragonBreathSkill")]
    [Tooltip("용숨결탄 전용 재장전 속도 배율입니다. 2이면 일반 재장전보다 2배 빠르게 모션과 장전이 완료됩니다. (0.01 이상)")]
    [SerializeField] private float ChungSolDragonBreathSkill_m_reloadSpeedMultiplier = 1f;

    // ───────────── 용숨결 판정 ─────────────

    [Header("DragonBreathEffect")]
    [Tooltip("기본 피해입니다. 거리 구간 표가 비어 있거나 배율 모드일 때 기준으로 씁니다. (1 이상)")]
    [SerializeField] private int DragonBreathEffect_m_damage = 10;

    [Tooltip("총구에서의 거리에 따른 구간별 피해 표입니다. 원뿔 피격은 총구→대상 거리, 착탄 범위 피격은 총구→착탄 지점 거리로 구간을 고릅니다.")]
    [SerializeField] private DamageFalloffTable DragonBreathEffect_m_damageFalloff = new DamageFalloffTable();

    [Tooltip("원뿔 판정 길이(m)이자 착탄 지점을 찾는 거리입니다. 지형(벽)에 막히면 그 앞에서 끝납니다. (0.01 이상)")]
    [SerializeField] private float DragonBreathEffect_m_damageRange = 17.5f;

    [Tooltip("총구 쪽 원뿔 판정 반지름(m)입니다. (0.01 이상)")]
    [SerializeField] private float DragonBreathEffect_m_damageStartRadius = 0.3f;

    [Tooltip("판정 길이 끝 지점의 원뿔 반지름(m)입니다. (0.01 이상)")]
    [SerializeField] private float DragonBreathEffect_m_damageEndRadius = 1.5f;

    [Tooltip("가까운 착탄의 착탄 범위 피해 반경(m)입니다. 멀리서 맞으면 아래 배율로 커집니다. 0이면 범위 피해 없음. (0 이상)")]
    [SerializeField] private float DragonBreathEffect_m_impactDamageRadius = 1.7f;

    [Tooltip("착탄 효과·착탄 범위가 커지기 시작하는 거리(m)입니다. (0 이상)")]
    [SerializeField] private float DragonBreathEffect_m_impactScaleStartDistance = 6f;

    [Tooltip("최대 거리(판정 길이)에서 착탄 효과·착탄 범위 반경의 배율입니다. (0.1 이상)")]
    [SerializeField] private float DragonBreathEffect_m_impactFarScale = 1.6f;
}
