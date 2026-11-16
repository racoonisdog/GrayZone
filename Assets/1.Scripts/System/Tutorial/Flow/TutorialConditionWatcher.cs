using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 트리거 하나의 완료 조건을 셉니다. 트리거가 실행될 때 만들어 세기 시작하고, 그 트리거를 닫는 엔드포인트가 닫히면 <see cref="Dispose"/>합니다.
/// </summary>
/// <remarks>
/// 트리거와 엔드포인트 사이에 대사가 있어도 그동안의 처치·상호작용을 셀 수 있도록, 조건은 트리거 시점부터 셉니다.
/// 열린 트리거가 여러 개면 감시자도 여러 개가 함께 세고, 엔드포인트는 자기가 닫는 트리거의 감시자가 모두 채워질 때까지 기다립니다.
/// </remarks>
public sealed class TutorialConditionWatcher : IDisposable
{
    private readonly TutorialBlock m_trigger;
    private readonly float m_startTime;
    private readonly Dictionary<EnemyHealth, Action> m_deathHandlers = new();

    private int m_killed;
    private bool m_interacted;
    private bool m_skillUsed;

    /// <summary>트리거가 실행될 때 조작 중이던 캐릭터의 스쿼드 목록 번호입니다. 캐릭터 번호 0인 SwitchCharacter 조건이 비교합니다.</summary>
    private readonly int m_startMemberIndex = -1;

    /// <summary>대상 ID로 대상을 찾지 못했는지 여부입니다. 튜토리얼이 멈추지 않도록 경고 후 조건을 채운 것으로 봅니다.</summary>
    private bool m_targetMissing;

    private bool m_disposed;

    /// <summary>상호작용 조건이 기다리는 대상입니다. 목표 외곽선을 켜 둔 동안만 값이 있습니다.</summary>
    private TutorialTarget m_interactTarget;

    /// <summary>상호작용 대상에 붙은 함정입니다. 함정이 아니면 null입니다. 이미 설치·수리된 상태인지 볼 때 씁니다.</summary>
    private Trap m_targetTrap;

    /// <summary>상호작용 대상에 붙은 방어 목표 수리(정문 등)입니다. 아니면 null입니다. 체력이 가득 찼는지 볼 때 씁니다.</summary>
    private DefenseRepairInteractable m_targetRepair;

    /// <summary>ReachArea·AllyInArea 조건의 도착 범위 오브젝트입니다. 없으면 위치·반경으로 판정합니다.</summary>
    private readonly TutorialArriveArea m_area;

    /// <summary>트리거의 완료 조건을 세기 시작합니다.</summary>
    /// <param name="trigger">완료 조건을 가진 트리거 블록입니다.</param>
    /// <param name="context">경고 로그를 눌렀을 때 선택할 오브젝트입니다.</param>
    public TutorialConditionWatcher(TutorialBlock trigger, UnityEngine.Object context)
    {
        m_trigger = trigger;
        m_startTime = Time.unscaledTime;

        switch (trigger.Condition)
        {
            case TutorialConditionType.KillEnemy:
                EnemyHealth[] enemies = UnityEngine.Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
                for (int i = 0; i < enemies.Length; i++)
                {
                    WatchEnemy(enemies[i]);
                }

                // 세는 동안 새로 나오는(스폰되거나 풀에서 꺼내지는) 적도 셉니다.
                EnemyHealth.OnEnemyEnabled += WatchEnemy;
                break;

            case TutorialConditionType.Interact:
                m_interactTarget = TutorialTarget.Find(trigger.TargetId);
                m_targetTrap = m_interactTarget != null ? m_interactTarget.GetComponent<Trap>() : null;
                m_targetRepair = m_interactTarget != null ? m_interactTarget.GetComponent<DefenseRepairInteractable>() : null;
                if (!string.IsNullOrEmpty(trigger.TargetId) && m_interactTarget == null)
                {
                    Debug.LogWarning($"튜토리얼 대상 '{trigger.TargetId}'를 찾지 못해 상호작용 조건을 건너뜁니다. 씬 오브젝트에 같은 ID의 {nameof(TutorialTarget)}이 있는지 확인하세요.", context);
                    m_targetMissing = true;
                }

                // 기다리는 동안 대상에 목표 외곽선을 켭니다. 상호작용을 마치거나 조건이 닫히면 끕니다.
                m_interactTarget?.AcquireObjective();
                InteractionController.OnAnyInteracted += HandleInteracted;
                break;

            case TutorialConditionType.SwitchCharacter:
                SquadManager squad = SquadManager.Instance;
                m_startMemberIndex = squad != null ? squad.PlayerSquadMemberIndex : -1;
                break;

            case TutorialConditionType.UseSkill:
                CharacterSkill.OnAnyActivated += HandleSkillActivated;
                break;

            case TutorialConditionType.ReachArea:
            case TutorialConditionType.AllyInArea:
                m_area = TutorialArriveArea.Find(trigger.AreaObject);
                if (!string.IsNullOrEmpty(trigger.AreaObject) && m_area == null)
                {
                    Debug.LogWarning($"도착 범위 오브젝트 '{trigger.AreaObject}'에서 {nameof(TutorialArriveArea)}를 찾지 못해 위치·반경으로 판정합니다.", context);
                }

                break;
        }
    }

