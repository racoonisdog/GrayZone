using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시설 업그레이드 레벨에 맞춰 미니맵 위 시설 이미지와 위치를 교체
/// </summary>
/// <remarks>
/// FacilityManager의 상태를 읽고 StateChanged만 구독함. 시설/업그레이드 쪽은 미니맵을 알지 못함.
/// 레벨별 위치/크기는 원본 이미지 픽셀 좌표(좌상단 기준), 회전은 UI 각도로 지정함.
/// footprint(월드 기준 오브젝트)를 지정하고 컨텍스트 메뉴 Bake Footprints를 실행하면 그 범위로 pixelRect/rotation을 채움 (런타임에는 계산하지 않음).
/// </remarks>
[RequireComponent(typeof(Image))]
public class MinimapFacilityOverlay : MonoBehaviour
{
    [System.Serializable]
    private struct LevelVisual
    {
        public Sprite sprite;
        [Tooltip("이 레벨의 월드 외곽 기준 (예: Medical_Room_1/Wall_Collider). Bake Footprints 실행 시 pixelRect/rotation 계산에만 사용")]
        public Transform footprint;
        [Tooltip("미니맵 원본 이미지 기준 픽셀 영역 (x, y = 좌상단, width, height)")]
        public Rect pixelRect;
        [Tooltip("시설 탑뷰의 UI 회전 각도 (양수는 반시계 방향)")]
        public float rotation;
    }

    [Tooltip("FacilityDefinition의 시설 ID (제조실: manufacturing_center, 의료실: M_103)")]
    [SerializeField] private string facilityId;
    [Tooltip("Bake Footprints 좌표 기준. 비워두면 부모 쪽 MinimapPlayerMarker를 사용")]
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

        if (sourceSize.x > 0f && sourceSize.y > 0f)
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

    /// <summary>
    /// 레벨별 footprint의 월드 범위/회전으로 pixelRect와 rotation을 계산해 저장 (에디터에서 실행 후 씬 저장)
    /// </summary>
    [ContextMenu("Bake Footprints")]
    public void BakeFootprints()
    {
        MinimapPlayerMarker map = MinimapWorldPlacement.ResolveMapping(mapping, transform);
        if (map == null || levels == null || sourceSize.x <= 0f || sourceSize.y <= 0f)
            return;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Bake Minimap Footprints");
#endif

        for (int i = 0; i < levels.Length; i++)
        {
            LevelVisual visual = levels[i];
            if (visual.footprint == null)
                continue;

            float yaw = MinimapWorldPlacement.GetResidualYaw(map, visual.footprint);
            if (!MinimapWorldPlacement.TryGetMapSpaceBounds(map, new[] { visual.footprint },
                    MinimapWorldPlacement.BoundsSource.Colliders, yaw, out Vector3 min, out Vector3 max)
                || !MinimapWorldPlacement.TryGetOrientedAnchors(map, min, max, yaw, out Vector2 a, out Vector2 b))
                continue;

            // 앵커(좌하단 기준 0~1) → 원본 픽셀(좌상단 기준)
            visual.pixelRect = Rect.MinMaxRect(a.x * sourceSize.x, (1f - b.y) * sourceSize.y,
                b.x * sourceSize.x, (1f - a.y) * sourceSize.y);
            // 월드 yaw +θ는 UI에서 -θ
            visual.rotation = -yaw;
            levels[i] = visual;
        }

        Refresh();
    }
}
