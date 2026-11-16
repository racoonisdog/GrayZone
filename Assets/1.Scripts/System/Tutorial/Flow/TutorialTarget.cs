using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 시퀀스가 ID로 가리킬 씬 오브젝트에 붙입니다(처치할 적, 상호작용할 대상 등).
/// </summary>
/// <remarks>
/// 시퀀스는 에셋이라 씬 오브젝트를 직접 넣을 수 없어서, 시퀀스에는 ID 문자열을 적고 씬 쪽에서 이 컴포넌트로 이어 줍니다.
/// 같은 ID가 여럿이면 먼저 켜진 쪽을 씁니다.
///
/// 상호작용 조건이 이 대상을 기다리는 동안에는 목표 외곽선(기본 파란색)을 그립니다. 플레이어가 조준해
/// 상호작용 대상으로 잡힌 동안에는 그리지 않습니다. 그때는 상호작용 HUD의 강조 외곽선이 대신 보입니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialTarget : MonoBehaviour
{
    private static readonly List<TutorialTarget> s_targets = new();

    private const string OutlineMaterialPath = "Outline/M_FocusOutline";
    private const string OutlineMaskMaterialPath = "Outline/M_FocusOutlineMask";
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    [Tooltip("시퀀스의 대상 ID 칸에 적을 이름입니다.")]
    [SerializeField] private string m_id;

    [Header("목표 외곽선")]
    [Tooltip("상호작용 조건이 이 대상을 기다리는 동안, 조준하지 않았을 때 그릴 외곽선 색입니다.")]
    [SerializeField] private Color m_objectiveOutlineColor = new Color(0.2f, 0.6f, 1.0f, 1.0f);

    [Tooltip("목표 외곽선 굵기입니다. 상호작용 HUD의 강조 외곽선과 같은 단위입니다.")]
    [SerializeField] private float m_objectiveOutlineWidth = 3.0f;

    /// <summary>이 대상을 기다리는 상호작용 조건 수입니다. 0보다 크면 목표 외곽선을 그립니다.</summary>
    private int m_objectiveCount;

    private FocusOutlineRenderer m_outline;
    private Material m_outlineInstance;

    /// <summary>같은 오브젝트에 붙은 함정입니다. 처음 볼 때 한 번 찾습니다.</summary>
    private Trap m_trap;
    private bool m_trapResolved;

    /// <summary>대상 ID입니다.</summary>
    public string Id => m_id;

    /// <summary>상호작용 조건이 지금 이 대상을 기다리고 있는지 여부입니다.</summary>
    public bool IsObjective => m_objectiveCount > 0;

    /// <summary>ID가 같은 대상을 찾습니다. 켜져 있는 대상을 먼저 찾고, 없으면 꺼진 대상도 찾습니다. 없으면 null입니다.</summary>
    /// <remarks>
    /// 트리거는 조건 감시를 이벤트보다 먼저 시작하므로, 같은 트리거의 이벤트가 켜 줄 청사진은 이 시점에 아직 꺼져 있습니다.
    /// 그래서 꺼진 대상도 찾습니다.
    /// </remarks>
    public static TutorialTarget Find(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        for (int i = 0; i < s_targets.Count; i++)
        {
            if (s_targets[i] != null && s_targets[i].m_id == id)
            {
                return s_targets[i];
            }
        }

        foreach (TutorialTarget target in FindObjectsByType<TutorialTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (target.m_id == id)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>상호작용 조건이 이 대상을 기다리기 시작할 때 부릅니다. 목표 외곽선을 켭니다.</summary>
    /// <remarks>조건마다 한 번씩 부르고, 끝날 때 <see cref="ReleaseObjective"/>를 같은 횟수만큼 불러야 합니다.</remarks>
    public void AcquireObjective()
    {
        m_objectiveCount++;
    }

    /// <summary>상호작용 조건이 이 대상을 더 기다리지 않을 때 부릅니다. 기다리는 조건이 없으면 목표 외곽선을 끕니다.</summary>
    public void ReleaseObjective()
    {
        m_objectiveCount = Mathf.Max(0, m_objectiveCount - 1);
    }

    private void OnEnable()
    {
        s_targets.Add(this);
    }

    private void OnDisable()
    {
        s_targets.Remove(this);
    }

    private void OnDestroy()
    {
        if (m_outlineInstance != null)
        {
            Destroy(m_outlineInstance);
        }
    }

    private void LateUpdate()
    {
        if ((m_objectiveCount <= 0 && !IsPendingTrapGoal()) || IsFocusedByPlayer())
        {
            return;
        }

        if (m_outline == null && !CreateOutline())
        {
            return;
        }

        m_outlineInstance.SetColor(OutlineColorId, m_objectiveOutlineColor);
        m_outlineInstance.SetFloat(OutlineWidthId, m_objectiveOutlineWidth);
        m_outline.Draw(gameObject);
    }

    /// <summary>
    /// 튜토리얼 진행 중에 이 대상이 할 일이 남은 함정인지 여부입니다. 설치할 수 있는 청사진이거나, 설치됐지만 내구도가 깎여 수리할 함정입니다.
    /// </summary>
    /// <remarks>
    /// 대상 ID를 비운 상호작용 조건("아무 함정이나 설치")은 대상을 몰라 외곽선을 켜 주지 못합니다. 그래서 함정은 조건과 별개로 자기 상태를 보고 켭니다.
    /// 스크립트를 꺼 둔 함정(외형만 보이는 상태)과 꺼진 청사진은 켜지지 않습니다.
    /// </remarks>
    private bool IsPendingTrapGoal()
    {
        if (!TutorialFlowDirector.IsAnyRunning)
        {
            return false;
        }

        if (!m_trapResolved)
        {
            m_trap = GetComponent<Trap>();
            m_trapResolved = true;
        }

        return m_trap != null && m_trap.isActiveAndEnabled
            && (m_trap.IsBuildable || (m_trap.IsBuilt && m_trap.Health < m_trap.MaxHealth));
    }

    /// <summary>조작 중인 캐릭터가 이 대상을 상호작용 대상으로 잡고 있는지 여부입니다.</summary>
    private bool IsFocusedByPlayer()
    {
        SquadMemberController player = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        if (player == null || !player.TryGetComponent(out InteractionController interaction))
        {
            return false;
        }

        return interaction.Current is Component current && current != null && current.GetComponentInParent<TutorialTarget>() == this;
    }

    private bool CreateOutline()
    {
        Material outline = Resources.Load<Material>(OutlineMaterialPath);
        Material mask = Resources.Load<Material>(OutlineMaskMaterialPath);
        if (outline == null || mask == null)
        {
            return false;
        }

        m_outlineInstance = new Material(outline);
        m_outline = new FocusOutlineRenderer(mask, m_outlineInstance);
        return true;
    }
}
