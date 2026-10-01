using UnityEngine;

/// <summary>
/// 투척기와 포물선 이동기가 공통으로 다루는 투척물 기반 컴포넌트입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public abstract class ProjectileBase : MonoBehaviour
{
    /// <summary>투척 경로를 계산할 최대 비행 시간입니다.</summary>
    public virtual float FlightTimeLimit => float.PositiveInfinity;

    /// <summary>예상 충돌 지점에 표시할 원형 범위의 반지름입니다. 0이면 표시하지 않습니다.</summary>
    public virtual float ImpactPreviewRadius => 0.0f;

    /// <summary>투척 경로와 실제 이동 중 충돌 대상에서 제외할 Layer입니다.</summary>
    public virtual LayerMask ContactExcludeLayers => default;

    /// <summary>
    /// 투척물이 표면에 도달했을 때 고유 효과를 실행합니다.
    /// </summary>
    /// <param name="projectilePosition">충돌 순간 투척물 중심 위치입니다.</param>
    /// <param name="contactPoint">표면과 실제로 맞닿은 위치입니다.</param>
    /// <param name="surfaceNormal">맞닿은 표면의 법선입니다.</param>
    public abstract void ImpactAt(
        Vector3 projectilePosition,
        Vector3 contactPoint,
        Vector3 surfaceNormal);
}
