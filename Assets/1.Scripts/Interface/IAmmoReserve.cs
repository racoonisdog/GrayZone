/// <summary>
/// 총기에 예비 탄약을 공급하는 주체입니다.
/// </summary>
/// <remarks>
/// 예비 탄약의 주인은 총기가 아니라 그것을 든 캐릭터입니다. 총기를 바꿔도 들고 있던 탄은 남아야 하고,
/// 세이브에 들어가는 것도 캐릭터 쪽 값입니다. 그래서 총기는 예비량을 직접 들지 않고 이 인터페이스로 묻습니다.
/// </remarks>
public interface IAmmoReserve
{
    /// <summary>현재 남은 예비 탄약 수입니다.</summary>
    int ReserveAmmo { get; }

    /// <summary>
    /// 재장전에 쓸 탄약을 요청하고 실제로 받은 수를 돌려줍니다.
    /// </summary>
    /// <param name="requestedAmount">채우고 싶은 탄약 수입니다.</param>
    /// <returns>실제로 공급된 탄약 수입니다. 남은 양이 모자라면 요청보다 적습니다.</returns>
    /// <remarks>
    /// 확인과 차감을 한 번에 합니다. 나눠 두면 "확인했을 때는 있었는데 차감할 때는 없는" 틈이 생깁니다.
    /// </remarks>
    int ConsumeReserveAmmo(int requestedAmount);
}
