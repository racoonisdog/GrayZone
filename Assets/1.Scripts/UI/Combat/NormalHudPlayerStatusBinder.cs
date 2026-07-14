using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Normal HUD 상태 위젯을 현재 PlayerSquadMember 데이터에 연결합니다.
/// </summary>
[DisallowMultipleComponent]
public class NormalHudPlayerStatusBinder : MonoBehaviour
{
    [Serializable]
    private sealed class HealthGaugeSlot
    {
        [SerializeField] private string m_childName;
        [SerializeField] private TMP_Text m_hpText;
        [SerializeField] private RawImage m_hpGaugeRawImage;
        [SerializeField] private Image m_hpGaugeImage;
        [SerializeField] private float m_fillStartX = 176.0f;
        [SerializeField] private float m_fillWidth = 404.0f;

        public HealthGaugeSlot(string childName, float fillStartX, float fillWidth)
        {
            m_childName = childName;
            m_fillStartX = fillStartX;
            m_fillWidth = fillWidth;
        }

        public void AutoFind(Transform root)
        {
            if (root == null || string.IsNullOrWhiteSpace(m_childName))
            {
                return;
            }

            Transform slotRoot = FindDeep(root, m_childName);
            if (slotRoot == null)
            {
                return;
            }

            if (m_hpText == null)
            {
                m_hpText = slotRoot.GetComponentInChildren<TMP_Text>(true);
            }

            if (m_hpGaugeRawImage == null)
            {
                m_hpGaugeRawImage = slotRoot.GetComponent<RawImage>();
            }

            if (m_hpGaugeImage == null)
            {
                m_hpGaugeImage = slotRoot.GetComponent<Image>();
            }
        }

        public void Update(PlayerbleUnitData data, float referenceWidth)
        {
            bool hasData = data != null;
            SetVisible(hasData);

            int currentHp = hasData ? data.CurrentHp : 0;
            int maxHp = hasData ? data.MaxHp : 0;
            float normalizedHp = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0.0f;

            if (m_hpText != null)
            {
                m_hpText.text = currentHp.ToString();
            }

            UpdateGauge(normalizedHp, referenceWidth);
        }

        private void SetVisible(bool visible)
        {
            if (m_hpGaugeRawImage != null)
            {
                m_hpGaugeRawImage.gameObject.SetActive(visible);
            }
            else if (m_hpGaugeImage != null)
            {
                m_hpGaugeImage.gameObject.SetActive(visible);
            }

            if (m_hpText != null)
            {
                m_hpText.gameObject.SetActive(visible);
            }
        }

        private void UpdateGauge(float normalizedHp, float referenceWidth)
        {
            if (m_hpGaugeImage != null)
            {
                m_hpGaugeImage.fillAmount = normalizedHp;
            }

            if (m_hpGaugeRawImage == null)
            {
                return;
            }

            UpdateRawImageGauge(m_hpGaugeRawImage, normalizedHp, referenceWidth, m_fillStartX, m_fillWidth);
        }
    }

    [Header("References")]
    [SerializeField] private TMP_Text m_currentHpText;
    [SerializeField] private RawImage m_hpGaugeRawImage;
    [SerializeField] private Image m_hpGaugeImage;

    [Header("Ammo")]
    [Tooltip("현재 탄창 탄약 수 텍스트(Mag_Count)입니다.")]
    [SerializeField] private TMP_Text m_magCountText;
    [Tooltip("예비 탄약 수 텍스트(Mag_All)입니다.")]
    [SerializeField] private TMP_Text m_magAllText;

    [Header("Player Data")]
    [SerializeField] private SquadManager m_squadManager;
    [SerializeField] private PlayerbleUnitData[] m_playerDataSources;
    [SerializeField] private bool m_autoFindPlayerData = true;
    [SerializeField] private bool m_refreshControlledPlayerEveryFrame = true;

    [Header("Gauge Crop")]
    [SerializeField] private float m_referenceWidth = 1920.0f;
    [SerializeField] private float m_hpGaugeFillStartX = 176.0f;
    [SerializeField] private float m_hpGaugeFillWidth = 404.0f;

    [Header("Team Gauges")]
    [SerializeField] private HealthGaugeSlot[] m_teamGaugeSlots =
    {
        new HealthGaugeSlot("Gauge_HP-1", 176.0f, 404.0f),
        new HealthGaugeSlot("Gauge_HP-2", 176.0f, 404.0f),
    };

    private PlayerbleUnitData m_playerSquadMemberData;
    private readonly List<PlayerbleUnitData> m_sortedTeamData = new();

    private void Reset()
    {
        AutoFindHudReferences();
    }

    private void Awake()
    {
        // 참조는 인스펙터에서 직접 지정하는 것이 기본. 여기서는 UI 요소를 자동 탐색하지 않고 검증만 한다.
        // (에디터에서 컴포넌트를 추가/Reset하면 AutoFindHudReferences가 한 번 채워 직렬화한다.)
        EnsureTeamGaugeSlots();
        if (m_squadManager == null)
        {
            m_squadManager = UnityEngine.Object.FindFirstObjectByType<SquadManager>();
        }

        ValidateReferences();
        RefreshPlayerDataSources();
        SetPlayerSquadMemberData(ResolvePlayerSquadMemberData());
        UpdateHud();
    }

