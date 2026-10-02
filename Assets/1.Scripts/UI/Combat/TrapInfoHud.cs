using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조작 대원이 설치 가능한 함정을 바라보고 있을 때 함정 이름·설치 비용·설명과 F 홀드 진행도를 표시합니다.
/// </summary>
/// <remarks>
/// 구조 HUD(<see cref="ReviveHudController"/>)와 같은 방식입니다. 레이아웃은 씬에 미리 배치돼 있고(Figma
/// <c>Trap HUD</c>의 <c>Rectangle 71</c> 영역), 이 컴포넌트는 값과 표시 여부만 갱신합니다.
///
/// 표시 조건은 <see cref="InteractionController.Current"/>가 <see cref="Trap"/>인지 하나뿐입니다. 상호작용
/// 대상 판정(거리, 설치 구간, 자원)을 여기서 다시 하면 HUD와 실제 F 입력 결과가 갈릴 수 있어서입니다.
/// 그래서 자원이 모자라 설치할 수 없는 함정은 대상이 되지 않고, 안내도 뜨지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TrapInfoHud : MonoBehaviour
{
    [Header("References")]
    [Tooltip("안내 패널 루트입니다. 함정을 바라볼 때만 켜집니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("함정 이름 텍스트입니다.")]
    [SerializeField] private TMP_Text m_nameText;

    [Tooltip("설치 비용 텍스트입니다. 비용이 없는 함정이면 비용 아이콘과 함께 숨깁니다.")]
    [SerializeField] private TMP_Text m_costText;

    [Tooltip("설치 비용 아이콘입니다. 선택 사항입니다.")]
    [SerializeField] private GameObject m_costIcon;

    [Tooltip("함정 설명 텍스트입니다.")]
    [SerializeField] private TMP_Text m_descriptionText;

    [Tooltip("F 키 둘레의 원형 홀드 게이지(Filled)입니다. 선택 사항입니다.")]
    [SerializeField] private Image m_holdGaugeFill;

    [Header("Squad")]
    [Tooltip("조작 대원을 제공하는 SquadManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Text")]
    [Tooltip("설치 비용 표시 형식입니다. {0}에 수량이 들어갑니다.")]
    [SerializeField] private string m_costFormat = "{0} 소모";

    private InteractionController m_interaction;
    private GameObject m_interactionOwner;
    private Trap m_shownTrap;

    private void Awake()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        SetVisible(false);
    }

    private void LateUpdate()
    {
        InteractionController interaction = ResolveInteraction();
        Trap trap = interaction != null ? interaction.Current as Trap : null;
        if (trap == null)
        {
            m_shownTrap = null;
            SetVisible(false);
            return;
        }

        if (trap != m_shownTrap)
        {
            m_shownTrap = trap;
            ApplyTrap(trap);
        }

        SetVisible(true);

        if (m_holdGaugeFill != null)
        {
            m_holdGaugeFill.fillAmount = interaction.HoldProgress01;
        }
    }

    /// <summary>조작 대원의 InteractionController를 찾습니다. 대원이 바뀔 때만 다시 찾습니다.</summary>
    private InteractionController ResolveInteraction()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
            if (m_squadManager == null)
            {
                return null;
            }
        }

        SquadMemberController member = m_squadManager.PlayerSquadMember;
        GameObject owner = member != null ? member.gameObject : null;
        if (owner != m_interactionOwner)
        {
            m_interactionOwner = owner;
            m_interaction = owner != null ? owner.GetComponent<InteractionController>() : null;
        }

        return m_interaction;
    }

    private void ApplyTrap(Trap trap)
    {
        if (m_nameText != null)
        {
            m_nameText.text = trap.DisplayName;
        }

        if (m_descriptionText != null)
        {
            m_descriptionText.text = trap.Description;
        }

        ResourceCost cost = trap.BuildCost;
        bool hasCost = cost.IsValid;
        if (m_costText != null)
        {
            m_costText.gameObject.SetActive(hasCost);
            if (hasCost)
            {
                m_costText.text = string.Format(m_costFormat, cost.Amount);
            }
        }

        if (m_costIcon != null)
        {
            m_costIcon.SetActive(hasCost);
        }
    }

    private void SetVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }
}
