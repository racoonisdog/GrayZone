using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼·패널 같은 UI 오브젝트에 붙여, 인스펙터에서 상황 ID만 골라 사운드를 재생하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// <para>
/// 실제 재생은 <see cref="ShelterAudio"/>가 맡습니다. 이 컴포넌트는 "언제" 재생할지만 정합니다.
/// 각 칸을 <see cref="ShelterSoundId.None"/>으로 두면 그 시점에는 소리를 내지 않습니다.
/// </para>
/// <para>
/// - 클릭: 같은 오브젝트의 <see cref="Button"/> <c>onClick</c>에 자동 연결됩니다. Button이 없으면 무시합니다.<br/>
/// - 호버: 마우스가 올라갈 때 재생합니다. 비활성(interactable 꺼짐) 버튼에서는 재생하지 않습니다.<br/>
/// - 활성화/비활성화: 패널이 열리고 닫힐 때(<c>SetActive</c>) 재생합니다.<br/>
/// - 그 밖의 시점은 UnityEvent나 코드에서 <see cref="PlayClick"/> 등을 직접 호출합니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class ShelterSoundTrigger : MonoBehaviour, IPointerEnterHandler
{
    [Header("재생 시점별 상황")]
    [Tooltip("같은 오브젝트의 Button이 클릭될 때 재생할 상황입니다.")]
    [SerializeField] private ShelterSoundId m_clickSound = ShelterSoundId.UiButtonClick;

    [Tooltip("마우스가 올라갈 때 재생할 상황입니다.")]
    [SerializeField] private ShelterSoundId m_hoverSound = ShelterSoundId.None;

    [Tooltip("오브젝트가 활성화될 때(패널 열림) 재생할 상황입니다.")]
    [SerializeField] private ShelterSoundId m_enableSound = ShelterSoundId.None;

    [Tooltip("오브젝트가 비활성화될 때(패널 닫힘) 재생할 상황입니다.")]
    [SerializeField] private ShelterSoundId m_disableSound = ShelterSoundId.None;

    [Header("재생 방식")]
    [Tooltip("켜면 이 오브젝트 위치를 따라가는 3D 소리로 재생합니다. UI는 보통 끈 상태(2D)로 둡니다.")]
    [SerializeField] private bool m_positional;

    private Button m_button;

    private void Awake()
    {
        m_button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (m_button != null)
        {
            m_button.onClick.AddListener(PlayClick);
        }

        PlaySound(m_enableSound);
    }

    private void OnDisable()
    {
        if (m_button != null)
        {
            m_button.onClick.RemoveListener(PlayClick);
        }

        // 씬 종료 중 일괄 비활성화될 때는 닫힘 소리를 내지 않습니다.
        if (gameObject.scene.isLoaded)
        {
            PlaySound(m_disableSound);
        }
    }

    /// <inheritdoc/>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (m_button != null && !m_button.IsInteractable())
        {
            return;
        }

        PlaySound(m_hoverSound);
    }

    /// <summary>클릭 칸의 상황을 재생합니다. Button 외의 UnityEvent에서 연결할 때 씁니다.</summary>
    public void PlayClick() => PlaySound(m_clickSound);

    /// <summary>호버 칸의 상황을 재생합니다.</summary>
    public void PlayHover() => PlaySound(m_hoverSound);

    /// <summary>활성화 칸의 상황을 재생합니다.</summary>
    public void PlayEnable() => PlaySound(m_enableSound);

    /// <summary>비활성화 칸의 상황을 재생합니다.</summary>
    public void PlayDisable() => PlaySound(m_disableSound);

    private void PlaySound(ShelterSoundId id)
    {
        if (id == ShelterSoundId.None)
        {
            return;
        }

        if (m_positional)
        {
            ShelterAudio.PlayAttached(id, gameObject);
        }
        else
        {
            ShelterAudio.Play(id);
        }
    }
}
