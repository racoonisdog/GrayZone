using UnityEngine;

public class PlayerAnimationEventReceiver : MonoBehaviour
{
    [SerializeField] private ShelterPlayerFootstepEmitter m_footstepEmitter;

    private void Awake()
    {
        ResolveEmitter();
    }

    public void SetFootstepEmitter(ShelterPlayerFootstepEmitter footstepEmitter)
    {
        m_footstepEmitter = footstepEmitter;
    }

    public void OnFootstep(AnimationEvent animationEvent)
    {
        ResolveEmitter();
        m_footstepEmitter?.OnFootstep(animationEvent);
    }

    public void OnLand(AnimationEvent animationEvent)
    {
        ResolveEmitter();
        m_footstepEmitter?.OnLand(animationEvent);
    }

    private void ResolveEmitter()
    {
        if (m_footstepEmitter == null)
            m_footstepEmitter = GetComponentInParent<ShelterPlayerFootstepEmitter>();
    }
}
