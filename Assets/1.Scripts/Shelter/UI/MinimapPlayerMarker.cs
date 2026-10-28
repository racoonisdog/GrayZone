using UnityEngine;

/// <summary>
/// 셸터 맵 영역(펜스 기준) 안의 플레이어 위치를 미니맵 이미지 위 마커 위치로 변환
/// </summary>
public class MinimapPlayerMarker : MonoBehaviour
{
    // 탑뷰 UI 기준: 위(북) = +X, 왼쪽(서) = +Z. 월드 yaw 90(+X)이 UI 위를 향하도록 보정
    public const float NorthYaw = 90f;

    [Header("Map Space")]
    [Tooltip("맵 영역 기준 Transform (Shelter_Inner_Perimeter_Fences). 회전도 이 기준을 따름")]
    [SerializeField] private Transform mapSpace;
    [Tooltip("탑뷰 이미지가 월드 축(정북) 기준이면 체크, 펜스 회전 기준이면 해제")]
    [SerializeField] private bool worldAligned;
    [Tooltip("비워두면 mapSpace 자식 Renderer 바운드로 자동 계산")]
    [SerializeField] private Transform minAnchor;
    [SerializeField] private Transform maxAnchor;

    [Header("Target")]
    [Tooltip("비워두면 씬의 PlayerMove를 자동 탐색")]
    [SerializeField] private Transform player;

    [Header("UI")]
    [Tooltip("탑뷰 이미지 RectTransform (마커의 부모)")]
    [SerializeField] private RectTransform mapRect;
    [Tooltip("마커 RectTransform. 비워두면 이 오브젝트 사용 (앵커/피벗 0.5 권장)")]
    [SerializeField] private RectTransform marker;
    [SerializeField] private bool rotateWithPlayer = true;

    private Vector3 boundsMin;
    private Vector3 boundsMax;
    private bool hasBounds;

    private void Awake()
    {
        if (marker == null)
            marker = transform as RectTransform;

        if (mapRect == null && marker != null)
            mapRect = marker.parent as RectTransform;

        if (player == null)
        {
            PlayerMove playerMove = FindFirstObjectByType<PlayerMove>();
            if (playerMove != null)
                player = playerMove.transform;
        }

        if (mapSpace == null)
        {
            GameObject fences = GameObject.Find("Shelter_Inner_Perimeter_Fences");
            if (fences != null)
                mapSpace = fences.transform;
        }

        RecalculateBounds();
    }

    private void LateUpdate()
    {
        if (!hasBounds || player == null || mapRect == null || marker == null)
            return;

        Vector2 uv = MapSpaceToNormalized(ToMapSpace(player.position));
        float u = Mathf.Clamp01(uv.x);
        float v = Mathf.Clamp01(uv.y);

        Rect r = mapRect.rect;
        marker.anchoredPosition = new Vector2((u - 0.5f) * r.width, (v - 0.5f) * r.height);

        if (rotateWithPlayer)
        {
            float yaw = player.eulerAngles.y - MapYaw() - NorthYaw;
            marker.localEulerAngles = new Vector3(0f, 0f, -yaw);
        }
    }

    /// <summary>
    /// 앵커 또는 펜스 Renderer 기준으로 맵 영역(min/max)을 다시 계산
    /// </summary>
    [ContextMenu("Recalculate Bounds")]
    public void RecalculateBounds()
    {
        hasBounds = false;

        if (minAnchor != null && maxAnchor != null)
        {
            Vector3 a = ToMapSpace(minAnchor.position);
            Vector3 b = ToMapSpace(maxAnchor.position);
            boundsMin = Vector3.Min(a, b);
            boundsMax = Vector3.Max(a, b);
            hasBounds = true;
            return;
        }

        if (mapSpace == null)
        {
            Debug.LogWarning("[MinimapPlayerMarker] mapSpace와 앵커가 모두 없어 맵 영역을 계산할 수 없습니다.", this);
            return;
        }

        Renderer[] renderers = mapSpace.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[MinimapPlayerMarker] mapSpace 하위에 Renderer가 없습니다.", this);
            return;
        }

        boundsMin = Vector3.one * float.MaxValue;
        boundsMax = Vector3.one * float.MinValue;

        foreach (Renderer rend in renderers)
        {
            Bounds b = rend.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);

                Vector3 local = ToMapSpace(corner);
                boundsMin = Vector3.Min(boundsMin, local);
                boundsMax = Vector3.Max(boundsMax, local);
            }
        }

        hasBounds = true;
    }

    /// <summary>
    /// 월드 좌표를 맵 기준 공간(펜스 로컬 또는 월드) 좌표로 변환
    /// </summary>
    public Vector3 ToMapSpace(Vector3 worldPos)
    {
        if (worldAligned || mapSpace == null)
            return worldPos;

        return mapSpace.InverseTransformPoint(worldPos);
    }

    /// <summary>
    /// 맵 기준 공간 좌표를 미니맵 정규화 좌표(좌하단 0, 우상단 1)로 변환. 영역 밖이면 0~1을 벗어남
    /// </summary>
    /// <param name="mapSpacePos">맵 기준 공간 좌표</param>
    /// <param name="uv">x = 가로(왼쪽 0), y = 세로(아래 0)</param>
    /// <returns>맵 영역이 계산되어 있으면 <c>true</c></returns>
    public bool TryMapSpaceToNormalized(Vector3 mapSpacePos, out Vector2 uv)
    {
        if (!hasBounds)
            RecalculateBounds();

        uv = hasBounds ? MapSpaceToNormalized(mapSpacePos) : default;
        return hasBounds;
    }

    // UI 가로: 왼쪽(서) = +Z → 오른쪽으로 갈수록 Z 감소 / UI 세로: 위(북) = +X
    private Vector2 MapSpaceToNormalized(Vector3 p)
    {
        return new Vector2(
            (boundsMax.z - p.z) / (boundsMax.z - boundsMin.z),
            (p.x - boundsMin.x) / (boundsMax.x - boundsMin.x));
    }

    private float MapYaw()
    {
        if (worldAligned || mapSpace == null)
            return 0f;

        return mapSpace.eulerAngles.y;
    }

    private void OnDrawGizmosSelected()
    {
        if (!hasBounds)
            return;

        Gizmos.color = Color.cyan;
        if (!worldAligned && mapSpace != null)
            Gizmos.matrix = mapSpace.localToWorldMatrix;

        Vector3 center = (boundsMin + boundsMax) * 0.5f;
        Vector3 size = boundsMax - boundsMin;
        Gizmos.DrawWireCube(center, size);
    }
}
