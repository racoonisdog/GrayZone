using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 플레이어의 투척 모드와 좌클릭 입력을 받아 폭발탄 경로를 표시하고 투척합니다.
/// </summary>
public class ExplosiveProjectileShooter : MonoBehaviour
{
    private const int MaxTrajectoryPointCount = 65;

    [Tooltip("투척할 ExplosiveProjectile Prefab입니다.")]
    [SerializeField] private ExplosiveProjectile m_projectilePrefab;

    [Tooltip("플레이어 Collider 중심을 기준으로 한 로컬 투척 시작 위치입니다.")]
    [SerializeField] private Vector3 m_throwOriginOffset = new Vector3(0.0f, 0.2f, 1.0f);

    [Tooltip("최대 투척 거리")]
    [Min(0.1f)]
    [SerializeField] private float m_maxThrowDistance = 20.0f;

    [Tooltip("포물선의 최고 높이.")]
    [Min(0.0f)]
    [SerializeField] private float m_arcHeight = 4.0f;

    [Tooltip("투척 시작부터 도착까지 걸리는 시간입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_travelDuration = 1.0f;

    [Tooltip("한 번 투척한 뒤 다음 투척 경로를 표시하고 다시 던질 수 있을 때까지의 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_throwCooldown = 1.0f;

    [Tooltip("경로 표시를 구성할 선분 수입니다.")]
    [Range(4, 64)]
    [SerializeField] private int m_trajectorySegments = 24;

    [Tooltip("이동 중 충돌을 검사할 구체 반지름입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_collisionRadius = 0.5f;

    [Tooltip("투척 경로 및 실제 이동 중 충돌을 검사할 Layer입니다.")]
    [SerializeField] private LayerMask m_collisionLayers = ~0;

    [Tooltip("경로 표시 선의 두께입니다.")]
    [Min(0.001f)]
    [SerializeField] private float m_trajectoryWidth = 0.04f;

    [Tooltip("경로 표시 선의 색상입니다.")]
    [SerializeField] private Color m_trajectoryColor = new Color(1.0f, 0.75f, 0.1f, 0.9f);

    private readonly RaycastHit[] m_previewHits = new RaycastHit[16];
    private readonly Vector3[] m_trajectoryPoints = new Vector3[MaxTrajectoryPointCount];

    private PlayerInputController m_input;
    private AimController m_aimController;
    private Collider m_sourceCollider;
    private LineRenderer m_trajectoryLine;
    private Material m_runtimeLineMaterial;
    private bool m_wasThrowModeActive;
    private bool m_throwWasHeld;
    private Vector3 m_throwStart;
    private Vector3 m_throwEnd;
    private Vector3 m_plannedEndPosition;
    private float m_plannedEndNormalizedTime = 1.0f;
    private int m_trajectoryPointCount;
    private float m_nextThrowReadyTime;

    private void Awake()
    {
        m_input = GetComponent<PlayerInputController>();
        m_aimController = GetComponent<AimController>();
        m_sourceCollider = GetComponent<Collider>();
        CreateTrajectoryLine();
    }

    private void LateUpdate()
    {
        if (m_input == null || m_aimController == null || !m_input.ThrowMode)
        {
            HideTrajectory();
            m_wasThrowModeActive = false;
            m_throwWasHeld = false;
            return;
        }

        bool throwHeld = m_input.Throw;

        if (m_input.Sprint)
        {
            HideTrajectory();
            m_wasThrowModeActive = true;
            m_throwWasHeld = throwHeld;
            return;
        }

        bool enteredThrowModeThisFrame = !m_wasThrowModeActive;
        bool canThrow = Time.time >= m_nextThrowReadyTime;

        if (canThrow)
        {
            ResolveTrajectory();
            BuildTrajectoryPlan();
            DrawTrajectory();

            if (!enteredThrowModeThisFrame && throwHeld && !m_throwWasHeld && ThrowProjectile())
            {
                m_nextThrowReadyTime = Time.time + m_throwCooldown;
                HideTrajectory();
            }
        }
        else
        {
            HideTrajectory();
        }

        m_wasThrowModeActive = true;
        m_throwWasHeld = throwHeld;
    }

    private void OnDisable()
    {
        HideTrajectory();
        m_wasThrowModeActive = false;
        m_throwWasHeld = false;
    }

    private void OnDestroy()
    {
        if (m_runtimeLineMaterial != null)
        {
            Destroy(m_runtimeLineMaterial);
        }
    }

    private void ResolveTrajectory()
    {
        Vector3 origin = m_sourceCollider != null ? m_sourceCollider.bounds.center : transform.position;
        m_throwStart = origin + transform.TransformDirection(m_throwOriginOffset);

        Vector3 toAimPoint = m_aimController.CurrentAimPoint - m_throwStart;
        if (toAimPoint.sqrMagnitude < 0.0001f)
        {
            toAimPoint = transform.forward * m_maxThrowDistance;
        }

        m_throwEnd = m_throwStart + Vector3.ClampMagnitude(toAimPoint, m_maxThrowDistance);
    }

    private void DrawTrajectory()
    {
        m_trajectoryLine.enabled = true;
        m_trajectoryLine.positionCount = m_trajectoryPointCount;

        for (int i = 0; i < m_trajectoryPointCount; i++)
        {
            m_trajectoryLine.SetPosition(i, m_trajectoryPoints[i]);
        }
    }

    private void BuildTrajectoryPlan()
    {
        int segmentCount = Mathf.Clamp(m_trajectorySegments, 4, MaxTrajectoryPointCount - 1);
        m_trajectoryPointCount = 1;
        m_trajectoryPoints[0] = m_throwStart;
        m_plannedEndNormalizedTime = 1.0f;
        m_plannedEndPosition = m_throwEnd;

        Vector3 previous = m_throwStart;
        float previousTime = 0.0f;

        for (int i = 1; i <= segmentCount; i++)
        {
            float currentTime = i / (float)segmentCount;
            Vector3 next = ParabolicProjectileMover.EvaluatePosition(
                m_throwStart,
                m_throwEnd,
                m_arcHeight,
                currentTime);

            if (TryGetBlockingHit(previous, next, out RaycastHit hit))
            {
                Vector3 segment = next - previous;
                float segmentDistance = segment.magnitude;
                float hitFraction = segmentDistance > 0.0001f
                    ? Mathf.Clamp01(hit.distance / segmentDistance)
                    : 0.0f;

                m_plannedEndNormalizedTime = Mathf.Lerp(previousTime, currentTime, hitFraction);
                m_plannedEndPosition = ParabolicProjectileMover.EvaluatePosition(
                    m_throwStart,
                    m_throwEnd,
                    m_arcHeight,
                    m_plannedEndNormalizedTime);

                if ((m_plannedEndPosition - previous).sqrMagnitude > 0.000001f)
                {
                    m_trajectoryPoints[m_trajectoryPointCount] = m_plannedEndPosition;
                    m_trajectoryPointCount++;
                }

                return;
            }

            m_trajectoryPoints[m_trajectoryPointCount] = next;
            m_trajectoryPointCount++;
            previous = next;
            previousTime = currentTime;
        }
    }

    private bool ThrowProjectile()
    {
        if (m_projectilePrefab == null)
        {
            Debug.LogWarning($"[{name}] 투척할 폭발 투사체 Prefab이 없습니다.", this);
            return false;
        }

        float initialSampleTime = Mathf.Min(0.05f, m_plannedEndNormalizedTime);
        Vector3 initialSamplePosition = initialSampleTime > 0.0f
            ? ParabolicProjectileMover.EvaluatePosition(
                m_throwStart,
                m_throwEnd,
                m_arcHeight,
                initialSampleTime)
            : m_plannedEndPosition;
        Vector3 initialDirection = initialSamplePosition - m_throwStart;

        Quaternion rotation = initialDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(initialDirection.normalized, Vector3.up)
            : transform.rotation;

        ExplosiveProjectile projectile = Instantiate(m_projectilePrefab, m_throwStart, rotation);
        Rigidbody projectileRigidbody = projectile.GetComponent<Rigidbody>();
        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;
        projectileRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        projectileRigidbody.linearVelocity = Vector3.zero;
        projectileRigidbody.angularVelocity = Vector3.zero;

        ParabolicProjectileMover mover = projectile.gameObject.AddComponent<ParabolicProjectileMover>();
        mover.Initialize(
            projectile,
            m_throwStart,
            m_throwEnd,
            m_arcHeight,
            m_travelDuration,
            m_plannedEndNormalizedTime,
            m_plannedEndPosition,
            m_collisionRadius,
            m_collisionLayers,
            transform);

        return true;
    }

    private bool TryGetBlockingHit(Vector3 start, Vector3 end, out RaycastHit nearestHit)
    {
        nearestHit = default;
        Vector3 movement = end - start;
        float distance = movement.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            m_collisionRadius,
            movement / distance,
            m_previewHits,
            distance,
            m_collisionLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = m_previewHits[i];
            if (candidate.collider == null || IsOwnedByPlayer(candidate.collider.transform))
            {
                continue;
            }

            if (candidate.distance < nearestDistance)
            {
                nearestDistance = candidate.distance;
                nearestHit = candidate;
                found = true;
            }
        }

        return found;
    }

    private bool IsOwnedByPlayer(Transform hitTransform)
    {
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private void CreateTrajectoryLine()
    {
        GameObject lineObject = new GameObject("ThrowTrajectoryPreview");
        lineObject.transform.SetParent(transform, false);

        m_trajectoryLine = lineObject.AddComponent<LineRenderer>();
        m_trajectoryLine.useWorldSpace = true;
        m_trajectoryLine.loop = false;
        m_trajectoryLine.widthMultiplier = m_trajectoryWidth;
        m_trajectoryLine.startColor = m_trajectoryColor;
        m_trajectoryLine.endColor = m_trajectoryColor;
        m_trajectoryLine.shadowCastingMode = ShadowCastingMode.Off;
        m_trajectoryLine.receiveShadows = false;
        m_trajectoryLine.enabled = false;

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader != null)
        {
            m_runtimeLineMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_runtimeLineMaterial.SetColor("_BaseColor", m_trajectoryColor);
            m_trajectoryLine.material = m_runtimeLineMaterial;
        }
    }

    private void HideTrajectory()
    {
        if (m_trajectoryLine == null)
        {
            return;
        }

        m_trajectoryLine.enabled = false;
        m_trajectoryLine.positionCount = 0;
    }
}
