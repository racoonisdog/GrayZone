using UnityEngine;

/// <summary>
/// 하나의 재화 타입과 수량으로 구성된 비용 항목
/// </summary>
public readonly struct CurrencyCost
{
    /// <summary>차감하거나 검사할 재화 타입</summary>
    public CurrencyType Type { get; }

    /// <summary>필요한 재화 수량</summary>
    public int Amount { get; }

    /// <summary>
    /// 재화 비용 항목을 생성
    /// </summary>
    /// <param name="type">비용으로 사용할 재화 타입</param>
    /// <param name="amount">필요 수량</param>
    public CurrencyCost(CurrencyType type, int amount)
    {
        Type = type;
        Amount = amount;
    }
}
