using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;

namespace Hbpos.Client.Tests;

public sealed class DeviceRegistrationViewModelTests
{
    [Fact]
    public async Task Saving_new_server_address_blocks_registration_until_restart()
    {
        var workflow = new StubDeviceRegistrationWorkflowService();
        var apiServerSettings = CreateApiServerSettings();
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            apiServerSettings: apiServerSettings);
        await viewModel.InitializeAsync(cachedDevice: null);
        var registerStateChangeCount = 0;
        viewModel.RegisterCommand.CanExecuteChanged += (_, _) => registerStateChangeCount++;

        Assert.Same(apiServerSettings, viewModel.ApiServerSettings);
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.True(viewModel.VerifyCommand.CanExecute(null));

        apiServerSettings.ServerAddressText = "https://new.example.com";
        await apiServerSettings.SaveCommand.ExecuteAsync(null);

        Assert.True(apiServerSettings.RestartRequired);
        Assert.False(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(viewModel.VerifyCommand.CanExecute(null));
        Assert.True(registerStateChangeCount > 0);

        // 重复加载父页面和共享设置不能解除等待重启期间的注册阻断。
        apiServerSettings.Load();
        await viewModel.InitializeAsync(cachedDevice: null);

        Assert.False(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(viewModel.VerifyCommand.CanExecute(null));
        await viewModel.RegisterCommand.ExecuteAsync(null);
        await viewModel.VerifyCommand.ExecuteAsync(null);
        Assert.Equal(0, workflow.RegisterCallCount);
        Assert.Equal(0, workflow.VerifyCallCount);
    }

    [Fact]
    public void Registration_and_settings_views_use_the_shared_server_settings_panel()
    {
        var viewsRoot = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views");
        var deviceRegistrationXaml = File.ReadAllText(Path.Combine(viewsRoot, "Screens", "DeviceRegistrationView.xaml"));
        var settingsXaml = File.ReadAllText(Path.Combine(viewsRoot, "Screens", "SettingsView.xaml"));
        var sharedPanelXaml = File.ReadAllText(Path.Combine(viewsRoot, "Controls", "ApiServerSettingsPanel.xaml"));
        var mainWindowXaml = File.ReadAllText(Path.Combine(viewsRoot, "..", "MainWindow.xaml"));
        const string panelBinding = "<controls:ApiServerSettingsPanel DataContext=\"{Binding ApiServerSettings}\"";

        Assert.Contains(panelBinding, deviceRegistrationXaml);
        Assert.Contains(panelBinding, settingsXaml);
        Assert.True(
            deviceRegistrationXaml.IndexOf(panelBinding, StringComparison.Ordinal) <
            deviceRegistrationXaml.IndexOf("SelectedItem=\"{Binding SelectedStore", StringComparison.Ordinal));
        Assert.True(
            settingsXaml.IndexOf("IsDeviceRegistrationSelected", StringComparison.Ordinal) <
            settingsXaml.IndexOf(panelBinding, StringComparison.Ordinal));
        Assert.True(
            settingsXaml.IndexOf(panelBinding, StringComparison.Ordinal) <
            settingsXaml.IndexOf("ReregisterDeviceCommand", StringComparison.Ordinal));
        Assert.Contains("settings.serverAddress.address", sharedPanelXaml);

        var cashierLoginOverlay = XDocument.Parse(mainWindowXaml)
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId" &&
                attribute.Value == "CashierLoginOverlay"));
        var serverAddressExpander = cashierLoginOverlay
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Expander" &&
                element.Attribute("Header")?.Value == "{loc:Loc shell.cashierLogin.changeServerAddress}");
        Assert.Equal("False", serverAddressExpander.Attribute("IsExpanded")?.Value);
        Assert.Contains(
            serverAddressExpander.Descendants(),
            element =>
                element.Name.LocalName == "ApiServerSettingsPanel" &&
                element.Attribute("DataContext")?.Value == "{Binding ApiServerSettings}");

        var settingsViewModelSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "ViewModels",
            "SettingsViewModel.cs"));
        Assert.Contains(
            "SelectCategoryAsync(SettingsCategory.DeviceRegistration, Permissions.PosTerminal.Settings.DeviceRegistration)",
            settingsViewModelSource);
    }

    [Fact]
    public void Registration_view_wraps_centered_card_in_vertical_scroll_container()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "DeviceRegistrationView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollViewer = Assert.Single(document.Descendants(presentation + "ScrollViewer").Where(
            element => (string?)element.Attribute(x + "Name") == "RegistrationScrollViewer"));
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));

        var viewportGrid = Assert.Single(scrollViewer.Elements(presentation + "Grid"));
        Assert.Equal(
            "{Binding ElementName=RegistrationScrollViewer, Path=ViewportHeight}",
            (string?)viewportGrid.Attribute("MinHeight"));
        var card = Assert.Single(viewportGrid.Elements(presentation + "Border"));
        Assert.Equal("Center", (string?)card.Attribute("VerticalAlignment"));
        Assert.Equal(3, card.Descendants(presentation + "Button").Count());
    }

    [Fact]
    public void Server_settings_header_uses_constrained_grid_for_wrapping_description()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Controls",
            "ApiServerSettingsPanel.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var headerGrid = Assert.Single(document.Descendants(presentation + "Grid").Where(
            element => (string?)element.Attribute("Margin") == "0,0,0,12"));
        var columns = Assert.Single(headerGrid.Elements(presentation + "Grid.ColumnDefinitions"))
            .Elements(presentation + "ColumnDefinition")
            .ToArray();
        Assert.Equal(["Auto", "*"], columns.Select(column => (string?)column.Attribute("Width")));

        var icon = Assert.Single(headerGrid.Elements().Where(element => element.Name.LocalName == "PackIcon"));
        Assert.Equal("0", (string?)icon.Attribute("Grid.Column"));
        var textColumn = Assert.Single(headerGrid.Elements(presentation + "StackPanel"));
        Assert.Equal("1", (string?)textColumn.Attribute("Grid.Column"));
        var description = Assert.Single(textColumn.Elements(presentation + "TextBlock").Where(
            element => ((string?)element.Attribute("Text"))?.Contains("settings.serverAddress.description", StringComparison.Ordinal) == true));
        Assert.Equal("Wrap", (string?)description.Attribute("TextWrapping"));
    }

    private static ApiServerSettingsViewModel CreateApiServerSettings()
    {
        var service = new ApiServerSettingsService(
            new HttpClient(new StubHttpMessageHandler()),
            () => "https://current.example.com/",
            _ => { });
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

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "hb-platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to find repository root.");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OnlineResponse());
        }
    }

    private sealed class StubDeviceRegistrationWorkflowService : IDeviceRegistrationWorkflowService
    {
        private static readonly StoreSelectionItem Store = new("1002", "Lutwyche", true);

        public int RegisterCallCount { get; private set; }

        public int VerifyCallCount { get; private set; }

        public string GetHardwareId() => "HW-001";

        public Task<DeviceRegistrationLoadResult> LoadStoresAsync(
            LocalDeviceCache? cachedDevice,
            bool isReregisterMode,
            string? excludedStoreCode = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DeviceRegistrationLoadResult(
                [Store],
                Store,
                "POS-001",
                false,
                "Ready"));
        }

        public Task<DeviceRegistrationActionResult> RegisterAsync(
            StoreSelectionItem selectedStore,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            RegisterCallCount++;
            return Task.FromResult(EmptyActionResult());
        }

        public Task<DeviceRegistrationActionResult> VerifyAsync(
            StoreSelectionItem selectedStore,
            string deviceCode,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            VerifyCallCount++;
            return Task.FromResult(EmptyActionResult());
        }

        public Task<DeviceRegistrationActionResult> ReregisterAsync(
            StoreSelectionItem selectedStore,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EmptyActionResult());
        }

        private static DeviceRegistrationActionResult EmptyActionResult()
        {
            return new DeviceRegistrationActionResult(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                null,
                false,
                false);
        }
    }
}
