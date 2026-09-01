using UnityEngine;

/// <summary>
/// 콜라이더 하나를 특정 피격 부위로 표시합니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 <b>부위 표시만</b> 합니다. 피해량과 약점 배율은 무기가 소유합니다.
/// 부위마다 수치를 두면 같은 무기가 대상에 따라 다른 피해를 주게 되어, 무기 밸런스를 한 자리에서 볼 수 없습니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    [Tooltip("이 콜라이더에 맞은 것을 약점 명중으로 볼지 여부입니다.")]
    [SerializeField] private bool m_isHeadshot = false;

    [Tooltip("이 부위를 항상 켜 둘지 여부입니다. 몸통 캡슐이 감싸지 못하는 부위(팔처럼 크게 휘두르는 곳)에만 켭니다.")]
    [SerializeField] private bool m_alwaysActive = false;

    [Tooltip("이 부위에 맞았을 때 어디를 맞았는지 콘솔에 남깁니다. 사격 표적처럼 판정 확인이 목적인 대상에만 켭니다.")]
    [SerializeField] private bool m_logHit = false;

    /// <summary>이 콜라이더 명중을 약점 명중으로 처리하는지 여부입니다.</summary>
    public bool IsHeadshot => m_isHeadshot;

    /// <summary>
    /// 이 부위에 맞았을 때 콘솔에 남길지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 부위별 판정이 의도대로 도는지 확인하는 용도입니다. 사격 표적(X/Y Bot)처럼 검증이 목적인 대상에만 켭니다.
    /// 실제 적에 켜면 교전 한 번에 수십 줄이 쌓여 콘솔이 묻힙니다.
    /// </remarks>
    public bool LogHit => m_logHit;

    /// <summary>
    /// 이 부위가 2단계 판정에서 제외되어 항상 켜져 있어야 하는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 2단계 판정은 몸통 캡슐로 "이 개체를 지나갔는가"를 먼저 거른 뒤 부위를 켭니다.
    /// 그래서 <b>몸통 캡슐 밖으로 나가는 부위는 그 필터를 통과하지 못합니다.</b>
    /// 팔이 그렇습니다. 휘두르는 반경까지 덮으려면 캡슐이 몸통의 두 배가 되어 몸통 판정 자체가 뭉개집니다.
    /// 그런 부위만 이 값을 켜서 상시 유지하고, 나머지는 <see cref="HitboxGroup"/>이 필요할 때만 켭니다.
    /// </remarks>
    public bool AlwaysActive => m_alwaysActive;
}
