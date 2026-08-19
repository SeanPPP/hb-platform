using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

internal sealed class NonReviewDeviceAuthorizationBoundary : IPosIpadAppReviewAuthorizationBoundary
{
    public Task<bool> IsReviewDeviceAsync(
        string storeCode,
        string deviceCode,
        string hardwareId,
        CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> IsActiveEmployeeCashierAsync(
        string cashierId,
        string userGuid,
        CancellationToken cancellationToken) => Task.FromResult(false);
}
