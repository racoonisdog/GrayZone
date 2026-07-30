using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="ClampAttribute"/>가 붙은 필드의 Inspector 입력을 선언된 범위로 제한합니다.
/// </summary>
/// <remarks>
/// 같은 선언을 <see cref="BindManager"/>도 Bind 시점에 읽습니다. 여기는 사람이 손으로 넣는 경로를,
/// 그쪽은 SO/시트에서 들어오는 경로를 막습니다. 두 경로 모두 같은 선언 한 줄을 근거로 삼습니다.
///
/// 경계를 자르는 계산은 여기서 하지 않고 <see cref="ClampAttribute.Apply"/>에 맡깁니다.
/// 선언이 뒤집혔을 때의 처리 같은 규칙이 두 곳에 갈라지면 Inspector와 Bind의 결과가 달라지기 때문입니다.
///
/// 범위가 양쪽 다 있으면 슬라이더로 그립니다. 값의 폭을 눈으로 보는 편이 조정에 낫고,
/// 한쪽만 있으면 슬라이더의 반대쪽 끝을 정할 수 없어 일반 입력칸으로 둡니다.
/// </remarks>
[CustomPropertyDrawer(typeof(ClampAttribute))]
public class ClampAttributeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        ClampAttribute clamp = (ClampAttribute)attribute;

        if (property.propertyType != SerializedPropertyType.Integer
            && property.propertyType != SerializedPropertyType.Float)
        {
            // 숫자가 아닌 필드에 붙은 것은 선언 실수입니다. 조용히 기본 그리기로 넘기면 눈치채기 어렵습니다.
            EditorGUI.LabelField(position, label.text, "Clamp는 숫자 필드에만 사용할 수 있습니다.");
            return;
        }

        AppendRangeToTooltip(clamp, label);

        if (clamp.HasMin && clamp.HasMax)
        {
            DrawSlider(position, property, label, clamp);
            return;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(position, property, label);
        if (EditorGUI.EndChangeCheck())
        {
            ApplyClamp(property, clamp);
        }
    }

    /// <summary>범위 설명을 툴팁 끝에 덧붙입니다.</summary>
    /// <remarks>필드 설명과 허용 범위를 같이 보여 주어, 값을 넣기 전에 한계를 알 수 있게 합니다.</remarks>
    private static void AppendRangeToTooltip(ClampAttribute clamp, GUIContent label)
    {
        string range = clamp.DescribeRange();
        if (string.IsNullOrEmpty(range))
        {
            return;
        }

        label.tooltip = string.IsNullOrEmpty(label.tooltip) ? range : $"{label.tooltip}\n{range}";
    }

    /// <summary>양쪽 경계가 모두 있을 때 슬라이더로 그립니다.</summary>
    private static void DrawSlider(Rect position, SerializedProperty property, GUIContent label, ClampAttribute clamp)
    {
        if (property.propertyType == SerializedPropertyType.Integer)
        {
            EditorGUI.IntSlider(
                position, property, Mathf.RoundToInt((float)clamp.EffectiveMin), Mathf.RoundToInt((float)clamp.EffectiveMax), label);
            return;
        }

        EditorGUI.Slider(position, property, (float)clamp.EffectiveMin, (float)clamp.EffectiveMax, label);
    }

    /// <summary>입력된 값을 선언된 범위로 자릅니다.</summary>
    private static void ApplyClamp(SerializedProperty property, ClampAttribute clamp)
    {
        if (property.propertyType == SerializedPropertyType.Integer)
        {
            property.intValue = Mathf.RoundToInt((float)clamp.Apply(property.intValue, out _));
            return;
        }

        property.floatValue = (float)clamp.Apply(property.floatValue, out _);
    }
}
