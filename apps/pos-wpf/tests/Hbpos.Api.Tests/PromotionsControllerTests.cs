using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Promotions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class PromotionsControllerTests
{
    [Fact]
    public void RulesEndpoint_KeepExpectedRoute()
    {
        Assert.Equal("rules", GetHttpGetTemplate(nameof(PromotionsController.GetRules)));
    }

    [Fact]
    public void PromotionsController_RequiresAuthorization()
    {
        Assert.NotNull(typeof(PromotionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .SingleOrDefault());
    }

    [Fact]
    public async Task GetRules_ReturnsBadRequestWhenStoreCodeMissing()
    {
        var controller = new PromotionsController(new FakePromotionRuleService());

        var result = await controller.GetRules(" ", null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var apiResult = Assert.IsType<ApiResult<PromotionRulesResponse>>(badRequest.Value);
        Assert.False(apiResult.Success);
        Assert.Equal("STORE_CODE_REQUIRED", apiResult.ErrorCode);
    }

    [Fact]
    public async Task GetRules_ReturnsForbiddenWhenDeviceStoreDoesNotMatch()
    {
        var controller = new PromotionsController(new FakePromotionRuleService());
        SetAuthenticatedDevice(controller, storeCode: "S02", deviceCode: "POS-02");

        var result = await controller.GetRules("S01", null, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var apiResult = Assert.IsType<ApiResult<PromotionRulesResponse>>(forbidden.Value);
        Assert.False(apiResult.Success);
        Assert.Equal("DEVICE_SCOPE_FORBIDDEN", apiResult.ErrorCode);
    }

    [Fact]
    public async Task GetRules_ReturnsWrappedServiceResponse()
    {
        var expected = new PromotionRulesResponse(
            "S01",
            DateTimeOffset.Parse("2026-06-13T00:00:00Z"),
            [
                new PromotionRuleDto(
                    "PROMO-001",
                    "Tea Bundle",
                    DateTime.Parse("2026-06-01T00:00:00Z"),
                    DateTime.Parse("2026-06-30T23:59:59Z"),
                    true,
                    10,
                    2,
                    8.5m,
                    1,
                    [new PromotionRuleProductDto("SKU-001", 2)])
            ]);
        var service = new FakePromotionRuleService { Response = expected };
        var controller = new PromotionsController(service);

        var asOf = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
        var result = await controller.GetRules("S01", asOf, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var apiResult = Assert.IsType<ApiResult<PromotionRulesResponse>>(ok.Value);
        Assert.True(apiResult.Success);
        Assert.Same(expected, apiResult.Data);
        Assert.Equal(("S01", asOf), service.LastRequest);
    }

    private static string? GetHttpGetTemplate(string methodName)
    {
        return typeof(PromotionsController)
            .GetMethod(methodName)?
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .Cast<HttpGetAttribute>()
            .Single()
            .Template;
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

    private sealed class FakePromotionRuleService : IPromotionRuleService
    {
        public PromotionRulesResponse Response { get; set; } = new("S01", DateTimeOffset.UnixEpoch, []);

        public (string StoreCode, DateTimeOffset? AsOf)? LastRequest { get; private set; }

        public Task<PromotionRulesResponse> GetRulesAsync(
            string storeCode,
            DateTimeOffset? asOf,
            CancellationToken cancellationToken)
        {
            LastRequest = (storeCode, asOf);
            return Task.FromResult(Response);
        }
    }
}
