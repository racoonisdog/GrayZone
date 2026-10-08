using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// 현재 조작 캐릭터가 피해를 받았을 때 공격이 들어온 월드 방향을 화면 중앙 주변에 표시합니다.
/// </summary>
/// <remarks>
/// 별도 이미지 리소스 없이 UI Toolkit의 <see cref="Painter2D"/>로 마커를 그립니다.
/// 피격 순간의 공격자 위치는 고정하고, 표시 중에는 카메라 방향을 매 프레임 다시 비교하므로
/// 플레이어가 마우스로 시점을 돌리면 마커가 같은 월드 방향을 계속 가리킵니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class PlayerDamageDirectionIndicator : MonoBehaviour
{
    private const string IndicatorElementName = "PlayerDamageDirectionIndicator";
    private const string CrosshairRootElementName = "CrosshairRoot";

    [Header("References")]
    [Tooltip("표시를 추가할 조준선 UI 문서입니다. 비워두면 같은 GameObject에서 찾습니다.")]
    [SerializeField] private UIDocument m_document;

    [Tooltip("방향 계산에 사용할 게임 카메라입니다. 비워두면 Main Camera를 사용합니다.")]
    [SerializeField] private Camera m_camera;

    [Header("Layout")]
    [Tooltip("화면 중앙에서 피격 마커까지의 거리(픽셀)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_radiusPixels = 150.0f;

    [Tooltip("피격 방향을 덮는 원호 각도입니다. 값이 클수록 링 조각이 길어집니다.")]
    [Range(5.0f, 120.0f)]
    [SerializeField] private float m_arcAngleDegrees = 58.0f;

    [Tooltip("환형 부채꼴의 두께(픽셀)입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_arcThicknessPixels = 11.0f;

    [Tooltip("배경과 구분하기 위해 마커 바깥에 추가할 어두운 외곽선 두께(픽셀)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_outlineWidthPixels = 3.0f;

    [Header("Appearance")]
    [SerializeField] private Color m_color = new Color32(235, 70, 70, 245);
    [SerializeField] private Color m_outlineColor = new Color32(20, 20, 20, 205);

    [Header("Timing")]
    [Tooltip("피격 직후 완전히 보이는 시간(초)입니다. 0이면 피격 순간부터 바로 페이드아웃합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_holdDuration = 0.0f;

    [Tooltip("완전 표시가 끝난 뒤 서서히 사라지는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_fadeDuration = 0.8f;

    private static bool s_sceneHookRegistered;

    private VisualElement m_rootElement;
    private VisualElement m_indicatorElement;
    private PlayerbleUnitData m_controlledPlayer;
    private PlayerHealth m_controlledHealth;
    private Vector3 m_damageOrigin;
    private float m_elapsed;
    private bool m_isVisible;

    /// <summary>
    /// 조준선 HUD가 배치된 씬에서는 별도 씬 배선 없이 이 컴포넌트를 자동으로 추가합니다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachOnStartup()
    {
        AttachToLoadedScene();

        if (s_sceneHookRegistered)
        {
            return;
        }

        s_sceneHookRegistered = true;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AttachToLoadedScene();
    }

    private static void AttachToLoadedScene()
    {
        CrosshairController crosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        if (crosshair != null && crosshair.GetComponent<PlayerDamageDirectionIndicator>() == null)
        {
            crosshair.gameObject.AddComponent<PlayerDamageDirectionIndicator>();
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheVisualElement();
        RefreshControlledPlayer();
    }

    private void OnDisable()
    {
        BindHealth(null, null);
        HideIndicator();
    }

    private void OnDestroy()
    {
        if (m_indicatorElement != null)
        {
            m_indicatorElement.generateVisualContent -= GenerateIndicatorVisual;
        }
    }

    private void OnValidate()
    {
        m_radiusPixels = Mathf.Max(0.0f, m_radiusPixels);
        m_arcAngleDegrees = Mathf.Clamp(m_arcAngleDegrees, 5.0f, 120.0f);
        m_arcThicknessPixels = Mathf.Max(1.0f, m_arcThicknessPixels);
        m_outlineWidthPixels = Mathf.Max(0.0f, m_outlineWidthPixels);
        m_holdDuration = Mathf.Max(0.0f, m_holdDuration);
        m_fadeDuration = Mathf.Max(0.01f, m_fadeDuration);

        m_indicatorElement?.MarkDirtyRepaint();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RefreshControlledPlayer();

        if (m_indicatorElement == null && !CacheVisualElement())
        {
            return;
        }

        if (!m_isVisible)
        {
            return;
        }

        UpdateIndicatorLayout();
        UpdateIndicatorFade();
    }

    private void ResolveReferences()
    {
        if (m_document == null)
        {
            m_document = GetComponent<UIDocument>();
        }

        if (m_camera == null)
        {
            m_camera = Camera.main;
        }
    }

    private bool CacheVisualElement()
    {
        ResolveReferences();
        if (m_document == null || m_document.rootVisualElement == null)
        {
            return false;
        }

        VisualElement documentRoot = m_document.rootVisualElement;
        m_rootElement = documentRoot.Q<VisualElement>(CrosshairRootElementName) ?? documentRoot;

        m_indicatorElement = m_rootElement.Q<VisualElement>(IndicatorElementName);
        if (m_indicatorElement == null)
        {
            m_indicatorElement = new VisualElement { name = IndicatorElementName };
            m_rootElement.Add(m_indicatorElement);
        }

        m_indicatorElement.pickingMode = PickingMode.Ignore;
        m_indicatorElement.style.position = Position.Absolute;
        m_indicatorElement.style.backgroundColor = Color.clear;
        m_indicatorElement.generateVisualContent -= GenerateIndicatorVisual;
        m_indicatorElement.generateVisualContent += GenerateIndicatorVisual;

        if (!m_isVisible)
        {
            m_indicatorElement.style.display = DisplayStyle.None;
        }

        return true;
    }

    private void RefreshControlledPlayer()
    {
        SquadManager squadManager = SquadManager.Instance;
        PlayerbleUnitData nextPlayer = squadManager != null
            ? SquadHudSlotOrder.ResolveControlled(squadManager.PlayerDataSources)
            : null;

        if (nextPlayer == m_controlledPlayer)
        {
            return;
        }

        PlayerHealth nextHealth = nextPlayer != null ? nextPlayer.GetComponent<PlayerHealth>() : null;
        BindHealth(nextPlayer, nextHealth);
        HideIndicator();
    }

    private void BindHealth(PlayerbleUnitData player, PlayerHealth health)
    {
        if (m_controlledHealth != null)
        {
            m_controlledHealth.OnDamaged -= HandlePlayerDamaged;
        }

        m_controlledPlayer = player;
        m_controlledHealth = health;

        if (m_controlledHealth != null)
        {
            m_controlledHealth.OnDamaged += HandlePlayerDamaged;
        }
    }

    private void HandlePlayerDamaged(float damage, GameObject attacker)
    {
        if (damage <= 0.0f || attacker == null)
        {
            return;
        }

        EnemyController enemy = attacker.GetComponentInParent<EnemyController>();
        Transform source = enemy != null ? enemy.transform : attacker.transform;
        ShowFromWorldPosition(source.position);
    }

    /// <summary>
    /// 지정한 월드 위치에서 피해가 들어온 것처럼 마커를 표시합니다.
    /// 피격 이벤트 외의 공격도 같은 표시를 써야 할 때 사용할 수 있습니다.
    /// </summary>
    public void ShowFromWorldPosition(Vector3 worldPosition)
    {
        if (m_controlledPlayer == null)
        {
            return;
        }

        m_damageOrigin = worldPosition;
        m_elapsed = 0.0f;
        m_isVisible = true;

        if (m_indicatorElement == null && !CacheVisualElement())
        {
            return;
        }

        m_indicatorElement.style.display = DisplayStyle.Flex;
        m_indicatorElement.style.opacity = 1.0f;
        UpdateIndicatorLayout();
    }

    private void UpdateIndicatorLayout()
    {
        if (m_indicatorElement == null || m_rootElement == null || m_controlledPlayer == null)
        {
            return;
        }

        if (m_camera == null)
        {
            m_camera = Camera.main;
            if (m_camera == null)
            {
                return;
            }
        }

        Vector3 direction = Vector3.ProjectOnPlane(
            m_damageOrigin - m_controlledPlayer.transform.position,
            Vector3.up);
        Vector3 cameraForward = Vector3.ProjectOnPlane(m_camera.transform.forward, Vector3.up);

        if (direction.sqrMagnitude <= 0.0001f || cameraForward.sqrMagnitude <= 0.0001f)
        {
            HideIndicator();
            return;
        }

        float angle = Vector3.SignedAngle(cameraForward.normalized, direction.normalized, Vector3.up);
        float canvasSize = Mathf.Max(
            1.0f,
            (m_radiusPixels + m_arcThicknessPixels + m_outlineWidthPixels) * 2.0f);

        float rootWidth = m_rootElement.resolvedStyle.width;
        float rootHeight = m_rootElement.resolvedStyle.height;
        if (float.IsNaN(rootWidth) || float.IsNaN(rootHeight) || rootWidth <= 0.0f || rootHeight <= 0.0f)
        {
            return;
        }

        m_indicatorElement.style.left = (rootWidth - canvasSize) * 0.5f;
        m_indicatorElement.style.top = (rootHeight - canvasSize) * 0.5f;
        m_indicatorElement.style.width = canvasSize;
        m_indicatorElement.style.height = canvasSize;
        m_indicatorElement.style.rotate = new StyleRotate(new Rotate(new Angle(angle, AngleUnit.Degree)));
    }

    private void UpdateIndicatorFade()
    {
        m_elapsed += Time.deltaTime;
        float fadeElapsed = m_elapsed - m_holdDuration;
        if (fadeElapsed <= 0.0f)
        {
            m_indicatorElement.style.opacity = 1.0f;
            return;
        }

        float opacity = 1.0f - fadeElapsed / Mathf.Max(0.01f, m_fadeDuration);
        if (opacity <= 0.0f)
        {
            HideIndicator();
            return;
        }

        m_indicatorElement.style.opacity = Mathf.Clamp01(opacity);
    }

    private void HideIndicator()
    {
        m_isVisible = false;
        m_elapsed = 0.0f;

        if (m_indicatorElement != null)
        {
            m_indicatorElement.style.display = DisplayStyle.None;
        }
    }

    private void GenerateIndicatorVisual(MeshGenerationContext context)
    {
        Rect contentRect = context.visualElement.contentRect;
        Vector2 center = contentRect.center;
        float radius = Mathf.Max(0.0f, m_radiusPixels);
        float halfArc = Mathf.Clamp(m_arcAngleDegrees, 5.0f, 120.0f) * 0.5f;
        const float topAngle = 270.0f;
        float startAngle = topAngle - halfArc;
        float endAngle = topAngle + halfArc;

        Painter2D painter = context.painter2D;
        painter.lineCap = LineCap.Butt;

        float outlineLineWidth = m_arcThicknessPixels + m_outlineWidthPixels * 2.0f;
        if (outlineLineWidth > m_arcThicknessPixels && m_outlineColor.a > 0.0f)
        {
            DrawRingSegment(painter, center, radius, startAngle, endAngle, m_outlineColor, outlineLineWidth);
        }

        DrawRingSegment(painter, center, radius, startAngle, endAngle, m_color, m_arcThicknessPixels);
    }

    private static void DrawRingSegment(
        Painter2D painter,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        Color color,
        float lineWidth)
    {
        painter.strokeColor = color;
        painter.lineWidth = Mathf.Max(1.0f, lineWidth);
        painter.BeginPath();
        painter.Arc(center, radius, startAngle, endAngle);
        painter.Stroke();
    }
}
