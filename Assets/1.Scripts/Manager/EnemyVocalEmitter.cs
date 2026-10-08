using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 감염체를 전역 보컬 재생 후보로 등록하고 현재 이동 상태를 제공합니다.
/// </summary>
/// <remarks>
/// 실제 FMOD 재생권과 동시 발성 수는 <see cref="EnemyVocalDirector"/>가 관리합니다.
/// 각 적이 독립적으로 타이머를 돌리지 않으므로 다수의 적이 동시에 울부짖는 상황을 피할 수 있습니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyController))]
public sealed class EnemyVocalEmitter : MonoBehaviour
{
    private const float MinimumMoveSpeed = 0.1f;

    private EnemyController m_controller;
    private NavMeshAgent m_agent;

    internal EnemyController Controller => m_controller;
    internal bool HasActiveIndividualVoice { get; set; }
    internal float NextIndividualVoiceTime { get; set; }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        HasActiveIndividualVoice = false;
        NextIndividualVoiceTime = Time.time + Random.Range(1.0f, 3.0f);
        EnemyVocalDirector.Register(this);
    }

    private void OnDisable()
    {
        EnemyVocalDirector.Unregister(this);
        HasActiveIndividualVoice = false;
    }

    /// <summary>현재 이 감염체가 일반 접근 보컬 후보인지 확인합니다.</summary>
    internal bool TryGetMovingCandidate(out Vector3 position, out float moveSpeed)
    {
        CacheReferences();
        position = transform.position;
        moveSpeed = 0.0f;

        if (m_controller == null
            || !m_controller.isActiveAndEnabled
            || !m_controller.IsDefenseEnemy
            || m_controller.CurrentHP <= 0
            || m_controller.Current == m_controller.Dead
            || m_controller.IsAttackingDefenseObjective
            || m_agent == null
            || !m_agent.enabled
            || !m_agent.isOnNavMesh)
        {
            return false;
        }

        moveSpeed = m_agent.velocity.magnitude;
        return moveSpeed >= MinimumMoveSpeed;
    }

    private void CacheReferences()
    {
        if (m_controller == null)
        {
            m_controller = GetComponent<EnemyController>();
        }

        if (m_controller != null)
        {
            m_agent = m_controller.Agent;
        }
    }
}
