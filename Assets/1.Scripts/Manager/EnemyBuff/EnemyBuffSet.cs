using System.Collections.Generic;
using UnityEngine;

/// <summary>한 적에게 적용 중인 적 전용 버프와 단일 소유권을 관리합니다.</summary>
/// <remarks>
/// 같은 버프는 동시에 한 하울러만 소유할 수 있습니다. 소유자가 살아 있는 동안 잔여시간은 감소하지 않고,
/// 소유자가 죽거나 비활성화된 뒤부터 에셋의 지속시간이 흐릅니다. 그 시점부터 다른 하울러가 소유권을
/// 가져오면 잔여시간은 새 버프의 전체 지속시간으로 다시 설정됩니다.
/// </remarks>
public sealed class EnemyBuffSet
{
    private struct Entry
    {
        public EnemyBuffSO Buff;
        public EnemyController Owner;
        public float RemainingDuration;
        public float LastTickTime;
    }

    private readonly EnemyController m_recipient;
    private readonly List<Entry> m_entries = new List<Entry>();
    private StatusEffectContainer m_visuals;

    public EnemyBuffSet(EnemyController recipient)
    {
        m_recipient = recipient;
    }

    public int Count => m_entries.Count;

    public bool Has(EnemyBuffSO buff) => IndexOf(buff) >= 0;

    public EnemyController GetOwner(EnemyBuffSO buff)
    {
        int index = IndexOf(buff);
        return index >= 0 ? m_entries[index].Owner : null;
    }

    public float GetRemainingDuration(EnemyBuffSO buff)
    {
        int index = IndexOf(buff);
        return index >= 0 ? Mathf.Max(0.0f, m_entries[index].RemainingDuration) : 0.0f;
    }

    /// <summary>해당 버프를 지금 지정한 소유자가 새로 확보할 수 있는지 확인합니다.</summary>
    public bool CanAcceptOwner(EnemyBuffSO buff, EnemyController owner)
    {
        if (buff == null || owner == null || !IsAlive(owner))
        {
            return false;
        }

        int index = IndexOf(buff);
        if (index < 0)
        {
            return true;
        }

        Entry entry = m_entries[index];
        if (entry.Owner == owner)
        {
            // 이미 이 소유자의 버프를 유지 중인 대상은 재하울링 인원으로 세지 않습니다.
            return false;
        }

        // 기존 소유자가 살아 있는 동안 다른 하울러는 같은 버프의 소유권을 가질 수 없습니다.
        return !IsAlive(entry.Owner);
    }

    /// <summary>
    /// 버프를 적용하거나 죽은 소유자의 자리를 새 소유자로 교체합니다.
    /// </summary>
    /// <returns>새 적용 또는 소유권 교체가 일어났으면 true입니다.</returns>
    public bool Apply(EnemyBuffSO buff, EnemyController owner = null)
    {
        if (buff == null)
        {
            return false;
        }

        int index = IndexOf(buff);
        float now = Time.time;
        if (index >= 0)
        {
            Entry existing = m_entries[index];
            bool existingOwnerAlive = IsAlive(existing.Owner);

            if (existingOwnerAlive && existing.Owner != owner)
            {
                return false;
            }

            // 소유권 없는 기존 호출은 살아 있는 하울러가 잡은 버프를 덮어쓸 수 없습니다.
            if (existingOwnerAlive && owner == null)
            {
                return false;
            }

            existing.Owner = owner;
            existing.RemainingDuration = buff.Duration;
            existing.LastTickTime = now;
            m_entries[index] = existing;
        }
        else
        {
            m_entries.Add(new Entry
            {
                Buff = buff,
                Owner = owner,
                RemainingDuration = buff.Duration,
                LastTickTime = now,
            });
        }

        m_recipient.AddMoveSpeedMultiplier(buff, buff.MoveSpeedMultiplier);
        Visuals?.PlayOrRefreshVisual(buff, buff.RecipientVisual, buff.Duration);
        return true;
    }

    /// <summary>소유자 생존 여부에 따라 버프 잔여시간을 동결하거나 감소시킵니다.</summary>
    public void Tick()
    {
        if (m_entries.Count == 0)
        {
            return;
        }

        float now = Time.time;
        for (int i = m_entries.Count - 1; i >= 0; i--)
        {
            Entry entry = m_entries[i];
            float deltaTime = Mathf.Max(0.0f, now - entry.LastTickTime);
            entry.LastTickTime = now;

            if (IsAlive(entry.Owner))
            {
                // 시각 이펙트도 소유자가 살아 있는 동안 만료되지 않게 같은 키의 수명만 갱신합니다.
                Visuals?.PlayOrRefreshVisual(entry.Buff, entry.Buff.RecipientVisual, entry.Buff.Duration);
                m_entries[i] = entry;
                continue;
            }

            entry.RemainingDuration -= deltaTime;
            if (entry.RemainingDuration > 0.0f)
            {
                m_entries[i] = entry;
                continue;
            }

            m_entries.RemoveAt(i);
            m_recipient.RemoveMoveSpeedMultiplier(entry.Buff);
            Visuals?.StopVisual(entry.Buff);
        }
    }

    /// <summary>풀 반환 시 버프 기록과 연결된 시각 이펙트를 정리합니다.</summary>
    public void ClearRecordsOnly()
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            Visuals?.StopVisual(m_entries[i].Buff);
        }

        m_entries.Clear();
    }

    private StatusEffectContainer Visuals => m_visuals != null
        ? m_visuals
        : m_visuals = StatusEffectContainer.GetOrAdd(m_recipient.gameObject);

    private int IndexOf(EnemyBuffSO buff)
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            if (m_entries[i].Buff == buff)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsAlive(EnemyController owner)
    {
        return owner != null
            && owner.isActiveAndEnabled
            && (owner.Health == null || !owner.Health.IsDead);
    }
}
