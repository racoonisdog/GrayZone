using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 월드 기준 오브젝트의 위치로 미니맵 위 이미지 배치를 계산하는 에디터용 도구
/// </summary>
/// <remarks>
/// 좌표 변환은 MinimapPlayerMarker와 같은 공식을 쓰므로 플레이어 마커와 항상 같은 기준으로 맞음.
/// 런타임에는 계산하지 않음. 월드 오브젝트를 옮기면 컨텍스트 메뉴 Apply로 다시 계산해 씬에 저장.
/// </remarks>
[RequireComponent(typeof(RectTransform))]
public class MinimapWorldPlacement : MonoBehaviour
{
    public enum SizeMode
    {
        /// <summary>기준 범위 중심에 두고 스프라이트 원본 픽셀 / artPixelsPerMeter 크기로 표시</summary>
        ArtScale,
        /// <summary>기준 범위에 맞춰 늘림</summary>
        FitBounds,
    }

    public enum BoundsSource
    {
        Meshes,
        Colliders,
    }

    [Tooltip("좌표 기준. 비워두면 부모 쪽 MinimapPlayerMarker를 사용")]
    [SerializeField] private MinimapPlayerMarker mapping;
    [Tooltip("위치 기준 월드 오브젝트들 (하위 포함, 비활성도 계산)")]
    [SerializeField] private Transform[] references;
    [SerializeField] private BoundsSource boundsSource = BoundsSource.Meshes;
    [SerializeField] private SizeMode sizeMode = SizeMode.ArtScale;
    [Tooltip("ArtScale일 때 스프라이트 원본의 미터당 픽셀 수")]
    [SerializeField] private float artPixelsPerMeter = 15.7f;
    [Tooltip("첫 번째 기준 오브젝트의 회전(90° 단위 나머지)에 맞춰 범위를 재고 이미지를 회전. 반듯한 사각형 아트에 사용")]
    [SerializeField] private bool alignToReference;
    [Tooltip("alignToReference가 꺼져 있을 때 쓰는 UI 회전 각도 (양수는 반시계 방향). 범위도 이 각도 기준으로 잼")]
    [SerializeField] private float rotation;
    [Tooltip("기준 범위 중심 보정 (맵 공간 미터, x = 북쪽 +X, y = 서쪽 +Z). 소품 등으로 중심이 치우칠 때 사용")]
    [SerializeField] private Vector2 centerOffset;

    /// <summary>
    /// 기준 오브젝트 위치로 다시 배치 (에디터에서 실행 후 씬 저장)
    /// </summary>
    [ContextMenu("Apply")]
    public void Apply()
    {
        MinimapPlayerMarker map = ResolveMapping(mapping, transform);
        if (map == null || references == null || references.Length == 0)
            return;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(transform, "Apply Minimap Placement");
#endif

        // UI 회전 -θ = 월드 yaw +θ
        float yaw = alignToReference ? GetResidualYaw(map, references[0]) : -rotation;
        if (!TryGetMapSpaceBounds(map, references, boundsSource, yaw, out Vector3 min, out Vector3 max))
            return;

        // 보정값은 맵 공간 기준이므로 yaw 좌표계로 돌려서 더함
        Vector3 offset = Quaternion.Euler(0f, -yaw, 0f) * new Vector3(centerOffset.x, 0f, centerOffset.y);
        min += offset;
        max += offset;

        if (sizeMode == SizeMode.ArtScale)
        {
            UnityEngine.UI.Image image = GetComponent<UnityEngine.UI.Image>();
            if (image != null && image.sprite != null && artPixelsPerMeter > 0f)
            {
                Vector3 center = (min + max) * 0.5f;
                // 화면 가로 = 맵 공간 Z, 세로 = X
                Vector2 half = image.sprite.rect.size / artPixelsPerMeter * 0.5f;
                min = new Vector3(center.x - half.y, center.y, center.z - half.x);
                max = new Vector3(center.x + half.y, center.y, center.z + half.x);
            }
        }

        SetOrientedBox(map, (RectTransform)transform, min, max, yaw);
    }

    /// <summary>
    /// 기준 오브젝트가 맵 공간 대비 회전한 각도를 -45~45° 범위로 반환 (90° 단위 회전은 축만 바뀌므로 제외)
    /// </summary>
    internal static float GetResidualYaw(MinimapPlayerMarker map, Transform target)
    {
        if (target == null)
            return 0f;

        Vector3 forward = map.ToMapSpace(target.position + target.forward) - map.ToMapSpace(target.position);
        float rel = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        return rel - 90f * Mathf.Round(rel / 90f);
    }

