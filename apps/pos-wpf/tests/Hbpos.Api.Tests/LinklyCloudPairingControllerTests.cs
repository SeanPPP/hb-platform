using System.Security.Claims;
using System.Text.Json;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Linkly;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudPairingControllerTests
{
    [Fact]
    public void Pair_endpoint_requires_authenticated_payment_settings_policy()
    {
        var method = typeof(LinklyController).GetMethod(nameof(LinklyController.PairCloudBackend));
        var controllerAuthorize = typeof(LinklyController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();
        var post = method?
            .GetCustomAttributes(typeof(HttpPostAttribute), inherit: false)
            .Cast<HttpPostAttribute>()
            .SingleOrDefault();
        var actionAuthorize = method?
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(controllerAuthorize);
        Assert.Equal("cloud-backend/pair", post?.Template);
        Assert.Equal(CashierAuthorizationPolicies.PaymentSettings, actionAuthorize?.Policy);
    }

    [Fact]
    public async Task PairCloudBackend_uses_store_and_device_claims_only()
    {
        var pairing = new CapturingLinklyCloudPairingService();
        var controller = CreateController(
            pairing,
            new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01"));

        var result = await controller.PairCloudBackend(
            new LinklyCloudBackendPairRequest("Sandbox", "123456"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var apiResult = Assert.IsType<ApiResult<LinklyCloudBackendTerminalCredentialResponse>>(response.Value);
        Assert.True(apiResult.Success);
        Assert.Equal(1, pairing.Calls);
        Assert.Equal("S01", pairing.StoreCode);
        Assert.Equal("POS-01", pairing.DeviceCode);
        Assert.Equal("Sandbox", pairing.Request?.Environment);
        Assert.Equal("123456", pairing.Request?.PairCode);
        Assert.Equal("device:POS-01", pairing.UpdatedBy);
        Assert.DoesNotContain("\"secret\"", JsonSerializer.Serialize(apiResult), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PairCloudBackend_returns_403_when_device_scope_claims_are_missing()
    {
        var pairing = new CapturingLinklyCloudPairingService();
        var controller = CreateController(pairing);

        var result = await controller.PairCloudBackend(
            new LinklyCloudBackendPairRequest("Sandbox", "123456"),
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(0, pairing.Calls);
    }

    [Fact]
    public async Task PairCloudBackend_returns_400_for_null_body()
    {
        var pairing = new CapturingLinklyCloudPairingService();
        var controller = CreateController(
            pairing,
            new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01"));

        var result = await controller.PairCloudBackend(null!, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var apiResult = Assert.IsType<ApiResult<LinklyCloudBackendTerminalCredentialResponse>>(badRequest.Value);
        Assert.Equal("LINKLY_CLOUD_BACKEND_PAIR_REQUEST_INVALID", apiResult.ErrorCode);
        Assert.Equal(0, pairing.Calls);
    }

    [Theory]
    [MemberData(nameof(PairingErrorCases))]
    public async Task PairCloudBackend_maps_pairing_errors_to_stable_http_results(
        Exception exception,
        int expectedStatusCode,
        string expectedErrorCode)
    {
        var pairing = new CapturingLinklyCloudPairingService(exception);
        var controller = CreateController(
            pairing,
            new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01"));

        var result = await controller.PairCloudBackend(
            new LinklyCloudBackendPairRequest("Sandbox", "123456"),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        var apiResult = Assert.IsType<ApiResult<LinklyCloudBackendTerminalCredentialResponse>>(objectResult.Value);
        Assert.False(apiResult.Success);
        Assert.Equal(expectedErrorCode, apiResult.ErrorCode);
    }

    public static IEnumerable<object[]> PairingErrorCases()
    {
        yield return [
            new LinklyCloudPairingValidationException("pairCode must contain exactly 6 digits."),
            StatusCodes.Status400BadRequest,
            "LINKLY_CLOUD_BACKEND_PAIR_REQUEST_INVALID"];
        yield return [
            new LinklyCloudPairingCredentialMissingException(),
            StatusCodes.Status409Conflict,
            "LINKLY_CLOUD_BACKEND_PAIR_CREDENTIAL_MISSING"];
        yield return [
            new LinklyCloudPairingInProgressException(),
            StatusCodes.Status409Conflict,
            "LINKLY_CLOUD_BACKEND_PAIR_IN_PROGRESS"];
        yield return [
            new LinklyCloudPairingRejectedException(),
            StatusCodes.Status422UnprocessableEntity,
            "LINKLY_CLOUD_BACKEND_PAIR_REJECTED"];
        yield return [
            new LinklyCloudPairingUpstreamException(),
            StatusCodes.Status502BadGateway,
            "LINKLY_CLOUD_BACKEND_PAIR_UPSTREAM_FAILED"];
        yield return [
            new LinklyCloudPairingTimeoutException(),
            StatusCodes.Status504GatewayTimeout,
            "LINKLY_CLOUD_BACKEND_PAIR_TIMEOUT"];
        yield return [
            new LinklyCloudPairingPersistenceException(),
            StatusCodes.Status500InternalServerError,
            "LINKLY_CLOUD_BACKEND_PAIR_PERSISTENCE_FAILED"];
        yield return [
            new LinklyCloudPairingPreparationException(),
            StatusCodes.Status500InternalServerError,
            "LINKLY_CLOUD_BACKEND_PAIR_PREPARATION_FAILED"];
    }

    [Fact]
    public void Pair_endpoint_documents_all_stable_http_outcomes()
    {
        var method = typeof(LinklyController).GetMethod(nameof(LinklyController.PairCloudBackend));
        var statusCodes = method!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false)
            .Cast<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Order()
            .ToArray();

        Assert.Equal(
            new[] { 200, 400, 409, 422, 500, 502, 504 },
            statusCodes);
    }

    [Fact]
    public void Pair_request_exposes_only_environment_and_pair_code()
    {
        Assert.Equal(
            new[] { nameof(LinklyCloudBackendPairRequest.Environment), nameof(LinklyCloudBackendPairRequest.PairCode) },
            typeof(LinklyCloudBackendPairRequest).GetProperties().Select(property => property.Name));
    }

    private static LinklyController CreateController(
        ILinklyCloudPairingService pairing,
        params Claim[] claims)
    {
        var controller = new LinklyController(null!, null!, pairing)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "pairing-test"))
                }
            }
        };
        return controller;
    }
}
