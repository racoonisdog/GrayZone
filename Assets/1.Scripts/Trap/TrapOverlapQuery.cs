using UnityEngine;

/// <summary>트랩의 현재 트리거 형상과 같은 범위로 겹친 콜라이더를 찾습니다.</summary>
public static class TrapOverlapQuery
{
    private static readonly Collider[] EmptyResults = new Collider[0];

    public static Collider[] FindOverlaps(
        Collider trigger,
        LayerMask layerMask,
        QueryTriggerInteraction queryTriggerInteraction)
    {
        if (trigger == null)
        {
            return EmptyResults;
        }

        switch (trigger)
        {
            case BoxCollider boxCollider:
                return FindBoxOverlaps(boxCollider, layerMask, queryTriggerInteraction);

            case SphereCollider sphereCollider:
                return FindSphereOverlaps(sphereCollider, layerMask, queryTriggerInteraction);

            case CapsuleCollider capsuleCollider:
                return FindCapsuleOverlaps(capsuleCollider, layerMask, queryTriggerInteraction);

            default:
                // MeshCollider 등은 정확한 형상 쿼리를 지원하지 않으므로 world AABB로 보수적으로 처리한다.
                Bounds bounds = trigger.bounds;
                return Physics.OverlapBox(
                    bounds.center,
                    bounds.extents,
                    Quaternion.identity,
                    layerMask,
                    queryTriggerInteraction);
        }
    }

    private static Collider[] FindBoxOverlaps(
        BoxCollider boxCollider,
        LayerMask layerMask,
        QueryTriggerInteraction queryTriggerInteraction)
    {
        Transform boxTransform = boxCollider.transform;
        Vector3 halfExtents = Vector3.Scale(boxCollider.size * 0.5f, Absolute(boxTransform.lossyScale));

        return Physics.OverlapBox(
            boxTransform.TransformPoint(boxCollider.center),
            halfExtents,
            boxTransform.rotation,
            layerMask,
            queryTriggerInteraction);
    }

    private static Collider[] FindSphereOverlaps(
        SphereCollider sphereCollider,
        LayerMask layerMask,
        QueryTriggerInteraction queryTriggerInteraction)
    {
        Transform sphereTransform = sphereCollider.transform;
        float radius = sphereCollider.radius * MaxComponent(Absolute(sphereTransform.lossyScale));

        return Physics.OverlapSphere(
            sphereTransform.TransformPoint(sphereCollider.center),
            radius,
            layerMask,
            queryTriggerInteraction);
    }

    private static Collider[] FindCapsuleOverlaps(
        CapsuleCollider capsuleCollider,
        LayerMask layerMask,
        QueryTriggerInteraction queryTriggerInteraction)
    {
        Transform capsuleTransform = capsuleCollider.transform;
        Vector3 scale = Absolute(capsuleTransform.lossyScale);
        Vector3 localAxis;
        float axialScale;
        float radialScale;

        switch (capsuleCollider.direction)
        {
            case 0:
                localAxis = Vector3.right;
                axialScale = scale.x;
                radialScale = Mathf.Max(scale.y, scale.z);
                break;

            case 2:
                localAxis = Vector3.forward;
                axialScale = scale.z;
                radialScale = Mathf.Max(scale.x, scale.y);
                break;

            default:
                localAxis = Vector3.up;
                axialScale = scale.y;
                radialScale = Mathf.Max(scale.x, scale.z);
                break;
        }

        float radius = capsuleCollider.radius * radialScale;
        float height = Mathf.Max(capsuleCollider.height * axialScale, radius * 2f);
        float halfLineLength = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 center = capsuleTransform.TransformPoint(capsuleCollider.center);
        Vector3 axis = capsuleTransform.TransformDirection(localAxis).normalized;

        return Physics.OverlapCapsule(
            center - axis * halfLineLength,
            center + axis * halfLineLength,
            radius,
            layerMask,
            queryTriggerInteraction);
    }

    private static Vector3 Absolute(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static float MaxComponent(Vector3 value)
    {
        return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
    }
}
