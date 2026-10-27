using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시설 업그레이드 레벨에 맞춰 미니맵 위 시설 이미지와 위치를 교체
/// </summary>
/// <remarks>
/// FacilityManager의 상태를 읽고 StateChanged만 구독함. 시설/업그레이드 쪽은 미니맵을 알지 못함.
/// 레벨별 위치/크기는 footprint(월드 기준 오브젝트)가 있으면 그 범위로, 없으면 원본 이미지 픽셀 좌표(좌상단 기준)로 지정함.
/// 회전은 UI 각도로 지정함.
/// </remarks>
[RequireComponent(typeof(Image))]
public class MinimapFacilityOverlay : MonoBehaviour
{
    [System.Serializable]
    private struct LevelVisual
    {
        public Sprite sprite;
        [Tooltip("이 레벨의 월드 외곽 기준 (예: Medical_Room_1/Wall_Collider). 지정하면 pixelRect 대신 사용")]
        public Transform footprint;
        [Tooltip("미니맵 원본 이미지 기준 픽셀 영역 (x, y = 좌상단, width, height). footprint가 없을 때 사용")]
        public Rect pixelRect;
        [Tooltip("시설 탑뷰의 UI 회전 각도 (양수는 반시계 방향). footprint가 있으면 footprint 회전으로 자동 계산")]
        public float rotation;
    }

    [Tooltip("FacilityDefinition의 시설 ID (제조실: manufacturing_center, 의료실: M_103)")]
    [SerializeField] private string facilityId;
    [Tooltip("footprint 좌표 기준. 비워두면 부모 쪽 MinimapPlayerMarker를 사용")]
    [SerializeField] private MinimapPlayerMarker mapping;
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

        if (levels == null || levels.Length == 0)
            return;

        LevelVisual visual = levels[Mathf.Clamp(level, 0, levels.Length - 1)];
        RectTransform rt = (RectTransform)transform;

        MinimapPlayerMarker map = visual.footprint != null
            ? MinimapWorldPlacement.ResolveMapping(mapping, transform)
            : null;

        float yaw = map != null ? MinimapWorldPlacement.GetResidualYaw(map, visual.footprint) : 0f;

        if (map != null && MinimapWorldPlacement.TryGetMapSpaceBounds(map, new[] { visual.footprint },
                MinimapWorldPlacement.BoundsSource.Colliders, yaw, out Vector3 min, out Vector3 max))
        {
            // footprint 회전에 맞춰 범위를 재고 이미지도 같이 회전 (rotation 값은 사용하지 않음)
            MinimapWorldPlacement.SetOrientedBox(map, rt, min, max, yaw);
        }
        else if (sourceSize.x > 0f && sourceSize.y > 0f)
        {
            Rect r = visual.pixelRect;
            rt.anchorMin = new Vector2(r.xMin / sourceSize.x, 1f - r.yMax / sourceSize.y);
            rt.anchorMax = new Vector2(r.xMax / sourceSize.x, 1f - r.yMin / sourceSize.y);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localEulerAngles = new Vector3(0f, 0f, visual.rotation);
        }

        image.sprite = visual.sprite;
        image.enabled = visual.sprite != null;
    }
}
