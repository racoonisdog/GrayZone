using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터의 Animator 구동 상태와 뼈대 물리 래그돌 상태를 전환합니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 사망 판정이나 오브젝트 제거 시점을 소유하지 않습니다.
/// Unity Ragdoll Wizard 등으로 미리 구성된 Joint 연결망을 찾아 시각 표현의 주도권만
/// Animator와 물리 시뮬레이션 사이에서 전환합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class RagdollController : MonoBehaviour
{
    private Animator m_animator;
    private Rigidbody[] m_ragdollBodies = System.Array.Empty<Rigidbody>();
    private Collider[] m_ragdollColliders = System.Array.Empty<Collider>();
    private Collider[] m_gameplayColliders = System.Array.Empty<Collider>();
    private bool[] m_gameplayColliderStates = System.Array.Empty<bool>();
    private bool m_animatorWasEnabled;
    private bool m_isRagdollActive;

    /// <summary>Joint로 연결된 래그돌 물리 골격이 준비됐는지 여부입니다.</summary>
    public bool IsConfigured => m_ragdollBodies.Length > 0 && m_ragdollColliders.Length > 0;

    /// <summary>현재 물리 래그돌이 활성화됐는지 여부입니다.</summary>
    public bool IsRagdollActive => m_isRagdollActive;

    private void Awake()
    {
        CacheRagdollParts();
        PrepareAnimationDrivenState();
    }

    /// <summary>
    /// Animator를 멈추고, 사망 전 현재 자세에서 뼈대 물리 시뮬레이션을 시작합니다.
    /// </summary>
    /// <returns>물리 골격이 준비되어 래그돌을 활성화했으면 true입니다.</returns>
    public bool TryActivateRagdoll()
    {
        if (m_isRagdollActive)
        {
            return true;
        }

        if (!IsConfigured)
        {
            return false;
        }

        CaptureAndDisableGameplayColliders();

        m_animatorWasEnabled = m_animator != null && m_animator.enabled;
        if (m_animator != null)
        {
            m_animator.enabled = false;
        }

        for (int i = 0; i < m_ragdollColliders.Length; i++)
        {
            m_ragdollColliders[i].enabled = true;
        }

        Physics.SyncTransforms();

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Rigidbody body = m_ragdollBodies[i];
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = false;
            body.WakeUp();
        }

        m_isRagdollActive = true;
        return true;
    }

    /// <summary>
    /// 래그돌 물리를 끄고 Animator 구동 상태로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// 정상 게임 규칙에서는 최종 사망 후 부활하지 않지만, Editor 디버그 부활 검증을 위해 제공합니다.
    /// </remarks>
    public void DeactivateRagdoll()
    {
        if (!m_isRagdollActive)
        {
            return;
        }

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Rigidbody body = m_ragdollBodies[i];
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        for (int i = 0; i < m_ragdollColliders.Length; i++)
        {
            m_ragdollColliders[i].enabled = false;
        }

        RestoreGameplayColliders();

        if (m_animator != null)
        {
            m_animator.enabled = m_animatorWasEnabled;
        }

        m_isRagdollActive = false;
    }

    /// <summary>
    /// Joint가 붙은 Rigidbody와 그 연결 바디만 래그돌 물리 골격으로 수집합니다.
    /// </summary>
    /// <remarks>
    /// 캐릭터 자식에 무기 같은 별도 Rigidbody가 있어도 래그돌로 오인하지 않도록
    /// 전체 Rigidbody 목록이 아니라 Joint 연결망을 기준으로 삼습니다.
    /// </remarks>
    private void CacheRagdollParts()
    {
        m_animator = GetComponentInChildren<Animator>(true);

        Joint[] joints = GetComponentsInChildren<Joint>(true);
        HashSet<Rigidbody> bodySet = new HashSet<Rigidbody>();

        for (int i = 0; i < joints.Length; i++)
        {
            Joint joint = joints[i];
            Rigidbody attachedBody = joint.GetComponent<Rigidbody>();
            if (attachedBody != null && attachedBody.transform != transform)
            {
                bodySet.Add(attachedBody);
            }

            if (joint.connectedBody != null && joint.connectedBody.transform != transform)
            {
                bodySet.Add(joint.connectedBody);
            }
        }

        m_ragdollBodies = new Rigidbody[bodySet.Count];
        bodySet.CopyTo(m_ragdollBodies);

        HashSet<Collider> ragdollColliderSet = new HashSet<Collider>();
        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Collider[] bodyColliders = m_ragdollBodies[i].GetComponents<Collider>();
            for (int j = 0; j < bodyColliders.Length; j++)
            {
                ragdollColliderSet.Add(bodyColliders[j]);
            }
        }

        m_ragdollColliders = new Collider[ragdollColliderSet.Count];
        ragdollColliderSet.CopyTo(m_ragdollColliders);

        Collider[] allColliders = GetComponentsInChildren<Collider>(true);
        List<Collider> gameplayColliders = new List<Collider>();
        for (int i = 0; i < allColliders.Length; i++)
        {
            if (!ragdollColliderSet.Contains(allColliders[i]))
            {
                gameplayColliders.Add(allColliders[i]);
            }
        }

        m_gameplayColliders = gameplayColliders.ToArray();
        m_gameplayColliderStates = new bool[m_gameplayColliders.Length];
    }

    /// <summary>
    /// 래그돌 물리를 재우고 Animator가 뼈대를 구동하는 평상시 상태로 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 물리를 재우는 수단은 <c>isKinematic</c>과 래그돌 콜라이더 비활성 두 가지뿐입니다.
    /// <c>Rigidbody.detectCollisions = false</c>는 절대 쓰지 마십시오. 이 값은 해당 바디에 속한
    /// <b>모든</b> shape을 물리 씬에서 통째로 빼며, 시뮬레이션뿐 아니라 <c>Physics.Raycast</c> 같은
    /// 씬 쿼리에서도 사라집니다.
    ///
    /// 피격 히트박스와 근접 판정 콜라이더는 자기 Rigidbody가 없어 가장 가까운 부모인
    /// <b>래그돌 뼈 Rigidbody</b>에 소속됩니다. 그래서 뼈 바디를 끄면 살아 있는 개체가
    /// 총에 맞지 않고(히트스캔 관통) 근접 공격 트리거도 발화하지 않습니다.
    /// 실제로 그 사고가 있었으므로, 재우는 것은 바디가 아니라 콜라이더 쪽에서만 합니다.
    /// </remarks>
    private void PrepareAnimationDrivenState()
    {
        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            m_ragdollBodies[i].isKinematic = true;
        }

        for (int i = 0; i < m_ragdollColliders.Length; i++)
        {
            m_ragdollColliders[i].enabled = false;
        }
    }

    private void CaptureAndDisableGameplayColliders()
    {
        for (int i = 0; i < m_gameplayColliders.Length; i++)
        {
            Collider collider = m_gameplayColliders[i];
            m_gameplayColliderStates[i] = collider.enabled;
            collider.enabled = false;
        }
    }

    private void RestoreGameplayColliders()
    {
        for (int i = 0; i < m_gameplayColliders.Length; i++)
        {
            m_gameplayColliders[i].enabled = m_gameplayColliderStates[i];
        }
    }
}
