using System;
using System.Collections.Generic;

/// <summary>
/// 스쿼드가 공유하는 전투/비전투 상태입니다.
/// </summary>
/// <remarks>
/// 공용 문서 `스쿼드 AI 시스템` §4.2가 요구하는 두 규칙(전투 진입 조건, 전투 유지 조건)은
/// 사실 하나의 파생값으로 접힙니다. 플레이어가 직접 인식되든 적을 공격하든 하울링이 퍼지든,
/// 그 결과로 해당 변이체가 교전에 진입하기 때문입니다. 그래서 별도의 전환 트리거를 두지 않고
/// **스쿼드를 대상으로 교전 중인 변이체가 한 마리라도 있는가**만 봅니다.
///
/// <para>
/// <b>데이터는 한 방향으로만 흐릅니다</b>: 변이체 개체별 교전 상태 -> 스쿼드 전투 상태.
/// 역방향으로 참조하면 안 됩니다. 공용 문서 `적 시스템` §5.6이 명시하듯
/// "보호 적용 여부는 스쿼드 전체의 전투 상태가 아니라 각 변이체의 교전 상태를 기준으로 판단"하며,
/// "스쿼드 전체가 교전 중이더라도 아직 교전에 진입하지 않은 주변 변이체까지 자동으로 교전에 참여시키지 않는다".
/// 이 값을 변이체의 감지 판정에 연결하면 비전투 감지 보호가 통째로 무너집니다.
/// </para>
///
/// <para>
/// 순회 대신 등록/해제(push)를 쓰는 이유는 진입·이탈 지점이 이미
/// <see cref="EnemyTargetSensor.SetEngaged"/> 한 쌍으로 존재하기 때문입니다.
/// 매 틱 변이체를 세면 개체 수에 비례하는 비용이 붙는데, 실측(2026-08-06)에서
/// 변이체 50기 전투 시 이미 `EnemyController.Update()`가 1.185ms로 가장 비쌌습니다.
/// </para>
///
/// <para>
/// MonoBehaviour가 아니라 일반 클래스입니다. <see cref="SquadManager"/>가 소유하므로
/// 씬 배선이 필요 없고 컴포넌트 수도 늘지 않습니다.
/// </para>
/// </remarks>
public class SquadEngagement
{
    // 등록 주체를 EnemyController가 아니라 센서로 잡은 이유: SetEngaged를 부르는 쪽이 센서이고,
    // 센서가 교전 상태와 대상 정보를 함께 들고 있어 §7 적 정보 공유에서도 같은 핸들을 씁니다.
    private readonly HashSet<EnemyTargetSensor> m_engagedEnemies = new HashSet<EnemyTargetSensor>();

    /// <summary>스쿼드가 전투 상태인지 여부입니다.</summary>
    /// <remarks>스쿼드를 대상으로 교전 중인 변이체가 하나라도 있으면 true입니다(§4.2).</remarks>
    public bool IsInCombat => m_engagedEnemies.Count > 0;

    /// <summary>현재 스쿼드와 교전 중인 변이체 수입니다.</summary>
    public int EngagedEnemyCount => m_engagedEnemies.Count;

    /// <summary>전투 상태가 바뀔 때 발생합니다. 인자는 전환 후 상태입니다.</summary>
    /// <remarks>비전투 -> 전투, 전투 -> 비전투 두 경계에서만 발생하며 매 등록마다 발생하지 않습니다.</remarks>
    public event Action<bool> OnCombatStateChanged;

    // 교전 적을 EnemyController로 훑을 수 있게 별도로 들고 갑니다(§8.1 "교전에 참여한 적만 등록").
    // 센서에서 매번 컴포넌트를 되찾으면 갱신 주기마다 GetComponent 비용이 붙습니다.
    private readonly List<EnemyController> m_engagedControllers = new List<EnemyController>();

    /// <summary>현재 스쿼드와 교전 중인 적 목록입니다.</summary>
    /// <remarks>
    /// 적 정보 공유(§8)의 등록 후보가 정확히 이 목록입니다. 순서는 보장하지 않습니다.
    /// 파괴된 항목은 등록·해제 시점에 정리되지만, 읽는 쪽도 null 검사를 하는 편이 안전합니다.
    /// </remarks>
    public IReadOnlyList<EnemyController> EngagedEnemies => m_engagedControllers;

