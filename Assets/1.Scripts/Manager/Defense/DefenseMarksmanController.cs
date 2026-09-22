using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>
/// 방어전 배치 NPC가 제자리에서 가시 적을 탐색하고, 몸을 돌려 조준한 뒤 장착 총기로 사격하게 합니다.
/// </summary>
/// <remarks>
/// 스쿼드 합류, 이동, 구조, 조작 캐릭터 전환에는 참여하지 않습니다. 실제 명중 판정과 무기 피드백은
/// <see cref="Gun.TryLayShoot"/>에 위임하므로 플레이어와 같은 총기 규칙을 사용합니다.
/// </remarks>
public class DefenseMarksmanController : MonoBehaviour
{
    private const int ActionLayerIndex = 1;
    private const int RecoilLayerIndex = 2;

    private static readonly int AnimIDMoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int AnimIDMotionSpeed = Animator.StringToHash("MotionSpeed");
    private static readonly int AnimIDMoveX = Animator.StringToHash("MoveX");
    private static readonly int AnimIDMoveZ = Animator.StringToHash("MoveZ");
    private static readonly int AnimIDIsMove = Animator.StringToHash("IsMove");
    private static readonly int AnimIDIsRun = Animator.StringToHash("IsRun");
    private static readonly int AnimIDIsGrounded = Animator.StringToHash("IsGrounded");
    private static readonly int AnimIDIsAim = Animator.StringToHash("IsAim");
    private static readonly int AnimIDIsShoot = Animator.StringToHash("IsShoot");
    private static readonly int AnimIDIsReload = Animator.StringToHash("IsReload");
    private static readonly int AnimIDDoReload = Animator.StringToHash("DoReload");
    private static readonly int AnimIDReloadSpeed = Animator.StringToHash("ReloadSpeed");

    [Foldout("References")]
    [Tooltip("사수가 사용할 총기입니다. 비워 두면 자식에서 Gun을 자동으로 찾습니다.")]
    [SerializeField] private Gun m_weapon;

    [Tooltip("시야 판정 시작점입니다. 비워 두면 Humanoid 머리 본, 그것도 없으면 루트 높이를 사용합니다.")]
    [SerializeField] private Transform m_eyePoint;

    [Tooltip("사격 자세를 재생하는 Animator입니다. 비워 두면 같은 오브젝트에서 자동으로 찾습니다.")]
    [SerializeField] private Animator m_animator;
    [SerializeField] private ThirdPersonController m_pitchReference;

