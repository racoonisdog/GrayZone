using UnityEngine;

/// <summary>
/// 월드 오브젝트 위에 띄울 마커의 기준점을 계산합니다. 튜토리얼 목표 마커와 구조 대기 마커가 같이 씁니다.
/// </summary>
public static class WorldMarkerAnchor
{
    /// <summary>
    /// 대상 아래 켜진 렌더러를 모두 감싸는 범위의 맨 위 가운데에서 <paramref name="height"/>만큼 위 지점을 돌려줍니다. 렌더러가 없으면 대상 위치 위입니다.
    /// </summary>
    /// <remarks>
    /// 함정 루트는 실제 모습과 멀리 떨어져 있을 수 있어서(예: 철조망 묶음은 10m) 루트 위치를 쓰지 않습니다.
    /// 파티클은 재생하지 않을 때 범위가 원점(0,0,0)으로 잡혀 마커를 엉뚱한 곳으로 끌고 가므로 뺍니다(예: 캐릭터의 총구 화염).
    /// </remarks>
    public static Vector3 GetTopCenter(Transform root, float height)
    {
        return TryGetVisibleBounds(root, out Bounds bounds)
            ? new Vector3(bounds.center.x, bounds.max.y + height, bounds.center.z)
            : root.position + Vector3.up * height;
    }

    /// <summary>대상 아래 켜진 렌더러를 모두 감싸는 범위의 바닥 가운데를 돌려줍니다. 렌더러가 없으면 대상 위치입니다. 경로선 목적지처럼 땅 위 지점이 필요할 때 씁니다.</summary>
    public static Vector3 GetBottomCenter(Transform root)
    {
        return TryGetVisibleBounds(root, out Bounds bounds)
            ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)
            : root.position;
    }

    private static bool TryGetVisibleBounds(Transform root, out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer.bounds.size == Vector3.zero)
            {
                continue;
            }

            if (hasBounds)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
        }

        return hasBounds;
    }
}
