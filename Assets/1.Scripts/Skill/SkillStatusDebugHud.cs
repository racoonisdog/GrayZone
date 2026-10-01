using UnityEngine;

/// <summary>최종 HUD 제작 전 스킬 쿨타임과 지속 상태를 확인하기 위한 임시 IMGUI 표시입니다.</summary>
[DisallowMultipleComponent]
public sealed class SkillStatusDebugHud : MonoBehaviour
{
    private SquadManager m_squadManager;
    private GUIStyle m_titleStyle;
    private GUIStyle m_bodyStyle;

    public static void EnsureAttached(SquadManager squadManager)
    {
        if (squadManager != null && squadManager.GetComponent<SkillStatusDebugHud>() == null)
        {
            squadManager.gameObject.AddComponent<SkillStatusDebugHud>();
        }
    }

    private void Awake()
    {
        m_squadManager = GetComponent<SquadManager>();
    }

    private void OnGUI()
    {
        CharacterSkill skill = m_squadManager != null ? m_squadManager.PlayerSquadMemberSkill : null;
        if (skill == null)
        {
            return;
        }

        EnsureStyles();
        Rect area = new Rect(Mathf.Max(12.0f, Screen.width - 342.0f), 18.0f, 324.0f, 112.0f);
        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label($"[C] {skill.DisplayName}", m_titleStyle);
        GUILayout.Label($"쿨타임  {skill.CooldownRemaining:0.0} / {skill.CooldownDuration:0.0}초", m_bodyStyle);

        if (skill.ActiveDuration > 0.0f)
        {
            string label = skill.IsActive ? "버프 남은 시간" : "버프 지속시간";
            float value = skill.IsActive ? skill.ActiveRemaining : skill.ActiveDuration;
            GUILayout.Label($"{label}  {value:0.0}초", m_bodyStyle);
        }
        else if (skill is ChungSolDragonBreathSkill dragonBreath)
        {
            string state = dragonBreath.IsLoadingSpecialAmmo
                ? "특수탄 장전 중"
                : $"용숨결탄  {dragonBreath.SpecialRoundsRemaining} / {dragonBreath.SpecialRoundsCapacity}";
            GUILayout.Label(state, m_bodyStyle);
        }
        else
        {
            GUILayout.Label(skill.IsReady ? "사용 가능" : "효과 적용 완료", m_bodyStyle);
        }

        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (m_titleStyle != null)
        {
            return;
        }

        m_titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1.0f, 0.82f, 0.25f) },
        };
        m_bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white },
        };
    }
}