    /// <summary>조건을 채웠는지 여부입니다.</summary>
    public bool IsMet
    {
        get
        {
            switch (m_trigger.Condition)
            {
                case TutorialConditionType.None:
                    return true;

                case TutorialConditionType.Time:
                    return Time.unscaledTime - m_startTime >= m_trigger.Seconds;

                case TutorialConditionType.KillEnemy:
                    return m_killed >= m_trigger.KillCount;

                case TutorialConditionType.ReachArea:
                    return m_area != null ? TutorialEvents.IsPlayerInArea(m_area) : TutorialEvents.IsPlayerInArea(m_trigger.AreaCenter, m_trigger.AreaRadius);

                case TutorialConditionType.Interact:
                    return m_interacted || m_targetMissing || IsTrapGoalReached() || IsRepairGoalReached();

                case TutorialConditionType.SwitchCharacter:
                    return IsSwitched();

                case TutorialConditionType.UseSkill:
                    return m_skillUsed;

                case TutorialConditionType.HoldOrder:
                    return IsOrderState(true);

                case TutorialConditionType.RecallOrder:
                    return IsOrderState(false);

                case TutorialConditionType.RescueMember:
                    return IsMemberRescued();

                case TutorialConditionType.AllyInArea:
                    return IsAllyInArea(IsInArea);

                default:
                    return true;
            }
        }
    }

    /// <summary>걸어 둔 알림을 모두 뗍니다.</summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        EnemyHealth.OnEnemyEnabled -= WatchEnemy;
        InteractionController.OnAnyInteracted -= HandleInteracted;
        ReleaseInteractTarget();
        CharacterSkill.OnAnyActivated -= HandleSkillActivated;
        foreach (KeyValuePair<EnemyHealth, Action> pair in m_deathHandlers)
        {
            if (pair.Key != null)
            {
                pair.Key.OnDeath -= pair.Value;
            }
        }

