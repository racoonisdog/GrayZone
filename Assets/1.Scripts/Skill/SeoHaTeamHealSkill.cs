using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>전 스쿼드원의 HP를 즉시 회복하고 다운된 동료는 원격 구조하는 서하의 스킬입니다.</summary>
public sealed class SeoHaTeamHealSkill : CharacterSkill
{
    private sealed class RemoteRescueTarget
    {
        public DownedAllyInteractable Interactable;
        public float StartProgress;
        public float Duration;
        public float Elapsed;
    }

    [Tooltip("각 팀원의 최대 HP에서 회복할 비율입니다. 30이면 최대 HP의 30%를 회복합니다.")]
    [Range(1.0f, 100.0f)]
    [SerializeField] private float m_healPercent = 30.0f;

    [Tooltip("다운된 대상을 스킬로 원격 구조했을 때 부여할 체력입니다. 일반 전체 회복량과 별도입니다.")]
    [Min(1)]
    [SerializeField] private int m_skillReviveHp = 20;

    [Tooltip("One-shot visual played around every squad member who is healed or revived.")]
    [SerializeField] private StatusEffectVisualSO m_healVisual;

    private readonly List<RemoteRescueTarget> m_remoteRescueTargets =
        new List<RemoteRescueTarget>();
    private Coroutine m_remoteRescueRoutine;
    private float m_remoteRescueEndTime;

    public override CharacterSkillType SkillType => CharacterSkillType.SeoHaTeamHeal;
    public override string DisplayName => "응급 치료";
    public override string Description => "일반 대원은 즉시 회복하고 다운된 동료는 원격으로 구조 게이지를 채웁니다.";
    public override bool IsActive => m_remoteRescueRoutine != null;
    public override float ActiveDuration
    {
        get
        {
            float duration = 0.0f;
            for (int i = 0; i < m_remoteRescueTargets.Count; i++)
            {
                duration = Mathf.Max(duration, m_remoteRescueTargets[i].Duration);
            }

            return duration;
        }
    }
    public override float ActiveRemaining => IsActive
        ? Mathf.Max(0.0f, m_remoteRescueEndTime - Time.time)
        : 0.0f;

    protected override bool ActivateSkill(SquadManager squadManager)
    {
        bool healedAny = false;
        m_remoteRescueTargets.Clear();
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
                DownedAllyInteractable interactable = member.GetComponent<DownedAllyInteractable>();
                if (interactable == null || !interactable.BeginRemoteRescue())
                {
                    continue;
                }

                float startProgress = interactable.ReviveHoldProgress01;
                float duration = Mathf.Max(0.01f, interactable.HoldDuration * (1.0f - startProgress));
                m_remoteRescueTargets.Add(new RemoteRescueTarget
                {
                    Interactable = interactable,
                    StartProgress = startProgress,
                    Duration = duration,
                    Elapsed = 0.0f,
                });
                member.StatusEffects.PlayVisual(ResolveHealVisual(), duration);
                continue;
            }

            if (health.Heal(amount))
            {
                member.StatusEffects.PlayVisual(ResolveHealVisual());
                healedAny = true;
            }
        }

        if (m_remoteRescueTargets.Count > 0)
        {
            float longestDuration = ActiveDuration;
            m_remoteRescueEndTime = Time.time + longestDuration;
            m_remoteRescueRoutine = StartCoroutine(RunRemoteRescues());
        }

        return healedAny || m_remoteRescueTargets.Count > 0;
    }

    private IEnumerator RunRemoteRescues()
    {
        while (m_remoteRescueTargets.Count > 0)
        {
            for (int i = m_remoteRescueTargets.Count - 1; i >= 0; i--)
            {
                RemoteRescueTarget target = m_remoteRescueTargets[i];
                if (target.Interactable == null || !target.Interactable.CanReceiveRemoteRescue())
                {
                    if (target.Interactable != null)
                    {
                        target.Interactable.CancelRemoteRescue();
                    }

                    m_remoteRescueTargets.RemoveAt(i);
                    continue;
                }

                target.Elapsed += Time.deltaTime;
                float localProgress = Mathf.Clamp01(target.Elapsed / target.Duration);
                float progress = Mathf.Lerp(target.StartProgress, 1.0f, localProgress);
                if (!target.Interactable.UpdateRemoteRescue(progress))
                {
                    m_remoteRescueTargets.RemoveAt(i);
                    continue;
                }

                if (localProgress < 1.0f)
                {
                    continue;
                }

                target.Interactable.CompleteRemoteRescue(m_skillReviveHp);
                m_remoteRescueTargets.RemoveAt(i);
            }

            yield return null;
        }

        FinishRemoteRescues();
    }

    private void OnDisable()
    {
        if (m_remoteRescueRoutine != null)
        {
            StopCoroutine(m_remoteRescueRoutine);
        }

        for (int i = 0; i < m_remoteRescueTargets.Count; i++)
        {
            if (m_remoteRescueTargets[i].Interactable != null)
            {
                m_remoteRescueTargets[i].Interactable.CancelRemoteRescue();
            }
        }

        FinishRemoteRescues();
    }

    private void FinishRemoteRescues()
    {
        m_remoteRescueTargets.Clear();
        m_remoteRescueRoutine = null;
        m_remoteRescueEndTime = 0.0f;
        NotifyStateChanged();
    }

    private StatusEffectVisualSO ResolveHealVisual()
    {
        return m_healVisual != null
            ? m_healVisual
            : Resources.Load<StatusEffectVisualSO>("StatusEffectVisuals/TeamHeal");
    }
}
