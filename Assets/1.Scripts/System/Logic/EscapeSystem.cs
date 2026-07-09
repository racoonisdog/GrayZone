using UnityEngine;

[DisallowMultipleComponent]
public class EscapeSystem : MonoBehaviour
{
    private const string ResultUIObjectName = "Result UI";

    [Header("Squad")]
    [Tooltip("Squad manager used to identify the currently controlled member. Auto-filled when empty.")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Result UI")]
    [Tooltip("Result UI root to enable on escape. Auto-finds the object named 'Result UI' when empty.")]
    [SerializeField] private GameObject m_resultUI;

    [Tooltip("Optional result UI controller. The root GameObject is enabled directly when this is empty.")]
    [SerializeField] private ResultUIController m_resultUIController;

    [Tooltip("Hide Result UI on Awake so it is not visible before escape.")]
    [SerializeField] private bool m_hideResultUIOnAwake = true;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        if (m_hideResultUIOnAwake)
        {
            SetResultUIActive(false);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryShowResultFor(collision != null ? collision.collider : null);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryShowResultFor(other);
    }

    private void AutoFindReferences()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_resultUI == null)
        {
            Transform resultRoot = FindTransformByName(ResultUIObjectName);
            if (resultRoot != null)
            {
                m_resultUI = resultRoot.gameObject;
            }
        }

        if (m_resultUIController == null && m_resultUI != null)
        {
            m_resultUIController = m_resultUI.GetComponent<ResultUIController>();
        }

        if (m_resultUI == null && m_resultUIController != null)
        {
            m_resultUI = m_resultUIController.gameObject;
        }
    }

    private void TryShowResultFor(Collider entrant)
    {
        if (!IsCurrentPlayerEntrant(entrant))
        {
            return;
        }

        ShowResultUI();
    }

    private bool IsCurrentPlayerEntrant(Collider entrant)
    {
        if (entrant == null)
        {
            return false;
        }

        SquadMemberController member = entrant.GetComponentInParent<SquadMemberController>();
        PlayerbleUnitData playerData = entrant.GetComponentInParent<PlayerbleUnitData>();

        if (member == null && playerData != null)
        {
            member = playerData.GetComponent<SquadMemberController>();
        }

        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_squadManager != null)
        {
            SquadMemberController currentMember = m_squadManager.CurrentMember;
            if (currentMember != null)
            {
                if (member == currentMember)
                {
                    return true;
                }

                PlayerbleUnitData currentMemberData = m_squadManager.CurrentPlayerData;
                return playerData != null && playerData == currentMemberData;
            }

            PlayerbleUnitData currentPlayerData = m_squadManager.CurrentPlayerData;
            if (currentPlayerData != null)
            {
                return playerData == currentPlayerData;
            }
        }

        if (member != null)
        {
            return member.IsPlayerControlled;
        }

        return playerData != null && playerData.IsPlayerControlled;
    }

    private void ShowResultUI()
    {
        AutoFindReferences();
        SetResultUIActive(true);
    }

    private void SetResultUIActive(bool active)
    {
        if (m_resultUIController != null)
        {
            m_resultUIController.gameObject.SetActive(active);
            return;
        }

        if (m_resultUI != null)
        {
            m_resultUI.SetActive(active);
        }
    }

    private static Transform FindTransformByName(string targetName)
    {
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current != null && current.name == targetName)
            {
                return current;
            }
        }

        return null;
    }
}
