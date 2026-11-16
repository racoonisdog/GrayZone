using UnityEngine;

/// <summary>
/// 방어 목표(정문) 위에 방어선 마커를 띄웁니다. 기본 게임 내내 보입니다. 튜토리얼은 초반에 이 마커를 꺼 두었다가 정문 안내 때 다시 켭니다.
/// </summary>
/// <remarks>
/// 마커 모양은 튜토리얼 목표 마커와 같은 <see cref="ObjectiveIndicatorController"/> 템플릿을 복제해 아이콘만 바꿉니다.
/// 마커 위치는 목표의 보이는 모습(켜진 렌더러) 맨 위를 따라갑니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseTargetMarker : MonoBehaviour
{
    [Tooltip("복제할 마커입니다(튜토리얼 목표 마커 템플릿과 같은 것). 씬에서는 꺼 둡니다.")]
    [SerializeField] private ObjectiveIndicatorController m_markerTemplate;

    [Tooltip("마커를 띄울 방어 목표(정문)입니다.")]
    [SerializeField] private Transform m_target;

    [Tooltip("마커에 쓸 이미지입니다. 비우면 템플릿의 기본 이미지를 씁니다.")]
    [SerializeField] private Sprite m_icon;

    [Tooltip("마커 크기 배율입니다. 1이면 템플릿 크기 그대로입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_scale = 1.25f;

    [Tooltip("테두리는 두고 안쪽 아이콘만 곱할 배율입니다. 아이콘이 테두리 밖으로 나가면 1보다 작게 둡니다.")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float m_iconScale = 0.45f;

    [Tooltip("테두리는 두고 안쪽 아이콘만 옮길 거리(마커 UI 단위)입니다. 테두리 아래 화살표 때문에 마름모 중심이 마커 가운데보다 위라서 올려 맞춥니다.")]
    [SerializeField] private Vector2 m_iconOffset = new(0.0f, 3.5f);

    [Tooltip("마커를 목표의 보이는 모습 맨 위에서 이만큼(m) 더 띄웁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_height = 0.5f;

    /// <summary>튜토리얼이 꺼 둔 상태인지 여부입니다.</summary>
    private bool m_hidden;

    private ObjectiveIndicatorController m_view;
    private Transform m_anchor;

    /// <summary>마커를 켜거나 끕니다. 튜토리얼이 정문 안내 전까지 꺼 둘 때 씁니다.</summary>
    public void SetShown(bool shown)
    {
        m_hidden = !shown;
    }

    private void LateUpdate()
    {
        if (m_markerTemplate == null || m_target == null)
        {
            return;
        }

        if (m_view == null)
        {
            CreateMarker();
        }

        m_anchor.position = WorldMarkerAnchor.GetTopCenter(m_target, m_height);
        m_view.SetVisible(ShouldShow());
    }

    private void OnDestroy()
    {
        if (m_view != null)
        {
            Destroy(m_view.gameObject);
        }

        if (m_anchor != null)
        {
            Destroy(m_anchor.gameObject);
        }
    }

    private bool ShouldShow()
    {
        return !m_hidden && m_target.gameObject.activeInHierarchy;
    }

    private void CreateMarker()
    {
        m_anchor = new GameObject($"DefenseTargetMarkerAnchor_{m_target.name}").transform;
        m_view = Instantiate(m_markerTemplate, m_markerTemplate.transform.parent);
        m_view.name = $"DefenseTargetMarker_{m_target.name}";
        m_view.gameObject.SetActive(true);
        m_view.SetTarget(m_anchor);
        m_view.SetOnscreenMarkerSprite(m_icon);
        m_view.SetOnscreenMarkerScale(m_scale);
        m_view.SetOnscreenIconScale(m_iconScale);
        m_view.SetOnscreenIconOffset(m_iconOffset);
        m_view.SetVisible(false);
    }
}
