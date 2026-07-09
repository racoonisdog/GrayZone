using UnityEngine;

/// <summary>
/// 유닛의 특정 콜라이더(예: 머리)에 붙여 히트 위치를 구분하는 마커입니다.
/// </summary>
/// <remarks>
/// 히트스캔이 이 콜라이더를 직접 맞으면 <see cref="CombatDamage.ResolveHit"/>가
/// 헤드샷 여부와 피해 배수를 읽어 피드백/피해에 반영합니다. 반드시 콜라이더와 같은
/// GameObject에 두어야 하며, 배수를 1로 두면 피해량 변화 없이 히트 위치 구분만 제공합니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    [Tooltip("이 콜라이더 피격을 헤드샷으로 취급할지 여부입니다. 조준선 히트마커 색상 구분에 사용합니다.")]
    [SerializeField] private bool m_isHeadshot = false;

    [Tooltip("이 부위 피격 시 기본 피해에 곱할 배수입니다. 1이면 피해량 변화가 없습니다.")]
    [SerializeField] private float m_damageMultiplier = 1.0f;

    /// <summary>이 부위 피격을 헤드샷으로 취급하는지 여부입니다.</summary>
    public bool IsHeadshot => m_isHeadshot;

    /// <summary>이 부위 피격 시 기본 피해에 곱할 배수입니다(0 미만은 0으로 보정).</summary>
    public float DamageMultiplier => Mathf.Max(0.0f, m_damageMultiplier);
}
