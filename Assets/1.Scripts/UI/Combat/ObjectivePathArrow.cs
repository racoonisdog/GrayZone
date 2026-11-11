using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 조작 중인 캐릭터의 발밑에 목표 지점으로 가는 길의 방향을 화살표로 보여 줍니다.
/// </summary>
/// <remarks>
/// 목표를 향한 직선 방향이 아니라 NavMesh 경로를 따라간 방향을 가리킵니다. 벽 너머 목표를 직선으로 가리키면
/// 플레이어가 벽으로 걸어가기 때문입니다. 경로는 <see cref="NavMesh.CalculatePath(Vector3, Vector3, NavMeshQueryFilter, NavMeshPath)"/>로
/// 일정 간격마다 다시 계산하고, 캐릭터에서 경로를 따라 <see cref="m_lookAheadDistance"/>만큼 간 지점을 향합니다.
/// 첫 꺾임점만 보면 꺾임점 바로 앞에서 방향이 갑자기 바뀌기 때문입니다.
///
/// 캐릭터를 바꾸면 <see cref="SquadManager.PlayerSquadMember"/>를 따라 새 캐릭터 발밑으로 옮겨 갑니다.
/// 화살표 모양(메시, 데칼, 스프라이트)은 <see cref="m_arrowVisual"/>에 연결한 오브젝트가 맡습니다. 그 오브젝트의
/// 정면(파란 축)이 화살표 끝을 향하게 둡니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class ObjectivePathArrow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("길을 안내할 목표입니다. 실행 중에는 SetTarget으로 바꿉니다.")]
    [SerializeField] private Transform m_target;

    [Tooltip("목표 Transform에서 실제로 길을 찾을 위치를 보정합니다.")]
    [SerializeField] private Vector3 m_targetOffset;

    [Tooltip("테스트용 초기 표시 상태입니다. 실제 기능에서는 SetVisible로 제어합니다.")]
    [SerializeField] private bool m_visibleOnStart;

    [Header("Origin")]
    [Tooltip("화살표를 띄울 캐릭터입니다. 비워 두면 SquadManager의 조작 캐릭터를 따라갑니다.")]
    [SerializeField] private Transform m_originOverride;

    [Header("View")]
    [Tooltip("회전시키고 옮길 화살표 오브젝트입니다. 정면(파란 축)이 화살표 끝을 향해야 합니다. " +
             "이 컴포넌트가 붙은 오브젝트 자신은 넣지 않습니다(숨길 때 같이 꺼집니다). 비워 두면 경로 선만 그립니다.")]
    [SerializeField] private Transform m_arrowVisual;

    [Tooltip("캐릭터 중심에서 화살표까지의 수평 거리(m)입니다. 0이면 발밑 한가운데에 둡니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_ringRadius = 0.8f;

    [Tooltip("바닥에 묻히지 않도록 캐릭터 발 위치에서 띄울 높이(m)입니다.")]
    [SerializeField] private float m_heightOffset = 0.05f;

    [Tooltip("화살표가 새 방향으로 도는 최대 속도(도/초)입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_turnSpeed = 540.0f;

    [Tooltip("목표까지 남은 경로 길이(m)가 이보다 짧으면 화살표를 숨깁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_hideDistance = 3.0f;

    [Header("Path")]
    [Tooltip("경로를 찾고 선 높이를 맞출 NavMesh의 Agent Type 이름입니다. 적·팀원 AI가 쓰는 Humanoid와 따로, 장애물 윗면을 빼고 " +
             "Height Mesh를 켜서 구운 안내 전용 NavMesh를 씁니다. 이 이름이 없으면 Humanoid NavMesh를 씁니다.")]
    [SerializeField] private string m_agentTypeName = "PathGuide";

    [Tooltip("캐릭터에서 경로를 따라 이 거리(m)만큼 간 지점을 가리킵니다. 짧으면 꺾임점에서 방향이 급하게 바뀝니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_lookAheadDistance = 2.5f;

    [Tooltip("경로를 다시 계산하는 간격(초)입니다.")]
    [Min(0.02f)]
    [SerializeField] private float m_repathInterval = 0.25f;

    [Tooltip("캐릭터와 목표 위치를 NavMesh 위로 붙일 때 찾아볼 반경(m)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_sampleRadius = 2.0f;

    [Tooltip("켜면 목표까지 길이 끊겨 있을 때 갈 수 있는 데까지의 경로를 안내합니다. 끄면 화살표를 숨깁니다.")]
    [SerializeField] private bool m_allowPartialPath = true;

    [Header("Path Line")]
    [Tooltip("켜면 캐릭터에서 목표까지의 경로를 바닥에 선으로 그립니다. 선은 실행 중에 이 오브젝트 아래에 만듭니다.")]
    [SerializeField] private bool m_drawPathLine = true;

    [Tooltip("경로 선의 색입니다.")]
    [SerializeField] private Color m_pathLineColor = Color.red;

    [Tooltip("경로 선의 두께(m)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_pathLineWidth = 0.12f;

    [Tooltip("경로 선을 바닥에서 띄울 높이(m)입니다.")]
    [SerializeField] private float m_pathLineHeight = 0.1f;

    [Tooltip("경로 선에 쓸 머티리얼입니다. 비워 두면 Path Line Color 단색으로 그립니다. 넣으면 복사본을 만들어 쓰므로 " +
             "에셋 자체는 바뀌지 않습니다. 텍스처는 선 길이 방향(U)으로 펼쳐지고, 반복하려면 텍스처 Wrap Mode가 Repeat여야 합니다.")]
    [SerializeField] private Material m_pathLineMaterialOverride;

    [Tooltip("머티리얼 텍스처 한 장이 차지할 선 길이(m)입니다. Tile 방식으로 이 길이마다 반복합니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_pathLineTileLength = 0.5f;

    [Tooltip("텍스처가 목표 쪽으로 흐르는 속도(초당 장 수)입니다. 0이면 멈춰 있습니다.")]
    [SerializeField] private float m_pathLineScrollSpeed = 1.0f;

    [Tooltip("바닥 높이를 확인할 간격(m)입니다. 작을수록 굴곡을 잘 따라가지만 확인 횟수가 늘어납니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_groundSampleSpacing = 0.5f;

    [Tooltip("선 점마다 NavMesh 바닥을 찾아볼 반경(m)입니다. 못 찾으면 꺾임점을 이은 높이를 그대로 씁니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_groundSnapRadius = 1.0f;

    [Tooltip("켜면 경로 선의 꺾이는 곳을 2차 베지어 곡선으로 휘게 그립니다. 화살표 방향 계산에는 영향이 없습니다.")]
    [SerializeField] private bool m_smoothCorners = true;

    [Tooltip("꺾임점에서 이 거리(m)만큼 앞에서 곡선을 시작해 뒤에서 끝냅니다. 앞뒤 구간 길이의 절반을 넘으면 그만큼 줄여 씁니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_cornerRadius = 2.5f;

    [Tooltip("꺾이는 곳 곡선 하나를 나눌 점 개수입니다. 많을수록 매끄럽습니다.")]
    [Range(2, 32)]
    [SerializeField] private int m_cornerSegments = 12;

    [Tooltip("곡선을 켰을 때, 이 거리(m)보다 가까운 꺾임점은 가운데 한 점으로 합쳐서 그립니다. " +
             "벽 모서리처럼 꺾임점이 몰린 곳에서 곡선이 너무 작아지지 않게 합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_cornerMergeDistance = 1.0f;

    [Tooltip("바닥 높이 변화가 이 값(m) 안쪽이면 높은 점들을 이은 선으로 높이를 펴서 그립니다. 움푹한 곳은 메우고, " +
             "선이 원래 높이 아래로는 내려가지 않습니다. 울퉁불퉁한 바닥에서 선이 출렁이지 않게 합니다. 0이면 끕니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_heightFlattenTolerance = 0.3f;

    [Tooltip("높이 그래프에서 앞뒤 기울기 각도 차가 이 값(도) 이상인 곳을 곡선으로 둥글게 꺾습니다. 계단 위아래처럼 각진 곳입니다.")]
    [Range(1.0f, 90.0f)]
    [SerializeField] private float m_heightCornerAngle = 25.0f;

    [Tooltip("높이가 꺾이는 곳 앞뒤로 이 거리(m)만큼을 곡선으로 바꿉니다. 클수록 넓게 휘어집니다. 0이면 끕니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_heightCornerRadius = 1.0f;

    /// <summary>경로 계산 결과를 받는 버퍼입니다. 매번 새로 만들지 않으려고 재사용합니다.</summary>
    private NavMeshPath m_path;

    /// <summary>안내 전용 NavMesh로 조회하기 위한 조건입니다. <see cref="m_agentTypeName"/>에서 Awake 때 정합니다.</summary>
    private NavMeshQueryFilter m_queryFilter;

    /// <summary>마지막으로 계산한 경로의 꺾임점입니다. 경로가 없으면 비어 있습니다.</summary>
    private Vector3[] m_corners = new Vector3[0];

    /// <summary>선을 그릴 때 쓰는 꺾임점입니다. 곡선을 켜면 가까운 꺾임점을 합친 결과입니다.</summary>
    private Vector3[] m_lineCorners = new Vector3[0];
    private readonly System.Collections.Generic.List<Vector3> m_lineCornerBuffer = new System.Collections.Generic.List<Vector3>();

    /// <summary>높이 평탄화에서 윗선을 이루는 선 점 번호입니다. 재사용합니다.</summary>
    private readonly System.Collections.Generic.List<int> m_hullIndices = new System.Collections.Generic.List<int>();

    /// <summary>높이 평탄화에 쓰는 선 점별 누적 수평 거리와 원래 높이입니다. 매 프레임 새로 만들지 않으려고 재사용합니다.</summary>
    private float[] m_lineDistances = new float[0];
    private float[] m_lineHeights = new float[0];

    /// <summary>높이 꺾임을 둥글리기 직전의 높이입니다. 곡선 양 끝 높이와 기울기를 여기서 읽습니다.</summary>
    private float[] m_lineProfileHeights = new float[0];

    /// <summary>높이 꺾임 곡선이 바닥 쪽으로 파고들 때 바닥에서 띄울 최소 높이(m)입니다. 바닥과 겹쳐 깜빡이지 않게 합니다.</summary>
    private const float CornerGroundClearance = 0.02f;

    /// <summary>높이 꺾임 곡선이 바닥에 막힐 때 곡선 위치를 앞뒤로 옮겨 볼 간격(m)입니다.</summary>
    private const float CornerShiftStep = 0.1f;

    private bool m_hasPath;
    private float m_remainingPathLength;
    private float m_repathTimer;
    private bool m_visibleRequested;

    /// <summary>화살표가 지금 가리키는 수평 각도(도)입니다. 회전을 부드럽게 하려고 프레임 사이에 남겨 둡니다.</summary>
    private float m_currentYaw;
    private bool m_hasYaw;

    private LineRenderer m_pathLine;
    private Material m_pathLineMaterial;

    /// <summary>첫 꺾임점부터 목표까지를 바닥에 붙인 점입니다. 경로를 다시 계산할 때만 새로 만듭니다.</summary>
    private readonly System.Collections.Generic.List<Vector3> m_groundedTail = new System.Collections.Generic.List<Vector3>();

    /// <summary>경로 선에 넘길 점입니다. 캐릭터에서 첫 꺾임점까지의 점 + <see cref="m_groundedTail"/>입니다.</summary>
    private readonly System.Collections.Generic.List<Vector3> m_linePoints = new System.Collections.Generic.List<Vector3>();

    /// <summary>
    /// 곡선을 켰을 때 <see cref="m_groundedTail"/>이 이어지는 시작점입니다. 매 프레임 만드는 앞부분은 여기까지 그립니다.
    /// 둘째 꺾임점의 곡선 시작점이고, 꺾임점이 셋뿐이면 마지막 꺾임점입니다.
    /// </summary>
    private Vector3 m_tailStart;

    /// <summary>표시할 목표를 바꿉니다. null이면 화살표를 숨깁니다.</summary>
    public void SetTarget(Transform target)
    {
        m_target = target;
        RequestRepath();
    }

    /// <summary>화살표 표시를 켜거나 끕니다.</summary>
    public void SetVisible(bool visible)
    {
        m_visibleRequested = visible;
        if (visible)
        {
            RequestRepath();
        }
        else
        {
            HideAll();
        }
    }

    /// <summary>다음 프레임에 경로를 바로 다시 계산합니다. 길을 막거나 여는 장애물이 바뀌었을 때 부릅니다.</summary>
    public void RequestRepath()
    {
        m_repathTimer = 0.0f;
    }

    private void Awake()
    {
        m_path = new NavMeshPath();
        m_queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = ResolveAgentTypeId(),
            areaMask = NavMesh.AllAreas
        };
        m_visibleRequested = m_visibleOnStart;
        if (m_drawPathLine)
        {
            CreatePathLine();
        }

        HideAll();
    }

    private void OnDisable()
    {
        HideAll();
    }

    private void OnDestroy()
    {
        if (m_pathLineMaterial != null)
        {
            Destroy(m_pathLineMaterial);
        }
    }

    // 캐릭터 이동이 끝난 뒤에 따라 붙어야 화살표와 선이 한 프레임 늦게 끌려오지 않습니다.
    private void LateUpdate()
    {
        Transform origin = ResolveOrigin();
        if (!m_visibleRequested || m_target == null || origin == null)
        {
            HideAll();
            return;
        }

        m_repathTimer -= Time.deltaTime;
        if (m_repathTimer <= 0.0f)
        {
            m_repathTimer = m_repathInterval;
            RecalculatePath(origin.position);
        }

        if (!m_hasPath || m_remainingPathLength <= m_hideDistance ||
            !TryGetLookAheadDirection(origin.position, out Vector3 direction))
        {
            HideAll();
            return;
        }

        UpdatePathLine(origin.position);
        if (m_arrowVisual == null)
        {
            return;
        }

        float targetYaw = Quaternion.LookRotation(direction).eulerAngles.y;
        m_currentYaw = m_hasYaw
            ? Mathf.MoveTowardsAngle(m_currentYaw, targetYaw, m_turnSpeed * Time.deltaTime)
            : targetYaw;
        m_hasYaw = true;

        Quaternion rotation = Quaternion.Euler(0.0f, m_currentYaw, 0.0f);
        Vector3 position = origin.position + rotation * Vector3.forward * m_ringRadius;
        position.y = origin.position.y + m_heightOffset;
        m_arrowVisual.SetPositionAndRotation(position, rotation);
        SetArrowActive(true);
    }

    /// <summary>Agent Type 이름으로 ID를 찾습니다. 없으면 경고를 남기고 기본 Agent Type(Humanoid, 0)을 씁니다.</summary>
    private int ResolveAgentTypeId()
    {
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
            if (NavMesh.GetSettingsNameFromID(id) == m_agentTypeName)
            {
                return id;
            }
        }

        Debug.LogWarning($"{nameof(ObjectivePathArrow)}: Agent Type '{m_agentTypeName}'이 없어 기본 NavMesh로 길을 찾습니다.", this);
        return 0;
    }

    private Transform ResolveOrigin()
    {
        if (m_originOverride != null)
        {
            return m_originOverride;
        }

        SquadMemberController player = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        return player != null ? player.transform : null;
    }

    /// <summary>캐릭터 위치에서 목표까지 경로를 다시 계산합니다.</summary>
    /// <remarks>
    /// 두 위치를 먼저 NavMesh 위로 붙입니다. 캐릭터는 점프나 경사로 NavMesh에서 조금 떠 있을 수 있고, 목표는 건물
    /// 안쪽처럼 NavMesh 밖에 놓이는 일이 많아서, 그대로 넘기면 경로 계산이 실패하기 때문입니다.
    /// </remarks>
    private void RecalculatePath(Vector3 originPosition)
    {
        m_hasPath = false;
        m_corners = new Vector3[0];
        m_remainingPathLength = 0.0f;

        Vector3 targetPosition = m_target.position + m_targetOffset;
        if (!NavMesh.SamplePosition(originPosition, out NavMeshHit originHit, m_sampleRadius, m_queryFilter) ||
            !NavMesh.SamplePosition(targetPosition, out NavMeshHit targetHit, m_sampleRadius, m_queryFilter))
        {
            return;
        }

        if (!NavMesh.CalculatePath(originHit.position, targetHit.position, m_queryFilter, m_path))
        {
            return;
        }

        bool usable = m_path.status == NavMeshPathStatus.PathComplete ||
            (m_allowPartialPath && m_path.status == NavMeshPathStatus.PathPartial);
        if (!usable || m_path.corners.Length < 2)
        {
            return;
        }

        m_corners = m_path.corners;
        for (int i = 1; i < m_corners.Length; i++)
        {
            m_remainingPathLength += HorizontalDistance(m_corners[i - 1], m_corners[i]);
        }

        m_hasPath = true;
        if (m_pathLine != null)
        {
            BuildGroundedTail();
        }
    }

    /// <summary>캐릭터에서 경로를 따라 내다볼 거리만큼 간 지점으로의 수평 방향을 구합니다.</summary>
    /// <remarks>
    /// 경로는 일정 간격마다만 다시 계산하므로 그 사이 캐릭터는 첫 꺾임점에서 벗어나 있습니다. 그래서 거리는 경로
    /// 시작점이 아니라 지금 캐릭터 위치에서 잽니다. 캐릭터에서 다음 꺾임점까지는 직선으로, 그 뒤로는 경로를 따라 잽니다.
    /// </remarks>
    private bool TryGetLookAheadDirection(Vector3 originPosition, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (m_corners.Length < 2)
        {
            return false;
        }

        Vector3 from = originPosition;
        float remaining = m_lookAheadDistance;
        Vector3 lookPoint = m_corners[m_corners.Length - 1];
        for (int i = 1; i < m_corners.Length; i++)
        {
            float segment = HorizontalDistance(from, m_corners[i]);
            if (segment >= remaining)
            {
                lookPoint = Vector3.Lerp(from, m_corners[i], remaining / Mathf.Max(segment, 0.0001f));
                break;
            }

            remaining -= segment;
            from = m_corners[i];
        }

        direction = lookPoint - originPosition;
        direction.y = 0.0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        return true;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0.0f;
        b.y = 0.0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>경로 선을 그릴 LineRenderer와 단색 머티리얼을 만듭니다.</summary>
    /// <remarks>
    /// 셰이더는 투척 궤적 미리보기와 같은 URP Unlit을 씁니다. 조명과 그림자에 영향받지 않아 어두운 곳에서도 색이 그대로 보입니다.
    /// 선 오브젝트는 이 오브젝트의 자식이지만 월드 좌표로 그려서, 이 오브젝트가 어디에 있든 상관없습니다.
    /// </remarks>
    private void CreatePathLine()
    {
        GameObject lineObject = new GameObject("ObjectivePathLine");
        lineObject.transform.SetParent(transform, false);

        m_pathLine = lineObject.AddComponent<LineRenderer>();
        m_pathLine.useWorldSpace = true;
        m_pathLine.widthMultiplier = m_pathLineWidth;
        m_pathLine.numCornerVertices = 4;
        m_pathLine.numCapVertices = 4;
        // View 정렬은 띠를 카메라 쪽으로 세워서, 낮은 시점에서는 바닥에서 떠 보이고 꺾임마다 비틀려(롤) 보입니다.
        // 선 오브젝트가 위를 보게 고정하고 TransformZ로 정렬해 띠를 바닥과 평행하게 눕힙니다.
        // 이 오브젝트는 회전하지 않으므로(화살표는 따로 돎) 만들 때 한 번만 맞춥니다.
        lineObject.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        m_pathLine.alignment = LineAlignment.TransformZ;
        m_pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_pathLine.receiveShadows = false;
        m_pathLine.startColor = m_pathLineColor;
        m_pathLine.endColor = m_pathLineColor;
        m_pathLine.positionCount = 0;

        // 텍스처는 선 길이 방향으로 Tile 반복합니다. 단색이면 결과가 같습니다. textureScale.x는 1m당 반복 횟수입니다.
        m_pathLine.textureMode = LineTextureMode.Tile;
        m_pathLine.textureScale = new Vector2(1.0f / m_pathLineTileLength, 1.0f);

        if (m_pathLineMaterialOverride != null)
        {
            // 흐름 연출로 텍스처 오프셋을 매 프레임 바꾸므로 에셋이 아니라 복사본에 씁니다.
            m_pathLineMaterial = new Material(m_pathLineMaterialOverride)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_pathLine.sharedMaterial = m_pathLineMaterial;
            return;
        }

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader != null)
        {
            m_pathLineMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_pathLineMaterial.SetColor("_BaseColor", m_pathLineColor);
            m_pathLine.sharedMaterial = m_pathLineMaterial;
        }
    }

    /// <summary>첫 꺾임점부터 목표까지를 잘게 나눠 바닥에 붙인 점을 만듭니다.</summary>
    /// <remarks>
    /// NavMesh는 꺾임점 사이를 직선으로 잇기 때문에, 그 사이에 지형이 솟아 있으면 선이 땅에 묻힙니다.
    /// 그래서 일정 간격마다 실제 바닥 높이를 찾아 붙입니다. 이 구간은 경로가 바뀔 때만 다시 만듭니다.
    /// </remarks>
    private void BuildGroundedTail()
    {
        BuildLineCorners();

        // 첫 구간(캐릭터 → 첫 꺾임점)은 캐릭터가 움직이므로 매 프레임 따로 만듭니다. 여기서는 첫 꺾임점 다음부터 만듭니다.
        m_groundedTail.Clear();
        if (!m_smoothCorners)
        {
            for (int i = 2; i < m_lineCorners.Length; i++)
            {
                AppendGroundedSegment(m_groundedTail, m_lineCorners[i - 1], m_lineCorners[i], false);
            }

            return;
        }

        // 곡선을 켜면 첫 꺾임점의 곡선도 캐릭터 위치에 따라 바뀌어 매 프레임 만듭니다. 여기서는 둘째 꺾임점의 곡선부터 만듭니다.
        int last = m_lineCorners.Length - 1;
        if (last < 3)
        {
            m_tailStart = m_lineCorners[last];
            return;
        }

        Vector3 previousEnd = Vector3.zero;
        for (int i = 2; i < last; i++)
        {
            float radius = CornerRadius(m_lineCorners[i - 1], m_lineCorners[i], m_lineCorners[i + 1]);
            Vector3 curveStart = Vector3.MoveTowards(m_lineCorners[i], m_lineCorners[i - 1], radius);
            Vector3 curveEnd = Vector3.MoveTowards(m_lineCorners[i], m_lineCorners[i + 1], radius);
            if (i == 2)
            {
                m_tailStart = curveStart;
            }
            else
            {
                AppendGroundedSegment(m_groundedTail, previousEnd, curveStart, false);
            }

            AppendGroundedCurve(m_groundedTail, curveStart, m_lineCorners[i], curveEnd);
            previousEnd = curveEnd;
        }

        AppendGroundedSegment(m_groundedTail, previousEnd, m_lineCorners[last], false);
    }

    /// <summary>선을 그릴 꺾임점을 만듭니다. 곡선을 켜면 서로 가까운 꺾임점을 가운데 한 점으로 합칩니다.</summary>
    /// <remarks>
    /// NavMesh는 벽 모서리를 돌 때 1m도 안 되는 간격으로 꺾임점을 여러 개 찍습니다. 곡선 반경은 앞뒤 구간 길이의 절반을
    /// 넘지 못하므로, 그대로 쓰면 그런 곳에서 반경이 수십 cm로 줄어 꺾임이 각지게 남습니다. 시작점과 목표는 합치지 않고,
    /// 화살표 방향은 합치기 전 꺾임점(<see cref="m_corners"/>)으로 계산합니다.
    /// </remarks>
    private void BuildLineCorners()
    {
        m_lineCornerBuffer.Clear();
        int last = m_corners.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            Vector3 corner = m_corners[i];
            int keptCount = m_lineCornerBuffer.Count;
            bool canMerge = m_smoothCorners && i > 0 && i < last && keptCount > 1;
            if (canMerge && HorizontalDistance(m_lineCornerBuffer[keptCount - 1], corner) < m_cornerMergeDistance)
            {
                m_lineCornerBuffer[keptCount - 1] = (m_lineCornerBuffer[keptCount - 1] + corner) * 0.5f;
                continue;
            }

            m_lineCornerBuffer.Add(corner);
        }

        m_lineCorners = m_lineCornerBuffer.ToArray();
    }

    /// <summary>
    /// 선 점의 높이를 허용치 안에서 일자로 폅니다. 수평 위치는 그대로 두고 y만 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 선을 따라 간 수평 거리와 높이로 놓고, 구간 양 끝을 잇는 직선에서 높이가 가장 많이 벗어난 점을 찾습니다.
    /// 그 차이가 허용치 안이면 그 구간을 높은 점 기준 윗선으로 펴고(<see cref="TryApplyUpperHeightHull"/>), 아니면 그 점에서
    /// 나눠 앞뒤를 같은 방법으로 봅니다(Ramer-Douglas-Peucker).
    /// </remarks>
    private void FlattenLineHeights(System.Collections.Generic.List<Vector3> points)
    {
        int count = points.Count;
        bool flatten = m_heightFlattenTolerance > 0.0f;
        bool round = m_heightCornerRadius > 0.0f;
        if ((!flatten && !round) || count < 3)
        {
            return;
        }

        if (m_lineDistances.Length < count)
        {
            m_lineDistances = new float[count * 2];
            m_lineHeights = new float[count * 2];
            m_lineProfileHeights = new float[count * 2];
        }

        m_lineDistances[0] = 0.0f;
        for (int i = 0; i < count; i++)
        {
            m_lineHeights[i] = points[i].y;
            if (i > 0)
            {
                m_lineDistances[i] = m_lineDistances[i - 1] + HorizontalDistance(points[i - 1], points[i]);
            }
        }

        if (flatten)
        {
            FlattenHeightRange(points, 0, count - 1);
        }

        if (round)
        {
            RoundHeightCorners(points);
        }
    }

    /// <summary>
    /// 높이가 크게 꺾이는 곳(계단 위아래처럼 직각에 가까운 곳)을 곡선으로 둥글게 이어 줍니다. 수평 위치는 그대로 두고 y만 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 선을 옆에서 본 그래프(누적 수평 거리, 높이)에서 앞 구간과 뒤 구간의 기울기 각도 차가 <see cref="m_heightCornerAngle"/>
    /// 이상인 점을 꺾임점으로 봅니다. 계단은 아래 모서리와 위 모서리가 한 칸(0.5m) 간격으로 붙어 있어서, 반경의 두 배보다
    /// 가까운 꺾임점은 한 묶음으로 처리합니다. 묶음의 첫 꺾임점보다 반경만큼 앞에서 시작해 마지막 꺾임점보다 반경만큼 뒤에서
    /// 끝나는 곡선 하나로 바꿉니다. 곡선은 시작점에서 들어오던 기울기, 끝점에서 나가는 기울기를 그대로 이어받는
    /// 3차 에르미트 곡선이라 앞뒤 직선과 꺾이지 않고 이어집니다.
    /// 계단 윗모서리처럼 곡선이 안쪽으로 파고드는 곳은 바닥보다 아래로 내려가지 않게 바닥에서 조금 띄운 높이로 막습니다.
    /// </remarks>
    private void RoundHeightCorners(System.Collections.Generic.List<Vector3> points)
    {
        int count = points.Count;
        for (int i = 0; i < count; i++)
        {
            m_lineProfileHeights[i] = points[i].y;
        }

        float total = m_lineDistances[count - 1];
        float cornerAngle = m_heightCornerAngle * Mathf.Deg2Rad;
        float spanStart = 0.0f;
        float spanEnd = -1.0f;
        for (int i = 1; i < count - 1; i++)
        {
            float inRun = m_lineDistances[i] - m_lineDistances[i - 1];
            float outRun = m_lineDistances[i + 1] - m_lineDistances[i];
            if (inRun <= 0.0001f || outRun <= 0.0001f)
            {
                continue;
            }

            float inAngle = Mathf.Atan2(m_lineProfileHeights[i] - m_lineProfileHeights[i - 1], inRun);
            float outAngle = Mathf.Atan2(m_lineProfileHeights[i + 1] - m_lineProfileHeights[i], outRun);
            if (Mathf.Abs(outAngle - inAngle) < cornerAngle)
            {
                continue;
            }

            float cornerStart = Mathf.Max(0.0f, m_lineDistances[i] - m_heightCornerRadius);
            float cornerEnd = Mathf.Min(total, m_lineDistances[i] + m_heightCornerRadius);
            if (spanEnd >= 0.0f && cornerStart <= spanEnd)
            {
                spanEnd = cornerEnd;
                continue;
            }

            if (spanEnd >= 0.0f)
            {
                ApplyCornerCurve(points, count, total, spanStart, spanEnd);
            }

            spanStart = cornerStart;
            spanEnd = cornerEnd;
        }

        if (spanEnd >= 0.0f)
        {
            ApplyCornerCurve(points, count, total, spanStart, spanEnd);
        }
    }

    /// <summary>
    /// 꺾임 묶음 하나를 곡선으로 바꿉니다. 곡선이 바닥에 파고들지 않는 위치를 찾아 그 자리에 놓습니다.
    /// </summary>
    /// <remarks>
    /// 내리막 계단 윗모서리에서 곡선을 모서리 앞부터 내리기 시작하면 아직 높은 바닥 위라서 바닥에 막힙니다. 막힌 점을 바닥 높이로
    /// 올리면 선이 모서리까지 평평하게 가다가 수직으로 떨어져서 곡선이 사라집니다. 그래서 같은 폭의 곡선을 앞뒤로 조금씩 옮겨 보며
    /// 모든 점이 바닥 위에 있는 위치 중 원래 자리에서 가장 가까운 곳을 씁니다. 내리막은 모서리에서부터 내려가는 곡선, 오르막은 위
    /// 바닥 모서리에 닿으며 끝나는 곡선이 됩니다. 그런 위치가 없으면 원래 자리에 놓고 바닥 아래로 내려간 점만 바닥 위로 올립니다.
    /// </remarks>
    private void ApplyCornerCurve(System.Collections.Generic.List<Vector3> points, int count, float total, float start, float end)
    {
        float width = end - start;
        int stepCount = Mathf.CeilToInt(width / CornerShiftStep);
        for (int step = 0; step <= stepCount * 2; step++)
        {
            // 0, -1, +1, -2, +2 ... 순서로 원래 자리에서 가까운 위치부터 봅니다.
            float shift = ((step + 1) / 2) * CornerShiftStep * (step % 2 == 1 ? -1.0f : 1.0f);
            float shiftedStart = Mathf.Max(0.0f, start + shift);
            float shiftedEnd = Mathf.Min(total, end + shift);
            if (ApplyHeightCurve(points, count, shiftedStart, shiftedEnd, false))
            {
                ApplyHeightCurve(points, count, shiftedStart, shiftedEnd, true);
                return;
            }
        }

        ApplyHeightCurve(points, count, start, end, true);
    }

    /// <summary>
    /// <paramref name="start"/>부터 <paramref name="end"/>까지(누적 거리) 점의 높이를 양 끝 높이와 기울기를 잇는 3차 곡선으로 바꿉니다.
    /// <paramref name="write"/>가 false면 바꾸지 않고, 곡선이 모든 점에서 바닥 위에 있는지만 돌려줍니다.
    /// </summary>
    private bool ApplyHeightCurve(System.Collections.Generic.List<Vector3> points, int count, float start, float end, bool write)
    {
        float span = end - start;
        if (span <= 0.0001f)
        {
            return false;
        }

        float startHeight = ProfileHeightAt(start, count);
        float endHeight = ProfileHeightAt(end, count);
        float startSlope = ProfileSlopeAt(start, count, false);
        float endSlope = ProfileSlopeAt(end, count, true);
        for (int i = 0; i < count; i++)
        {
            float distance = m_lineDistances[i];
            if (distance <= start || distance >= end)
            {
                continue;
            }

            float t = (distance - start) / span;
            float t2 = t * t;
            float t3 = t2 * t;
            float curveHeight = (2.0f * t3 - 3.0f * t2 + 1.0f) * startHeight
                + (t3 - 2.0f * t2 + t) * span * startSlope
                + (-2.0f * t3 + 3.0f * t2) * endHeight
                + (t3 - t2) * span * endSlope;
            float minHeight = m_lineHeights[i] - m_pathLineHeight + CornerGroundClearance;
            if (!write)
            {
                if (curveHeight < minHeight)
                {
                    return false;
                }

                continue;
            }

            Vector3 point = points[i];
            point.y = Mathf.Max(curveHeight, minHeight);
            points[i] = point;
        }

        return true;
    }

    /// <summary>
    /// 둥글리기 전 높이 그래프의 기울기입니다. 지점이 점 위에 딱 걸리면 <paramref name="after"/>일 때 그 뒤 구간, 아니면 앞 구간을 씁니다.
    /// </summary>
    private float ProfileSlopeAt(float distance, int count, bool after)
    {
        for (int i = 1; i < count; i++)
        {
            bool inSegment = after ? m_lineDistances[i] > distance : m_lineDistances[i] >= distance;
            if (inSegment)
            {
                float run = m_lineDistances[i] - m_lineDistances[i - 1];
                return run > 0.0001f ? (m_lineProfileHeights[i] - m_lineProfileHeights[i - 1]) / run : 0.0f;
            }
        }

        return 0.0f;
    }

    /// <summary>둥글리기 전 높이 그래프에서 누적 거리 <paramref name="distance"/>의 높이를 앞뒤 점 사이 직선으로 구합니다.</summary>
    private float ProfileHeightAt(float distance, int count)
    {
        if (distance <= m_lineDistances[0])
        {
            return m_lineProfileHeights[0];
        }

        for (int i = 1; i < count; i++)
        {
            if (m_lineDistances[i] >= distance)
            {
                float width = Mathf.Max(m_lineDistances[i] - m_lineDistances[i - 1], 0.0001f);
                return Mathf.Lerp(m_lineProfileHeights[i - 1], m_lineProfileHeights[i], (distance - m_lineDistances[i - 1]) / width);
            }
        }

        return m_lineProfileHeights[count - 1];
    }

    private void FlattenHeightRange(System.Collections.Generic.List<Vector3> points, int start, int end)
    {
        if (end - start < 2)
        {
            return;
        }

        float d0 = m_lineDistances[start];
        float span = Mathf.Max(m_lineDistances[end] - d0, 0.0001f);
        float y0 = m_lineHeights[start];
        float y1 = m_lineHeights[end];
        int farthest = -1;
        float farthestDeviation = 0.0f;
        for (int i = start + 1; i < end; i++)
        {
            float lineY = Mathf.Lerp(y0, y1, (m_lineDistances[i] - d0) / span);
            float deviation = Mathf.Abs(m_lineHeights[i] - lineY);
            if (deviation > farthestDeviation)
            {
                farthestDeviation = deviation;
                farthest = i;
            }
        }

        if (farthest < 0)
        {
            return;
        }

        // 직선에서 벗어난 정도가 허용치 안이어도, 윗선으로 폈을 때 올라가는 높이가 허용치를 넘으면 펴지 않고 나눕니다.
        // 계단 앞 평지처럼, 직선 기준으로는 작아 보여도 윗선이 평지 전체를 계단 높이까지 들어 올리는 경우가 있습니다.
        if (farthestDeviation <= m_heightFlattenTolerance && TryApplyUpperHeightHull(points, start, end))
        {
            return;
        }

        FlattenHeightRange(points, start, farthest);
        FlattenHeightRange(points, farthest, end);
    }

    /// <summary>
    /// 구간 안의 높은 점들만 이은 윗선(위쪽 볼록 껍질)으로 높이를 바꿉니다. 움푹한 곳은 메우고 솟은 곳은 그 높이를 지납니다.
    /// </summary>
    /// <remarks>
    /// 윗선은 모든 점의 원래 높이 이상이라 선이 바닥에 묻히지 않습니다. 구간 양 끝은 원래 높이 그대로라 옆 구간과 끊기지 않습니다.
    /// 어느 점이든 윗선이 원래 높이보다 허용치 넘게 올라가면 바꾸지 않고 false를 돌려줍니다.
    /// </remarks>
    /// <returns>높이를 바꿨으면 true입니다.</returns>
    private bool TryApplyUpperHeightHull(System.Collections.Generic.List<Vector3> points, int start, int end)
    {
        m_hullIndices.Clear();
        for (int i = start; i <= end; i++)
        {
            // 마지막 두 점과 새 점이 아래로 볼록하거나 일직선이면 가운데 점은 윗선 아래라 뺍니다.
            while (m_hullIndices.Count >= 2)
            {
                int a = m_hullIndices[m_hullIndices.Count - 2];
                int b = m_hullIndices[m_hullIndices.Count - 1];
                float cross = (m_lineDistances[b] - m_lineDistances[a]) * (m_lineHeights[i] - m_lineHeights[a])
                    - (m_lineHeights[b] - m_lineHeights[a]) * (m_lineDistances[i] - m_lineDistances[a]);
                if (cross < 0.0f)
                {
                    break;
                }

                m_hullIndices.RemoveAt(m_hullIndices.Count - 1);
            }

            m_hullIndices.Add(i);
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool apply = pass == 1;
            for (int h = 1; h < m_hullIndices.Count; h++)
            {
                int left = m_hullIndices[h - 1];
                int right = m_hullIndices[h];
                float width = Mathf.Max(m_lineDistances[right] - m_lineDistances[left], 0.0001f);
                for (int i = left + 1; i < right; i++)
                {
                    float hullHeight = Mathf.Lerp(m_lineHeights[left], m_lineHeights[right], (m_lineDistances[i] - m_lineDistances[left]) / width);
                    if (!apply)
                    {
                        if (hullHeight - m_lineHeights[i] > m_heightFlattenTolerance)
                        {
                            return false;
                        }

                        continue;
                    }

                    Vector3 point = points[i];
                    point.y = hullHeight;
                    points[i] = point;
                }
            }
        }

        return true;
    }

    /// <summary>꺾임점에서 곡선을 시작할 거리입니다. 곡선끼리 겹치지 않게 앞뒤 구간 길이의 절반을 넘지 않습니다.</summary>
    private float CornerRadius(Vector3 previous, Vector3 corner, Vector3 next)
    {
        return Mathf.Min(
            m_cornerRadius,
            HorizontalDistance(previous, corner) * 0.5f,
            HorizontalDistance(corner, next) * 0.5f);
    }

    /// <summary>a에서 b까지 control을 조절점으로 하는 2차 베지어 곡선을 나눠 바닥에 붙여 넣습니다. a는 넣지 않습니다.</summary>
    /// <remarks>조절점을 꺾임점에 두므로 곡선은 a에서 들어오는 구간, b에서 나가는 구간과 같은 방향으로 이어집니다.</remarks>
    private void AppendGroundedCurve(System.Collections.Generic.List<Vector3> points, Vector3 a, Vector3 control, Vector3 b)
    {
        for (int k = 1; k <= m_cornerSegments; k++)
        {
            float t = k / (float)m_cornerSegments;
            float u = 1.0f - t;
            points.Add(SnapToGround(u * u * a + 2.0f * u * t * control + t * t * b));
        }
    }

    /// <summary>a에서 b까지를 바닥 확인 간격으로 나눈 점을 바닥에 붙여 넣습니다. b는 넣고 a는 includeStart일 때만 넣습니다.</summary>
    private void AppendGroundedSegment(System.Collections.Generic.List<Vector3> points, Vector3 a, Vector3 b, bool includeStart)
    {
        int count = SegmentSampleCount(a, b);
        if (includeStart)
        {
            points.Add(SnapToGround(a));
        }

        for (int k = 1; k <= count; k++)
        {
            points.Add(SnapToGround(Vector3.Lerp(a, b, k / (float)count)));
        }
    }

    private int SegmentSampleCount(Vector3 a, Vector3 b)
    {
        return Mathf.Max(1, Mathf.CeilToInt(HorizontalDistance(a, b) / m_groundSampleSpacing));
    }

    /// <summary>점에서 가장 가까운 안내용 NavMesh 바닥 높이에 선 높이를 더한 위치를 돌려줍니다.</summary>
    /// <remarks>
    /// 안내용 NavMesh는 Height Mesh를 켜고 구워서 실제 바닥 높이를 돌려줍니다. 장애물 윗면은 빼고 구웠기 때문에
    /// 선이 바리케이드나 차량 위로 올라타지 않습니다. 바닥 레이어 정리 없이 씬마다 그대로 쓸 수 있습니다.
    /// </remarks>
    private Vector3 SnapToGround(Vector3 point)
    {
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, m_groundSnapRadius, m_queryFilter))
        {
            point.y = hit.position.y;
        }

        point.y += m_pathLineHeight;
        return point;
    }

    /// <summary>캐릭터 위치에서 시작해 남은 경로를 잇는 선을 그립니다.</summary>
    private void UpdatePathLine(Vector3 originPosition)
    {
        if (m_pathLine == null)
        {
            return;
        }

        // 경로의 첫 점은 계산할 때의 캐릭터 위치라 그 뒤 움직인 만큼 어긋납니다. 첫 구간만 지금 캐릭터 위치로 다시 만듭니다.
        m_linePoints.Clear();
        if (m_smoothCorners && m_lineCorners.Length >= 3)
        {
            // 캐릭터 → 첫 꺾임점 곡선 → 뒷부분 시작점까지입니다. 곡선 크기가 캐릭터와 첫 꺾임점 사이 거리에 따라 달라집니다.
            Vector3 corner = m_lineCorners[1];
            float radius = CornerRadius(originPosition, corner, m_lineCorners[2]);
            Vector3 curveStart = Vector3.MoveTowards(corner, originPosition, radius);
            Vector3 curveEnd = Vector3.MoveTowards(corner, m_lineCorners[2], radius);
            AppendGroundedSegment(m_linePoints, originPosition, curveStart, true);
            AppendGroundedCurve(m_linePoints, curveStart, corner, curveEnd);
            if (HorizontalDistance(curveEnd, m_tailStart) > 0.01f)
            {
                AppendGroundedSegment(m_linePoints, curveEnd, m_tailStart, false);
            }
        }
        else
        {
            AppendGroundedSegment(m_linePoints, originPosition, m_lineCorners[1], true);
        }

        m_linePoints.AddRange(m_groundedTail);
        FlattenLineHeights(m_linePoints);

        // 텍스처 U는 캐릭터에서 목표 쪽으로 커집니다. 오프셋을 줄이면 무늬가 목표 쪽으로 흐릅니다.
        if (m_pathLineMaterialOverride != null && m_pathLineMaterial != null && m_pathLineScrollSpeed != 0.0f)
        {
            Vector2 offset = m_pathLineMaterial.mainTextureOffset;
            offset.x = Mathf.Repeat(offset.x - m_pathLineScrollSpeed * Time.deltaTime, 1.0f);
            m_pathLineMaterial.mainTextureOffset = offset;
        }

        m_pathLine.positionCount = m_linePoints.Count;
        for (int i = 0; i < m_linePoints.Count; i++)
        {
            m_pathLine.SetPosition(i, m_linePoints[i]);
        }
        if (!m_pathLine.enabled)
        {
            m_pathLine.enabled = true;
        }
    }

    private void HideAll()
    {
        SetArrowActive(false);
        m_hasYaw = false;
        if (m_pathLine != null)
        {
            m_pathLine.enabled = false;
        }
    }

    private void SetArrowActive(bool active)
    {
        if (m_arrowVisual != null && m_arrowVisual.gameObject.activeSelf != active)
        {
            m_arrowVisual.gameObject.SetActive(active);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (m_corners == null || m_corners.Length < 2)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.9f, 1.0f, 0.9f);
        for (int i = 1; i < m_corners.Length; i++)
        {
            Gizmos.DrawLine(m_corners[i - 1], m_corners[i]);
            Gizmos.DrawWireSphere(m_corners[i], 0.15f);
        }
    }
}
