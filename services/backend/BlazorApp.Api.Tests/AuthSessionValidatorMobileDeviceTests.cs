using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.MobileDeviceActivation;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class AuthSessionValidatorMobileDeviceTests
{
    private static readonly Guid BindingId = Guid.Parse("e2d61ff6-a86c-49f6-9ca9-edb7db784213");

    [Fact]
    public async Task MobileBoundAccountToken_ValidBinding_DoesNotRequireRefreshTokenSession()
    {
        const string userGuid = "mobile-bound-user";
        var bindingContext = new MobileDeviceBindingContext(
            BindingId,
            4,
            27,
            "mobile-hardware-001",
            userGuid);
        var activationService = new Mock<IMobileDeviceActivationService>(MockBehavior.Strict);
        activationService
            .Setup(service => service.ValidateTokenBindingAsync(
                bindingContext,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileDeviceBoundAccountValidationResult(
                true,
                userGuid,
                "mobile-user",
                ["StoreUser"]));
        var validator = new AuthSessionValidator(
            CreateUninitializedSqlSugarContext(),
            activationService.Object);

        var isActive = await validator.IsAccessSessionActiveAsync(
            userGuid,
            CreateMobilePrincipal(bindingContext));

        Assert.True(isActive);
        activationService.VerifyAll();
    }

    [Fact]
    public async Task MobileBoundAccountToken_MalformedBindingClaims_FailsClosed()
    {
        const string userGuid = "mobile-bound-user";
        var activationService = new Mock<IMobileDeviceActivationService>(MockBehavior.Strict);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("token_use", MobileDeviceAccountTokenIssuer.TokenUse),
                new Claim("userGuid", userGuid),
            ],
            "Bearer"));
        var validator = new AuthSessionValidator(
            CreateUninitializedSqlSugarContext(),
            activationService.Object);

        var isActive = await validator.IsAccessSessionActiveAsync(userGuid, principal);

        Assert.False(isActive);
        activationService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MobileBoundAccountToken_ValidationReturnsDifferentUser_FailsClosed()
    {
        const string userGuid = "mobile-bound-user";
        var bindingContext = new MobileDeviceBindingContext(
            BindingId,
            4,
            27,
            "mobile-hardware-001",
            userGuid);
        var activationService = new Mock<IMobileDeviceActivationService>(MockBehavior.Strict);
        activationService
            .Setup(service => service.ValidateTokenBindingAsync(
                bindingContext,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileDeviceBoundAccountValidationResult(
                true,
                "another-user",
                "mobile-user",
                ["StoreUser"]));
        var validator = new AuthSessionValidator(
            CreateUninitializedSqlSugarContext(),
            activationService.Object);

        var isActive = await validator.IsAccessSessionActiveAsync(
            userGuid,
            CreateMobilePrincipal(bindingContext));

        Assert.False(isActive);
        activationService.VerifyAll();
    }

    private static ClaimsPrincipal CreateMobilePrincipal(MobileDeviceBindingContext context)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("token_use", MobileDeviceAccountTokenIssuer.TokenUse),
                new Claim("userGuid", context.UserGuid),
                new Claim(
                    MobileDeviceAccountTokenIssuer.BindingIdClaim,
                    context.BindingId.ToString("N")),
                new Claim(
                    MobileDeviceAccountTokenIssuer.BindingVersionClaim,
                    context.BindingVersion.ToString()),
                new Claim(
                    MobileDeviceAccountTokenIssuer.DeviceRegistrationIdClaim,
                    context.DeviceRegistrationId.ToString()),
                new Claim(
                    MobileDeviceAccountTokenIssuer.HardwareIdClaim,
                    context.HardwareId),
            ],
            "Bearer"));
    }

    private static SqlSugarContext CreateUninitializedSqlSugarContext()
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, Mock.Of<ISqlSugarClient>());
        return context;
    }
}
