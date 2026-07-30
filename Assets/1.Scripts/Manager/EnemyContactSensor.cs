using UnityEngine;

/// <summary>
/// 몸이 맞닿은 스쿼드 캐릭터를 변이체에게 알려 주는 접촉 감지기입니다.
/// </summary>
/// <remarks>
/// 평소 탐지는 시야가 담당하고, 이 감지기는 "부딪혔으면 당연히 안다"만 책임집니다.
/// 판정 규칙을 들고 있지 않고 <see cref="EnemyTargetSensor.NotifyContact"/>로 넘기기만 합니다.
/// 감지 여부의 판단은 센서 한 곳에 모여 있어야 나중에 규칙이 바뀔 때 흩어지지 않기 때문입니다.
///
/// 이 컴포넌트는 트리거 콜라이더와 같은 GameObject에 붙여야 합니다.
/// 복합 콜라이더에서 트리거 콜백이 콜라이더 쪽으로 오는지 Rigidbody 쪽으로 오는지가 헷갈리는데,
/// 같은 오브젝트에 두면 그 모호함이 사라집니다.
///
/// 트리거를 쓰는 이유는 <see cref="CharacterController"/>가 상대에게 OnCollisionEnter를 발생시키지 않기 때문입니다.
/// 플레이어가 CharacterController라 일반 충돌로는 변이체가 접촉을 알 방법이 없고,
/// 트리거 이벤트는 CharacterController도 발생시키므로 이 경로만 성립합니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public class EnemyContactSensor : MonoBehaviour
{
    [Tooltip("접촉을 알려 줄 대상 센서입니다. 비어 있으면 상위에서 찾습니다.")]
    [SerializeField] private EnemyTargetSensor m_sensor;

    private void Awake()
    {
        if (m_sensor == null)
        {
            m_sensor = GetComponentInParent<EnemyTargetSensor>();
        }

        Collider self = GetComponent<Collider>();
        if (self != null && !self.isTrigger)
        {
            // 트리거가 아니면 CharacterController와의 접촉을 전혀 받지 못해 조용히 동작하지 않습니다.
            Debug.LogWarning($"[{name}] 접촉 감지 콜라이더가 트리거가 아닙니다. Is Trigger를 켜야 동작합니다.", this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Notify(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // 트리거가 켜지기 전부터 겹쳐 있었거나 진입 이벤트를 놓친 경우를 위해 유지 중에도 확인합니다.
        // 인식은 같은 값을 다시 쓰는 것이라 반복 호출이 문제되지 않습니다.
        Notify(other);
    }

    /// <summary>맞닿은 상대가 스쿼드 캐릭터면 센서에 알립니다.</summary>
    private void Notify(Collider other)
    {
        if (m_sensor == null || other == null)
        {
            return;
        }

        SquadMemberController member = other.GetComponentInParent<SquadMemberController>();
        if (member == null)
        {
            return;
        }

        m_sensor.NotifyContact(member);
    }
}
