using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 팀원 이동·사수 명령(Q/E)과 복귀 명령에 쓰는 이펙트 참조를 보관합니다.
/// </summary>
/// <remarks>
/// <see cref="SquadManager"/>가 명령을 내린 자리와 복귀 시점의 돌아올 팀원 발밑에서 재생합니다.
/// 어느 키로 내린 명령인지 보이도록 팀원 1(Q)과 팀원 2(E)가 이펙트를 따로 가집니다.
/// 이펙트를 바꿀 때는 프리팹만 갈아 끼우면 되고 코드는 건드리지 않습니다.
/// </remarks>
[CreateAssetMenu(fileName = "SquadOrderFeedback", menuName = "GrayZone/Feedback/Squad Order Feedback")]
public sealed class SquadOrderFeedbackSO : ScriptableObject, IFeedbackData
{
    [Header("Member 1 (Q)")]
    [Tooltip("팀원 1(Q)에게 이동·사수 명령을 내린 자리에 생성할 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "팀원 1(Q) 명령 이펙트 프리팹")]
    [FormerlySerializedAs("m_orderEffectPrefab")]
    [SerializeField] private GameObject m_member1OrderEffectPrefab;

    [Tooltip("팀원 1(Q)을 복귀시킬 때 그 팀원 발밑에 생성할 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "팀원 1(Q) 복귀 이펙트 프리팹")]
    [FormerlySerializedAs("m_recallEffectPrefab")]
    [SerializeField] private GameObject m_member1RecallEffectPrefab;

    [Header("Member 2 (E)")]
    [Tooltip("팀원 2(E)에게 이동·사수 명령을 내린 자리에 생성할 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "팀원 2(E) 명령 이펙트 프리팹")]
    [SerializeField] private GameObject m_member2OrderEffectPrefab;

    [Tooltip("팀원 2(E)를 복귀시킬 때 그 팀원 발밑에 생성할 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "팀원 2(E) 복귀 이펙트 프리팹")]
    [SerializeField] private GameObject m_member2RecallEffectPrefab;

    [Header("Invalid Order")]
    [Tooltip("이동할 수 없는 곳에 명령했을 때 조준한 지점에 생성할 이펙트 프리팹입니다. Q/E 공통입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "명령 불가 이펙트 프리팹")]
    [SerializeField] private GameObject m_invalidOrderEffectPrefab;

    [Tooltip("명령 불가 이펙트를 자동 제거하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_invalidOrderEffectLifetime = 1.0f;

    [Header("Common")]
    [Tooltip("명령 이펙트를 자동 제거하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_orderEffectLifetime = 1.5f;

    [Tooltip("복귀 이펙트를 자동 제거하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_recallEffectLifetime = 1.5f;

    [Tooltip("지면과 겹쳐 가려지지 않도록 이펙트를 띄울 높이(m)입니다.")]
    [SerializeField] private float m_groundOffset = 0.05f;

    /// <summary>명령 이펙트의 런타임 수명(초)입니다.</summary>
    public float OrderEffectLifetime => m_orderEffectLifetime;

    /// <summary>복귀 이펙트의 런타임 수명(초)입니다.</summary>
    public float RecallEffectLifetime => m_recallEffectLifetime;

    /// <summary>이펙트를 지면에서 띄울 높이(m)입니다.</summary>
    public float GroundOffset => m_groundOffset;

    /// <summary>이동할 수 없는 곳에 명령했을 때의 이펙트 프리팹입니다.</summary>
    public GameObject InvalidOrderEffectPrefab => m_invalidOrderEffectPrefab;

    /// <summary>명령 불가 이펙트의 런타임 수명(초)입니다.</summary>
    public float InvalidOrderEffectLifetime => m_invalidOrderEffectLifetime;

    /// <summary>지정한 팀원에게 쓸 명령 이펙트 프리팹을 돌려줍니다.</summary>
    /// <param name="aiOrder">0이 팀원 1(Q), 1이 팀원 2(E)입니다.</param>
    /// <returns>해당 프리팹입니다. 비어 있거나 범위 밖이면 null입니다.</returns>
    public GameObject GetOrderEffectPrefab(int aiOrder)
    {
        return aiOrder switch
        {
            0 => m_member1OrderEffectPrefab,
            1 => m_member2OrderEffectPrefab,
            _ => null,
        };
    }

    /// <summary>지정한 팀원에게 쓸 복귀 이펙트 프리팹을 돌려줍니다.</summary>
    /// <param name="aiOrder">0이 팀원 1(Q), 1이 팀원 2(E)입니다.</param>
    /// <returns>해당 프리팹입니다. 비어 있거나 범위 밖이면 null입니다.</returns>
    public GameObject GetRecallEffectPrefab(int aiOrder)
    {
        return aiOrder switch
        {
            0 => m_member1RecallEffectPrefab,
            1 => m_member2RecallEffectPrefab,
            _ => null,
        };
    }
}
