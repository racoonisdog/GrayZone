using System;
using UnityEngine;

/// <summary>자원 종류와 현재 보유량을 함께 보관하는 GameDataManager 런타임 값 객체입니다.</summary>
[Serializable]
public sealed class ResourceAmountState
{
    [Tooltip("현재 값이 나타내는 안정적인 자원 ID입니다.")]
    [SerializeField] private string resourceId = string.Empty;
    [Tooltip("현재 자원 보유량입니다. 0 이상으로 유지됩니다.")]
    [Min(0)][SerializeField] private int amount;

    /// <summary>자원 종류입니다.</summary>
    public string ResourceId => ResourceIds.Normalize(resourceId);

    /// <summary>0 이상으로 보정된 현재 보유량입니다.</summary>
    public int Amount => Mathf.Max(0, amount);

    /// <summary>Unity 직렬화를 위한 빈 자원 값을 생성합니다.</summary>
    public ResourceAmountState()
    {
    }

    /// <summary>자원 종류와 보유량을 지정해 값 객체를 생성합니다.</summary>
    public ResourceAmountState(string resourceId, int amount)
    {
        this.resourceId = ResourceIds.Normalize(resourceId);
        this.amount = Mathf.Max(0, amount);
    }

    /// <summary>현재 자원 값을 독립된 객체로 복제합니다.</summary>
    public ResourceAmountState Clone()
    {
        return new ResourceAmountState(ResourceId, Amount);
    }

    /// <summary>현재 보유량을 0 이상으로 보정해 설정합니다.</summary>
    public void SetAmount(int value)
    {
        amount = Mathf.Max(0, value);
    }
}
