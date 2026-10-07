using FMODUnity;
using UnityEngine;

/// <summary>
/// 방어 목표가 실제 피해를 받을 때 외곽 방어선 타격음과 단계별 경고음을 재생합니다.
/// </summary>
/// <remarks>
/// 타격음은 실제 피해 한 건마다 3D로 재생합니다. 일반 경고음은 첫 피해에 즉시 재생한 뒤 쿨다운 동안
/// 억제하고, 쿨다운이 지난 뒤 다음 피해가 들어올 때 다시 재생합니다. 따라서 적 수가 늘어도 경고음이
/// 매 타격마다 겹치지 않으며, 피해가 멈춘 동안에는 별도의 반복 타이머가 소리를 만들지 않습니다.
/// 체력이 위험 기준을 처음 통과한 피해에서는 일반 경고 대신 긴급 경고를 한 번만 재생합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(DefenseEventHealth))]
public sealed class DefenseObjectiveAudioController : MonoBehaviour
{
    private const string BarrierHitEvent = "event:/World/Defense/BarrierHit";
    private const string UnderAttackWarningEvent = "event:/UI/Defense/UnderAttackWarning";
    private const string CriticalWarningEvent = "event:/UI/Defense/CriticalWarning";

    [Header("Warning Cadence")]
    [Tooltip("일반 경고음을 재생한 뒤 다음 일반 경고를 허용할 때까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_warningCooldown = 6.0f;

    [Tooltip("체력이 이 비율 이하가 되는 첫 피해에서 일반 경고 대신 긴급 경고를 한 번 재생합니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_criticalHealthRatio = 0.25f;

    private DefenseEventHealth m_objective;
    private float m_nextWarningAllowedTime;
    private bool m_criticalWarningPlayed;
    private bool m_loggedMissingBarrierEvent;
    private bool m_loggedMissingUnderAttackEvent;
    private bool m_loggedMissingCriticalEvent;

    private void Awake()
    {
        m_objective = GetComponent<DefenseEventHealth>();
    }

    private void OnEnable()
    {
        if (m_objective == null)
        {
            m_objective = GetComponent<DefenseEventHealth>();
        }

        if (m_objective == null)
        {
            enabled = false;
            return;
        }

        m_objective.OnDamaged -= HandleDamaged;
        m_objective.OnDamaged += HandleDamaged;
        m_objective.OnHPChanged -= HandleHpChanged;
        m_objective.OnHPChanged += HandleHpChanged;
        ResetWarningState();
    }

    private void OnDisable()
    {
        if (m_objective == null)
        {
            return;
        }

        m_objective.OnDamaged -= HandleDamaged;
        m_objective.OnHPChanged -= HandleHpChanged;
    }

    private void HandleDamaged(float actualDamage, GameObject attacker)
    {
        if (actualDamage <= 0.0f)
        {
            return;
        }

        PlayOneShot(BarrierHitEvent, transform.position, ref m_loggedMissingBarrierEvent);

        // 0 HP 피해에서는 패배 연출이 바로 이어지므로 경고음을 추가로 겹치지 않습니다.
        if (m_objective.CurrentHPExact <= 0.0f)
        {
            return;
        }

        float healthRatio = m_objective.MaxHP > 0
            ? Mathf.Clamp01(m_objective.CurrentHPExact / m_objective.MaxHP)
            : 0.0f;

        if (!m_criticalWarningPlayed && healthRatio <= m_criticalHealthRatio)
        {
            m_criticalWarningPlayed = true;
            PlayOneShot(CriticalWarningEvent, Vector3.zero, ref m_loggedMissingCriticalEvent);
            m_nextWarningAllowedTime = Time.unscaledTime + m_warningCooldown;
            return;
        }

        if (Time.unscaledTime < m_nextWarningAllowedTime)
        {
            return;
        }

        PlayOneShot(UnderAttackWarningEvent, Vector3.zero, ref m_loggedMissingUnderAttackEvent);
        m_nextWarningAllowedTime = Time.unscaledTime + m_warningCooldown;
    }

    private void HandleHpChanged(int currentHp, int maxHp)
    {
        // 새 라운드나 재시작으로 목표 체력이 다시 가득 찼으면 경고 상태도 초기화합니다.
        if (maxHp > 0 && currentHp >= maxHp)
        {
            ResetWarningState();
        }
    }

    private void ResetWarningState()
    {
        m_nextWarningAllowedTime = 0.0f;
        m_criticalWarningPlayed = false;
    }

    private void PlayOneShot(string eventPath, Vector3 position, ref bool loggedMissingEvent)
    {
        if (!RuntimeManager.IsInitialized)
        {
            return;
        }

        try
        {
            RuntimeManager.PlayOneShot(eventPath, position);
        }
        catch (EventNotFoundException exception)
        {
            if (loggedMissingEvent)
            {
                return;
            }

            loggedMissingEvent = true;
            Debug.LogWarning(
                $"[DefenseObjectiveAudioController] FMOD 이벤트를 찾지 못했습니다: {eventPath}\n{exception.Message}",
                this
            );
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_warningCooldown = Mathf.Max(0.0f, m_warningCooldown);
        m_criticalHealthRatio = Mathf.Clamp01(m_criticalHealthRatio);
    }
#endif
}
