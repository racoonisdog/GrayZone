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
    [Header("Ragdoll Handoff")]
    [Tooltip("애니메이션이 만들던 뼈 속도를 래그돌에 넘길지 여부입니다. 끄면 전환 순간 정지 상태에서 무너져 달리다 죽어도 제자리에 쓰러집니다.")]
    [SerializeField] private bool m_inheritAnimationVelocity = true;

    [Tooltip("인계할 속도에 곱하는 배수입니다. 1이면 애니메이션이 만들던 속도 그대로이고, 낮추면 관성을 덜 싣습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_inheritedVelocityScale = 1.0f;

    [Tooltip("인계할 선속도의 상한(m/s)입니다. 순간이동에 가까운 애니메이션 프레임이 섞이면 속도가 튀므로 상한을 둡니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_maxInheritedSpeed = 10.0f;

    private Animator m_animator;
    private Rigidbody[] m_ragdollBodies = System.Array.Empty<Rigidbody>();
    private Collider[] m_ragdollColliders = System.Array.Empty<Collider>();
    private Collider[] m_gameplayColliders = System.Array.Empty<Collider>();
    private bool[] m_gameplayColliderStates = System.Array.Empty<bool>();
    private bool m_animatorWasEnabled;
    private bool m_isRagdollActive;
    private bool m_gameplayCollidersDisabled;

    /// <summary>직전 프레임의 뼈 위치입니다. 속도 인계 계산에만 씁니다.</summary>
    private Vector3[] m_previousBonePositions = System.Array.Empty<Vector3>();

    /// <summary>직전 프레임의 뼈 회전입니다. 속도 인계 계산에만 씁니다.</summary>
    private Quaternion[] m_previousBoneRotations = System.Array.Empty<Quaternion>();

    /// <summary>직전 프레임 캐시가 채워져 있는지 여부입니다. 첫 프레임에는 계산할 델타가 없습니다.</summary>
    private bool m_hasPreviousBonePose;

    /// <summary>직전 캐시를 기록한 프레임의 간격(초)입니다. 속도 계산의 분모입니다.</summary>
    private float m_previousBoneDeltaTime;

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
    /// 애니메이션이 적용된 뒤의 뼈 자세를 기록해 둡니다.
    /// </summary>
    /// <remarks>
    /// LateUpdate에서 읽는 이유는 이 시점의 뼈 자세가 이번 프레임 애니메이션의 결과이기 때문입니다.
    /// Update에서 읽으면 아직 이전 프레임 자세가 남아 있어 델타가 한 프레임 밀립니다.
    ///
    /// 래그돌이 켜진 뒤에는 기록하지 않습니다. 그 뒤의 자세는 물리가 만드는 것이라 인계할 대상이 아닙니다.
    /// 애니메이터가 컬링으로 멈추면 이 델타는 0이 되므로, 화면 밖에서 죽은 개체는 관성 없이 무너집니다.
    /// 그것까지 맞추려면 프리팹의 Animator Culling Mode가 Always Animate여야 합니다.
    /// </remarks>
    private void LateUpdate()
    {
        if (m_isRagdollActive || !m_inheritAnimationVelocity)
        {
            return;
        }

        CacheBonePose();
    }

    /// <summary>
    /// Animator를 멈추고, 사망 시점의 자세와 속도에서 뼈대 물리 시뮬레이션을 시작합니다.
    /// </summary>
    /// <returns>물리 골격이 준비되어 래그돌을 활성화했으면 true입니다.</returns>
    /// <remarks>
    /// <b>순서가 중요합니다.</b> <c>isKinematic</c>이 true인 동안에는 속도 대입이 무시되므로
    /// (Unity 문서: kinematic 리지드바디의 velocity 설정은 효과가 없음), 반드시 동역학으로 바꾼 뒤에
    /// 속도를 넣어야 합니다. 이전 구현은 속도를 먼저 넣고 나중에 <c>isKinematic</c>을 풀었는데,
    /// 넣는 값이 0이라 결과가 같아 문제가 드러나지 않았습니다.
    ///
    /// 속도를 0으로 지우면 애니메이션이 만들던 관성이 전부 사라져 "멈췄다가 무너지는" 모양이 됩니다.
    /// 달리던 개체가 제자리에 쓰러지는 것이 그 증상입니다.
    /// </remarks>
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

        DisableGameplayColliders();

        m_animatorWasEnabled = m_animator != null && m_animator.enabled;
        if (m_animator != null)
        {
            m_animator.enabled = false;
        }

        for (int i = 0; i < m_ragdollColliders.Length; i++)
        {
            m_ragdollColliders[i].enabled = true;
        }

        // 애니메이터가 만든 마지막 자세를 물리 쪽에 반영한 뒤 시뮬레이션을 시작합니다.
        Physics.SyncTransforms();

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Rigidbody body = m_ragdollBodies[i];

            body.isKinematic = false;
            ApplyInheritedVelocity(body, i);
            body.WakeUp();
        }

        m_isRagdollActive = true;
        return true;
    }

    /// <summary>
    /// 뼈 하나에 직전 프레임 애니메이션이 만들던 속도를 넣습니다.
    /// </summary>
    /// <param name="body">속도를 넣을 뼈 리지드바디입니다.</param>
    /// <param name="index">뼈 배열에서의 순번입니다. 캐시 배열과 같은 순서입니다.</param>
    /// <remarks>
    /// 인계를 껐거나 직전 자세 캐시가 없으면 0으로 시작합니다.
    /// 애니메이션 한 프레임에 뼈가 크게 튀는 경우가 있어 선속도에 상한을 둡니다. 상한이 없으면
    /// 전환 순간 래그돌이 폭발적으로 날아갑니다.
    /// </remarks>
    private void ApplyInheritedVelocity(Rigidbody body, int index)
    {
        if (!m_inheritAnimationVelocity
            || !m_hasPreviousBonePose
            || m_previousBoneDeltaTime <= 0.0f
            || index >= m_previousBonePositions.Length)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return;
        }

        Transform bone = body.transform;

        Vector3 linear = (bone.position - m_previousBonePositions[index])
            / m_previousBoneDeltaTime
            * m_inheritedVelocityScale;

        if (linear.magnitude > m_maxInheritedSpeed)
        {
            linear = linear.normalized * m_maxInheritedSpeed;
        }

        body.linearVelocity = linear;
        body.angularVelocity = CalculateAngularVelocity(
            m_previousBoneRotations[index],
            bone.rotation,
            m_previousBoneDeltaTime) * m_inheritedVelocityScale;
    }

    /// <summary>
    /// 두 회전 사이의 각속도를 구합니다.
    /// </summary>
    /// <param name="previous">직전 프레임의 회전입니다.</param>
    /// <param name="current">현재 회전입니다.</param>
    /// <param name="deltaTime">두 회전 사이의 시간(초)입니다.</param>
    /// <returns>축과 크기를 합친 각속도(라디안/초)입니다.</returns>
    /// <remarks>
    /// 회전 델타를 축·각으로 풀어 각속도로 바꿉니다. 180도를 넘는 각은 반대 방향의 짧은 회전으로
    /// 해석해야 합니다. 그러지 않으면 한 프레임에 살짝 넘어간 회전이 거의 한 바퀴 도는 각속도로 잡힙니다.
    /// </remarks>
    private static Vector3 CalculateAngularVelocity(
        Quaternion previous,
        Quaternion current,
        float deltaTime)
    {
        Quaternion delta = current * Quaternion.Inverse(previous);
        delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);

        if (float.IsInfinity(axis.x) || float.IsNaN(axis.x) || axis.sqrMagnitude < Mathf.Epsilon)
        {
            return Vector3.zero;
        }

        if (angleDegrees > 180.0f)
        {
            angleDegrees -= 360.0f;
        }

        return axis.normalized * (angleDegrees * Mathf.Deg2Rad / deltaTime);
    }

    /// <summary>현재 뼈 자세를 다음 프레임의 델타 계산용으로 기록합니다.</summary>
    private void CacheBonePose()
    {
        if (m_previousBonePositions.Length != m_ragdollBodies.Length)
        {
            m_previousBonePositions = new Vector3[m_ragdollBodies.Length];
            m_previousBoneRotations = new Quaternion[m_ragdollBodies.Length];
            m_hasPreviousBonePose = false;
        }

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Transform bone = m_ragdollBodies[i].transform;
            m_previousBonePositions[i] = bone.position;
            m_previousBoneRotations[i] = bone.rotation;
        }

        m_previousBoneDeltaTime = Time.deltaTime;
        m_hasPreviousBonePose = m_ragdollBodies.Length > 0;
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

        // 물리가 만든 자세를 애니메이션 델타로 오해하지 않도록 캐시를 버립니다.
        // 되살아난 직후 다시 죽으면 그 사이의 자세 변화가 속도로 잡혀 시체가 튑니다.
        m_hasPreviousBonePose = false;
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

    /// <summary>
    /// 래그돌 골격에 속하지 않은 게임플레이 콜라이더만 끄고, 되돌릴 수 있게 원래 상태를 기록합니다.
    /// </summary>
    /// <remarks>
    /// 래그돌 전환보다 먼저 불러도 됩니다. 사망 애니메이션을 재생하는 동안에도 피격·이동 충돌은
    /// 즉시 사라져 있어야 하기 때문입니다.
    /// 두 번 불려도 처음 기록한 원래 상태를 덮어쓰지 않습니다. 덮어쓰면 이미 꺼진 값을 원래 상태로
    /// 기억해 <see cref="DeactivateRagdoll"/>이 콜라이더를 되살리지 못합니다.
    /// </remarks>
    public void DisableGameplayColliders()
    {
        if (m_gameplayCollidersDisabled)
        {
            return;
        }

        for (int i = 0; i < m_gameplayColliders.Length; i++)
        {
            Collider collider = m_gameplayColliders[i];
            m_gameplayColliderStates[i] = collider.enabled;
            collider.enabled = false;
        }

        m_gameplayCollidersDisabled = true;
    }

    private void RestoreGameplayColliders()
    {
        if (!m_gameplayCollidersDisabled)
        {
            return;
        }

        for (int i = 0; i < m_gameplayColliders.Length; i++)
        {
            m_gameplayColliders[i].enabled = m_gameplayColliderStates[i];
        }

        m_gameplayCollidersDisabled = false;
    }
}
