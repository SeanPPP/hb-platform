using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Client.Tests;

public sealed class SharedHeldOrderPublicationHostedServiceTests
{
    [Fact]
    public async Task RunOnceIfAuthorizedAsync_uses_matching_device_and_cashier_scope()
    {
        var worker = new RecordingWorker();
        var authorization = new DeviceAuthorizationState();
        authorization.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-1", "device-token"));
        var cashier = new CashierSessionContext();
        cashier.SetCurrent(Cashier("S001", "POS-01"));
        var service = new SharedHeldOrderPublicationHostedService(worker, authorization, cashier);

        var ran = await service.RunOnceIfAuthorizedAsync(CancellationToken.None);

        Assert.True(ran);
        Assert.Equal(("S001", "POS-01"), Assert.Single(worker.Calls));
    }

    [Fact]
    public async Task RunOnceIfAuthorizedAsync_skips_missing_or_mismatched_scope()
    {
        var worker = new RecordingWorker();
        var authorization = new DeviceAuthorizationState();
        var cashier = new CashierSessionContext();
        var service = new SharedHeldOrderPublicationHostedService(worker, authorization, cashier);

        Assert.False(await service.RunOnceIfAuthorizedAsync(CancellationToken.None));

        authorization.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-1", "device-token"));
        cashier.SetCurrent(Cashier("S002", "POS-01"));
        Assert.False(await service.RunOnceIfAuthorizedAsync(CancellationToken.None));

        cashier.SetCurrent(Cashier("S001", "POS-02"));
        Assert.False(await service.RunOnceIfAuthorizedAsync(CancellationToken.None));
        Assert.Empty(worker.Calls);
    }

    private static CashierSessionDto Cashier(string storeCode, string deviceCode)
    {
        return new CashierSessionDto(
            "cashier-1",
            "user-1",
            "Cashier One",
            storeCode,
            deviceCode,
            [],
            [],
            [storeCode],
            IsSuperAdmin: false,
            IsOfflineCached: false,
            IsEmergencyOverride: false,
            AuthorizationToken: "cashier-token",
            AuthorizationExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));
    }

    private sealed class RecordingWorker : ISharedHeldOrderPublicationWorker
    {
        public List<(string StoreCode, string DeviceCode)> Calls { get; } = [];

        public Task<SharedHeldOrderPublicationRunResult> RunOnceAsync(
            string storeCode,
            string? deviceCode = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((storeCode, deviceCode ?? string.Empty));
            return Task.FromResult(new SharedHeldOrderPublicationRunResult(0, 0, 0, 0, 0, 0));
        }
    }
}
