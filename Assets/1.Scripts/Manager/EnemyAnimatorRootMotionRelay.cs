using UnityEngine;

/// <summary>
/// 모델 자식의 <see cref="Animator"/>가 만든 루트 모션을 상위 <see cref="EnemyController"/>에 전달합니다.
/// </summary>
/// <remarks>
/// Humanoid Avatar는 자신이 만들어진 FBX의 골격 루트에서 구동해야 합니다. Enemy 루트에 Animator를 두면
/// FBX 골격 앞에 프리팹 래퍼 Transform이 하나 추가되어 스킨이 길게 늘어날 수 있으므로, 실제 애니메이션은
/// 모델 자식 Animator가 재생합니다. 이 컴포넌트는 그 구조에서도 경직 루트 모션의 소유권을 기존
/// EnemyController에 유지하기 위한 얇은 전달 계층입니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class EnemyAnimatorRootMotionRelay : MonoBehaviour
{
    private EnemyController m_owner;
    private Animator m_animator;

    /// <summary>루트 모션을 받을 Enemy와 이를 생성하는 Animator를 연결합니다.</summary>
    public void Initialize(EnemyController owner, Animator animator)
    {
        m_owner = owner;
        m_animator = animator;
    }

    private void Awake()
    {
        m_animator ??= GetComponent<Animator>();
        m_owner ??= GetComponentInParent<EnemyController>();
    }

    private void OnAnimatorMove()
    {
        m_owner?.ApplyAnimatorRootMotion(m_animator);
    }
}
