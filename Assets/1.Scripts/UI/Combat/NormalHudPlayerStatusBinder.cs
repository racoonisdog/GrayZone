using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the Normal HUD player status widgets to the currently controlled player data.
/// </summary>
[DisallowMultipleComponent]
public class NormalHudPlayerStatusBinder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Text m_currentHpText;
    [SerializeField] private RawImage m_hpGaugeRawImage;
    [SerializeField] private Image m_hpGaugeImage;

    [Header("Player Data")]
    [SerializeField] private PlayerbleUnitData[] m_playerDataSources;
    [SerializeField] private bool m_autoFindPlayerData = true;
    [SerializeField] private bool m_refreshControlledPlayerEveryFrame = true;

    [Header("Gauge Crop")]
    [SerializeField] private float m_referenceWidth = 1920.0f;
    [SerializeField] private float m_hpGaugeFillStartX = 176.0f;
    [SerializeField] private float m_hpGaugeFillWidth = 404.0f;

    private PlayerbleUnitData m_currentPlayerData;

    private void Reset()
    {
        AutoFindHudReferences();
    }

    private void Awake()
    {
        AutoFindHudReferences();
        RefreshPlayerDataSources();
        SetCurrentPlayerData(ResolveControlledPlayerData());
        UpdateHud();
    }

    private void OnEnable()
    {
        RefreshPlayerDataSources();
        SetCurrentPlayerData(ResolveControlledPlayerData());
        UpdateHud();
    }

    private void OnDisable()
    {
        SetCurrentPlayerData(null);
    }

    private void LateUpdate()
    {
        if (!m_refreshControlledPlayerEveryFrame)
        {
            return;
        }

        PlayerbleUnitData nextData = ResolveControlledPlayerData();
        if (nextData != m_currentPlayerData)
        {
            SetCurrentPlayerData(nextData);
        }

        UpdateHud();
    }

    private void AutoFindHudReferences()
    {
        if (m_currentHpText == null)
        {
            Transform currentHp = transform.Find("CurrentHP");
            if (currentHp != null)
            {
                m_currentHpText = currentHp.GetComponent<Text>();
            }
        }

        if (m_hpGaugeRawImage == null)
        {
            Transform gaugeHp = transform.Find("Gauge_HP");
            if (gaugeHp != null)
            {
                m_hpGaugeRawImage = gaugeHp.GetComponent<RawImage>();
            }
        }

        if (m_hpGaugeImage == null)
        {
            Transform gaugeHp = transform.Find("Gauge_HP");
            if (gaugeHp != null)
            {
                m_hpGaugeImage = gaugeHp.GetComponent<Image>();
            }
        }
    }

    private void RefreshPlayerDataSources()
    {
        if (!m_autoFindPlayerData)
        {
            return;
        }

        if (m_playerDataSources != null && m_playerDataSources.Length > 0)
        {
            return;
        }

        m_playerDataSources = Object.FindObjectsByType<PlayerbleUnitData>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    private PlayerbleUnitData ResolveControlledPlayerData()
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
            if (data != null && data.IsPlayerControlled)
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

    private void SetCurrentPlayerData(PlayerbleUnitData nextData)
    {
        if (m_currentPlayerData == nextData)
        {
            return;
        }

        if (m_currentPlayerData != null)
        {
            m_currentPlayerData.OnPublicDataChanged -= HandlePlayerDataChanged;
        }

        m_currentPlayerData = nextData;

        if (m_currentPlayerData != null)
        {
            m_currentPlayerData.OnPublicDataChanged += HandlePlayerDataChanged;
        }
    }

    private void HandlePlayerDataChanged()
    {
        UpdateHud();
    }

    private void UpdateHud()
    {
        int currentHp = m_currentPlayerData != null ? m_currentPlayerData.CurrentHp : 0;
        int maxHp = m_currentPlayerData != null ? m_currentPlayerData.MaxHp : 0;
        float normalizedHp = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0.0f;

        if (m_currentHpText != null)
        {
            m_currentHpText.text = currentHp.ToString();
        }

        UpdateGauge(normalizedHp);
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

        RectTransform rectTransform = m_hpGaugeRawImage.rectTransform;
        RectTransform parentRect = rectTransform.parent as RectTransform;
        float baseWidth = parentRect != null && parentRect.rect.width > 0.0f
            ? parentRect.rect.width
            : m_referenceWidth;

        float fillEndX = m_hpGaugeFillStartX + m_hpGaugeFillWidth * Mathf.Clamp01(normalizedHp);
        float visibleWidthNormalized = Mathf.Clamp01(fillEndX / Mathf.Max(1.0f, m_referenceWidth));

        rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
        rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
        rectTransform.pivot = new Vector2(0.0f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(baseWidth * visibleWidthNormalized, 0.0f);

        Rect uvRect = m_hpGaugeRawImage.uvRect;
        uvRect.x = 0.0f;
        uvRect.width = visibleWidthNormalized;
        m_hpGaugeRawImage.uvRect = uvRect;
    }
}
