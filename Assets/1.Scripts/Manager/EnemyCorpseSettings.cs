using UnityEngine;

/// <summary>
/// 현재 필드의 모든 적 진영 유닛에 공통 적용할 시체 처리 설정을 보관합니다.
/// </summary>
/// <remarks>
/// 적 개체는 사망 상태 진입 시 이 값을 한 번 읽습니다. 이미 사망한 시체에는 이후 Inspector 변경이 소급 적용되지 않습니다.
/// 이 컴포넌트는 <see cref="FieldManager"/>와 같은 GameObject에 배치합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemyCorpseSettings : MonoBehaviour
{
    [Header("Enemy Corpse Options")]
    [Tooltip("켜면 적 사망 후 시체 오브젝트를 지정한 시간이 지난 뒤 삭제합니다. 끄면 필드가 끝날 때까지 유지합니다.")]
    [SerializeField] private bool m_destroyCorpse = true;

    [Tooltip("적이 사망한 뒤 시체 오브젝트를 삭제하기까지 기다리는 시간(초)입니다. 시체 삭제가 꺼져 있으면 사용하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_corpseLifetime = 3.0f;

    [Tooltip("켜면 물리 골격이 준비된 적을 사망 시 래그돌로 전환합니다. 끄거나 골격이 없으면 사망 애니메이션을 사용합니다.")]
    [SerializeField] private bool m_useRagdoll = true;

    /// <summary>적 시체 오브젝트를 시간 경과 후 삭제할지 여부입니다.</summary>
    public bool DestroyCorpse => m_destroyCorpse;

    /// <summary>적 사망 후 시체 오브젝트를 삭제하기까지의 시간(초)입니다.</summary>
    public float CorpseLifetime => Mathf.Max(0.0f, m_corpseLifetime);

    /// <summary>물리 골격이 준비된 적에게 래그돌을 사용할지 여부입니다.</summary>
    public bool UseRagdoll => m_useRagdoll;
}
