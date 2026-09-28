using UnityEngine;

/// <summary>
/// 투사체의 이동과 충돌에는 영향을 주지 않고 지정한 Visual만 회전시킵니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProjectileVisualSpinner : MonoBehaviour
{
    [Tooltip("비행 중 회전시킬 투사체 Visual입니다.")]
    [SerializeField] private Transform m_visualRoot;

    [Tooltip("Visual의 로컬 축을 기준으로 한 초당 회전 각도입니다.")]
    [SerializeField] private Vector3 m_rotationSpeed = new Vector3(720.0f, 0.0f, 0.0f);

    private void Update()
    {
        if (m_visualRoot == null)
        {
            return;
        }

        m_visualRoot.Rotate(m_rotationSpeed * Time.deltaTime, Space.Self);
    }
}
