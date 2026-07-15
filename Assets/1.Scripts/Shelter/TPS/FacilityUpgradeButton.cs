using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시설 UI의 업그레이드 버튼 클릭을 <see cref="UIManager"/>의 공용 시설 업그레이드 UI로 전달한다.
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class FacilityUpgradeButton : MonoBehaviour
{
    [Header("Navigation")]
    [SerializeField] private UIManager m_uiManager;

    [Header("Facility Source")]
    [Tooltip("IFacilityUpgradeable을 구현한 시설 컴포넌트를 연결합니다. 예: MedicalManager")]
    [SerializeField] private MonoBehaviour m_facilitySource;

    private Button m_button;
    private IFacilityUpgradeable m_facility;

    private void Awake()
    {
        m_button = GetComponent<Button>();
        CacheFacility();
    }

    private void OnEnable()
    {
        if (m_button == null)
            m_button = GetComponent<Button>();

        m_button.onClick.AddListener(OpenUpgradeUI);
    }

    private void OnDisable()
    {
        if (m_button != null)
            m_button.onClick.RemoveListener(OpenUpgradeUI);
    }

    /// <summary>연결된 시설 ID로 공용 시설 업그레이드 UI를 연다.</summary>
    public void OpenUpgradeUI()
    {
        if (m_uiManager == null)
        {
            Debug.LogWarning("[FacilityUpgradeButton] UIManager is not assigned.", this);
            return;
        }

        if (!CacheFacility())
        {
            Debug.LogWarning("[FacilityUpgradeButton] Facility source must implement IFacilityUpgradeable.", this);
            return;
        }

        m_uiManager.OpenFacilityUpgradeUI(m_facility.FacilityId);
    }

    private bool CacheFacility()
    {
        m_facility = m_facilitySource as IFacilityUpgradeable;
        return m_facility != null;
    }
}
