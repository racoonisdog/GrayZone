using StarterAssets;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 플레이어의 조준 카메라, 조준 UI, 조준 방향 회전, IK 리그, 사격 및 재장전 입력을 제어하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 <see cref="PlayerInputs"/>, <see cref="ThirdPersonController"/>,
/// <see cref="Animator"/>, <see cref="AudioSource"/>를 같은 GameObject의 필수 참조로 사용합니다.
/// 필수 참조는 <c>Awake</c>에서 캐싱하고, 누락 시 컴포넌트를 비활성화하여 런타임 null 참조를 방지합니다.
/// </remarks>
[RequireComponent(typeof(PlayerInputs))]
[RequireComponent(typeof(ThirdPersonController))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(AudioSource))]
public class PlayerManager : MonoBehaviour
{
    private const int WeaponLayerIndex = 1;
    private const float AimRotationLerpSpeed = 50.0f;

    private static readonly int AnimIDShoot = Animator.StringToHash("Shoot");
    private static readonly int AnimIDReload = Animator.StringToHash("Reload");

    [Foldout("Aim Options")]
    [Tooltip("조준 중 활성화할 Cinemachine 카메라입니다.")]
    [FormerlySerializedAs("aimCam")]
    [SerializeField] private CinemachineCamera m_aimCamera;

    [Tooltip("조준 중 표시할 UI 오브젝트입니다.")]
    [FormerlySerializedAs("aimImage")]
    [SerializeField] private GameObject m_aimImage;

    [Tooltip("조준 지점을 표시하거나 IK 타겟으로 사용할 오브젝트입니다.")]
    [FormerlySerializedAs("aimObj")]
    [SerializeField] private GameObject m_aimTarget;

    [Tooltip("Raycast가 아무 대상도 맞추지 않았을 때 카메라 전방에 둘 기본 조준 거리입니다.")]
    [FormerlySerializedAs("aimObjDis")]
    [SerializeField] private float m_aimTargetDistance = 10.0f;

    [Tooltip("조준 Raycast가 충돌할 대상 레이어입니다.")]
    [FormerlySerializedAs("targetLayer")]
    [SerializeField] private LayerMask m_targetLayer;

    [Foldout("IK Options")]
    [Tooltip("손 위치 보정에 사용할 Rig입니다.")]
    [FormerlySerializedAs("handRig")]
    [SerializeField] private Rig m_handRig;

    [Tooltip("조준 자세 보정에 사용할 Rig입니다.")]
    [FormerlySerializedAs("aimRig")]
    [SerializeField] private Rig m_aimRig;

    [Foldout("Audio Options")]
    [Tooltip("사격 사운드입니다. 실제 사격 사운드를 WeaponController가 처리한다면 비워둘 수 있습니다.")]
    [FormerlySerializedAs("shootingSound")]
    [SerializeField] private AudioClip m_shootingSound;

    [Tooltip("재장전 애니메이션 이벤트에서 사용할 사운드 배열입니다. 0: 탄창 제거, 1: 탄창 삽입, 2: 재장전 완료.")]
    [FormerlySerializedAs("reloadSound")]
    [SerializeField] private AudioClip[] m_reloadSounds;

    private PlayerInputs m_input;
    private ThirdPersonController m_controller;
    private Animator m_animator;
    private AudioSource m_weaponAudioSource;
    private WeaponController m_weaponController;
    private Camera m_mainCamera;
    private Enemy m_currentAimEnemy;
    private bool m_hasRequiredReferences;

    /// <summary>조준 카메라 참조입니다.</summary>
    public CinemachineCamera AimCamera => m_aimCamera;

    /// <summary>조준 UI 오브젝트 참조입니다.</summary>
    public GameObject AimImage => m_aimImage;

    /// <summary>조준 타겟 오브젝트 참조입니다.</summary>
    public GameObject AimTarget => m_aimTarget;

    /// <summary>Raycast 미충돌 시 사용할 기본 조준 거리입니다.</summary>
    public float AimTargetDistance => m_aimTargetDistance;

