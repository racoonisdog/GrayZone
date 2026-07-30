using System;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// 필드가 가질 수 있는 값의 범위를 선언하고, 그 범위로 자르는 방법까지 함께 가지는 어트리뷰트입니다.
/// </summary>
/// <remarks>
/// 경계 선언 한 줄이 두 경로를 모두 지킵니다.
/// 하나는 Inspector 입력이고(전용 PropertyDrawer가 이 선언을 읽습니다), 다른 하나는 밸런스 SO 주입입니다
/// (<see cref="BindManager"/>가 Bind 시점에 이 선언을 읽습니다).
/// 그래서 SO를 쓰지 않는 값도 같은 범위를 따르고, SO를 쓰는 값은 시트에서 들어온 값도 같은 범위로 잘립니다.
///
/// 유니티 기본 <c>[Min]</c>·<c>[Range]</c>와 다른 점은 <b>런타임에도 읽힌다</b>는 것입니다.
/// 기본 어트리뷰트는 PropertyDrawer가 Inspector를 그릴 때만 개입해서, CSV 임포트처럼
/// Inspector를 거치지 않는 경로로 들어온 값은 막지 못합니다.
///
/// 자르는 로직을 <see cref="BindManager"/>가 아니라 이 어트리뷰트가 가지는 이유는,
/// "어떻게 자르는가"는 경계 선언에 딸린 지식이고 "언제 자르는가"만 호출부의 몫이기 때문입니다.
/// 어트리뷰트는 스스로 실행되지 않으므로 호출은 여전히 밖에서 합니다.
///
/// <see cref="Min"/>·<see cref="Max"/>를 적지 않으면 <see cref="double.NaN"/>이 되어 그 방향은 제한하지 않습니다.
/// 0을 기본값으로 쓰지 않는 이유는 "0으로 제한"과 "제한 없음"을 구분해야 하기 때문입니다.
/// </remarks>
[Preserve]
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ClampAttribute : PropertyAttribute
{
    /// <summary>허용 최솟값입니다. 적지 않으면 하한을 두지 않습니다.</summary>
    public double Min { get; set; } = double.NaN;

    /// <summary>허용 최댓값입니다. 적지 않으면 상한을 두지 않습니다.</summary>
    public double Max { get; set; } = double.NaN;

    /// <summary>하한이 선언되어 있는지 여부입니다.</summary>
    public bool HasMin => !double.IsNaN(Min);

    /// <summary>상한이 선언되어 있는지 여부입니다.</summary>
    public bool HasMax => !double.IsNaN(Max);

    /// <summary>
    /// 선언된 경계가 뒤집혀 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 둘 다 선언되어 있을 때만 판단합니다. 하나가 없으면 비교 자체가 성립하지 않습니다.
    /// 이 검사가 호출부가 아니라 여기 있는 이유는, NaN 조합을 매 호출부가 다시 따지지 않게 하기 위해서입니다.
    /// </remarks>
    public bool IsInverted => HasMin && HasMax && Min > Max;

    /// <summary>실제로 사용할 하한입니다. 선언이 뒤집혀 있으면 둘을 바꿔 씁니다.</summary>
    public double EffectiveMin => IsInverted ? Max : Min;

    /// <summary>실제로 사용할 상한입니다. 선언이 뒤집혀 있으면 둘을 바꿔 씁니다.</summary>
    public double EffectiveMax => IsInverted ? Min : Max;

    /// <summary>
    /// 값을 선언된 범위 안으로 자릅니다.
    /// </summary>
    /// <param name="value">자를 값입니다.</param>
    /// <param name="clamped">실제로 값이 바뀌었으면 true입니다.</param>
    /// <returns>범위 안으로 보정된 값입니다.</returns>
    public double Apply(double value, out bool clamped)
    {
        double result = value;

        if (HasMin)
        {
            result = Math.Max(EffectiveMin, result);
        }

        if (HasMax)
        {
            result = Math.Min(EffectiveMax, result);
        }

        clamped = result != value;
        return result;
    }

    /// <summary>
    /// 이 선언을 해당 타입에 적용할 수 있는지 확인합니다.
    /// </summary>
    /// <param name="fieldType">선언이 붙은 필드의 타입입니다.</param>
    /// <param name="error">적용할 수 없을 때 사유입니다.</param>
    /// <returns>적용 가능하면 true입니다.</returns>
    /// <remarks>
    /// enum은 숫자로 캐스팅되지만 범위를 자르면 없는 멤버가 되므로 대상에서 제외합니다.
    /// </remarks>
    public bool Validate(Type fieldType, out string error)
    {
        error = null;

        if (!IsClampableType(fieldType))
        {
            error = $"'{fieldType.Name}' 타입에는 Min/Max를 적용할 수 없습니다. 숫자 타입에만 사용하십시오.";
            return false;
        }

        if (IsInverted)
        {
            error = $"Min({Min})이 Max({Max})보다 큽니다. 선언이 뒤집혀 있어 서로 바꿔 적용합니다.";
            return false;
        }

        return true;
    }

    /// <summary>범위 보정을 적용할 수 있는 숫자 타입인지 확인합니다.</summary>
    /// <remarks>enum은 기반 타입이 정수라도 제외합니다. 범위를 자르면 정의되지 않은 값이 되기 때문입니다.</remarks>
    public static bool IsClampableType(Type type)
    {
        if (type == null || type.IsEnum)
        {
            return false;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Inspector 표시에 쓸 범위 설명입니다. 시트 툴팁으로도 내보냅니다.</summary>
    /// <returns>선언이 없으면 빈 문자열입니다.</returns>
    public string DescribeRange()
    {
        if (!HasMin && !HasMax)
        {
            return string.Empty;
        }

        if (HasMin && HasMax)
        {
            return $"범위 {EffectiveMin:G}~{EffectiveMax:G}";
        }

        return HasMin ? $"{EffectiveMin:G} 이상" : $"{EffectiveMax:G} 이하";
    }
}
