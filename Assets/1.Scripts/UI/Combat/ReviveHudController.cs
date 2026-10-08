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

    [Tooltip("구조 키 둘레의 원형 게이지(F_key_gauge)입니다. 막대 게이지와 같은 값으로 채웁니다. 선택 사항입니다.")]
    [SerializeField] private Image m_keyGaugeFill;

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
        bool isReviving = reviveTarget != null && playerData.IsReviving;

        // 서하 스킬 같은 원격 구조는 대상이 상호작용 후보에서 빠져 위 경로로는 잡히지 않습니다. 그대로 두면 게이지가
        // 전혀 보이지 않아, 팀원이 기립 동작만 하다 갑자기 부활한 것처럼 보입니다. 내가 직접 구조 중이 아니면 보여 줍니다.
        if (!isReviving && TryGetRemoteRescueProgress(out float remoteProgress))
        {
            SetActive(m_promptRoot, false);
            SetActive(m_revivingRoot, true);
            SetGauge(remoteProgress);
            return;
        }

        if (reviveTarget == null)
        {
            HideHud();
            return;
        }

        SetActive(m_promptRoot, !isReviving);
        SetActive(m_revivingRoot, isReviving);
        SetGauge(isReviving ? playerData.ReviveGaugeAmount : 0.0f);
    }

    /// <summary>원격 구조가 진행 중인 팀원 중 가장 많이 찬 진행도를 찾습니다.</summary>
    /// <returns>원격 구조 중인 팀원이 하나라도 있으면 <c>true</c>입니다.</returns>
    /// <remarks>여럿이 동시에 구조되면 가장 앞선 것을 보여 줍니다. 게이지 칸이 하나뿐이라서입니다.</remarks>
    private bool TryGetRemoteRescueProgress(out float progress)
    {
        progress = 0.0f;
        if (m_squadManager == null)
        {
            return false;
        }

        bool found = false;
        var members = m_squadManager.SquadMembers;
        for (int i = 0; i < members.Count; i++)
        {
            DownedAllyInteractable target = members[i] != null ? members[i].GetComponent<DownedAllyInteractable>() : null;
            if (target == null || !target.IsRemoteRescueActive)
            {
                continue;
            }

            found = true;
            progress = Mathf.Max(progress, target.ReviveHoldProgress01);
        }

        return found;
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
        if (m_keyGaugeFill != null)
        {
            // 원형 채움 방식(시작 위치·방향)은 씬에서 정한 값을 그대로 씁니다. 아트마다 시작점이 달라서입니다.
            m_keyGaugeFill.type = Image.Type.Filled;
            m_keyGaugeFill.fillAmount = Mathf.Clamp01(amount);
        }

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
