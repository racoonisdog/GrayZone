using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>
/// 한 엔티티가 함께 쓰는 통합 SO를 하위 컴포넌트들에 나눠 주는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 같은 SO를 컴포넌트마다 Inspector에서 반복해 꽂던 것을 한 자리로 모읍니다.
/// 프리팹과 씬 양쪽에 같은 참조가 여러 벌 생기면 한쪽만 갈아 끼운 상태를 눈치채기 어렵습니다.
/// <para>
/// 밸런스와 피드백을 한 컴포넌트가 함께 다룹니다. 둘은 담는 값만 다를 뿐 주입 규칙이 같아서,
/// 컴포넌트를 나누면 프리팹마다 바인더가 둘씩 붙고 배선 자리도 둘로 늘어납니다.
/// </para>
/// <para>
/// 주입 대상은 <see cref="ISharedBalanceReceiver"/> 또는 <see cref="ISharedFeedbackReceiver"/>를 구현한
/// 컴포넌트뿐이며, 그중 개별 SO를 이미 물고 있는 것은 건너뜁니다.
/// 우선순위는 프리팹 값 &lt; 통합 SO &lt; 개별 SO입니다.
/// </para>
/// <para>
/// <b>실행 순서에 의존하지 않습니다.</b> 대상 컴포넌트는 자기 Awake에서 개별 SO가 없으면 아무것도 하지 않고,
/// 실제 대입은 여기서 부르는 주입 메서드 한 번뿐입니다.
/// 그래서 이 컴포넌트의 Awake가 대상보다 먼저 돌든 나중에 돌든 결과가 같습니다.
/// </para>
/// <para>
/// 엔티티 단위로 붙입니다. 총기 프리팹처럼 독립적으로 스폰되는 것은 캐릭터의 일부가 아니라
/// 그 자체가 하나의 엔티티이므로, 자기 프리팹 루트에 이 컴포넌트를 따로 답니다.
/// </para>
/// </remarks>
public sealed class SOBinder : MonoBehaviour
{
    [Header("Shared SO (개별 SO가 없는 컴포넌트에만 주입)")]
    [Tooltip("이 엔티티의 컴포넌트들이 함께 쓸 통합 밸런스 SO입니다. 비우면 각 컴포넌트가 자기 SO나 프리팹 값을 그대로 씁니다.")]
    [SerializeField] private ScriptableObject m_sharedBalance;

    [Tooltip("이 엔티티의 컴포넌트들이 함께 쓸 통합 피드백 SO입니다. 비우면 각 컴포넌트가 자기 SO나 프리팹 값을 그대로 씁니다.")]
    [SerializeField] private ScriptableObject m_sharedFeedback;

    [Header("Options")]
    [Tooltip("자식 오브젝트의 컴포넌트까지 대상으로 삼을지 여부입니다. 하위에 모듈이 나뉘어 붙는 구성이면 켜 둡니다.")]
    [SerializeField] private bool m_includeChildren = true;

    [Foldout("Debug")]
    [Tooltip("주입 결과를 콘솔에 요약해 남깁니다. 배선 확인용이며 평소에는 꺼 둡니다.")]
    [SerializeField] private bool m_logResult;

    /// <summary>현재 지정된 통합 밸런스 SO입니다.</summary>
    public ScriptableObject SharedBalance => m_sharedBalance;

    /// <summary>현재 지정된 통합 피드백 SO입니다.</summary>
    public ScriptableObject SharedFeedback => m_sharedFeedback;

    /// <summary>지정된 통합 SO들을 대상 컴포넌트들에 주입합니다.</summary>
    private void Awake()
    {
        ApplyShared();
    }

    /// <summary>현재 지정된 통합 SO들을 다시 주입합니다.</summary>
    /// <returns>실제로 주입한 컴포넌트 수입니다. 밸런스와 피드백을 합산합니다.</returns>
    public int ApplyShared()
    {
        int bound = ApplySharedBalance(m_sharedBalance);
        bound += ApplySharedFeedback(m_sharedFeedback);
        return bound;
    }

    /// <summary>
    /// 지정한 통합 밸런스 SO를 대상 컴포넌트들에 주입하고, 이후 기준으로 삼습니다.
    /// </summary>
    /// <param name="balance">주입할 통합 밸런스 SO입니다.</param>
    /// <returns>실제로 주입한 컴포넌트 수입니다.</returns>
    public int ApplySharedBalance(ScriptableObject balance)
    {
        m_sharedBalance = balance;

        if (balance == null)
        {
            return 0;
        }

        // 시트로 나갈 수 있는 순수 밸런스 SO인지 먼저 확인합니다.
        // 피드백 SO를 잘못 꽂으면 대입은 되지만 의도와 다른 데이터가 들어갑니다.
        if (!TryValidateSource<IBalanceTableData>(balance, "통합 밸런스"))
        {
            return 0;
        }

        int bound = 0;
        int skipped = 0;

        foreach (ISharedBalanceReceiver receiver in CollectReceivers<ISharedBalanceReceiver>())
        {
            if (receiver.HasOwnBalance)
            {
                // 개별 SO가 더 강합니다. 통합을 덮어쓰면 Bind가 두 번 돌아 후처리도 두 번 돕니다.
                skipped++;
                continue;
            }

            receiver.BindSharedBalance(balance);
            bound++;
        }

        ReportResult("밸런스", balance, bound, skipped);
        return bound;
    }

