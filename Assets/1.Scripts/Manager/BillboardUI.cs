using UnityEngine;

[DisallowMultipleComponent]
public class BillboardUI : MonoBehaviour
{
    [SerializeField] private Transform m_targetCamera;
    [SerializeField] private bool m_yawOnly;
    [SerializeField] private bool m_findMainCameraOnEnable = true;

    private void OnEnable()
    {
        if (m_targetCamera == null && m_findMainCameraOnEnable)
        {
            CacheMainCamera();
        }
    }

    private void LateUpdate()
    {
        if (m_targetCamera == null)
        {
            CacheMainCamera();
        }

        if (m_targetCamera == null)
        {
            return;
        }

        if (m_yawOnly)
        {
            RotateYawOnly();
            return;
        }

        transform.rotation = m_targetCamera.rotation;
    }

    private void CacheMainCamera()
    {
        if (Camera.main != null)
        {
            m_targetCamera = Camera.main.transform;
        }
    }

    private void RotateYawOnly()
    {
        Vector3 direction = transform.position - m_targetCamera.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(direction);
    }
}
