using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 다운돼 구조를 기다리는 분대원 머리 위에 구조 마커를 띄웁니다. 튜토리얼과 본 방어전 모두에서 동작합니다.
/// </summary>
/// <remarks>
/// 마커 생김새는 프리팹(<see cref="m_markerPrefab"/>)에 미리 만들어 둔 그대로 씁니다. 이 컴포넌트는 대원 머리 위 높이(<see cref="m_height"/>)만 정합니다.
/// 화면 밖이면 템플릿 설정대로 가장자리 방향 화살표가 나옵니다. 대원이 구조돼 일어나거나 전투에서 이탈하면 마커를 내립니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DownedAllyMarkers : MonoBehaviour
{
    [Tooltip("대원마다 만들 구조 마커 프리팹입니다(Assets/2.Prefabs/UI/Marker/Marker_Rescue). 아이콘·크기·모양은 프리팹에서 고칩니다.")]
    [FormerlySerializedAs("m_markerTemplate")]
    [SerializeField] private ObjectiveIndicatorController m_markerPrefab;

    [Tooltip("만든 마커를 둘 부모입니다. 비우면 씬 맨 위에 둡니다. 마커는 자체 화면 캔버스를 가져 다른 캔버스 아래에 두지 않습니다.")]
    [SerializeField] private Transform m_markerParent;

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
        if (squad == null || m_markerPrefab == null)
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
        ObjectiveIndicatorController view = Instantiate(m_markerPrefab, m_markerParent);
        view.name = $"DownedAllyMarker_{member.name}";
        view.gameObject.SetActive(true);
        view.SetTarget(anchor);
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