    /// <summary>
    /// 지정한 통합 피드백 SO를 대상 컴포넌트들에 주입하고, 이후 기준으로 삼습니다.
    /// </summary>
    /// <param name="feedback">주입할 통합 피드백 SO입니다.</param>
    /// <returns>실제로 주입한 컴포넌트 수입니다.</returns>
    public int ApplySharedFeedback(ScriptableObject feedback)
    {
        m_sharedFeedback = feedback;

        if (feedback == null)
        {
            return 0;
        }

        if (!TryValidateSource<IFeedbackData>(feedback, "통합 피드백"))
        {
            return 0;
        }

        int bound = 0;
        int skipped = 0;

        foreach (ISharedFeedbackReceiver receiver in CollectReceivers<ISharedFeedbackReceiver>())
        {
            if (receiver.HasOwnFeedback)
            {
                skipped++;
                continue;
            }

            receiver.BindSharedFeedback(feedback);
            bound++;
        }

        ReportResult("피드백", feedback, bound, skipped);
        return bound;
    }

    /// <summary>꽂힌 SO가 그 슬롯이 기대하는 종류인지 확인합니다.</summary>
    /// <typeparam name="TMarker">슬롯이 요구하는 데이터 표시 인터페이스입니다.</typeparam>
    /// <param name="source">검사할 SO입니다.</param>
    /// <param name="slotLabel">경고에 표시할 슬롯 이름입니다.</param>
    /// <returns>종류가 맞으면 <c>true</c>입니다.</returns>
    private bool TryValidateSource<TMarker>(ScriptableObject source, string slotLabel)
    {
        if (source is TMarker)
        {
            return true;
        }

        Debug.LogWarning(
            $"[SOBinder] '{source.name}'은 {typeof(TMarker).Name} 구현이 아닙니다. " +
            $"{slotLabel} SO 자리에 맞는 종류만 꽂아주세요.",
            this);
        return false;
    }

    /// <summary>주입 결과를 상황에 맞게 보고합니다.</summary>
    /// <param name="kindLabel">밸런스인지 피드백인지 나타내는 이름입니다.</param>
    /// <param name="source">이번에 주입한 SO입니다.</param>
    /// <param name="bound">실제로 주입한 수입니다.</param>
    /// <param name="skipped">개별 SO 때문에 건너뛴 수입니다.</param>
    /// <remarks>대상이 하나도 없는 것은 배선 실수일 가능성이 높아 옵션과 무관하게 경고합니다.</remarks>
    private void ReportResult(string kindLabel, ScriptableObject source, int bound, int skipped)
    {
        if (bound == 0 && skipped == 0)
        {
            Debug.LogWarning(
                $"[SOBinder] '{name}' 아래에 통합 {kindLabel}를 받을 컴포넌트가 없습니다. " +
                $"수신 인터페이스 구현 여부와 {nameof(m_includeChildren)} 설정을 확인하세요.",
                this);
            return;
        }

        if (m_logResult)
        {
            Debug.Log(
                $"[SOBinder] {kindLabel} '{source.name}' 주입 {bound}개, 개별 SO로 건너뜀 {skipped}개.",
                this);
        }
    }

    /// <summary>주입 대상이 될 수 있는 컴포넌트를 모읍니다.</summary>
    /// <typeparam name="TReceiver">찾을 수신 인터페이스입니다.</typeparam>
    /// <returns>이 엔티티의 범위 안에 있는 대상 목록입니다.</returns>
    /// <remarks>
    /// 비활성 오브젝트까지 훑습니다. 스폰 직후 꺼져 있다가 나중에 켜지는 구성이 있기 때문입니다.
    /// </remarks>
    private List<TReceiver> CollectReceivers<TReceiver>()
    {
        List<TReceiver> receivers = new List<TReceiver>();
        CollectFrom(transform, receivers, true);
        return receivers;
    }

    /// <summary>한 노드와 그 아래를 훑되 다른 엔티티의 영역에서 멈춥니다.</summary>
    /// <typeparam name="TReceiver">찾을 수신 인터페이스입니다.</typeparam>
    /// <param name="node">검사할 노드입니다.</param>
    /// <param name="receivers">찾은 대상을 담을 목록입니다.</param>
    /// <param name="isRoot">이 바인더 자신이 붙은 노드인지 여부입니다.</param>
    /// <remarks>
    /// <b>다른 <see cref="SOBinder"/>를 만나면 그 아래로는 내려가지 않습니다.</b>
    /// 총기처럼 독립적으로 스폰되어 캐릭터에 부착되는 프리팹은 캐릭터의 일부가 아니라 그 자체가 하나의 엔티티이고,
    /// 자기 밸런스·피드백 SO도 자기 바인더가 씁니다. 경계를 두지 않으면 캐릭터 쪽 통합 SO가
    /// 손에 들린 총기의 필드까지 덮어써서, 총기 프리팹의 배선이 조용히 무시됩니다.
    /// <para>
    /// 즉 이 바인더의 범위는 "자기 노드부터 다음 바인더를 만나기 전까지"입니다.
    /// </para>
    /// </remarks>
    private void CollectFrom<TReceiver>(Transform node, List<TReceiver> receivers, bool isRoot)
    {
        if (!isRoot && node.GetComponent<SOBinder>() != null)
        {
            return;
        }

        foreach (Component candidate in node.GetComponents<Component>())
        {
            if (candidate is TReceiver receiver)
            {
                receivers.Add(receiver);
            }
        }

        if (!m_includeChildren)
        {
            return;
        }

        for (int i = 0; i < node.childCount; i++)
        {
            CollectFrom(node.GetChild(i), receivers, false);
        }
    }
}
