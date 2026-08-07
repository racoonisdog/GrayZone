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

    /// <summary>이 콜라이더 명중을 약점 명중으로 처리하는지 여부입니다.</summary>
    public bool IsHeadshot => m_isHeadshot;
}