        m_deathHandlers.Clear();
    }

    private void WatchEnemy(EnemyHealth enemy)
    {
        if (enemy == null || m_deathHandlers.ContainsKey(enemy) || !TutorialEvents.IsFromPrefab(enemy, m_trigger.EnemyPrefab))
        {
            return;
        }

        Action handler = () => m_killed++;
        enemy.OnDeath += handler;
        m_deathHandlers[enemy] = handler;
    }

    /// <summary>조작 중인 캐릭터가 정한 번호의 캐릭터인지, 번호가 0이면 트리거 때와 다른 캐릭터인지 확인합니다.</summary>
    private bool IsSwitched()
    {
        SquadManager squad = SquadManager.Instance;
        if (squad == null)
        {
            return false;
        }

        int index = squad.PlayerSquadMemberIndex;
        return m_trigger.MemberNumber > 0 ? index == m_trigger.MemberNumber - 1 : index != m_startMemberIndex;
    }

    /// <summary>
    /// 고른 명령 키의 AI 팀원이 위치 사수 명령을 받은 상태(<paramref name="holding"/>가 true) 또는 복귀한 상태인지 확인합니다.
    /// </summary>
    /// <remarks>상태를 보므로, 트리거 전에 이미 그 상태였어도 완료입니다.</remarks>
    private bool IsOrderState(bool holding)
    {
        SquadManager squad = SquadManager.Instance;
        if (squad == null)
        {
            return false;
        }

        switch (m_trigger.OrderKey)
        {
            case TutorialOrderKey.Q:
                return squad.HasAiMoveOrder(0) == holding;

            case TutorialOrderKey.E:
                return squad.HasAiMoveOrder(1) == holding;

            default:
                return squad.HasAiMoveOrder(0) == holding && squad.HasAiMoveOrder(1) == holding;
        }
    }

    private void HandleSkillActivated(CharacterSkill skill)
    {
        if (m_trigger.MemberNumber <= 0)
        {
            m_skillUsed = true;
            return;
        }

        SquadManager squad = SquadManager.Instance;
        int index = m_trigger.MemberNumber - 1;
        if (squad != null && skill != null && index < squad.SquadMembers.Count && squad.SquadMembers[index] == skill.Owner)
        {
            m_skillUsed = true;
        }
    }

    private void HandleInteracted(IInteractable interactable, GameObject interactor)
    {
        string id = m_trigger.TargetId;
        if (string.IsNullOrEmpty(id))
        {
            m_interacted = true;
            return;
        }

        TutorialTarget target = interactable is Component component ? component.GetComponentInParent<TutorialTarget>() : null;
        if (target != null && target.Id == id)
        {
            // 방어 목표 수리는 한 번 고칠 때마다 상호작용이 끝나므로, 체력이 가득 찰 때까지는 완료로 보지 않습니다.
            if (m_targetRepair != null && !m_targetRepair.IsFullHealth)
            {
                return;
            }

            m_interacted = true;
            ReleaseInteractTarget();
        }
    }

    /// <summary>
    /// 상호작용 대상이 함정이면, 설치돼 있고 내구도가 가득 찬 상태를 이미 이룬 것으로 봅니다. 이루면 목표 외곽선도 끕니다.
    /// </summary>
    /// <remarks>
    /// 앞 챕터에서 미리 설치하거나 수리한 함정은 이 조건이 시작된 뒤 다시 상호작용할 일이 없어, 상호작용만 기다리면 진행이 멈춥니다.
    /// 청사진은 설치하면, 깎인 함정(예: 30% 스파이크)은 수리해서 가득 채우면 완료입니다.
    /// </remarks>
    private bool IsTrapGoalReached()
    {
        if (m_targetTrap == null || !m_targetTrap.IsBuilt || m_targetTrap.Health < m_targetTrap.MaxHealth)
        {
            return false;
        }

        ReleaseInteractTarget();
        return true;
    }

    /// <summary>정한 번호의 캐릭터가 다운·이탈 상태가 아니면(구조돼 일어났으면) 완료로 봅니다. 캐릭터를 찾지 못하면 멈추지 않도록 완료로 봅니다.</summary>
    private bool IsMemberRescued()
    {
        SquadManager squad = SquadManager.Instance;
        int index = m_trigger.MemberNumber - 1;
        if (squad == null || index < 0 || index >= squad.SquadMembers.Count || squad.SquadMembers[index] == null)
        {
            return true;
        }

        PlayerHealth health = squad.SquadMembers[index].GetComponent<PlayerHealth>();
        return health == null || (!health.IsDowned && !health.IsDead);
    }

    /// <summary>조작 중이 아닌 동료 중 다운·이탈하지 않은 한 명이 도착 범위 안에 있는지 여부입니다.</summary>
    /// <remarks>
    /// 위치 사수 명령을 받고 반경 안에 들어온 동료는 그 동료의 Q/E 명령 키를 막습니다. 다른 지점으로 다시 옮겨 조건이 깨지지 않게 하기 위해서입니다.
    /// 막은 키는 "위치 사수 키 잠금 풀기" 이벤트나 튜토리얼 종료 때 풉니다.
    /// </remarks>
    private static bool IsAllyInArea(Func<Vector3, bool> isInArea)
    {
        SquadManager squad = SquadManager.Instance;
        if (squad == null)
        {
            return false;
        }

        foreach (SquadMemberController member in squad.SquadMembers)
        {
            if (member == null || member.IsPlayerSquadMember || !member.IsAlive || member.IsDown)
            {
                continue;
            }

            if (isInArea(member.transform.position))
            {
                SquadAIController ai = member.GetComponent<SquadAIController>();
                if (ai != null && ai.HasMoveOrder)
                {
                    squad.LockOrders(member);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>위치가 이 조건의 도착 범위 안인지 여부입니다. 범위 오브젝트가 있으면 그 범위로, 없으면 위치·반경(평면 거리)으로 봅니다.</summary>
    private bool IsInArea(Vector3 position)
    {
        if (m_area != null)
        {
            return m_area.Contains(position);
        }

        Vector3 offset = position - m_trigger.AreaCenter;
        offset.y = 0.0f;
        return offset.sqrMagnitude <= m_trigger.AreaRadius * m_trigger.AreaRadius;
    }

    /// <summary>상호작용 대상이 방어 목표 수리면, 체력이 가득 찬 상태를 완료로 봅니다. 이루면 목표 외곽선도 끕니다.</summary>
    private bool IsRepairGoalReached()
    {
        if (m_targetRepair == null || !m_targetRepair.IsFullHealth)
        {
            return false;
        }

        ReleaseInteractTarget();
        return true;
    }

    /// <summary>대상의 목표 외곽선을 한 번만 끕니다.</summary>
    private void ReleaseInteractTarget()
    {
        if (m_interactTarget != null)
        {
            m_interactTarget.ReleaseObjective();
        }

        m_interactTarget = null;
    }
}
