using TMPro;
using UnityEngine;

/// <summary>
/// 튜토리얼 위치 사수 지점 위에 제목과 "배치된 동료 수 / 필요한 수"를 두 줄로 띄웁니다(예: 위치 사수 / 0/1). 글자는 늘 카메라를 향합니다.
/// </summary>
/// <remarks>
/// 위치 사수 지점(Defense Point) 아래 자식으로 두므로, 지점이 켜진 위치 사수 파트에서만 보입니다.
/// 세는 기준은 위치 사수 도착 조건(AllyInArea)과 같습니다. 조작 중이 아닌 동료 중 다운·이탈하지 않은 동료가 지점 반경 안에 있으면 셉니다. 높이는 보지 않습니다.
/// 지점을 가리키는 화살표(<see cref="m_arrow"/>)도 함께 맡습니다. 아직 다 채우지 않은 지점에만 화살표를 띄우고, 다 채우면 끕니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialAllyCountLabel : MonoBehaviour
{
    [Tooltip("숫자를 쓸 글자입니다. 비우면 같은 오브젝트의 TMP 글자를 씁니다.")]
    [SerializeField] private TMP_Text m_text;

    [Tooltip("숫자 위 줄에 쓸 제목입니다. 비우면 숫자만 씁니다.")]
    [SerializeField] private string m_title = "위치 사수(Q,E)";

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

    [Header("Arrow")]
    [Tooltip("지점을 가리키는 화살표입니다(오른쪽을 가리키는 스프라이트). 카메라를 향한 채 아래를 가리키고 위아래로 움직입니다. 다 채우면 끕니다. 비우면 화살표 없이 숫자만 띄웁니다.")]
    [SerializeField] private Transform m_arrow;

    [Tooltip("화살표가 위아래로 움직이는 폭(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_arrowBobHeight = 0.3f;

    [Tooltip("화살표가 위아래로 움직이는 빠르기(초당 왕복 수)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_arrowBobSpeed = 1.5f;

    private Vector3 m_arrowBasePosition;

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

        if (m_arrow != null)
        {
            m_arrowBasePosition = m_arrow.localPosition;
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
        UpdateArrow(camera, count < m_required);
        if (count == m_shownCount || m_text == null)
        {
            return;
        }

        m_shownCount = count;
        m_text.text = string.IsNullOrEmpty(m_title) ? $"{count}/{m_required}" : $"{m_title}\n{count}/{m_required}";
        m_text.color = count >= m_required ? m_doneColor : m_pendingColor;
    }

    /// <summary>아직 다 채우지 않았으면 화살표를 켜서 카메라를 향한 채 아래를 가리키며 위아래로 움직이고, 다 채웠으면 끕니다.</summary>
    private void UpdateArrow(Camera camera, bool show)
    {
        if (m_arrow == null)
        {
            return;
        }

        if (m_arrow.gameObject.activeSelf != show)
        {
            m_arrow.gameObject.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        // 창이 게임 시간을 멈춰도 움직이도록 실제 시간으로 셉니다.
        float bob = Mathf.Sin(Time.unscaledTime * m_arrowBobSpeed * Mathf.PI * 2.0f) * m_arrowBobHeight;
        m_arrow.localPosition = m_arrowBasePosition + Vector3.up * bob;
        if (camera != null)
        {
            // 스프라이트는 오른쪽을 가리키므로 화면 안에서 -90도 돌려 아래를 가리키게 합니다.
            m_arrow.rotation = camera.transform.rotation * Quaternion.Euler(0.0f, 0.0f, -90.0f);
        }
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
