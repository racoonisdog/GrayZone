using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// 청솔의 샷건 발사 애니메이션을 실제 발사 성공 시점과 동기화합니다.
/// </summary>
/// <remarks>
/// 반자동 입력의 <c>IsShoot</c>는 한 프레임만 유지되므로 긴 펌프 액션을 끝까지 재생할 수 없습니다.
/// 이 컴포넌트는 <see cref="Gun.OnShotFired"/>를 기준으로 전용 Recoil Layer 상태를 직접 재생하고,
/// 무기의 실제 발사 간격에 맞춰 클립 속도를 조절합니다. 다른 캐릭터의 공용 사격 구조에는 관여하지 않습니다.
/// </remarks>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(AimController))]
public sealed class ChungSolShotgunAnimationDriver : MonoBehaviour
{
    private const int RecoilLayerIndex = 2;

    private static readonly int ShootSpeedHash = Animator.StringToHash("ShootSpeed");
    private static readonly int IsDownHash = Animator.StringToHash("IsDown");
    private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
    private static readonly int StandStateHash = Animator.StringToHash("Recoil Layer.Shotgun Shoot Stand");
    private static readonly int CrouchStateHash = Animator.StringToHash("Recoil Layer.Shotgun Shoot Crouch");
    private static readonly int NoRecoilStateHash = Animator.StringToHash("Recoil Layer.No Recoil");

