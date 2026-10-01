using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 캐릭터 몸통에서 충돌 전 목표 카메라 위치까지의 경로만 검사해 카메라를 벽 앞에 배치합니다.
/// Third Person Follow의 root-hand 충돌을 대신하여 카메라 반대편 장애물에 반응하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
[RequireComponent(typeof(CinemachineThirdPersonFollow))]
public sealed class DirectionalCameraCollisionExtension : CinemachineExtension
{
    private sealed class CameraCollisionState : VcamExtraStateBase
    {
        public float CurrentDistance;
        public float DistanceVelocity;
        public bool Initialized;
    }

    [Header("Collision")]
    [Tooltip("카메라 이동 경로에서 충돌을 검사할 레이어입니다.")]
    [SerializeField] private LayerMask m_collisionFilter = 1;
    [Tooltip("이 태그가 붙은 Collider는 카메라 충돌에서 제외합니다.")]
    [SerializeField] private string m_ignoreTag = "Player";
    [Tooltip("벽과 겹치지 않도록 검사할 카메라 구체의 반경입니다.")]
    [SerializeField, Min(0.01f)] private float m_cameraRadius = 0.4f;
    [Tooltip("SphereCast 충돌 지점에서 추가로 확보할 여유 거리입니다.")]
    [SerializeField, Min(0f)] private float m_surfaceOffset = 0.02f;
    [Tooltip("장애물에서 벗어난 뒤 원래 카메라 거리로 복귀하는 시간입니다.")]
    [SerializeField, Min(0.01f)] private float m_recoveryDuration = 0.5f;

    private readonly RaycastHit[] m_hitBuffer = new RaycastHit[16];

    public bool IsColliding { get; private set; }
    public Collider CurrentObstacle { get; private set; }
    public float DesiredDistance { get; private set; }
    public float CurrentDistance { get; private set; }

    /// <summary>
    /// 기존 Third Person Follow의 충돌 설정을 단일 방향 충돌 검사 설정으로 이전합니다.
    /// </summary>
    public void Configure(
        LayerMask collisionFilter,
        string ignoreTag,
        float cameraRadius,
        float recoveryDuration)
    {
        m_collisionFilter = collisionFilter;
        m_ignoreTag = ignoreTag;
        m_cameraRadius = Mathf.Max(0.01f, cameraRadius);
        m_recoveryDuration = Mathf.Max(0.01f, recoveryDuration);
    }

    private void OnValidate()
    {
        m_cameraRadius = Mathf.Max(0.01f, m_cameraRadius);
        m_surfaceOffset = Mathf.Max(0f, m_surfaceOffset);
        m_recoveryDuration = Mathf.Max(0.01f, m_recoveryDuration);
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body)
            return;

        Transform followTarget = vcam.Follow;
        if (followTarget == null)
        {
            ResetCollisionState();
            return;
        }

        CharacterController characterController =
            followTarget.GetComponentInParent<CharacterController>();
        Transform characterRoot = characterController != null
            ? characterController.transform
            : followTarget;
        Vector3 origin = characterController != null
            ? characterController.bounds.center
            : followTarget.position;
        Vector3 desiredPosition = state.GetCorrectedPosition();
        Vector3 cameraOffset = desiredPosition - origin;
        float desiredDistance = cameraOffset.magnitude;

        DesiredDistance = desiredDistance;
        if (desiredDistance <= Epsilon)
        {
            ResetCollisionState();
            return;
        }

        Vector3 direction = cameraOffset / desiredDistance;
        bool hasObstacle = TryFindObstacle(
            origin,
            direction,
            desiredDistance,
            characterRoot,
            out RaycastHit closestHit,
            out float safeDistance
        );

        IsColliding = hasObstacle;
        CurrentObstacle = hasObstacle ? closestHit.collider : null;

        CameraCollisionState collisionState = GetExtraState<CameraCollisionState>(vcam);
        float targetDistance = hasObstacle ? safeDistance : desiredDistance;
        if (!collisionState.Initialized || deltaTime < 0f)
        {
            collisionState.CurrentDistance = targetDistance;
            collisionState.DistanceVelocity = 0f;
            collisionState.Initialized = true;
        }
        else if (targetDistance < collisionState.CurrentDistance)
        {
            collisionState.CurrentDistance = targetDistance;
            collisionState.DistanceVelocity = 0f;
        }
        else
        {
            collisionState.CurrentDistance = Mathf.SmoothDamp(
                collisionState.CurrentDistance,
                targetDistance,
                ref collisionState.DistanceVelocity,
                m_recoveryDuration,
                Mathf.Infinity,
                deltaTime
            );

            if (Mathf.Abs(collisionState.CurrentDistance - targetDistance) < 0.001f)
            {
                collisionState.CurrentDistance = targetDistance;
                collisionState.DistanceVelocity = 0f;
            }
        }

        CurrentDistance = collisionState.CurrentDistance;
        Vector3 correctedPosition = origin + direction * CurrentDistance;
        state.PositionCorrection += correctedPosition - desiredPosition;
    }

    private bool TryFindObstacle(
        Vector3 origin,
        Vector3 direction,
        float desiredDistance,
        Transform characterRoot,
        out RaycastHit closestHit,
        out float safeDistance)
    {
        closestHit = default;
        safeDistance = desiredDistance;

        float castStartDistance = Mathf.Min(m_cameraRadius, desiredDistance);
        float castDistance = desiredDistance - castStartDistance;
        if (castDistance <= Epsilon || m_collisionFilter.value == 0)
            return false;

        Vector3 castOrigin = origin + direction * castStartDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            castOrigin,
            m_cameraRadius,
            direction,
            m_hitBuffer,
            castDistance,
            m_collisionFilter,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.PositiveInfinity;
        bool foundObstacle = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = m_hitBuffer[i];
            if (ShouldIgnore(hit.collider, characterRoot) || hit.distance >= closestDistance)
                continue;

            if (hit.distance <= Epsilon && IsBehindOrigin(hit.collider, origin, direction))
                continue;

            closestDistance = hit.distance;
            closestHit = hit;
            foundObstacle = true;
        }

        if (!foundObstacle)
            return false;

        safeDistance = Mathf.Clamp(
            castStartDistance + closestDistance - m_surfaceOffset,
            0f,
            desiredDistance
        );
        return true;
    }

    private bool ShouldIgnore(Collider hitCollider, Transform characterRoot)
    {
        if (hitCollider == null)
            return true;

        Transform hitTransform = hitCollider.transform;
        if (characterRoot != null &&
            (hitTransform == characterRoot || hitTransform.IsChildOf(characterRoot)))
        {
            return true;
        }

        return !string.IsNullOrEmpty(m_ignoreTag) && hitCollider.CompareTag(m_ignoreTag);
    }

    private static bool IsBehindOrigin(
        Collider hitCollider,
        Vector3 origin,
        Vector3 cameraDirection)
    {
        Vector3 closestPoint = hitCollider.ClosestPoint(origin);
        Vector3 obstacleOffset = closestPoint - origin;
        if (obstacleOffset.sqrMagnitude <= Epsilon)
            obstacleOffset = hitCollider.bounds.center - origin;

        return Vector3.Dot(obstacleOffset, cameraDirection) <= 0f;
    }

    private void ResetCollisionState()
    {
        IsColliding = false;
        CurrentObstacle = null;
        DesiredDistance = 0f;
        CurrentDistance = 0f;
    }
}
