using UnityEngine;

/// <summary>
/// 재질별 소음 차폐율을 한곳에 모은 밸런스 표입니다.
/// </summary>
/// <remarks>
/// <para>
/// 재질마다 에셋을 나누지 않고 필드로 나열합니다. 차폐율은 재질당 실수 하나뿐이라 나눌 이득이 없고,
/// 한 에셋에 모아 두면 인스펙터에서 전체 분포가 한 화면에 보입니다. <see cref="EnemyBalanceSO"/>와 같은
/// 모양이며, <see cref="IBalanceTableData"/>라서 SO CSV 도구의 입출력 대상입니다.
/// </para>
/// <para>
/// <b>여러 재질을 묶는 기능이 없는 것이 의도입니다.</b> 묶기가 필요한 쪽은 표면 피드백입니다. 그쪽은
/// 클립 배열과 데칼 프리팹이라는 에셋 덩어리를 공유해야 해서 <see cref="SurfaceFeedbackSO"/> 하나가
/// 여러 재질을 가리킵니다. 차폐율은 숫자라 "석재와 벽돌을 같게"가 같은 값을 두 번 적는 것으로 끝납니다.
/// </para>
/// <para>
/// Unity 오브젝트 참조를 담지 않습니다. 담는 순간 CSV 도구가 내보내기와 가져오기를 거부합니다
/// (`Balance and Feedback Data Flow.md`의 SO CSV 조건). 재질 식별에 물리 머티리얼이 아니라
/// <see cref="SurfaceMaterialType"/>을 쓰는 이유가 이것입니다.
/// </para>
/// </remarks>
[CreateAssetMenu(fileName = "NoiseOcclusion", menuName = "GrayZone/Noise/Noise Occlusion")]
public sealed class NoiseOcclusionSO : ScriptableObject, IBalanceTableData
{
    [Header("Default")]
    [Tooltip("재질이 지정되지 않았거나 아래 목록에 없는 구조물의 차폐율입니다. 0이면 그대로 통과하고 1이면 완전히 막습니다. 재질 태그를 붙이기 전에는 모든 구조물이 이 값을 씁니다. 기획 미확정 - 임시값입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_defaultOcclusion = 0.5f;

    [Header("Material")]
    [Tooltip("콘크리트를 한 겹 통과할 때 깎이는 소음 비율입니다. 기획 미확정 - 임시값입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_concreteOcclusion = 0.7f;

    [Tooltip("석재를 한 겹 통과할 때 깎이는 소음 비율입니다. 기획 미확정 - 임시값입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_stoneOcclusion = 0.65f;

    [Tooltip("금속을 한 겹 통과할 때 깎이는 소음 비율입니다. 얇은 철판을 기준으로 두고, 두꺼운 것은 NoiseOccluder로 따로 덮습니다. 기획 미확정 - 임시값입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_metalOcclusion = 0.45f;

    [Tooltip("목재를 한 겹 통과할 때 깎이는 소음 비율입니다. 기획 미확정 - 임시값입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_woodOcclusion = 0.35f;

    /// <summary>재질이 지정되지 않은 구조물의 차폐율입니다.</summary>
    public float DefaultOcclusion => Mathf.Clamp01(m_defaultOcclusion);

    /// <summary>
    /// 재질에 대응하는 차폐율을 돌려줍니다.
    /// </summary>
    /// <param name="materialType">구조물의 재질입니다.</param>
    /// <returns>이 재질을 한 겹 통과할 때 깎이는 비율입니다.</returns>
    /// <remarks>
    /// <b>주의</b>: <see cref="SurfaceMaterialType"/>에 값을 추가하고 여기 <c>case</c>를 빠뜨리면 그 재질이
    /// 조용히 기본값으로 떨어집니다. 컴파일은 통과하므로 드러나지 않습니다. 재질을 늘릴 때는 열거형,
    /// 이 클래스의 필드, 아래 분기 세 곳을 함께 고칠 것.
    /// </remarks>
    public float Resolve(SurfaceMaterialType materialType)
    {
        switch (materialType)
        {
            case SurfaceMaterialType.Concrete:
                return Mathf.Clamp01(m_concreteOcclusion);

            case SurfaceMaterialType.Stone:
                return Mathf.Clamp01(m_stoneOcclusion);

            case SurfaceMaterialType.Metal:
                return Mathf.Clamp01(m_metalOcclusion);

            case SurfaceMaterialType.Wood:
                return Mathf.Clamp01(m_woodOcclusion);

            default:
                return DefaultOcclusion;
        }
    }
}
