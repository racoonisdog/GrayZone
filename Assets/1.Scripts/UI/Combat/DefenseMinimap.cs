using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방어전 맵 이미지 위에 분대원, 적, 함정 위치를 표시하는 미니맵입니다.
/// </summary>
/// <remarks>
/// 월드 좌표의 높이(Y)는 버리고 XZ만 맵 이미지에 옮깁니다. 이미지 중심의 월드 위치, 이미지가 덮는 거리,
/// 이미지 위쪽이 가리키는 월드 방향 세 값으로 변환합니다.
/// 분대원은 바라보는 방향으로 아이콘을 돌리고, 적은 위치만 표시합니다.
/// 함정은 시작할 때 메시 범위로 위치와 긴 방향을 한 번 계산하고, 설치 여부에 따라 색만 바꿉니다.
/// 아이콘 그림은 Sprite가 아닌 기본 텍스처로 가져와져 있어 RawImage로 그립니다(3.Resources 가져오기 설정을 바꾸지 않기 위해서입니다).
/// </remarks>
public class DefenseMinimap : MonoBehaviour
{
    /// <summary>미니맵에서 아이콘을 고르는 함정 분류입니다.</summary>
    public enum TrapKind
    {
        Spike,
        Wire,
        Fire,
        Mine,
    }

    [Serializable]
    private struct TrapIconSetting
    {
        [Tooltip("이 아이콘을 쓸 함정 분류입니다.")]
        public TrapKind kind;

        [Tooltip("함정 아이콘 그림입니다.")]
        public Texture icon;

        [Tooltip("켜면 함정 메시의 긴 방향에 맞춰 아이콘을 돌립니다. 철조망·가시 함정처럼 길쭉한 함정에 씁니다.")]
        public bool alignToTrap;

        [Tooltip("아이콘 그림에서 긴 방향이 화면 위쪽 기준 시계 방향으로 몇 도 기울어 있는지입니다. alignToTrap이 켜져 있을 때만 씁니다.")]
        public float artAngle;
    }

    private sealed class TrapMarker
    {
        public Trap trap;
        public RawImage image;
    }

    [Header("Map")]
    [Tooltip("방어전 맵 이미지(RawImage)의 RectTransform입니다. 마커는 이 아래에 만들어집니다. 비워 두면 이 오브젝트를 씁니다.")]
    [SerializeField] private RectTransform m_mapRect;

    [Tooltip("맵 이미지 중심에 해당하는 월드 위치(x = 월드 X, y = 월드 Z)입니다.")]
    [SerializeField] private Vector2 m_worldCenter = new Vector2(50.0f, 45.0f);

    [Tooltip("맵 이미지 가로·세로가 덮는 월드 거리(m)입니다. x = 이미지 가로, y = 이미지 세로.")]
    [SerializeField] private Vector2 m_worldSize = new Vector2(100.0f, 136.0f);

    [Tooltip("맵 이미지 위쪽이 가리키는 월드 방향(도)입니다. 0이면 +Z, 90이면 +X가 화면 위쪽입니다.")]
    [SerializeField] private float m_upYaw;

