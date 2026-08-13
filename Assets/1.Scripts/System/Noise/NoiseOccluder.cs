using UnityEngine;

/// <summary>
/// 재질 기본값 대신 이 구조물만의 소음 차폐율을 쓰게 하는 예외 표시입니다.
/// </summary>
/// <remarks>
/// <para>
/// 소리를 막는 것은 표면 재질이 아니라 구조입니다. 같은 콘크리트라도 20cm 칸막이와 1m 방호벽이 다르고,
/// 얇은 함석 셔터와 두꺼운 금고문이 다릅니다. 그래서 <b>기본은 재질에서 가져오되 재질로 설명되지 않는
/// 구조물만 이 컴포넌트로 덮습니다.</b>
/// </para>
/// <para>
/// 전부 이 컴포넌트로 지정하지 않는 이유는 그러면 값이 필드 전체에 흩어져 아무도 전체 분포를 모르게 되기
/// 때문입니다. 반대로 재질만 쓰면 두께 차이를 표현할 방법이 없습니다. 예외만 손으로 잡는 편이 둘 사이에서
/// 가장 싸게 맞습니다.
/// </para>
/// <para>
/// <see cref="SurfaceMaterialTag"/>를 대체하지 않습니다. 탄착 이펙트와 피격음은 여전히 재질을 읽으므로,
/// 이 컴포넌트를 붙인 구조물에도 재질 태그는 그대로 필요합니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class NoiseOccluder : MonoBehaviour
{
    [Tooltip("이 구조물을 한 겹 통과할 때 깎이는 소음 비율입니다. 0이면 그대로 통과하고 1이면 완전히 막습니다. 재질 기본값을 덮어씁니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_occlusion = 0.5f;

    /// <summary>이 구조물을 한 겹 통과할 때 깎이는 소음 비율입니다.</summary>
    public float Occlusion => Mathf.Clamp01(m_occlusion);

    /// <summary>
    /// 충돌한 콜라이더에 구조물 차폐율 지정이 있으면 가져옵니다.
    /// </summary>
    /// <param name="collider">판별할 콜라이더입니다.</param>
    /// <param name="occlusion">찾은 차폐율입니다. 없으면 0입니다.</param>
    /// <returns>지정이 있으면 true입니다.</returns>
    /// <remarks>
    /// <see cref="SurfaceMaterialTag.Resolve"/>와 같이 부모까지 거슬러 올라갑니다. 두 표시가 같은 계층에
    /// 섞여 있어도 각자 가장 가까운 것이 이깁니다.
    /// </remarks>
    public static bool TryResolve(Collider collider, out float occlusion)
    {
        occlusion = 0.0f;

        if (collider == null)
        {
            return false;
        }

        NoiseOccluder occluder = collider.GetComponentInParent<NoiseOccluder>();

        if (occluder == null)
        {
            return false;
        }

        occlusion = occluder.Occlusion;
        return true;
    }
}
