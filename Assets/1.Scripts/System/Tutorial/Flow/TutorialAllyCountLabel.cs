using TMPro;
using UnityEngine;

/// <summary>
/// 튜토리얼 위치 사수 지점 위에 제목과 "배치된 동료 수 / 필요한 수"를 두 줄로 띄웁니다(예: 위치 사수 / 0/1). 글자는 늘 카메라를 향합니다.
/// </summary>
/// <remarks>
/// 위치 사수 지점(Defense Point) 아래 자식으로 두므로, 지점이 켜진 위치 사수 파트에서만 보입니다.
/// 세는 기준은 위치 사수 도착 조건(AllyInArea)과 같습니다. 조작 중이 아닌 동료 중 다운·이탈하지 않은 동료가 지점 반경 안에 있으면 셉니다. 높이는 보지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialAllyCountLabel : MonoBehaviour
{
    [Tooltip("숫자를 쓸 글자입니다. 비우면 같은 오브젝트의 TMP 글자를 씁니다.")]
    [SerializeField] private TMP_Text m_text;

    [Tooltip("숫자 위 줄에 쓸 제목입니다. 비우면 숫자만 씁니다.")]
    [SerializeField] private string m_title = "위치 사수";

    [Tooltip("셀 지점입니다. 비우면 부모(위치 사수 지점)를 씁니다.")]
    [SerializeField] private Transform m_center;

    [Tooltip("지점 반경(m)입니다. 위치 사수 도착 조건 트리거의 반경과 같게 둡니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_radius = 3.0f;

    [Tooltip("채워야 하는 동료 수입니다.")]
    [Min(1)]
    [SerializeField] private int m_required = 1;

    [Tooltip("다 채우기 전 글자 색입니다.")]
    [SerializeField] private Color m_pendingColor = Color.white;

    [Tooltip("다 채웠을 때 글자 색입니다.")]
    [SerializeField] private Color m_doneColor = new(0.3f, 1.0f, 0.45f, 1.0f);

    private int m_shownCount = -1;

    private void Awake()
    {
        if (m_text == null)
        {
            m_text = GetComponent<TMP_Text>();
        }

        if (m_center == null)
        {
            m_center = transform.parent != null ? transform.parent : transform;
        }
    }

    private void OnEnable()
    {
        m_shownCount = -1;
    }

    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera != null)
        {
            transform.rotation = camera.transform.rotation;
        }

        int count = Mathf.Min(CountAlliesInArea(), m_required);
        if (count == m_shownCount || m_text == null)
        {
            return;
        }

        m_shownCount = count;
        m_text.text = string.IsNullOrEmpty(m_title) ? $"{count}/{m_required}" : $"{m_title}\n{count}/{m_required}";
        m_text.color = count >= m_required ? m_doneColor : m_pendingColor;
    }

    private int CountAlliesInArea()
    {
        SquadManager squad = SquadManager.Instance;
        if (squad == null)
        {
            return 0;
        }

        int count = 0;
        foreach (SquadMemberController member in squad.SquadMembers)
        {
            if (member == null || member.IsPlayerSquadMember || !member.IsAlive || member.IsDown)
            {
                continue;
            }

            Vector3 offset = member.transform.position - m_center.position;
            offset.y = 0.0f;
            if (offset.sqrMagnitude <= m_radius * m_radius)
            {
                count++;
            }
        }

        return count;
    }
}