    /// <summary>
    /// yaw만큼 회전한 좌표계 기준 범위(min/max)를 앵커 + UI 회전으로 적용
    /// </summary>
    internal static void SetOrientedBox(MinimapPlayerMarker map, RectTransform rt, Vector3 min, Vector3 max, float yaw)
    {
        if (!TryGetOrientedAnchors(map, min, max, yaw, out Vector2 anchorMin, out Vector2 anchorMax))
            return;

        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        // 월드 yaw +θ는 UI에서 -θ (MinimapPlayerMarker 회전과 같은 규칙)
        rt.localEulerAngles = new Vector3(0f, 0f, -yaw);
    }

    /// <summary>
    /// yaw만큼 회전한 좌표계 기준 범위(min/max)를 회전 전 앵커(정규화 좌표)로 변환
    /// </summary>
    internal static bool TryGetOrientedAnchors(MinimapPlayerMarker map, Vector3 min, Vector3 max, float yaw,
        out Vector2 anchorMin, out Vector2 anchorMax)
    {
        Vector3 center = Quaternion.Euler(0f, yaw, 0f) * ((min + max) * 0.5f);
        Vector3 half = (max - min) * 0.5f;
        anchorMin = anchorMax = default;

        if (!map.TryMapSpaceToNormalized(center - half, out Vector2 a) || !map.TryMapSpaceToNormalized(center + half, out Vector2 b))
            return false;

        anchorMin = Vector2.Min(a, b);
        anchorMax = Vector2.Max(a, b);
        return true;
    }

    internal static MinimapPlayerMarker ResolveMapping(MinimapPlayerMarker assigned, Transform from)
    {
        if (assigned != null)
            return assigned;

        Transform parent = from.parent;
        return parent != null ? parent.GetComponentInChildren<MinimapPlayerMarker>(true) : null;
    }

    /// <summary>
    /// 기준 오브젝트들의 범위를 맵 공간을 yaw만큼 돌린 좌표계 기준 AABB로 계산 (비활성 오브젝트 포함)
    /// </summary>
    internal static bool TryGetMapSpaceBounds(MinimapPlayerMarker map, IReadOnlyList<Transform> targets,
        BoundsSource source, float yaw, out Vector3 min, out Vector3 max)
    {
        Quaternion toFrame = Quaternion.Euler(0f, -yaw, 0f);
        min = Vector3.one * float.MaxValue;
        max = Vector3.one * float.MinValue;
        bool found = false;

        if (targets == null)
            return false;

        foreach (Transform target in targets)
        {
            if (target == null)
                continue;

            if (source == BoundsSource.Colliders)
            {
                foreach (Collider col in target.GetComponentsInChildren<Collider>(true))
                {
                    Bounds local;
                    if (col is BoxCollider box)
                        local = new Bounds(box.center, box.size);
                    else if (col is MeshCollider mc && mc.sharedMesh != null)
                        local = mc.sharedMesh.bounds;
                    else
                        continue;

                    Encapsulate(map, toFrame, col.transform.localToWorldMatrix, local, ref min, ref max);
                    found = true;
                }
            }
            else
            {
                foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null)
                        continue;

                    // 정적 배칭되면 sharedMesh가 씬 단위 Combined Mesh로 바뀌어 로컬 bounds를 쓸 수 없음 → 월드 바운드 사용
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && mr.isPartOfStaticBatch)
                    {
                        if (!mr.enabled || !mr.gameObject.activeInHierarchy)
                            continue;

                        Encapsulate(map, toFrame, Matrix4x4.identity, mr.bounds, ref min, ref max);
                    }
                    else
                    {
                        Encapsulate(map, toFrame, mf.transform.localToWorldMatrix, mf.sharedMesh.bounds, ref min, ref max);
                    }

                    found = true;
                }
            }
        }

        return found;
    }

    private static void Encapsulate(MinimapPlayerMarker map, Quaternion toFrame, Matrix4x4 toWorld, Bounds local,
        ref Vector3 min, ref Vector3 max)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(
                (i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f,
                (i & 4) == 0 ? -1f : 1f));

            Vector3 p = toFrame * map.ToMapSpace(toWorld.MultiplyPoint3x4(corner));
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }
}
