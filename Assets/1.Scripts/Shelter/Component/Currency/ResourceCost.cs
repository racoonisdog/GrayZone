/// <summary>
/// 하나의 자원 ID와 수량으로 구성된 비용 항목입니다.
/// </summary>
public readonly struct ResourceCost
{
    public string ResourceId { get; }

    public int Amount { get; }

    public bool IsValid => !string.IsNullOrEmpty(ResourceId) && Amount > 0;

    public ResourceCost(string resourceId, int amount)
    {
        ResourceId = ResourceIds.Normalize(resourceId);
        Amount = amount;
    }
}
