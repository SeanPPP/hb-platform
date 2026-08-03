using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Installments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class InstallmentsControllerTests
{
    [Fact]
    public async Task Details_allows_installment_from_another_device_in_the_same_store()
    {
        var installmentGuid = Guid.NewGuid();
        var controller = new InstallmentsController(
            null!,
            new FakeInstallmentHistoryService(CreateDetails(
                installmentGuid,
                storeCode: "S01",
                deviceCode: "POS-02")));
        SetAuthenticatedDevice(controller, storeCode: "S01", deviceCode: "POS-01");

        var result = await controller.Details(installmentGuid, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResult<InstallmentDetailsDto?>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal(installmentGuid, envelope.Data?.InstallmentGuid);
    }

    [Fact]
    public async Task Details_rejects_installment_from_another_store()
    {
        var installmentGuid = Guid.NewGuid();
        var controller = new InstallmentsController(
            null!,
            new FakeInstallmentHistoryService(CreateDetails(
                installmentGuid,
                storeCode: "S02",
                deviceCode: "POS-02")));
        SetAuthenticatedDevice(controller, storeCode: "S01", deviceCode: "POS-01");

        var result = await controller.Details(installmentGuid, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var envelope = Assert.IsType<ApiResult<InstallmentDetailsDto?>>(forbidden.Value);
        Assert.Equal("DEVICE_SCOPE_FORBIDDEN", envelope.ErrorCode);
    }

    private static InstallmentDetailsDto CreateDetails(
        Guid installmentGuid,
        string storeCode,
        string deviceCode)
    {
        return new InstallmentDetailsDto(
            installmentGuid,
            "INS-001",
            storeCode,
            deviceCode,
            "CASHIER-1",
            "Cashier One",
            "Customer One",
            "0400000000",
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            100m,
            20m,
            20m,
            20m,
            80m,
            InstallmentStatus.Active,
            [],
            [],
            null);
    }

    private static void SetAuthenticatedDevice(
        ControllerBase controller,
        string storeCode,
        string deviceCode)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(DeviceAuthConstants.StoreCodeClaim, storeCode),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, deviceCode),
            new Claim(DeviceAuthConstants.HardwareIdClaim, "HW-001")
        ], DeviceAuthConstants.Scheme);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private sealed class FakeInstallmentHistoryService(InstallmentDetailsDto? details)
        : IInstallmentHistoryService
    {
        public Task<InstallmentHistoryQueryResponse> QueryAsync(
            InstallmentHistoryQueryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new InstallmentHistoryQueryResponse([]));
        }

        public Task<InstallmentDetailsDto?> GetDetailsAsync(
            Guid installmentGuid,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(details);
        }
    }
}
