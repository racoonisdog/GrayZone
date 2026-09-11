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
    [SerializeField] private float m_maxInheritedSpeed = 4.0f;

    [Tooltip("인계할 각속도의 상한(라디안/초)입니다. 손발 뼈는 한 프레임에 크게 도는 경우가 있어 상한이 없으면 시체가 팽이처럼 돕니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_maxInheritedAngularSpeed = 12.0f;

    [Tooltip("래그돌로 넘기기 직전에 애니메이터 자세를 한 번 강제로 갱신할지 여부입니다. 화면 밖에서는 뼈 자세가 갱신되지 않아 낡은 자세가 물리로 넘어가면 래그돌이 폭발합니다. 끄면 화면 밖 사망이 튈 수 있습니다.")]
    [SerializeField] private bool m_refreshPoseBeforeHandoff = true;

    [Header("Ragdoll Stability")]
    [Tooltip("물리 시뮬레이션 중 허용할 뼈 선속도 상한(m/s)입니다. 피격 충격과 관절 보정으로 속도가 누적돼 시체가 늘어지는 것을 막습니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_maxSimulationSpeed = 8.0f;

    [Tooltip("물리 시뮬레이션 중 허용할 뼈 각속도 상한(라디안/초)입니다. Rigidbody의 사전 상한과 FixedUpdate 사후 상한을 함께 적용합니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_maxSimulationAngularSpeed = 12.0f;

    [Tooltip("래그돌 뼈마다 적용할 관절·충돌 해석 반복 횟수입니다. 기본 물리 반복 수보다 높여 긴 관절 체인이 늘어나는 현상을 줄입니다.")]
    [Min(1)]
    [SerializeField] private int m_solverIterations = 12;

    [Tooltip("래그돌 뼈마다 적용할 속도 해석 반복 횟수입니다. 관절이 한 프레임에 과도하게 회전하는 현상을 줄입니다.")]
    [Min(1)]
    [SerializeField] private int m_solverVelocityIterations = 4;

    [Tooltip("관절 제약이 어긋났을 때 투영 보정이 허용할 거리(m)입니다. 작을수록 관절 늘어짐을 빠르게 되돌립니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_jointProjectionDistance = 0.02f;

    [Tooltip("관절 제약이 어긋났을 때 투영 보정이 허용할 회전각(도)입니다. 작을수록 과도한 비틀림을 빠르게 되돌립니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_jointProjectionAngle = 5.0f;

    [Header("Hit Impulse")]
    [Tooltip("맞은 부위 외의 나머지 뼈에 함께 실어줄 충격량 비율입니다. 0이면 맞은 부위만 튀어 관절이 뒤틀리고, 1이면 몸 전체가 통째로 밀립니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_impulseSpreadRatio = 0.25f;

    private Animator m_animator;
    private Rigidbody[] m_ragdollBodies = System.Array.Empty<Rigidbody>();
    private CharacterJoint[] m_ragdollJoints = System.Array.Empty<CharacterJoint>();
    private Collider[] m_ragdollColliders = System.Array.Empty<Collider>();
    private Collider[] m_gameplayColliders = System.Array.Empty<Collider>();
    private bool[] m_gameplayColliderStates = System.Array.Empty<bool>();
    private bool m_animatorWasEnabled;
    private bool m_isRagdollActive;
    private bool m_gameplayCollidersDisabled;

    /// <summary>직전 프레임의 뼈 위치입니다. 속도 표본을 만드는 데만 씁니다.</summary>
    private Vector3[] m_previousBonePositions = System.Array.Empty<Vector3>();

    /// <summary>직전 프레임의 뼈 회전입니다. 속도 표본을 만드는 데만 씁니다.</summary>
    private Quaternion[] m_previousBoneRotations = System.Array.Empty<Quaternion>();

    /// <summary>직전 두 프레임 사이에 애니메이션이 만든 뼈별 선속도입니다.</summary>
    private Vector3[] m_boneLinearVelocities = System.Array.Empty<Vector3>();

    /// <summary>직전 두 프레임 사이에 애니메이션이 만든 뼈별 각속도입니다.</summary>
    private Vector3[] m_boneAngularVelocities = System.Array.Empty<Vector3>();

    /// <summary>직전 프레임 자세가 기록돼 있는지 여부입니다. 첫 프레임에는 비교할 대상이 없습니다.</summary>
    private bool m_hasPreviousBonePose;

    /// <summary>속도 표본이 한 번이라도 채워졌는지 여부입니다.</summary>
    private bool m_hasBoneVelocities;

    /// <summary>가시성 판정에 쓰는 렌더러 목록입니다.</summary>
    private Renderer[] m_renderers = System.Array.Empty<Renderer>();

    /// <summary>피격 충격량의 하한입니다. 피격 대상 쪽 정책이며 사망 시 주입됩니다.</summary>
    private float m_minimumHitImpulse;

    /// <summary>Joint로 연결된 래그돌 물리 골격이 준비됐는지 여부입니다.</summary>
    public bool IsConfigured => m_ragdollBodies.Length > 0 && m_ragdollColliders.Length > 0;

    /// <summary>현재 물리 래그돌이 활성화됐는지 여부입니다.</summary>
    public bool IsRagdollActive => m_isRagdollActive;

    /// <summary>
    /// 애니메이션에서 측정된 뼈 선속도 중 가장 큰 값(m/s)입니다. 상한을 적용하기 전의 원값입니다.
    /// </summary>
    /// <remarks>인계가 실제로 측정되는지 확인하는 진단용입니다. 표본이 없으면 0입니다.</remarks>
    public float MeasuredMaxBoneSpeed
    {
        get
        {
            float max = 0.0f;
            for (int i = 0; i < m_boneLinearVelocities.Length; i++)
            {
                max = Mathf.Max(max, m_boneLinearVelocities[i].magnitude);
            }

            return max;
        }
    }

    /// <summary>애니메이션에서 측정된 뼈 각속도 중 가장 큰 값(라디안/초)입니다. 상한 적용 전의 원값입니다.</summary>
    public float MeasuredMaxBoneAngularSpeed
    {
        get
        {
            float max = 0.0f;
            for (int i = 0; i < m_boneAngularVelocities.Length; i++)
            {
                max = Mathf.Max(max, m_boneAngularVelocities[i].magnitude);
            }

            return max;
        }
    }

    private void Awake()
    {
        CacheRagdollParts();
        PrepareAnimationDrivenState();
    }

    /// <summary>
    /// 애니메이션이 만든 뼈 속도를 매 프레임 표본으로 남깁니다.
    /// </summary>
    /// <remarks>
    /// LateUpdate에서 읽는 이유는 이 시점의 뼈 자세가 이번 프레임 애니메이션의 결과이기 때문입니다.
    /// Update에서 읽으면 아직 이전 프레임 자세가 남아 있어 델타가 한 프레임 밀립니다.
    ///
    /// <b>속도를 전환 시점에 계산하지 않고 여기서 미리 구해 두는 것이 중요합니다.</b> 전환은 피격 처리
    /// 도중에 일어나므로 프레임 안에서의 위치가 정해져 있지 않습니다. 전환 시점에 "직전 자세와의 차이"를
    /// 구하면 분자(자세 차이)와 분모(프레임 간격)의 시간 기준이 어긋나 속도가 실제보다 몇 배로 뜁니다.
    /// 실제로 배회 속도 1.2m/s인 개체에서 선속도 9m/s가 찍혀 이 방식으로 바꿨습니다.
    ///
    /// 래그돌이 켜진 뒤에는 표본을 만들지 않습니다. 그 뒤의 자세는 물리가 만드는 것이라 인계 대상이 아닙니다.
    /// 애니메이터가 컬링으로 멈추면 자세가 변하지 않아 속도가 0이 되므로, 화면 밖에서 죽은 개체는 관성 없이
    /// 무너집니다. 그것까지 맞추려면 프리팹의 Animator Culling Mode가 Always Animate여야 합니다.
    /// </remarks>
    private void LateUpdate()
    {
        if (m_isRagdollActive)
        {
            // FixedUpdate 뒤의 PhysX 관절 해석이 속도를 다시 올릴 수 있습니다.
            // 렌더 직전에도 잘라 다음 물리 스텝으로 과속이 이어지지 않게 합니다.
            ClampSimulationVelocities();
            return;
        }

        if (!m_inheritAnimationVelocity)
        {
            return;
        }

        SampleBoneVelocities();
    }

    /// <summary>
    /// 다음 물리 스텝에 들어가기 전에 선속도와 각속도를 안전 범위로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// <see cref="Rigidbody.maxAngularVelocity"/>는 물리 스텝 <b>전</b>에만 적용되므로, 관절 해석이 끝난
    /// 뒤에는 그 상한을 넘을 수 있습니다. FixedUpdate에서는 다음 해석 전 값을 제한하고,
    /// <see cref="LateUpdate"/>에서는 관절 해석 뒤 렌더 직전 값을 다시 제한합니다.
    /// </remarks>
    private void FixedUpdate()
    {
        if (!m_isRagdollActive)
        {
            return;
        }

        ClampSimulationVelocities();
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

        // 애니메이터를 끄기 전에 자세를 최신으로 맞춥니다. 순서를 바꾸면 갱신할 수단이 사라집니다.
        RefreshAnimatorPose();

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

        // 기존에 저장된 프리팹도 생성 도구를 다시 실행하지 않아도 안정화 설정을 받게 합니다.
        ConfigureJointsForRagdoll();

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Rigidbody body = m_ragdollBodies[i];

            ConfigureBodyForRagdoll(body);
            body.isKinematic = false;
            ApplyInheritedVelocity(body, i);
            body.WakeUp();
        }

        m_isRagdollActive = true;
        return true;
    }

    /// <summary>
    /// 피격 충격량의 하한을 지정합니다.
    /// </summary>
    /// <param name="minimumImpulse">무기 넉백이 0이거나 작아도 최소한 이만큼은 적용할 충격량(N·s)입니다.</param>
    /// <remarks>
    /// "얼마나 밀릴지"의 하한은 맞는 쪽 정책이라 무기가 아니라 피격 대상 설정에서 옵니다.
    /// 변이체는 <see cref="EnemyManager"/>의 종류별 슬롯 값이 사망 시 여기로 들어옵니다.
    /// </remarks>
    public void SetMinimumHitImpulse(float minimumImpulse)
    {
        m_minimumHitImpulse = Mathf.Max(0.0f, minimumImpulse);
    }

    /// <summary>래그돌 전환 전후에 켜고 끌 실제 모델 Animator를 지정합니다.</summary>
    public void SetAnimator(Animator animator)
    {
        m_animator = animator;
    }

    /// <summary>
    /// 피격 방향으로 래그돌에 충격량을 가합니다.
    /// </summary>
    /// <param name="direction">공격자에서 피격 지점으로 향하는 방향입니다. 정규화하지 않아도 됩니다.</param>
    /// <param name="hitPoint">피격 지점의 월드 좌표입니다. 부위를 알 수 없을 때 가까운 뼈를 찾는 데 씁니다.</param>
    /// <param name="weaponImpulse">무기가 정한 충격량(N·s)입니다. 하한보다 작으면 하한을 씁니다.</param>
    /// <param name="hitBone">맞은 부위의 리지드바디입니다. 알 수 없으면 null을 넘겨도 됩니다.</param>
    /// <returns>충격량을 적용했으면 true입니다.</returns>
    /// <remarks>
    /// <b>래그돌이 켜져 있을 때만 동작합니다.</b> 살아 있는 개체의 넉백은 뼈가 kinematic이라 물리로 처리할 수
    /// 없으므로 <see cref="EnemyController"/>가 NavMeshAgent 변위로 담당합니다. 이 함수는 사망 후 부가 효과입니다.
    ///
    /// 맞은 부위에 전량을 주고 나머지 뼈에는 <see cref="m_impulseSpreadRatio"/>만큼 나눠 싣습니다.
    /// 한 뼈에만 몰아주면 그 뼈만 튀어 관절이 뒤틀리는 것이 알려진 실패 모드입니다.
    /// 질량 보정은 <see cref="ForceMode.Impulse"/>가 담당하므로 무거운 부위는 자연히 덜 밀립니다.
    ///
    /// 히트박스 콜라이더는 래그돌 뼈의 자식이 아니어서 <c>Hit.rigidbody</c>가 null로 옵니다. 그래서 부위를
    /// 못 받은 경우 피격 지점에서 가장 가까운 뼈를 대신 씁니다. 그러지 않으면 모든 뼈가 균등하게 밀려
    /// 어디를 맞았는지가 연출에 드러나지 않습니다.
    /// </remarks>
    public bool ApplyHitImpulse(
        Vector3 direction,
        Vector3 hitPoint,
        float weaponImpulse,
        Rigidbody hitBone,
        float effectiveMass)
    {
        if (!m_isRagdollActive)
        {
            return false;
        }

        float impulse = Mathf.Max(m_minimumHitImpulse, weaponImpulse);
        if (impulse <= 0.0f || direction.sqrMagnitude <= Mathf.Epsilon || effectiveMass <= 0.0f)
        {
            return false;
        }

        // 충격량을 몸 전체의 속도로 환산합니다. 뼈 질량으로 나누지 않는 이유는 생존 넉백과 같은
        // 유효 질량을 써야 무기 값 하나로 두 경로를 함께 조절할 수 있기 때문입니다.
        // 뼈 질량(부위당 0.5~3kg)으로 나누면 같은 숫자가 시체에서 수십 배 빠르게 나와 튜닝이 갈라집니다.
        float speed = impulse / effectiveMass;
        Rigidbody target = IsRagdollBone(hitBone) ? hitBone : FindNearestBone(hitPoint);
        Vector3 unitDirection = direction.normalized;
        bool applied = false;

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Rigidbody body = m_ragdollBodies[i];
            if (body == null || body.isKinematic)
            {
                continue;
            }

            float factor = body == target ? 1.0f : m_impulseSpreadRatio;
            if (factor <= 0.0f)
            {
                continue;
            }

            // VelocityChange를 쓰는 이유는 위에서 이미 속도로 환산했기 때문입니다. Impulse로 주면
            // 뼈 질량으로 한 번 더 나뉘어 가벼운 손발만 튀어 나갑니다.
            body.AddForce(unitDirection * (speed * factor), ForceMode.VelocityChange);
            applied = true;
        }

        return applied;
    }

    /// <summary>지정한 리지드바디가 이 래그돌 골격에 속하는지 여부입니다.</summary>
    /// <remarks>외부에서 넘어온 리지드바디가 무기나 다른 오브젝트일 수 있어 확인합니다.</remarks>
    private bool IsRagdollBone(Rigidbody body)
    {
        if (body == null)
        {
            return false;
        }

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            if (m_ragdollBodies[i] == body)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>피격 지점에서 가장 가까운 래그돌 뼈를 찾습니다. 없으면 null입니다.</summary>
    /// <remarks>
    /// 뼈 원점까지의 거리로 고릅니다. 뼈가 12개 남짓이라 선형 탐색으로 충분하고, 부위 구분에는
    /// 원점 거리만으로도 충분합니다(머리를 맞으면 머리 뼈가, 다리를 맞으면 다리 뼈가 가장 가깝습니다).
    /// </remarks>
    private Rigidbody FindNearestBone(Vector3 hitPoint)
    {
        Rigidbody nearest = null;
        float nearestSqr = float.MaxValue;

        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Rigidbody body = m_ragdollBodies[i];
            if (body == null)
            {
                continue;
            }

            float sqr = (body.worldCenterOfMass - hitPoint).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = body;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 래그돌로 넘기기 직전에 뼈 자세를 현재 애니메이션 시점으로 맞춥니다.
    /// </summary>
    /// <remarks>
    /// <b>화면 밖 사망 대응입니다.</b> Animator Culling Mode가 <c>CullUpdateTransforms</c>면 렌더러가 보이지
    /// 않는 동안 뼈 Transform을 쓰지 않습니다. 클립 시간은 계속 흐르므로 상태 머신과 실제 뼈 자세가
    /// 어긋나고, 그 낡은 자세를 물리로 넘기면 관절 제한을 위반해 래그돌이 폭발합니다.
    /// 실측으로 선속도 120m/s까지 튀었습니다.
    ///
    /// 컬링을 잠깐 풀고 <c>Update(0)</c>으로 한 프레임 평가만 시킨 뒤 원래 모드로 돌려놓습니다.
    /// 상시 <c>AlwaysAnimate</c>와 달리 비용이 사망 순간 1회로 끝납니다.
    /// 시간을 0만큼 진행시키므로 클립 진행도는 그대로이고 자세만 현재 시점으로 써집니다.
    /// </remarks>
    private void RefreshAnimatorPose()
    {
        if (!m_refreshPoseBeforeHandoff || m_animator == null || !m_animator.enabled)
        {
            return;
        }

        AnimatorCullingMode previousMode = m_animator.cullingMode;
        m_animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        m_animator.Update(0.0f);
        m_animator.cullingMode = previousMode;

        // 자세를 여기서 갱신했다면 직전 표본은 갱신 전 자세로 만든 것이라 이번 갱신량이 속도에 섞입니다.
        // 화면 밖에 오래 있었으면 그 양이 몇 초 분이라 속도가 통째로 허수가 되므로 표본을 버립니다.
        // 상한이 있어 폭발까지 가지는 않지만, 상한에 붙은 값은 방향만 맞고 크기는 의미가 없습니다.
        if (!IsVisibleToAnyRenderer())
        {
            m_hasBoneVelocities = false;
        }
    }

    /// <summary>
    /// 이 캐릭터의 렌더러 중 하나라도 카메라에 보이는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 자세 갱신이 필요했는지를 사후에 가리는 데만 씁니다. Editor에서는 Scene 뷰도 보는 것으로 세므로
    /// 판정이 관대해질 수 있는데, 그 경우 표본을 살리는 쪽으로 기울고 상한이 뒤를 받칩니다.
    /// </remarks>
    private bool IsVisibleToAnyRenderer()
    {
        for (int i = 0; i < m_renderers.Length; i++)
        {
            if (m_renderers[i] != null && m_renderers[i].isVisible)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 뼈 하나에 애니메이션이 만들던 속도 표본을 넣습니다.
    /// </summary>
    /// <param name="body">속도를 넣을 뼈 리지드바디입니다.</param>
    /// <param name="index">뼈 배열에서의 순번입니다. 표본 배열과 같은 순서입니다.</param>
    /// <remarks>
    /// 인계를 껐거나 표본이 아직 없으면 0으로 시작합니다.
    /// 상한은 여기서 겁니다. 표본을 만들 때가 아니라 적용할 때 걸어야 인스펙터에서 상한을 바꾼 것이
    /// 다음 사망에 바로 반영됩니다.
    /// </remarks>
    private void ApplyInheritedVelocity(Rigidbody body, int index)
    {
        if (!m_inheritAnimationVelocity
            || !m_hasBoneVelocities
            || index >= m_boneLinearVelocities.Length)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return;
        }

        body.linearVelocity = ClampMagnitude(
            m_boneLinearVelocities[index] * m_inheritedVelocityScale,
            m_maxInheritedSpeed);

        body.angularVelocity = ClampMagnitude(
            m_boneAngularVelocities[index] * m_inheritedVelocityScale,
            m_maxInheritedAngularSpeed);
    }

    /// <summary>
    /// 래그돌 본 하나에 관절 안정화용 물리 상한을 적용합니다.
    /// </summary>
    /// <remarks>
    /// 생성 도구가 만든 옛 프리팹에도 동일하게 적용해야 하므로, 프리팹 직렬화 값만 믿지 않고
    /// 래그돌 전환 순간에 다시 설정합니다. <see cref="Rigidbody.maxAngularVelocity"/>는 다음 물리
    /// 스텝 전에 작동하고, 실제 사후 제한은 <see cref="ClampSimulationVelocities"/>가 담당합니다.
    /// </remarks>
    private void ConfigureBodyForRagdoll(Rigidbody body)
    {
        body.maxAngularVelocity = m_maxSimulationAngularSpeed;
        body.solverIterations = m_solverIterations;
        body.solverVelocityIterations = m_solverVelocityIterations;
    }

    /// <summary>래그돌 관절의 제약 전처리와 투영 보정을 활성화합니다.</summary>
    /// <remarks>
    /// 전처리는 PhysX가 풀기 어려운 제약을 정리하고, 투영은 반복 해석 뒤에도 남은 관절 간격을 되돌립니다.
    /// 둘 다 래그돌을 생성할 때의 기본값이지만, 이미 만들어진 프리팹에도 적용해야 하므로 활성화 순간에
    /// 한 번 더 보정합니다.
    /// </remarks>
    private void ConfigureJointsForRagdoll()
    {
        for (int i = 0; i < m_ragdollJoints.Length; i++)
        {
            CharacterJoint joint = m_ragdollJoints[i];
            if (joint == null)
            {
                continue;
            }

            joint.enablePreprocessing = true;
            joint.enableProjection = true;
            joint.projectionDistance = m_jointProjectionDistance;
            joint.projectionAngle = m_jointProjectionAngle;
        }
    }

    /// <summary>래그돌 전체의 시뮬레이션 속도를 인스펙터 상한 안으로 제한합니다.</summary>
    /// <remarks>Rigidbody를 직접 움직이는 기능이 아니라, 이미 물리가 계산한 과도한 속도만 잘라 냅니다.</remarks>
    private void ClampSimulationVelocities()
    {
        for (int i = 0; i < m_ragdollBodies.Length; i++)
        {
            Rigidbody body = m_ragdollBodies[i];
            if (body == null || body.isKinematic)
            {
                continue;
            }

            body.linearVelocity = ClampMagnitude(body.linearVelocity, m_maxSimulationSpeed);
            body.angularVelocity = ClampMagnitude(body.angularVelocity, m_maxSimulationAngularSpeed);
        }
    }

    /// <summary>벡터의 크기를 상한으로 자릅니다. 방향은 유지합니다.</summary>
    private static Vector3 ClampMagnitude(Vector3 value, float maxMagnitude)
    {
        float magnitude = value.magnitude;
        return magnitude > maxMagnitude && magnitude > Mathf.Epsilon
            ? value / magnitude * maxMagnitude
            : value;
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

    /// <summary>
    /// 직전 프레임 자세와 비교해 뼈별 속도를 구하고, 현재 자세를 다음 비교용으로 남깁니다.
    /// </summary>
    /// <remarks>
    /// 분자와 분모가 같은 한 프레임을 가리키므로 이 값이 곧 애니메이션이 만든 속도입니다.
    /// 상한은 적용 시점(<see cref="ApplyInheritedVelocity"/>)에서 걸고 여기서는 원값을 남깁니다.
    /// </remarks>
    private void SampleBoneVelocities()
    {
        int count = m_ragdollBodies.Length;
        if (count == 0)
        {
            return;
        }

        EnsureVelocitySampleBuffers(count);

        float deltaTime = Time.deltaTime;
        bool canMeasure = m_hasPreviousBonePose && deltaTime > 0.0f;

        for (int i = 0; i < count; i++)
        {
            Transform bone = m_ragdollBodies[i].transform;
            Vector3 position = bone.position;
            Quaternion rotation = bone.rotation;

            if (canMeasure)
            {
                m_boneLinearVelocities[i] = (position - m_previousBonePositions[i]) / deltaTime;
                m_boneAngularVelocities[i] = CalculateAngularVelocity(
                    m_previousBoneRotations[i],
                    rotation,
                    deltaTime);
            }

            m_previousBonePositions[i] = position;
            m_previousBoneRotations[i] = rotation;
        }

        m_hasPreviousBonePose = true;
        m_hasBoneVelocities = m_hasBoneVelocities || canMeasure;
    }

    /// <summary>속도 표본 버퍼를 뼈 수에 맞춥니다. 크기가 바뀌면 기존 표본은 버립니다.</summary>
    private void EnsureVelocitySampleBuffers(int count)
    {
        if (m_previousBonePositions.Length == count)
        {
            return;
        }

        m_previousBonePositions = new Vector3[count];
        m_previousBoneRotations = new Quaternion[count];
        m_boneLinearVelocities = new Vector3[count];
        m_boneAngularVelocities = new Vector3[count];
        m_hasPreviousBonePose = false;
        m_hasBoneVelocities = false;
    }

    /// <summary>
    /// 풀에서 재사용할 캐릭터의 래그돌 물리와 뼈 자세를 Animator 기준 상태로 즉시 복원합니다.
    /// </summary>
    /// <remarks>
    /// 래그돌 해제만으로는 물리가 마지막으로 기록한 뼈 Transform이 다음 Animator 갱신 전까지 남습니다.
    /// 풀 스폰은 루트 위치를 먼저 옮긴 뒤 같은 프레임에 재활성화하므로, 이 메서드가 Rebind와 0초 갱신으로
    /// 사망 자세가 새 스폰 지점에 한 프레임 보이는 현상을 막습니다. 실제 래그돌을 활성화했던 경우에만
    /// Animator 상태를 재설정하므로, 최초 스폰의 Animator 진행 상태에는 영향을 주지 않습니다.
    /// </remarks>
    public void ResetForReuse()
    {
        if (!m_isRagdollActive)
        {
            return;
        }

        DeactivateRagdoll();

        if (m_animator == null || !m_animator.enabled)
        {
            return;
        }

        m_animator.Rebind();
        m_animator.Update(0.0f);
        Physics.SyncTransforms();
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

        // 물리가 만든 자세를 애니메이션 델타로 오해하지 않도록 표본을 버립니다.
        // 되살아난 직후 다시 죽으면 그 사이의 자세 변화가 속도로 잡혀 시체가 튑니다.
        m_hasPreviousBonePose = false;
        m_hasBoneVelocities = false;
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
        if (m_animator == null)
        {
            Animator[] animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator candidate = animators[i];
                if (candidate.transform != transform
                    && candidate.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                {
                    m_animator = candidate;
                    break;
                }
            }

            m_animator ??= GetComponent<Animator>();
        }

        m_renderers = GetComponentsInChildren<Renderer>(true);

        Joint[] joints = GetComponentsInChildren<Joint>(true);
        m_ragdollJoints = GetComponentsInChildren<CharacterJoint>(true);
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