    /// <summary>조준 Raycast 대상 레이어입니다.</summary>
    public LayerMask TargetLayer => m_targetLayer;

    /// <summary>현재 조준 Raycast가 감지한 적입니다.</summary>
    public Enemy CurrentAimEnemy => m_currentAimEnemy;

    /// <summary>사격 사운드 클립입니다.</summary>
    public AudioClip ShootingSound => m_shootingSound;

    /// <summary>재장전 사운드 클립 배열입니다.</summary>
    public AudioClip[] ReloadSounds => m_reloadSounds;

    /// <summary>
    /// 조준 카메라 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 카메라입니다.</param>
    public void SetAimCamera(CinemachineCamera value) => m_aimCamera = value;

    /// <summary>
    /// 조준 UI 오브젝트 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 UI 오브젝트입니다.</param>
    public void SetAimImage(GameObject value) => m_aimImage = value;

    /// <summary>
    /// 조준 타겟 오브젝트 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 타겟 오브젝트입니다.</param>
    public void SetAimTarget(GameObject value) => m_aimTarget = value;

    /// <summary>
    /// Raycast 미충돌 시 사용할 기본 조준 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 거리입니다.</param>
    public void SetAimTargetDistance(float value) => m_aimTargetDistance = Mathf.Max(0.0f, value);

    /// <summary>
    /// 조준 Raycast 대상 레이어를 설정합니다.
    /// </summary>
    /// <param name="value">새 대상 레이어 마스크입니다.</param>
    public void SetTargetLayer(LayerMask value) => m_targetLayer = value;

    /// <summary>
    /// 사격 사운드 클립을 설정합니다.
    /// </summary>
    /// <param name="value">새 사격 사운드 클립입니다.</param>
    public void SetShootingSound(AudioClip value) => m_shootingSound = value;

    /// <summary>
    /// 재장전 사운드 배열을 설정합니다.
    /// </summary>
    /// <param name="value">새 재장전 사운드 배열입니다.</param>
    public void SetReloadSounds(AudioClip[] value) => m_reloadSounds = value;

    /// <summary>
    /// Unity 생명주기 초기화 함수입니다.
    /// 필수 참조를 캐싱하고 누락 여부를 검증합니다.
    /// </summary>
    private void Awake()
    {
        CacheRequiredReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        m_hasRequiredReferences = true;
        SetAimState(false);
        SetRigWeight(0.0f);
    }

