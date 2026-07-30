using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Hbpos.Api.Tests;

public sealed class PosIpadAppUpdateContractsTests
{
    [Fact]
    public void Response_keeps_the_unlisted_app_update_fields_separate_from_wpf_release_contract()
    {
        var response = new PosIpadAppUpdateResponse(
            Enabled: true,
            MinimumSupportedVersion: "1.0.0",
            LatestVersion: "1.0.1",
            ForceUpdate: false,
            AppStoreUrl: "https://apps.apple.com/unlisted/example",
            ReleaseMessage: "Preview rollout");

        Assert.True(response.Enabled);
        Assert.Equal("1.0.1", response.LatestVersion);
        Assert.False(response.ForceUpdate);
    }

    [Fact]
    public void Response_json_keeps_the_exact_six_field_contract()
    {
        var response = new PosIpadAppUpdateResponse(
            Enabled: true,
            MinimumSupportedVersion: "1.5.0.42",
            LatestVersion: "1.5.0.88",
            ForceUpdate: true,
            AppStoreUrl: "https://apps.apple.com/au/app/example/id123",
            ReleaseMessage: "同版本构建升级");

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                response,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var fields = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "appStoreUrl",
                "enabled",
                "forceUpdate",
                "latestVersion",
                "minimumSupportedVersion",
                "releaseMessage"
            },
            fields);
    }

    [Fact]
    public void Controller_uses_the_separate_authenticated_ipad_update_route()
    {
        var route = typeof(PosIpadAppUpdateController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Single();
        var authorize = typeof(PosIpadAppUpdateController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("api/v1/app-updates/pos-ipad", route.Template);
        Assert.Equal("HbposDevice", authorize.AuthenticationSchemes);
    }

    [Fact]
    public void Authenticated_device_policy_always_allows_new_transactions()
    {
        var controller = CreateController(new PosIpadOptions());

        var response = GetResponse(controller.Check("1.0.0", "1", "1.0.0"));

        Assert.True(response.Enabled);
    }

    [Fact]
    public void Controller_forces_update_when_marketing_version_and_build_are_below_minimum()
    {
        var controller = CreateController(new PosIpadOptions
        {
            MinimumSupportedVersion = "1.5.0.42",
            LatestVersion = "1.6.0"
        });

        var response = GetResponse(controller.Check("1.5.0", "41", "1.5.0"));

        Assert.True(response.ForceUpdate);
    }

    [Fact]
    public void Controller_uses_runtime_version_when_marketing_version_is_missing()
    {
        var controller = CreateController(new PosIpadOptions
        {
            MinimumSupportedVersion = "1.5.0",
            LatestVersion = "1.6.0"
        });

        var response = GetResponse(controller.Check(null, null, "1.4.9"));

        Assert.True(response.ForceUpdate);
    }

    [Fact]
    public void Configured_force_update_stops_for_clients_already_at_latest_version()
    {
        var controller = CreateController(new PosIpadOptions
        {
            MinimumSupportedVersion = "1.5.0",
            LatestVersion = "1.6.0",
            ForceUpdate = true
        });

        var response = GetResponse(controller.Check("1.6.0", "77", "1.6.0"));

        Assert.False(response.ForceUpdate);
    }

    [Fact]
    public void Missing_or_invalid_client_version_fails_closed_when_minimum_is_configured()
    {
        var controller = CreateController(new PosIpadOptions
        {
            MinimumSupportedVersion = "1.5.0"
        });

        var response = GetResponse(controller.Check("invalid", "invalid", "invalid"));

        Assert.True(response.ForceUpdate);
    }

    private static PosIpadAppUpdateController CreateController(PosIpadOptions configuration)
        => new(Options.Create(configuration));

    private static PosIpadAppUpdateResponse GetResponse(
        ActionResult<ApiResult<PosIpadAppUpdateResponse>> actionResult)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResult<PosIpadAppUpdateResponse>>(ok.Value);
        return Assert.IsType<PosIpadAppUpdateResponse>(envelope.Data);
    }
}
