using System;
using UnityEngine;

/// <summary>캐릭터가 보유한 스킬의 공통 상태와 쿨다운을 관리하는 기반 컴포넌트입니다.</summary>
[RequireComponent(typeof(SquadMemberController))]
public abstract class CharacterSkill : MonoBehaviour
{
    [Tooltip("스킬을 다시 사용할 수 있을 때까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_cooldownDuration = 30.0f;

    private float m_nextReadyTime;
    private SquadMemberController m_owner;

    /// <summary>UI와 외부 시스템이 구분할 스킬 종류입니다.</summary>
    public abstract CharacterSkillType SkillType { get; }

    /// <summary>UI에 표시할 스킬 이름입니다.</summary>
    public abstract string DisplayName { get; }

    /// <summary>UI에 표시할 짧은 스킬 설명입니다.</summary>
    public abstract string Description { get; }

    /// <summary>이 스킬을 소유한 스쿼드 멤버입니다.</summary>
    public SquadMemberController Owner => m_owner;

    /// <summary>전체 쿨다운 시간입니다.</summary>
    public float CooldownDuration => Mathf.Max(0.0f, m_cooldownDuration);

    /// <summary>남은 쿨다운 시간입니다.</summary>
    public float CooldownRemaining => Mathf.Max(0.0f, m_nextReadyTime - Time.time);

    /// <summary>스킬을 지금 사용할 수 있는지 여부입니다.</summary>
    public bool IsReady => CooldownRemaining <= 0.0f && !IsActive;

    /// <summary>지속 효과 또는 장전된 특수탄이 활성 상태인지 여부입니다.</summary>
    public virtual bool IsActive => false;

    /// <summary>지속 효과의 전체 시간입니다. 지속 시간이 없는 스킬은 0입니다.</summary>
    public virtual float ActiveDuration => 0.0f;

    /// <summary>지속 효과의 남은 시간입니다.</summary>
    public virtual float ActiveRemaining => 0.0f;

    /// <summary>활성·종료 등 큰 상태가 변경될 때 발생합니다. 매 프레임 남은 시간은 속성으로 조회합니다.</summary>
    public event Action<CharacterSkill> OnStateChanged;

    protected virtual void Awake()
    {
        m_owner = GetComponent<SquadMemberController>();
    }

    /// <summary>현재 조작 멤버의 스킬 발동을 시도합니다.</summary>
    public bool TryActivate(SquadManager squadManager)
    {
        if (squadManager == null || m_owner == null || !m_owner.IsAlive || m_owner.IsDown || !IsReady)
        {
            return false;
        }

        if (!ActivateSkill(squadManager))
        {
            return false;
        }

        m_nextReadyTime = Time.time + CooldownDuration;
        NotifyStateChanged();
        return true;
    }

    /// <summary>캐릭터별 실제 스킬 효과를 실행합니다. 효과가 성립했을 때만 true를 반환합니다.</summary>
    protected abstract bool ActivateSkill(SquadManager squadManager);

    protected void NotifyStateChanged()
    {
        OnStateChanged?.Invoke(this);
    }
}

/// <summary>플레이어블 캐릭터가 보유하는 스킬 종류입니다.</summary>
public enum CharacterSkillType
{
    NarinTeamHaste,
    ChungSolDragonBreath,
    SeoHaTeamHeal,
}
