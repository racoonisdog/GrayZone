using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 모든 시설이 공용으로 쓰는 업그레이드 창입니다. Canvas에 상시 존재하며 열 때 SetActive로 표시합니다.
/// </summary>
/// <remarks>
/// 특정 시설을 알지 않고 <see cref="Open"/>에 받은 facilityId로 <see cref="FacilityManager"/>에서 데이터를 pull합니다.
/// (시설 이름·제공 기능·비용/보유·업그레이드 실행 모두 FacilityManager/ShelterDataManager 경유)
/// </remarks>
public class FacilityUpgradeUI : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TMP_Text m_nameText;
    [SerializeField] private TMP_Text m_levelText; // "Lv.1 → Lv.2" (선택)

    [Header("Feature List (제공하는 기능)")]
    [SerializeField] private Transform m_featureRoot;
    [SerializeField] private LabelValueRow m_featureRowPrefab;

    [Header("Cost List")]
    [SerializeField] private Transform m_costRoot;
    [SerializeField] private LabelValueRow m_costRowPrefab;
    [SerializeField] private Color m_enoughColor = Color.white;
    [SerializeField] private Color m_lackColor = Color.red;

    [Header("Buttons / Notice")]
    [SerializeField] private Button m_upgradeButton;
    [SerializeField] private Button m_closeButton;
    [SerializeField] private GameObject m_insufficientNotice; // 자원 부족 알림
    [SerializeField] private GameObject m_maxLevelNotice;      // 최대 레벨 알림

    private string m_facilityId;

    private void Awake()
    {
        if (m_upgradeButton != null)
            m_upgradeButton.onClick.AddListener(OnUpgradeClicked);
        if (m_closeButton != null)
            m_closeButton.onClick.AddListener(Close);

        gameObject.SetActive(false);
    }

    /// <summary>지정한 시설의 업그레이드 창을 연다(여는 쪽이 자기 FacilityId 전달).</summary>
    public void Open(string facilityId)
    {
        m_facilityId = facilityId;
        gameObject.SetActive(true);
        Refresh();
    }

    /// <summary>창을 닫는다(상시 존재하므로 비활성화).</summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    private void Refresh()
    {
        FacilityManager fm = FacilityManager.Instance;
        if (fm == null || string.IsNullOrWhiteSpace(m_facilityId))
            return;

        FacilityState state = fm.GetState(m_facilityId);
        int level = fm.GetUpgradeLevel(m_facilityId);
        bool canUpgrade = fm.CanUpgrade(m_facilityId);   // 해금·최대레벨·비자원 조건
        bool canAfford = fm.CanAffordUpgrade(m_facilityId); // + 자원까지

        // 이름 / 레벨 표기
        if (m_nameText != null)
            m_nameText.text = state?.Definition != null ? state.Definition.FacilityName : m_facilityId;
        if (m_levelText != null)
            m_levelText.text = canUpgrade ? $"Lv.{level + 1} → Lv.{level + 2}" : $"Lv.{level + 1}";

        // 제공하는 기능 (리스트 길이만큼 생성)
        ClearChildren(m_featureRoot);
        if (m_featureRoot != null && m_featureRowPrefab != null)
        {
            foreach (FacilityFeatureLine line in fm.GetUpgradeFeatureLines(m_facilityId))
                Instantiate(m_featureRowPrefab, m_featureRoot).Set(line.Label, line.Value, m_enoughColor);
        }

        // 비용 (줄마다 보유 vs 요구 비교, 부족=lack 색)
        ClearChildren(m_costRoot);
        ShelterDataManager sdm = ShelterDataManager.Instance;
        if (m_costRoot != null && m_costRowPrefab != null)
        {
            foreach (CurrencyCost c in fm.GetUpgradeCost(m_facilityId).Costs)
            {
                int owned = sdm != null ? sdm.GetResourceAmount(c.Type) : 0;
                bool enough = owned >= c.Amount;
                Instantiate(m_costRowPrefab, m_costRoot)
                    .Set(c.Type.ToString(), $"{owned}/{c.Amount}", enough ? m_enoughColor : m_lackColor);
            }
        }

        // 버튼 / 알림
        if (m_upgradeButton != null)
            m_upgradeButton.interactable = canAfford;
        if (m_insufficientNotice != null)
            m_insufficientNotice.SetActive(canUpgrade && !canAfford); // 올릴 순 있는데 자원만 부족
        if (m_maxLevelNotice != null)
            m_maxLevelNotice.SetActive(state != null && state.IsUnlocked && !canUpgrade); // 해금됐는데 더 못 올림 = 최대
    }

    private void OnUpgradeClicked()
    {
        if (FacilityManager.Instance != null && FacilityManager.Instance.TryUpgrade(m_facilityId))
            Refresh(); // 성공 → 새 레벨 기준으로 다시 그림 (슬롯/건물은 매니저 이벤트로 자동 갱신)
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);
    }
}
