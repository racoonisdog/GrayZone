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
    private const float RingReferenceThicknessPixels = 1.0f;

    /// <summary>중앙 표시(Main) 형태입니다.</summary>
    private enum MainShape
    {
        /// <summary>중앙 표시를 그리지 않습니다.</summary>
        None = 0,

        /// <summary>둥근 점(Dot)으로 표시합니다.</summary>
        Dot = 1,

        /// <summary>중앙 원형 링으로 표시합니다.</summary>
        Ring = 2,
    }

    /// <summary>보조 표시(Sub, 상하좌우 팔) 형태입니다.</summary>
    private enum SubShape
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
    [Tooltip("켜면 무기 탄퍼짐 방사각을 조준선 벌어짐으로 표시합니다. 끄면 Center Space만 유지합니다.")]
    [SerializeField] private bool m_useSpreadAccuracy = true;

    [Tooltip("켜면 스프레드 도(degree)를 카메라 FOV 기준 화면 픽셀로 환산합니다. 끄면 Spread Scale을 픽셀/도 값처럼 사용합니다.")]
    [SerializeField] private bool m_projectSpreadByCameraFov = true;

    [Tooltip("탄퍼짐 환산값에 곱할 배율입니다. FOV 환산을 끄면 픽셀/도처럼 작동합니다.")]
    [SerializeField] private float m_spreadScale = 1.0f;

    [Tooltip("중심에서 팔 안쪽까지 벌어질 수 있는 최대 간격(픽셀)입니다.")]
    [SerializeField] private float m_maxGapPixels = 220.0f;

    [Tooltip("조준선이 목표 벌어짐을 따라가는 보간 속도입니다. 0 이하이면 실제 스프레드 값을 즉시 반영합니다.")]
    [SerializeField] private float m_lerpSpeed = 0.0f;

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

    [Tooltip("탄퍼짐이 0일 때 중심에서 상하좌우 팔 안쪽까지의 통일 간격(픽셀)입니다.")]
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

    [Foldout("Debug")]
    [Tooltip("(디버그) 켜면 에디트 모드(비플레이)에서도 조준선을 미리 렌더링합니다. 프리뷰 전용이며 게임 로직엔 영향이 없습니다. [ExecuteAlways]와 함께 동작합니다.")]
    [SerializeField] private bool m_editModePreview = false;

    [Tooltip("(디버그) 마지막 상태 전환(자유시점/힙파이어/ADS) 시점에 캡처된 전투 스탠스입니다.")]
    [ReadOnly][SerializeField] private string m_debugStance = "Free";

    // (디버그) 값 복사가 아니라 전환 시점에 연결된 라이브 소스 포인터를 통해 현재값을 읽는 읽기전용 게터입니다.
    // 별도 매 프레임 업데이트 없이 인스펙터 리페인트 때마다 포인터로 현재값을 당겨옵니다.
    [ShowInInspector] private float DebugSpreadDegrees => m_debugSpreadSource != null ? m_debugSpreadSource() : 0.0f;
    [ShowInInspector] private float DebugCameraFovDegrees => m_debugFovSource != null ? m_debugFovSource() : 0.0f;
    [ShowInInspector] private float DebugSpreadGapPixels => CalculateSpreadGapPixels(DebugSpreadDegrees, DebugCameraFovDegrees);
    [ShowInInspector] private float DebugCurrentGapPixels => CalculateTargetGapPixels(DebugSpreadDegrees, DebugCameraFovDegrees);

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
    private float m_currentGapPixels;
    private float m_lastSpreadDegrees;
    private float m_lastCameraFovDegrees = 60.0f;

    /// <summary>탄퍼짐 정확도 표시 여부입니다.</summary>
    public bool UseSpreadAccuracy => m_useSpreadAccuracy;

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
        SetSpread(m_lastSpreadDegrees, m_lastCameraFovDegrees, true);
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
            SetSpread(m_lastSpreadDegrees, m_lastCameraFovDegrees, true);
            return;
        }

        // 에디트 모드: 디버그 프리뷰 토글 상태에 맞춰 즉시 표시/숨김을 반영합니다.
        if (m_editModePreview)
        {
            CacheVisualElements();
            SetVisible(true);
            SetSpread(m_lastSpreadDegrees, m_lastCameraFovDegrees, true);
        }
        else
        {
            SetVisible(false);
        }
    }

    /// <summary>
    /// 조준선 표시 여부를 설정합니다.
    /// </summary>
    /// <param name="visible">표시하려면 <c>true</c>, 숨기려면 <c>false</c>입니다.</param>
    public void SetVisible(bool visible)
    {
        if (!CacheVisualElements())
        {
            return;
        }

        m_rootElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>
    /// 현재 무기 탄퍼짐 방사각과 카메라 FOV를 받아 조준선 벌어짐을 갱신합니다.
    /// </summary>
    /// <param name="spreadDegrees">현재 무기 탄퍼짐 방사각(도)입니다.</param>
    /// <param name="cameraFovDegrees">현재 조준 카메라 FOV(도)입니다.</param>
    /// <param name="snap">true면 보간 없이 즉시 반영합니다.</param>
    public void SetSpread(float spreadDegrees, float cameraFovDegrees, bool snap)
    {
        if (!CacheVisualElements())
        {
            return;
        }

        ClampSettings();

        m_lastSpreadDegrees = Mathf.Max(0.0f, spreadDegrees);
        m_lastCameraFovDegrees = Mathf.Max(1.0f, cameraFovDegrees);

        float targetGap = CalculateTargetGapPixels(m_lastSpreadDegrees, m_lastCameraFovDegrees);
        float lerpSpeed = Mathf.Max(0.0f, m_lerpSpeed);
        m_currentGapPixels = snap || lerpSpeed <= 0.0f
            ? targetGap
            : Mathf.Lerp(m_currentGapPixels, targetGap, Time.deltaTime * lerpSpeed);

        ApplyLayout(m_currentGapPixels);
    }

    /// <summary>
    /// 전투 스탠스(자유시점/힙파이어/ADS) 전환 시점에 디버그 라이브 소스 포인터를 연결합니다.
    /// </summary>
    /// <param name="stance">스탠스 이름(디버그 표시용)입니다.</param>
    /// <param name="spreadSource">현재 방사각(도)을 반환하는 라이브 소스입니다. 값을 복사하지 않고 이 포인터로 읽습니다.</param>
    /// <param name="fovSource">해당 스탠스에서 사용하는 카메라 FOV(도)를 반환하는 라이브 소스입니다.</param>
    /// <remarks>
    /// 값 복사(스냅샷)가 아니라 소스 포인터를 연결만 합니다. 전환 때 한 번 호출하면 이후 매 프레임 업데이트 없이도
    /// 인스펙터 게터(<see cref="DebugSpreadDegrees"/> 등)가 이 포인터로 현재값(예: 연사 중 누적되는 방사각)을 읽습니다.
    /// </remarks>
    public void BindSpreadDebug(string stance, System.Func<float> spreadSource, System.Func<float> fovSource)
    {
        m_debugStance = stance;
        m_debugSpreadSource = spreadSource;
        m_debugFovSource = fovSource;
    }

    /// <summary>
    /// 탄퍼짐 표시를 기본 간격 상태로 되돌립니다.
    /// </summary>
    public void ResetSpread()
    {
        m_lastSpreadDegrees = 0.0f;
        SetSpread(0.0f, m_lastCameraFovDegrees, true);
    }

    /// <summary>
    /// 탄퍼짐 정확도 표시 토글을 런타임에 바꿉니다.
    /// </summary>
    /// <param name="enabled">탄퍼짐 벌어짐을 표시하려면 <c>true</c>, Center Space만 유지하려면 <c>false</c>입니다.</param>
    public void SetSpreadAccuracyEnabled(bool enabled)
    {
        m_useSpreadAccuracy = enabled;
        SetSpread(m_lastSpreadDegrees, m_lastCameraFovDegrees, true);
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
        m_spreadScale = Mathf.Max(0.0f, m_spreadScale);
        m_maxGapPixels = Mathf.Max(m_centerSpacePixels, m_maxGapPixels);
        m_lerpSpeed = Mathf.Max(0.0f, m_lerpSpeed);
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
    }

    /// <summary>
    /// 무기 탄퍼짐 방사각과 카메라 FOV로부터 이번 프레임 목표 벌어짐 간격(픽셀)을 계산합니다.
    /// </summary>
    /// <param name="spreadDegrees">현재 무기 탄퍼짐 방사각(도)입니다.</param>
    /// <param name="cameraFovDegrees">현재 조준 카메라 FOV(도)입니다.</param>
    /// <returns>Center Space에 탄퍼짐 환산값을 더한 뒤 최대 간격으로 클램프한 목표 간격(픽셀)입니다.</returns>
    /// <remarks>탄퍼짐 표시가 꺼져 있으면 Center Space만 반환합니다.</remarks>
    private float CalculateTargetGapPixels(float spreadDegrees, float cameraFovDegrees)
    {
        float spreadGap = CalculateSpreadGapPixels(spreadDegrees, cameraFovDegrees);
        return Mathf.Clamp(m_centerSpacePixels + spreadGap, 0.0f, m_maxGapPixels);
    }

    /// <summary>
    /// 탄퍼짐이 벌어짐 간격에 더할 기여분(픽셀)을 계산합니다. 중심 간격과 최대 간격 클램프는 포함하지 않습니다.
    /// </summary>
    /// <param name="spreadDegrees">현재 무기 탄퍼짐 방사각(도)입니다.</param>
    /// <param name="cameraFovDegrees">현재 조준 카메라 FOV(도)입니다.</param>
    /// <returns>FOV 투영(켜짐) 또는 도 값(꺼짐)에 Spread Scale을 곱한 기여분입니다. 탄퍼짐 표시가 꺼져 있으면 0입니다.</returns>
    /// <remarks><see cref="CalculateTargetGapPixels"/>와 디버그 표시가 동일한 값을 쓰도록 이 헬퍼를 공유합니다.</remarks>
    private float CalculateSpreadGapPixels(float spreadDegrees, float cameraFovDegrees)
    {
        if (!m_useSpreadAccuracy)
        {
            return 0.0f;
        }

        float spreadGap = m_projectSpreadByCameraFov
            ? CalculateProjectedSpreadPixels(spreadDegrees, cameraFovDegrees)
            : spreadDegrees;

        return spreadGap * m_spreadScale;
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
