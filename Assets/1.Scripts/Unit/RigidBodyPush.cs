using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// <see cref="CharacterController"/>가 충돌한 Rigidbody 오브젝트를 이동 방향으로 밀어내는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 <see cref="CharacterController.OnControllerColliderHit"/> 콜백을 이용합니다.
/// 지정된 레이어에 속하고, kinematic이 아닌 <see cref="Rigidbody"/>만 밀어냅니다.
/// </remarks>
public class RigidBodyPush : MonoBehaviour
{
    /// <summary>
    /// 밀기 처리를 허용할 Rigidbody 오브젝트의 레이어 마스크입니다.
    /// </summary>
    [FormerlySerializedAs("pushLayers")]
    [SerializeField] private LayerMask m_pushLayers;

    /// <summary>
    /// Rigidbody 밀기 기능의 활성화 여부입니다.
    /// </summary>
    [FormerlySerializedAs("canPush")]
    [SerializeField] private bool m_canPush;

    /// <summary>
    /// 충돌한 Rigidbody에 적용할 밀기 힘입니다.
    /// </summary>
    [FormerlySerializedAs("strength")]
    [Range(0.5f, 5f)]
    [SerializeField] private float m_strength = 1.1f;

    /// <summary>
    /// 밀기 처리를 허용할 레이어 마스크를 반환합니다.
    /// </summary>
    public LayerMask PushLayers => m_pushLayers;

    /// <summary>
    /// Rigidbody 밀기 기능이 활성화되어 있는지 반환합니다.
    /// </summary>
    public bool CanPush => m_canPush;

    /// <summary>
    /// Rigidbody에 적용할 밀기 힘을 반환합니다.
    /// </summary>
    public float Strength => m_strength;

    /// <summary>
    /// 밀기 처리를 허용할 레이어 마스크를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 레이어 마스크입니다.</param>
    public void SetPushLayers(LayerMask value)
    {
        m_pushLayers = value;
    }

    /// <summary>
    /// Rigidbody 밀기 기능의 활성화 여부를 설정합니다.
    /// </summary>
    /// <param name="value">밀기 기능을 활성화하려면 <c>true</c>, 비활성화하려면 <c>false</c>입니다.</param>
    public void SetCanPush(bool value)
    {
        m_canPush = value;
    }

    /// <summary>
    /// Rigidbody에 적용할 밀기 힘을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 밀기 힘입니다. 0.5에서 5 사이로 제한됩니다.</param>
    public void SetStrength(float value)
    {
        m_strength = Mathf.Clamp(value, 0.5f, 5.0f);
    }

    /// <summary>
    /// CharacterController가 다른 Collider와 충돌했을 때 호출됩니다.
    /// </summary>
    /// <param name="hit">CharacterController 충돌 정보입니다.</param>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!m_canPush)
        {
            return;
        }

        PushRigidBodies(hit);
    }

    /// <summary>
    /// 충돌한 대상이 유효한 Rigidbody라면 이동 방향 기준으로 힘을 적용합니다.
    /// </summary>
    /// <param name="hit">CharacterController 충돌 정보입니다.</param>
    private void PushRigidBodies(ControllerColliderHit hit)
    {
        if (hit == null || hit.collider == null)
        {
            return;
        }

        Rigidbody body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic)
        {
            return;
        }

        int bodyLayerMask = 1 << body.gameObject.layer;
        if ((bodyLayerMask & m_pushLayers.value) == 0)
        {
            return;
        }

        if (hit.moveDirection.y < -0.3f)
        {
            return;
        }

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0.0f, hit.moveDirection.z);
        body.AddForce(pushDirection * m_strength, ForceMode.Impulse);
    }
}
