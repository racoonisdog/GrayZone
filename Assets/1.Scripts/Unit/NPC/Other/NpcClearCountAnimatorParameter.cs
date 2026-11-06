using UnityEngine;

/// <summary>
/// 방어전 클리어 횟수(<see cref="GameDataManager.DefenseClearCount"/>)를 Animator Int 파라미터로 넘깁니다.
/// 컨트롤러 쪽에서 이 값으로 상태 전이를 나눕니다(예: NPC03_Controller의 ClearCount &gt; 0 → AfterClear).
/// </summary>
/// <remarks>
/// 클리어 횟수는 귀환 정산·디버그 토글(F12)로만 바뀌므로 값이 달라질 때만 다시 넘깁니다.
/// 활성화 직후에는 기본 상태가 한 프레임 보이지 않도록 Animator를 즉시 갱신합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class NpcClearCountAnimatorParameter : MonoBehaviour
{
    [Tooltip("클리어 횟수를 넘길 Animator Int 파라미터 이름")]
    [SerializeField] private string m_parameterName = "ClearCount";

    private Animator m_animator;
    private int m_parameterHash;
    private bool m_hasParameter;
    private int m_appliedClearCount = -1;

    private void Awake()
    {
        m_animator = GetComponent<Animator>();
        m_parameterHash = Animator.StringToHash(m_parameterName);
        m_hasParameter = HasIntParameter(m_parameterHash);
        if (!m_hasParameter)
        {
            Debug.LogWarning($"[NpcClearCountAnimatorParameter] {name}: Animator에 Int 파라미터 '{m_parameterName}'가 없습니다.", this);
        }
    }

    private void OnEnable()
    {
        m_appliedClearCount = -1;
        if (Refresh())
        {
            // 기본 상태(BeforeClear)에서 바로 분기 상태로 넘어가도록 즉시 평가
            m_animator.Update(0f);
        }
    }

    private void Update()
    {
        Refresh();
    }

    private bool Refresh()
    {
        if (!m_hasParameter)
        {
            return false;
        }

        GameDataManager gameData = GameDataManager.Instance;
        int clearCount = gameData != null ? gameData.DefenseClearCount : 0;
        if (clearCount == m_appliedClearCount)
        {
            return false;
        }

        m_appliedClearCount = clearCount;
        m_animator.SetInteger(m_parameterHash, clearCount);
        return true;
    }

    private bool HasIntParameter(int hash)
    {
        if (m_animator.runtimeAnimatorController == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in m_animator.parameters)
        {
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Int)
            {
                return true;
            }
        }

        return false;
    }
}