    /// <summary>
    /// 필수 참조가 인스펙터에서 지정되지 않았으면 경고를 출력합니다.
    /// </summary>
    private void ValidateReferences()
    {
        WarnIfNull(m_currentHpText, "CurrentHP 텍스트(m_currentHpText)");
        WarnIfNull(m_magCountText, "탄창 탄약 텍스트(m_magCountText / Mag_Count)");
        WarnIfNull(m_magAllText, "예비 탄약 텍스트(m_magAllText / Mag_All)");
        WarnIfNull(m_squadManager, "SquadManager(m_squadManager)");

        if (m_hpGaugeImage == null && m_hpGaugeRawImage == null)
        {
            Debug.LogWarning("[NormalHudPlayerStatusBinder] 필수 참조 'HP 게이지(m_hpGaugeImage 또는 m_hpGaugeRawImage)'가 비어 있습니다. 인스펙터에서 지정하세요.", this);
        }
    }

    private void WarnIfNull(UnityEngine.Object reference, string label)
    {
        if (reference == null)
        {
            Debug.LogWarning($"[NormalHudPlayerStatusBinder] 필수 참조 '{label}'가 비어 있습니다. 인스펙터에서 지정하세요.", this);
        }
    }

    private void OnEnable()
    {
        RefreshPlayerDataSources();
        SetPlayerSquadMemberData(ResolvePlayerSquadMemberData());
        UpdateHud();
    }

    private void OnDisable()
    {
        SetPlayerSquadMemberData(null);
    }

    private void LateUpdate()
    {
        if (!m_refreshControlledPlayerEveryFrame)
        {
            return;
        }

        PlayerbleUnitData nextData = ResolvePlayerSquadMemberData();
        if (nextData != m_playerSquadMemberData)
        {
            SetPlayerSquadMemberData(nextData);
        }

        UpdateHud();
    }

    private void AutoFindHudReferences()
    {
        EnsureTeamGaugeSlots();

        if (m_squadManager == null)
        {
            m_squadManager = UnityEngine.Object.FindFirstObjectByType<SquadManager>();
        }

        if (m_currentHpText == null)
        {
            Transform currentHp = FindDeep(transform, "CurrentHP");
            if (currentHp != null)
            {
                m_currentHpText = currentHp.GetComponent<TMP_Text>();
            }
        }

        if (m_magCountText == null)
        {
            Transform magCount = FindDeep(transform, "Mag_Count");
            if (magCount != null)
            {
                m_magCountText = magCount.GetComponent<TMP_Text>();
            }
        }

        if (m_magAllText == null)
        {
            Transform magAll = FindDeep(transform, "Mag_All");
            if (magAll != null)
            {
                m_magAllText = magAll.GetComponent<TMP_Text>();
            }
        }

        if (m_hpGaugeRawImage == null)
        {
            Transform gaugeHp = FindDeep(transform, "Gauge_HP");
            if (gaugeHp != null)
            {
                m_hpGaugeRawImage = gaugeHp.GetComponent<RawImage>();
            }
        }

        if (m_hpGaugeImage == null)
        {
            Transform gaugeHp = FindDeep(transform, "Gauge_HP");
            if (gaugeHp != null)
            {
                m_hpGaugeImage = gaugeHp.GetComponent<Image>();
            }
        }

        for (int i = 0; i < m_teamGaugeSlots.Length; i++)
        {
            m_teamGaugeSlots[i]?.AutoFind(transform);
        }
    }

    private void EnsureTeamGaugeSlots()
    {
        if (m_teamGaugeSlots != null && m_teamGaugeSlots.Length >= 2)
        {
            return;
        }

        m_teamGaugeSlots = new[]
        {
            new HealthGaugeSlot("Gauge_HP-1", m_hpGaugeFillStartX, m_hpGaugeFillWidth),
            new HealthGaugeSlot("Gauge_HP-2", m_hpGaugeFillStartX, m_hpGaugeFillWidth),
        };
    }

