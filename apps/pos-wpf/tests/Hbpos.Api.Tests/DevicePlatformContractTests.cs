using Hbpos.Contracts.Devices;

namespace Hbpos.Api.Tests;

public sealed class DevicePlatformContractTests
{
    [Fact]
    public void Legacy_device_registration_constructors_keep_an_unspecified_platform()
    {
        var register = new DeviceRegisterRequest("1003", "HW-001", "Counter 2");
        var verify = new DeviceVerifyRequest("POS_1003_1011", "1003", "HW-001", "Counter 2");

        Assert.Null(register.DeviceSystem);
        Assert.Null(verify.DeviceSystem);
    }

    [Fact]
    public void Ipad_registration_and_verification_contracts_expose_ipados_platform()
    {
        var register = new DeviceRegisterRequest("1003", "HW-001", "Counter 2", "iPadOS");
        var verify = new DeviceVerifyRequest("POS_1003_1011", "1003", "HW-001", "Counter 2", "iPadOS");

        Assert.Equal("iPadOS", register.DeviceSystem);
        Assert.Equal("iPadOS", verify.DeviceSystem);
    }

    [Fact]
    public void Reregister_contract_does_not_allow_client_platform_override()
    {
        var constructor = typeof(DeviceReregisterRequest).GetConstructors().Single();

        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => string.Equals(parameter.Name, "deviceSystem", StringComparison.OrdinalIgnoreCase));
    }
}
