using TMPro;
using UnityEngine;

/// <summary>
/// 조작 캐릭터의 스킬 이름·설명·쿨타임·상태를 우측 상단 패널(Figma <c>Skill_Narin</c>/<c>Skill_Seoha</c>/<c>Skill_Chungsol</c>)에 표시합니다.
/// </summary>
/// <remarks>
/// 레이아웃은 씬에 미리 배치돼 있고, 이 컴포넌트는 값과 표시 여부만 갱신합니다. 스킬이 없는 캐릭터를
/// 조작하면 패널을 숨깁니다. 상태 줄은 <see cref="CharacterSkill.HudDetailText"/>가 비면 숨겨지며,
/// 줄 배치는 패널의 VerticalLayoutGroup이 맡습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class SkillHud : MonoBehaviour
{
    [Header("References")]
    [Tooltip("스킬 패널 루트입니다. 조작 캐릭터에게 스킬이 있을 때만 켜집니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("\"[C] 스킬 이름\" 제목 텍스트입니다.")]
    [SerializeField] private TMP_Text m_titleText;

    [Tooltip("스킬 설명 텍스트입니다.")]
    [SerializeField] private TMP_Text m_descriptionText;

    [Tooltip("쿨타임 텍스트입니다.")]
    [SerializeField] private TMP_Text m_cooldownText;

    [Tooltip("쿨타임 아래 상태 줄입니다(지속 시간, 회복량, 특수탄 수). 스킬이 내용을 주지 않으면 숨깁니다.")]
    [SerializeField] private TMP_Text m_detailText;

    [Header("Squad")]
    [Tooltip("조작 캐릭터와 스킬 키를 제공하는 SquadManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Text")]
    [Tooltip("쿨타임 표시 형식입니다. {0}에 남은 시간, {1}에 전체 시간(초)이 들어갑니다.")]
    [SerializeField] private string m_cooldownFormat = "쿨타임 {0:0.0} / {1:0.0}초";

    private CharacterSkill m_shownSkill;

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
        CharacterSkill skill = m_squadManager != null ? m_squadManager.PlayerSquadMemberSkill : null;
        if (skill == null)
        {
            m_shownSkill = null;
            SetVisible(false);
            return;
        }

        if (skill != m_shownSkill)
        {
            m_shownSkill = skill;
            ApplySkill(skill);
        }

        SetVisible(true);

        if (m_cooldownText != null)
        {
            m_cooldownText.text = string.Format(m_cooldownFormat, skill.CooldownRemaining, skill.CooldownDuration);
        }

        if (m_detailText != null)
        {
            string detail = skill.HudDetailText;
            bool hasDetail = !string.IsNullOrEmpty(detail);
            if (m_detailText.gameObject.activeSelf != hasDetail)
            {
                m_detailText.gameObject.SetActive(hasDetail);
            }

            if (hasDetail)
            {
                m_detailText.text = detail;
            }
        }
    }

    /// <summary>캐릭터가 바뀔 때만 바뀌는 제목과 설명을 채웁니다.</summary>
    private void ApplySkill(CharacterSkill skill)
    {
        if (m_titleText != null)
        {
            m_titleText.text = $"[{m_squadManager.SkillKey}] {skill.DisplayName}";
        }

        if (m_descriptionText != null)
        {
            m_descriptionText.text = skill.Description;
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
