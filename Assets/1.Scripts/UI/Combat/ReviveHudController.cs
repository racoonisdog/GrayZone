using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 현재 PlayerSquadMember 데이터에 따라 구조 HUD 패널을 전환합니다.
/// </summary>
[DisallowMultipleComponent]
public class ReviveHudController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject m_promptRoot;
    [SerializeField] private GameObject m_revivingRoot;
    [SerializeField] private Image m_gaugeFill;

    [Header("Squad")]
    [SerializeField] private SquadManager m_squadManager;

    private void Reset()
    {
        AutoFindHudReferences();
    }

    private void Awake()
    {
        AutoFindHudReferences();
        UpdateHud();
    }

    private void OnEnable()
    {
        UpdateHud();
    }

    private void LateUpdate()
    {
        UpdateHud();
    }

    private void AutoFindHudReferences()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_promptRoot == null)
        {
            Transform prompt = transform.Find("Note_Revive");
            if (prompt != null)
            {
                m_promptRoot = prompt.gameObject;
            }
        }

        if (m_revivingRoot == null)
        {
            Transform reviving = transform.Find("Note_Reviving");
            if (reviving != null)
            {
                m_revivingRoot = reviving.gameObject;
            }
        }

        if (m_gaugeFill == null && m_revivingRoot != null)
        {
            Transform fill = m_revivingRoot.transform.Find("Gauge_Track/Gauge_Fill");
            if (fill != null)
            {
                m_gaugeFill = fill.GetComponent<Image>();
            }
        }
    }

    private void UpdateHud()
    {
        PlayerbleUnitData playerData = ResolvePlayerSquadMemberData();
        DownedAllyInteractable reviveTarget = playerData != null ? playerData.CurrentReviveInteractionTarget : null;
        if (reviveTarget == null)
        {
            HideHud();
            return;
        }

        bool isReviving = playerData.IsReviving;
        SetActive(m_promptRoot, !isReviving);
        SetActive(m_revivingRoot, isReviving);
        SetGauge(isReviving ? playerData.ReviveGaugeAmount : 0.0f);
    }

    private PlayerbleUnitData ResolvePlayerSquadMemberData()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_squadManager != null)
        {
            return m_squadManager.PlayerSquadMemberData;
        }

        PlayerbleUnitData[] dataSources = FindObjectsByType<PlayerbleUnitData>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < dataSources.Length; i++)
        {
            PlayerbleUnitData data = dataSources[i];
            if (data != null && data.IsPlayerSquadMember)
            {
                return data;
            }
        }

        return null;
    }

    private void HideHud()
    {
        SetActive(m_promptRoot, false);
        SetActive(m_revivingRoot, false);
        SetGauge(0.0f);
    }

    private void SetGauge(float amount)
    {
        if (m_gaugeFill == null)
        {
            return;
        }

        m_gaugeFill.type = Image.Type.Filled;
        m_gaugeFill.fillMethod = Image.FillMethod.Horizontal;
        m_gaugeFill.fillOrigin = 0;
        m_gaugeFill.fillAmount = Mathf.Clamp01(amount);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
