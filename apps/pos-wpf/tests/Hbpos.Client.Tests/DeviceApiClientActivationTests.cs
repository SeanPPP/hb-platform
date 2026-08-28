using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;

namespace Hbpos.Client.Tests;

public sealed class DeviceApiClientActivationTests
{
    private const string ActivationCode = "HBDEV1-0123456789ABCDEFGHJKMNPQRS-6789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public async Task Preview_posts_activation_code_and_windows_platform_without_store_selection()
    {
        CapturedRequest? captured = null;
        var client = CreateClient(async request =>
        {
            captured = await CaptureAsync(request);
            return Ok(new DeviceActivationCodePreviewResponse(
                true,
                null,
                "1002",
                "Lutwyche",
                DeviceSystems.Windows,
                DateTime.UtcNow.AddMinutes(15),
                "Ready"));
        });

        var result = await client.PreviewActivationCodeAsync(
            new DeviceActivationCodePreviewRequest(ActivationCode, DeviceSystems.Windows));

        Assert.Equal("1002", result.StoreCode);
        Assert.NotNull(captured);
        Assert.Equal("/api/v1/devices/activation-code/preview", captured.Path);
        using var body = JsonDocument.Parse(captured.Body);
        Assert.Equal(ActivationCode, body.RootElement.GetProperty("activationCode").GetString());
        Assert.Equal(DeviceSystems.Windows, body.RootElement.GetProperty("deviceSystem").GetString());
        Assert.False(body.RootElement.TryGetProperty("storeCode", out _));
    }

    [Fact]
    public async Task Redeem_posts_hardware_terminal_and_windows_platform()
    {
        CapturedRequest? captured = null;
        var client = CreateClient(async request =>
        {
            captured = await CaptureAsync(request);
            return Ok(AllowedCredentials());
        });

        await client.RedeemActivationCodeAsync(new DeviceActivationCodeRedeemRequest(
            ActivationCode,
            "HW-001",
            "POS-TILL-01",
            DeviceSystems.Windows));

        Assert.NotNull(captured);
        Assert.Equal("/api/v1/devices/activation-code/redeem", captured.Path);
        using var body = JsonDocument.Parse(captured.Body);
        Assert.Equal(ActivationCode, body.RootElement.GetProperty("activationCode").GetString());
        Assert.Equal("HW-001", body.RootElement.GetProperty("hardwareId").GetString());
        Assert.Equal("POS-TILL-01", body.RootElement.GetProperty("terminalName").GetString());
        Assert.Equal(DeviceSystems.Windows, body.RootElement.GetProperty("deviceSystem").GetString());
        Assert.False(body.RootElement.TryGetProperty("storeCode", out _));
        Assert.Empty(captured.ActivationRecoveryOnlyHeaderValues);
    }

    [Fact]
    public async Task Recovery_redeem_uses_fixed_recovery_only_header_and_same_body_contract()
    {
        CapturedRequest? captured = null;
        var client = CreateClient(async request =>
        {
            captured = await CaptureAsync(request);
            return Ok(AllowedCredentials());
        });

        await client.RedeemActivationCodeForRecoveryAsync(new DeviceActivationCodeRedeemRequest(
            ActivationCode,
            "HW-001",
            "POS-TILL-01",
            DeviceSystems.Windows));

        Assert.NotNull(captured);
        Assert.Equal("/api/v1/devices/activation-code/redeem", captured.Path);
        Assert.Equal(["true"], captured.ActivationRecoveryOnlyHeaderValues);
        using var body = JsonDocument.Parse(captured.Body);
        Assert.Equal(ActivationCode, body.RootElement.GetProperty("activationCode").GetString());
        Assert.Equal("HW-001", body.RootElement.GetProperty("hardwareId").GetString());
        Assert.Equal("POS-TILL-01", body.RootElement.GetProperty("terminalName").GetString());
        Assert.Equal(DeviceSystems.Windows, body.RootElement.GetProperty("deviceSystem").GetString());
        Assert.False(body.RootElement.TryGetProperty("storeCode", out _));
    }

    [Fact]
    public async Task Rebind_posts_only_activation_code_and_terminal_name()
    {
        CapturedRequest? captured = null;
        var client = CreateClient(async request =>
        {
            captured = await CaptureAsync(request);
            return Ok(AllowedCredentials());
        });

        await client.RebindActivationCodeAsync(new DeviceActivationCodeRebindRequest(
            ActivationCode,
            "POS-TILL-01"));

        Assert.NotNull(captured);
        Assert.Equal("/api/v1/devices/activation-code/rebind", captured.Path);
        using var body = JsonDocument.Parse(captured.Body);
        Assert.Equal(ActivationCode, body.RootElement.GetProperty("activationCode").GetString());
        Assert.Equal("POS-TILL-01", body.RootElement.GetProperty("terminalName").GetString());
        Assert.False(body.RootElement.TryGetProperty("hardwareId", out _));
        Assert.False(body.RootElement.TryGetProperty("deviceSystem", out _));
        Assert.False(body.RootElement.TryGetProperty("storeCode", out _));
    }

    private static DeviceApiClient CreateClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        return new DeviceApiClient(new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://pos.example.test/")
        });
    }

    private static async Task<CapturedRequest> CaptureAsync(HttpRequestMessage request)
    {
        var recoveryOnlyHeaderValues = request.Headers.TryGetValues(
            DeviceApiClient.ActivationRecoveryOnlyHeader,
            out var values)
            ? values.ToArray()
            : [];
        return new CapturedRequest(
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(),
            recoveryOnlyHeaderValues);
    }

    private static HttpResponseMessage Ok<T>(T data)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiResult<T>.Ok(data))
        };
    }

    private static DeviceActivationCodeRedeemResponse AllowedCredentials()
    {
        return new DeviceActivationCodeRedeemResponse(
            "POS-001",
            "1002",
            "Lutwyche",
            1,
            true,
            "Enabled",
            "AUTH-001",
            DeviceActivationReasonCodes.Activated);
    }

    private sealed record CapturedRequest(
        string Path,
        string Body,
        IReadOnlyList<string> ActivationRecoveryOnlyHeaderValues);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
