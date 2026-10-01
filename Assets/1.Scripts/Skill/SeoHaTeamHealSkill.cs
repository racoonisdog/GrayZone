using UnityEngine;

/// <summary>전 스쿼드원의 HP를 즉시 회복하고 다운된 동료는 원격 구조하는 서하의 스킬입니다.</summary>
public sealed class SeoHaTeamHealSkill : CharacterSkill
{
    [Tooltip("각 팀원의 최대 HP에서 회복할 비율입니다. 30이면 최대 HP의 30%를 회복합니다.")]
    [Range(1.0f, 100.0f)]
    [SerializeField] private float m_healPercent = 30.0f;

    [Tooltip("One-shot visual played around every squad member who is healed or revived.")]
    [SerializeField] private StatusEffectVisualSO m_healVisual;

    public override CharacterSkillType SkillType => CharacterSkillType.SeoHaTeamHeal;
    public override string DisplayName => "응급 치료";
    public override string Description => "전 대원의 HP를 즉시 회복하고 다운된 동료는 원격 구조합니다.";

    protected override bool ActivateSkill(SquadManager squadManager)
    {
        bool healedAny = false;
        for (int i = 0; i < squadManager.SquadMembers.Count; i++)
        {
            SquadMemberController member = squadManager.SquadMembers[i];
            if (member == null || !member.IsAlive)
            {
                continue;
            }

            PlayerHealth health = member.GetComponent<PlayerHealth>();
            if (health == null)
            {
                continue;
            }

            int amount = Mathf.Max(1, Mathf.RoundToInt(health.MaxHP * m_healPercent * 0.01f));
            if (member.IsDown || health.IsDowned)
            {
                if (!health.ReviveFromDown(amount))
                {
                    continue;
                }

                member.SetAlive(true);
                member.SetDown(false);
                member.CompleteAssistedStandingAnimator();
                member.StatusEffects.PlayVisual(ResolveHealVisual());
                healedAny = true;
                continue;
            }

            if (health.Heal(amount))
            {
                member.StatusEffects.PlayVisual(ResolveHealVisual());
                healedAny = true;
            }
        }

        return healedAny;
    }

    private StatusEffectVisualSO ResolveHealVisual()
    {
        return m_healVisual != null
            ? m_healVisual
            : Resources.Load<StatusEffectVisualSO>("StatusEffectVisuals/TeamHeal");
    }
}
