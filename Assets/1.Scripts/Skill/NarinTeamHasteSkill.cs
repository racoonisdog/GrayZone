using System.Collections;
using UnityEngine;

/// <summary>일정 시간 동안 전 스쿼드의 이동·행동 속도를 높이는 나린의 스킬입니다.</summary>
public sealed class NarinTeamHasteSkill : CharacterSkill
{
    private const string HasteResourcePath = "StatusEffects/NarinTeamHaste";

    [Tooltip("전 스쿼드에 적용할 이동·행동속도 상태효과입니다. 비어 있으면 Resources 기본 에셋을 사용합니다.")]
    [SerializeField] private StatusEffectDefinitionSO m_hasteEffect;

    private Coroutine m_effectRoutine;
    private float m_effectEndTime;

    public override CharacterSkillType SkillType => CharacterSkillType.NarinTeamHaste;
    public override string DisplayName => "전술 가속";
    public override string Description => "팀원 전체의 이동속도와 행동속도를 증가시킵니다";
    public override bool IsActive => m_effectRoutine != null;
    public override float ActiveDuration => ResolveHasteEffect() != null ? ResolveHasteEffect().Duration : 0.0f;
    public override float ActiveRemaining => IsActive ? Mathf.Max(0.0f, m_effectEndTime - Time.time) : 0.0f;
    public override string HudDetailText => IsActive
        ? $"지속 시간 {ActiveRemaining:0.0} / {ActiveDuration:0.0}초"
        : $"지속 시간 {ActiveDuration:0.0}초";

    protected override bool ActivateSkill(SquadManager squadManager)
    {
        StatusEffectDefinitionSO effect = ResolveHasteEffect();
        if (effect == null || squadManager.SquadMembers == null || squadManager.SquadMembers.Count == 0)
        {
            return false;
        }

        m_effectEndTime = Time.time + ActiveDuration;
        squadManager.ApplySquadStatusEffect(effect, Faction.Player, gameObject);
        m_effectRoutine = StartCoroutine(RunEffect());
        return true;
    }

    private IEnumerator RunEffect()
    {
        if (ActiveDuration > 0.0f)
        {
            yield return new WaitForSeconds(ActiveDuration);
        }

        FinishEffect();
    }

    private void OnDisable()
    {
        if (m_effectRoutine != null)
        {
            StopCoroutine(m_effectRoutine);
            FinishEffect();
        }
    }

    private void FinishEffect()
    {
        m_effectRoutine = null;
        m_effectEndTime = 0.0f;
        NotifyStateChanged();
    }

    private StatusEffectDefinitionSO ResolveHasteEffect()
    {
        if (m_hasteEffect == null)
        {
            m_hasteEffect = Resources.Load<StatusEffectDefinitionSO>(HasteResourcePath);
        }

        return m_hasteEffect;
    }
}
