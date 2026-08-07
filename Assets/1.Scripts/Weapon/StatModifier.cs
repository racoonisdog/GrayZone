using UnityEngine;

/// <summary>
/// 무기 스탯 하나에 가하는 보정 한 건입니다.
/// </summary>
/// <remarks>
/// "무엇을(<see cref="statType"/>) 어떻게(<see cref="modifierType"/>) 얼마나(<see cref="value"/>)"의 세 조각으로 이루어집니다.
/// 연산 방식이 값과 분리되어 있어야 같은 수치라도 덧셈 보정과 배율 보정을 구분할 수 있습니다.
/// </remarks>
[System.Serializable]
public class StatModifier
{
    /// <summary>보정 대상 스탯입니다.</summary>
    [Tooltip("보정 대상 스탯입니다.")]
    public StatType statType;

    /// <summary>보정을 적용하는 연산 방식입니다.</summary>
    [Tooltip("보정을 적용하는 연산 방식입니다. 덧셈인지 배율인지에 따라 같은 값도 결과가 달라집니다.")]
    public ModifierOp modifierType;

    /// <summary>보정에 사용할 값입니다.</summary>
    /// <remarks>의미는 <see cref="modifierType"/>이 정합니다. 덧셈이면 증감량, 배율이면 곱할 비율입니다.</remarks>
    [Tooltip("보정에 사용할 값입니다. 연산 방식이 덧셈이면 증감량, 배율이면 곱할 비율입니다.")]
    public float value;
}