    [Foldout("Targeting Options")]
    [Tooltip("적을 탐색하고 사격할 최대 거리(m)입니다. 실제 사거리는 총기의 HitscanRange도 함께 제한합니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_sightRange = 45.0f;

    [Tooltip("현재 정면을 기준으로 탐색할 수평 시야각(도)입니다. 360이면 전 방향을 탐색합니다.")]
    [Range(1.0f, 360.0f)]
    [SerializeField] private float m_sightAngle = 360.0f;

    [Tooltip("가시 적 후보를 다시 비교하는 간격(초)입니다.")]
    [Min(0.02f)]
    [SerializeField] private float m_targetScanInterval = 0.2f;

    [Tooltip("적 루트 위치에서 위로 올려 잡을 조준점 높이(m)입니다.")]
    [SerializeField] private float m_targetAimHeight = 1.0f;

    [Foldout("Aim Options")]
    [Tooltip("적 방향으로 몸을 돌리는 초당 보간 속도입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_rotationSpeed = 12.0f;

    [Tooltip("사격을 허용할 수평 조준 오차(도)입니다.")]
    [Range(0.1f, 45.0f)]
    [SerializeField] private float m_aimToleranceAngle = 8.0f;

    [Foldout("Aim Pose Options")]
    [Range(1.0f, 89.0f)]
    [SerializeField] private float m_maxAimUpPitch = 70.0f;

    [Range(1.0f, 89.0f)]
    [SerializeField] private float m_maxAimDownPitch = 30.0f;

    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_chestPitchWeight = 0.35f;

    [Tooltip("일반 봇과 같은 Additive 반동 레이어의 적용 강도입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_recoilAnimationWeight = 1.0f;

    [Foldout("Fire Options")]
    [Tooltip("한 번 연사를 시작했을 때 계속 쏘는 시간(초)입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_burstDuration = 0.8f;

    [Tooltip("연사 구간 사이에 사격을 쉬는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_burstRestDuration = 0.5f;

    [Tooltip("연사 구간마다 조준점에 더하는 최대 무작위 오차 반경(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_aimErrorRadius = 0.25f;

    [Foldout("Debug")]
    [Tooltip("선택했을 때 탐색 반경과 현재 조준선을 Scene 뷰에 표시합니다.")]
    [SerializeField] private bool m_drawDebugGizmos;

    private EnemyController m_currentTarget;
    private Vector3 m_currentAimPoint;
    private Transform m_cachedHead;
    private Transform m_cachedChest;
    private Transform m_cachedUpperChest;
    private Quaternion m_preAimChestLocalRotation;
    private Quaternion m_preAimUpperChestLocalRotation;
    private bool m_hasAimPose;
    private float m_nextTargetScanTime;
    private float m_burstPhaseEndTime;
    private bool m_isBursting;
    private Vector3 m_aimErrorOffset;
    private bool m_fireRequested;
    private Vector3 m_requestedAimPoint;
    private bool m_reloadVisualActive;
    private float m_reloadEventTimeAtUnitSpeed = -1.0f;
    private readonly HashSet<int> m_boolParameters = new HashSet<int>();
    private readonly HashSet<int> m_floatParameters = new HashSet<int>();
    private readonly HashSet<int> m_triggerParameters = new HashSet<int>();

    /// <summary>현재 조준 중인 적입니다. 가시 적이 없으면 <c>null</c>입니다.</summary>
    public EnemyController CurrentTarget => m_currentTarget;

    /// <summary>현재 적에게 사용하는 월드 조준점입니다.</summary>
    public Vector3 CurrentAimPoint => m_currentAimPoint;

    /// <summary>현재 수평 회전이 사격 허용 오차 안에 들어왔는지 여부입니다.</summary>
    public bool IsAimAligned => m_currentTarget != null && GetAimAngleError() <= m_aimToleranceAngle;

    private void Awake()
    {
        CacheReferences();

        if (m_weapon == null)
        {
            Debug.LogError("[DefenseMarksmanController] 자식에서 Gun을 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        // 배치 사수는 애니메이션 루트 이동을 사용하지 않고 현재 위치를 지킵니다.
        if (m_animator != null)
        {
            m_animator.applyRootMotion = false;
            CacheAnimatorCapabilities();
            InitializeStationaryAnimatorState();
        }

        m_weapon.OnReloadCompleted += OnWeaponReloadCompleted;
    }

    private void OnValidate()
    {
        m_sightRange = Mathf.Max(0.1f, m_sightRange);
        m_sightAngle = Mathf.Clamp(m_sightAngle, 1.0f, 360.0f);
        m_targetScanInterval = Mathf.Max(0.02f, m_targetScanInterval);
        m_rotationSpeed = Mathf.Max(0.1f, m_rotationSpeed);
        m_aimToleranceAngle = Mathf.Clamp(m_aimToleranceAngle, 0.1f, 45.0f);
        m_maxAimUpPitch = Mathf.Clamp(m_maxAimUpPitch, 1.0f, 89.0f);
        m_maxAimDownPitch = Mathf.Clamp(m_maxAimDownPitch, 1.0f, 89.0f);
        m_chestPitchWeight = Mathf.Clamp01(m_chestPitchWeight);
        m_recoilAnimationWeight = Mathf.Clamp01(m_recoilAnimationWeight);
        m_burstDuration = Mathf.Max(0.05f, m_burstDuration);
        m_burstRestDuration = Mathf.Max(0.0f, m_burstRestDuration);
        m_aimErrorRadius = Mathf.Max(0.0f, m_aimErrorRadius);
    }

    private void Update()
    {
        RestoreAimPose();
        m_fireRequested = false;

        if (m_weapon == null)
        {
            return;
        }

        if (Time.time >= m_nextTargetScanTime)
        {
            m_nextTargetScanTime = Time.time + m_targetScanInterval;
            m_currentTarget = SelectNearestVisibleTarget();
        }

        if (!TryResolveCurrentAimPoint(out m_currentAimPoint))
        {
            StopBurst();
            ApplyCombatAnimation(false, false);
            return;
        }

        RotateToward(m_currentAimPoint);

        if (m_weapon.CurrentBullet <= 0)
        {
            StopBurst();
            if (m_weapon.CanReload)
            {
                BeginReload();
            }

            ApplyCombatAnimation(true, false);

            return;
        }

        if (m_weapon.IsReloading || !IsAimAligned)
        {
            StopBurst();
            ApplyCombatAnimation(true, false);
            return;
        }

        UpdateBurstPhase();
        if (!m_isBursting)
        {
            ApplyCombatAnimation(true, false);
            return;
        }

        m_requestedAimPoint = m_currentAimPoint + m_aimErrorOffset;
        m_fireRequested = true;
        ApplyCombatAnimation(true, true);
    }

    private void LateUpdate()
    {
        ApplyAimPose();

        if (m_fireRequested)
        {
            TryFire(m_requestedAimPoint);
        }
    }

    private void CacheReferences()
    {
        if (m_weapon == null)
        {
            m_weapon = GetComponentInChildren<Gun>(true);
        }

        if (m_animator == null)
        {
            m_animator = GetComponent<Animator>();
        }

        if (m_animator != null && m_animator.isHuman)
        {
            m_cachedHead = m_animator.GetBoneTransform(HumanBodyBones.Head);
            m_cachedChest = m_animator.GetBoneTransform(HumanBodyBones.Chest);
            m_cachedUpperChest = m_animator.GetBoneTransform(HumanBodyBones.UpperChest);
        }

        if (m_pitchReference == null)
        {
            m_pitchReference = FindAnyObjectByType<ThirdPersonController>(FindObjectsInactive.Include);
        }
    }

    /// <summary>제자리 사수의 하체를 접지 Idle로 고정하고 전투용 상·하체 레이어를 초기화합니다.</summary>
    private void InitializeStationaryAnimatorState()
    {
        SetAnimatorFloat(AnimIDMoveSpeed, 0.0f);
        SetAnimatorFloat(AnimIDMotionSpeed, 1.0f);
        SetAnimatorFloat(AnimIDMoveX, 0.0f);
        SetAnimatorFloat(AnimIDMoveZ, 0.0f);
        SetAnimatorBool(AnimIDIsMove, false);
        SetAnimatorBool(AnimIDIsRun, false);
        SetAnimatorBool(AnimIDIsGrounded, true);
        SetAnimatorBool(AnimIDIsAim, false);
        SetAnimatorBool(AnimIDIsShoot, false);
        SetAnimatorBool(AnimIDIsReload, false);
        SetLayerWeight(ActionLayerIndex, 0.0f);
        SetLayerWeight(RecoilLayerIndex, 0.0f);
    }

    /// <summary>일반 스쿼드 봇과 같은 Base 조준 + Additive 반동 레이어 조합을 적용합니다.</summary>
    private void ApplyCombatAnimation(bool inCombat, bool shooting)
    {
        if (m_animator == null)
        {
            return;
        }

        if (m_reloadVisualActive && (m_weapon == null || !m_weapon.IsReloading))
        {
            FinishReloadVisualState(false);
        }

        SetAnimatorBool(AnimIDIsGrounded, true);
        SetAnimatorBool(AnimIDIsMove, false);
        SetAnimatorBool(AnimIDIsRun, false);

        if (m_reloadVisualActive)
        {
            SetAnimatorBool(AnimIDIsAim, true);
            SetAnimatorBool(AnimIDIsShoot, false);
            SetLayerWeight(ActionLayerIndex, 1.0f);
            SetLayerWeight(RecoilLayerIndex, 0.0f);
            return;
        }

        SetAnimatorBool(AnimIDIsAim, inCombat);
        SetAnimatorBool(AnimIDIsShoot, inCombat && shooting);
        SetLayerWeight(ActionLayerIndex, 0.0f);
        SetLayerWeight(RecoilLayerIndex, inCombat && shooting ? m_recoilAnimationWeight : 0.0f);
    }

    /// <summary>무기 타이머와 상체 재장전 상태를 함께 시작합니다.</summary>
    private void BeginReload()
    {
        if (m_weapon == null || !m_weapon.CanReload || m_reloadVisualActive)
        {
            return;
        }

        m_reloadVisualActive = true;
        SetAnimatorBool(AnimIDIsAim, true);
        SetAnimatorBool(AnimIDIsShoot, false);
        SetAnimatorFloat(AnimIDReloadSpeed, ResolveReloadAnimationSpeed());
        SetAnimatorBool(AnimIDIsReload, true);
        SetAnimatorTrigger(AnimIDDoReload);
        SetLayerWeight(ActionLayerIndex, 1.0f);
        SetLayerWeight(RecoilLayerIndex, 0.0f);
        m_weapon.StartReload();
    }

    /// <summary>재장전 완료 애니메이션 이벤트에서 호출합니다.</summary>
    public void Reload()
    {
        FinishReloadVisualState(true);
    }

    /// <summary>재장전 애니메이션의 탄창 제거 이벤트를 무기에 전달합니다.</summary>
    public void ReloadWeaponClip()
    {
        if (m_weapon != null)
        {
            m_weapon.OnReloadMagOut();
        }
    }

    /// <summary>플레이어용 재장전 클립의 탄창 삽입 이벤트 수신점입니다.</summary>
    public void ReloadInsertClip()
    {
        // Gun은 재장전 시작 시 자체 오디오를 재생하므로 지정사수는 별도 중복음을 내지 않습니다.
    }

    private void OnWeaponReloadCompleted()
    {
        FinishReloadVisualState(false);
    }

    private void FinishReloadVisualState(bool completeWeaponReload)
    {
        if (!m_reloadVisualActive && !completeWeaponReload)
        {
            return;
        }

        m_reloadVisualActive = false;
        SetAnimatorBool(AnimIDIsReload, false);
        SetAnimatorBool(AnimIDIsShoot, false);
        SetLayerWeight(ActionLayerIndex, 0.0f);
        SetLayerWeight(RecoilLayerIndex, 0.0f);

        if (completeWeaponReload && m_weapon != null && m_weapon.IsReloading)
        {
            m_weapon.CompleteReload();
        }
    }

    private float ResolveReloadAnimationSpeed()
    {
        float reloadTime = m_weapon != null ? m_weapon.ReloadTime : 0.0f;
        float eventTime = ResolveReloadEventTimeAtUnitSpeed();
        return reloadTime > 0.0f && eventTime > 0.0f ? eventTime / reloadTime : 1.0f;
    }

    private float ResolveReloadEventTimeAtUnitSpeed()
    {
        if (m_reloadEventTimeAtUnitSpeed >= 0.0f)
        {
            return m_reloadEventTimeAtUnitSpeed;
        }

        m_reloadEventTimeAtUnitSpeed = 0.0f;
        RuntimeAnimatorController controller = m_animator != null ? m_animator.runtimeAnimatorController : null;
        if (controller == null)
        {
            return 0.0f;
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip == null)
            {
                continue;
            }

            foreach (AnimationEvent animationEvent in clip.events)
            {
                if (animationEvent.functionName == nameof(Reload))
                {
                    m_reloadEventTimeAtUnitSpeed = animationEvent.time;
                    return m_reloadEventTimeAtUnitSpeed;
                }
            }
        }

        Debug.LogWarning(
            $"[DefenseMarksmanController] 재장전 완료 이벤트({nameof(Reload)})를 찾지 못해 애니메이션 배속을 1로 유지합니다.",
            this);
        return 0.0f;
    }

    private bool HasAnimatorParameter(int hash, AnimatorControllerParameterType type)
    {
        return type switch
        {
            AnimatorControllerParameterType.Bool => m_boolParameters.Contains(hash),
            AnimatorControllerParameterType.Float => m_floatParameters.Contains(hash),
            AnimatorControllerParameterType.Trigger => m_triggerParameters.Contains(hash),
            _ => false,
        };
    }

    private void CacheAnimatorCapabilities()
    {
        m_boolParameters.Clear();
        m_floatParameters.Clear();
        m_triggerParameters.Clear();

        if (m_animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = m_animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                    m_boolParameters.Add(parameter.nameHash);
                    break;
                case AnimatorControllerParameterType.Float:
                    m_floatParameters.Add(parameter.nameHash);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    m_triggerParameters.Add(parameter.nameHash);
                    break;
            }
        }
    }

    private void SetAnimatorBool(int hash, bool value)
    {
        if (HasAnimatorParameter(hash, AnimatorControllerParameterType.Bool))
        {
            m_animator.SetBool(hash, value);
        }
    }

    private void SetAnimatorFloat(int hash, float value)
    {
        if (HasAnimatorParameter(hash, AnimatorControllerParameterType.Float))
        {
            m_animator.SetFloat(hash, value);
        }
    }

    private void SetAnimatorTrigger(int hash)
    {
        if (HasAnimatorParameter(hash, AnimatorControllerParameterType.Trigger))
        {
            m_animator.SetTrigger(hash);
        }
    }

    private void SetLayerWeight(int layerIndex, float value)
    {
        if (m_animator != null && m_animator.layerCount > layerIndex)
        {
            m_animator.SetLayerWeight(layerIndex, value);
        }
    }

    private EnemyController SelectNearestVisibleTarget()
    {
        EnemyController[] enemies = FindObjectsByType<EnemyController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        EnemyController best = null;
        float bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController candidate = enemies[i];
            if (!TryResolveVisibleAimPoint(candidate, out Vector3 aimPoint, out float distanceSqr)
                || distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            best = candidate;
            bestDistanceSqr = distanceSqr;
            m_currentAimPoint = aimPoint;
        }

        return best;
    }

    private bool TryResolveCurrentAimPoint(out Vector3 aimPoint)
    {
        return TryResolveVisibleAimPoint(m_currentTarget, out aimPoint, out _);
    }

    private bool TryResolveVisibleAimPoint(
        EnemyController candidate,
        out Vector3 aimPoint,
        out float distanceSqr)
    {
        aimPoint = default;
        distanceSqr = float.PositiveInfinity;

        if (candidate == null || !candidate.isActiveAndEnabled || candidate.CurrentHP <= 0)
        {
            return false;
        }

        aimPoint = candidate.transform.position + Vector3.up * m_targetAimHeight;
        Vector3 eye = GetEyePosition();
        Vector3 delta = aimPoint - eye;
        distanceSqr = delta.sqrMagnitude;

        float maxRange = Mathf.Min(m_sightRange, m_weapon.HitscanRange);
        if (distanceSqr <= 0.0001f || distanceSqr > maxRange * maxRange)
        {
            return false;
        }

        float pitch = GetPitchAngle(delta);
        GetAimPitchLimits(out float maxUpPitch, out float maxDownPitch);
        if (pitch > maxUpPitch || pitch < -maxDownPitch)
        {
            return false;
        }

        Vector3 flatDirection = Vector3.ProjectOnPlane(delta, Vector3.up);
        if (m_sightAngle < 359.9f
            && flatDirection.sqrMagnitude > 0.0001f
            && Vector3.Angle(transform.forward, flatDirection) > m_sightAngle * 0.5f)
        {
            return false;
        }

        float distance = Mathf.Sqrt(distanceSqr);
        Vector3 direction = delta / distance;
        if (!m_weapon.TryTraceAimPoint(
                eye,
                direction,
                distance + 0.5f,
                m_weapon.HitscanLayerMask.value,
                out RaycastHit hit))
        {
            return false;
        }

        return hit.collider != null && hit.collider.GetComponentInParent<EnemyController>() == candidate;
    }

    private Vector3 GetEyePosition()
    {
        if (m_eyePoint != null)
        {
            return m_eyePoint.position;
        }

        if (m_cachedHead != null)
        {
            return m_cachedHead.position;
        }

        return transform.position + Vector3.up * 1.6f;
    }

    private void RotateToward(Vector3 aimPoint)
    {
        Vector3 flatDirection = Vector3.ProjectOnPlane(aimPoint - transform.position, Vector3.up);
        if (flatDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * m_rotationSpeed);
    }

    private float GetAimAngleError()
    {
        Vector3 flatDirection = Vector3.ProjectOnPlane(m_currentAimPoint - transform.position, Vector3.up);
        return flatDirection.sqrMagnitude <= 0.0001f
            ? 0.0f
            : Vector3.Angle(transform.forward, flatDirection);
    }

    private void GetAimPitchLimits(out float maxUpPitch, out float maxDownPitch)
    {
        maxUpPitch = m_pitchReference != null
            ? Mathf.Clamp(m_pitchReference.TopClamp, 1.0f, 89.0f)
            : m_maxAimUpPitch;
        maxDownPitch = m_pitchReference != null
            ? Mathf.Clamp(Mathf.Abs(m_pitchReference.BottomClamp), 1.0f, 89.0f)
            : m_maxAimDownPitch;
    }

    private static float GetPitchAngle(Vector3 direction)
    {
        float horizontalDistance = Vector3.ProjectOnPlane(direction, Vector3.up).magnitude;
        return horizontalDistance <= 0.0001f
            ? (direction.y >= 0.0f ? 90.0f : -90.0f)
            : Mathf.Atan2(direction.y, horizontalDistance) * Mathf.Rad2Deg;
    }

    private void ApplyAimPose()
    {
        if (m_currentTarget == null || (m_cachedChest == null && m_cachedUpperChest == null))
        {
            return;
        }

        Vector3 poseOrigin = m_cachedUpperChest != null
            ? m_cachedUpperChest.position
            : m_cachedChest.position;
        GetAimPitchLimits(out float maxUpPitch, out float maxDownPitch);
        float pitch = Mathf.Clamp(GetPitchAngle(m_currentAimPoint - poseOrigin), -maxDownPitch, maxUpPitch);

        if (m_cachedChest != null)
        {
            m_preAimChestLocalRotation = m_cachedChest.localRotation;
            m_cachedChest.localRotation = m_preAimChestLocalRotation
                * Quaternion.AngleAxis(-pitch * m_chestPitchWeight, Vector3.right);
        }

        if (m_cachedUpperChest != null)
        {
            m_preAimUpperChestLocalRotation = m_cachedUpperChest.localRotation;
            m_cachedUpperChest.localRotation = m_preAimUpperChestLocalRotation
                * Quaternion.AngleAxis(-pitch * (1.0f - m_chestPitchWeight), Vector3.right);
        }

        m_hasAimPose = true;
    }

    private void RestoreAimPose()
    {
        if (!m_hasAimPose)
        {
            return;
        }

        if (m_cachedChest != null)
        {
            m_cachedChest.localRotation = m_preAimChestLocalRotation;
        }

        if (m_cachedUpperChest != null)
        {
            m_cachedUpperChest.localRotation = m_preAimUpperChestLocalRotation;
        }

        m_hasAimPose = false;
    }

    private void UpdateBurstPhase()
    {
        if (Time.time < m_burstPhaseEndTime)
        {
            return;
        }

        if (m_isBursting)
        {
            m_isBursting = false;
            m_burstPhaseEndTime = Time.time + m_burstRestDuration;
            return;
        }

        m_isBursting = true;
        m_burstPhaseEndTime = Time.time + m_burstDuration;

        Vector2 error = Random.insideUnitCircle * m_aimErrorRadius;
        m_aimErrorOffset = transform.right * error.x + Vector3.up * error.y;
    }

    private void StopBurst()
    {
        m_isBursting = false;
        m_burstPhaseEndTime = 0.0f;
        m_aimErrorOffset = Vector3.zero;
    }

    private void TryFire(Vector3 aimPoint)
    {
        Transform firePos = m_weapon.FirePos;
        Vector3 origin = firePos != null ? firePos.position : GetEyePosition();
        Vector3 delta = aimPoint - origin;
        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 direction = delta.normalized;
        var shot = new Gun.HitscanShotInfo
        {
            IsValid = true,
            Origin = origin,
            Direction = direction,
            AimPoint = aimPoint,
            EndPoint = origin + direction * m_weapon.HitscanRange,
            FrameCount = Time.frameCount,
        };

        m_weapon.TryLayShoot(shot, false, out _);
    }

    private void OnDisable()
    {
        RestoreAimPose();
        m_currentTarget = null;
        StopBurst();
        m_fireRequested = false;
        m_reloadVisualActive = false;
        InitializeStationaryAnimatorState();
    }

    private void OnDestroy()
    {
        if (m_weapon != null)
        {
            m_weapon.OnReloadCompleted -= OnWeaponReloadCompleted;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!m_drawDebugGizmos)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, m_sightRange);

        if (m_currentTarget != null)
        {
            Gizmos.color = IsAimAligned ? Color.green : Color.cyan;
            Gizmos.DrawLine(GetEyePosition(), m_currentAimPoint);
        }
    }
}
