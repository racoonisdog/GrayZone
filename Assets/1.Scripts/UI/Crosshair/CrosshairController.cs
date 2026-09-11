using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
using VInspector;

/// <summary>
/// UI Toolkit 기반 전투 조준선의 표시, 모양, 탄퍼짐 간격을 제어하는 컴포넌트입니다.
/// 조준선의 경우 벡터 이미지(.svg)를 활용하고 있습니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 같은 GameObject의 <see cref="UIDocument"/>를 필수 참조로 사용하며, 조준선 파츠는
/// 중앙 표시(Main)와 상하좌우 팔(Sub)로 나뉜 <see cref="VisualElement"/> 트리로 구성됩니다.
/// 필요한 VisualElement가 UXML에 없으면 <see cref="m_createMissingElements"/> 설정에 따라 런타임에 자동 생성합니다.
/// 무기 탄퍼짐 방사각(도)은 <see cref="SetSpread"/>를 통해 <see cref="AimController"/>가 전달하며,
/// 카메라 FOV 기준으로 화면 픽셀 간격으로 환산되어 팔 벌어짐으로 표시됩니다.
/// </remarks>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class CrosshairController : MonoBehaviour
{
    private const float GapSnapEpsilon = 0.01f;
    private const float LowAmmoGaugeThreshold = 0.33f;

    private const float RingReferenceThicknessPixels = 1.0f;

    /// <summary>중앙 표시(Main) 형태입니다.</summary>
    public enum MainShape
    {
        /// <summary>중앙 표시를 그리지 않습니다.</summary>
        None = 0,

        /// <summary>둥근 점(Dot)으로 표시합니다.</summary>
        Dot = 1,

        /// <summary>중앙 원형 링으로 표시합니다.</summary>
        Ring = 2,
    }

    /// <summary>보조 표시(Sub, 상하좌우 팔) 형태입니다.</summary>
    public enum SubShape
    {
        /// <summary>보조 표시(팔)를 그리지 않습니다.</summary>
        None = 0,

        /// <summary>상하좌우 둥근 십자(Rounded Cross) 팔로 표시합니다.</summary>
        RoundedCross = 1,

        /// <summary>상하좌우 사각 십자(Square Cross) 팔로 표시합니다.</summary>
        SquareCross = 2,

        /// <summary>중앙 기준 원형 링으로 표시합니다.</summary>
        Ring = 3,

        /// <summary>상하좌우 점(Dot)으로 표시합니다.</summary>
        Dot = 4,
    }

    /// <summary>
    /// 조준선 팔 끝이 탄퍼짐 콘의 "어느 반경"을 가리킬지 정하는 표시 기준입니다.
    /// </summary>
    /// <remarks>
    /// 이건 순수하게 "어떻게 보여줄까"의 표현 선택이며, 실제 탄 궤적은 바꾸지 않습니다(실제 분포는 무기가 소유).
    /// 탄은 콘 경계(하드캡)까지 거의 안 가고 중심에 몰리므로, 팔 끝을 콘 경계에 두면 실제 탄착보다 몇 배 넓어 보입니다.
    /// 무기의 분포·집중도(<see cref="Gun.Distribution"/>/<see cref="Gun.SpreadConcentration"/>)를
    /// 읽어, 선택한 기준의 반경을 표시 배율(factor)로 환산합니다.
    /// </remarks>
    public enum SpreadDisplayBasis
    {
        /// <summary>콘 경계(하드캡). 이론상 최대 편향이며 탄은 극히 일부만 도달합니다(가장 넓게 보임).</summary>
        ConeEdge,

        /// <summary>대부분의 탄이 들어오는 반경입니다(Gaussian 기준 약 2σ ≈ 86%). "이 안에 거의 다 맞는다".</summary>
        MostShots,

        /// <summary>통상적으로 탄이 떨어지는 반경(RMS)입니다. 팔 밖에 박히는 탄도 꽤 있습니다.</summary>
        Typical,

        /// <summary>탄이 가장 빽빽하게 몰리는 밀집 코어 반경입니다(Gaussian 최빈 = σ). 가장 타이트하며, 실제 밀집 그룹에 딱 붙습니다.</summary>
        Core,
    }

    /// <summary>탄약 게이지가 채워지는 방향입니다.</summary>
    public enum AmmoGaugeFillDirection
    {
        /// <summary>시계 방향으로 채웁니다.</summary>
        Clockwise,

        /// <summary>반시계 방향으로 채웁니다.</summary>
        CounterClockwise,
    }

    [Foldout("References")]
    [Tooltip("조준선 UXML을 표시하는 UIDocument입니다. 비워두면 같은 GameObject에서 찾습니다.")]
    [SerializeField] private UIDocument m_document;

    [Tooltip("전체 화면 루트 VisualElement 이름입니다.")]
    [SerializeField] private string m_rootElementName = "CrosshairRoot";

    [Tooltip("상하좌우 파츠를 담는 중앙 컨테이너 VisualElement 이름입니다.")]
    [SerializeField] private string m_crosshairElementName = "Crosshair";

    [Tooltip("중앙점 VisualElement 이름입니다.")]
    [SerializeField] private string m_centerElementName = "Center";

    [Tooltip("위쪽 팔 VisualElement 이름입니다.")]
    [SerializeField] private string m_topElementName = "Top";

    [Tooltip("위쪽 팔/점 스트로크 VisualElement 이름입니다.")]
    [SerializeField] private string m_topStrokeElementName = "TopStroke";

    [Tooltip("아래쪽 팔 VisualElement 이름입니다.")]
    [SerializeField] private string m_bottomElementName = "Bottom";

    [Tooltip("아래쪽 팔/점 스트로크 VisualElement 이름입니다.")]
    [SerializeField] private string m_bottomStrokeElementName = "BottomStroke";

    [Tooltip("왼쪽 팔 VisualElement 이름입니다.")]
    [SerializeField] private string m_leftElementName = "Left";

    [Tooltip("왼쪽 팔/점 스트로크 VisualElement 이름입니다.")]
    [SerializeField] private string m_leftStrokeElementName = "LeftStroke";

    [Tooltip("오른쪽 팔 VisualElement 이름입니다.")]
    [SerializeField] private string m_rightElementName = "Right";

    [Tooltip("오른쪽 팔/점 스트로크 VisualElement 이름입니다.")]
    [SerializeField] private string m_rightStrokeElementName = "RightStroke";

    [Tooltip("중앙 표시 스트로크 VisualElement 이름입니다.")]
    [FormerlySerializedAs("m_mainRingStrokeElementName")]
    [FormerlySerializedAs("m_mainRingLineElementName")]
    [SerializeField] private string m_mainStrokeElementName = "MainStroke";

    [Tooltip("보조 원형 표시 VisualElement 이름입니다.")]
    [FormerlySerializedAs("m_subRingElementName")]
    [SerializeField] private string m_subShapeElementName = "Sub";

    [Tooltip("보조 원형 표시 스트로크 VisualElement 이름입니다.")]
    [FormerlySerializedAs("m_subRingStrokeElementName")]
    [FormerlySerializedAs("m_subRingLineElementName")]
    [SerializeField] private string m_subStrokeElementName = "SubStroke";

    [Tooltip("켜면 UXML에 필요한 VisualElement가 없을 때 런타임에 자동 생성합니다.")]
    [SerializeField] private bool m_createMissingElements = true;

    [Foldout("Spread")]
    [Tooltip("켜면 무기 현재 spread를 조준선 벌어짐에 반영합니다. 끄면 spread 기여분은 0이 되어 Center Space만 최종 gap으로 사용합니다.")]
    [SerializeField] private bool m_useSpreadAccuracy = true;

    [Tooltip("조준선 팔 끝이 탄퍼짐 콘의 어느 반경을 가리킬지 정합니다. ConeEdge=콘 경계(하드캡, 가장 넓음), MostShots=대부분 포함(≈2σ), Typical=통상 탄착(RMS), Core=밀집 코어(가장 타이트). 표시 기준만 바꾸며 실제 탄 궤적에는 영향이 없습니다. 무기의 분포·집중도를 읽어 배율로 환산합니다.")]
    [SerializeField] private SpreadDisplayBasis m_spreadDisplayBasis = SpreadDisplayBasis.MostShots;

    [Tooltip("켜면 Max Gap Pixels를 상한(안전 클램프)으로 써서 조준선이 그 이상 벌어지지 않게 합니다. 끄면 물리 투영값을 그대로 사용합니다.")]
    [SerializeField] private bool m_clampToMaxGap = false;

    [Tooltip("조준선 최대 벌어짐 상한(픽셀)입니다. Clamp To Max Gap이 켜져 있을 때만 적용됩니다.")]
    [ShowIf(nameof(m_clampToMaxGap))]
    [SerializeField] private float m_maxGapPixels = 220.0f;

    [EndIf]
    [Tooltip("최종 gap(중앙 간격 + 탄퍼짐 + 발당 펄스)이 목표를 따라가는 보간 속도입니다. 계수는 1-e^(-속도×dt)이라 프레임레이트와 무관하며, 초반이 빠르고 뒤로 갈수록 느려지는 앞쏠림 곡선입니다. 값이 클수록 즉각적이고, 0 이하이면 목표를 즉시 반영합니다(첫 발이 스냅처럼 보입니다).")]
    [SerializeField] private float m_lerpSpeed = 0.0f;

    [Foldout("Shot Recoil Feedback")]
    [Tooltip("켜면 실제 발사가 성사될 때마다 크로스헤어 팔이 한 번 빠르게 벌어졌다 복귀합니다. 무기의 실제 탄퍼짐·탄착에는 영향을 주지 않는 UI 피드백입니다.")]
    [SerializeField] private bool m_enableShotRecoilPulse = true;

    [Tooltip("발사 한 발이 크로스헤어 간격에 더하는 최대 벌어짐(픽셀)입니다. 연사 중에는 각 발의 펄스가 겹쳐집니다.")]
    [ShowIf(nameof(m_enableShotRecoilPulse))]
    [Min(0.0f)]
    [SerializeField] private float m_shotRecoilPulseAmplitudePixels = 14.0f;

    [Tooltip("발사 반동 UI 펄스 하나의 전체 길이(초)입니다. 피크 위치는 Shot Recoil Pulse Curve의 x축으로 정합니다.")]
    [ShowIf(nameof(m_enableShotRecoilPulse))]
    [Min(0.0f)]
    [SerializeField] private float m_shotRecoilPulseDuration = 0.20f;

    [Tooltip("발사 반동 UI 펄스 곡선입니다. 기본값은 약 32ms 안에 피크로 튀어 오른 뒤 부드럽게 복귀합니다. x=정규화 시간, y=벌어짐 배율입니다.")]
    [ShowIf(nameof(m_enableShotRecoilPulse))]
    [SerializeField]
    private AnimationCurve m_shotRecoilPulseCurve = ImpulseEnvelope.BuildFastAttackConstantReleaseCurve(0.16f, 1.0f, 0.85f);
    [EndIf]

    [Foldout("Shape Options")]
    [Header("Main")]
    [Tooltip("중앙 표시 형태입니다.")]
    [FormerlySerializedAs("m_centerShape")]
    [SerializeField] private MainShape m_mainShape = MainShape.Dot;

    [Tooltip("중앙 표시의 통일 가로/세로 크기(픽셀)입니다.")]
    [FormerlySerializedAs("m_centerSizePixels")]
    [ShowIf(nameof(m_mainShape), MainShape.Dot)]
    [SerializeField] private float m_mainSizePixels = 1.0f;

    [Tooltip("중앙 링을 가장 얇게 그렸을 때의 기준 바깥 지름(픽셀)입니다.")]
    [ShowIf(nameof(m_mainShape), MainShape.Ring)]
    [SerializeField] private float m_mainRingSizePixels = 8.0f;

    [Tooltip("중앙 링 본체 두께(픽셀)입니다. 기준 링의 안쪽 지름은 유지하고 바깥쪽으로 확장됩니다.")]
    [ShowIf(nameof(m_mainShape), MainShape.Ring)]
    [SerializeField] private float m_mainRingThicknessPixels = 1.0f;

    [Tooltip("중앙 표시의 채움 색상입니다.")]
    [FormerlySerializedAs("m_centerColor")]
    [ShowIf(nameof(HasMainShape))]
    [SerializeField] private Color m_mainColor = Color.white;

    [Tooltip("중앙 표시의 스트로크 두께(픽셀)입니다.")]
    [FormerlySerializedAs("m_lineThicknessPixels")]
    [FormerlySerializedAs("m_mainLineThicknessPixels")]
    [ShowIf(nameof(HasMainShape))]
    [SerializeField] private float m_mainStrokeThicknessPixels = 1.0f;

    [Tooltip("중앙 표시의 스트로크 색상입니다.")]
    [FormerlySerializedAs("m_lineColor")]
    [FormerlySerializedAs("m_mainLineColor")]
    [ShowIf(nameof(HasMainShape))]
    [SerializeField] private Color m_mainStrokeColor = Color.black;

    [Header("Sub")]
    [Tooltip("보조 표시 형태입니다.")]
    [SerializeField] private SubShape m_subShape = SubShape.RoundedCross;

    [Tooltip("기본 오프셋입니다. 최종 gap 계산의 시작값으로 항상 더해지며, 동적 크로스헤어가 켜져 있으면 최종 gap = Center Space + spread 기여분입니다.")]
    [FormerlySerializedAs("m_baseGapPixels")]
    [ShowIf(nameof(HasSubShape))]
    [SerializeField] private float m_centerSpacePixels = 1.0f;

    [Tooltip("상하좌우 보조 점을 가장 얇게 그렸을 때의 기준 지름(픽셀)입니다.")]
    [ShowIf(nameof(m_subShape), SubShape.Dot)]
    [SerializeField] private float m_subSizePixels = 1.0f;

    [Tooltip("상하좌우 팔의 통일 길이(픽셀)입니다. 좌우는 가로 길이, 상하는 세로 길이에 적용됩니다.")]
    [FormerlySerializedAs("m_crossWidthPixels")]
    [FormerlySerializedAs("m_armLengthPixels")]
    [ShowIf(nameof(HasSubCrossShape))]
    [SerializeField] private float m_subWidthPixels = 1.0f;

    [Tooltip("상하좌우 팔의 통일 두께(픽셀)입니다. 좌우는 세로 두께, 상하는 가로 두께에 적용됩니다.")]
    [FormerlySerializedAs("m_crossThicknessPixels")]
    [FormerlySerializedAs("m_armThicknessPixels")]
    [ShowIf(nameof(HasSubCrossShape))]
    [SerializeField] private float m_subThicknessPixels = 1.0f;

    [Tooltip("보조 링을 가장 얇게 그렸을 때의 기준 바깥 지름(픽셀)입니다. 탄퍼짐이 커지면 이 지름에 현재 벌어짐 간격이 더해집니다.")]
    [ShowIf(nameof(m_subShape), SubShape.Ring)]
    [SerializeField] private float m_subRingSizePixels = 16.0f;

    [Tooltip("보조 링 본체 두께(픽셀)입니다. 기준 링의 안쪽 지름은 유지하고 바깥쪽으로 확장됩니다.")]
    [ShowIf(nameof(m_subShape), SubShape.Ring)]
    [SerializeField] private float m_subRingThicknessPixels = 1.0f;

    [Tooltip("상하좌우 팔의 채움 색상입니다.")]
    [FormerlySerializedAs("m_crossColor")]
    [FormerlySerializedAs("m_armColor")]
    [ShowIf(nameof(HasSubShape))]
    [SerializeField] private Color m_subColor = Color.white;

    [Tooltip("보조 표시의 스트로크 두께(픽셀)입니다.")]
    [FormerlySerializedAs("m_subLineThicknessPixels")]
    [ShowIf(nameof(HasSubShape))]
    [SerializeField] private float m_subStrokeThicknessPixels = 1.0f;

    [Tooltip("보조 표시의 스트로크 색상입니다.")]
    [FormerlySerializedAs("m_subLineColor")]
    [ShowIf(nameof(HasSubShape))]
    [SerializeField] private Color m_subStrokeColor = Color.black;

    [Tooltip("상하좌우 팔의 통일 모서리 둥글기(픽셀)입니다.")]
    [FormerlySerializedAs("m_armCornerRadiusPixels")]
    [ShowIf(nameof(m_subShape), SubShape.RoundedCross)]
    [SerializeField] private float m_cornerRadiusPixels = 0.0f;

    [EndIf]
    [Foldout("Reload Ammo")]
    [Header("Reload Swap")]
    [Tooltip("켜면 재장전 중 크로스헤어를 숨기고 중앙에 재장전 탄약 아이콘을 표시합니다. 끄면 재장전 중에도 크로스헤어를 유지합니다.")]
    [SerializeField] private bool m_swapCrosshairOnReload = true;

    [Tooltip("재장전 시 중앙에 표시할 탄약 아이콘 벡터 이미지(예: Reloading_Bullet.svg)입니다.")]
    [SerializeField] private Texture2D m_reloadBulletImage;

    [Tooltip("재장전 탄약 아이콘의 표시 크기(픽셀)입니다.")]
    [SerializeField] private float m_reloadBulletSizePixels = 50.0f;

    [Tooltip("재장전 중 탄약 아이콘이 페이드로 깜빡이는 속도입니다. 0 이하이면 깜빡이지 않고 항상 표시합니다.")]
    [SerializeField] private float m_reloadBlinkSpeed = 3.0f;

    [Range(0.0f, 1.0f)]
    [Tooltip("재장전 깜빡임의 최소 불투명도입니다. 이 값과 1 사이를 오갑니다.")]
    [SerializeField] private float m_reloadBlinkMinAlpha = 0.15f;

    [Header("Ammo Gauge")]
    [Tooltip("켜면 크로스헤어 우하단에 탄약 아크 게이지를 표시합니다.")]
    [SerializeField] private bool m_showAmmoGauge = true;

    [Tooltip("켜면 재장전 중이 아닐 때도 게이지를 항상 표시합니다(평소 = 현재 탄약 비율, 재장전 중 = 재장전 진행도). 끄면 재장전 중에만 표시합니다.")]
    [SerializeField] private bool m_ammoGaugeAlwaysVisible = true;

    [Tooltip("게이지 아크 색상입니다. Figma Gauge_Ammo 원본은 #D9D9D9입니다.")]
    [SerializeField] private Color m_ammoGaugeColor = new Color32(217, 217, 217, 255);

    [Tooltip("현재 장탄이 33% 미만일 때 게이지 아크에 사용할 경고 색상입니다.")]
    [SerializeField] private Color m_lowAmmoGaugeColor = new Color32(226, 59, 59, 255);

    [Tooltip("재장전 중 재장전 진행도 아크에 사용할 색상입니다.")]
    [SerializeField] private Color m_reloadAmmoGaugeColor = new Color32(217, 217, 217, 255);

    [Tooltip("게이지 아크의 선 두께(픽셀)입니다. Figma 원본은 100px 크기 기준 5px입니다.")]
    [SerializeField] private float m_ammoGaugeThicknessPixels = 5.0f;

    [Tooltip("아크 시작 각도(도)입니다. 0 = 3시 방향이며 시계 방향으로 진행합니다.")]
    [SerializeField] private float m_ammoGaugeStartAngleDegrees = 0.0f;

    [Tooltip("채움 아크가 시작 각도에서 진행되는 방향입니다.")]
    [SerializeField] private AmmoGaugeFillDirection m_ammoGaugeFillDirection = AmmoGaugeFillDirection.Clockwise;

    [Range(0.0f, 360.0f)]
    [Tooltip("아크 전체 구간(도)입니다. Figma 원본은 우하단 4분원(90도)이며, 360이면 완전한 링으로 채워집니다.")]
    [SerializeField] private float m_ammoGaugeSweepDegrees = 90.0f;

    [Tooltip("켜면 진행분 아크 뒤에 전체 구간 배경 바를 표시합니다.")]
    [FormerlySerializedAs("m_showAmmoGaugeTrack")]
    [SerializeField] private bool m_showAmmoGaugeBackground = true;

    [Range(0.0f, 1.0f)]
    [ShowIf(nameof(m_showAmmoGaugeBackground))]
    [Tooltip("배경 바의 불투명도 배율입니다.")]
    [FormerlySerializedAs("m_ammoGaugeTrackAlpha")]
    [SerializeField] private float m_ammoGaugeBackgroundAlpha = 0.25f;

    [ShowIf(nameof(m_showAmmoGaugeBackground))]
    [Tooltip("배경 바 색상입니다.")]
    [SerializeField] private Color m_ammoGaugeBackgroundColor = new Color32(217, 217, 217, 255);
    [EndIf]

    [Tooltip("탄약 게이지의 표시 크기(픽셀)입니다.")]
    [SerializeField] private float m_ammoGaugeSizePixels = 100.0f;

    [Tooltip("크로스헤어 중심에서 우하단 대각 방향으로 탄약 게이지를 얼마나 떨어뜨릴지(픽셀)입니다. x·y 각 축에 동일 적용됩니다.")]
    [SerializeField] private float m_ammoGaugeDiagonalOffset = 40.0f;

    [Foldout("Hit Feedback")]
    [Header("Hit Marker")]
    [Tooltip("켜면 적을 맞혔을 때 중앙에 X자 히트마커를 잠깐 표시합니다.")]
    [SerializeField] private bool m_showHitMarker = true;

    [Tooltip("몸샷 히트마커 색상입니다. Figma Crosshair_Hit 원본은 #D9D9D9입니다.")]
    [SerializeField] private Color m_hitMarkerColorBody = new Color32(217, 217, 217, 255);

    [Tooltip("헤드샷 히트마커 색상입니다. Figma Crosshair_HeadShot 원본은 빨강 계열입니다.")]
    [SerializeField] private Color m_hitMarkerColorHead = new Color32(226, 59, 59, 255);

    [Tooltip("히트마커 각 삼각형의 길이(픽셀)입니다. 중앙에서 바깥으로 뻗는 방향 길이입니다. 딜량 비례가 꺼져 있을 때 쓰는 고정 길이입니다.")]
    [SerializeField] private float m_hitMarkerLengthPixels = 26.0f;

    [Tooltip("켜면 히트마커 길이가 그 명중의 피해량에 비례해 달라집니다. 끄면 위의 고정 길이를 씁니다.")]
    [SerializeField] private bool m_enableHitMarkerDamageScale = true;

    [Tooltip("길이 비례의 기준이 되는 최소 피해량입니다. 이 이하의 피해는 모두 최소 길이로 표시됩니다.")]
    [ShowIf(nameof(m_enableHitMarkerDamageScale))]
    [SerializeField] private float m_hitMarkerMinDamage = 1.0f;

    [Tooltip("길이 비례의 기준이 되는 최대 피해량입니다. 이 이상의 피해는 모두 최대 길이로 표시됩니다.")]
    [ShowIf(nameof(m_enableHitMarkerDamageScale))]
    [SerializeField] private float m_hitMarkerMaxDamage = 10.0f;

    [Tooltip("최소 피해량일 때의 히트마커 삼각형 길이(픽셀)입니다.")]
    [ShowIf(nameof(m_enableHitMarkerDamageScale))]
    [Min(0.0f)]
    [SerializeField] private float m_hitMarkerLengthAtMinDamagePixels = 16.0f;

    [Tooltip("최대 피해량일 때의 히트마커 삼각형 길이(픽셀)입니다.")]
    [ShowIf(nameof(m_enableHitMarkerDamageScale))]
    [Min(0.0f)]
    [SerializeField] private float m_hitMarkerLengthAtMaxDamagePixels = 40.0f;

    [EndIf]

    [Tooltip("히트마커 중앙 공간(픽셀)입니다. 중심에서 각 삼각형이 시작되기까지의 빈 간격입니다.")]
    [SerializeField] private float m_hitMarkerCenterGapPixels = 8.0f;

    [FormerlySerializedAs("m_hitMarkerThicknessPixels")]
    [Tooltip("히트마커 각 삼각형의 밑변 길이(픽셀)입니다. 바깥 꼭짓점 반대편, 중심 쪽 두 꼭짓점 사이 변의 길이입니다. 클수록 삼각형이 넓어집니다.")]
    [SerializeField] private float m_hitMarkerBaseLengthPixels = 12.0f;

    [Tooltip("히트마커가 표시된 뒤 사라지기까지 걸리는 페이드아웃 시간(초)입니다.")]
    [SerializeField] private float m_hitMarkerFadeDuration = 0.25f;

    [Tooltip("히트마커의 기준 회전 각도(도)입니다. 화면 정면 기준 Z축 회전이며, 무작위 흔들림을 꺼도 이 각도는 그대로 적용됩니다.")]
    [SerializeField] private float m_hitMarkerRollBaseDegrees = 0.0f;

    [Tooltip("켜면 명중할 때마다 히트마커 회전이 기준 각도에서 무작위로 흔들립니다. 끄면 항상 기준 각도로 고정됩니다.")]
    [SerializeField] private bool m_enableHitMarkerRandomRoll = true;

    [Tooltip("히트마커가 발마다 기준 각도에서 무작위로 흔들리는 범위(도)입니다. 10이면 매 명중마다 기준에서 -10~+10도 사이가 적용됩니다.")]
    [ShowIf(nameof(m_enableHitMarkerRandomRoll))]
    [Min(0.0f)]
    [SerializeField] private float m_hitMarkerRollRandomRangeDegrees = 10.0f;

    [EndIf]

    [Header("Kill Skull")]
    [Tooltip("켜면 적을 처치했을 때 중앙에 해골이 떴다가 페이드아웃됩니다.")]
    [SerializeField] private bool m_showKillSkull = true;

    [Tooltip("킬 시 표시할 해골 텍스처입니다(예: KillStreak.png).")]
    [SerializeField] private Texture2D m_killSkullTexture;

    [Tooltip("해골 색조입니다. 흰색이면 텍스처 원본 색을 그대로 씁니다.")]
    [SerializeField] private Color m_killSkullTint = Color.white;

    [Tooltip("해골 표시 크기(픽셀)입니다.")]
    [SerializeField] private float m_killSkullSizePixels = 48.0f;

    [Tooltip("해골이 완전히 보이는 유지 시간(초)입니다. 이후 페이드아웃이 시작됩니다.")]
    [SerializeField] private float m_killSkullHoldDuration = 0.35f;

    [Tooltip("해골이 사라지기까지 걸리는 페이드아웃 시간(초)입니다.")]
    [SerializeField] private float m_killSkullFadeDuration = 0.6f;

    [Foldout("Block Marker")]
    [Tooltip("켜면 총구와 조준점 사이가 막혔을 때(아군·장애물 길막) 실제 탄착점 화면 위치에 차단 마커를 표시합니다.")]
    [SerializeField] private bool m_showBlockMarker = true;

    [Tooltip("차단 마커 원(링)의 지름(픽셀)입니다.")]
    [SerializeField] private float m_blockMarkerRingSizePixels = 24.0f;

    [Tooltip("차단 마커 원(링)의 선 두께(픽셀)입니다. 0이면 링을 그리지 않습니다.")]
    [SerializeField] private float m_blockMarkerRingThicknessPixels = 2.0f;

    [Tooltip("차단 마커 가운데 점의 지름(픽셀)입니다. 0이면 점을 그리지 않습니다.")]
    [SerializeField] private float m_blockMarkerDotSizePixels = 4.0f;

    [Tooltip("차단 마커 색상입니다. 임시 UI라 링과 가운데 점이 같은 색을 씁니다.")]
    [SerializeField] private Color m_blockMarkerColor = new Color32(226, 59, 59, 255);

    [Tooltip("켜면 차단 마커가 표시되는 동안 중앙 크로스헤어를 반투명하게 만듭니다. 차단 마커 자체와 탄착점 위치 표시는 영향을 받지 않습니다.")]
    [SerializeField] private bool m_dimCrosshairWhileBlockMarker = true;

    [Tooltip("차단 마커가 표시 중일 때의 크로스헤어 알파입니다. 0은 완전히 투명, 1은 기존처럼 불투명입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_blockMarkerCrosshairAlpha = 0.4f;

    [Tooltip("차단 마커 표시·해제에 맞춰 크로스헤어 알파가 전환되는 시간(초)입니다. 0이면 즉시 전환합니다.")]
    [SerializeField] private float m_blockMarkerCrosshairFadeDuration = 0.20f;

    [Foldout("Debug")]
    [Tooltip("(디버그) 켜면 에디트 모드(비플레이)에서도 조준선을 미리 렌더링합니다. 프리뷰 전용이며 게임 로직엔 영향이 없습니다. [ExecuteAlways]와 함께 동작합니다.")]
    [SerializeField] private bool m_editModePreview = false;

    [Tooltip("(디버그) 켜면 에디트 프리뷰에서 히트마커와 킬 해골도 함께 미리 표시합니다(길이·중앙공간·밑변·색 튜닝용). Edit Mode Preview가 켜져 있어야 동작합니다.")]
    [SerializeField] private bool m_previewHitFeedback = false;

    [ShowIf(nameof(m_previewHitFeedback))]
    [Tooltip("(디버그) 히트마커 프리뷰 색을 헤드샷 색으로 표시합니다. 끄면 몸샷 색입니다.")]
    [SerializeField] private bool m_previewHeadshotColor = false;
    [EndIf]

    [Tooltip("(디버그) 마지막 상태 전환 기준 전투 스탠스입니다. Free, Hipfire, Ads 중 현재 디버그 소스가 어느 기준으로 연결되어 있는지 보여줍니다.")]
    [ReadOnly][SerializeField] private string m_debugStance = "Free";

    // (디버그) 값 복사가 아니라 전환 시점에 연결된 라이브 소스 포인터를 통해 현재값을 읽는 읽기전용 게터입니다.
    // 별도 매 프레임 업데이트 없이 인스펙터 리페인트 때마다 포인터로 현재값을 당겨옵니다.
    [Tooltip("(디버그) Gun에서 읽어온 현재 spread degree입니다. 실제 탄의 랜덤 방향이 아니라 발사 cone의 현재 크기(콘 반각=하드캡)입니다.")]
    [ShowInInspector] private float DebugSpreadDegrees => m_debugSpreadSource != null ? m_debugSpreadSource() : 0.0f;

    [Tooltip("(디버그) 콘 반각 대비 표시 배율(tan 공간, 수정 불가)입니다. 현재 표시 기준(basis)과 마지막으로 받은 무기 분포/집중도로 계산되는 중간값이며, 팔 끝이 탄이 실제로 몰리는 반경을 가리키게 합니다.")]
    [ShowInInspector] private float DebugSpreadDisplayFactor => m_lastSpreadDisplayFactor;

    [Tooltip("(디버그) spread degree를 화면 픽셀로 실제 투영할 때 사용하는 현재 카메라 FOV입니다.")]
    [ShowInInspector] private float DebugCameraFovDegrees => m_debugFovSource != null ? m_debugFovSource() : 0.0f;

    [Tooltip("(디버그) Center Space를 제외한 spread 기여분 픽셀입니다. 유효각(spread×배율)을 FOV로 물리 투영한 값입니다.")]
    [ShowInInspector] private float DebugSpreadGapPixels => CalculateSpreadGapPixels(DebugSpreadDegrees, DebugSpreadDisplayFactor, DebugCameraFovDegrees);

    [Tooltip("(디버그) 현재 화면에 적용 중인 최종 gap 픽셀입니다. 탄퍼짐 gap과 발사 반동 UI 펄스가 함께 반영됩니다.")]
    [ShowInInspector] private float DebugCurrentGapPixels => m_currentGapPixels;

    [Tooltip("(디버그) 현재 발사 반동 UI 펄스가 더하고 있는 gap(픽셀)입니다. 실제 탄퍼짐·탄착에는 영향을 주지 않습니다.")]
    [ShowInInspector] private float DebugShotRecoilPulsePixels => m_currentShotRecoilPulsePixels;

    // 전환 시점에 연결되는 라이브 소스 포인터(값 복사 아님). null이면 미연결(0 표시).
    private System.Func<float> m_debugSpreadSource;
    private System.Func<float> m_debugFovSource;

    private VisualElement m_rootElement;
    private VisualElement m_crosshairElement;
    private VisualElement m_centerElement;
    private VisualElement m_topElement;
    private VisualElement m_topStrokeElement;
    private VisualElement m_bottomElement;
    private VisualElement m_bottomStrokeElement;
    private VisualElement m_leftElement;
    private VisualElement m_leftStrokeElement;
    private VisualElement m_rightElement;
    private VisualElement m_rightStrokeElement;
    private VisualElement m_mainStrokeElement;
    private VisualElement m_subShapeElement;
    private VisualElement m_subStrokeElement;
    private VisualElement m_reloadBulletElement;
    private VisualElement m_ammoGaugeElement;
    private VisualElement m_hitMarkerElement;
    private VisualElement m_killSkullElement;
    private VisualElement m_blockMarkerElement;
    private bool m_isReloading;
    private bool m_blockMarkerVisible;
    private float m_crosshairOpacity = 1.0f;
    private float m_ammoGaugeFill = 1.0f;
    private Color m_hitMarkerActiveColor;
    private float m_hitMarkerTimer;

    /// <summary>이번 히트마커에 적용 중인 Z축 회전 각도(도)입니다. 명중마다 새로 뽑습니다.</summary>
    private float m_hitMarkerRollDegrees;

    /// <summary>이번 히트마커에 적용 중인 삼각형 길이(픽셀)입니다. 명중마다 피해량으로 다시 계산합니다.</summary>
    private float m_hitMarkerActiveLengthPixels;
    private float m_killTimer;
    private float m_currentGapPixels;
    private float m_targetSpreadGapPixels;
    private float m_currentShotRecoilPulsePixels;
    private float m_heldShotRecoilPulsePixels;
    private float m_shotRecoilPulseSpreadScale = 1.0f;
    private float m_lastSpreadDegrees;
    private SpreadDistribution m_lastDistribution = SpreadDistribution.Gaussian;
    private float m_lastConcentration = 3.0f;
    private float m_lastSpreadDisplayFactor = 1.0f;
    private float m_lastCameraFovDegrees = 60.0f;

    /// <summary>동시에 진행 중인 발사 반동 UI 펄스입니다.</summary>
    private readonly ImpulseEnvelope m_shotRecoilPulseEnvelope = new ImpulseEnvelope();

    /// <summary>발사 입력 홀드로 펄스 복귀를 막을 마지막 프레임입니다. AimController가 매 프레임 연장합니다.</summary>
    private int m_shotRecoilPulseHoldUntilFrame = -1;

    /// <summary>탄퍼짐 정확도 표시 여부입니다.</summary>
    public bool UseSpreadAccuracy => m_useSpreadAccuracy;

    /// <summary>탄퍼짐 콘을 조준선 간격으로 환산하는 표시 기준입니다.</summary>
    public SpreadDisplayBasis CurrentSpreadDisplayBasis => m_spreadDisplayBasis;

    /// <summary>조준선 최대 간격 상한 사용 여부입니다.</summary>
    public bool ClampToMaxGap => m_clampToMaxGap;

    /// <summary>조준선 최대 간격 상한(픽셀)입니다.</summary>
    public float MaxGapPixels => Mathf.Max(0.0f, m_maxGapPixels);

    /// <summary>조준선 간격이 목표를 따라가는 보간 속도입니다.</summary>
    public float SpreadLerpSpeed => Mathf.Max(0.0f, m_lerpSpeed);

    /// <summary>발사 반동 UI 펄스 사용 여부입니다.</summary>
    public bool ShotRecoilPulseEnabled => m_enableShotRecoilPulse;

    /// <summary>발사 한 발의 크로스헤어 벌어짐 최대값(픽셀)입니다.</summary>
    public float ShotRecoilPulseAmplitudePixels => Mathf.Max(0.0f, m_shotRecoilPulseAmplitudePixels);

    /// <summary>발사 반동 UI 펄스 한 번의 전체 길이(초)입니다.</summary>
    public float ShotRecoilPulseDuration => Mathf.Max(0.0f, m_shotRecoilPulseDuration);

    private bool HasMainShape => m_mainShape != MainShape.None;
    private bool HasSubShape => m_subShape != SubShape.None;
    private bool HasSubCrossShape => m_subShape == SubShape.RoundedCross || m_subShape == SubShape.SquareCross;

    /// <summary>
    /// Unity 생명주기 초기화 함수입니다.
    /// 조준선 UXML을 표시할 <see cref="UIDocument"/> 참조를 캐싱합니다.
    /// </summary>
    private void Awake()
    {
        CacheDocument();
    }

    /// <summary>
    /// 매 프레임 재장전 깜빡임과 히트마커·킬 해골 페이드아웃, 차단 중 크로스헤어 알파를 갱신합니다(플레이 중에만).
    /// </summary>
    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        UpdateReloadBlink();
        UpdateHitMarkerFade();
        UpdateKillFade();
        UpdateShotRecoilPulse();
        UpdateGapSmoothing();
        UpdateBlockMarkerCrosshairOpacity();
    }

    /// <summary>
    /// 발사 반동 UI 펄스를 진행하고 현재 크로스헤어 간격에 합칩니다.
    /// </summary>
    /// <remarks>
    /// 탄퍼짐 갱신과 분리해 한 발씩 즉시 피드백을 줄 수 있게 합니다. 따라서 ADS 정밀탄처럼 실제 spread가 0인
    /// 첫 발에도 크로스헤어가 움직입니다. 발사 입력을 유지하면 펄스가 도달한 가장 큰 gap을 붙잡고, 버튼을
    /// 놓은 뒤에만 일반 복귀를 재개합니다.
    /// </remarks>
    private void UpdateShotRecoilPulse()
    {
        if (!m_enableShotRecoilPulse)
        {
            if (m_currentShotRecoilPulsePixels <= 0.0f)
            {
                return;
            }

            m_shotRecoilPulseEnvelope.Clear();
            m_currentShotRecoilPulsePixels = 0.0f;
            m_heldShotRecoilPulsePixels = 0.0f;
            ApplyCurrentGapLayout();
            return;
        }

        float nextPulse = m_shotRecoilPulseEnvelope.Evaluate(
            m_shotRecoilPulseDuration,
            m_shotRecoilPulseCurve,
            Time.deltaTime);

        bool fireInputHeld = Time.frameCount <= m_shotRecoilPulseHoldUntilFrame;
        if (fireInputHeld)
        {
            // 공격 구간은 기존 envelope가 진행시켜 빠르게 올리고, 이미 도달한 최고값은 홀드 중에
            // 다시 내려가지 않게 붙잡습니다. 여러 발의 펄스가 합쳐져 초기 연사에서 과도하게 커지지 않도록
            // 한 발의 표시 진폭으로 제한하며, 실제 spread 누적은 별도 gap이 계속 담당합니다.
            m_heldShotRecoilPulsePixels = Mathf.Min(
                ShotRecoilPulseAmplitudePixels,
                Mathf.Max(m_heldShotRecoilPulsePixels, nextPulse));
            nextPulse = m_heldShotRecoilPulsePixels;
        }
        else if (m_heldShotRecoilPulsePixels > 0.0f)
        {
            // 클릭/버튼 해제 뒤에는 기존 펄스 길이에 맞춘 일정 속도로 홀드분을 돌려 보냅니다.
            float releaseSpeed = ShotRecoilPulseAmplitudePixels /
                                 Mathf.Max(0.0001f, ShotRecoilPulseDuration);
            m_heldShotRecoilPulsePixels = Mathf.MoveTowards(
                m_heldShotRecoilPulsePixels,
                0.0f,
                releaseSpeed * Time.deltaTime);
            nextPulse = Mathf.Max(nextPulse, m_heldShotRecoilPulsePixels);
        }

        if (Mathf.Abs(nextPulse - m_currentShotRecoilPulsePixels) <= GapSnapEpsilon &&
            Mathf.Approximately(nextPulse, 0.0f))
        {
            return;
        }

        m_currentShotRecoilPulsePixels = Mathf.Max(0.0f, nextPulse);
        ApplyCurrentGapLayout();
    }

    /// <summary>
    /// 차단 마커 표시 상태에 맞춰 중앙 크로스헤어의 알파를 선형 보간합니다.
    /// </summary>
    /// <remarks>
    /// 차단 마커는 <see cref="m_rootElement"/>의 직속 요소이고, 중앙 크로스헤어는
    /// <see cref="m_crosshairElement"/> 아래에 있습니다. 따라서 이 요소에만 opacity를 적용하면
    /// 실제 탄착점의 차단 마커는 선명하게 유지하면서 크로스헤어 색상의 알파만 함께 전환할 수 있습니다.
    /// </remarks>
    private void UpdateBlockMarkerCrosshairOpacity()
    {
        if (m_crosshairElement == null && !CacheVisualElements())
        {
            return;
        }

        float targetOpacity = m_dimCrosshairWhileBlockMarker && m_blockMarkerVisible
            ? m_blockMarkerCrosshairAlpha
            : 1.0f;

        if (m_blockMarkerCrosshairFadeDuration <= 0.0f)
        {
            m_crosshairOpacity = targetOpacity;
        }
        else
        {
            float step = Time.deltaTime / m_blockMarkerCrosshairFadeDuration;
            m_crosshairOpacity = Mathf.MoveTowards(m_crosshairOpacity, targetOpacity, step);
        }

        m_crosshairElement.style.opacity = m_crosshairOpacity;
    }

    /// <summary>
    /// 재장전 중 탄약 아이콘을 페이드로 깜빡입니다(재장전 스왑 활성 시에만).
    /// </summary>
    private void UpdateReloadBlink()
    {
        if (m_reloadBulletElement == null || !m_isReloading || !m_swapCrosshairOnReload || m_reloadBulletImage == null)
        {
            return;
        }

        float alpha = 1.0f;
        if (m_reloadBlinkSpeed > 0.0f)
        {
            float t = (Mathf.Sin(Time.time * m_reloadBlinkSpeed) + 1.0f) * 0.5f; // 0..1
            alpha = Mathf.Lerp(Mathf.Clamp01(m_reloadBlinkMinAlpha), 1.0f, t);
        }

        m_reloadBulletElement.style.opacity = alpha;
    }

    /// <summary>
    /// 표시된 히트마커를 남은 시간에 비례해 페이드아웃하고, 다 사라지면 숨깁니다.
    /// </summary>
    private void UpdateHitMarkerFade()
    {
        if (m_hitMarkerElement == null || m_hitMarkerTimer <= 0.0f)
        {
            return;
        }

        m_hitMarkerTimer = Mathf.Max(0.0f, m_hitMarkerTimer - Time.deltaTime);
        float duration = Mathf.Max(0.0001f, m_hitMarkerFadeDuration);
        float alpha = Mathf.Clamp01(m_hitMarkerTimer / duration);
        m_hitMarkerElement.style.opacity = alpha;

        if (m_hitMarkerTimer <= 0.0f)
        {
            HideElement(m_hitMarkerElement);
        }
    }

    /// <summary>
    /// 킬 해골을 유지 시간 동안 완전히 보인 뒤 페이드아웃하고, 다 사라지면 숨깁니다.
    /// </summary>
    private void UpdateKillFade()
    {
        if (m_killSkullElement == null || m_killTimer <= 0.0f)
        {
            return;
        }

        m_killTimer = Mathf.Max(0.0f, m_killTimer - Time.deltaTime);
        float fade = Mathf.Max(0.0001f, m_killSkullFadeDuration);
        // 유지 구간에서는 불투명도 1, 페이드 구간에서만 0으로 선형 감소합니다.
        float alpha = Mathf.Clamp01(m_killTimer / fade);
        m_killSkullElement.style.opacity = alpha;

        if (m_killTimer <= 0.0f)
        {
            HideElement(m_killSkullElement);
        }
    }

    /// <summary>
    /// 컴포넌트 활성화 시 VisualElement 트리를 캐싱하고 조준선을 표시한 뒤 마지막 벌어짐 상태를 즉시 반영합니다.
    /// </summary>
    private void OnEnable()
    {
        // 에디트 모드에서는 디버그 프리뷰 토글이 켜졌을 때만 조준선을 그립니다(플레이 중에는 항상 그립니다).
        if (!Application.isPlaying && !m_editModePreview)
        {
            return;
        }

        CacheVisualElements();
        SetVisible(true);
        SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
    }

    /// <summary>
    /// Inspector 값 변경 시 설정값을 유효 범위로 보정하고, 플레이 중이면 조준선 레이아웃을 즉시 다시 반영합니다.
    /// </summary>
    private void OnValidate()
    {
        ClampSettings();

        if (Application.isPlaying)
        {
            CacheVisualElements();
            SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
            return;
        }

        // 에디트 모드: 디버그 프리뷰 토글 상태에 맞춰 즉시 표시/숨김을 반영합니다.
        if (m_editModePreview)
        {
            CacheVisualElements();
            SetVisible(true);
            SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
        }
        else
        {
            SetVisible(false);
        }
    }

    /// <summary>조준선을 마지막으로 요청받은 표시 상태입니다.</summary>
    private bool m_requestedVisible = true;

    /// <summary>전체화면 UI 등이 조준선을 강제로 숨기고 있는지 여부입니다.</summary>
    private bool m_isSuppressed;

    /// <summary>
    /// 조준선 표시 여부를 설정합니다.
    /// </summary>
    /// <param name="visible">표시하려면 <c>true</c>, 숨기려면 <c>false</c>입니다.</param>
    /// <remarks>
    /// 억제 중이면 요청만 기록하고 화면에는 반영하지 않습니다.
    /// <see cref="AimController"/>가 매 프레임 조준 상태로 이 값을 다시 지정하므로,
    /// 바깥에서 숨겨도 다음 프레임에 되살아나기 때문입니다.
    /// </remarks>
    public void SetVisible(bool visible)
    {
        m_requestedVisible = visible;
        ApplyRootVisibility();
    }

    /// <summary>
    /// 조준선을 강제로 숨길지 여부를 설정합니다.
    /// </summary>
    /// <param name="suppressed">숨기려면 <c>true</c>입니다.</param>
    /// <remarks>
    /// 결과 화면처럼 화면을 덮는 UI가 떴을 때 인게임 표시를 내리는 용도입니다.
    /// 억제를 풀면 마지막으로 요청받은 표시 상태로 돌아갑니다.
    /// 표시 요청과 억제를 따로 들고 있어야, 억제 중에 들어온 요청이 유실되지 않습니다.
    /// </remarks>
    public void SetSuppressed(bool suppressed)
    {
        if (m_isSuppressed == suppressed)
        {
            return;
        }

        m_isSuppressed = suppressed;
        ApplyRootVisibility();
    }

    /// <summary>표시 요청과 억제 상태를 합쳐 루트 요소에 반영합니다.</summary>
    private void ApplyRootVisibility()
    {
        if (!CacheVisualElements())
        {
            return;
        }

        bool visible = m_requestedVisible && !m_isSuppressed;
        m_rootElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>
    /// 현재 무기 탄퍼짐 방사각·분포·집중도·카메라 FOV를 받아 조준선 벌어짐을 갱신합니다.
    /// </summary>
    /// <param name="spreadDegrees">현재 무기 탄퍼짐 방사각(도, 콘 반각=하드캡)입니다.</param>
    /// <param name="distribution">콘 안에서의 탄 분포(무기 소유)입니다. 표시 배율 계산에만 씁니다.</param>
    /// <param name="concentration">Gaussian 중심 집중도(σ=1/이 값, 무기 소유)입니다. 표시 배율 계산에만 씁니다.</param>
    /// <param name="cameraFovDegrees">현재 조준 카메라 세로 FOV(도)입니다.</param>
    /// <param name="snap">true면 보간 없이 즉시 반영합니다.</param>
    /// <remarks>
    /// 표시 기준(<see cref="m_spreadDisplayBasis"/>)과 분포·집중도로 배율(factor)을 계산하고, 유효각(= spread를 tan 공간에서 factor배)을
    /// FOV로 실제 화면 투영하므로 팔 벌어짐이 화면상 탄착 분포와 1:1이 됩니다. 분포·집중도는 읽기만 하며 실제 탄 궤적은 무기가 결정합니다.
    /// </remarks>
    public void SetSpread(float spreadDegrees, SpreadDistribution distribution, float concentration, float cameraFovDegrees, bool snap)
    {
        SetSpreadInternal(spreadDegrees, distribution, concentration, cameraFovDegrees, snap);
    }

    private void SetSpreadInternal(float spreadDegrees, SpreadDistribution distribution, float concentration, float cameraFovDegrees, bool snap)
    {
        if (!CacheVisualElements())
        {
            return;
        }

        ClampSettings();

        m_lastSpreadDegrees = Mathf.Max(0.0f, spreadDegrees);
        m_lastDistribution = distribution;
        m_lastConcentration = Mathf.Max(1.0f, concentration);
        m_lastSpreadDisplayFactor = CalculateDisplayFactor(m_lastDistribution, m_lastConcentration);
        m_lastCameraFovDegrees = Mathf.Max(1.0f, cameraFovDegrees);

        // 여기서는 탄퍼짐 기여분의 **목표만** 갱신합니다. 실제 접근은 프레임당 한 번
        // <see cref="UpdateGapSmoothing"/>가 최종 gap 전체에 대해 수행합니다. 예전에는 이 자리에서
        // 탄퍼짐 기여분만 보간했는데, 그러면 매 발 계단식으로 바뀌는 펄스 기여분은 보간을 거치지 않아
        // 두 성분의 변화 속도가 어긋나고 연사 중 벌어짐이 발마다 덜컥거렸습니다.
        m_targetSpreadGapPixels = CalculateSpreadGapPixels(
            m_lastSpreadDegrees, m_lastSpreadDisplayFactor, m_lastCameraFovDegrees);

        if (snap)
        {
            m_currentGapPixels = ComposeTargetGapPixels();
        }

        ApplyCurrentGapLayout();
    }

    /// <summary>
    /// 현재 상태가 가리키는 최종 gap 목표(중앙 간격 + 탄퍼짐 기여 + 비율 적용 펄스)를 계산합니다.
    /// </summary>
    private float ComposeTargetGapPixels()
    {
        float pulseGap = m_currentShotRecoilPulsePixels * Mathf.Clamp01(m_shotRecoilPulseSpreadScale);
        float total = Mathf.Max(0.0f, m_centerSpacePixels + m_targetSpreadGapPixels + pulseGap);
        return m_clampToMaxGap ? Mathf.Min(total, m_maxGapPixels) : total;
    }

    /// <summary>
    /// 최종 gap을 목표로 프레임당 한 번 접근시키고 레이아웃에 반영합니다.
    /// </summary>
    /// <remarks>
    /// 탄퍼짐 기여분과 펄스 기여분을 **합친 뒤** 보간하므로 두 성분의 계단이 같은 곡선으로 흡수됩니다.
    /// 그래서 연사 중 성분별 속도 차이로 생기던 덜컥거림이 사라지고, 첫 발의 펄스 상승(32ms 피크)도
    /// 이 보간을 한 번 더 거쳐 스냅이 아닌 상승으로 보입니다.
    /// <para>
    /// 계수는 <c>1 - e^(-speed·dt)</c>입니다. 프레임레이트에 무관하게 같은 시정수를 갖고(예전 <c>dt·speed</c>는
    /// 30fps에서 60fps보다 약 20% 빨리 붙었습니다), 초반이 가장 빠르고 뒤로 갈수록 느려지는 앞쏠림 곡선입니다.
    /// </para>
    /// 목표 자체가 단조증가하는 계단이면 이 보간의 출력도 단조증가합니다. 즉 벌어짐이 도중에 줄지 않습니다.
    /// </remarks>
    private void UpdateGapSmoothing()
    {
        float target = ComposeTargetGapPixels();
        float speed = SpreadLerpSpeed;

        m_currentGapPixels = speed <= 0.0f
            ? target
            : Mathf.Lerp(m_currentGapPixels, target, 1.0f - Mathf.Exp(-speed * Time.deltaTime));

        if (Mathf.Abs(m_currentGapPixels - target) <= GapSnapEpsilon)
        {
            m_currentGapPixels = target;
        }

        ApplyLayout(m_currentGapPixels);
    }

    /// <summary>
    /// 실제 발사가 성사된 프레임에 크로스헤어 벌어짐 펄스를 시작합니다.
    /// </summary>
    /// <remarks>
    /// 탄퍼짐 표시는 여전히 <see cref="SetSpread"/>가 소유합니다. 이 메서드는 표시용 gap만 더하므로,
    /// Gun의 spread·탄착·에임 반동에는 영향을 주지 않습니다.
    /// </remarks>
    public void TriggerShotRecoilPulse()
    {
        ClampSettings();

        if (!m_enableShotRecoilPulse || m_shotRecoilPulseAmplitudePixels <= 0.0f ||
            m_shotRecoilPulseDuration <= 0.0f)
        {
            return;
        }

        m_shotRecoilPulseEnvelope.Add(m_shotRecoilPulseAmplitudePixels);
    }

    /// <summary>
    /// 진행 중인 발사 반동 UI 펄스를 즉시 제거하고 현재 탄퍼짐 간격만 남깁니다.
    /// </summary>
    /// <remarks>
    /// 진행 중인 펄스를 한 프레임에 버리므로 총 gap이 그만큼 즉시 줄어듭니다. 따라서 사격 중 상한 처리에는
    /// 쓰지 않습니다. 그 용도는 <see cref="SetShotRecoilPulseSpreadScale"/>의 비례 감쇠가 대신합니다.
    /// 이 메서드는 무기 교체·전투 종료처럼 gap이 끊겨도 되는 하드 리셋 지점을 위해 남겨 둡니다.
    /// 실제 탄퍼짐·탄착·에임 반동에는 영향을 주지 않습니다.
    /// </remarks>
    public void ClearShotRecoilPulse()
    {
        if (m_shotRecoilPulseEnvelope.ActiveCount == 0 &&
            Mathf.Approximately(m_currentShotRecoilPulsePixels, 0.0f) &&
            Mathf.Approximately(m_heldShotRecoilPulsePixels, 0.0f))
        {
            return;
        }

        m_shotRecoilPulseEnvelope.Clear();
        m_currentShotRecoilPulsePixels = 0.0f;
        m_heldShotRecoilPulsePixels = 0.0f;

        // 씬 초기화 순서상 발사 처리가 UI Toolkit 트리보다 먼저 올 수 있습니다.
        // 그때는 상태만 비우고, 다음 SetSpread/Update가 준비된 레이아웃에 반영하게 둡니다.
        if (CacheVisualElements())
        {
            ApplyCurrentGapLayout();
        }
    }

    /// <summary>
    /// 현재 gap 값을 화면 레이아웃에 반영합니다.
    /// </summary>
    /// <remarks>
    /// 목표 계산은 <see cref="ComposeTargetGapPixels"/>, 목표 접근은 <see cref="UpdateGapSmoothing"/>이
    /// 프레임당 한 번 담당합니다. 이 메서드는 다시 그리기만 하므로 한 프레임에 여러 번 불려도 안전합니다.
    /// </remarks>
    private void ApplyCurrentGapLayout()
    {
        // 플레이 중 보간이 켜져 있으면 목표 접근은 UpdateGapSmoothing이 프레임당 한 번만 담당합니다.
        // 이 메서드는 한 프레임에 여러 번 불릴 수 있어, 여기서 보간을 진행하면 호출 횟수만큼 빨라집니다.
        // 에디터 미리보기(Update 미실행)와 보간 off에서는 즉시 목표로 맞춥니다.
        if (!Application.isPlaying || SpreadLerpSpeed <= 0.0f)
        {
            m_currentGapPixels = ComposeTargetGapPixels();
        }

        ApplyLayout(m_currentGapPixels);
    }

    /// <summary>
    /// 발사 입력 홀드에 따라 발당 UI 펄스의 복귀를 막을지 통지합니다.
    /// </summary>
    /// <remarks>
    /// 짧은 클릭은 버튼을 놓으면 기존 복귀 경로를 그대로 쓰고, 자동 사격 홀드는 초기 정밀탄의 UI 펄스까지
    /// 최고 gap에 유지합니다. 한 프레임 여유는 CrosshairController와 AimController의 Update 순서 차이를 흡수합니다.
    /// </remarks>
    /// <param name="fireInputHeld">발사 입력을 계속 누르고 있으면 <c>true</c>입니다.</param>
    public void SetShotRecoilPulseHoldByFireInput(bool fireInputHeld)
    {
        m_shotRecoilPulseHoldUntilFrame = fireInputHeld ? Time.frameCount + 1 : -1;
    }

    /// <summary>
    /// 발당 UI 펄스가 최종 gap에 실릴 비율(0~1)을 통지합니다.
    /// </summary>
    /// <param name="scale01">1이면 펄스를 그대로 싣고, 0이면 펄스 기여를 없앱니다. 범위 밖 값은 Clamp01됩니다.</param>
    /// <remarks>
    /// 탄퍼짐이 상한에 도달한 뒤에도 발당 펄스를 계속 합치면 최대 벌어짐 상태에서 크로스헤어가 들썩입니다.
    /// 예전에는 그 지점에서 <see cref="ClearShotRecoilPulse"/>로 펄스를 한 프레임에 버렸는데, 마지막 탄퍼짐
    /// 증가폭이 펄스 진폭보다 작으면 그 순간 총 gap이 오히려 줄어들어 벌어짐이 단조증가하지 않았습니다.
    /// 이 비율을 탄퍼짐 진행도에 맞춰 낮추면 상한에서 기여가 정확히 0이 되므로, 들썩임은 그대로 막으면서
    /// 단조성을 지킬 수 있습니다. 비율은 무기 상한을 아는 <see cref="AimController"/>가 매 프레임 통지합니다.
    /// 표시용 gap만 바꾸므로 Gun의 탄퍼짐·탄착·에임 반동에는 영향을 주지 않습니다.
    /// </remarks>
    public void SetShotRecoilPulseSpreadScale(float scale01)
    {
        float clamped = Mathf.Clamp01(scale01);
        if (Mathf.Approximately(clamped, m_shotRecoilPulseSpreadScale))
        {
            return;
        }

        m_shotRecoilPulseSpreadScale = clamped;

        // 씬 초기화 순서상 이 통지가 UI Toolkit 트리보다 먼저 올 수 있습니다.
        // 그때는 비율만 담아 두고, 다음 SetSpread/Update가 준비된 레이아웃에 반영하게 둡니다.
        if (CacheVisualElements())
        {
            ApplyCurrentGapLayout();
        }
    }

    /// <summary>
    /// 무기의 분포·집중도와 현재 표시 기준(<see cref="m_spreadDisplayBasis"/>)으로 표시 배율(콘 반각 대비, tan 공간)을 계산합니다.
    /// </summary>
    /// <remarks>
    /// ApplySpread의 편향 = 단위오프셋(크기 |·|∈[0,1]) × tan(spread)이라, 이 배율을 tan(spread)에 곱하면 선택한 기준의 반경이 됩니다.
    /// 고정 계수의 출처:
    /// - Gaussian: Gun.SampleGaussianUnitOffset이 σ = 1/concentration으로 샘플링(단위원 밖은 경계로 클램프).
    ///   Rayleigh(σ) 기준 RMS = √2·σ = √2/concentration, 2σ(≈86% 포함) = 2/concentration.
    ///   ※ 이 공식은 클램프를 무시한 해석적 근사입니다. concentration이 낮아 클램프가 자주 걸리면 실제 분포와 벌어집니다(기본 3에서는 클램프 ~1%로 정확).
    ///     Gun.SampleGaussianUnitOffset의 σ 정의를 바꾸면 이 계수도 함께 갱신해야 합니다.
    /// - Uniform: 반경 1 원판(pdf 2r) 기준 RMS = 1/√2 ≈ 0.707, 86% 포함 반경 = √0.86 ≈ 0.927. (클램프·concentration과 무관하게 정확)
    /// - Core: Gaussian 최빈 반경(mode) = σ = 1/concentration(가장 타이트). Uniform은 밀집 코어가 없어 0.5로 둡니다.
    /// - ConeEdge: 하드캡(단위오프셋 최대 = 1)이라 항상 1.
    /// 마지막에 [0,1]로 클램프해 콘 경계를 넘지 않게 합니다.
    /// </remarks>
    private float CalculateDisplayFactor(SpreadDistribution distribution, float concentration)
    {
        float c = Mathf.Max(1.0f, concentration);

        float factor = distribution == SpreadDistribution.Uniform
            ? m_spreadDisplayBasis switch
            {
                SpreadDisplayBasis.Core => 0.5f,               // Uniform은 밀집 코어가 없어 임의의 타이트값
                SpreadDisplayBasis.Typical => 0.70710678f,     // RMS = 1/√2
                SpreadDisplayBasis.MostShots => 0.92736185f,   // ≈ √0.86 (86% 포함)
                _ => 1.0f,                                     // ConeEdge
            }
            : m_spreadDisplayBasis switch                      // Gaussian
            {
                SpreadDisplayBasis.Core => 1.0f / c,                  // 최빈 반경(mode) = σ = 1/concentration
                SpreadDisplayBasis.Typical => Mathf.Sqrt(2.0f) / c,   // RMS = √2·σ (σ = 1/concentration)
                SpreadDisplayBasis.MostShots => 2.0f / c,             // 2σ ≈ 86%
                _ => 1.0f,                                            // ConeEdge (하드캡)
            };

        return Mathf.Clamp01(factor);
    }

    /// <summary>
    /// 전투 스탠스(자유시점/힙파이어/ADS) 전환 시점에 디버그 라이브 소스 포인터를 연결합니다.
    /// </summary>
    /// <param name="stance">스탠스 이름(디버그 표시용)입니다.</param>
    /// <param name="spreadSource">현재 방사각(도)을 반환하는 라이브 소스입니다. 값을 복사하지 않고 이 포인터로 읽습니다.</param>
    /// <param name="fovSource">해당 스탠스에서 사용하는 카메라 FOV(도)를 반환하는 라이브 소스입니다.</param>
    /// <remarks>
    /// 값 복사(스냅샷)가 아니라 소스 포인터를 연결만 합니다. 표시 배율(factor)은 별도 소스가 아니라 마지막으로 받은 분포/집중도와
    /// 현재 표시 기준으로 크로스헤어가 직접 계산합니다(<see cref="DebugSpreadDisplayFactor"/>). 전투 중에는 매 프레임 SetSpread가 갱신합니다.
    /// </remarks>
    public void BindSpreadDebug(string stance, System.Func<float> spreadSource, System.Func<float> fovSource)
    {
        m_debugStance = stance;
        m_debugSpreadSource = spreadSource;
        m_debugFovSource = fovSource;
    }

    /// <summary>
    /// 탄퍼짐 정확도 표시 토글을 런타임에 바꿉니다.
    /// </summary>
    /// <param name="enabled">탄퍼짐 벌어짐을 표시하려면 <c>true</c>, Center Space만 유지하려면 <c>false</c>입니다.</param>
    public void SetSpreadAccuracyEnabled(bool enabled)
    {
        m_useSpreadAccuracy = enabled;
        SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
    }

    /// <summary>탄퍼짐 콘을 조준선 간격으로 환산하는 표시 기준을 설정합니다.</summary>
    /// <param name="value">새 표시 기준입니다. 실제 탄 궤적은 변경하지 않습니다.</param>
    public void SetSpreadDisplayBasis(SpreadDisplayBasis value)
    {
        m_spreadDisplayBasis = value;
        SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
    }

    /// <summary>조준선 최대 간격 상한 사용 여부를 설정합니다.</summary>
    /// <param name="value">상한을 적용하려면 <c>true</c>입니다.</param>
    public void SetClampToMaxGap(bool value)
    {
        m_clampToMaxGap = value;
        SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
    }

    /// <summary>조준선 최대 간격 상한(픽셀)을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetMaxGapPixels(float value)
    {
        m_maxGapPixels = Mathf.Max(0.0f, value);
        SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
    }

    /// <summary>조준선 간격 보간 속도를 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetSpreadLerpSpeed(float value) => m_lerpSpeed = Mathf.Max(0.0f, value);

    /// <summary>발사 반동 UI 펄스 사용 여부를 설정합니다.</summary>
    /// <param name="enabled">실제 발사마다 UI 펄스를 보이려면 <c>true</c>입니다.</param>
    public void SetShotRecoilPulseEnabled(bool enabled)
    {
        m_enableShotRecoilPulse = enabled;
        if (!enabled)
        {
            m_shotRecoilPulseEnvelope.Clear();
            m_currentShotRecoilPulsePixels = 0.0f;
            m_heldShotRecoilPulsePixels = 0.0f;
        }

        RefreshRuntimeLayout();
    }

    /// <summary>발사 한 발의 크로스헤어 벌어짐 최대값을 설정합니다.</summary>
    /// <param name="value">새 최대 벌어짐(픽셀)입니다. 음수는 0으로 보정합니다.</param>
    public void SetShotRecoilPulseAmplitudePixels(float value)
    {
        m_shotRecoilPulseAmplitudePixels = Mathf.Max(0.0f, value);
    }

    /// <summary>발사 반동 UI 펄스의 전체 길이를 설정합니다.</summary>
    /// <param name="value">새 길이(초)입니다. 음수는 0으로 보정합니다.</param>
    public void SetShotRecoilPulseDuration(float value)
    {
        m_shotRecoilPulseDuration = Mathf.Max(0.0f, value);
    }

    /// <summary>발사 반동 UI 펄스 곡선을 교체합니다.</summary>
    /// <param name="value">새 곡선입니다. null이면 기본 삼각 펄스로 처리합니다.</param>
    public void SetShotRecoilPulseCurve(AnimationCurve value)
    {
        m_shotRecoilPulseCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    // ─────────────────────────────────────────────────────────────
    // 런타임 조절용 접근자
    //
    // 트레이너가 값을 바꾼 뒤 화면에 바로 반영되도록 재빌드를 함께 수행합니다.
    // 인스펙터에서 고칠 때와 같은 경로를 쓰기 위해 ClampSettings와 SetSpreadInternal을 거칩니다.
    // ─────────────────────────────────────────────────────────────

    // 모양 ─────────────────────────────────────────────────────

    /// <summary>중앙 표시의 형태입니다.</summary>
    public MainShape CurrentMainShape
    {
        get => m_mainShape;
        set { m_mainShape = value; RefreshRuntimeLayout(); }
    }

    /// <summary>보조 표시(상하좌우 팔)의 형태입니다.</summary>
    public SubShape CurrentSubShape
    {
        get => m_subShape;
        set { m_subShape = value; RefreshRuntimeLayout(); }
    }

    /// <summary>중앙 링의 지름(픽셀)입니다.</summary>
    public float MainRingSizePixels
    {
        get => m_mainRingSizePixels;
        set { m_mainRingSizePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>중앙 링의 두께(픽셀)입니다.</summary>
    public float MainRingThicknessPixels
    {
        get => m_mainRingThicknessPixels;
        set { m_mainRingThicknessPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>중앙 표시의 색입니다.</summary>
    public Color MainColor
    {
        get => m_mainColor;
        set { m_mainColor = value; RefreshRuntimeLayout(); }
    }

    /// <summary>중앙 표시 테두리의 색입니다.</summary>
    public Color MainStrokeColor
    {
        get => m_mainStrokeColor;
        set { m_mainStrokeColor = value; RefreshRuntimeLayout(); }
    }

    /// <summary>보조 팔의 길이(픽셀)입니다.</summary>
    public float SubSizePixels
    {
        get => m_subSizePixels;
        set { m_subSizePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>보조 팔의 폭(픽셀)입니다.</summary>
    public float SubWidthPixels
    {
        get => m_subWidthPixels;
        set { m_subWidthPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>보조 팔의 두께(픽셀)입니다.</summary>
    public float SubThicknessPixels
    {
        get => m_subThicknessPixels;
        set { m_subThicknessPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>보조 링의 지름(픽셀)입니다.</summary>
    public float SubRingSizePixels
    {
        get => m_subRingSizePixels;
        set { m_subRingSizePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>보조 링의 두께(픽셀)입니다.</summary>
    public float SubRingThicknessPixels
    {
        get => m_subRingThicknessPixels;
        set { m_subRingThicknessPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>보조 표시의 색입니다.</summary>
    public Color SubColor
    {
        get => m_subColor;
        set { m_subColor = value; RefreshRuntimeLayout(); }
    }

    /// <summary>보조 표시 테두리의 두께(픽셀)입니다.</summary>
    public float SubStrokeThicknessPixels
    {
        get => m_subStrokeThicknessPixels;
        set { m_subStrokeThicknessPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>보조 표시 테두리의 색입니다.</summary>
    public Color SubStrokeColor
    {
        get => m_subStrokeColor;
        set { m_subStrokeColor = value; RefreshRuntimeLayout(); }
    }

    /// <summary>모서리를 둥글게 하는 반지름(픽셀)입니다.</summary>
    public float CornerRadiusPixels
    {
        get => m_cornerRadiusPixels;
        set { m_cornerRadiusPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    // 재장전 ───────────────────────────────────────────────────

    /// <summary>재장전 중 조준선을 탄약 아이콘으로 바꿀지 여부입니다.</summary>
    public bool SwapCrosshairOnReload
    {
        get => m_swapCrosshairOnReload;
        set { m_swapCrosshairOnReload = value; RefreshRuntimeLayout(); }
    }

    /// <summary>재장전 탄약 아이콘의 크기(픽셀)입니다.</summary>
    public float ReloadBulletSizePixels
    {
        get => m_reloadBulletSizePixels;
        set { m_reloadBulletSizePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>재장전 아이콘 깜빡임 속도입니다.</summary>
    public float ReloadBlinkSpeed
    {
        get => m_reloadBlinkSpeed;
        set { m_reloadBlinkSpeed = Mathf.Max(0.0f, value); }
    }

    /// <summary>재장전 아이콘이 가장 흐려질 때의 투명도입니다.</summary>
    public float ReloadBlinkMinAlpha
    {
        get => m_reloadBlinkMinAlpha;
        set { m_reloadBlinkMinAlpha = Mathf.Clamp01(value); }
    }

    // 탄약 게이지 ───────────────────────────────────────────────

    /// <summary>탄약 게이지를 표시할지 여부입니다.</summary>
    public bool ShowAmmoGauge
    {
        get => m_showAmmoGauge;
        set { m_showAmmoGauge = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지를 항상 보이게 할지 여부입니다.</summary>
    public bool AmmoGaugeAlwaysVisible
    {
        get => m_ammoGaugeAlwaysVisible;
        set { m_ammoGaugeAlwaysVisible = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지의 크기(픽셀)입니다.</summary>
    public float AmmoGaugeSizePixels
    {
        get => m_ammoGaugeSizePixels;
        set { m_ammoGaugeSizePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지의 두께(픽셀)입니다.</summary>
    public float AmmoGaugeThicknessPixels
    {
        get => m_ammoGaugeThicknessPixels;
        set { m_ammoGaugeThicknessPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지가 차지하는 각도(도)입니다.</summary>
    public float AmmoGaugeSweepDegrees
    {
        get => m_ammoGaugeSweepDegrees;
        set { m_ammoGaugeSweepDegrees = Mathf.Clamp(value, 0.0f, 360.0f); RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지가 시작하는 각도(도)입니다.</summary>
    public float AmmoGaugeStartAngleDegrees
    {
        get => m_ammoGaugeStartAngleDegrees;
        set { m_ammoGaugeStartAngleDegrees = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지가 중앙에서 대각선으로 떨어진 거리(픽셀)입니다.</summary>
    public float AmmoGaugeDiagonalOffset
    {
        get => m_ammoGaugeDiagonalOffset;
        set { m_ammoGaugeDiagonalOffset = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지를 채우는 방향입니다.</summary>
    public AmmoGaugeFillDirection CurrentAmmoGaugeFillDirection
    {
        get => m_ammoGaugeFillDirection;
        set { m_ammoGaugeFillDirection = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지의 색입니다.</summary>
    public Color AmmoGaugeColor
    {
        get => m_ammoGaugeColor;
        set { m_ammoGaugeColor = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약이 적을 때 게이지의 색입니다.</summary>
    public Color LowAmmoGaugeColor
    {
        get => m_lowAmmoGaugeColor;
        set { m_lowAmmoGaugeColor = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지 배경을 표시할지 여부입니다.</summary>
    public bool ShowAmmoGaugeBackground
    {
        get => m_showAmmoGaugeBackground;
        set { m_showAmmoGaugeBackground = value; RefreshRuntimeLayout(); }
    }

    /// <summary>탄약 게이지 배경의 투명도입니다.</summary>
    public float AmmoGaugeBackgroundAlpha
    {
        get => m_ammoGaugeBackgroundAlpha;
        set { m_ammoGaugeBackgroundAlpha = Mathf.Clamp01(value); RefreshRuntimeLayout(); }
    }

    // 적중 표시 ─────────────────────────────────────────────────

    /// <summary>히트마커 밑변의 길이(픽셀)입니다.</summary>
    public float HitMarkerBaseLengthPixels
    {
        get => m_hitMarkerBaseLengthPixels;
        set { m_hitMarkerBaseLengthPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>몸통 적중 시 히트마커의 색입니다.</summary>
    public Color HitMarkerColorBody
    {
        get => m_hitMarkerColorBody;
        set => m_hitMarkerColorBody = value;
    }

    /// <summary>약점 적중 시 히트마커의 색입니다.</summary>
    public Color HitMarkerColorHead
    {
        get => m_hitMarkerColorHead;
        set => m_hitMarkerColorHead = value;
    }

    /// <summary>처치 해골의 색조입니다.</summary>
    public Color KillSkullTint
    {
        get => m_killSkullTint;
        set => m_killSkullTint = value;
    }

    /// <summary>킬 해골의 표시 크기(픽셀)입니다.</summary>
    public float KillSkullSizePixels
    {
        get => m_killSkullSizePixels;
        set { m_killSkullSizePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>킬 해골이 완전히 보이는 유지 시간(초)입니다.</summary>
    public float KillSkullHoldDuration
    {
        get => m_killSkullHoldDuration;
        set { m_killSkullHoldDuration = Mathf.Max(0.0f, value); }
    }

    /// <summary>킬 해골이 사라지기까지 걸리는 시간(초)입니다.</summary>
    public float KillSkullFadeDuration
    {
        get => m_killSkullFadeDuration;
        set { m_killSkullFadeDuration = Mathf.Max(0.0f, value); }
    }

    /// <summary>킬 해골을 표시할지 여부입니다.</summary>
    public bool ShowKillSkull
    {
        get => m_showKillSkull;
        set => m_showKillSkull = value;
    }

    /// <summary>히트마커를 표시할지 여부입니다.</summary>
    /// <remarks>표시를 실행하는 <see cref="ShowHitMarker(bool)"/>와 이름이 겹치지 않도록 Enabled를 붙였습니다.</remarks>
    public bool HitMarkerEnabled
    {
        get => m_showHitMarker;
        set => m_showHitMarker = value;
    }

    /// <summary>히트마커 선의 길이(픽셀)입니다.</summary>
    public float HitMarkerLengthPixels
    {
        get => m_hitMarkerLengthPixels;
        set { m_hitMarkerLengthPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>히트마커 중앙 빈 공간(픽셀)입니다.</summary>
    public float HitMarkerCenterGapPixels
    {
        get => m_hitMarkerCenterGapPixels;
        set { m_hitMarkerCenterGapPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>히트마커가 사라지기까지 걸리는 시간(초)입니다.</summary>
    public float HitMarkerFadeDuration
    {
        get => m_hitMarkerFadeDuration;
        set { m_hitMarkerFadeDuration = Mathf.Max(0.0f, value); }
    }

    /// <summary>히트마커의 기준 Z축 회전 각도(도)입니다. 발마다의 무작위 흔들림이 이 값을 중심으로 더해집니다.</summary>
    public float HitMarkerRollBaseDegrees
    {
        get => m_hitMarkerRollBaseDegrees;
        set { m_hitMarkerRollBaseDegrees = value; RefreshRuntimeLayout(); }
    }

    /// <summary>히트마커가 발마다 기준 각도에서 무작위로 흔들리는 범위(도)입니다. 0이면 고정됩니다.</summary>
    public float HitMarkerRollRandomRangeDegrees
    {
        get => m_hitMarkerRollRandomRangeDegrees;
        set { m_hitMarkerRollRandomRangeDegrees = Mathf.Max(0.0f, value); }
    }

    /// <summary>명중마다 히트마커 회전을 무작위로 흔들지 여부입니다. 끄면 기준 각도로 고정됩니다.</summary>
    public bool HitMarkerRandomRollEnabled
    {
        get => m_enableHitMarkerRandomRoll;
        set => m_enableHitMarkerRandomRoll = value;
    }

    /// <summary>히트마커 길이를 피해량에 비례시킬지 여부입니다. 끄면 고정 길이를 씁니다.</summary>
    public bool HitMarkerDamageScaleEnabled
    {
        get => m_enableHitMarkerDamageScale;
        set => m_enableHitMarkerDamageScale = value;
    }

    /// <summary>길이 비례의 기준이 되는 최소 피해량입니다.</summary>
    public float HitMarkerMinDamage
    {
        get => m_hitMarkerMinDamage;
        set => m_hitMarkerMinDamage = value;
    }

    /// <summary>길이 비례의 기준이 되는 최대 피해량입니다.</summary>
    public float HitMarkerMaxDamage
    {
        get => m_hitMarkerMaxDamage;
        set => m_hitMarkerMaxDamage = value;
    }

    /// <summary>최소 피해량일 때의 히트마커 삼각형 길이(픽셀)입니다.</summary>
    public float HitMarkerLengthAtMinDamagePixels
    {
        get => m_hitMarkerLengthAtMinDamagePixels;
        set => m_hitMarkerLengthAtMinDamagePixels = Mathf.Max(0.0f, value);
    }

    /// <summary>최대 피해량일 때의 히트마커 삼각형 길이(픽셀)입니다.</summary>
    public float HitMarkerLengthAtMaxDamagePixels
    {
        get => m_hitMarkerLengthAtMaxDamagePixels;
        set => m_hitMarkerLengthAtMaxDamagePixels = Mathf.Max(0.0f, value);
    }

    /// <summary>조준선 주 선의 길이(픽셀)입니다.</summary>
    public float MainSizePixels
    {
        get => m_mainSizePixels;
        set { m_mainSizePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>조준선 주 선의 두께(픽셀)입니다.</summary>
    public float MainStrokeThicknessPixels
    {
        get => m_mainStrokeThicknessPixels;
        set { m_mainStrokeThicknessPixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>조준선 중앙의 빈 공간(픽셀)입니다.</summary>
    public float CenterSpacePixels
    {
        get => m_centerSpacePixels;
        set { m_centerSpacePixels = Mathf.Max(0.0f, value); RefreshRuntimeLayout(); }
    }

    /// <summary>
    /// 값을 바꾼 뒤 화면에 즉시 반영합니다.
    /// </summary>
    /// <remarks>
    /// 플레이 중이 아니면 시각 요소가 준비되지 않아 아무 일도 하지 않습니다.
    /// 마지막으로 적용한 탄퍼짐 값을 그대로 다시 넣어, 보간 없이 바뀐 크기가 바로 보이게 합니다.
    /// </remarks>
    private void RefreshRuntimeLayout()
    {
        ClampSettings();

        if (!Application.isPlaying)
        {
            return;
        }

        CacheVisualElements();
        SetSpreadInternal(m_lastSpreadDegrees, m_lastDistribution, m_lastConcentration, m_lastCameraFovDegrees, true);
    }

    /// <summary>
    /// 재장전 상태를 설정합니다. 스왑 토글이 켜져 있으면 재장전 중 크로스헤어를 숨기고 중앙 탄약 아이콘을 표시합니다.
    /// </summary>
    /// <param name="reloading">재장전 중이면 <c>true</c>입니다.</param>
    public void SetReloading(bool reloading)
    {
        if (m_isReloading == reloading)
        {
            return;
        }

        m_isReloading = reloading;

        if (!CacheVisualElements())
        {
            return;
        }

        if (!reloading && m_reloadBulletElement != null)
        {
            m_reloadBulletElement.style.opacity = 1.0f;
        }

        ApplyLayout(m_currentGapPixels);
    }

    /// <summary>
    /// 탄약 게이지 채움 비율(0~1)을 설정합니다. 평소에는 현재 탄약 비율, 재장전 중에는 재장전 진행도를 전달합니다.
    /// </summary>
    /// <param name="fill">게이지 채움 비율(0~1)입니다.</param>
    public void SetAmmoGaugeFill(float fill)
    {
        fill = Mathf.Clamp01(fill);
        if (Mathf.Approximately(m_ammoGaugeFill, fill))
        {
            return;
        }

        m_ammoGaugeFill = fill;
        if (m_ammoGaugeElement != null)
        {
            m_ammoGaugeElement.MarkDirtyRepaint();
        }
    }

    /// <summary>
    /// 총구 히트스캔이 중간 장애물에 막혔을 때, 실제 탄착점의 화면 위치에 차단 마커를 표시합니다.
    /// </summary>
    /// <param name="worldPosition">표시할 탄착점의 월드 좌표입니다.</param>
    /// <param name="camera">월드 좌표를 화면으로 투영할 렌더 카메라입니다. <c>null</c>이면 마커를 숨깁니다.</param>
    /// <remarks>
    /// 표면 법선에 맞춰 벽에 눕히던 월드 마커를 대신합니다. 화면 UI라 벽 방향·기울기·z-파이팅과 무관하며,
    /// 벽·바닥·적 몸통 어디에 걸려도 같은 크기와 같은 모양으로 보입니다.
    /// <para>
    /// <see cref="AimController"/>가 조준 중 매 프레임 호출합니다. 막힘이 풀리면 <see cref="HideBlockMarker"/>를 부르는
    /// 쪽이 호출자이며, 여기서는 카메라 뒤로 넘어간 지점만 스스로 숨깁니다.
    /// </para>
    /// </remarks>
    public void ShowBlockMarker(Vector3 worldPosition, Camera camera)
    {
        if (!m_showBlockMarker || camera == null)
        {
            HideBlockMarker();
            return;
        }

        if (!CacheVisualElements() || m_blockMarkerElement == null)
        {
            return;
        }

        IPanel panel = m_blockMarkerElement.panel;
        if (panel == null)
        {
            return;
        }

        // 카메라 뒤쪽 지점은 투영이 반대편으로 되접혀 엉뚱한 위치에 그려지므로 표시하지 않습니다.
        if (camera.WorldToViewportPoint(worldPosition).z <= 0.0f)
        {
            HideBlockMarker();
            return;
        }

        float size = Mathf.Max(m_blockMarkerRingSizePixels, m_blockMarkerDotSizePixels);
        if (size <= 0.0f)
        {
            HideBlockMarker();
            return;
        }

        // 패널 스케일 모드까지 반영해 월드 좌표를 패널 좌표로 변환합니다(화면 픽셀 직접 계산은 스케일에서 어긋납니다).
        Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition, camera);

        m_blockMarkerElement.style.display = DisplayStyle.Flex;
        m_blockMarkerElement.style.width = size;
        m_blockMarkerElement.style.height = size;
        m_blockMarkerElement.style.left = panelPosition.x - size * 0.5f;
        m_blockMarkerElement.style.top = panelPosition.y - size * 0.5f;

        if (!m_blockMarkerVisible)
        {
            m_blockMarkerVisible = true;
            m_blockMarkerElement.MarkDirtyRepaint();
        }
    }

    /// <summary>
    /// 차단 마커를 숨깁니다. 막힘이 풀렸거나 조준·전투 자세가 끝났을 때 호출합니다.
    /// </summary>
    public void HideBlockMarker()
    {
        m_blockMarkerVisible = false;
        HideElement(m_blockMarkerElement);
    }

    /// <summary>
    /// 차단 마커를 Painter2D로 그립니다. 임시 UI로 원(링) 하나와 가운데 점 하나만 그립니다.
    /// </summary>
    /// <remarks>
    /// 링 스트로크는 중심선을 따라 그려지므로 반지름을 (지름 − 두께)/2로 잡아 두께가 요소 안쪽에 들어오게 합니다.
    /// 텍스처 대신 프로시저럴로 그려 크기를 바꿔도 선명하고, 최종 아트로 교체할 때 이 함수만 걷어내면 됩니다.
    /// </remarks>
    private void OnGenerateBlockMarker(MeshGenerationContext context)
    {
        VisualElement element = context.visualElement;
        float size = Mathf.Min(element.resolvedStyle.width, element.resolvedStyle.height);
        if (size <= 0.0f)
        {
            return;
        }

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        Painter2D painter = context.painter2D;

        float ringSize = Mathf.Min(m_blockMarkerRingSizePixels, size);
        float ringThickness = Mathf.Clamp(m_blockMarkerRingThicknessPixels, 0.0f, ringSize * 0.5f);
        float ringRadius = (ringSize - ringThickness) * 0.5f;
        if (ringThickness > 0.0f && ringRadius > 0.0f)
        {
            painter.lineCap = LineCap.Butt;
            painter.lineWidth = ringThickness;
            painter.strokeColor = m_blockMarkerColor;
            painter.BeginPath();
            painter.Arc(center, ringRadius, 0.0f, 360.0f);
            painter.ClosePath();
            painter.Stroke();
        }

        float dotRadius = Mathf.Min(m_blockMarkerDotSizePixels, size) * 0.5f;
        if (dotRadius > 0.0f)
        {
            painter.fillColor = m_blockMarkerColor;
            painter.BeginPath();
            painter.Arc(center, dotRadius, 0.0f, 360.0f);
            painter.ClosePath();
            painter.Fill();
        }
    }

    /// <summary>
    /// 조준선 UXML을 표시하는 <see cref="UIDocument"/> 참조가 비어 있으면 같은 GameObject에서 찾아 캐싱합니다.
    /// </summary>
    private void CacheDocument()
    {
        if (m_document == null)
        {
            m_document = GetComponent<UIDocument>();
        }
    }

    /// <summary>
    /// 조준선을 구성하는 루트/컨테이너/중앙/상하좌우 팔 VisualElement를 이름으로 찾아 캐싱합니다.
    /// </summary>
    /// <returns>조준선 조작에 필요한 모든 VisualElement가 유효하게 준비되면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 요소를 찾지 못하면 <see cref="m_createMissingElements"/>가 켜진 경우에 한해 런타임에 생성합니다.
    /// <see cref="UIDocument.rootVisualElement"/>가 아직 없으면(패널 미부착 등) <c>false</c>를 반환하고 조기 종료합니다.
    /// </remarks>
    private bool CacheVisualElements()
    {
        CacheDocument();

        if (m_document == null || m_document.rootVisualElement == null)
        {
            return false;
        }

        VisualElement documentRoot = m_document.rootVisualElement;
        m_rootElement = FindElement(documentRoot, m_rootElementName);
        if (m_rootElement == null)
        {
            m_rootElement = m_createMissingElements ? CreateElement(documentRoot, m_rootElementName) : documentRoot;
        }

        m_crosshairElement = FindElement(m_rootElement, m_crosshairElementName);
        if (m_crosshairElement == null && m_createMissingElements)
        {
            m_crosshairElement = CreateElement(m_rootElement, m_crosshairElementName);
        }

        m_centerElement = FindOrCreateChild(m_crosshairElement, m_centerElementName);
        m_topElement = FindOrCreateChild(m_crosshairElement, m_topElementName);
        m_topStrokeElement = FindOrCreateChild(m_crosshairElement, m_topStrokeElementName);
        m_bottomElement = FindOrCreateChild(m_crosshairElement, m_bottomElementName);
        m_bottomStrokeElement = FindOrCreateChild(m_crosshairElement, m_bottomStrokeElementName);
        m_leftElement = FindOrCreateChild(m_crosshairElement, m_leftElementName);
        m_leftStrokeElement = FindOrCreateChild(m_crosshairElement, m_leftStrokeElementName);
        m_rightElement = FindOrCreateChild(m_crosshairElement, m_rightElementName);
        m_rightStrokeElement = FindOrCreateChild(m_crosshairElement, m_rightStrokeElementName);
        m_mainStrokeElement = FindOrCreateChild(m_crosshairElement, m_mainStrokeElementName);
        m_subShapeElement = FindOrCreateChild(m_crosshairElement, m_subShapeElementName);
        m_subStrokeElement = FindOrCreateChild(m_crosshairElement, m_subStrokeElementName);
        m_ammoGaugeElement = FindOrCreateChild(m_crosshairElement, "AmmoGauge");
        if (m_ammoGaugeElement != null)
        {
            // 같은 요소가 재캐싱될 수 있어 중복 구독을 막기 위해 해제 후 구독합니다.
            m_ammoGaugeElement.generateVisualContent -= OnGenerateAmmoGauge;
            m_ammoGaugeElement.generateVisualContent += OnGenerateAmmoGauge;
        }

        m_reloadBulletElement = FindOrCreateChild(m_crosshairElement, "ReloadBullet");

        m_hitMarkerElement = FindOrCreateChild(m_crosshairElement, "HitMarker");
        if (m_hitMarkerElement != null)
        {
            // 같은 요소가 재캐싱될 수 있어 중복 구독을 막기 위해 해제 후 구독합니다.
            m_hitMarkerElement.generateVisualContent -= OnGenerateHitMarker;
            m_hitMarkerElement.generateVisualContent += OnGenerateHitMarker;
        }

        m_killSkullElement = FindOrCreateChild(m_crosshairElement, "KillSkull");

        // 차단 마커는 화면 중앙이 아니라 탄착점 위치에 놓이므로 0×0 앵커(크로스헤어) 밖, 패널 전체를 덮는 루트에 붙입니다.
        m_blockMarkerElement = FindOrCreateChild(m_rootElement, "BlockMarker");
        if (m_blockMarkerElement != null)
        {
            // 절대배치가 아니면 루트의 flex 중앙정렬에 참여해 크로스헤어 앵커를 밀어냅니다.
            m_blockMarkerElement.pickingMode = PickingMode.Ignore;
            m_blockMarkerElement.style.position = Position.Absolute;
            if (!m_blockMarkerVisible)
            {
                HideElement(m_blockMarkerElement);
            }

            // 같은 요소가 재캐싱될 수 있어 중복 구독을 막기 위해 해제 후 구독합니다.
            m_blockMarkerElement.generateVisualContent -= OnGenerateBlockMarker;
            m_blockMarkerElement.generateVisualContent += OnGenerateBlockMarker;
        }

        return m_rootElement != null
            && m_crosshairElement != null
            && m_centerElement != null
            && m_topElement != null
            && m_topStrokeElement != null
            && m_bottomElement != null
            && m_bottomStrokeElement != null
            && m_leftElement != null
            && m_leftStrokeElement != null
            && m_rightElement != null
            && m_rightStrokeElement != null
            && m_mainStrokeElement != null
            && m_subShapeElement != null
            && m_subStrokeElement != null;
    }

    /// <summary>
    /// 지정한 루트 하위에서 이름이 일치하는 VisualElement를 찾습니다.
    /// </summary>
    /// <param name="root">탐색을 시작할 루트 VisualElement입니다.</param>
    /// <param name="elementName">찾을 VisualElement 이름입니다.</param>
    /// <returns>일치하는 VisualElement, 이름이 비어 있거나 찾지 못하면 <c>null</c>입니다.</returns>
    private static VisualElement FindElement(VisualElement root, string elementName)
    {
        return string.IsNullOrWhiteSpace(elementName) ? null : root.Q<VisualElement>(elementName);
    }

    /// <summary>
    /// 부모 하위에서 이름이 일치하는 자식 VisualElement를 찾고, 없으면 설정에 따라 생성합니다.
    /// </summary>
    /// <param name="parent">자식을 찾거나 생성할 부모 VisualElement입니다.</param>
    /// <param name="elementName">찾거나 생성할 자식 VisualElement 이름입니다.</param>
    /// <returns>찾거나 생성한 자식 VisualElement입니다. 부모가 <c>null</c>이거나 생성이 꺼져 있고 찾지 못하면 <c>null</c>입니다.</returns>
    private VisualElement FindOrCreateChild(VisualElement parent, string elementName)
    {
        if (parent == null)
        {
            return null;
        }

        VisualElement element = FindElement(parent, elementName);
        return element ?? (m_createMissingElements ? CreateElement(parent, elementName) : null);
    }

    /// <summary>
    /// 부모 하위에 새 VisualElement를 생성해 추가합니다.
    /// </summary>
    /// <param name="parent">새 요소를 추가할 부모 VisualElement입니다.</param>
    /// <param name="elementName">생성할 VisualElement 이름입니다.</param>
    /// <returns>생성해 부모에 추가한 VisualElement입니다. 부모가 <c>null</c>이면 <c>null</c>입니다.</returns>
    /// <remarks>조준선은 입력을 가로채면 안 되므로 <see cref="PickingMode.Ignore"/>로 생성합니다.</remarks>
    private static VisualElement CreateElement(VisualElement parent, string elementName)
    {
        if (parent == null)
        {
            return null;
        }

        VisualElement element = new()
        {
            name = elementName,
            pickingMode = PickingMode.Ignore,
        };
        parent.Add(element);
        return element;
    }

    /// <summary>
    /// 픽셀·배율·간격 등 Inspector 설정값을 서로 모순되지 않는 유효 범위로 보정합니다.
    /// </summary>
    /// <remarks>최대 간격은 Center Space 이상으로 강제해, 팔이 기본 간격보다 안쪽으로 들어가지 않게 합니다.</remarks>
    private void ClampSettings()
    {
        m_centerSpacePixels = Mathf.Max(0.0f, m_centerSpacePixels);
        m_maxGapPixels = Mathf.Max(m_centerSpacePixels, m_maxGapPixels);
        m_lerpSpeed = Mathf.Max(0.0f, m_lerpSpeed);
        m_shotRecoilPulseAmplitudePixels = Mathf.Max(0.0f, m_shotRecoilPulseAmplitudePixels);
        m_shotRecoilPulseDuration = Mathf.Max(0.0f, m_shotRecoilPulseDuration);
        m_mainSizePixels = Mathf.Max(0.0f, m_mainSizePixels);
        m_mainRingSizePixels = Mathf.Max(0.0f, m_mainRingSizePixels);
        m_mainRingThicknessPixels = Mathf.Max(0.0f, m_mainRingThicknessPixels);
        m_mainStrokeThicknessPixels = Mathf.Max(0.0f, m_mainStrokeThicknessPixels);
        m_subSizePixels = Mathf.Max(0.0f, m_subSizePixels);
        m_subWidthPixels = Mathf.Max(0.0f, m_subWidthPixels);
        m_subThicknessPixels = Mathf.Max(0.0f, m_subThicknessPixels);
        m_subRingSizePixels = Mathf.Max(0.0f, m_subRingSizePixels);
        m_subRingThicknessPixels = Mathf.Max(0.0f, m_subRingThicknessPixels);
        m_subStrokeThicknessPixels = Mathf.Max(0.0f, m_subStrokeThicknessPixels);
        m_cornerRadiusPixels = Mathf.Max(0.0f, m_cornerRadiusPixels);
        m_blockMarkerRingSizePixels = Mathf.Max(0.0f, m_blockMarkerRingSizePixels);
        m_blockMarkerRingThicknessPixels = Mathf.Max(0.0f, m_blockMarkerRingThicknessPixels);
        m_blockMarkerDotSizePixels = Mathf.Max(0.0f, m_blockMarkerDotSizePixels);
        m_blockMarkerCrosshairAlpha = Mathf.Clamp01(m_blockMarkerCrosshairAlpha);
        m_blockMarkerCrosshairFadeDuration = Mathf.Max(0.0f, m_blockMarkerCrosshairFadeDuration);
        m_hitMarkerRollRandomRangeDegrees = Mathf.Max(0.0f, m_hitMarkerRollRandomRangeDegrees);
    }

    /// <summary>
    /// 탄퍼짐이 벌어짐 간격에 더할 기여분(픽셀)을 계산합니다. 중심 간격은 포함하지 않습니다.
    /// </summary>
    /// <param name="spreadDegrees">현재 무기 탄퍼짐 방사각(도, 콘 반각=하드캡)입니다.</param>
    /// <param name="displayFactor">콘 반각 대비 표시 배율(tan 공간)입니다.</param>
    /// <param name="cameraFovDegrees">현재 조준 카메라 세로 FOV(도)입니다.</param>
    /// <returns>유효각(= spread를 tan 공간에서 배율만큼 축소한 값)을 FOV로 실제 화면 투영한 픽셀 기여분입니다.</returns>
    /// <remarks>
    /// A(유효각): 탄은 콘 경계가 아니라 중심에 몰리므로, 배율로 "탄이 실제로 몰리는 반경"을 구합니다.
    /// C(물리 투영): 그 각도를 FOV로 실제 화면 투영해, 팔 벌어짐이 화면상 탄착 분포와 1:1이 되게 합니다.
    /// <see cref="ComposeTargetGapPixels"/>와 디버그 표시가 동일한 값을 쓰도록 이 헬퍼를 공유합니다.
    /// </remarks>
    private float CalculateSpreadGapPixels(float spreadDegrees, float displayFactor, float cameraFovDegrees)
    {
        if (!m_useSpreadAccuracy)
        {
            return 0.0f;
        }

        return CalculateProjectedSpreadPixels(spreadDegrees, cameraFovDegrees) * Mathf.Max(0.0f, displayFactor);
    }

    /// <summary>
    /// 탄퍼짐 방사각(도)을 카메라 FOV 기준으로 화면 세로 픽셀 간격으로 환산합니다.
    /// </summary>
    /// <param name="spreadDegrees">환산할 탄퍼짐 방사각(도)입니다.</param>
    /// <param name="cameraFovDegrees">투영 기준으로 삼을 카메라 세로 FOV(도)입니다.</param>
    /// <returns>화면상 조준선 반벌어짐에 해당하는 픽셀 값입니다.</returns>
    /// <remarks>반FOV와 방사각의 tan 비율에 화면 세로 절반 픽셀을 곱해, 원근 투영과 일치하는 벌어짐을 만듭니다.</remarks>
    private static float CalculateProjectedSpreadPixels(float spreadDegrees, float cameraFovDegrees)
    {
        float halfFovRadians = Mathf.Max(1.0f, cameraFovDegrees) * 0.5f * Mathf.Deg2Rad;
        float spreadRadians = Mathf.Max(0.0f, spreadDegrees) * Mathf.Deg2Rad;
        return Mathf.Tan(spreadRadians) / Mathf.Tan(halfFovRadians) * Screen.height * 0.5f;
    }

    /// <summary>
    /// 계산된 벌어짐 간격을 바탕으로 루트/컨테이너/중앙/상하좌우 팔의 위치와 크기를 배치합니다.
    /// </summary>
    /// <param name="gapPixels">중심에서 각 팔 안쪽까지의 현재 벌어짐 간격(픽셀)입니다.</param>
    /// <remarks>
    /// 컨테이너는 0×0 앵커로 두고 flex 중앙정렬로 패널 정중앙에 배치한 뒤, 중앙 표시와 팔을 앵커(중심=0) 기준 ± 오프셋으로 절대배치합니다.
    /// 고정 크기 컨테이너를 중앙정렬하면 벌어짐이 화면(패널)보다 커질 때 오버플로로 정렬이 상단에 pin되어 중심이 밀리므로, 크기에 무관한 앵커 방식을 사용합니다.
    /// 보조 표시가 꺼져 있으면 네 팔을 숨깁니다.
    /// </remarks>
    private void ApplyLayout(float gapPixels)
    {
        // 파츠 배치 기준 = 앵커(0). 컨테이너가 0×0이라 앵커가 곧 패널 정중앙이 됩니다.
        const float center = 0.0f;

        m_rootElement.pickingMode = PickingMode.Ignore;
        m_rootElement.style.position = Position.Absolute;
        m_rootElement.style.left = 0.0f;
        m_rootElement.style.top = 0.0f;
        m_rootElement.style.right = 0.0f;
        m_rootElement.style.bottom = 0.0f;
        m_rootElement.style.alignItems = Align.Center;
        m_rootElement.style.justifyContent = Justify.Center;

        // 0×0 앵커: flex 중앙정렬로 패널 정중앙에 놓이며, 절대배치 파츠 좌표의 원점이 됩니다. 파츠는 이 원점 밖으로 자유롭게 오버플로합니다.
        m_crosshairElement.pickingMode = PickingMode.Ignore;
        m_crosshairElement.style.position = Position.Relative;
        m_crosshairElement.style.width = 0.0f;
        m_crosshairElement.style.height = 0.0f;

        ApplyCenter(center);

        switch (m_subShape)
        {
            case SubShape.RoundedCross:
                HideSubShapeElements();
                ApplySubCross(center, gapPixels, m_cornerRadiusPixels);
                break;

            case SubShape.SquareCross:
                HideSubShapeElements();
                ApplySubCross(center, gapPixels, 0.0f);
                break;

            case SubShape.Dot:
                HideSubShapeElements();
                ApplySubDots(center, gapPixels);
                break;

            case SubShape.Ring:
                HideSubDirectionElements();
                ApplySubRing(center, gapPixels);
                break;

            default:
                HideSubDirectionElements();
                HideSubShapeElements();
                break;
        }

        // 재장전 스왑: 재장전 중이고 토글이 켜져 있으면 크로스헤어를 숨기고 중앙에 재장전 탄약 아이콘을 표시합니다.
        bool reloadSwap = m_isReloading && m_swapCrosshairOnReload;
        if (reloadSwap)
        {
            HideElement(m_centerElement);
            HideElement(m_mainStrokeElement);
            HideSubDirectionElements();
            HideSubShapeElements();
        }

        ApplyReloadBullet(center, reloadSwap);
        ApplyAmmoGauge(center);
        LayoutHitMarker(center);
        LayoutKillSkull(center);
    }

    /// <summary>
    /// 재장전 중 중앙에 표시할 탄약 아이콘을 배치합니다. 활성 상태가 아니거나 이미지/크기가 없으면 숨깁니다.
    /// </summary>
    /// <param name="center">파츠 배치 기준 앵커(0 = 패널 정중앙)입니다.</param>
    /// <param name="active">재장전 스왑이 활성 상태인지 여부입니다.</param>
    private void ApplyReloadBullet(float center, bool active)
    {
        if (m_reloadBulletElement == null)
        {
            return;
        }

        if (!active || m_reloadBulletImage == null || m_reloadBulletSizePixels <= 0.0f)
        {
            HideElement(m_reloadBulletElement);
            return;
        }

        float size = m_reloadBulletSizePixels;
        ApplyTextureImage(m_reloadBulletElement, m_reloadBulletImage, center - size * 0.5f, center - size * 0.5f, size);
    }

    /// <summary>
    /// 탄약 아크 게이지를 크로스헤어 중심에서 우하단 대각으로 오프셋해 배치합니다.
    /// 항상 표시가 꺼져 있으면 재장전 중에만 보이고, 표시 자체가 꺼져 있으면 숨깁니다(에디트 프리뷰에서는 전체 아크를 표시).
    /// </summary>
    /// <param name="center">파츠 배치 기준 앵커(0 = 패널 정중앙)입니다.</param>
    private void ApplyAmmoGauge(float center)
    {
        if (m_ammoGaugeElement == null)
        {
            return;
        }

        bool visible = m_showAmmoGauge
                    && m_ammoGaugeSizePixels > 0.0f
                    && (m_ammoGaugeAlwaysVisible || m_isReloading || !Application.isPlaying);
        if (!visible)
        {
            HideElement(m_ammoGaugeElement);
            return;
        }

        float size = m_ammoGaugeSizePixels;
        float offset = m_ammoGaugeDiagonalOffset;
        // 게이지 중심을 크로스헤어 중심에서 (offset, offset)만큼 우하단으로 이동(각 축 동일 = 대각).
        float position = center + offset - size * 0.5f;
        m_ammoGaugeElement.pickingMode = PickingMode.Ignore;
        m_ammoGaugeElement.style.display = DisplayStyle.Flex;
        m_ammoGaugeElement.style.position = Position.Absolute;
        m_ammoGaugeElement.style.left = position;
        m_ammoGaugeElement.style.top = position;
        m_ammoGaugeElement.style.width = size;
        m_ammoGaugeElement.style.height = size;
        m_ammoGaugeElement.style.backgroundColor = Color.clear;
        m_ammoGaugeElement.style.backgroundImage = new StyleBackground(StyleKeyword.None);
        m_ammoGaugeElement.MarkDirtyRepaint();
    }

    /// <summary>
    /// 탄약 게이지 아크를 Painter2D로 그립니다. 전체 구간 트랙을 옅게 깐 뒤 채움 비율(평소 탄약 비율, 재장전 중 진행도)만큼 아크를 채웁니다.
    /// </summary>
    /// <remarks>
    /// 각도는 UI Toolkit 좌표 기준(0도 = 3시 방향, 시계 방향)입니다. 벡터 에셋 대신 프로시저럴로 그려
    /// 어떤 크기에서도 선명하고, 진행도에 따른 부분 아크를 임의 각도로 표현할 수 있습니다.
    /// 에디트 프리뷰(비플레이)에서는 크기·두께·각도 튜닝을 위해 전체 아크를 표시합니다.
    /// </remarks>
    private void OnGenerateAmmoGauge(MeshGenerationContext context)
    {
        VisualElement element = context.visualElement;
        float size = Mathf.Min(element.resolvedStyle.width, element.resolvedStyle.height);
        float thickness = Mathf.Clamp(m_ammoGaugeThicknessPixels, 0.0f, size * 0.5f);
        float radius = (size - thickness) * 0.5f;
        if (thickness <= 0.0f || radius <= 0.0f)
        {
            return;
        }

        Vector2 arcCenter = new Vector2(size * 0.5f, size * 0.5f);
        float startAngle = m_ammoGaugeStartAngleDegrees;
        float sweep = Mathf.Clamp(m_ammoGaugeSweepDegrees, 0.0f, 360.0f);
        Painter2D painter = context.painter2D;
        painter.lineCap = LineCap.Butt;
        painter.lineWidth = thickness;

        if (m_showAmmoGaugeBackground && m_ammoGaugeBackgroundAlpha > 0.0f)
        {
            Color backgroundColor = m_ammoGaugeBackgroundColor;
            backgroundColor.a *= m_ammoGaugeBackgroundAlpha;
            DrawGaugeArc(painter, arcCenter, radius, startAngle, sweep, m_ammoGaugeFillDirection, backgroundColor);
        }

        float fill = Application.isPlaying ? m_ammoGaugeFill : 1.0f;
        Color fillColor = GetAmmoGaugeFillColor(fill);
        DrawGaugeArc(painter, arcCenter, radius, startAngle, sweep * fill, m_ammoGaugeFillDirection, fillColor);
    }

    /// <summary>
    /// 재장전 중이면 재장전 색, 현재 장탄 비율이 낮으면 경고 색, 아니면 기본 게이지 색을 반환합니다.
    /// </summary>
    private Color GetAmmoGaugeFillColor(float fill)
    {
        if (Application.isPlaying && m_isReloading)
        {
            return m_reloadAmmoGaugeColor;
        }

        if (Application.isPlaying && fill < LowAmmoGaugeThreshold)
        {
            return m_lowAmmoGaugeColor;
        }

        return m_ammoGaugeColor;
    }

    /// <summary>
    /// 시작 각도에서 지정한 방향과 구간만큼 스트로크 아크를 그립니다. 구간이 0이면 그리지 않습니다.
    /// </summary>
    private static void DrawGaugeArc(Painter2D painter, Vector2 center, float radius, float startAngle, float sweep, AmmoGaugeFillDirection fillDirection, Color color)
    {
        if (sweep <= 0.0f)
        {
            return;
        }

        ArcDirection arcDirection = fillDirection == AmmoGaugeFillDirection.CounterClockwise
            ? ArcDirection.CounterClockwise
            : ArcDirection.Clockwise;

        painter.strokeColor = color;
        painter.BeginPath();
        painter.Arc(center, radius, startAngle, startAngle + sweep, arcDirection);
        painter.Stroke();
    }

    /// <summary>
    /// 히트마커 요소의 위치·크기를 잡습니다. 가시성은 페이드 타이머가 제어하므로, 타이머가 없으면 숨깁니다.
    /// </summary>
    /// <param name="center">파츠 배치 기준 앵커(0 = 패널 정중앙)입니다.</param>
    /// <summary>
    /// 지금 히트마커에 적용할 Z축 회전 각도(도)를 반환합니다.
    /// </summary>
    /// <returns>플레이 중에는 이번 명중에 뽑힌 각도, 에디트 프리뷰에서는 기준 각도입니다.</returns>
    /// <remarks>
    /// 프리뷰에서까지 무작위를 쓰면 레이아웃이 갱신될 때마다 각도가 흔들려 값 조정이 어렵습니다.
    /// 그래서 프리뷰는 기준 각도만 보여주고, 무작위는 실제 명중 시점(<see cref="ShowHitMarker"/>)에만 뽑습니다.
    /// </remarks>
    private float ResolveHitMarkerRollDegrees()
    {
        return IsHitFeedbackPreview() ? m_hitMarkerRollBaseDegrees : m_hitMarkerRollDegrees;
    }

    /// <summary>
    /// 피해량에 대응하는 히트마커 삼각형 길이(픽셀)를 계산합니다.
    /// </summary>
    /// <param name="damage">이번 명중의 최종 피해량입니다.</param>
    /// <returns>비례가 꺼져 있거나 피해량이 0 이하이면 고정 길이, 아니면 최소~최대 길이 사이 보간값입니다.</returns>
    /// <remarks>
    /// 최소·최대 피해량이 같거나 뒤집혀 있으면 나눗셈이 성립하지 않으므로 고정 길이로 물러섭니다.
    /// 설정 실수로 마커가 사라지거나 튀는 것보다 예전 크기로 도는 편이 낫기 때문입니다.
    /// </remarks>
    private float ResolveHitMarkerLengthPixels(int damage)
    {
        if (!m_enableHitMarkerDamageScale || damage <= 0)
        {
            return m_hitMarkerLengthPixels;
        }

        float span = m_hitMarkerMaxDamage - m_hitMarkerMinDamage;
        if (span <= 0.0001f)
        {
            return m_hitMarkerLengthPixels;
        }

        float t = Mathf.Clamp01((damage - m_hitMarkerMinDamage) / span);
        return Mathf.Lerp(m_hitMarkerLengthAtMinDamagePixels, m_hitMarkerLengthAtMaxDamagePixels, t);
    }

    /// <summary>
    /// 지금 히트마커를 그릴 때 쓸 삼각형 길이(픽셀)를 반환합니다.
    /// </summary>
    /// <remarks>
    /// 에디트 프리뷰에서는 피해량이 없으므로 고정 길이를 보여줍니다. 플레이 중에는 마지막 명중에서
    /// 계산한 길이를 유지해, 페이드아웃되는 동안 크기가 흔들리지 않게 합니다.
    /// </remarks>
    private float ResolveActiveHitMarkerLengthPixels()
    {
        if (IsHitFeedbackPreview() || m_hitMarkerActiveLengthPixels <= 0.0f)
        {
            return m_hitMarkerLengthPixels;
        }

        return m_hitMarkerActiveLengthPixels;
    }

    private void LayoutHitMarker(float center)
    {
        if (m_hitMarkerElement == null)
        {
            return;
        }

        float activeLength = ResolveActiveHitMarkerLengthPixels();

        if (!m_showHitMarker || activeLength <= 0.0f)
        {
            HideElement(m_hitMarkerElement);
            m_hitMarkerTimer = 0.0f;
            return;
        }

        // 중심에서 삼각형 바깥 끝(gap+length)에 밑변 절반 길이까지 감싸도록 여유를 둔 정사각형 요소.
        // 길이가 피해량에 따라 달라지므로 요소 크기도 이번 길이에 맞춰 잡아야 큰 마커가 잘리지 않습니다.
        float half = m_hitMarkerCenterGapPixels + activeLength + m_hitMarkerBaseLengthPixels;
        float size = half * 2.0f;
        float position = center - half;

        m_hitMarkerElement.pickingMode = PickingMode.Ignore;
        m_hitMarkerElement.style.position = Position.Absolute;
        m_hitMarkerElement.style.left = position;
        m_hitMarkerElement.style.top = position;
        m_hitMarkerElement.style.width = size;
        m_hitMarkerElement.style.height = size;
        m_hitMarkerElement.style.backgroundColor = Color.clear;

        // 요소가 중심을 감싸는 정사각형이라 기본 transform-origin(중앙)이 곧 조준 중심입니다.
        // 따라서 회전만 얹으면 히트마커가 중심을 축으로 돕니다.
        m_hitMarkerElement.style.rotate = new StyleRotate(
            new Rotate(new Angle(ResolveHitMarkerRollDegrees(), AngleUnit.Degree)));

        if (IsHitFeedbackPreview())
        {
            // 에디트 프리뷰: 페이드 타이머와 무관하게 선택한 색으로 상시 표시합니다.
            m_hitMarkerActiveColor = m_previewHeadshotColor ? m_hitMarkerColorHead : m_hitMarkerColorBody;
            m_hitMarkerElement.style.display = DisplayStyle.Flex;
            m_hitMarkerElement.style.opacity = 1.0f;
        }
        else if (m_hitMarkerTimer <= 0.0f)
        {
            HideElement(m_hitMarkerElement);
        }

        m_hitMarkerElement.MarkDirtyRepaint();
    }

    /// <summary>
    /// 킬 해골 요소의 위치·크기·텍스처를 잡습니다. 가시성은 페이드 타이머가 제어하므로, 타이머가 없으면 숨깁니다.
    /// </summary>
    /// <param name="center">파츠 배치 기준 앵커(0 = 패널 정중앙)입니다.</param>
    private void LayoutKillSkull(float center)
    {
        if (m_killSkullElement == null)
        {
            return;
        }

        if (!m_showKillSkull || m_killSkullTexture == null || m_killSkullSizePixels <= 0.0f)
        {
            HideElement(m_killSkullElement);
            m_killTimer = 0.0f;
            return;
        }

        float size = m_killSkullSizePixels;
        float position = center - size * 0.5f;

        m_killSkullElement.pickingMode = PickingMode.Ignore;
        m_killSkullElement.style.position = Position.Absolute;
        m_killSkullElement.style.left = position;
        m_killSkullElement.style.top = position;
        m_killSkullElement.style.width = size;
        m_killSkullElement.style.height = size;
        m_killSkullElement.style.backgroundColor = Color.clear;
        m_killSkullElement.style.backgroundImage = new StyleBackground(m_killSkullTexture);
        m_killSkullElement.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        m_killSkullElement.style.unityBackgroundImageTintColor = m_killSkullTint;

        if (IsHitFeedbackPreview())
        {
            // 에디트 프리뷰: 페이드 타이머와 무관하게 상시 표시합니다.
            m_killSkullElement.style.display = DisplayStyle.Flex;
            m_killSkullElement.style.opacity = 1.0f;
        }
        else if (m_killTimer <= 0.0f)
        {
            HideElement(m_killSkullElement);
        }
    }

    /// <summary>
    /// 에디트 모드 프리뷰에서 히트마커/킬 해골을 상시 표시하는 디버그 상태인지 여부입니다.
    /// </summary>
    private bool IsHitFeedbackPreview()
    {
        return !Application.isPlaying && m_editModePreview && m_previewHitFeedback;
    }

    /// <summary>
    /// 히트마커 4개 삼각형을 Painter2D로 그립니다. 각 삼각형은 바깥 쪽이 뾰족하고 중심 쪽 밑변이 넓은 X자 형태입니다.
    /// </summary>
    /// <remarks>
    /// 색상은 표시 시점(<see cref="ShowHitMarker"/>)에 몸샷/헤드샷으로 결정된 <see cref="m_hitMarkerActiveColor"/>를 씁니다.
    /// 정적 이미지 대신 프로시저럴로 그려 길이·중앙 공간·두께·색을 파라미터로 자유롭게 조절합니다.
    /// </remarks>
    private void OnGenerateHitMarker(MeshGenerationContext context)
    {
        VisualElement element = context.visualElement;
        float width = element.resolvedStyle.width;
        float height = element.resolvedStyle.height;
        float gap = Mathf.Max(0.0f, m_hitMarkerCenterGapPixels);
        float length = Mathf.Max(0.0f, ResolveActiveHitMarkerLengthPixels());
        float halfBase = Mathf.Max(0.0f, m_hitMarkerBaseLengthPixels) * 0.5f;
        if (length <= 0.0f || width <= 0.0f || height <= 0.0f)
        {
            return;
        }

        Vector2 markerCenter = new Vector2(width * 0.5f, height * 0.5f);
        Painter2D painter = context.painter2D;
        painter.fillColor = m_hitMarkerActiveColor;

        // 네 대각선(↖ ↗ ↙ ↘) 방향으로 삼각형을 배치합니다.
        Vector2[] diagonals =
        {
            new Vector2(-1.0f, -1.0f),
            new Vector2(1.0f, -1.0f),
            new Vector2(-1.0f, 1.0f),
            new Vector2(1.0f, 1.0f),
        };

        foreach (Vector2 raw in diagonals)
        {
            Vector2 dir = raw.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 apex = markerCenter + dir * (gap + length);      // 바깥 쪽 뾰족한 끝
            Vector2 inner = markerCenter + dir * gap;                // 중심 쪽 밑변 중심
            Vector2 baseA = inner + perp * halfBase;
            Vector2 baseB = inner - perp * halfBase;

            painter.BeginPath();
            painter.MoveTo(apex);
            painter.LineTo(baseA);
            painter.LineTo(baseB);
            painter.ClosePath();
            painter.Fill();
        }
    }

    /// <summary>
    /// 적중 시 히트마커를 표시합니다. 몸샷/헤드샷에 따라 색을 정하고 페이드아웃 타이머를 리셋합니다.
    /// </summary>
    /// <param name="headshot">헤드샷이면 <c>true</c>(헤드샷 색), 아니면 몸샷 색입니다.</param>
    public void ShowHitMarker(bool headshot)
    {
        ShowHitMarker(headshot, 0);
    }

    /// <summary>
    /// 히트마커를 표시하고, 이번 명중의 피해량으로 길이를 정합니다.
    /// </summary>
    /// <param name="headshot">약점에 맞았으면 <c>true</c>입니다. 색상만 바꿉니다.</param>
    /// <param name="damage">이번 명중으로 들어간 최종 피해량입니다. 0 이하이면 고정 길이를 씁니다.</param>
    /// <remarks>
    /// 길이 비례가 꺼져 있거나 피해량을 모르면 <see cref="m_hitMarkerLengthPixels"/>를 그대로 씁니다.
    /// 그래서 피해량을 넘기지 않는 기존 호출도 예전과 같은 크기로 동작합니다.
    /// </remarks>
    public void ShowHitMarker(bool headshot, int damage)
    {
        if (!m_showHitMarker || !CacheVisualElements() || m_hitMarkerElement == null)
        {
            return;
        }

        m_hitMarkerActiveLengthPixels = ResolveHitMarkerLengthPixels(damage);
        m_hitMarkerActiveColor = headshot ? m_hitMarkerColorHead : m_hitMarkerColorBody;

        // 발마다 기준 각도에서 ±범위만큼 새로 뽑습니다. 같은 그림이 반복되지 않게 해 타격감을 살립니다.
        // 꺼져 있으면 기준 각도만 씁니다(기준 각도 자체는 토글과 무관하게 유지).
        float range = m_enableHitMarkerRandomRoll ? Mathf.Max(0.0f, m_hitMarkerRollRandomRangeDegrees) : 0.0f;
        m_hitMarkerRollDegrees = m_hitMarkerRollBaseDegrees + (range > 0.0f ? Random.Range(-range, range) : 0.0f);

        m_hitMarkerTimer = Mathf.Max(0.0001f, m_hitMarkerFadeDuration);
        m_hitMarkerElement.style.display = DisplayStyle.Flex;
        m_hitMarkerElement.style.opacity = 1.0f;
        LayoutHitMarker(0.0f);
        m_hitMarkerElement.MarkDirtyRepaint();
    }

    /// <summary>
    /// 처치 시 중앙에 해골을 표시하고, 유지 후 페이드아웃되도록 타이머를 리셋합니다.
    /// </summary>
    public void ShowKill()
    {
        if (!m_showKillSkull || m_killSkullTexture == null || !CacheVisualElements() || m_killSkullElement == null)
        {
            return;
        }

        m_killTimer = Mathf.Max(0.0001f, m_killSkullHoldDuration) + Mathf.Max(0.0f, m_killSkullFadeDuration);
        m_killSkullElement.style.display = DisplayStyle.Flex;
        m_killSkullElement.style.opacity = 1.0f;
        LayoutKillSkull(0.0f);
    }

    /// <summary>
    /// VisualElement에 벡터 이미지 배경을 절대 배치로 적용합니다(크기에 맞춰 축소, 배경색 투명).
    /// </summary>
    private static void ApplyTextureImage(VisualElement element, Texture2D image, float left, float top, float size)
    {
        size = Mathf.Max(0.0f, size);

        element.pickingMode = PickingMode.Ignore;
        element.style.display = DisplayStyle.Flex;
        element.style.position = Position.Absolute;
        element.style.left = left;
        element.style.top = top;
        element.style.width = size;
        element.style.height = size;
        element.style.backgroundColor = Color.clear;
        element.style.backgroundImage = new StyleBackground(image);
        element.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
    }

    /// <summary>
    /// 현재 벌어짐 간격을 반영한 상하좌우 십자 팔을 배치합니다.
    /// </summary>
    private void ApplySubCross(float center, float gapPixels, float cornerRadius)
    {
        float gap = Mathf.Max(0.0f, gapPixels);
        float width = Mathf.Max(0.0f, m_subWidthPixels);
        float thickness = Mathf.Max(0.0f, m_subThicknessPixels);
        float radius = Mathf.Max(0.0f, cornerRadius);

        ApplyHorizontalArm(m_leftElement, m_leftStrokeElement, center - gap - width, center, width, thickness, radius);
        ApplyHorizontalArm(m_rightElement, m_rightStrokeElement, center + gap, center, width, thickness, radius);
        ApplyVerticalArm(m_topElement, m_topStrokeElement, center, center - gap - width, thickness, width, radius);
        ApplyVerticalArm(m_bottomElement, m_bottomStrokeElement, center, center + gap, thickness, width, radius);
    }

    /// <summary>
    /// 앵커 중심에 중앙 표시(Main)를 배치합니다. 형태가 None이거나 크기가 0이면 숨깁니다.
    /// </summary>
    /// <param name="center">파츠 배치 기준이 되는 앵커 좌표(0 = 패널 정중앙)입니다.</param>
    /// <remarks>Dot 형태이면 크기의 절반을 모서리 반지름으로 주어 원형으로 렌더링합니다.</remarks>
    private void ApplyCenter(float center)
    {
        if (m_mainShape == MainShape.None)
        {
            m_centerElement.style.display = DisplayStyle.None;
            HideElement(m_mainStrokeElement);
            return;
        }

        switch (m_mainShape)
        {
            case MainShape.Dot:
                ApplyDot(
                    m_centerElement,
                    m_mainStrokeElement,
                    center,
                    center,
                    m_mainSizePixels,
                    m_mainColor,
                    m_mainStrokeThicknessPixels,
                    m_mainStrokeColor);
                break;

            case MainShape.Ring:
                ApplyRing(
                    m_centerElement,
                    m_mainStrokeElement,
                    center,
                    m_mainRingSizePixels,
                    m_mainRingThicknessPixels,
                    m_mainColor,
                    m_mainStrokeThicknessPixels,
                    m_mainStrokeColor);
                break;
        }
    }

    /// <summary>
    /// 앵커 중심에 원형 점을 배치합니다.
    /// </summary>
    private static void ApplyDot(
        VisualElement colorElement,
        VisualElement strokeElement,
        float centerX,
        float centerY,
        float size,
        Color color,
        float strokeThickness,
        Color strokeColor)
    {
        size = Mathf.Max(0.0f, size);
        strokeThickness = Mathf.Max(0.0f, strokeThickness);

        if (size <= 0.0f)
        {
            HideElement(colorElement);
            HideElement(strokeElement);
            return;
        }

        ApplyFilledCircle(colorElement, strokeElement, centerX, centerY, size, color, strokeThickness, strokeColor);
    }

    /// <summary>
    /// 현재 벌어짐 간격을 반영한 상하좌우 보조 점을 배치합니다.
    /// </summary>
    private void ApplySubDots(float center, float gapPixels)
    {
        float subSize = Mathf.Max(0.0f, m_subSizePixels);
        float offset = Mathf.Max(0.0f, gapPixels) + subSize * 0.5f;
        ApplyDot(m_leftElement, m_leftStrokeElement, center - offset, center, subSize, m_subColor, m_subStrokeThicknessPixels, m_subStrokeColor);
        ApplyDot(m_rightElement, m_rightStrokeElement, center + offset, center, subSize, m_subColor, m_subStrokeThicknessPixels, m_subStrokeColor);
        ApplyDot(m_topElement, m_topStrokeElement, center, center - offset, subSize, m_subColor, m_subStrokeThicknessPixels, m_subStrokeColor);
        ApplyDot(m_bottomElement, m_bottomStrokeElement, center, center + offset, subSize, m_subColor, m_subStrokeThicknessPixels, m_subStrokeColor);
    }

    /// <summary>
    /// 현재 벌어짐 간격을 반영한 보조 링을 배치합니다.
    /// </summary>
    private void ApplySubRing(float center, float gapPixels)
    {
        float diameter = Mathf.Max(0.0f, m_subRingSizePixels) + Mathf.Max(0.0f, gapPixels) * 2.0f;
        ApplyRing(
            m_subShapeElement,
            m_subStrokeElement,
            center,
            diameter,
            m_subRingThicknessPixels,
            m_subColor,
            m_subStrokeThicknessPixels,
            m_subStrokeColor);
    }

    /// <summary>
    /// 앵커 중심에 색상 링과 스트로크 링을 분리해 배치합니다.
    /// 기준 지름은 가장 얇은 링의 바깥 지름으로 취급하고, 두께는 안쪽 지름을 유지한 채 바깥쪽으로 확장합니다.
    /// </summary>
    private static void ApplyRing(
        VisualElement colorElement,
        VisualElement strokeElement,
        float center,
        float diameter,
        float thickness,
        Color color,
        float strokeThickness,
        Color strokeColor)
    {
        diameter = Mathf.Max(0.0f, diameter);
        thickness = Mathf.Max(0.0f, thickness);
        strokeThickness = Mathf.Max(0.0f, strokeThickness);

        if (diameter <= 0.0f || thickness <= 0.0f)
        {
            HideElement(colorElement);
            HideElement(strokeElement);
            return;
        }

        float renderedThickness = Mathf.Max(RingReferenceThicknessPixels, thickness);
        float innerDiameter = CalculateReferenceInnerDiameter(diameter);
        float colorOuterDiameter = CalculateOutwardRingOuterDiameter(innerDiameter, renderedThickness);
        float clampedStrokeThickness = CalculateClampedRingStrokeThickness(innerDiameter, strokeThickness);

        if (clampedStrokeThickness > 0.0f)
        {
            float strokeInnerDiameter = Mathf.Max(0.0f, innerDiameter - clampedStrokeThickness * 2.0f);
            float strokeOuterDiameter = colorOuterDiameter + clampedStrokeThickness * 2.0f;
            float strokeRingThickness = (strokeOuterDiameter - strokeInnerDiameter) * 0.5f;
            ApplySingleRing(strokeElement, center, strokeOuterDiameter, strokeRingThickness, strokeColor);
        }
        else
        {
            HideElement(strokeElement);
        }

        ApplySingleRing(colorElement, center, colorOuterDiameter, renderedThickness, color);
        PlaceStrokeBehindColor(strokeElement, colorElement);
    }

    /// <summary>
    /// 채움 원과 스트로크 원을 분리해 배치합니다. 스트로크는 기준 원 바깥쪽으로만 확장됩니다.
    /// </summary>
    private static void ApplyFilledCircle(
        VisualElement colorElement,
        VisualElement strokeElement,
        float centerX,
        float centerY,
        float diameter,
        Color color,
        float strokeThickness,
        Color strokeColor)
    {
        diameter = Mathf.Max(0.0f, diameter);
        strokeThickness = Mathf.Max(0.0f, strokeThickness);

        if (strokeThickness > 0.0f)
        {
            float strokeDiameter = diameter + strokeThickness * 2.0f;
            ApplySolidCircle(strokeElement, centerX, centerY, strokeDiameter, strokeColor);
        }
        else
        {
            HideElement(strokeElement);
        }

        ApplySolidCircle(colorElement, centerX, centerY, diameter, color);
        PlaceStrokeBehindColor(strokeElement, colorElement);
    }

    /// <summary>
    /// 가장 얇은 링 기준 바깥 지름에서 유지해야 할 안쪽 지름을 계산합니다.
    /// </summary>
    private static float CalculateReferenceInnerDiameter(float referenceOuterDiameter)
    {
        return Mathf.Max(0.0f, referenceOuterDiameter - RingReferenceThicknessPixels * 2.0f);
    }

    /// <summary>
    /// 링 내부 스트로크가 중앙에서 서로 맞닿으면 더 커지지 않도록 스트로크 두께를 제한합니다.
    /// </summary>
    private static float CalculateClampedRingStrokeThickness(float innerDiameter, float strokeThickness)
    {
        float maxInnerStrokeThickness = Mathf.Max(0.0f, innerDiameter * 0.5f);
        return Mathf.Clamp(Mathf.Max(0.0f, strokeThickness), 0.0f, maxInnerStrokeThickness);
    }

    /// <summary>
    /// 안쪽 지름을 유지한 채 링 두께가 바깥쪽으로만 커진 최종 바깥 지름을 계산합니다.
    /// </summary>
    private static float CalculateOutwardRingOuterDiameter(float innerDiameter, float thickness)
    {
        return innerDiameter + Mathf.Max(0.0f, thickness) * 2.0f;
    }

    /// <summary>
    /// 앵커 중심에 단일 색상 링을 배치합니다.
    /// </summary>
    private static void ApplySingleRing(VisualElement element, float center, float diameter, float thickness, Color color)
    {
        diameter = Mathf.Max(0.0f, diameter);
        thickness = Mathf.Max(0.0f, thickness);

        ApplyBox(element, center - diameter * 0.5f, center - diameter * 0.5f, diameter, diameter, Color.clear);
        SetCornerRadius(element, diameter * 0.5f);
        SetBorder(element, thickness, color);
    }

    /// <summary>
    /// 앵커 중심에 채움 원을 배치합니다. 이전 Ring border 스타일이 남지 않도록 border를 초기화합니다.
    /// </summary>
    private static void ApplySolidCircle(VisualElement element, float centerX, float centerY, float diameter, Color color)
    {
        diameter = Mathf.Max(0.0f, diameter);

        ApplyBox(element, centerX - diameter * 0.5f, centerY - diameter * 0.5f, diameter, diameter, color);
        SetCornerRadius(element, diameter * 0.5f);
        SetBorder(element, 0.0f, Color.clear);
    }

    /// <summary>
    /// 상하좌우 보조 표시와 각 스트로크 표시를 모두 숨깁니다.
    /// </summary>
    private void HideSubDirectionElements()
    {
        HideElement(m_leftElement);
        HideElement(m_leftStrokeElement);
        HideElement(m_rightElement);
        HideElement(m_rightStrokeElement);
        HideElement(m_topElement);
        HideElement(m_topStrokeElement);
        HideElement(m_bottomElement);
        HideElement(m_bottomStrokeElement);
    }

    /// <summary>
    /// 보조 원형 표시와 스트로크 표시를 숨깁니다.
    /// </summary>
    private void HideSubShapeElements()
    {
        HideElement(m_subShapeElement);
        HideElement(m_subStrokeElement);
    }

    /// <summary>
    /// 좌우 팔을 배치합니다. 두께를 세로 중심에 맞춰 정렬한 뒤 가로 방향으로 길이를 적용합니다.
    /// </summary>
    /// <param name="element">배치할 팔 VisualElement입니다.</param>
    /// <param name="left">팔의 왼쪽 시작 좌표(픽셀)입니다.</param>
    /// <param name="center">세로 정렬 기준이 되는 중심 좌표(픽셀)입니다.</param>
    /// <param name="length">팔의 가로 길이(픽셀)입니다.</param>
    /// <param name="thickness">팔의 세로 두께(픽셀)입니다.</param>
    private void ApplyHorizontalArm(
        VisualElement element,
        VisualElement strokeElement,
        float left,
        float center,
        float length,
        float thickness,
        float cornerRadius)
    {
        ApplyArm(element, strokeElement, left, center - thickness * 0.5f, length, thickness, cornerRadius);
    }

    /// <summary>
    /// 상하 팔을 배치합니다. 두께를 가로 중심에 맞춰 정렬한 뒤 세로 방향으로 길이를 적용합니다.
    /// </summary>
    /// <param name="element">배치할 팔 VisualElement입니다.</param>
    /// <param name="center">가로 정렬 기준이 되는 중심 좌표(픽셀)입니다.</param>
    /// <param name="top">팔의 위쪽 시작 좌표(픽셀)입니다.</param>
    /// <param name="thickness">팔의 가로 두께(픽셀)입니다.</param>
    /// <param name="length">팔의 세로 길이(픽셀)입니다.</param>
    private void ApplyVerticalArm(
        VisualElement element,
        VisualElement strokeElement,
        float center,
        float top,
        float thickness,
        float length,
        float cornerRadius)
    {
        ApplyArm(element, strokeElement, center - thickness * 0.5f, top, thickness, length, cornerRadius);
    }

    /// <summary>
    /// 팔 VisualElement의 위치, 크기, 채움 색상, 모서리, 스트로크를 절대 배치로 적용합니다.
    /// </summary>
    /// <param name="element">배치할 팔 VisualElement입니다.</param>
    /// <param name="left">왼쪽 좌표(픽셀)입니다.</param>
    /// <param name="top">위쪽 좌표(픽셀)입니다.</param>
    /// <param name="width">가로 크기(픽셀)입니다. 0 이하이면 숨깁니다.</param>
    /// <param name="height">세로 크기(픽셀)입니다. 0 이하이면 숨깁니다.</param>
    private void ApplyArm(
        VisualElement element,
        VisualElement strokeElement,
        float left,
        float top,
        float width,
        float height,
        float cornerRadius)
    {
        width = Mathf.Max(0.0f, width);
        height = Mathf.Max(0.0f, height);
        float strokeThickness = Mathf.Max(0.0f, m_subStrokeThicknessPixels);
        cornerRadius = Mathf.Max(0.0f, cornerRadius);

        if (width <= 0.0f || height <= 0.0f)
        {
            HideElement(element);
            HideElement(strokeElement);
            return;
        }

        if (strokeThickness > 0.0f)
        {
            float strokeLeft = left - strokeThickness;
            float strokeTop = top - strokeThickness;
            float strokeWidth = width + strokeThickness * 2.0f;
            float strokeHeight = height + strokeThickness * 2.0f;
            ApplyBox(strokeElement, strokeLeft, strokeTop, strokeWidth, strokeHeight, m_subStrokeColor);
            SetCornerRadius(strokeElement, cornerRadius + strokeThickness);
            SetBorder(strokeElement, 0.0f, Color.clear);
        }
        else
        {
            HideElement(strokeElement);
        }

        ApplyBox(element, left, top, width, height, m_subColor);
        SetCornerRadius(element, cornerRadius);
        SetBorder(element, 0.0f, Color.clear);
        PlaceStrokeBehindColor(strokeElement, element);
    }

    /// <summary>
    /// 스트로크 element가 본체 색상 element를 덮지 않도록 같은 부모 안에서 렌더 순서를 고정합니다.
    /// </summary>
    private static void PlaceStrokeBehindColor(VisualElement strokeElement, VisualElement colorElement)
    {
        if (strokeElement == null || colorElement == null || strokeElement.parent != colorElement.parent)
        {
            return;
        }

        strokeElement.SendToBack();
        colorElement.BringToFront();
    }

    /// <summary>
    /// VisualElement의 기본 절대 배치, 크기, 채움 색상을 적용합니다.
    /// </summary>
    private static void ApplyBox(VisualElement element, float left, float top, float width, float height, Color color)
    {
        width = Mathf.Max(0.0f, width);
        height = Mathf.Max(0.0f, height);

        element.pickingMode = PickingMode.Ignore;
        element.style.display = width <= 0.0f || height <= 0.0f ? DisplayStyle.None : DisplayStyle.Flex;
        element.style.position = Position.Absolute;
        element.style.left = left;
        element.style.top = top;
        element.style.width = width;
        element.style.height = height;
        element.style.backgroundColor = color;
    }

    /// <summary>
    /// VisualElement를 화면에서 숨깁니다.
    /// </summary>
    /// <param name="element">숨길 VisualElement입니다. <c>null</c>이면 아무 것도 하지 않습니다.</param>
    private static void HideElement(VisualElement element)
    {
        if (element != null)
        {
            element.style.display = DisplayStyle.None;
        }
    }

    /// <summary>
    /// VisualElement 네 모서리에 동일한 둥글기 반지름을 적용합니다.
    /// </summary>
    /// <param name="element">모서리를 설정할 VisualElement입니다.</param>
    /// <param name="radius">모든 모서리에 적용할 반지름(픽셀)입니다.</param>
    private static void SetCornerRadius(VisualElement element, float radius)
    {
        radius = Mathf.Max(0.0f, radius);

        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }

    /// <summary>
    /// VisualElement 네 변의 스트로크 두께와 색상을 동일하게 적용합니다.
    /// </summary>
    /// <param name="element">스트로크를 설정할 VisualElement입니다.</param>
    /// <param name="width">모든 변에 적용할 스트로크 두께(픽셀)입니다.</param>
    /// <param name="color">모든 변에 적용할 스트로크 색상입니다.</param>
    private static void SetBorder(VisualElement element, float width, Color color)
    {
        width = Mathf.Max(0.0f, width);

        element.style.borderTopWidth = width;
        element.style.borderRightWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftWidth = width;
        element.style.borderTopColor = color;
        element.style.borderRightColor = color;
        element.style.borderBottomColor = color;
        element.style.borderLeftColor = color;
    }
}
