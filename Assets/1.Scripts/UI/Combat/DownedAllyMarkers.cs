using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 다운돼 구조를 기다리는 분대원 머리 위에 구조 마커를 띄웁니다. 튜토리얼과 본 방어전 모두에서 동작합니다.
/// </summary>
/// <remarks>
/// 마커 모양은 튜토리얼 목표 마커와 같은 <see cref="ObjectiveIndicatorController"/> 템플릿을 복제해 아이콘만 바꿉니다.
/// 화면 밖이면 템플릿 설정대로 가장자리 방향 화살표가 나옵니다. 대원이 구조돼 일어나거나 전투에서 이탈하면 마커를 내립니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DownedAllyMarkers : MonoBehaviour
{
    [Tooltip("대원마다 복제할 마커입니다(튜토리얼 목표 마커 템플릿과 같은 것). 씬에서는 꺼 둡니다.")]
    [SerializeField] private ObjectiveIndicatorController m_markerTemplate;

    [Tooltip("구조 마커에 쓸 이미지입니다. 비우면 템플릿의 기본 이미지를 씁니다.")]
    [SerializeField] private Sprite m_icon;

    [Tooltip("마커 크기 배율입니다. 1이면 템플릿 크기 그대로입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_scale = 1.0f;

    [Tooltip("테두리는 두고 안쪽 아이콘만 곱할 배율입니다. 아이콘이 테두리 밖으로 나가면 1보다 작게 둡니다.")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float m_iconScale = 1.0f;

    [Tooltip("테두리는 두고 안쪽 아이콘만 옮길 거리(마커 UI 단위)입니다. 테두리 아래 화살표 때문에 마름모 중심이 마커 가운데보다 위라서 올려 맞춥니다.")]
    [SerializeField] private Vector2 m_iconOffset = new(0.0f, 3.5f);

    [Tooltip("마커를 대원의 보이는 모습 맨 위에서 이만큼(m) 더 띄웁니다. 음수면 그만큼 아래입니다.")]
    [SerializeField] private float m_height = -1.0f;

    private readonly Dictionary<SquadMemberController, Marker> m_markers = new();
    private readonly List<SquadMemberController> m_removeBuffer = new();

    private sealed class Marker
    {
        public ObjectiveIndicatorController View;
        public Transform Anchor;
    }

    private void LateUpdate()
    {
        SquadManager squad = SquadManager.Instance;
        if (squad == null || m_markerTemplate == null)
        {
            return;
        }

        foreach (SquadMemberController member in squad.SquadMembers)
        {
            if (member == null)
            {
                continue;
            }

            PlayerHealth health = member.GetComponent<PlayerHealth>();
            bool waiting = health != null && health.IsDowned && !health.IsDead;
            if (waiting && !m_markers.ContainsKey(member))
            {
                m_markers[member] = CreateMarker(member);
            }
            else if (!waiting && m_markers.ContainsKey(member))
            {
                DestroyMarker(member);
            }
        }

        m_removeBuffer.Clear();
        foreach (KeyValuePair<SquadMemberController, Marker> pair in m_markers)
        {
            if (pair.Key == null)
            {
                m_removeBuffer.Add(pair.Key);
                continue;
            }

            pair.Value.Anchor.position = WorldMarkerAnchor.GetTopCenter(pair.Key.transform, m_height);
        }

        foreach (SquadMemberController member in m_removeBuffer)
        {
            DestroyMarker(member);
        }
    }

    private void OnDisable()
    {
        m_removeBuffer.Clear();
        m_removeBuffer.AddRange(m_markers.Keys);
        foreach (SquadMemberController member in m_removeBuffer)
        {
            DestroyMarker(member);
        }
    }

    private Marker CreateMarker(SquadMemberController member)
    {
        Transform anchor = new GameObject($"DownedAllyMarkerAnchor_{member.name}").transform;
        ObjectiveIndicatorController view = Instantiate(m_markerTemplate, m_markerTemplate.transform.parent);
        view.name = $"DownedAllyMarker_{member.name}";
        view.gameObject.SetActive(true);
        view.SetTarget(anchor);
        view.SetOnscreenMarkerSprite(m_icon);
        view.SetOnscreenMarkerScale(m_scale);
        view.SetOnscreenIconScale(m_iconScale);
        view.SetOnscreenIconOffset(m_iconOffset);
        view.SetVisible(true);
        anchor.position = WorldMarkerAnchor.GetTopCenter(member.transform, m_height);
        return new Marker { View = view, Anchor = anchor };
    }

    private void DestroyMarker(SquadMemberController member)
    {
        if (!m_markers.TryGetValue(member, out Marker marker))
        {
            return;
        }

        m_markers.Remove(member);
        if (marker.View != null)
        {
            Destroy(marker.View.gameObject);
        }

        if (marker.Anchor != null)
        {
            Destroy(marker.Anchor.gameObject);
        }
    }
}