    [Header("Shot Animation")]
    [Tooltip("Shoot01/Shoot03 원본 클립의 1배속 길이입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_sourceClipDuration = 1.1f;

    [Tooltip("발사 상태로 들어갈 때 사용하는 짧은 크로스페이드 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_enterBlendDuration = 0.02f;

    [Tooltip("발사 사이클 종료 후 No Recoil로 돌아갈 때 사용하는 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_exitBlendDuration = 0.04f;

    [Header("Left Hand Pump IK")]
    [Tooltip("펌프 구간에만 해제할 왼손 Two Bone IK입니다. 비어 있으면 이름으로 자동 탐색합니다.")]
    [SerializeField] private TwoBoneIKConstraint m_leftHandIk;

    [Tooltip("클립 정규화 시간 기준 왼손 IK 해제를 시작하는 지점입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_ikFadeOutStart = 0.38f;

    [Tooltip("클립 정규화 시간 기준 왼손 IK가 완전히 해제되는 지점입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_ikReleasedStart = 0.45f;

    [Tooltip("클립 정규화 시간 기준 왼손 IK 복귀를 시작하는 지점입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_ikRestoreStart = 0.72f;

    [Tooltip("클립 정규화 시간 기준 왼손 IK가 완전히 복귀하는 지점입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_ikRestoreEnd = 0.85f;

    private Animator m_animator;
    private AimController m_aimController;
    private ThirdPersonController m_controller;
    private Gun m_boundGun;
    private float m_leftHandIkRestWeight = 1.0f;
    private float m_cycleStartTime;
    private float m_cycleDuration;
    private float m_returnStartTime;
    private bool m_cycleActive;
    private bool m_returningToIdle;

    private void Awake()
    {
        m_animator = GetComponent<Animator>();
        m_aimController = GetComponent<AimController>();
        m_controller = GetComponent<ThirdPersonController>();
        ResolveLeftHandIk();
    }

    private void OnEnable()
    {
        RefreshGunBinding();
    }

    private void Start()
    {
        // AimController가 Awake에서 총기 참조를 캐싱하므로 생명주기 순서와 무관하게 한 번 더 연결합니다.
        RefreshGunBinding();
    }

    private void OnDisable()
    {
        UnbindGun();
        RestoreNeutralPose();
    }

    private void OnValidate()
    {
        m_sourceClipDuration = Mathf.Max(0.01f, m_sourceClipDuration);
        m_enterBlendDuration = Mathf.Max(0.0f, m_enterBlendDuration);
        m_exitBlendDuration = Mathf.Max(0.0f, m_exitBlendDuration);

        m_ikReleasedStart = Mathf.Max(m_ikFadeOutStart, m_ikReleasedStart);
        m_ikRestoreStart = Mathf.Max(m_ikReleasedStart, m_ikRestoreStart);
        m_ikRestoreEnd = Mathf.Max(m_ikRestoreStart, m_ikRestoreEnd);
    }

    private void Update()
    {
        RefreshGunBinding();

        if (!m_cycleActive && !m_returningToIdle)
        {
            return;
        }

        if (ShouldInterruptShotAnimation())
        {
            RestoreNeutralPose();
            return;
        }

        if (m_cycleActive)
        {
            float normalizedTime = Mathf.Clamp01((Time.time - m_cycleStartTime) / m_cycleDuration);
            ApplyRecoilLayerWeight(1.0f);
            ApplyLeftHandIk(normalizedTime);

            if (normalizedTime >= 1.0f)
            {
                BeginReturnToIdle();
            }

            return;
        }

        float returnDuration = Mathf.Max(0.0001f, m_exitBlendDuration);
        float returnProgress = Mathf.Clamp01((Time.time - m_returnStartTime) / returnDuration);
        ApplyRecoilLayerWeight(1.0f - returnProgress);
        RestoreLeftHandIk(returnProgress);

        if (returnProgress >= 1.0f)
        {
            m_returningToIdle = false;
            ApplyRecoilLayerWeight(0.0f);
            RestoreLeftHandIk(1.0f);
        }
    }

    private void RefreshGunBinding()
    {
        Gun currentGun = m_aimController != null ? m_aimController.EquippedGun : null;
        if (currentGun == m_boundGun)
        {
            return;
        }

        UnbindGun();
        m_boundGun = currentGun;

        if (m_boundGun != null)
        {
            m_boundGun.OnShotFired += OnShotFired;
        }
    }

    private void UnbindGun()
    {
        if (m_boundGun != null)
        {
            m_boundGun.OnShotFired -= OnShotFired;
            m_boundGun = null;
        }
    }

    private void OnShotFired(Gun.HitscanShotInfo _)
    {
        if (m_boundGun == null || m_boundGun.WeaponType != WeaponType.Shotgun
            || m_animator == null || m_animator.layerCount <= RecoilLayerIndex)
        {
            return;
        }

        bool crouching = m_controller != null
            ? m_controller.IsCrouchActive
            : m_animator.GetBool(Animator.StringToHash("IsCrouch"));
        int stateHash = crouching ? CrouchStateHash : StandStateHash;

        if (!m_animator.HasState(RecoilLayerIndex, stateHash))
        {
            Debug.LogWarning($"[{nameof(ChungSolShotgunAnimationDriver)}] 전용 샷건 상태를 찾지 못했습니다: {stateHash}", this);
            return;
        }

        bool animationWasActive = m_cycleActive || m_returningToIdle;
        if (!animationWasActive && m_leftHandIk != null)
        {
            m_leftHandIkRestWeight = m_leftHandIk.weight;
        }

        m_cycleDuration = Mathf.Max(0.05f, m_boundGun.ShootDelay);
        m_cycleStartTime = Time.time;
        m_cycleActive = true;
        m_returningToIdle = false;

        m_animator.SetFloat(ShootSpeedHash, m_sourceClipDuration / m_cycleDuration);
        m_animator.CrossFadeInFixedTime(stateHash, m_enterBlendDuration, RecoilLayerIndex, 0.0f);

        ApplyRecoilLayerWeight(1.0f);
        ApplyLeftHandIk(0.0f);
    }

    private bool ShouldInterruptShotAnimation()
    {
        if (m_boundGun == null || m_boundGun.WeaponType != WeaponType.Shotgun || m_boundGun.IsReloading)
        {
            return true;
        }

        return m_animator == null || m_animator.GetBool(IsDownHash) || m_animator.GetBool(IsDeadHash);
    }

    private void BeginReturnToIdle()
    {
        m_cycleActive = false;
        m_returningToIdle = true;
        m_returnStartTime = Time.time;

        if (m_animator != null && m_animator.HasState(RecoilLayerIndex, NoRecoilStateHash))
        {
            m_animator.CrossFadeInFixedTime(NoRecoilStateHash, m_exitBlendDuration, RecoilLayerIndex, 0.0f);
        }

        if (m_exitBlendDuration <= 0.0f)
        {
            m_returningToIdle = false;
            ApplyRecoilLayerWeight(0.0f);
            RestoreLeftHandIk(1.0f);
        }
    }

    private void RestoreNeutralPose()
    {
        m_cycleActive = false;
        m_returningToIdle = false;

        if (m_animator != null && m_animator.layerCount > RecoilLayerIndex)
        {
            if (m_animator.HasState(RecoilLayerIndex, NoRecoilStateHash))
            {
                m_animator.Play(NoRecoilStateHash, RecoilLayerIndex, 0.0f);
            }

            ApplyRecoilLayerWeight(0.0f);
        }

        RestoreLeftHandIk(1.0f);
    }

    private void ApplyRecoilLayerWeight(float normalizedWeight)
    {
        if (m_animator == null || m_animator.layerCount <= RecoilLayerIndex)
        {
            return;
        }

        float configuredWeight = m_aimController != null ? m_aimController.RecoilAnimationWeight : 1.0f;
        m_animator.SetLayerWeight(RecoilLayerIndex, Mathf.Clamp01(normalizedWeight) * configuredWeight);
    }

    private void ApplyLeftHandIk(float normalizedTime)
    {
        if (m_leftHandIk == null)
        {
            return;
        }

        float weightScale;
        if (normalizedTime <= m_ikFadeOutStart)
        {
            weightScale = 1.0f;
        }
        else if (normalizedTime < m_ikReleasedStart)
        {
            weightScale = 1.0f - Mathf.InverseLerp(m_ikFadeOutStart, m_ikReleasedStart, normalizedTime);
        }
        else if (normalizedTime <= m_ikRestoreStart)
        {
            weightScale = 0.0f;
        }
        else if (normalizedTime < m_ikRestoreEnd)
        {
            weightScale = Mathf.InverseLerp(m_ikRestoreStart, m_ikRestoreEnd, normalizedTime);
        }
        else
        {
            weightScale = 1.0f;
        }

        m_leftHandIk.weight = m_leftHandIkRestWeight * weightScale;
    }

    private void RestoreLeftHandIk(float normalizedProgress)
    {
        if (m_leftHandIk != null)
        {
            m_leftHandIk.weight = Mathf.Lerp(m_leftHandIk.weight, m_leftHandIkRestWeight, normalizedProgress);
        }
    }

    private void ResolveLeftHandIk()
    {
        if (m_leftHandIk == null)
        {
            Transform humanoidLeftHand = m_animator != null && m_animator.isHuman
                ? m_animator.GetBoneTransform(HumanBodyBones.LeftHand)
                : null;
            TwoBoneIKConstraint[] constraints = GetComponentsInChildren<TwoBoneIKConstraint>(true);
            TwoBoneIKConstraint namedFallback = null;

            for (int i = 0; i < constraints.Length; i++)
            {
                TwoBoneIKConstraint candidate = constraints[i];
                if (candidate.name != "Left Hand IK")
                {
                    continue;
                }

                namedFallback ??= candidate;

                // Final 씬은 프리팹의 Hand Rig를 씬 전용 Rig로 교체합니다. 이름만 같은 잔존 제약보다
                // 실제 Humanoid 왼손 뼈를 tip으로 쓰는 활성 제약을 우선해 씬/프리팹 양쪽에서 안전하게 찾습니다.
                if (candidate.gameObject.activeInHierarchy
                    && (humanoidLeftHand == null || candidate.data.tip == humanoidLeftHand))
                {
                    m_leftHandIk = candidate;
                    break;
                }
            }

            m_leftHandIk ??= namedFallback;
        }

        if (m_leftHandIk != null)
        {
            m_leftHandIkRestWeight = m_leftHandIk.weight;
        }
    }
}
