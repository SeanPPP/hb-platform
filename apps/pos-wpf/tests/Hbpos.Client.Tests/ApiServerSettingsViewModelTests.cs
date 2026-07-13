using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;

namespace Hbpos.Client.Tests;

public sealed class ApiServerSettingsViewModelTests
{
    [Fact]
    public void Load_reads_current_server_address()
    {
        var viewModel = CreateViewModel(
            _ => OnlineResponse(),
            currentAddress: "https://current.example.com/base");

        viewModel.Load();

        Assert.Equal("https://current.example.com/base/", viewModel.ServerAddressText);
        Assert.Equal("Ready.", viewModel.StatusMessage);
        Assert.False(viewModel.RestartRequired);
    }

    [Fact]
    public async Task TestConnectionCommand_reports_success_without_saving()
    {
        var savedAddresses = new List<string>();
        var viewModel = CreateViewModel(_ => OnlineResponse(), savedAddresses: savedAddresses);
        viewModel.ServerAddressText = "https://api.example.com";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("Connection succeeded.", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
        Assert.Empty(savedAddresses);
    }

    [Fact]
    public async Task SaveCommand_tests_before_saving_and_marks_restart_for_changed_address()
    {
        var events = new List<string>();
        var savedAddresses = new List<string>();
        var viewModel = CreateViewModel(
            _ =>
            {
                events.Add("test");
                return OnlineResponse();
            },
            currentAddress: "https://current.example.com/",
            savedAddresses: savedAddresses,
            onSave: _ => events.Add("save"));
        viewModel.ServerAddressText = "https://new.example.com";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["test", "save"], events);
        Assert.Equal("https://new.example.com/", Assert.Single(savedAddresses));
        Assert.True(viewModel.RestartRequired);
        Assert.Equal("Server address saved. Restart HBPOS to use the new address.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveCommand_does_not_write_when_health_check_fails()
    {
        var savedAddresses = new List<string>();
        var viewModel = CreateViewModel(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            savedAddresses: savedAddresses);
        viewModel.ServerAddressText = "https://offline.example.com";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(savedAddresses);
        Assert.False(viewModel.RestartRequired);
        Assert.Equal("Connection failed. Check the address and try again.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveCommand_does_not_require_restart_when_normalized_address_is_unchanged()
    {
        var savedAddresses = new List<string>();
        var viewModel = CreateViewModel(
            _ => OnlineResponse(),
            currentAddress: "https://api.example.com/",
            savedAddresses: savedAddresses);
        viewModel.ServerAddressText = " HTTPS://API.EXAMPLE.COM ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("https://api.example.com/", Assert.Single(savedAddresses));
        Assert.False(viewModel.RestartRequired);
        Assert.Equal("Server address saved.", viewModel.StatusMessage);
    }

    private static ApiServerSettingsViewModel CreateViewModel(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string currentAddress = "http://localhost:5159/",
        List<string>? savedAddresses = null,
        Action<string>? onSave = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var service = new ApiServerSettingsService(
            new HttpClient(handler),
            () => currentAddress,
            address =>
            {
                savedAddresses?.Add(address);
                onSave?.Invoke(address);
            });
        return new ApiServerSettingsViewModel(service, new LocalizationService());
    }

    private static HttpResponseMessage OnlineResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiResult<HealthCheckResponse>.Ok(
                new HealthCheckResponse(true, DateTimeOffset.UnixEpoch, "ok")))
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
