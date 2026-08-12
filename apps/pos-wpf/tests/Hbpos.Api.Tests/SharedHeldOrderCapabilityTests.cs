using Hbpos.Api.Services;
using Hbpos.Contracts.HeldOrders;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class SharedHeldOrderCapabilityTests
{
    [Fact]
    public void Capability_can_be_explicitly_disabled_with_frozen_response_fields()
    {
        var service = CreateService(enabled: false);

        var capabilities = service.GetCapabilities();

        Assert.False(capabilities.Enabled);
        Assert.Equal(1, capabilities.PayloadVersion);
        Assert.Equal(120, capabilities.PreparedTtlSeconds);
        Assert.True(capabilities.ForceReleaseSupported);
    }

    [Fact]
    public void Capability_turns_on_when_enabled_option_is_set()
    {
        var service = CreateService(enabled: true);

        var capabilities = service.GetCapabilities();

        Assert.True(capabilities.Enabled);
    }

    [Fact]
    public async Task Disabled_service_fails_closed_before_any_repository_write()
    {
        var repository = new SharedHeldOrderServiceTestSupport.FakeSharedHeldOrderRepository();
        var service = CreateService(enabled: false, repository);
        var identity = SharedHeldOrderServiceTestSupport.Identity();

        var exception = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            service.PublishAsync(
                SharedHeldOrderServiceTestSupport.PublishRequest(),
                identity,
                CancellationToken.None));

        Assert.Equal(SharedHeldOrderErrorCodes.Disabled, exception.Code);
        Assert.Empty(await repository.ListPendingAsync(identity.StoreCode, CancellationToken.None));
    }

    private static SharedHeldOrderService CreateService(
        bool enabled,
        SharedHeldOrderServiceTestSupport.FakeSharedHeldOrderRepository? repository = null)
    {
        return new SharedHeldOrderService(
            repository ?? new SharedHeldOrderServiceTestSupport.FakeSharedHeldOrderRepository(),
            new SharedHeldOrderServiceTestSupport.EphemeralPayloadProtector(),
            Options.Create(new SharedHeldOrderOptions { Enabled = enabled }),
            new SharedHeldOrderServiceTestSupport.ManualTimeProvider(
                SharedHeldOrderServiceTestSupport.InitialNow));
    }
}
