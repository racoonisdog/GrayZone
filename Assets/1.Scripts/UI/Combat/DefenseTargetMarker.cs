using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 방어 목표(정문) 위에 방어선 마커를 띄웁니다. 기본 게임 내내 보입니다. 튜토리얼은 초반에 이 마커를 꺼 두었다가 정문 안내 때 다시 켭니다.
/// </summary>
/// <remarks>
/// 마커 생김새는 프리팹(<see cref="m_markerPrefab"/>)에 미리 만들어 둔 그대로 씁니다. 이 컴포넌트는 목표 위 높이(<see cref="m_height"/>)만 정합니다.
/// 마커 위치는 목표의 보이는 모습(켜진 렌더러) 맨 위를 따라갑니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseTargetMarker : MonoBehaviour
{
    [Tooltip("만들 방어선 마커 프리팹입니다(Assets/2.Prefabs/UI/Marker/Marker_DefensePoint). 아이콘·크기·모양은 프리팹에서 고칩니다.")]
    [FormerlySerializedAs("m_markerTemplate")]
    [SerializeField] private ObjectiveIndicatorController m_markerPrefab;

    [Tooltip("만든 마커를 둘 부모입니다. 비우면 씬 맨 위에 둡니다. 마커는 자체 화면 캔버스를 가져 다른 캔버스 아래에 두지 않습니다.")]
    [SerializeField] private Transform m_markerParent;

    [Tooltip("마커를 띄울 방어 목표(정문)입니다.")]
    [SerializeField] private Transform m_target;

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
        if (m_markerPrefab == null || m_target == null)
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
        m_view = Instantiate(m_markerPrefab, m_markerParent);
        m_view.name = $"DefenseTargetMarker_{m_target.name}";
        m_view.gameObject.SetActive(true);
        m_view.SetTarget(m_anchor);
        m_view.SetVisible(false);
    }
}
