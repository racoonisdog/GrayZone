using UnityEngine;

/// <summary>
/// 모델 자식의 <see cref="Animator"/>가 받은 애니메이션 이벤트를 상위 <see cref="EnemyController"/>에 전달합니다.
/// </summary>
/// <remarks>
/// Unity는 애니메이션 이벤트를 <b>Animator가 붙어 있는 GameObject의 컴포넌트에만</b> 보냅니다.
/// <see cref="EnemyController.ResolveAnimator"/>가 실제 재생을 모델 자식 Animator로 옮기기 때문에,
/// 루트에 있는 <see cref="EnemyController"/>는 공격 클립의 이벤트를 더 이상 받지 못합니다.
/// 그 상태에서는 <c>OnAttackHitboxOn</c>이 오지 않아 손의 판정 콜라이더가 켜지지 않고,
/// 결과적으로 근접 공격이 피해를 전혀 주지 못합니다(콘솔에는
/// <c>AnimationEvent 'OnAttackHitboxOff' ... has no receiver!</c>만 남습니다).
///
/// 이 컴포넌트는 <see cref="EnemyAnimatorRootMotionRelay"/>와 같은 얇은 전달 계층이며,
/// 판정 소유권은 기존대로 <see cref="EnemyController"/>에 둡니다.
/// 클립이 부르는 이벤트 이름을 늘릴 때는 여기에 같은 이름의 public 메서드를 함께 추가해야 합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class EnemyAnimatorEventRelay : MonoBehaviour
{
    private EnemyController m_owner;

    /// <summary>이벤트를 받을 Enemy를 연결합니다.</summary>
    public void Initialize(EnemyController owner)
    {
        m_owner = owner;
    }

    private void Awake()
    {
        m_owner ??= GetComponentInParent<EnemyController>();
    }

    /// <summary>공격 클립의 판정 시작 이벤트를 전달합니다.</summary>
    public void OnAttackHitboxOn()
    {
        m_owner?.OnAttackHitboxOn();
    }

    /// <summary>공격 클립의 판정 종료 이벤트를 전달합니다.</summary>
    public void OnAttackHitboxOff()
    {
        m_owner?.OnAttackHitboxOff();
    }

    /// <summary>공격 클립의 피해 적용 이벤트를 전달합니다.</summary>
    public void ApplyAttackDamage()
    {
        m_owner?.ApplyAttackDamage();
    }
}
