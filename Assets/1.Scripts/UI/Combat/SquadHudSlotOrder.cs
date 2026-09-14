using System.Collections.Generic;

/// <summary>
/// 전투 HUD가 "지금 조작 중인 대원"과 "팀 슬롯에 채울 대원 순서"를 결정하는 단일 규칙입니다.
/// </summary>
/// <remarks>
/// 이 규칙이 한 벌뿐이어야 하는 이유가 있습니다. 예전에는 <c>NormalHudPlayerStatusBinder</c>가
/// 체력 게이지를(조작 멤버 제외 + 신뢰도 정렬), <c>SquadStatusHudBinder</c>가 다운 필터와 구조 타이머를
/// (분대 인덱스 고정) 각자 따로 계산해서, 같은 SquadStatus 슬롯 안에서 게이지와 필터가 서로 다른
/// 대원을 가리킬 수 있었습니다. 조작 멤버가 1번이면 SquadStatus 1의 필터가 본인을 가리키기까지 했습니다.
/// 두 바인더 모두 여기를 거치게 해서 슬롯 주인을 하나로 만듭니다.
///
/// HUD 전용 표시 규칙이라 SquadManager가 아니라 UI 쪽에 둡니다. SquadManager는 분대 구성과
/// 조작 대상만 소유하고, "그걸 슬롯에 어떤 순서로 늘어놓을지"는 HUD의 몫입니다.
/// </remarks>
public static class SquadHudSlotOrder
{
    /// <summary>
    /// 지금 직접 조작 중인 대원을 찾습니다.
    /// </summary>
    /// <param name="sources">SquadManager가 분대 인덱스 순으로 들고 있는 데이터 목록입니다.</param>
    /// <returns>조작 중인 대원. 없으면 출격 가능한 첫 대원, 그것도 없으면 <c>null</c>입니다.</returns>
    public static PlayerbleUnitData ResolveControlled(IReadOnlyList<PlayerbleUnitData> sources)
    {
        if (sources == null)
        {
            return null;
        }

        for (int i = 0; i < sources.Count; i++)
        {
            PlayerbleUnitData data = sources[i];
            if (data != null && data.IsPlayerSquadMember)
            {
                return data;
            }
        }

        // 전환 도중이나 초기화 직후처럼 조작 플래그가 아직 아무에게도 서지 않은 프레임이 있습니다.
        // 그때 HUD를 비우지 않으려고 출격 가능한 첫 대원으로 대신 채웁니다.
        for (int i = 0; i < sources.Count; i++)
        {
            PlayerbleUnitData data = sources[i];
            if (data != null && data.CanDeploy)
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>
    /// 팀 슬롯(SquadStatus 1, 2 ...)에 채울 대원을 순서대로 <paramref name="buffer"/>에 담습니다.
    /// </summary>
    /// <param name="sources">SquadManager가 분대 인덱스 순으로 들고 있는 데이터 목록입니다.</param>
    /// <param name="controlled">조작 중인 대원입니다. 이 대원은 결과에서 빠집니다.</param>
    /// <param name="buffer">결과를 담을 목록입니다. 호출 시 비워집니다.</param>
    public static void BuildTeammateOrder(
        IReadOnlyList<PlayerbleUnitData> sources,
        PlayerbleUnitData controlled,
        List<PlayerbleUnitData> buffer)
    {
        if (buffer == null)
        {
            return;
        }

        buffer.Clear();

        if (sources == null)
        {
            return;
        }

        for (int i = 0; i < sources.Count; i++)
        {
            PlayerbleUnitData data = sources[i];
            if (data == null || data == controlled)
            {
                continue;
            }

            buffer.Add(data);
        }

        buffer.Sort(Compare);
    }

    /// <summary>
    /// <paramref name="slotOrdinal"/>번째 팀 슬롯이 표시할 대원을 반환합니다.
    /// </summary>
    /// <param name="teammates"><see cref="BuildTeammateOrder"/>가 채운 목록입니다.</param>
    /// <param name="slotOrdinal">슬롯 번호입니다. "SquadStatus 1"은 1로, 1부터 셉니다.</param>
    /// <returns>해당 슬롯의 대원. 슬롯 수가 대원 수보다 많으면 <c>null</c>입니다.</returns>
    public static PlayerbleUnitData ResolveSlot(IReadOnlyList<PlayerbleUnitData> teammates, int slotOrdinal)
    {
        int index = slotOrdinal - 1;
        return teammates != null && index >= 0 && index < teammates.Count
            ? teammates[index]
            : null;
    }

    /// <summary>신뢰도 오름차순, 같으면 RuntimeId 순으로 정렬합니다.</summary>
    /// <remarks>
    /// 2차 키로 RuntimeId를 쓰는 이유는 신뢰도가 같을 때 프레임마다 순서가 뒤바뀌지 않게 하기 위함입니다.
    /// 슬롯이 매 프레임 다시 계산되므로 정렬이 불안정하면 초상화가 깜빡입니다.
    /// </remarks>
    private static int Compare(PlayerbleUnitData left, PlayerbleUnitData right)
    {
        int reliabilityCompare = left.Reliability.CompareTo(right.Reliability);
        if (reliabilityCompare != 0)
        {
            return reliabilityCompare;
        }

        return string.CompareOrdinal(left.RuntimeId, right.RuntimeId);
    }
}
