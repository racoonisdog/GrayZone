using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 월드 기준 오브젝트의 위치를 따라 미니맵 위 이미지를 배치
/// </summary>
/// <remarks>
/// 좌표 변환은 MinimapPlayerMarker와 같은 공식을 쓰므로 플레이어 마커와 항상 같은 기준으로 맞음.
/// 픽셀 좌표를 저장하지 않아 월드 오브젝트를 옮기면 미니맵도 따라감.
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

    private void Start()
    {
        Apply();
    }

    /// <summary>
    /// 기준 오브젝트 위치로 다시 배치
    /// </summary>
    [ContextMenu("Apply")]
    public void Apply()
    {
        MinimapPlayerMarker map = ResolveMapping(mapping, transform);
        if (map == null || references == null || references.Length == 0)
            return;

        float yaw = alignToReference ? GetResidualYaw(map, references[0]) : 0f;
        if (!TryGetMapSpaceBounds(map, references, boundsSource, yaw, out Vector3 min, out Vector3 max))
            return;

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
        Vector3 center = Quaternion.Euler(0f, yaw, 0f) * ((min + max) * 0.5f);
        Vector3 half = (max - min) * 0.5f;
        SetAnchors(map, rt, center - half, center + half);
        // 월드 yaw +θ는 UI에서 -θ (MinimapPlayerMarker 회전과 같은 규칙)
        rt.localEulerAngles = new Vector3(0f, 0f, -yaw);
    }

    internal static MinimapPlayerMarker ResolveMapping(MinimapPlayerMarker assigned, Transform from)
    {
        if (assigned != null)
            return assigned;

        Transform parent = from.parent;
        return parent != null ? parent.GetComponentInChildren<MinimapPlayerMarker>(true) : null;
    }

    /// <summary>
    /// 맵 공간 기준 사각 범위를 RectTransform 앵커로 적용
    /// </summary>
    internal static void SetAnchors(MinimapPlayerMarker map, RectTransform rt, Vector3 min, Vector3 max)
    {
        if (!map.TryMapSpaceToNormalized(min, out Vector2 a) || !map.TryMapSpaceToNormalized(max, out Vector2 b))
            return;

        rt.anchorMin = Vector2.Min(a, b);
        rt.anchorMax = Vector2.Max(a, b);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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

                    Encapsulate(map, toFrame, col.transform, local, ref min, ref max);
                    found = true;
                }
            }
            else
            {
                foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null)
                        continue;

                    Encapsulate(map, toFrame, mf.transform, mf.sharedMesh.bounds, ref min, ref max);
                    found = true;
                }
            }
        }

        return found;
    }

    private static void Encapsulate(MinimapPlayerMarker map, Quaternion toFrame, Transform owner, Bounds local,
        ref Vector3 min, ref Vector3 max)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(
                (i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f,
                (i & 4) == 0 ? -1f : 1f));

            Vector3 p = toFrame * map.ToMapSpace(owner.TransformPoint(corner));
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }
}
