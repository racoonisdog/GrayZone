using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 재질이 소음을 얼마나 막는지를 보관합니다.
/// </summary>
/// <remarks>
/// <para>
/// 재질 식별은 <see cref="SurfaceFeedbackSO"/>와 같은 <see cref="SurfaceMaterialType"/> 축을 씁니다.
/// 표면 피격음·데칼이 이미 그 축으로 재질을 가리므로, 소음 차폐가 자기만의 재질 분류를 새로 만들면
/// 같은 벽이 연출에서는 콘크리트, 소음에서는 다른 것이 되는 상태가 생깁니다.
/// </para>
/// <para>
/// 한 에셋이 재질을 여러 개 나열할 수 있습니다. 이것이 "퉁치기"의 실체입니다. 차폐가 석재와 벽돌을
/// 구분해야 하면 에셋을 둘로 나누고, 이펙트가 둘을 같은 돌로 취급하면 <see cref="SurfaceFeedbackSO"/>
/// 쪽에서 한 에셋이 둘 다 나열합니다. 축은 하나지만 묶는 방식은 소비자마다 다릅니다.
/// </para>
/// <para>
/// 그러면서도 에셋을 나눠 둡니다. <see cref="SurfaceFeedbackSO"/>는 "무엇이 보이고 들리는가"라는 연출
/// 데이터고 차폐율은 적이 알아채는지를 바꾸는 게임플레이 수치입니다. 한 에셋에 담으면 연출을 손보는
/// 사람이 감지 밸런스를 같이 움직입니다.
/// </para>
/// <para>
/// 실제 판별과 경로 누적은 <see cref="NoiseManager"/>가 담당합니다. 이 에셋은 값만 제공합니다.
/// </para>
/// </remarks>
[CreateAssetMenu(fileName = "NoiseOcclusion", menuName = "GrayZone/Noise/Noise Occlusion")]
public sealed class NoiseOcclusionSO : ScriptableObject
{
    [Header("Surface Identity")]
    [Tooltip("인스펙터와 디버그 로그에서 재질을 구분할 식별 이름입니다.")]
    [SerializeField] private string m_surfaceId = "Default";

    [Tooltip("이 차폐율을 적용할 재질 목록입니다. 여러 개를 넣으면 소음 차폐에서는 같은 것으로 취급됩니다.")]
    [SerializeField] private SurfaceMaterialType[] m_materialTypes = Array.Empty<SurfaceMaterialType>();

    [Header("Occlusion")]
    [Tooltip("이 재질을 한 겹 통과할 때 깎이는 소음 비율입니다. 0이면 그대로 통과하고 1이면 완전히 막습니다. 기획 미확정 - 임시값입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_occlusion = 0.5f;

    /// <summary>재질을 식별하는 이름입니다.</summary>
    public string SurfaceId => m_surfaceId;

    /// <summary>이 차폐율을 적용할 재질 목록입니다.</summary>
    public IReadOnlyList<SurfaceMaterialType> MaterialTypes => m_materialTypes;

    /// <summary>이 재질을 한 겹 통과할 때 깎이는 소음 비율입니다.</summary>
    public float Occlusion => Mathf.Clamp01(m_occlusion);
}
