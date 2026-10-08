using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// 플레이어의 투척 모드와 좌클릭 입력을 받아 폭발탄 경로를 표시하고 투척합니다.
/// </summary>
public class ExplosiveProjectileShooter : MonoBehaviour
{
    private const int MaxTrajectoryPointCount = 65;
    private const int ExplosionPreviewSegmentCount = 48;
    private const float ExplosionPreviewHeightOffset = 0.03f;
    private const string GrenadeActionLayerName = "Grenade Action Layer";
    private const string GrenadeEquipSoundEventPath = "event:/World/Throwable/Grenade/Equip";
    private const string MolotovEquipSoundEventPath = "event:/World/Throwable/Molotov/Equip";
    private static readonly int AnimIDGrenadeMode = Animator.StringToHash("IsGrenadeMode");
    private static readonly int AnimIDThrow = Animator.StringToHash("DoThrow");
    private static readonly int AnimStateGrenadeThrow = Animator.StringToHash("Grenade Throw");
    private static readonly int AnimStateGrenadeCrouchThrow = Animator.StringToHash("Grenade Crouch Throw");

    // CrosshairController는 분대 전체가 하나를 공유합니다. 대원별 인스턴스에 복원 스냅샷을 두면
    // 조작 대원 전환/사망/비활성화 순서에 따라 이전 대원이 새 대원의 HUD를 덮을 수 있습니다.
    // 따라서 투척 프리셋은 공용 HUD 기준으로 한 명만 소유하고, 그 소유자만 원래 값을 복원합니다.
    private static ExplosiveProjectileShooter s_crosshairOverrideOwner;
    private static CrosshairController s_crosshairOverrideTarget;
    private static CrosshairPreset s_crosshairOverrideBaseline;
    private static bool s_loggedMissingGrenadeEquipSound;
    private static bool s_loggedMissingMolotovEquipSound;

    [System.Serializable]
    private sealed class CrosshairPreset
    {
        [Tooltip("무기 탄퍼짐에 따라 조준선 간격을 변경할지 여부입니다.")]
        [SerializeField] private bool m_useSpreadAccuracy;

        [Tooltip("중앙 표시 형태입니다.")]
        [SerializeField] private CrosshairController.MainShape m_mainShape = CrosshairController.MainShape.Ring;

