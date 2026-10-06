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

    [Tooltip("설치 비용 텍스트입니다. 비용이 없는 함정이면 '소모 없음' 문구를 표시합니다.")]
    [SerializeField] private TMP_Text m_costText;

    [Tooltip("설치 비용 아이콘입니다. 비용이 없는 함정이면 숨깁니다. 선택 사항입니다.")]
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

    [Tooltip("설치 비용이 0인 함정에 표시할 문구입니다.")]
    [SerializeField] private string m_noCostText = "소모 없음";

    [Tooltip("자원이 모자랄 때 비용 옆에 붙일 문구입니다.")]
    [SerializeField] private string m_insufficientText = "자원이 부족합니다!";

    [Tooltip("자원 부족 문구의 색입니다.")]
    [SerializeField] private Color m_insufficientColor = new Color32(255, 59, 59, 255);

    [Header("Focus Outline")]
    [Tooltip("안내가 뜬 함정에 외곽선을 그립니다.")]
    [SerializeField] private bool m_showFocusOutline = true;

    [Tooltip("외곽선 색입니다.")]
    [SerializeField] private Color m_focusOutlineColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);

    [Tooltip("외곽선 굵기(화면 픽셀)입니다. 거리와 관계없이 같은 굵기로 보입니다.")]
    [Range(0.0f, 10.0f)]
    [SerializeField] private float m_focusOutlineWidth = 3.0f;

    [Tooltip("외곽선 머티리얼(GrayZone/FocusOutline)입니다. 비워 두면 Resources/Outline/M_FocusOutline을 씁니다.")]
    [SerializeField] private Material m_focusOutlineMaterial;

    [Tooltip("외곽선 마스크 머티리얼(GrayZone/FocusOutlineMask)입니다. 비워 두면 Resources/Outline/M_FocusOutlineMask를 씁니다.")]
    [SerializeField] private Material m_focusOutlineMaskMaterial;

    private const string DefaultOutlineMaterialPath = "Outline/M_FocusOutline";
    private const string DefaultOutlineMaskMaterialPath = "Outline/M_FocusOutlineMask";
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    private InteractionController m_interaction;
    private GameObject m_interactionOwner;
    private Trap m_shownTrap;
    private FocusOutlineRenderer m_focusOutline;

    // 색·굵기를 바꿔도 프로젝트의 머티리얼 에셋이 바뀌지 않도록 복제본에 씁니다.
    private Material m_focusOutlineInstance;

    private void Awake()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        CreateFocusOutline();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (m_focusOutlineInstance != null)
        {
            Destroy(m_focusOutlineInstance);
        }
    }

    private void CreateFocusOutline()
    {
        Material outline = m_focusOutlineMaterial != null
            ? m_focusOutlineMaterial
            : Resources.Load<Material>(DefaultOutlineMaterialPath);
        Material mask = m_focusOutlineMaskMaterial != null
            ? m_focusOutlineMaskMaterial
            : Resources.Load<Material>(DefaultOutlineMaskMaterialPath);

        if (outline == null || mask == null)
        {
            Debug.LogWarning("[TrapInfoHud] 외곽선 머티리얼을 찾지 못해 함정 외곽선을 그리지 않습니다.", this);
            return;
        }

        m_focusOutlineInstance = new Material(outline);
        m_focusOutline = new FocusOutlineRenderer(mask, m_focusOutlineInstance);
    }

    /// <summary>안내가 뜬 함정의 외곽선을 이번 프레임에 그립니다.</summary>
    private void DrawFocusOutline(Trap trap)
    {
        if (!m_showFocusOutline || m_focusOutline == null || trap == null)
        {
            return;
        }

        m_focusOutlineInstance.SetColor(OutlineColorId, m_focusOutlineColor);
        m_focusOutlineInstance.SetFloat(OutlineWidthId, m_focusOutlineWidth);
        m_focusOutline.Draw(trap.gameObject);
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

        // 자원은 보고 있는 동안에도 바뀔 수 있어(다른 함정 설치, 획득) 비용 줄은 매 프레임 맞춥니다.
        RefreshCost(trap);

        SetVisible(true);
        DrawFocusOutline(trap);

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

    }

    /// <summary>설치 비용 줄을 갱신합니다. 자원이 모자라면 비용 옆에 빨간 경고 문구를 붙입니다.</summary>
    private void RefreshCost(Trap trap)
    {
        ResourceCost cost = trap.BuildCost;
        bool hasCost = cost.IsValid;
        if (m_costText != null)
        {
            // 비용 줄은 항상 남깁니다. 0일 때 줄째 숨기면 공짜인지 표시 누락인지 구분할 수 없어서입니다.
            string text = hasCost ? string.Format(m_costFormat, cost.Amount) : m_noCostText;
            if (hasCost && Trap.IsDebugFreeBuild)
            {
                // 디버그 무료 중임을 알립니다. 표시가 없으면 자원이 왜 안 줄었는지 헷갈립니다.
                text += "  (디버그 무료)";
            }
            else if (hasCost && !trap.CanAffordBuild)
            {
                // 비용 칸 하나에 함께 적습니다. 따로 텍스트를 두면 씬마다 배선이 필요합니다.
                text += $"  <color=#{ColorUtility.ToHtmlStringRGBA(m_insufficientColor)}>{m_insufficientText}</color>";
            }

            if (m_costText.text != text)
            {
                m_costText.text = text;
            }
        }

        if (m_costIcon != null && m_costIcon.activeSelf != hasCost)
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
