using UnityEngine;

/// <summary>
/// 구조물이 어떤 재질인지 표시하는 태그입니다.
/// </summary>
/// <remarks>
/// <para>
/// 표면 피드백(탄착 이펙트·피격음)과 소음 차폐가 이 하나를 함께 읽습니다. 각자 필요한 만큼 퉁치는 것은
/// 소비자 쪽 에셋이 <see cref="SurfaceMaterialType"/>을 여러 개 나열해 선언합니다.
/// </para>
/// <para>
/// <b>물리 머티리얼을 쓰지 않는 이유</b>: <c>PhysicsMaterial</c>은 원래 마찰·반발 값입니다. 식별용으로
/// 전용하면 물리는 같은데 분류만 다른 머티리얼을 복제해야 하고, 반대로 물리 튜닝 때문에 머티리얼을 나누면
/// 재질 분류가 조용히 갈라집니다. 또 밸런스 SO는 <c>UnityEngine.Object</c> 참조를 담을 수 없어
/// (`Balance and Feedback Data Flow.md`의 CSV 규칙) 물리 머티리얼로 분류하면 차폐율 표를 CSV로 굴릴 수
/// 없습니다. 열거형은 그 규칙이 허용하는 형태입니다.
/// </para>
/// <para>
/// <b>기획 편차</b>: 같은 문서는 "표면 피격은 FieldManager가 소유하는 `SurfaceFeedbackSystem`이
/// PhysicsMaterial 기준으로 FeedbackSO를 선택한다"고 정합니다. 사용자 결정으로 축을 열거형으로 옮긴
/// 상태이며, 문서 개정 전까지 이 편차를 문서 쪽 실수로 오해해 되돌리지 말 것.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class SurfaceMaterialTag : MonoBehaviour
{
    [Tooltip("이 구조물의 재질입니다. 탄착 이펙트와 소음 차폐가 함께 읽습니다.")]
    [SerializeField] private SurfaceMaterialType m_materialType = SurfaceMaterialType.Unknown;

    [Tooltip("소음 차폐에서만 다르게 볼 재질입니다. Unknown이면 위의 재질을 그대로 따릅니다. 합판으로 덮은 콘크리트 벽처럼 보이는 표면과 소리를 막는 구조가 다를 때만 채웁니다.")]
    [SerializeField] private SurfaceMaterialType m_noiseMaterialType = SurfaceMaterialType.Unknown;

    /// <summary>이 구조물의 재질입니다. 탄착 연출이 읽습니다.</summary>
    public SurfaceMaterialType MaterialType => m_materialType;

    /// <summary>소음 차폐가 볼 재질입니다. 따로 지정하지 않았으면 <see cref="MaterialType"/>과 같습니다.</summary>
    /// <remarks>
    /// 축을 처음부터 둘로 나누지 않은 이유는 매핑이 흩어지기 때문입니다. 축이 하나면 "석재와 벽돌은 같은
    /// 돌 이펙트"라는 규칙이 소비자 에셋에 한 줄로 남지만, 둘이면 그 규칙이 배치된 오브젝트 전체에 사람 손으로
    /// 복사됩니다. 그래서 <b>기본은 하나로 두고 표면과 구조가 실제로 갈리는 것만 예외로 적습니다.</b>
    ///
    /// 예외를 <see cref="NoiseOccluder"/>의 숫자가 아니라 재질 이름으로 적을 수 있게 한 것이 이 필드의
    /// 목적입니다. 숫자로 덮으면 그 벽만 차폐율 표 밖으로 빠져나가, 나중에 콘크리트 값을 조정해도 따라오지
    /// 않습니다. 두께처럼 재질로 설명되지 않는 것만 <see cref="NoiseOccluder"/>가 맡습니다.
    /// </remarks>
    public SurfaceMaterialType NoiseMaterialType => m_noiseMaterialType != SurfaceMaterialType.Unknown
        ? m_noiseMaterialType
        : m_materialType;

    /// <summary>
    /// 충돌한 콜라이더의 재질을 찾습니다.
    /// </summary>
    /// <param name="collider">판별할 콜라이더입니다.</param>
    /// <returns>태그를 찾지 못하면 <see cref="SurfaceMaterialType.Unknown"/>입니다.</returns>
    /// <remarks>
    /// 부모까지 거슬러 올라갑니다. 벽 하나가 콜라이더 여러 개로 나뉘어 있을 때 프리팹 루트에 하나만 붙이면
    /// 되도록 하기 위해서입니다. 콜라이더마다 붙이길 강제하면 배치 작업이 콜라이더 수만큼 늘어납니다.
    ///
    /// 반대로 한 구조물 안에서 재질이 갈리는 경우(벽돌 벽에 달린 철문)에는 그 부분 콜라이더에 태그를 따로
    /// 붙이면 가까운 쪽이 이깁니다.
    /// </remarks>
    public static SurfaceMaterialType Resolve(Collider collider)
    {
        if (collider == null)
        {
            return SurfaceMaterialType.Unknown;
        }

        SurfaceMaterialTag tag = collider.GetComponentInParent<SurfaceMaterialTag>();

        return tag != null ? tag.MaterialType : SurfaceMaterialType.Unknown;
    }
}