    /// <summary>지정한 적이 지금 스쿼드와 교전 중인지 확인합니다.</summary>
    /// <param name="enemy">확인할 적입니다.</param>
    /// <returns>교전 중이면 true입니다.</returns>
    public bool IsEngaged(EnemyController enemy)
    {
        if (enemy == null)
        {
            return false;
        }

        for (int i = 0; i < m_engagedControllers.Count; i++)
        {
            if (m_engagedControllers[i] == enemy)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 변이체가 스쿼드와의 교전에 진입했음을 등록합니다.
    /// </summary>
    /// <param name="sensor">교전에 진입한 변이체의 대상 센서입니다.</param>
    /// <remarks>같은 대상을 여러 번 등록해도 안전합니다.</remarks>
    public void RegisterEngagedEnemy(EnemyTargetSensor sensor)
    {
        if (sensor == null)
        {
            return;
        }

        bool wasInCombat = IsInCombat;

        PruneDestroyed();
        m_engagedEnemies.Add(sensor);
        RebuildControllerList();

        NotifyIfChanged(wasInCombat);
    }

    /// <summary>
    /// 변이체가 스쿼드와의 교전에서 빠졌음을 등록 해제합니다.
    /// </summary>
    /// <param name="sensor">교전에서 빠진 변이체의 대상 센서입니다.</param>
    /// <remarks>등록되지 않은 대상을 해제해도 안전합니다.</remarks>
    public void UnregisterEngagedEnemy(EnemyTargetSensor sensor)
    {
        if (sensor == null)
        {
            // 파괴된 대상이 null로 들어와도 남은 파괴 항목은 정리해 카운트를 맞춥니다.
            bool wasInCombatOnNull = IsInCombat;
            PruneDestroyed();
            RebuildControllerList();
            NotifyIfChanged(wasInCombatOnNull);
            return;
        }

        bool wasInCombat = IsInCombat;

        m_engagedEnemies.Remove(sensor);
        PruneDestroyed();
        RebuildControllerList();

        NotifyIfChanged(wasInCombat);
    }

    /// <summary>
    /// 등록된 교전 변이체를 모두 비웁니다.
    /// </summary>
    /// <remarks>씬 정리나 스쿼드 재편성처럼 교전 관계를 전부 버려야 할 때 씁니다.</remarks>
    public void Clear()
    {
        bool wasInCombat = IsInCombat;

        m_engagedEnemies.Clear();
        m_engagedControllers.Clear();

        NotifyIfChanged(wasInCombat);
    }

    /// <summary>
    /// 센서 집합에서 적 컨트롤러 목록을 다시 만듭니다.
    /// </summary>
    /// <remarks>
    /// 등록·해제 시점에만 부릅니다. 교전 진입·이탈은 드물게 일어나므로 매 프레임 비용이 되지 않고,
    /// 대신 정보 공유 쪽이 갱신마다 <c>GetComponent</c>를 부르지 않아도 됩니다.
    /// </remarks>
    private void RebuildControllerList()
    {
        m_engagedControllers.Clear();

        foreach (EnemyTargetSensor sensor in m_engagedEnemies)
        {
            if (sensor == null)
            {
                continue;
            }

            EnemyController controller = sensor.GetComponent<EnemyController>();
            if (controller != null)
            {
                m_engagedControllers.Add(controller);
            }
        }
    }

    /// <summary>
    /// 파괴된 변이체가 남아 카운트를 부풀리지 않도록 정리합니다.
    /// </summary>
    /// <remarks>
    /// 정상 경로는 <see cref="EnemyTargetSensor"/>의 OnDisable 해제입니다. 이것은 그 경로가
    /// 빠졌을 때를 대비한 방어이며, 교전 진입·이탈은 드물게 일어나므로 그 시점에만 훑습니다.
    /// </remarks>
    private void PruneDestroyed()
    {
        m_engagedEnemies.RemoveWhere(static s => s == null);
    }

    /// <summary>전투 상태 경계를 넘었으면 구독자에게 알립니다.</summary>
    /// <param name="wasInCombat">변경 전 전투 상태입니다.</param>
    private void NotifyIfChanged(bool wasInCombat)
    {
        bool isInCombat = IsInCombat;
        if (wasInCombat == isInCombat)
        {
            return;
        }

        OnCombatStateChanged?.Invoke(isInCombat);
    }
}
