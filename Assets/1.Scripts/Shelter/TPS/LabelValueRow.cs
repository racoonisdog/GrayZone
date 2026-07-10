using UnityEngine;
using TMPro;

/// <summary>
/// 업그레이드 창의 기능/비용 리스트에 공용으로 쓰는 한 줄(라벨 + 값) 항목입니다.
/// </summary>
/// <remarks>
/// 프리팹에 라벨/값 TMP_Text를 연결해 두고, 컨트롤러가 <see cref="Set"/>로 내용을 채웁니다.
/// </remarks>
public class LabelValueRow : MonoBehaviour
{
    [SerializeField] private TMP_Text m_label;
    [SerializeField] private TMP_Text m_value;

    /// <summary>행 내용을 설정합니다. 값 색으로 부족/충족 등을 표시할 수 있습니다.</summary>
    public void Set(string label, string value, Color valueColor)
    {
        if (m_label != null)
            m_label.text = label;

        if (m_value != null)
        {
            m_value.text = value;
            m_value.color = valueColor;
        }
    }
}
