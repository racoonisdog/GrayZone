using UnityEngine;

/// <summary>
/// 변이체의 시야·공격 판정에서 무엇이 앞을 가리는지를 정하는 레이어 도우미입니다.
/// </summary>
/// <remarks>
/// 마스크를 지정하지 않은 컴포넌트가 <c>Nothing</c>으로 남아 있으면 Raycast가 아무것도 맞히지 못해
/// 벽을 그대로 투시하게 됩니다. 이 실수는 에러 없이 조용히 지나가고 "왜 벽 너머에서 쫓아오지?"로만 드러나므로,
/// 비어 있을 때는 프로젝트 관례에 맞는 기본값을 채우고 경고를 남깁니다.
/// 캐릭터와 변이체 레이어는 기본값에서 제외합니다.
/// 다른 살아 있는 변이체는 서로의 시야도 공격 판정도 막지 않기 때문입니다(공용 `적 시스템` v0.2 §5.4.1, §5.9.2).
/// </remarks>
public static class EnemyLayers
{
    /// <summary>마스크가 비어 있을 때 사용할 고정 환경 장애물 레이어 이름입니다.</summary>
    private static readonly string[] s_defaultObstacleLayerNames = { "Default", "Environment", "Prop" };

    /// <summary>
    /// 지정된 마스크를 그대로 쓰되, 비어 있으면 기본 장애물 레이어로 대체합니다.
    /// </summary>
    /// <param name="configured">인스펙터에서 지정한 마스크입니다.</param>
    /// <param name="owner">경고 로그에서 짚어 줄 컴포넌트입니다.</param>
    /// <param name="usage">경고 문구에 넣을 용도 이름입니다.</param>
    /// <returns>실제로 Raycast에 사용할 레이어 마스크 값입니다.</returns>
    public static int ResolveObstacleMask(LayerMask configured, Component owner, string usage)
    {
        if (configured.value != 0)
        {
            return configured.value;
        }

        int fallback = 0;
        for (int i = 0; i < s_defaultObstacleLayerNames.Length; i++)
        {
            int layer = LayerMask.NameToLayer(s_defaultObstacleLayerNames[i]);
            if (layer >= 0)
            {
                fallback |= 1 << layer;
            }
        }

        Debug.LogWarning(
            $"[{owner.gameObject.name}] {usage} 레이어가 비어 있어 기본값({string.Join("/", s_defaultObstacleLayerNames)})을 사용합니다. " +
            "인스펙터에서 명시하는 편이 안전합니다.",
            owner);

        return fallback;
    }
}
