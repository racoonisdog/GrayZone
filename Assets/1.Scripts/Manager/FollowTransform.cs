using UnityEngine;

public class FollowTransform : MonoBehaviour
{
    [Header("Follow Target")]
    public Transform target;

    [Header("Offset (target local space)")]
    public Vector3 localOffset = Vector3.zero;

    [Header("Options")]
    public bool followRotation = true;

    void LateUpdate()
    {
        if (target == null) return;

        transform.position = target.TransformPoint(localOffset);
        if (followRotation) transform.rotation = target.rotation;
    }

    public void Bind(Transform newTarget, Vector3 offset, bool rotFollow)
    {
        target = newTarget;
        localOffset = offset;
        followRotation = rotFollow;

        // 즉시 스냅(첫 프레임 튐 방지)
        if (target != null)
        {
            transform.position = target.TransformPoint(localOffset);
            if (followRotation) transform.rotation = target.rotation;
        }
    }
}