        [Tooltip("중앙점 크기입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainSizePixels = 3.0f;

        [Tooltip("중앙 링 지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainRingSizePixels = 14.0f;

        [Tooltip("중앙 링 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainRingThicknessPixels = 2.0f;

        [Tooltip("중앙 표시 색상입니다.")]
        [SerializeField] private Color m_mainColor = new Color(1.0f, 1.0f, 1.0f, 0.27450982f);

        [Tooltip("중앙 표시 외곽선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainStrokeThicknessPixels = 1.0f;

        [Tooltip("중앙 표시 외곽선 색상입니다.")]
        [SerializeField] private Color m_mainStrokeColor = new Color(0.0f, 0.0f, 0.0f, 0.27450982f);

        [Tooltip("보조 표시 형태입니다.")]
        [SerializeField] private CrosshairController.SubShape m_subShape = CrosshairController.SubShape.RoundedCross;

        [Tooltip("중앙과 보조 표시 사이의 간격입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_centerSpacePixels = 12.0f;

        [Tooltip("보조 점 크기입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subSizePixels = 2.0f;

        [Tooltip("보조 십자선 길이입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subWidthPixels = 7.0f;

        [Tooltip("보조 십자선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subThicknessPixels = 2.0f;

        [Tooltip("보조 링 지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subRingSizePixels = 20.0f;

        [Tooltip("보조 링 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subRingThicknessPixels = 2.0f;

        [Tooltip("보조 표시 색상입니다.")]
        [SerializeField] private Color m_subColor = new Color(1.0f, 1.0f, 1.0f, 0.27450982f);

        [Tooltip("보조 표시 외곽선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subStrokeThicknessPixels = 1.0f;

        [Tooltip("보조 표시 외곽선 색상입니다.")]
        [SerializeField] private Color m_subStrokeColor = new Color(0.0f, 0.0f, 0.0f, 0.27450982f);

        [Tooltip("Rounded Cross 모서리 반지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_cornerRadiusPixels = 2.0f;

        public static CrosshairPreset Capture(CrosshairController crosshair)
        {
            return new CrosshairPreset
            {
                m_useSpreadAccuracy = crosshair.SpreadAccuracyEnabled,
                m_mainShape = crosshair.CurrentMainShape,
                m_mainSizePixels = crosshair.MainSizePixels,
                m_mainRingSizePixels = crosshair.MainRingSizePixels,
                m_mainRingThicknessPixels = crosshair.MainRingThicknessPixels,
                m_mainColor = crosshair.MainColor,
                m_mainStrokeThicknessPixels = crosshair.MainStrokeThicknessPixels,
                m_mainStrokeColor = crosshair.MainStrokeColor,
                m_subShape = crosshair.CurrentSubShape,
                m_centerSpacePixels = crosshair.CenterSpacePixels,
                m_subSizePixels = crosshair.SubSizePixels,
                m_subWidthPixels = crosshair.SubWidthPixels,
                m_subThicknessPixels = crosshair.SubThicknessPixels,
                m_subRingSizePixels = crosshair.SubRingSizePixels,
                m_subRingThicknessPixels = crosshair.SubRingThicknessPixels,
                m_subColor = crosshair.SubColor,
                m_subStrokeThicknessPixels = crosshair.SubStrokeThicknessPixels,
                m_subStrokeColor = crosshair.SubStrokeColor,
                m_cornerRadiusPixels = crosshair.CornerRadiusPixels,
            };
        }

        public void Apply(CrosshairController crosshair)
        {
            crosshair.SetSpreadAccuracyEnabled(m_useSpreadAccuracy);
            crosshair.CurrentMainShape = m_mainShape;
            crosshair.MainSizePixels = m_mainSizePixels;
            crosshair.MainRingSizePixels = m_mainRingSizePixels;
            crosshair.MainRingThicknessPixels = m_mainRingThicknessPixels;
            crosshair.MainColor = m_mainColor;
            crosshair.MainStrokeThicknessPixels = m_mainStrokeThicknessPixels;
            crosshair.MainStrokeColor = m_mainStrokeColor;
            crosshair.CurrentSubShape = m_subShape;
            crosshair.CenterSpacePixels = m_centerSpacePixels;
            crosshair.SubSizePixels = m_subSizePixels;
            crosshair.SubWidthPixels = m_subWidthPixels;
            crosshair.SubThicknessPixels = m_subThicknessPixels;
            crosshair.SubRingSizePixels = m_subRingSizePixels;
            crosshair.SubRingThicknessPixels = m_subRingThicknessPixels;
            crosshair.SubColor = m_subColor;
            crosshair.SubStrokeThicknessPixels = m_subStrokeThicknessPixels;
            crosshair.SubStrokeColor = m_subStrokeColor;
            crosshair.CornerRadiusPixels = m_cornerRadiusPixels;
        }

        public CrosshairStyle ToStyle()
        {
            return new CrosshairStyle
            {
                useSpreadAccuracy = m_useSpreadAccuracy,
                mainShape = m_mainShape,
                mainSizePixels = m_mainSizePixels,
                mainRingSizePixels = m_mainRingSizePixels,
                mainRingThicknessPixels = m_mainRingThicknessPixels,
                mainColor = m_mainColor,
                mainStrokeThicknessPixels = m_mainStrokeThicknessPixels,
                mainStrokeColor = m_mainStrokeColor,
                subShape = m_subShape,
                centerSpacePixels = m_centerSpacePixels,
                subSizePixels = m_subSizePixels,
                subWidthPixels = m_subWidthPixels,
                subThicknessPixels = m_subThicknessPixels,
                subRingSizePixels = m_subRingSizePixels,
                subRingThicknessPixels = m_subRingThicknessPixels,
                subColor = m_subColor,
                subStrokeThicknessPixels = m_subStrokeThicknessPixels,
                subStrokeColor = m_subStrokeColor,
                cornerRadiusPixels = m_cornerRadiusPixels,
            };
        }

        public static CrosshairPreset FromStyle(CrosshairStyle style, bool useSpreadAccuracy)
        {
            return new CrosshairPreset
            {
                m_useSpreadAccuracy = useSpreadAccuracy,
                m_mainShape = style.mainShape,
                m_mainSizePixels = style.mainSizePixels,
                m_mainRingSizePixels = style.mainRingSizePixels,
                m_mainRingThicknessPixels = style.mainRingThicknessPixels,
                m_mainColor = style.mainColor,
                m_mainStrokeThicknessPixels = style.mainStrokeThicknessPixels,
                m_mainStrokeColor = style.mainStrokeColor,
                m_subShape = style.subShape,
                m_centerSpacePixels = style.centerSpacePixels,
                m_subSizePixels = style.subSizePixels,
                m_subWidthPixels = style.subWidthPixels,
                m_subThicknessPixels = style.subThicknessPixels,
                m_subRingSizePixels = style.subRingSizePixels,
                m_subRingThicknessPixels = style.subRingThicknessPixels,
                m_subColor = style.subColor,
                m_subStrokeThicknessPixels = style.subStrokeThicknessPixels,
                m_subStrokeColor = style.subStrokeColor,
                m_cornerRadiusPixels = style.cornerRadiusPixels,
            };
        }
    }

    [Tooltip("마우스 휠로 순환 선택할 ProjectileBase Prefab 목록입니다.")]
    [SerializeField] private List<ProjectileBase> m_projectilePrefabs = new List<ProjectileBase>();

    [UnityEngine.Serialization.FormerlySerializedAs("m_projectilePrefab")]
    [SerializeField, HideInInspector] private ProjectileBase m_legacyProjectilePrefab;

    [Tooltip("현재 선택된 투척물 목록 인덱스입니다.")]
    [Min(0)]
    [SerializeField] private int m_selectedProjectileIndex;

    [Tooltip("G 투척 모드에서 현재 선택된 투척물 아이콘을 표시할 UI입니다. 비어 있으면 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private GrenadeSelectionUI m_grenadeSelectionUI;

    [Tooltip("투척물 수량을 조회하고 실제 투척 시 1개를 소모할 스쿼드 공용 인벤토리입니다. 비어 있으면 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private SquadInventoryManager m_inventoryManager;

    [Tooltip("G 투척 모드에서 수치 프리셋을 적용할 크로스헤어입니다. 비어 있으면 AimController 또는 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private CrosshairController m_defaultCrosshair;

    [Tooltip("G 투척 모드에서 기존 크로스헤어에 임시로 적용할 수치 프리셋입니다.")]
    [SerializeField] private CrosshairPreset m_throwCrosshairPreset = new CrosshairPreset();

    [Header("Held Grenade Visual")]
    [Tooltip("G 투척 모드 동안 오른손에 표시할 수류탄 Visual Prefab입니다.")]
    [SerializeField] private GameObject m_heldGrenadeVisualPrefab;

    [Tooltip("수류탄 Visual을 배치할 손 장착 소켓입니다. 비어 있으면 Humanoid 오른손 본을 사용합니다.")]
    [SerializeField] private Transform m_heldGrenadeSocket;

    [Tooltip("오른손 본을 기준으로 한 수류탄 위치입니다.")]
    [SerializeField] private Vector3 m_heldGrenadeLocalPosition = Vector3.zero;

    [Tooltip("오른손 본을 기준으로 한 수류탄 회전입니다.")]
    [SerializeField] private Vector3 m_heldGrenadeLocalEulerAngles = Vector3.zero;

    [Tooltip("손에 표시할 수류탄 Visual의 크기입니다.")]
    [SerializeField] private Vector3 m_heldGrenadeLocalScale = Vector3.one * 0.25f;

    [Tooltip("Grenade Hand IK 소켓이 없을 때만 사용할 Collider 중심 기준 fallback 투척 시작 위치입니다.")]
    [SerializeField] private Vector3 m_throwOriginOffset = new Vector3(0.0f, 0.2f, 1.0f);

    [Tooltip("손 소켓 또는 fallback 투척 원점에 추가할 로컬 X/Y/Z 오프셋입니다. X는 좌우, Y는 높이, Z는 앞뒤이며 궤적 시작점과 실제 투척물 생성 위치에 함께 적용됩니다.")]
    [SerializeField] private Vector3 m_throwStartOffset = Vector3.zero;

    [Tooltip("수평 조준 시 폭탄이 같은 높이로 돌아올 때의 기준 투척 거리입니다. 실제 비행 종료점은 아닙니다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("m_maxThrowDistance")]
    [Min(0.1f)]
    [SerializeField] private float m_referenceThrowDistance = 20.0f;

    [Tooltip("수평 조준 시 투척 시작점보다 올라갈 기준 최고 높이입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_arcHeight = 1.5f;

    [Tooltip("투척 시작부터 최고점까지 포물선을 아래로 휘게 하는 가속도입니다. 낮을수록 상승 구간이 완만해집니다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("m_downwardAcceleration")]
    [Min(0.01f)]
    [SerializeField] private float m_ascentDownwardAcceleration = 8.0f;

    [Tooltip("최고점 이후 포물선을 아래로 휘게 하는 가속도입니다. 높을수록 빠르게 떨어집니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_descentDownwardAcceleration = 24.0f;

    [Tooltip("포물선의 거리와 높이는 유지하면서 실제 비행 속도만 조절합니다. 1은 기본 속도, 2는 두 배 속도입니다.")]
    [InspectorName("Throw Speed")]
    [Min(0.01f)]
    [SerializeField] private float m_throwSpeedMultiplier = 1.25f;

    [Tooltip("켜면 조준 중에 포물선 궤적 선을 함께 그립니다. 끄면 착탄 지점의 원형 표시만 보입니다.")]
    [SerializeField] private bool m_showTrajectoryLine = false;

    [Tooltip("LineRenderer로 미리 보여 줄 포물선의 최대 누적 길이입니다. 실제 폭탄 이동은 제한하지 않습니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_trajectoryPreviewDistance = 20.0f;

    [Tooltip("한 번 투척한 뒤 다음 투척 경로를 표시하고 다시 던질 수 있을 때까지의 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_throwCooldown = 1.0f;

    [Tooltip("경로 표시를 구성할 선분 수입니다.")]
    [Range(4, 64)]
    [SerializeField] private int m_trajectorySegments = 24;

    [Tooltip("이동 중 충돌을 검사할 구체 반지름입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_collisionRadius = 0.5f;

    [Tooltip("투척 경로 및 실제 이동 중 충돌을 검사할 Layer입니다.")]
    [SerializeField] private LayerMask m_collisionLayers = ~0;

    [Tooltip("경로 표시 선의 두께입니다.")]
    [Min(0.001f)]
    [SerializeField] private float m_trajectoryWidth = 0.04f;

    [Tooltip("경로 표시 선의 색상입니다.")]
    [SerializeField] private Color m_trajectoryColor = new Color(1.0f, 0.75f, 0.1f, 0.9f);

    [Header("Impact Range Preview")]
    [Tooltip("착탄 범위 원 내부 채움에 사용할 색상입니다.")]
    [SerializeField] private Color m_impactPreviewColor = new Color(1.0f, 0.75f, 0.1f, 0.9f);

    [Tooltip("착탄 범위 원 내부 채움의 투명도입니다. 0은 완전 투명, 1은 완전 불투명입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_impactPreviewFillAlpha = 0.2f;

    private readonly RaycastHit[] m_previewHits = new RaycastHit[16];
    private readonly Vector3[] m_trajectoryPoints = new Vector3[MaxTrajectoryPointCount];

    private PlayerInputController m_input;
    private AimController m_aimController;
    private Animator m_animator;
    private Collider m_sourceCollider;
    private LineRenderer m_trajectoryLine;
    private MeshRenderer m_explosionPreviewFillRenderer;
    private Mesh m_explosionPreviewFillMesh;
    private Material m_runtimeLineMaterial;
    private Material m_runtimeExplosionFillMaterial;
    private bool m_wasThrowModeActive;
    private bool m_throwWasHeld;
    private Vector3 m_throwStart;
    private Vector3 m_initialVelocity;
    private bool m_hasPlannedCollision;
    private float m_plannedCollisionTime;
    private Vector3 m_plannedCollisionPosition;
    private Vector3 m_plannedContactPoint;
    private Vector3 m_plannedSurfaceNormal;
    private bool m_hasExplosionPreview;
    private Vector3 m_explosionPreviewCenter;
    private int m_trajectoryPointCount;
    private float m_nextThrowReadyTime;
    private bool m_crosshairModeInitialized;
    private bool m_throwCrosshairActive;
    private bool m_hasGrenadeModeParameter;
    private bool m_hasThrowParameter;
    private int m_grenadeActionLayerIndex = -1;
    private GameObject m_heldGrenadeVisualInstance;
    private Renderer[] m_weaponRenderers;
    private bool[] m_weaponRendererEnabledStates;
    private bool m_grenadeEquipmentVisible;

    /// <summary>마우스 휠로 순환 선택하는 투척물 Prefab 목록입니다.</summary>
    public IReadOnlyList<ProjectileBase> ProjectilePrefabs => m_projectilePrefabs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCrosshairOverrideState()
    {
        // Domain Reload를 끈 Play Mode에서도 이전 세션의 Unity 오브젝트 참조를 들고 있지 않습니다.
        s_crosshairOverrideOwner = null;
        s_crosshairOverrideTarget = null;
        s_crosshairOverrideBaseline = null;
    }

    private void Awake()
    {
        MigrateLegacyProjectile();
        m_input = GetComponent<PlayerInputController>();
        m_aimController = GetComponent<AimController>();
        m_animator = GetComponent<Animator>();
        CacheGrenadeAnimationParameters();
        InitializeGrenadeEquipmentVisuals();
        m_sourceCollider = GetComponent<Collider>();
        ResolveInventoryManager();
        ResolveGrenadeSelectionUI();
        CreateTrajectoryLine();
        ApplyCrosshairMode(false);
    }

    private void LateUpdate()
    {
        // 공용 HUD는 현재 직접 조작 중인 캐릭터만 변경합니다. AI가 된 이전 캐릭터가
        // 자기 입력 상태로 수류탄 프리셋을 다시 쓰면 일반 크로스헤어와 값이 섞입니다.
        bool ownsPlayerCrosshair = m_aimController != null && m_aimController.IsPlayerControlled;
        bool throwModeActive = ownsPlayerCrosshair && m_input != null && m_input.ThrowMode;
        SetGrenadeAnimationMode(throwModeActive);
        SetGrenadeEquipmentVisible(throwModeActive);
        SetHeldGrenadeVisible(throwModeActive && !IsGrenadeThrowAnimationActive());
        ApplyCrosshairMode(throwModeActive);

        bool ownsSelectionUI = m_aimController != null
            ? m_aimController.IsPlayerControlled
            : m_input != null && m_input.isActiveAndEnabled;
        if (!ownsSelectionUI)
        {
            UpdateGrenadeSelectionUI(false);
            HideTrajectory();
            m_wasThrowModeActive = false;
            m_throwWasHeld = false;
            return;
        }

        UpdateGrenadeSelectionUI(throwModeActive);

        if (m_input == null || m_aimController == null || !throwModeActive)
        {
            HideTrajectory();
            m_wasThrowModeActive = false;
            m_throwWasHeld = false;
            return;
        }

        bool enteredThrowModeThisFrame = !m_wasThrowModeActive;
        CycleProjectile(m_input.ConsumeThrowSelectionDelta());
        UpdateGrenadeSelectionUI(true);
        if (enteredThrowModeThisFrame)
        {
            PlaySelectedProjectileEquipSound();
        }

        bool throwHeld = m_input.Throw;

        if (m_input.Sprint)
        {
            HideTrajectory();
            m_wasThrowModeActive = true;
            m_throwWasHeld = throwHeld;
            return;
        }

        bool canThrow = Time.time >= m_nextThrowReadyTime;

        if (canThrow)
        {
            ResolveTrajectory();
            BuildTrajectoryPlan();
            DrawTrajectory();

            if (!enteredThrowModeThisFrame && throwHeld && !m_throwWasHeld && ThrowProjectile())
            {
                PlayThrowAnimation();
                m_nextThrowReadyTime = Time.time + m_throwCooldown;
                HideTrajectory();
            }
        }
        else
        {
            HideTrajectory();
        }

        m_wasThrowModeActive = true;
        m_throwWasHeld = throwHeld;
    }

    private void OnDisable()
    {
        SetGrenadeAnimationMode(false);
        SetGrenadeEquipmentVisible(false);
        if (m_hasThrowParameter)
        {
            m_animator.ResetTrigger(AnimIDThrow);
        }

        UpdateGrenadeSelectionUI(false);
        ApplyCrosshairMode(false);
        m_crosshairModeInitialized = false;
        HideTrajectory();
        m_wasThrowModeActive = false;
        m_throwWasHeld = false;
    }

    private void OnDestroy()
    {
        ReleaseCrosshairOverrideIfOwned();

        if (m_heldGrenadeVisualInstance != null)
        {
            Destroy(m_heldGrenadeVisualInstance);
        }

        if (m_runtimeLineMaterial != null)
        {
            Destroy(m_runtimeLineMaterial);
        }

        if (m_runtimeExplosionFillMaterial != null)
        {
            Destroy(m_runtimeExplosionFillMaterial);
        }

        if (m_explosionPreviewFillMesh != null)
        {
            Destroy(m_explosionPreviewFillMesh);
        }
    }

    private void OnValidate()
    {
        MigrateLegacyProjectile();
    }

    private void CacheGrenadeAnimationParameters()
    {
        m_hasGrenadeModeParameter = false;
        m_hasThrowParameter = false;
        m_grenadeActionLayerIndex = -1;

        if (m_animator == null)
        {
            return;
        }

        m_grenadeActionLayerIndex = m_animator.GetLayerIndex(GrenadeActionLayerName);

        foreach (AnimatorControllerParameter parameter in m_animator.parameters)
        {
            if (parameter.nameHash == AnimIDGrenadeMode
                && parameter.type == AnimatorControllerParameterType.Bool)
            {
                m_hasGrenadeModeParameter = true;
            }
            else if (parameter.nameHash == AnimIDThrow
                     && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                m_hasThrowParameter = true;
            }
        }
    }

    private void SetGrenadeAnimationMode(bool active)
    {
        if (m_hasGrenadeModeParameter)
        {
            m_animator.SetBool(AnimIDGrenadeMode, active);
        }
    }

    private void PlayThrowAnimation()
    {
        if (m_hasThrowParameter)
        {
            SetHeldGrenadeVisible(false);
            m_animator.SetTrigger(AnimIDThrow);
        }
    }

    /// <summary>
    /// Grenade Action Layer가 서기 또는 웅크리기 투척 상태를 재생 중인지 확인합니다.
    /// </summary>
    private bool IsGrenadeThrowAnimationActive()
    {
        if (m_animator == null || m_grenadeActionLayerIndex < 0
                               || m_grenadeActionLayerIndex >= m_animator.layerCount)
        {
            return false;
        }

        AnimatorStateInfo current = m_animator.GetCurrentAnimatorStateInfo(m_grenadeActionLayerIndex);
        if (IsGrenadeThrowState(current))
        {
            return true;
        }

        return m_animator.IsInTransition(m_grenadeActionLayerIndex)
               && IsGrenadeThrowState(m_animator.GetNextAnimatorStateInfo(m_grenadeActionLayerIndex));
    }

    private static bool IsGrenadeThrowState(AnimatorStateInfo state)
    {
        return state.shortNameHash == AnimStateGrenadeThrow
               || state.shortNameHash == AnimStateGrenadeCrouchThrow;
    }

    /// <summary>
    /// 총기 표시 상태는 유지하고 손에 든 수류탄 Visual만 전환합니다.
    /// </summary>
    private void SetHeldGrenadeVisible(bool visible)
    {
        if (m_heldGrenadeVisualInstance != null
            && m_heldGrenadeVisualInstance.activeSelf != visible)
        {
            m_heldGrenadeVisualInstance.SetActive(visible);
        }
    }

    /// <summary>
    /// 손에 표시할 수류탄과 숨길 총기 Renderer를 한 번 준비합니다.
    /// </summary>
    private void InitializeGrenadeEquipmentVisuals()
    {
        // 활성 총을 먼저 찾습니다. 비활성으로 남은 옛 총을 잡으면 투척 자세에서 실제 들고 있는 총이 숨지 않습니다.
        Gun activeWeapon = GetComponentInChildren<Gun>(false);
        Gun weapon = activeWeapon != null ? activeWeapon : GetComponentInChildren<Gun>(true);
        if (weapon != null)
        {
            m_weaponRenderers = weapon.GetComponentsInChildren<Renderer>(true);
            m_weaponRendererEnabledStates = new bool[m_weaponRenderers.Length];
        }

        if (m_heldGrenadeVisualPrefab == null)
        {
            return;
        }

        Transform heldSocket = m_heldGrenadeSocket;
        if (heldSocket == null && m_animator != null && m_animator.isHuman)
        {
            heldSocket = m_animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        if (heldSocket == null)
        {
            Debug.LogWarning($"[{name}] 수류탄 Visual을 연결할 손 장착 소켓을 찾지 못했습니다.", this);
            return;
        }

        m_heldGrenadeVisualInstance = Instantiate(m_heldGrenadeVisualPrefab, heldSocket, false);
        m_heldGrenadeVisualInstance.name = $"{m_heldGrenadeVisualPrefab.name} (Held)";

        Transform heldTransform = m_heldGrenadeVisualInstance.transform;
        heldTransform.localPosition = m_heldGrenadeLocalPosition;
        heldTransform.localRotation = Quaternion.Euler(m_heldGrenadeLocalEulerAngles);
        heldTransform.localScale = m_heldGrenadeLocalScale;
        m_heldGrenadeVisualInstance.SetActive(false);
    }

    /// <summary>
    /// 수류탄 모드에서는 총기 메시를 숨기고 오른손 수류탄 Visual을 표시합니다.
    /// </summary>
    private void SetGrenadeEquipmentVisible(bool visible)
    {
        if (m_grenadeEquipmentVisible == visible)
        {
            return;
        }

        m_grenadeEquipmentVisible = visible;

        if (visible)
        {
            for (int i = 0; m_weaponRenderers != null && i < m_weaponRenderers.Length; i++)
            {
                Renderer weaponRenderer = m_weaponRenderers[i];
                if (weaponRenderer == null)
                {
                    continue;
                }

                m_weaponRendererEnabledStates[i] = weaponRenderer.enabled;
                weaponRenderer.enabled = false;
            }

            if (m_heldGrenadeVisualInstance != null)
            {
                m_heldGrenadeVisualInstance.SetActive(true);
            }

            return;
        }

        if (m_heldGrenadeVisualInstance != null)
        {
            m_heldGrenadeVisualInstance.SetActive(false);
        }

        for (int i = 0; m_weaponRenderers != null && i < m_weaponRenderers.Length; i++)
        {
            Renderer weaponRenderer = m_weaponRenderers[i];
            if (weaponRenderer != null)
            {
                weaponRenderer.enabled = m_weaponRendererEnabledStates[i];
            }
        }
    }

    private void MigrateLegacyProjectile()
    {
        if (m_projectilePrefabs == null)
        {
            m_projectilePrefabs = new List<ProjectileBase>();
        }

        if (m_projectilePrefabs.Count == 0 && m_legacyProjectilePrefab != null)
        {
            m_projectilePrefabs.Add(m_legacyProjectilePrefab);
            m_legacyProjectilePrefab = null;
        }

        m_selectedProjectileIndex = WrapIndex(m_selectedProjectileIndex, m_projectilePrefabs.Count);
    }

    private void CycleProjectile(int selectionDelta)
    {
        if (selectionDelta == 0 || m_projectilePrefabs == null || m_projectilePrefabs.Count == 0)
        {
            return;
        }

        m_selectedProjectileIndex = WrapIndex(
            m_selectedProjectileIndex + selectionDelta,
            m_projectilePrefabs.Count);
    }

    private ProjectileBase GetSelectedProjectile()
    {
        if (m_projectilePrefabs == null || m_projectilePrefabs.Count == 0)
        {
            return m_legacyProjectilePrefab;
        }

        m_selectedProjectileIndex = WrapIndex(m_selectedProjectileIndex, m_projectilePrefabs.Count);
        return m_projectilePrefabs[m_selectedProjectileIndex];
    }

    /// <summary>투척 모드에 들어갈 때 현재 선택한 투척물을 꺼내는 2D 장비음을 한 번 재생합니다.</summary>
    private void PlaySelectedProjectileEquipSound()
    {
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        if (selectedProjectile == null || !FMODUnity.RuntimeManager.IsInitialized)
        {
            return;
        }

        bool isMolotov = selectedProjectile is TemporaryTrapProjectile;
        string eventPath = isMolotov
            ? MolotovEquipSoundEventPath
            : GrenadeEquipSoundEventPath;

        try
        {
            FMODUnity.RuntimeManager.PlayOneShot(eventPath);
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            bool alreadyLogged = isMolotov
                ? s_loggedMissingMolotovEquipSound
                : s_loggedMissingGrenadeEquipSound;
            if (alreadyLogged)
            {
                return;
            }

            if (isMolotov)
            {
                s_loggedMissingMolotovEquipSound = true;
            }
            else
            {
                s_loggedMissingGrenadeEquipSound = true;
            }

            Debug.LogWarning(
                $"[ExplosiveProjectileShooter] FMOD 이벤트를 찾지 못했습니다: {eventPath}\n{exception.Message}",
                this);
        }
    }

    private int GetProjectileCollisionLayers(ProjectileBase projectile)
    {
        int excludedLayers = projectile != null
            ? projectile.ContactExcludeLayers.value
            : 0;
        return m_collisionLayers.value & ~excludedLayers;
    }

    private static int WrapIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        return (index % count + count) % count;
    }

    private void ResolveGrenadeSelectionUI()
    {
        if (m_grenadeSelectionUI == null)
        {
            m_grenadeSelectionUI = FindFirstObjectByType<GrenadeSelectionUI>(FindObjectsInactive.Include);
        }
    }

    private void UpdateGrenadeSelectionUI(bool visible)
    {
        ResolveGrenadeSelectionUI();
        if (m_grenadeSelectionUI != null)
        {
            ProjectileBase selectedProjectile = GetSelectedProjectile();
            m_grenadeSelectionUI.SetState(
                this,
                visible,
                selectedProjectile,
                GetProjectileQuantity(selectedProjectile));
        }
    }

    private void ResolveInventoryManager()
    {
        if (m_inventoryManager == null)
        {
            m_inventoryManager = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        }
    }

    private int GetProjectileQuantity(ProjectileBase projectile)
    {
        ResolveInventoryManager();
        return projectile != null && m_inventoryManager != null
            ? m_inventoryManager.CountOf(projectile.InventoryItemDefinitionId)
            : 0;
    }

    private void ApplyCrosshairMode(bool throwModeActive)
    {
        if (m_crosshairModeInitialized && m_throwCrosshairActive == throwModeActive)
        {
            bool ownsExpectedOverride = ReferenceEquals(s_crosshairOverrideOwner, this)
                && s_crosshairOverrideTarget == m_defaultCrosshair;
            if ((throwModeActive && ownsExpectedOverride)
                || (!throwModeActive && !ReferenceEquals(s_crosshairOverrideOwner, this)))
            {
                return;
            }
        }

        ResolveDefaultCrosshair();
        if (throwModeActive)
        {
            if (m_defaultCrosshair != null && m_throwCrosshairPreset != null)
            {
                bool alreadyOwnsTarget = ReferenceEquals(s_crosshairOverrideOwner, this)
                    && s_crosshairOverrideTarget == m_defaultCrosshair;
                if (!alreadyOwnsTarget)
                {
                    RestoreAndClearCrosshairOverride();
                    s_crosshairOverrideOwner = this;
                    s_crosshairOverrideTarget = m_defaultCrosshair;
                    s_crosshairOverrideBaseline = CrosshairPreset.Capture(m_defaultCrosshair);
                }

                m_throwCrosshairPreset.Apply(m_defaultCrosshair);
            }
        }
        else
        {
            ReleaseCrosshairOverrideIfOwned();
        }

        m_throwCrosshairActive = throwModeActive
            && ReferenceEquals(s_crosshairOverrideOwner, this);
        m_crosshairModeInitialized = true;
    }

    /// <summary>
    /// 이 대원이 소유한 투척 조준선 임시값을 즉시 해제하고, 적용 전 공용 HUD 값으로 복원합니다.
    /// </summary>
    /// <remarks>
    /// 조작권을 다음 대원에게 넘기기 전에 호출해야 새 대원의 프로필 위로 이전 대원의 스냅샷이
    /// 뒤늦게 덮이지 않습니다. 소유자가 아닌 대원은 공용 HUD를 변경하지 않습니다.
    /// </remarks>
    public void ReleaseCrosshairOverrideIfOwned()
    {
        if (ReferenceEquals(s_crosshairOverrideOwner, this))
        {
            RestoreAndClearCrosshairOverride();
        }

        m_throwCrosshairActive = false;
        m_crosshairModeInitialized = true;
    }

    /// <summary>투척 모드에서 쓰는 조준선 모양 값의 복사본입니다.</summary>
    public CrosshairStyle ThrowCrosshairStyle => m_throwCrosshairPreset != null
        ? m_throwCrosshairPreset.ToStyle()
        : null;

    /// <summary>
    /// 투척 모드 조준선 모양을 바꿉니다. 이 대원이 지금 투척 모드면 공용 HUD에도 바로 적용합니다.
    /// </summary>
    /// <remarks>탄퍼짐 연동 여부는 설정 화면에서 다루지 않으므로 이 컴포넌트의 기존 값을 유지합니다.</remarks>
    public void SetThrowCrosshairStyle(CrosshairStyle style)
    {
        if (style == null)
        {
            return;
        }

        bool useSpreadAccuracy = m_throwCrosshairPreset != null && m_throwCrosshairPreset.ToStyle().useSpreadAccuracy;
        m_throwCrosshairPreset = CrosshairPreset.FromStyle(style, useSpreadAccuracy);
        if (ReferenceEquals(s_crosshairOverrideOwner, this) && s_crosshairOverrideTarget != null)
        {
            m_throwCrosshairPreset.Apply(s_crosshairOverrideTarget);
        }
    }

    /// <summary>
    /// 투척 모드가 공용 HUD를 덮고 있으면, 투척 모드가 끝난 뒤 복원할 값을 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 투척 모드 중에 캐릭터 조준선 설정이 바뀌면 지금 화면은 그대로 두고, 끝날 때 새 값으로 돌아가게 하려는 것입니다.
    /// 복원 값의 탄퍼짐 연동 여부는 투척 모드 진입 전 값을 유지합니다.
    /// </remarks>
    /// <returns>투척 모드가 <paramref name="target"/>을 덮고 있어 복원 값을 바꿨으면 true입니다.</returns>
    public static bool TryReplaceCrosshairBaseline(CrosshairController target, CrosshairStyle style)
    {
        if (target == null || style == null || ReferenceEquals(s_crosshairOverrideOwner, null)
            || s_crosshairOverrideTarget != target || s_crosshairOverrideBaseline == null)
        {
            return false;
        }

        bool useSpreadAccuracy = s_crosshairOverrideBaseline.ToStyle().useSpreadAccuracy;
        s_crosshairOverrideBaseline = CrosshairPreset.FromStyle(style, useSpreadAccuracy);
        return true;
    }

    private static void RestoreAndClearCrosshairOverride()
    {
        CrosshairController target = s_crosshairOverrideTarget;
        CrosshairPreset baseline = s_crosshairOverrideBaseline;
        ExplosiveProjectileShooter previousOwner = s_crosshairOverrideOwner;

        // 복원 중 다른 코드가 현재 소유자를 조회해도 이미 임시 레이어가 끝난 상태로 보이게 합니다.
        s_crosshairOverrideOwner = null;
        s_crosshairOverrideTarget = null;
        s_crosshairOverrideBaseline = null;

        if (target != null && baseline != null)
        {
            baseline.Apply(target);
        }

        if (!ReferenceEquals(previousOwner, null))
        {
            previousOwner.m_throwCrosshairActive = false;
            previousOwner.m_crosshairModeInitialized = true;
        }
    }

    private void ResolveDefaultCrosshair()
    {
        if (m_defaultCrosshair != null)
        {
            return;
        }

        if (m_aimController != null)
        {
            m_defaultCrosshair = m_aimController.CrosshairController;
        }

        if (m_defaultCrosshair == null)
        {
            m_defaultCrosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        }
    }

    private void ResolveTrajectory()
    {
        m_throwStart = ResolveThrowStartPosition();

        Vector3 aimDirection = m_aimController.CurrentAimPoint - m_throwStart;
        if (aimDirection.sqrMagnitude < 0.0001f)
        {
            aimDirection = transform.forward;
        }

        aimDirection.Normalize();

        Vector3 horizontalDirection = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            horizontalDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        horizontalDirection.Normalize();

        float ascentDownwardAcceleration = Mathf.Max(0.01f, m_ascentDownwardAcceleration);
        float descentDownwardAcceleration = Mathf.Max(0.01f, m_descentDownwardAcceleration);
        float upwardSpeed = m_arcHeight > 0.0f
            ? Mathf.Sqrt(2.0f * ascentDownwardAcceleration * m_arcHeight)
            : 0.0f;
        float ascentTime = upwardSpeed > 0.0f
            ? upwardSpeed / ascentDownwardAcceleration
            : 0.0f;
        float descentTime = m_arcHeight > 0.0f
            ? Mathf.Sqrt(2.0f * m_arcHeight / descentDownwardAcceleration)
            : 0.0f;
        float referenceFlightTime = Mathf.Max(0.01f, ascentTime + descentTime);
        float horizontalSpeed = m_referenceThrowDistance / referenceFlightTime;
        float launchSpeed = Mathf.Sqrt(horizontalSpeed * horizontalSpeed + upwardSpeed * upwardSpeed);
        float baseLaunchAngle = Mathf.Atan2(upwardSpeed, horizontalSpeed);
        float aimPitch = Mathf.Asin(Mathf.Clamp(aimDirection.y, -1.0f, 1.0f));
        float launchAngle = Mathf.Clamp(
            baseLaunchAngle + aimPitch,
            -80.0f * Mathf.Deg2Rad,
            80.0f * Mathf.Deg2Rad);

        m_initialVelocity =
            horizontalDirection * (Mathf.Cos(launchAngle) * launchSpeed) +
            Vector3.up * (Mathf.Sin(launchAngle) * launchSpeed);
    }

    private Vector3 ResolveThrowStartPosition()
    {
        Vector3 origin = m_heldGrenadeSocket != null
            ? m_heldGrenadeSocket.position
            : (m_sourceCollider != null ? m_sourceCollider.bounds.center : transform.position)
                + transform.TransformDirection(m_throwOriginOffset);

        return origin + transform.TransformDirection(m_throwStartOffset);
    }

    private void DrawTrajectory()
    {
        // 궤적 선은 토글이 켜졌을 때만 그립니다. 기본은 착탄 지점의 원형 표시만 씁니다.
        if (m_showTrajectoryLine)
        {
            m_trajectoryLine.enabled = true;
            m_trajectoryLine.positionCount = m_trajectoryPointCount;

            for (int i = 0; i < m_trajectoryPointCount; i++)
            {
                m_trajectoryLine.SetPosition(i, m_trajectoryPoints[i]);
            }
        }
        else
        {
            m_trajectoryLine.enabled = false;
            m_trajectoryLine.positionCount = 0;
        }

        DrawExplosionPreview();
    }

    private void BuildTrajectoryPlan()
    {
        int segmentCount = Mathf.Clamp(m_trajectorySegments, 4, MaxTrajectoryPointCount - 1);
        m_trajectoryPointCount = 1;
        m_trajectoryPoints[0] = m_throwStart;
        m_hasPlannedCollision = false;
        m_plannedCollisionTime = 0.0f;
        m_plannedCollisionPosition = m_throwStart;
        m_plannedContactPoint = m_throwStart;
        m_plannedSurfaceNormal = Vector3.up;
        m_hasExplosionPreview = false;
        m_explosionPreviewCenter = m_throwStart;

        Vector3 previous = m_throwStart;
        float previousTime = 0.0f;
        float previewDistance = Mathf.Max(0.1f, m_trajectoryPreviewDistance);
        float targetSegmentLength = previewDistance / segmentCount;
        float accumulatedDistance = 0.0f;
        float throwSpeedMultiplier = Mathf.Max(0.01f, m_throwSpeedMultiplier);
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        float trajectoryTimeLimit = selectedProjectile != null
            ? Mathf.Max(0.0f, selectedProjectile.FlightTimeLimit) * throwSpeedMultiplier
            : float.PositiveInfinity;

        if (trajectoryTimeLimit <= 0.0f)
        {
            m_hasExplosionPreview = true;
            return;
        }

        for (int i = 1; i <= segmentCount; i++)
        {
            Vector3 currentVelocity = ParabolicProjectileMover.EvaluateVelocity(
                m_initialVelocity,
                m_ascentDownwardAcceleration,
                m_descentDownwardAcceleration,
                previousTime);
            float sampleInterval = Mathf.Clamp(
                targetSegmentLength / Mathf.Max(currentVelocity.magnitude, 1.0f),
                0.01f,
                0.25f);
            float currentTime = Mathf.Min(previousTime + sampleInterval, trajectoryTimeLimit);

            if (currentTime <= previousTime)
            {
                return;
            }

            Vector3 next = ParabolicProjectileMover.EvaluatePosition(
                m_throwStart,
                m_initialVelocity,
                m_ascentDownwardAcceleration,
                m_descentDownwardAcceleration,
                currentTime);
            Vector3 segment = next - previous;
            float segmentDistance = segment.magnitude;
            float remainingPreviewDistance = previewDistance - accumulatedDistance;
            bool reachedPreviewLimit = segmentDistance >= remainingPreviewDistance;

            if (reachedPreviewLimit && segmentDistance > 0.0001f)
            {
                float previewFraction = Mathf.Clamp01(remainingPreviewDistance / segmentDistance);
                currentTime = Mathf.Lerp(previousTime, currentTime, previewFraction);
                next = ParabolicProjectileMover.EvaluatePosition(
                    m_throwStart,
                    m_initialVelocity,
                    m_ascentDownwardAcceleration,
                    m_descentDownwardAcceleration,
                    currentTime);
                segment = next - previous;
                segmentDistance = segment.magnitude;
            }

            if (TryGetBlockingHit(previous, next, out RaycastHit hit))
            {
                float hitFraction = segmentDistance > 0.0001f
                    ? Mathf.Clamp01(hit.distance / segmentDistance)
                    : 0.0f;

                m_hasPlannedCollision = true;
                m_plannedCollisionTime = Mathf.Lerp(previousTime, currentTime, hitFraction);
                m_plannedCollisionPosition = ParabolicProjectileMover.EvaluatePosition(
                    m_throwStart,
                    m_initialVelocity,
                    m_ascentDownwardAcceleration,
                    m_descentDownwardAcceleration,
                    m_plannedCollisionTime);
                m_plannedContactPoint = hit.point;
                m_plannedSurfaceNormal = hit.normal;
                m_hasExplosionPreview = true;
                m_explosionPreviewCenter = m_plannedCollisionPosition;

                if ((m_plannedCollisionPosition - previous).sqrMagnitude > 0.000001f)
                {
                    m_trajectoryPoints[m_trajectoryPointCount] = m_plannedCollisionPosition;
                    m_trajectoryPointCount++;
                }

                return;
            }

            m_trajectoryPoints[m_trajectoryPointCount] = next;
            m_trajectoryPointCount++;
            accumulatedDistance += segmentDistance;

            if (currentTime >= trajectoryTimeLimit)
            {
                m_hasExplosionPreview = true;
                m_explosionPreviewCenter = next;
                return;
            }

            if (reachedPreviewLimit)
            {
                return;
            }

            previous = next;
            previousTime = currentTime;
        }
    }

    private bool ThrowProjectile()
    {
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        if (selectedProjectile == null)
        {
            Debug.LogWarning($"[{name}] 투척할 Projectile Prefab이 없습니다.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedProjectile.InventoryItemDefinitionId))
        {
            Debug.LogWarning($"[{name}] '{selectedProjectile.name}'에 Inventory Item Definition이 연결되지 않았습니다.", this);
            return false;
        }

        ResolveInventoryManager();
        if (m_inventoryManager == null)
        {
            Debug.LogWarning($"[{name}] SquadInventoryManager를 찾지 못해 투척물을 사용할 수 없습니다.", this);
            return false;
        }

        if (GetProjectileQuantity(selectedProjectile) <= 0)
        {
            return false;
        }

        Quaternion rotation = m_initialVelocity.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(m_initialVelocity.normalized, Vector3.up)
            : transform.rotation;

        ProjectileBase projectile = Instantiate(selectedProjectile, m_throwStart, rotation);
        Collider[] projectileColliders = projectile.GetComponentsInChildren<Collider>(true);
        foreach (Collider projectileCollider in projectileColliders)
        {
            projectileCollider.isTrigger = true;
        }

        Rigidbody projectileRigidbody = projectile.GetComponent<Rigidbody>();
        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;
        projectileRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        projectileRigidbody.linearVelocity = Vector3.zero;
        projectileRigidbody.angularVelocity = Vector3.zero;

        ParabolicProjectileMover mover = projectile.gameObject.AddComponent<ParabolicProjectileMover>();
        mover.Initialize(
            projectile,
            m_throwStart,
            m_initialVelocity,
            m_ascentDownwardAcceleration,
            m_descentDownwardAcceleration,
            m_throwSpeedMultiplier,
            m_hasPlannedCollision,
            m_plannedCollisionTime,
            m_plannedCollisionPosition,
            m_plannedContactPoint,
            m_plannedSurfaceNormal,
            m_collisionRadius,
            GetProjectileCollisionLayers(selectedProjectile),
            transform);

        if (!m_inventoryManager.TryConsumeOne(selectedProjectile.InventoryItemDefinitionId))
        {
            Destroy(projectile.gameObject);
            return false;
        }

        UpdateGrenadeSelectionUI(true);

        return true;
    }

    private bool TryGetBlockingHit(Vector3 start, Vector3 end, out RaycastHit nearestHit)
    {
        nearestHit = default;
        Vector3 movement = end - start;
        float distance = movement.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            m_collisionRadius,
            movement / distance,
            m_previewHits,
            distance,
            GetProjectileCollisionLayers(GetSelectedProjectile()),
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = m_previewHits[i];
            if (candidate.collider == null || IsOwnedByPlayer(candidate.collider.transform))
            {
                continue;
            }

            if (candidate.distance < nearestDistance)
            {
                nearestDistance = candidate.distance;
                nearestHit = candidate;
                found = true;
            }
        }

        return found;
    }

    private bool IsOwnedByPlayer(Transform hitTransform)
    {
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private void CreateTrajectoryLine()
    {
        GameObject lineObject = new GameObject("ThrowTrajectoryPreview");
        lineObject.transform.SetParent(transform, false);

        m_trajectoryLine = lineObject.AddComponent<LineRenderer>();
        ConfigurePreviewLine(m_trajectoryLine, false);

        GameObject explosionFillObject = new GameObject("ExplosionRadiusFill");
        explosionFillObject.transform.SetParent(transform, false);

        MeshFilter explosionFillFilter = explosionFillObject.AddComponent<MeshFilter>();
        m_explosionPreviewFillRenderer = explosionFillObject.AddComponent<MeshRenderer>();
        m_explosionPreviewFillRenderer.shadowCastingMode = ShadowCastingMode.Off;
        m_explosionPreviewFillRenderer.receiveShadows = false;
        m_explosionPreviewFillRenderer.sortingOrder = -1;
        m_explosionPreviewFillRenderer.enabled = false;

        m_explosionPreviewFillMesh = CreateExplosionPreviewFillMesh();
        explosionFillFilter.sharedMesh = m_explosionPreviewFillMesh;

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader != null)
        {
            m_runtimeLineMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_runtimeLineMaterial.SetColor("_BaseColor", m_trajectoryColor);
            m_trajectoryLine.sharedMaterial = m_runtimeLineMaterial;

            m_runtimeExplosionFillMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            ConfigureTransparentPreviewMaterial(m_runtimeExplosionFillMaterial);
            m_runtimeExplosionFillMaterial.SetFloat("_Cull", (float)CullMode.Off);
            m_explosionPreviewFillRenderer.sharedMaterial = m_runtimeExplosionFillMaterial;
        }
    }

    private static Mesh CreateExplosionPreviewFillMesh()
    {
        Vector3[] vertices = new Vector3[ExplosionPreviewSegmentCount + 1];
        Vector3[] normals = new Vector3[vertices.Length];
        int[] triangles = new int[ExplosionPreviewSegmentCount * 3];

        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;

        for (int i = 0; i < ExplosionPreviewSegmentCount; i++)
        {
            float angle = 2.0f * Mathf.PI * i / ExplosionPreviewSegmentCount;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            normals[i + 1] = Vector3.up;

            int triangleIndex = i * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = ((i + 1) % ExplosionPreviewSegmentCount) + 1;
            triangles[triangleIndex + 2] = i + 1;
        }

        Mesh mesh = new Mesh
        {
            name = "Explosion Radius Fill Mesh",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            normals = normals,
            triangles = triangles,
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ConfigureTransparentPreviewMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1.0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0.0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void ConfigurePreviewLine(LineRenderer lineRenderer, bool loop)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = loop;
        lineRenderer.widthMultiplier = m_trajectoryWidth;
        lineRenderer.startColor = m_trajectoryColor;
        lineRenderer.endColor = m_trajectoryColor;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.enabled = false;
    }

    private void DrawExplosionPreview()
    {
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        if (!m_hasExplosionPreview || selectedProjectile == null)
        {
            HideExplosionPreview();
            return;
        }

        float radius = Mathf.Max(0.0f, selectedProjectile.ImpactPreviewRadius);
        if (radius <= 0.0f)
        {
            HideExplosionPreview();
            return;
        }

        Vector3 center = m_explosionPreviewCenter + Vector3.up * ExplosionPreviewHeightOffset;
        ApplyImpactPreviewAppearance();

        if (m_explosionPreviewFillRenderer != null)
        {
            Transform fillTransform = m_explosionPreviewFillRenderer.transform;
            fillTransform.position = center;
            fillTransform.rotation = Quaternion.identity;
            fillTransform.localScale = new Vector3(radius, 1.0f, radius);
            m_explosionPreviewFillRenderer.enabled = m_impactPreviewFillAlpha > 0.0f;
        }
    }

    private void ApplyImpactPreviewAppearance()
    {
        if (m_runtimeExplosionFillMaterial == null)
        {
            return;
        }

        Color fillColor = m_impactPreviewColor;
        fillColor.a = Mathf.Clamp01(m_impactPreviewFillAlpha);
        m_runtimeExplosionFillMaterial.SetColor("_BaseColor", fillColor);
    }

    private void HideExplosionPreview()
    {
        if (m_explosionPreviewFillRenderer != null)
        {
            m_explosionPreviewFillRenderer.enabled = false;
        }
    }

    private void HideTrajectory()
    {
        if (m_trajectoryLine != null)
        {
            m_trajectoryLine.enabled = false;
            m_trajectoryLine.positionCount = 0;
        }

        HideExplosionPreview();
    }
}
