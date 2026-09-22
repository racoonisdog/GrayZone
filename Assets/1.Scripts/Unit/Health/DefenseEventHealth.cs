using System;

using UnityEngine;
using VInspector;

/// <summary>
/// 방어전에서 지켜야 하는 거점(정문 등)의 체력입니다. 파괴되면 패배로 끝냅니다.
/// </summary>
/// <remarks>
/// 체력·피해·UI 갱신과 HP 바 빌보드는 <see cref="HealthSystemBase"/>에서 그대로 물려받습니다.
/// 이 클래스가 더하는 것은 두 가지뿐입니다: 체력이 남은 비율에 따른 경고 문구, 그리고 파괴 시 패배 처리.
///
/// 패배는 스쿼드 전멸과 같은 경로(<see cref="FieldSceneDataManager.RequestGameOver"/>)를 씁니다.
/// 정산을 만들지 않고 게임오버 화면으로 가는 처리가 이미 그쪽에 있어, 같은 규칙을 두 벌 두지 않습니다.
/// </remarks>
public class DefenseEventHealth : HealthSystemBase
{
    /// <summary>방어 목표는 환경 레이어와 무관하게 플레이어 측 진영입니다. 명시적 Inspector 진영은 우선합니다.</summary>
    protected override Faction DefaultFaction => Faction.Player;

    /// <summary>체력 비율이 특정 값 아래로 내려갔을 때 한 번 띄울 경고입니다.</summary>
    [Serializable]
    public struct WarningStep
    {
        [Tooltip("이 비율(0~1) 아래로 내려가면 경고를 띄웁니다. 0.5면 절반 이하일 때입니다.")]
        [Range(0.0f, 1.0f)]
        public float threshold;

        [Tooltip("표시할 경고 문구입니다.")]
        public string message;
    }

    [Foldout("Defense Event")]
    [Tooltip("체력 비율별 경고입니다. 순서는 상관없습니다. 큰 비율부터 차례로 한 번씩만 발동합니다.")]
    [SerializeField]
    private WarningStep[] m_warningSteps =
    {
        new WarningStep { threshold = 0.5f, message = "정문이 손상되고 있습니다." },
        new WarningStep { threshold = 0.25f, message = "정문이 곧 무너집니다!" },
    };

    [Tooltip("파괴되어 패배할 때 표시할 문구입니다.")]
    [SerializeField] private string m_defeatMessage = "정문이 파괴되었습니다.";

    [Tooltip("파괴 시 패배 처리를 할지 여부입니다. 끄면 이벤트만 발생하고 게임오버를 요청하지 않습니다.")]
    [SerializeField] private bool m_requestGameOverOnDestroyed = true;

    /// <summary>이미 발동한 경고 단계입니다. 같은 경고가 반복해서 뜨지 않게 기록합니다.</summary>
    private bool[] m_warningFired;

    /// <summary>거점이 파괴되었을 때 발생합니다. 패배 처리와 별개로 연출을 붙일 수 있습니다.</summary>
    public event Action OnDestroyed;

    /// <summary>
    /// 경고가 발생했을 때 문구와 함께 발생합니다. 파괴 문구도 이 이벤트로 나갑니다.
    /// </summary>
    /// <remarks>
    /// 이 컴포넌트는 문구를 "언제 낼지"만 정하고 화면 표시는 하지 않습니다. 표시는 <see cref="DefenseObjectiveHud"/>가
    /// 맡습니다. 표시 규칙이 두 군데로 갈리면 한쪽만 고쳐지기 때문입니다.
    /// </remarks>
    public event Action<string> OnWarningRaised;

    public override void InitializeHealth()
    {
        base.InitializeHealth();

        m_warningFired = new bool[m_warningSteps != null ? m_warningSteps.Length : 0];
    }

    /// <summary>
    /// 피해를 입은 직후 체력 비율을 확인해 해당하는 경고를 띄웁니다.
    /// </summary>
    /// <remarks>
    /// 한 번의 큰 피해로 여러 단계를 한꺼번에 지나갈 수 있습니다. 그때는 지나친 단계를 모두 발동 처리하고
    /// 가장 낮은(가장 급한) 문구만 표시합니다. 경고를 연달아 덮어써 봐야 마지막 것만 보이기 때문입니다.
    /// </remarks>
    protected override void OnDamageApplied(int actualDamage, int previousHp)
    {
        base.OnDamageApplied(actualDamage, previousHp);

        if (m_isDead || m_warningSteps == null || m_warningSteps.Length == 0)
        {
            return;
        }

        EnsureWarningFiredSize();

        float ratio = m_maxHp > 0 ? (float)m_currentHp / m_maxHp : 0.0f;
        int chosen = -1;
        float chosenThreshold = float.MaxValue;

        for (int i = 0; i < m_warningSteps.Length; i++)
        {
            if (m_warningFired[i] || ratio > m_warningSteps[i].threshold)
            {
                continue;
            }

            m_warningFired[i] = true;

            if (m_warningSteps[i].threshold < chosenThreshold)
            {
                chosenThreshold = m_warningSteps[i].threshold;
                chosen = i;
            }
        }

        if (chosen >= 0)
        {
            ShowWarning(m_warningSteps[chosen].message);
        }
    }

    /// <summary>
    /// 체력이 0이 되면 패배로 끝냅니다.
    /// </summary>
    protected override void OnHpDepleted()
    {
        base.OnHpDepleted();

        ShowWarning(m_defeatMessage);
        OnDestroyed?.Invoke();

        if (!m_requestGameOverOnDestroyed)
        {
            return;
        }

        FieldSceneDataManager dataManager = FieldSceneDataManager.Instance != null
            ? FieldSceneDataManager.Instance
            : FindFirstObjectByType<FieldSceneDataManager>();

        if (dataManager == null)
        {
            Debug.LogWarning("[DefenseEventHealth] FieldSceneDataManager가 없어 패배를 요청하지 못했습니다.", this);
            return;
        }

        dataManager.RequestGameOver();
    }

    /// <summary>경고를 알립니다. 표시는 구독자(HUD)가 합니다.</summary>
    private void ShowWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        OnWarningRaised?.Invoke(message);
    }

    /// <summary>경고 단계 수가 인스펙터에서 바뀌었을 때 기록 배열 크기를 맞춥니다.</summary>
    private void EnsureWarningFiredSize()
    {
        int needed = m_warningSteps != null ? m_warningSteps.Length : 0;
        if (m_warningFired != null && m_warningFired.Length == needed)
        {
            return;
        }

        bool[] resized = new bool[needed];
        if (m_warningFired != null)
        {
            int copy = Mathf.Min(m_warningFired.Length, needed);
            Array.Copy(m_warningFired, resized, copy);
        }

        m_warningFired = resized;
    }
}