    /// <summary>
    /// 매 프레임 조준, 사격, 재장전 입력을 처리합니다.
    /// </summary>
    private void Update()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        UpdateAimAndWeapon();
    }

    /// <summary>
    /// 같은 GameObject 또는 자식 오브젝트에서 필요한 참조를 캐싱합니다.
    /// </summary>
    private void CacheRequiredReferences()
    {
        m_input = GetComponent<PlayerInputs>();
        m_controller = GetComponent<ThirdPersonController>();
        m_animator = GetComponent<Animator>();
        m_weaponAudioSource = GetComponent<AudioSource>();
        m_weaponController = GetComponentInChildren<WeaponController>();
        m_mainCamera = Camera.main;
    }

    /// <summary>
    /// 필수 참조가 정상적으로 준비되었는지 검증합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 true입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_input == null)
        {
            Debug.LogError("[AimController] StarterAssetsInputs 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_controller == null)
        {
            Debug.LogError("[AimController] ThirdPersonController 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_animator == null)
        {
            Debug.LogError("[AimController] Animator 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_weaponAudioSource == null)
        {
            Debug.LogError("[AimController] AudioSource 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_mainCamera == null)
        {
            Debug.LogError("[AimController] MainCamera 태그를 가진 카메라를 찾지 못했습니다.", this);
            isValid = false;
        }

        if (m_aimCamera == null)
        {
            Debug.LogError("[AimController] Aim Camera가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_aimImage == null)
        {
            Debug.LogError("[AimController] Aim Image가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_aimTarget == null)
        {
            Debug.LogError("[AimController] Aim Target이 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_handRig == null)
        {
            Debug.LogError("[AimController] Hand Rig가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_aimRig == null)
        {
            Debug.LogError("[AimController] Aim Rig가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_weaponController == null)
        {
            Debug.LogWarning("[AimController] WeaponController를 자식 오브젝트에서 찾지 못했습니다. 사격과 재장전 무기 처리는 생략됩니다.", this);
        }

        return isValid;
    }

    /// <summary>
    /// 재장전 입력과 조준 입력을 순서대로 처리합니다.
    /// </summary>
    private void UpdateAimAndWeapon()
    {
        if (HandleReloadInput())
        {
            return;
        }

        if (m_controller.IsReload)
        {
            return;
        }

        if (m_input.Aim)
        {
            UpdateAiming();
            return;
        }

        StopAiming();
    }

    /// <summary>
    /// 재장전 입력이 들어온 경우 재장전 상태와 애니메이션을 시작합니다.
    /// </summary>
    /// <returns>재장전 입력을 처리했으면 true입니다.</returns>
    private bool HandleReloadInput()
    {
        if (!m_input.Reload)
        {
            return false;
        }

        m_input.ReloadInput(false);

        if (m_controller.IsReload)
        {
            return true;
        }

        SetAimState(false);
        SetRigWeight(0.0f);
        m_animator.SetLayerWeight(WeaponLayerIndex, 1.0f);
        m_animator.SetTrigger(AnimIDReload);
        m_controller.SetReload(true);

        if (m_weaponController != null)
        {
            m_weaponController.StartReload();
        }

        return true;
    }

    /// <summary>
    /// 조준 중 카메라 전방 Raycast, 캐릭터 회전, IK, 사격 입력을 처리합니다.
    /// </summary>
    private void UpdateAiming()
    {
        SetAimState(true);
        m_animator.SetLayerWeight(WeaponLayerIndex, 1.0f);

        Vector3 targetPosition = GetAimTargetPosition();
        RotateToAimTarget(targetPosition);
        SetRigWeight(1.0f);
        UpdateShootState(targetPosition);
    }

    /// <summary>
    /// 조준 상태가 아닐 때 카메라, UI, IK, 사격 애니메이션 상태를 해제합니다.
    /// </summary>
    private void StopAiming()
    {
        SetAimState(false);
        SetRigWeight(0.0f);
        m_animator.SetLayerWeight(WeaponLayerIndex, 0.0f);
        m_animator.SetBool(AnimIDShoot, false);
    }

    /// <summary>
    /// 카메라 전방으로 Raycast를 수행하여 현재 조준 위치를 계산합니다.
    /// </summary>
    /// <returns>현재 조준 목표 월드 좌표입니다.</returns>
    private Vector3 GetAimTargetPosition()
    {
        Transform cameraTransform = m_mainCamera.transform;

        if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, Mathf.Infinity, m_targetLayer))
        {
            m_aimTarget.transform.position = hit.point;
            m_currentAimEnemy = hit.collider.GetComponentInParent<Enemy>();
            return hit.point;
        }

        Vector3 fallbackPosition = cameraTransform.position + cameraTransform.forward * m_aimTargetDistance;
        m_aimTarget.transform.position = fallbackPosition;
        m_currentAimEnemy = null;
        return fallbackPosition;
    }

    /// <summary>
    /// 플레이어가 조준 목표의 수평 방향을 바라보도록 회전시킵니다.
    /// </summary>
    /// <param name="targetPosition">바라볼 조준 목표 위치입니다.</param>
    private void RotateToAimTarget(Vector3 targetPosition)
    {
        Vector3 targetAim = targetPosition;
        targetAim.y = transform.position.y;

        Vector3 aimDirection = targetAim - transform.position;

        if (aimDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.forward = Vector3.Lerp(
            transform.forward,
            aimDirection.normalized,
            Time.deltaTime * AimRotationLerpSpeed);
    }

    /// <summary>
    /// 사격 입력 상태를 애니메이터와 무기 컨트롤러에 반영합니다.
    /// </summary>
    /// <param name="targetPosition">사격 대상 위치입니다.</param>
    private void UpdateShootState(Vector3 targetPosition)
    {
        if (m_input.Shoot)
        {
            m_animator.SetBool(AnimIDShoot, true);

            if (m_weaponController != null)
            {
                m_weaponController.TryShoot(targetPosition);
            }

            return;
        }

        m_animator.SetBool(AnimIDShoot, false);
    }

    /// <summary>
    /// 조준 카메라, 조준 UI, 이동 컨트롤러의 조준 이동 상태를 설정합니다.
    /// </summary>
    /// <param name="isAiming">조준 상태이면 true입니다.</param>
    private void SetAimState(bool isAiming)
    {
        if (m_aimCamera != null)
        {
            m_aimCamera.gameObject.SetActive(isAiming);
        }

        if (m_aimImage != null)
        {
            m_aimImage.SetActive(isAiming);
        }

        if (m_controller != null)
        {
            m_controller.SetAimMove(isAiming);
        }
    }

    /// <summary>
    /// 재장전 완료 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void Reload()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        m_controller.SetReload(false);
        SetRigWeight(1.0f);
        m_animator.SetLayerWeight(WeaponLayerIndex, 0.0f);

        if (m_weaponController != null)
        {
            m_weaponController.CompleteReload();
        }

        PlayWeaponSound(GetReloadSound(2));
    }

    /// <summary>
    /// 탄창 제거 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void ReloadWeaponClip()
    {
        if (m_weaponController != null)
        {
            m_weaponController.OnReloadMagOut();
        }

        PlayWeaponSound(GetReloadSound(0));
    }

    /// <summary>
    /// 탄창 삽입 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void ReloadInsertClip()
    {
        PlayWeaponSound(GetReloadSound(1));
    }

    /// <summary>
    /// 외부 상태 전환에 의해 조준을 강제로 해제합니다.
    /// </summary>
    public void ForceStopAim()
    {
        SetAimState(false);
        SetRigWeight(0.0f);

        if (m_animator != null)
        {
            m_animator.SetLayerWeight(WeaponLayerIndex, 0.0f);
            m_animator.SetBool(AnimIDShoot, false);
        }
    }

    /// <summary>
    /// 조준 및 손 IK 리그의 weight를 설정합니다.
    /// </summary>
    /// <param name="weight">적용할 리그 weight입니다. 0이면 비활성, 1이면 활성입니다.</param>
    private void SetRigWeight(float weight)
    {
        if (m_aimRig != null)
        {
            m_aimRig.weight = weight;
        }

        if (m_handRig != null)
        {
            m_handRig.weight = weight;
        }
    }

    /// <summary>
    /// 재장전 사운드 배열에서 지정한 인덱스의 클립을 가져옵니다.
    /// </summary>
    /// <param name="index">가져올 재장전 사운드 인덱스입니다.</param>
    /// <returns>유효한 인덱스이면 해당 AudioClip, 아니면 null입니다.</returns>
    private AudioClip GetReloadSound(int index)
    {
        if (m_reloadSounds == null || index < 0 || index >= m_reloadSounds.Length)
        {
            Debug.LogWarning($"[AimController] ReloadSounds[{index}]가 없습니다.", this);
            return null;
        }

        return m_reloadSounds[index];
    }

    /// <summary>
    /// 무기 사운드를 AudioSource로 재생합니다.
    /// </summary>
    /// <param name="sound">재생할 사운드 클립입니다.</param>
    private void PlayWeaponSound(AudioClip sound)
    {
        if (m_weaponAudioSource == null || sound == null)
        {
            return;
        }

        m_weaponAudioSource.clip = sound;
        m_weaponAudioSource.Play();
    }
}
