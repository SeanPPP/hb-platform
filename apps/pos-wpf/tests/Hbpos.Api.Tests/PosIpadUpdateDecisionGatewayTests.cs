using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class PosIpadUpdateDecisionGatewayTests
{
    [Fact]
    public async Task ActivatorUtilities_prefers_production_constructor_and_uses_central_policy()
    {
        var gateway = new RecordingGateway
        {
            NativeDecision = new PosIpadNativeUpdateDecision(
                "required",
                "12",
                "1.4.0",
                "1.5.0",
                "https://apps.apple.com/au/app/example/id123",
                "必须升级")
        };
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<PosIpadOptions>>(
            Options.Create(new PosIpadOptions()));
        services.AddSingleton<IOptions<AppUpdateOptions>>(
            Options.Create(new AppUpdateOptions { CentralPolicyEnabled = true }));
        services.AddSingleton<IPosIpadUpdateDecisionGateway>(gateway);
        using var provider = services.BuildServiceProvider();

        var factory = ActivatorUtilities.CreateFactory(
            typeof(PosIpadAppUpdateController),
            Type.EmptyTypes);
        var controller = Assert.IsType<PosIpadAppUpdateController>(factory(provider, null));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [
                            new(DeviceAuthConstants.StoreCodeClaim, "0247"),
                            new(
                                DeviceAuthConstants.DeviceSystemClaim,
                                DeviceSystems.IpadOs)
                        ],
                        DeviceAuthConstants.Scheme))
            }
        };

        var response = GetOk(
            await controller.Check("1.3.0", "7", "1.3.0", CancellationToken.None));

        Assert.Equal("0247", gateway.NativeStoreCode);
        Assert.True(response.ForceUpdate);
        Assert.Equal("1.5.0", response.LatestVersion);
    }

    [Fact]
    public async Task Native_check_defaults_to_legacy_policy_without_calling_central_gateway()
    {
        var gateway = new RecordingGateway
        {
            NativeDecision = new PosIpadNativeUpdateDecision(
                "required",
                "12",
                "2.0.0",
                "2.1.0",
                "https://apps.apple.com/au/app/example/id123",
                "中央策略")
        };
        var controller = CreateController(
            gateway,
            new PosIpadOptions
            {
                MinimumSupportedVersion = "1.5.0",
                LatestVersion = "1.6.0",
                AppStoreUrl = "https://apps.apple.com/au/app/example/id123"
            });

        var response = GetOk(
            await controller.Check("1.4.0", "3", "1.4.0", CancellationToken.None));

        Assert.Null(gateway.NativeStoreCode);
        Assert.True(response.ForceUpdate);
        Assert.Equal("1.6.0", response.LatestVersion);
    }

    [Fact]
    public async Task Native_check_does_not_allow_client_query_to_enable_central_policy()
    {
        var gateway = new RecordingGateway
        {
            NativeDecision = new PosIpadNativeUpdateDecision(
                "none",
                "12",
                null,
                null,
                null,
                null)
        };
        var controller = CreateController(
            gateway,
            new PosIpadOptions
            {
                MinimumSupportedVersion = "1.5.0",
                LatestVersion = "1.6.0"
            });
        controller.HttpContext.Request.QueryString = new QueryString("?centralPolicyEnabled=true");

        var response = GetOk(
            await controller.Check("1.4.0", "3", "1.4.0", CancellationToken.None));

        Assert.Null(gateway.NativeStoreCode);
        Assert.True(response.ForceUpdate);
        Assert.Equal("1.6.0", response.LatestVersion);
    }

    [Fact]
    public async Task Native_check_uses_authenticated_store_claim_and_maps_central_required_decision()
    {
        var gateway = new RecordingGateway
        {
            NativeDecision = new PosIpadNativeUpdateDecision(
                "required",
                "12",
                "1.4.0",
                "1.5.0",
                "https://apps.apple.com/au/app/example/id123",
                "必须升级")
        };
        var controller = CreateController(
            gateway,
            appUpdateOptions: new AppUpdateOptions { CentralPolicyEnabled = true });

        var response = GetOk(
            await controller.Check("1.3.0", "7", "1.3.0", CancellationToken.None));

        Assert.Equal("0247", gateway.NativeStoreCode);
        Assert.True(response.Enabled);
        Assert.True(response.ForceUpdate);
        Assert.Equal("1.4.0", response.MinimumSupportedVersion);
        Assert.Equal("1.5.0", response.LatestVersion);
    }

    [Fact]
    public async Task Native_check_treats_central_none_as_authoritative_when_migration_is_enabled()
    {
        var gateway = new RecordingGateway
        {
            NativeDecision = new PosIpadNativeUpdateDecision(
                "none",
                "12",
                null,
                null,
                null,
                null)
        };
        var controller = CreateController(
            gateway,
            new PosIpadOptions
            {
                MinimumSupportedVersion = "1.5.0",
                LatestVersion = "1.6.0"
            },
            new AppUpdateOptions { CentralPolicyEnabled = true });

        var response = GetOk(
            await controller.Check("1.4.0", "3", "1.4.0", CancellationToken.None));

        Assert.Equal("0247", gateway.NativeStoreCode);
        Assert.False(response.ForceUpdate);
        Assert.Null(response.MinimumSupportedVersion);
        Assert.Null(response.LatestVersion);
        Assert.Null(response.AppStoreUrl);
    }

    [Fact]
    public async Task Native_check_falls_back_to_legacy_configuration_when_central_is_unavailable()
    {
        var controller = CreateController(
            new RecordingGateway(),
            new PosIpadOptions
            {
                MinimumSupportedVersion = "1.5.0",
                LatestVersion = "1.6.0",
                AppStoreUrl = "https://apps.apple.com/au/app/example/id123"
            },
            new AppUpdateOptions { CentralPolicyEnabled = true });

        var response = GetOk(
            await controller.Check("1.4.0", "3", "1.4.0", CancellationToken.None));

        Assert.True(response.ForceUpdate);
        Assert.Equal("1.6.0", response.LatestVersion);
    }

    [Fact]
    public async Task Ota_check_exposes_separate_route_and_never_accepts_store_from_query()
    {
        var gateway = new RecordingGateway
        {
            OtaDecision = new PosIpadOtaUpdateResponse(
                "optional",
                "8",
                "pos-ipad-production",
                "1.2.0",
                "8fb126b2-0b64-4833-8dd8-5237d313d51c",
                "28a93806-6fd0-4ad5-931d-97c63648d50a",
                "分店灰度")
        };
        var controller = CreateController(gateway);

        var response = GetOk(
            await controller.CheckOta(
                "1.2.0",
                "current-update",
                "current-group",
                CancellationToken.None));

        Assert.Equal("0247", gateway.OtaStoreCode);
        Assert.Equal("optional", response.State);
        Assert.Equal("pos-ipad-production", response.Channel);
    }

    [Fact]
    public async Task Ota_check_returns_service_unavailable_instead_of_false_none_decision()
    {
        var controller = CreateController(
            new RecordingGateway(),
            new PosIpadOptions
            {
                MinimumSupportedVersion = "1.5.0",
                LatestVersion = "1.6.0",
                ForceUpdate = true
            });

        var result = await controller.CheckOta("1.2.0", null, null, CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        var envelope = Assert.IsType<ApiResult<PosIpadOtaUpdateResponse>>(unavailable.Value);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task Http_gateway_sends_service_token_and_unwraps_standard_api_envelope()
    {
        string? body = null;
        AuthenticationHeaderValue? authorization = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            authorization = request.Headers.Authorization;
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"success":true,"data":{"state":"none","policyVersion":"none","latestVersion":null,"minimumSupportedVersion":null,"appStoreUrl":null,"releaseMessage":null}}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var gateway = new HttpPosIpadUpdateDecisionGateway(
            new HttpClient(handler),
            Options.Create(new AppUpdateOptions
            {
                CenterBaseUrl = "https://center.example/base/",
                ServiceApiToken = " hbsvc_example "
            }),
            NullLogger<HttpPosIpadUpdateDecisionGateway>.Instance);

        var decision = await gateway.GetNativeDecisionAsync(
            new PosIpadNativeUpdateDecisionRequest("0247", "1.0.0", "7"),
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("none", decision.State);
        Assert.Equal("Bearer", authorization?.Scheme);
        Assert.Equal("hbsvc_example", authorization?.Parameter);
        Assert.Contains("\"storeCode\":\"0247\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_gateway_only_reads_the_narrow_decision_token_environment_variable()
    {
        var requestedNames = new List<string>();

        var token = HttpPosIpadUpdateDecisionGateway.ResolveServiceToken(
            new AppUpdateOptions(),
            name =>
            {
                requestedNames.Add(name);
                return name == "HBPOS_APP_UPDATE_DECISION_READ_TOKEN"
                    ? " hbsvc_reader "
                    : "hbsvc_publisher";
            });

        Assert.Equal("hbsvc_reader", token);
        Assert.Equal(
            ["HBPOS_APP_UPDATE_DECISION_READ_TOKEN"],
            requestedNames);
    }

    [Fact]
    public void Http_gateway_does_not_fall_back_when_the_narrow_reader_token_is_invalid()
    {
        var requestedNames = new List<string>();

        var token = HttpPosIpadUpdateDecisionGateway.ResolveServiceToken(
            new AppUpdateOptions(),
            name =>
            {
                requestedNames.Add(name);
                return "publisher-token";
            });

        Assert.Null(token);
        Assert.Equal(
            ["HBPOS_APP_UPDATE_DECISION_READ_TOKEN"],
            requestedNames);
    }

    [Fact]
    public async Task Http_gateway_rejects_public_http_before_sending_service_token()
    {
        var requestCount = 0;
        var gateway = new HttpPosIpadUpdateDecisionGateway(
            new HttpClient(new StubHttpMessageHandler(_ =>
            {
                requestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })),
            Options.Create(new AppUpdateOptions
            {
                CenterBaseUrl = "http://center.example/",
                ServiceApiToken = "hbsvc_example"
            }),
            NullLogger<HttpPosIpadUpdateDecisionGateway>.Instance);

        var decision = await gateway.GetNativeDecisionAsync(
            new PosIpadNativeUpdateDecisionRequest("0247", "1.0.0", "7"),
            CancellationToken.None);

        Assert.Null(decision);
        Assert.Equal(0, requestCount);
    }

    [Theory]
    [InlineData("https://center.example/", "not-a-service-token")]
    [InlineData("https://user:password@center.example/", "hbsvc_example")]
    public async Task Http_gateway_rejects_wrong_token_type_or_url_credentials_before_sending(
        string centerBaseUrl,
        string serviceApiToken)
    {
        var requestCount = 0;
        var gateway = new HttpPosIpadUpdateDecisionGateway(
            new HttpClient(new StubHttpMessageHandler(_ =>
            {
                requestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })),
            Options.Create(new AppUpdateOptions
            {
                CenterBaseUrl = centerBaseUrl,
                ServiceApiToken = serviceApiToken
            }),
            NullLogger<HttpPosIpadUpdateDecisionGateway>.Instance);

        var decision = await gateway.GetNativeDecisionAsync(
            new PosIpadNativeUpdateDecisionRequest("0247", "1.0.0", "7"),
            CancellationToken.None);

        Assert.Null(decision);
        Assert.Equal(0, requestCount);
    }

    [Theory]
    [InlineData("http://localhost:5002/")]
    [InlineData("http://127.0.0.1:5002/")]
    [InlineData("http://[::1]:5002/")]
    public async Task Http_gateway_allows_loopback_http_for_local_development(string centerBaseUrl)
    {
        var gateway = new HttpPosIpadUpdateDecisionGateway(
            new HttpClient(new StubHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"success":true,"data":{"state":"none","policyVersion":"none","latestVersion":null,"minimumSupportedVersion":null,"appStoreUrl":null,"releaseMessage":null}}
                        """,
                        Encoding.UTF8,
                        "application/json")
                }))),
            Options.Create(new AppUpdateOptions
            {
                CenterBaseUrl = centerBaseUrl,
                ServiceApiToken = "hbsvc_example"
            }),
            NullLogger<HttpPosIpadUpdateDecisionGateway>.Instance);

        var decision = await gateway.GetNativeDecisionAsync(
            new PosIpadNativeUpdateDecisionRequest("0247", "1.0.0", "7"),
            CancellationToken.None);

        Assert.NotNull(decision);
    }

    [Fact]
    public async Task Http_gateway_accepts_required_decision_with_verified_itunes_store_url()
    {
        var gateway = new HttpPosIpadUpdateDecisionGateway(
            new HttpClient(new StubHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"success":true,"data":{"state":"required","policyVersion":"17","latestVersion":"1.5.0","minimumSupportedVersion":"1.4.0","appStoreUrl":"https://itunes.apple.com/au/app/example/id123","releaseMessage":"必须升级"}}
                        """,
                        Encoding.UTF8,
                        "application/json")
                }))),
            Options.Create(new AppUpdateOptions
            {
                CenterBaseUrl = "https://center.example/",
                ServiceApiToken = "hbsvc_example"
            }),
            NullLogger<HttpPosIpadUpdateDecisionGateway>.Instance);

        var decision = await gateway.GetNativeDecisionAsync(
            new PosIpadNativeUpdateDecisionRequest("0247", "1.3.0", "7"),
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("required", decision.State);
        Assert.Equal("https://itunes.apple.com/au/app/example/id123", decision.AppStoreUrl);
    }

    [Fact]
    public async Task Http_gateway_accepts_four_part_effective_versions_without_changing_shape()
    {
        var gateway = CreateHttpGateway(
            """
            {"success":true,"data":{"state":"required","policyVersion":"18","latestVersion":"1.5.0.88","minimumSupportedVersion":"1.5.0.42","appStoreUrl":"https://apps.apple.com/au/app/example/id123","releaseMessage":"同版本构建升级"}}
            """);

        var decision = await gateway.GetNativeDecisionAsync(
            new PosIpadNativeUpdateDecisionRequest("0247", "1.5.0", "41"),
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("required", decision.State);
        Assert.Equal("1.5.0.42", decision.MinimumSupportedVersion);
        Assert.Equal("1.5.0.88", decision.LatestVersion);
    }

    [Fact]
    public async Task Http_gateway_rejects_native_decision_with_inexact_shape_or_state_contract()
    {
        string[] invalidResponses =
        [
            """
            {"success":true,"data":{"state":"none","policyVersion":"none","latestVersion":null,"minimumSupportedVersion":null,"appStoreUrl":null}}
            """,
            """
            {"success":true,"data":{"state":"none","policyVersion":"none","latestVersion":null,"minimumSupportedVersion":null,"appStoreUrl":null,"releaseMessage":null,"forceUpdate":true}}
            """,
            """
            {"success":true,"data":{"state":"none","policyVersion":"none","latestVersion":"1.5.0","minimumSupportedVersion":null,"appStoreUrl":null,"releaseMessage":null}}
            """,
            """
            {"success":true,"data":{"state":"none","policyVersion":"12","latestVersion":null,"minimumSupportedVersion":null,"appStoreUrl":null,"releaseMessage":null}}
            """,
            """
            {"success":true,"data":{"state":"optional","policyVersion":"none","latestVersion":"1.5.0","minimumSupportedVersion":null,"appStoreUrl":"https://apps.apple.com/au/app/example/id123","releaseMessage":null}}
            """,
            """
            {"success":true,"data":{"state":"required","policyVersion":"12","latestVersion":"1.5.0","minimumSupportedVersion":null,"appStoreUrl":"https://apps.apple.com/au/app/example/id123","releaseMessage":null}}
            """
        ];

        foreach (var responseJson in invalidResponses)
        {
            var gateway = CreateHttpGateway(responseJson);

            var decision = await gateway.GetNativeDecisionAsync(
                new PosIpadNativeUpdateDecisionRequest("0247", "1.3.0", "7"),
                CancellationToken.None);

            Assert.Null(decision);
        }
    }

    [Fact]
    public async Task Http_gateway_rejects_ota_decision_with_inexact_shape_or_state_contract()
    {
        string[] invalidResponses =
        [
            """
            {"success":true,"data":{"state":"none","policyVersion":"none","channel":null,"runtimeVersion":null,"iosUpdateId":null,"updateGroupId":null}}
            """,
            """
            {"success":true,"data":{"state":"none","policyVersion":"none","channel":null,"runtimeVersion":null,"iosUpdateId":null,"updateGroupId":null,"releaseMessage":null,"forceUpdate":true}}
            """,
            """
            {"success":true,"data":{"state":"none","policyVersion":"none","channel":"pos-ipad-release-1","runtimeVersion":null,"iosUpdateId":null,"updateGroupId":null,"releaseMessage":null}}
            """,
            """
            {"success":true,"data":{"state":"none","policyVersion":"4","channel":null,"runtimeVersion":null,"iosUpdateId":null,"updateGroupId":null,"releaseMessage":null}}
            """,
            """
            {"success":true,"data":{"state":"optional","policyVersion":"none","channel":"pos-ipad-release-1","runtimeVersion":"1.2.0","iosUpdateId":"8fb126b2-0b64-4833-8dd8-5237d313d51c","updateGroupId":"28a93806-6fd0-4ad5-931d-97c63648d50a","releaseMessage":null}}
            """,
            """
            {"success":true,"data":{"state":"required","policyVersion":"4","channel":"pos-ipad-release-1","runtimeVersion":"1.2.0","iosUpdateId":null,"updateGroupId":"28a93806-6fd0-4ad5-931d-97c63648d50a","releaseMessage":null}}
            """
        ];

        foreach (var responseJson in invalidResponses)
        {
            var gateway = CreateHttpGateway(responseJson);

            var decision = await gateway.GetOtaDecisionAsync(
                new PosIpadOtaUpdateDecisionRequest("0247", "1.2.0", null, null),
                CancellationToken.None);

            Assert.Null(decision);
        }
    }

    [Fact]
    public async Task Http_gateway_accepts_exact_active_ota_decision()
    {
        var gateway = CreateHttpGateway(
            """
            {"success":true,"data":{"state":"optional","policyVersion":"4","channel":"pos-ipad-release-20260730-1","runtimeVersion":"1.2.0","iosUpdateId":"8fb126b2-0b64-4833-8dd8-5237d313d51c","updateGroupId":"28a93806-6fd0-4ad5-931d-97c63648d50a","releaseMessage":"测试分店灰度"}}
            """);

        var decision = await gateway.GetOtaDecisionAsync(
            new PosIpadOtaUpdateDecisionRequest("0247", "1.2.0", null, null),
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("4", decision.PolicyVersion);
        Assert.Equal("pos-ipad-release-20260730-1", decision.Channel);
    }

    private static HttpPosIpadUpdateDecisionGateway CreateHttpGateway(string responseJson) =>
        new(
            new HttpClient(new StubHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        responseJson,
                        Encoding.UTF8,
                        "application/json")
                }))),
            Options.Create(new AppUpdateOptions
            {
                CenterBaseUrl = "https://center.example/",
                ServiceApiToken = "hbsvc_example"
            }),
            NullLogger<HttpPosIpadUpdateDecisionGateway>.Instance);

    private static PosIpadAppUpdateController CreateController(
        IPosIpadUpdateDecisionGateway gateway,
        PosIpadOptions? options = null,
        AppUpdateOptions? appUpdateOptions = null)
    {
        var controller = new PosIpadAppUpdateController(
            Options.Create(options ?? new PosIpadOptions()),
            Options.Create(appUpdateOptions ?? new AppUpdateOptions()),
            gateway);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [
                            new(
                                DeviceAuthConstants.StoreCodeClaim,
                                "0247"),
                            new(
                                DeviceAuthConstants.DeviceSystemClaim,
                                DeviceSystems.IpadOs)
                        ],
                        DeviceAuthConstants.Scheme))
            }
        };
        return controller;
    }

    private static T GetOk<T>(ActionResult<ApiResult<T>> actionResult)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResult<T>>(ok.Value);
        return Assert.IsType<T>(envelope.Data);
    }

    private sealed class RecordingGateway : IPosIpadUpdateDecisionGateway
    {
        public PosIpadNativeUpdateDecision? NativeDecision { get; init; }

        public PosIpadOtaUpdateResponse? OtaDecision { get; init; }

        public string? NativeStoreCode { get; private set; }

        public string? OtaStoreCode { get; private set; }

        public Task<PosIpadNativeUpdateDecision?> GetNativeDecisionAsync(
            PosIpadNativeUpdateDecisionRequest request,
            CancellationToken cancellationToken)
        {
            NativeStoreCode = request.StoreCode;
            return Task.FromResult(NativeDecision);
        }

        public Task<PosIpadOtaUpdateResponse?> GetOtaDecisionAsync(
            PosIpadOtaUpdateDecisionRequest request,
            CancellationToken cancellationToken)
        {
            OtaStoreCode = request.StoreCode;
            return Task.FromResult(OtaDecision);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
