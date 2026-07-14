using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 현재 PlayerSquadMember의 탈출 지점 진입을 판정하고 배틀 결과 확정 및 결과 UI 표시를 시작합니다.
/// </summary>
[DisallowMultipleComponent]
public class EscapeSystem : MonoBehaviour
{
    private const string ResultUIObjectName = "Result UI";

    [Header("Squad")]
    [Tooltip("현재 PlayerSquadMember를 식별하는 스쿼드 매니저입니다. 비어 있으면 자동 탐색합니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Tooltip("배틀 씬 런타임 데이터와 귀환 정산 결과를 관리하는 씬 전용 데이터 매니저입니다. 비어 있으면 자동 탐색합니다.")]
    [FormerlySerializedAs("m_battleManager")]
    [SerializeField] private BattleSceneDataManager m_battleSceneDataManager;

    [Header("Result UI")]
    [Tooltip("Result UI root to enable on escape. Auto-finds the object named 'Result UI' when empty.")]
    [SerializeField] private GameObject m_resultUI;

    [Tooltip("Optional result UI controller. The root GameObject is enabled directly when this is empty.")]
    [SerializeField] private ResultUIController m_resultUIController;

    [Tooltip("Hide Result UI on Awake so it is not visible before escape.")]
    [SerializeField] private bool m_hideResultUIOnAwake = true;

    /// <summary>Inspector에서 컴포넌트를 추가하거나 Reset할 때 필요한 씬 참조를 자동 탐색합니다.</summary>
    private void Reset()
    {
        AutoFindReferences();
    }

    /// <summary>런타임 참조를 확보하고 설정에 따라 결과 UI를 초기 비활성화합니다.</summary>
    private void Awake()
    {
        AutoFindReferences();
        if (m_hideResultUIOnAwake)
        {
            SetResultUIActive(false);
        }
    }

    /// <summary>일반 충돌로 탈출 지점에 들어온 Collider를 귀환 대상으로 판정합니다.</summary>
    private void OnCollisionEnter(Collision collision)
    {
        TryShowResultFor(collision != null ? collision.collider : null);
    }

    /// <summary>Trigger 충돌로 탈출 지점에 들어온 Collider를 귀환 대상으로 판정합니다.</summary>
    private void OnTriggerEnter(Collider other)
    {
        TryShowResultFor(other);
    }

    /// <summary>스쿼드·배틀 데이터·결과 UI 참조 중 비어 있는 항목을 현재 씬에서 자동 탐색합니다.</summary>
    private void AutoFindReferences()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_battleSceneDataManager == null)
        {
            m_battleSceneDataManager = FindFirstObjectByType<BattleSceneDataManager>();
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

    /// <summary>진입자가 현재 PlayerSquadMember이면 배틀 결과 확정과 결과 UI 표시를 시작합니다.</summary>
    private void TryShowResultFor(Collider entrant)
    {
        if (!IsCurrentPlayerEntrant(entrant))
        {
            return;
        }

        ShowResultUI();
    }

    /// <summary>지정한 Collider가 현재 플레이어가 직접 조작 중인 스쿼드원인지 확인합니다.</summary>
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
            SquadMemberController playerSquadMember = m_squadManager.PlayerSquadMember;
            if (playerSquadMember != null)
            {
                if (member == playerSquadMember)
                {
                    return true;
                }

                PlayerbleUnitData mappedPlayerSquadMemberData = m_squadManager.PlayerSquadMemberData;
                return playerData != null && playerData == mappedPlayerSquadMemberData;
            }

            PlayerbleUnitData playerSquadMemberData = m_squadManager.PlayerSquadMemberData;
            if (playerSquadMemberData != null)
            {
                return playerData == playerSquadMemberData;
            }
        }

        if (member != null)
        {
            return member.IsPlayerSquadMember;
        }

        return playerData != null && playerData.IsPlayerSquadMember;
    }

    /// <summary>배틀을 탈출 사유로 최종 확정한 뒤 기존 Result UI에 정산 값을 표시합니다.</summary>
    private void ShowResultUI()
    {
        AutoFindReferences();

        if (m_battleSceneDataManager != null)
        {
            m_battleSceneDataManager.FinalizeBattle(BattleEndReason.Escaped);
        }

        if (m_resultUIController != null && m_battleSceneDataManager != null)
        {
            m_resultUIController.ShowResult(m_battleSceneDataManager.CaptureResult());
            return;
        }

        SetResultUIActive(true);
    }

    /// <summary>연결된 결과 UI 루트 또는 컨트롤러의 활성 상태를 변경합니다.</summary>
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

    /// <summary>비활성 오브젝트를 포함한 현재 씬 계층에서 지정한 이름의 Transform을 찾습니다.</summary>
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
