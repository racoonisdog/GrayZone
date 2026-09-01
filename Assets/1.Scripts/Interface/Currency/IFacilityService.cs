using UnityEngine;

/// <summary>
/// 자원을 소모해 한 번 실행되는 셸터 서비스가 구현하는 계약입니다.
/// </summary>
/// <remarks>
/// "얼마인지 묻기(<see cref="Cost"/>) → 가능한지 묻기(<see cref="CanExecute"/>) → 실행하기(<see cref="TryExecute"/>)"의
/// 세 단계로 나뉩니다. 확인과 실행이 분리되어 있어야 UI가 버튼을 미리 비활성화할 수 있고,
/// 실행 시점에 자원이 모자라도 부분 차감된 상태가 남지 않습니다.
/// </remarks>
public interface IFacilityService
{
    /// <summary>이 서비스를 한 번 실행하는 데 드는 자원 묶음입니다.</summary>
    CostBundle Cost { get; }

    /// <summary>지금 실행할 수 있는지 확인합니다. 자원을 차감하지 않습니다.</summary>
    /// <param name="storage">비용을 낼 자원 보관소입니다.</param>
    /// <returns>실행 가능하면 <c>true</c>입니다.</returns>
    bool CanExecute(ResourceStorage storage);

    /// <summary>자원을 차감하고 서비스를 실행합니다.</summary>
    /// <param name="storage">비용을 낼 자원 보관소입니다.</param>
    /// <returns>실행에 성공했으면 <c>true</c>입니다. 실패하면 자원은 그대로입니다.</returns>
    bool TryExecute(ResourceStorage storage);
}
