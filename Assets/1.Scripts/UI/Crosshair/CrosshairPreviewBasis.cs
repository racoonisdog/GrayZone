using System;
using UnityEngine;

/// <summary>
/// 설정 화면 미리보기가 캐릭터 조준선 간격을 계산할 때 쓰는 값 한 벌입니다. 캐릭터 <see cref="AimController"/>와 든 총에서 옵니다.
/// </summary>
/// <remarks>
/// 전투 씬에서는 캐릭터의 AimController에서 바로 읽습니다. 타이틀·셸터처럼 캐릭터가 없는 씬에서는
/// 이번 실행에서 마지막으로 읽은 값, 그것도 없으면 <see cref="DefaultCrosshairSettings"/>에 담아 둔 값을 씁니다.
/// </remarks>
[Serializable]
public sealed class CrosshairPreviewBasis
{
    [Tooltip("탄퍼짐을 화면 간격으로 바꿀 때의 기준입니다(AimController 값).")]
    public CrosshairController.SpreadDisplayBasis spreadDisplayBasis = CrosshairController.SpreadDisplayBasis.MostShots;

    [Tooltip("탄퍼짐 표시 배율 강제값입니다. 0보다 작으면 쓰지 않습니다(AimController 값).")]
    public float spreadDisplayFactorOverride = -1.0f;

    [Tooltip("쏘지 않고 서 있을 때의 비조준 최소 탄퍼짐(도)입니다(총 값).")]
    public float hipfireMinSpread;

    [Tooltip("비조준 최대 탄퍼짐(도)입니다. 벌어짐 방식이 최대 탄퍼짐인 캐릭터가 씁니다(총 값).")]
    public float hipfireMaxSpread;

    [Tooltip("탄착 분포입니다(총 값).")]
    public SpreadDistribution distribution = SpreadDistribution.Gaussian;

    [Tooltip("탄착 분포 집중도입니다(총 값).")]
    public float concentration = 3.0f;

    [Tooltip("간격을 화면에 투영할 때 쓰는 FOV(도)입니다. 쏘지 않고 서 있을 때의 카메라 FOV입니다.")]
    public float fovDegrees = 30.0f;

    public CrosshairPreviewBasis Clone()
    {
        return (CrosshairPreviewBasis)MemberwiseClone();
    }
}
