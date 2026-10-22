using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시설 업그레이드 레벨에 맞춰 미니맵 위 시설 이미지와 위치를 교체
/// </summary>
/// <remarks>
/// FacilityManager의 상태를 읽고 StateChanged만 구독함. 시설/업그레이드 쪽은 미니맵을 알지 못함.
/// 레벨별 탑뷰 위치/크기는 원본 이미지 픽셀 좌표(좌상단 기준), 회전은 UI 각도로 지정함.
/// </remarks>
[RequireComponent(typeof(Image))]
public class MinimapFacilityOverlay : MonoBehaviour
{
    [System.Serializable]
    private struct LevelVisual
    {
        public Sprite sprite;
        [Tooltip("미니맵 원본 이미지 기준 픽셀 영역 (x, y = 좌상단, width, height)")]
        public Rect pixelRect;
        [Tooltip("시설 탑뷰의 UI 회전 각도 (양수는 반시계 방향)")]
        public float rotation;
    }

    [Tooltip("FacilityDefinition의 시설 ID (제조실: manufacturing_center, 의료실: M_103)")]
    [SerializeField] private string facilityId;
    [Tooltip("미니맵 원본 이미지 픽셀 크기 (pixelRect 기준)")]
    [SerializeField] private Vector2 sourceSize = new Vector2(1221f, 1053f);
    [Tooltip("업그레이드 레벨 인덱스 순서. 레벨이 배열보다 크면 마지막 항목 사용")]
    [SerializeField] private LevelVisual[] levels;
    [Tooltip("시설이 잠겨 있으면 이미지를 숨김")]
    [SerializeField] private bool hideWhenLocked;

    private Image image;
    private FacilityManager boundManager;

    private void Awake()
    {
        image = GetComponent<Image>();
    }

    private void OnEnable()
    {
        Bind();
        Refresh();
    }

    private void Start()
    {
        // OnEnable 시점에 FacilityManager가 아직 없었던 경우 재시도
        if (boundManager == null)
        {
            Bind();
            Refresh();
        }
    }

    private void OnDisable()
    {
        if (boundManager != null)
            boundManager.StateChanged -= OnFacilityStateChanged;

        boundManager = null;
    }

    private void Bind()
    {
        FacilityManager manager = FacilityManager.Instance;
        if (manager == null || manager == boundManager)
            return;

        if (boundManager != null)
            boundManager.StateChanged -= OnFacilityStateChanged;

        boundManager = manager;
        boundManager.StateChanged += OnFacilityStateChanged;
    }

    private void OnFacilityStateChanged(FacilityState state)
    {
        if (state != null && state.FacilityId == facilityId)
            Refresh();
    }

    /// <summary>
    /// 현재 시설 상태 기준으로 이미지와 위치를 다시 적용
    /// </summary>
    [ContextMenu("Refresh")]
    public void Refresh()
    {
        FacilityState state = boundManager != null ? boundManager.GetState(facilityId) : null;
        ApplyLevel(state != null ? state.UpgradeLevel : 0);

        if (hideWhenLocked && state != null && !state.IsUnlocked)
            image.enabled = false;
    }

    /// <summary>
    /// 지정한 레벨의 이미지와 위치를 적용 (에디터 미리보기에도 사용)
    /// </summary>
    /// <param name="level">업그레이드 레벨 인덱스</param>
    public void ApplyLevel(int level)
    {
        if (image == null)
            image = GetComponent<Image>();

        if (levels == null || levels.Length == 0
            || sourceSize.x <= 0f || sourceSize.y <= 0f)
            return;

        LevelVisual visual = levels[Mathf.Clamp(level, 0, levels.Length - 1)];
        Rect r = visual.pixelRect;

        RectTransform rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(r.xMin / sourceSize.x, 1f - r.yMax / sourceSize.y);
        rt.anchorMax = new Vector2(r.xMax / sourceSize.x, 1f - r.yMin / sourceSize.y);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localEulerAngles = new Vector3(0f, 0f, visual.rotation);

        image.sprite = visual.sprite;
        image.enabled = visual.sprite != null;
    }
}
