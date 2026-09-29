using UnityEngine;

/// <summary>
/// 적에게 거는 버프 한 종류를 정의하는 설정 에셋입니다.
/// </summary>
/// <remarks>
/// <b>에셋 하나가 버프 한 종류입니다.</b> 같은 에셋으로 다시 걸면 새로 쌓이지 않고 기존 버프를 새 값으로
/// 교체하며 지속 시간도 처음부터 다시 셉니다. 다른 에셋은 다른 종류로 보고 각각 따로 유지합니다.
/// 한 번 걸면 지속 시간이 끝날 때까지 유지하며, 버프를 건 개체의 사망으로 풀리지 않습니다.
///
/// 지금은 이동 속도 배율만 다룹니다. 다른 항목이 필요해지면 이 에셋에 필드를 추가하고
/// <see cref="EnemyBuffSet"/>에서 적용과 해제를 함께 늘립니다.
/// </remarks>
[CreateAssetMenu(fileName = "EnemyBuff_Name", menuName = "GrayZone/Enemy/Enemy Buff")]
public sealed class EnemyBuffSO : ScriptableObject
{
    [Header("Effect")]
    [Tooltip("버프가 걸린 동안 이동 속도에 곱할 배율입니다. 1.2면 20% 빨라집니다. 함정 감속 같은 다른 배율과 곱해집니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_moveSpeedMultiplier = 1.2f;

    [Header("Lifetime")]
    [Tooltip("버프 지속 시간(초)입니다. 한 번 걸리면 버프를 건 개체가 먼저 죽어도 이 시간이 끝날 때까지 유지됩니다. 0.1초보다 짧게 설정할 수 없습니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_duration = 10.0f;

    [Header("Target")]
    [Tooltip("버프를 건 개체 자신에게도 적용할지 여부입니다. 끄면 영향을 받은 다른 개체에게만 겁니다.")]
    [SerializeField] private bool m_applyToSource;

    /// <summary>버프가 걸린 동안 이동 속도에 곱할 배율입니다.</summary>
    public float MoveSpeedMultiplier => m_moveSpeedMultiplier;

    /// <summary>버프 지속 시간(초)입니다.</summary>
    public float Duration => m_duration;

    /// <summary>버프를 건 개체 자신에게도 적용할지 여부입니다.</summary>
    public bool ApplyToSource => m_applyToSource;

    /// <summary>Inspector 입력이 음수가 되지 않게 보정합니다.</summary>
    private void OnValidate()
    {
        m_moveSpeedMultiplier = Mathf.Max(0.0f, m_moveSpeedMultiplier);
        m_duration = Mathf.Max(0.1f, m_duration);
    }
}