    private void RefreshPlayerDataSources()
    {
        if (!m_autoFindPlayerData)
        {
            return;
        }

        if (m_squadManager != null)
        {
            IReadOnlyList<PlayerbleUnitData> squadData = m_squadManager.PlayerDataSources;
            if (squadData != null && squadData.Count > 0)
            {
                m_playerDataSources = CopyPlayerDataSources(squadData);
                return;
            }
        }

        if (m_playerDataSources != null && m_playerDataSources.Length > 0)
        {
            return;
        }

        m_playerDataSources = UnityEngine.Object.FindObjectsByType<PlayerbleUnitData>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    private static PlayerbleUnitData[] CopyPlayerDataSources(IReadOnlyList<PlayerbleUnitData> source)
    {
        PlayerbleUnitData[] result = new PlayerbleUnitData[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            result[i] = source[i];
        }

        return result;
    }

    private PlayerbleUnitData ResolvePlayerSquadMemberData()
    {
        if (m_playerDataSources == null || m_playerDataSources.Length == 0)
        {
            RefreshPlayerDataSources();
        }

        if (m_playerDataSources == null)
        {
            return null;
        }

        for (int i = 0; i < m_playerDataSources.Length; i++)
        {
            PlayerbleUnitData data = m_playerDataSources[i];
            if (data != null && data.IsPlayerSquadMember)
            {
                return data;
            }
        }

        for (int i = 0; i < m_playerDataSources.Length; i++)
        {
            PlayerbleUnitData data = m_playerDataSources[i];
            if (data != null && data.CanDeploy)
            {
                return data;
            }
        }

        return null;
    }

    private void SetPlayerSquadMemberData(PlayerbleUnitData nextData)
    {
        if (m_playerSquadMemberData == nextData)
        {
            return;
        }

        if (m_playerSquadMemberData != null)
        {
            m_playerSquadMemberData.OnPublicDataChanged -= HandlePlayerDataChanged;
        }

        m_playerSquadMemberData = nextData;

        if (m_playerSquadMemberData != null)
        {
            m_playerSquadMemberData.OnPublicDataChanged += HandlePlayerDataChanged;
        }
    }

    private void HandlePlayerDataChanged()
    {
        UpdateHud();
    }

    private void UpdateHud()
    {
        int currentHp = m_playerSquadMemberData != null ? m_playerSquadMemberData.CurrentHp : 0;
        int maxHp = m_playerSquadMemberData != null ? m_playerSquadMemberData.MaxHp : 0;
        float normalizedHp = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0.0f;

        if (m_currentHpText != null)
        {
            m_currentHpText.text = currentHp.ToString();
        }

        if (m_magCountText != null)
        {
            m_magCountText.text = (m_playerSquadMemberData != null ? m_playerSquadMemberData.CurrentMagazineAmmo : 0).ToString();
        }

        if (m_magAllText != null)
        {
            m_magAllText.text = (m_playerSquadMemberData != null ? m_playerSquadMemberData.ReserveAmmo : 0).ToString();
        }

        UpdateGauge(normalizedHp);
        UpdateTeamGauges();
    }

    /// <summary>
    /// 이름이 일치하는 자손 Transform을 재귀적으로 찾습니다(그룹 컨테이너로 재배치되어도 찾도록).
    /// </summary>
    private static Transform FindDeep(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != root && t.name == childName)
            {
                return t;
            }
        }

        return null;
    }

    private void UpdateGauge(float normalizedHp)
    {
        if (m_hpGaugeImage != null)
        {
            m_hpGaugeImage.fillAmount = normalizedHp;
        }

        if (m_hpGaugeRawImage == null)
        {
            return;
        }

        UpdateRawImageGauge(m_hpGaugeRawImage, normalizedHp, m_referenceWidth, m_hpGaugeFillStartX, m_hpGaugeFillWidth);
    }

    private void UpdateTeamGauges()
    {
        EnsureTeamGaugeSlots();
        BuildSortedTeamData();

        for (int i = 0; i < m_teamGaugeSlots.Length; i++)
        {
            PlayerbleUnitData data = i < m_sortedTeamData.Count ? m_sortedTeamData[i] : null;
            m_teamGaugeSlots[i]?.Update(data, m_referenceWidth);
        }
    }

    private void BuildSortedTeamData()
    {
        m_sortedTeamData.Clear();

        if (m_playerDataSources == null)
        {
            return;
        }

        for (int i = 0; i < m_playerDataSources.Length; i++)
        {
            PlayerbleUnitData data = m_playerDataSources[i];
            if (data == null || data == m_playerSquadMemberData)
            {
                continue;
            }

            m_sortedTeamData.Add(data);
        }

        m_sortedTeamData.Sort(CompareTeamData);
    }

    private static int CompareTeamData(PlayerbleUnitData left, PlayerbleUnitData right)
    {
        int reliabilityCompare = left.Reliability.CompareTo(right.Reliability);
        if (reliabilityCompare != 0)
        {
            return reliabilityCompare;
        }

        return string.CompareOrdinal(left.RuntimeId, right.RuntimeId);
    }

    private static void UpdateRawImageGauge(
        RawImage rawImage,
        float normalizedHp,
        float referenceWidth,
        float fillStartX,
        float fillWidth)
    {
        RectTransform rectTransform = rawImage.rectTransform;
        RectTransform parentRect = rectTransform.parent as RectTransform;
        float baseWidth = parentRect != null && parentRect.rect.width > 0.0f
            ? parentRect.rect.width
            : referenceWidth;

        float fillEndX = fillStartX + fillWidth * Mathf.Clamp01(normalizedHp);
        float visibleWidthNormalized = Mathf.Clamp01(fillEndX / Mathf.Max(1.0f, referenceWidth));

        rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
        rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
        rectTransform.pivot = new Vector2(0.0f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(baseWidth * visibleWidthNormalized, 0.0f);

        Rect uvRect = rawImage.uvRect;
        uvRect.x = 0.0f;
        uvRect.width = visibleWidthNormalized;
        rawImage.uvRect = uvRect;
    }
}
