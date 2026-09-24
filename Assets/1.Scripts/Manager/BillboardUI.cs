using UnityEngine;

[DisallowMultipleComponent]
public class BillboardUI : MonoBehaviour
{
    [SerializeField] private Transform m_targetCamera;
    [SerializeField] private bool m_yawOnly;
    [SerializeField] private bool m_findMainCameraOnEnable = true;

    /// <summary>수평 회전만 적용할지 여부를 설정합니다.</summary>
    /// <remarks>코드로 이 컴포넌트를 붙이는 쪽(예: <see cref="HealthSystemBase"/>)이 씁니다.</remarks>
    public void SetYawOnly(bool yawOnly)
    {
        m_yawOnly = yawOnly;
    }

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
