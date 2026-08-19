using System.ComponentModel.DataAnnotations;
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

    [Fact]
    public void Registration_code_validation_metadata_is_attached_to_record_constructor_parameter()
    {
        var constructor = typeof(DeviceRegisterRequest).GetConstructors().Single();
        var provisioningCodeParameter = constructor.GetParameters().Single(parameter =>
            string.Equals(parameter.Name, "ProvisioningCode", StringComparison.OrdinalIgnoreCase));

        var validation = Assert.Single(
            provisioningCodeParameter.GetCustomAttributes(typeof(StringLengthAttribute), inherit: false)
                .Cast<StringLengthAttribute>());
        Assert.Equal(128, validation.MaximumLength);
        Assert.Equal(16, validation.MinimumLength);
        Assert.Empty(typeof(DeviceRegisterRequest).GetProperty(nameof(DeviceRegisterRequest.ProvisioningCode))!
            .GetCustomAttributes(typeof(StringLengthAttribute), inherit: false));
    }
}
