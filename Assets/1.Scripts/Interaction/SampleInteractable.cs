using UnityEngine;
using UnityEngine.Events;
using VInspector;

/// <summary>
/// <see cref="IInteractable"/> 기반 배선을 확인하기 위한 범용 샘플 상호작용체입니다.
/// </summary>
/// <remarks>
/// 도메인 로직(부활·시설 사용 등)은 포함하지 않습니다. 실제 상호작용체는 이 클래스를 참고해
/// <see cref="Interact"/>에서 자기 시스템의 함수(예: PlayerHealth 부활)를 호출하도록 구현하세요.
/// Collider가 있어야 <see cref="InteractionController"/>가 탐지합니다(트리거/논트리거 모두 가능).
/// </remarks>
[DisallowMultipleComponent]
public class SampleInteractable : MonoBehaviour, IInteractable
{
    /// <summary>인스펙터에 노출하기 위한 상호작용 이벤트(주체 GameObject 전달) 구체 타입입니다.</summary>
    [System.Serializable]
    public class InteractionUnityEvent : UnityEvent<GameObject> { }

    [Tooltip("UI 프롬프트에 표시할 문구입니다.")]
    [SerializeField] private string m_prompt = "상호작용";

    [Tooltip("홀드(꾹 누르기) 시간(초)입니다. 0 이하이면 탭(즉시)입니다.")]
    [SerializeField] private float m_holdDuration = 0.0f;

    [Tooltip("현재 상호작용 가능한지 여부입니다. 끄면 탐지 후보에서 제외됩니다.")]
    [SerializeField] private bool m_interactable = true;

    [Tooltip("한 번 상호작용하면 이후 비활성화(1회용)할지 여부입니다.")]
    [SerializeField] private bool m_singleUse = false;

    [Foldout("Events")]
    [Tooltip("상호작용이 실행될 때 호출됩니다. 실제 동작을 여기에 연결하세요.")]
    [SerializeField] private InteractionUnityEvent m_onInteract;

    /// <inheritdoc/>
    public float HoldDuration => m_holdDuration;

    /// <inheritdoc/>
    public bool CanInteract(GameObject interactor) => m_interactable;

    /// <inheritdoc/>
    public string GetPrompt() => m_prompt;

    /// <inheritdoc/>
    public void Interact(GameObject interactor)
    {
        if (!m_interactable)
        {
            return;
        }

        Debug.Log($"[SampleInteractable] '{m_prompt}' interacted by {interactor.name}", this);
        m_onInteract?.Invoke(interactor);

        if (m_singleUse)
        {
            m_interactable = false;
        }
    }
}