    [Tooltip("아이콘 크기 배율입니다. 아이콘 원본 픽셀 크기에 곱합니다. 맵 이미지 원본과 표시 크기가 같으면 1입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_iconScale = 1.0f;

    [Header("Squad")]
    [Tooltip("분대원 아이콘입니다. SquadManager의 분대원 순서와 같은 순서입니다. 비어 있는 칸의 분대원은 표시하지 않습니다.")]
    [SerializeField] private Texture[] m_memberIcons;

    [Tooltip("켜면 이탈(사망)한 분대원 아이콘을 숨깁니다.")]
    [SerializeField] private bool m_hideDeadMembers = true;

    [Tooltip("플레이어가 조종하지 않는 분대원 아이콘의 불투명도(0~1)입니다. 조종 중인 분대원은 항상 1입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_aiMemberAlpha = 0.7f;

    [Header("Enemy")]
    [Tooltip("적 아이콘입니다. 방향 없이 위치만 표시합니다.")]
    [SerializeField] private Texture m_enemyIcon;

    [Tooltip("살아 있는 적 목록을 다시 찾는 간격(초)입니다. 위치는 매 프레임 갱신합니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_enemyRefreshInterval = 0.25f;

    [Header("Trap")]
    [Tooltip("함정 분류별 아이콘입니다.")]
    [SerializeField] private TrapIconSetting[] m_trapIcons;

    [Tooltip("설치된 함정 아이콘 색입니다. 아이콘 원본 색에 곱합니다.")]
    [SerializeField] private Color m_builtColor = Color.white;

    [Tooltip("설치 안 된(청사진) 함정 아이콘 색입니다. 아이콘 원본 색에 곱합니다. 기본값은 디자인 시안의 청사진 투명도(128/209)입니다.")]
    [SerializeField] private Color m_blueprintColor = new Color(1.0f, 1.0f, 1.0f, 0.61f);

    [Tooltip("함정 목록을 다시 찾는 간격(초)입니다. 함정 설치 상태는 매 프레임 반영합니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_trapRefreshInterval = 2.0f;

    [Header("Navigation")]
    [Tooltip("경로를 읽어 올 경로 안내입니다. 비워 두면 씬에서 찾습니다. 경로는 여기서 다시 계산하지 않고 그쪽 계산 결과를 그대로 그립니다.")]
    [SerializeField] private ObjectivePathArrow m_pathArrow;

    [Tooltip("미니맵 경로선 색입니다. 알파에는 경로 안내의 페이드 값이 곱해집니다.")]
    [SerializeField] private Color m_pathColor = new Color(1.0f, 0.82f, 0.2f, 1.0f);

    [Tooltip("미니맵 경로선 두께(UI 단위)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_pathThickness = 2.0f;

    private RectTransform m_pathLayer;
    private RectTransform m_trapLayer;
    private RectTransform m_enemyLayer;
    private RectTransform m_squadLayer;
    private MinimapPathGraphic m_pathGraphic;
    private readonly List<Vector2> m_pathPoints = new List<Vector2>();
    private bool m_pathArrowSearched;

    private readonly List<TrapMarker> m_trapMarkers = new List<TrapMarker>();
    private readonly HashSet<Trap> m_knownTraps = new HashSet<Trap>();
    private readonly List<RawImage> m_memberMarkers = new List<RawImage>();
    private readonly List<RawImage> m_enemyMarkers = new List<RawImage>();
    private readonly List<EnemyHealth> m_enemies = new List<EnemyHealth>();

    private float m_nextEnemyRefreshTime;
    private float m_nextTrapRefreshTime;

    private void Awake()
    {
        if (m_mapRect == null)
        {
            m_mapRect = transform as RectTransform;
        }

        // 위에 보이는 순서: 1 조종 중인 플레이어 → 2 AI 분대원 → 3 미션 오브젝트(함정) → 4 적 → 5 경로선.
        // 나중에 만든 레이어가 위에 그려지므로 아래쪽부터 만듭니다. 플레이어와 AI는 Squad 레이어 안에서 플레이어를 맨 뒤 형제로 둡니다.
        m_pathLayer = CreateLayer("Navigation");
        m_enemyLayer = CreateLayer("Enemies");
        m_trapLayer = CreateLayer("MissionObjects");
        m_squadLayer = CreateLayer("Squad");

        GameObject pathObject = new GameObject("Path", typeof(RectTransform), typeof(CanvasRenderer), typeof(MinimapPathGraphic));
        pathObject.layer = m_pathLayer.gameObject.layer;
        RectTransform pathRect = (RectTransform)pathObject.transform;
        pathRect.SetParent(m_pathLayer, false);
        pathRect.anchorMin = new Vector2(0.5f, 0.5f);
        pathRect.anchorMax = new Vector2(0.5f, 0.5f);
        pathRect.sizeDelta = Vector2.zero;
        m_pathGraphic = pathObject.GetComponent<MinimapPathGraphic>();
        m_pathGraphic.raycastTarget = false;
        m_pathGraphic.Thickness = m_pathThickness;
        m_pathGraphic.enabled = false;
    }

    private void LateUpdate()
    {
        if (m_mapRect == null)
        {
            return;
        }

        if (Time.unscaledTime >= m_nextTrapRefreshTime)
        {
            m_nextTrapRefreshTime = Time.unscaledTime + m_trapRefreshInterval;
            RefreshTrapList();
        }

        UpdateTraps();
        UpdateEnemies();
        UpdateSquad();
        UpdatePath();
    }

    /// <summary>
    /// 월드 위치를 맵 이미지 정규화 좌표(왼쪽 아래 0, 오른쪽 위 1)로 바꿉니다. 높이(Y)는 무시합니다.
    /// </summary>
    /// <param name="worldPosition">변환할 월드 위치입니다.</param>
    /// <returns>x = 가로, y = 세로 정규화 좌표입니다. 맵 밖이면 0~1을 벗어납니다.</returns>
    public Vector2 WorldToNormalized(Vector3 worldPosition)
    {
        Vector2 offset = new Vector2(worldPosition.x - m_worldCenter.x, worldPosition.z - m_worldCenter.y);
        float rad = m_upYaw * Mathf.Deg2Rad;
        // yaw φ의 월드 방향 = (sin φ, cos φ). 위쪽 = upYaw, 오른쪽 = upYaw + 90
        Vector2 up = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        Vector2 right = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));

        float width = Mathf.Max(0.01f, m_worldSize.x);
        float height = Mathf.Max(0.01f, m_worldSize.y);
        return new Vector2(
            0.5f + Vector2.Dot(offset, right) / width,
            0.5f + Vector2.Dot(offset, up) / height);
    }

    /// <summary>월드 yaw(도)를 맵 UI의 z 회전(도)으로 바꿉니다. 화면 위쪽이 0이고 반시계가 양수입니다.</summary>
    private float WorldYawToUiAngle(float worldYaw)
    {
        return -(worldYaw - m_upYaw);
    }

    private bool PlaceMarker(RectTransform marker, Vector3 worldPosition)
    {
        Vector2 uv = WorldToNormalized(worldPosition);
        bool inside = uv.x >= 0.0f && uv.x <= 1.0f && uv.y >= 0.0f && uv.y <= 1.0f;
        marker.anchoredPosition = NormalizedToLocal(uv);
        return inside;
    }

    /// <summary>정규화 좌표를 맵 이미지 중심 기준 UI 좌표로 바꿉니다.</summary>
    private Vector2 NormalizedToLocal(Vector2 uv)
    {
        Rect rect = m_mapRect.rect;
        return new Vector2((uv.x - 0.5f) * rect.width, (uv.y - 0.5f) * rect.height);
    }

    #region Navigation

    /// <summary>
    /// 경로 안내가 마지막으로 계산한 꺾임점을 높이를 버리고 미니맵에 잇습니다.
    /// </summary>
    /// <remarks>
    /// 경로를 다시 계산하지 않습니다. 첫 점만 지금 조종 중인 캐릭터 위치로 바꿉니다(바닥 경로선과 같은 방식).
    /// 경로선이 페이드되면 미니맵 선도 같이 흐려집니다.
    /// </remarks>
    private void UpdatePath()
    {
        if (m_pathGraphic == null)
        {
            return;
        }

        if (m_pathArrow == null && !m_pathArrowSearched)
        {
            m_pathArrowSearched = true;
            m_pathArrow = FindFirstObjectByType<ObjectivePathArrow>(FindObjectsInactive.Include);
        }

        IReadOnlyList<Vector3> corners = m_pathArrow != null ? m_pathArrow.PathCorners : null;
        float fade = m_pathArrow != null && m_pathArrow.isActiveAndEnabled && m_pathArrow.HasPath ? m_pathArrow.PathFade : 0.0f;
        if (fade <= 0.0f || corners == null || corners.Count < 2)
        {
            SetVisible(m_pathGraphic, false);
            return;
        }

        SquadManager squad = SquadManager.Instance;
        SquadMemberController player = squad != null ? squad.PlayerSquadMember : null;

        m_pathPoints.Clear();
        m_pathPoints.Add(NormalizedToLocal(WorldToNormalized(player != null ? player.transform.position : corners[0])));
        for (int i = 1; i < corners.Count; i++)
        {
            m_pathPoints.Add(NormalizedToLocal(WorldToNormalized(corners[i])));
        }

        m_pathGraphic.SetPoints(m_pathPoints);
        Color color = m_pathColor;
        color.a *= fade;
        m_pathGraphic.color = color;
        SetVisible(m_pathGraphic, true);
    }

    #endregion

    #region Squad

    private void UpdateSquad()
    {
        SquadManager squad = SquadManager.Instance;
        IReadOnlyList<SquadMemberController> members = squad != null ? squad.SquadMembers : null;
        int count = members != null ? members.Count : 0;

        for (int i = 0; i < count; i++)
        {
            RawImage marker = GetMemberMarker(i);
            if (marker == null)
            {
                continue;
            }

            SquadMemberController member = members[i];
            bool visible = member != null && member.isActiveAndEnabled && (!m_hideDeadMembers || member.IsAlive);
            if (visible)
            {
                visible = PlaceMarker(marker.rectTransform, member.transform.position);
                marker.rectTransform.localEulerAngles = new Vector3(0.0f, 0.0f, WorldYawToUiAngle(member.transform.eulerAngles.y));

                Color color = new Color(1.0f, 1.0f, 1.0f, i == squad.PlayerSquadMemberIndex ? 1.0f : m_aiMemberAlpha);
                if (marker.color != color)
                {
                    marker.color = color;
                }
            }

            SetVisible(marker, visible);
        }

        for (int i = count; i < m_memberMarkers.Count; i++)
        {
            SetVisible(m_memberMarkers[i], false);
        }

        // 조작 중인 분대원을 맨 위에 그립니다.
        RawImage playerMarker = squad != null ? GetExistingMemberMarker(squad.PlayerSquadMemberIndex) : null;
        if (playerMarker != null && playerMarker.transform.GetSiblingIndex() != m_squadLayer.childCount - 1)
        {
            playerMarker.transform.SetAsLastSibling();
        }
    }

    private RawImage GetExistingMemberMarker(int index)
    {
        return index >= 0 && index < m_memberMarkers.Count ? m_memberMarkers[index] : null;
    }

    private RawImage GetMemberMarker(int index)
    {
        while (m_memberMarkers.Count <= index)
        {
            int slot = m_memberMarkers.Count;
            Texture icon = m_memberIcons != null && slot < m_memberIcons.Length ? m_memberIcons[slot] : null;
            // 아이콘의 뾰족한 끝(위쪽)이 바라보는 방향이므로 중심을 둥근 부분에 둡니다.
            m_memberMarkers.Add(icon != null ? CreateMarker(m_squadLayer, "Member_" + slot, icon, new Vector2(0.5f, 0.3125f)) : null);
        }

        return m_memberMarkers[index];
    }

    #endregion

    #region Enemy

    private void UpdateEnemies()
    {
        if (m_enemyIcon == null)
        {
            return;
        }

        if (Time.unscaledTime >= m_nextEnemyRefreshTime)
        {
            m_nextEnemyRefreshTime = Time.unscaledTime + m_enemyRefreshInterval;
            m_enemies.Clear();
            m_enemies.AddRange(FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        int shown = 0;
        for (int i = 0; i < m_enemies.Count; i++)
        {
            EnemyHealth enemy = m_enemies[i];
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead)
            {
                continue;
            }

            while (m_enemyMarkers.Count <= shown)
            {
                m_enemyMarkers.Add(CreateMarker(m_enemyLayer, "Enemy_" + m_enemyMarkers.Count, m_enemyIcon, new Vector2(0.5f, 0.5f)));
            }

            RawImage marker = m_enemyMarkers[shown];
            if (PlaceMarker(marker.rectTransform, enemy.transform.position))
            {
                SetVisible(marker, true);
                shown++;
            }
        }

        for (int i = shown; i < m_enemyMarkers.Count; i++)
        {
            SetVisible(m_enemyMarkers[i], false);
        }
    }

    #endregion

    #region Trap

    private void RefreshTrapList()
    {
        Trap[] traps = FindObjectsByType<Trap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < traps.Length; i++)
        {
            Trap trap = traps[i];
            if (trap == null || m_knownTraps.Contains(trap))
            {
                continue;
            }

            m_knownTraps.Add(trap);
            TrapIconSetting setting;
            if (!TryGetTrapIcon(GetTrapKind(trap), out setting))
            {
                continue;
            }

            Vector3 center;
            float longYaw;
            GetTrapFootprint(trap, out center, out longYaw);

            RawImage image = CreateMarker(m_trapLayer, "Trap_" + trap.name, setting.icon, new Vector2(0.5f, 0.5f));
            if (!PlaceMarker(image.rectTransform, center))
            {
                // 맵 밖 함정(테스트용 등)은 표시하지 않습니다.
                Destroy(image.gameObject);
                continue;
            }

            if (setting.alignToTrap)
            {
                // 그림의 긴 방향(artAngle)이 함정의 긴 방향(longYaw)과 겹치도록 돌립니다.
                image.rectTransform.localEulerAngles = new Vector3(0.0f, 0.0f, WorldYawToUiAngle(longYaw) + setting.artAngle);
            }

            m_trapMarkers.Add(new TrapMarker { trap = trap, image = image });
        }
    }

    private void UpdateTraps()
    {
        for (int i = m_trapMarkers.Count - 1; i >= 0; i--)
        {
            TrapMarker marker = m_trapMarkers[i];
            if (marker.trap == null)
            {
                if (marker.image != null)
                {
                    Destroy(marker.image.gameObject);
                }

                m_trapMarkers.RemoveAt(i);
                continue;
            }

            bool visible = marker.trap.gameObject.activeInHierarchy;
            SetVisible(marker.image, visible);
            if (visible)
            {
                Color color = marker.trap.IsBuilt ? m_builtColor : m_blueprintColor;
                if (marker.image.color != color)
                {
                    marker.image.color = color;
                }
            }
        }
    }

    private static TrapKind GetTrapKind(Trap trap)
    {
        // SpikeTrap은 WireTrap을 상속하므로 먼저 확인합니다.
        if (trap is SpikeTrap)
        {
            return TrapKind.Spike;
        }

        if (trap is WireTrap)
        {
            return TrapKind.Wire;
        }

        if (trap is FireBarrelTrap || trap is FireTrap)
        {
            return TrapKind.Fire;
        }

        return TrapKind.Mine;
    }

    private bool TryGetTrapIcon(TrapKind kind, out TrapIconSetting setting)
    {
        if (m_trapIcons != null)
        {
            for (int i = 0; i < m_trapIcons.Length; i++)
            {
                if (m_trapIcons[i].kind == kind && m_trapIcons[i].icon != null)
                {
                    setting = m_trapIcons[i];
                    return true;
                }
            }
        }

        setting = default;
        return false;
    }

    /// <summary>
    /// 함정 메시(비활성 포함)의 바닥 투영으로 중심과 긴 방향(월드 yaw)을 구합니다.
    /// </summary>
    /// <remarks>
    /// 여러 함정이 같은 그룹 루트 위치를 공유하고 실제 메시는 자식에서 떨어져 있는 경우가 있어,
    /// 루트 위치 대신 메시 범위를 씁니다. 메시가 없으면 루트 위치와 회전을 씁니다.
    /// </remarks>
    private static void GetTrapFootprint(Trap trap, out Vector3 center, out float longYaw)
    {
        MeshFilter[] filters = trap.GetComponentsInChildren<MeshFilter>(true);
        List<Vector2> points = new List<Vector2>(filters.Length * 8);
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            Bounds local = mesh.bounds;
            Matrix4x4 toWorld = filters[i].transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(
                    (c & 1) == 0 ? -1.0f : 1.0f,
                    (c & 2) == 0 ? -1.0f : 1.0f,
                    (c & 4) == 0 ? -1.0f : 1.0f));
                Vector3 world = toWorld.MultiplyPoint3x4(corner);
                points.Add(new Vector2(world.x, world.z));
            }
        }

        if (points.Count == 0)
        {
            center = trap.transform.position;
            longYaw = trap.transform.eulerAngles.y;
            return;
        }

        Vector2 min = points[0];
        Vector2 max = points[0];
        Vector2 mean = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
            mean += points[i];
        }

        mean /= points.Count;
        Vector2 mid = (min + max) * 0.5f;
        center = new Vector3(mid.x, trap.transform.position.y, mid.y);

        // 바닥 투영 점들의 주축(분산이 가장 큰 방향)을 긴 방향으로 씁니다.
        float sxx = 0.0f;
        float szz = 0.0f;
        float sxz = 0.0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 d = points[i] - mean;
            sxx += d.x * d.x;
            szz += d.y * d.y;
            sxz += d.x * d.y;
        }

        // x-z 평면 각도(+X 기준 반시계) → yaw(+Z 기준 시계)
        float angleFromX = 0.5f * Mathf.Atan2(2.0f * sxz, sxx - szz) * Mathf.Rad2Deg;
        longYaw = 90.0f - angleFromX;
    }

    #endregion

    private RectTransform CreateLayer(string layerName)
    {
        GameObject go = new GameObject(layerName, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(m_mapRect, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.layer = m_mapRect.gameObject.layer;
        return rt;
    }

    private RawImage CreateMarker(RectTransform parent, string markerName, Texture icon, Vector2 pivot)
    {
        GameObject go = new GameObject(markerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.layer = parent.gameObject.layer;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(icon.width, icon.height) * m_iconScale;

        RawImage image = go.GetComponent<RawImage>();
        image.texture = icon;
        image.raycastTarget = false;
        return image;
    }

    private static void SetVisible(Graphic graphic, bool visible)
    {
        if (graphic != null && graphic.enabled != visible)
        {
            graphic.enabled = visible;
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 맵 이미지가 덮는 월드 영역을 m_gizmoHeight 높이에 그립니다. 노란 선은 화면 위쪽 방향입니다.
        float rad = m_upYaw * Mathf.Deg2Rad;
        Vector3 up = new Vector3(Mathf.Sin(rad), 0.0f, Mathf.Cos(rad)) * (m_worldSize.y * 0.5f);
        Vector3 right = new Vector3(Mathf.Cos(rad), 0.0f, -Mathf.Sin(rad)) * (m_worldSize.x * 0.5f);
        Vector3 c = new Vector3(m_worldCenter.x, m_gizmoHeight, m_worldCenter.y);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(c - right - up, c + right - up);
        Gizmos.DrawLine(c + right - up, c + right + up);
        Gizmos.DrawLine(c + right + up, c - right + up);
        Gizmos.DrawLine(c - right + up, c - right - up);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(c, c + up);
    }

    [Header("Debug")]
    [Tooltip("에디터 전용입니다. 선택했을 때 맵 영역 기즈모를 그릴 월드 높이(m)입니다.")]
    [SerializeField] private float m_gizmoHeight = 200.0f;
}
