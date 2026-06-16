using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public enum SettingsCategory
{
    DataMaintenance,
    PaymentTerminal,
    ReceiptPrinter,
    DeviceRegistration
}

public enum LinklySettingsMode
{
    LocalIp,
    CloudDirectSync,
    CloudBackendAsync
}

public sealed partial class LinklyModePriorityItem(LinklySettingsMode mode) : ObservableObject
{
    [ObservableProperty]
    private int _rank;

    public LinklySettingsMode Mode { get; } = mode;

    public string ModeKey => Mode switch
    {
        LinklySettingsMode.CloudDirectSync => "settings.linkly.mode.cloudDirectSync",
        LinklySettingsMode.CloudBackendAsync => "settings.linkly.mode.cloudBackendAsync",
        _ => "settings.linkly.mode.localIp"
    };

    public string HelpKey => Mode switch
    {
        LinklySettingsMode.CloudDirectSync => "settings.linkly.mode.cloudDirectSync.help",
        LinklySettingsMode.CloudBackendAsync => "settings.linkly.mode.cloudBackendAsync.help",
        _ => "settings.linkly.mode.localIp.help"
    };

    public string RoleKey => Rank == 1
        ? "settings.linkly.priority.primary"
        : "settings.linkly.priority.fallback";

    partial void OnRankChanged(int value)
    {
        OnPropertyChanged(nameof(RoleKey));
    }
}

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private const string DefaultSquareDeviceCodeName = "HBPOS Terminal";

    private readonly ICardTerminalSetupService _setupService;
    private readonly ILocalizationService? _localization;
    private readonly Func<CancellationToken, Task>? _downloadCatalogAsync;
    private readonly Func<CancellationToken, Task>? _resetCatalogAsync;
    private readonly Func<CancellationToken, Task>? _resetTestSalesDataAsync;
    private readonly Func<bool>? _confirmResetTestSalesData;
    private readonly Func<Task<DeviceReregistrationStartResult>>? _reregisterDeviceAsync;
    private readonly Action? _returnToPos;
    private readonly IReceiptPrinterSettingsStore? _receiptPrinterSettingsStore;
    private readonly IReceiptPrintService? _receiptPrintService;
    private readonly ICardRecoveryResultDialogService? _cardRecoveryResultDialogService;
    private readonly DataMaintenanceSection _dataMaintenanceSection;
    private readonly ReceiptPrinterSection _receiptPrinterSection;
    private CardTerminalConfiguration _loadedConfiguration = CardTerminalConfiguration.Default;
    private string? _savedSquareLocationId;
    private string? _savedSquareDeviceId;
    private string? _devicesLoadedForLocationId;
    private string _statusKey = "settings.status.ready";
    private object[] _statusArgs = [];
    private string? _statusOverride;
    private string? _linklyTestStatusKey;
    private object[] _linklyTestStatusArgs = [];
    private string? _linklyTestStatusOverride;
    private string? _receiptPrinterTestStatusOverride;
    private string _lastSquareDeviceCodeNameSuggestion = DefaultSquareDeviceCodeName;
    private int _linklySecretStatusVersion;
    private int _linklyCredentialStatusVersion;
    private int _linklyCredentialEditVersion;
    private bool _disposed;
    private bool _hasLinklyCloudPasswordInput;

    [ObservableProperty]
    private SettingsCategory _selectedCategory = SettingsCategory.DataMaintenance;

    [ObservableProperty]
    private bool _isSquareSandbox;

    [ObservableProperty]
    private bool _isLinklySandbox;

    [ObservableProperty]
    private bool _isLinklySetupInstructionsOpen;

    [ObservableProperty]
    private bool _hasSavedSquareToken;

    [ObservableProperty]
    private SquareLocationOption? _selectedSquareLocation;

    [ObservableProperty]
    private SquareDeviceOption? _selectedSquareDevice;

    [ObservableProperty]
    private SquareDeviceCodeOption? _selectedSquareDeviceCode;

    [ObservableProperty]
    private string _squareDeviceCodeNameText = DefaultSquareDeviceCodeName;

    [ObservableProperty]
    private string _linklyHostText = CardTerminalConfiguration.Default.LinklyHost;

    [ObservableProperty]
    private string _linklyPortText = CardTerminalConfiguration.Default.LinklyPort.ToString();

    [ObservableProperty]
    private LinklySettingsMode _selectedLinklyMode = LinklySettingsMode.LocalIp;

    [ObservableProperty]
    private string _linklyPairCodeText = string.Empty;

    [ObservableProperty]
    private string _linklyCloudUsernameText = string.Empty;

    [ObservableProperty]
    private string _linklyCloudPasswordText = string.Empty;

    [ObservableProperty]
    private bool _hasSavedLinklyCloudPassword;

    [ObservableProperty]
    private bool _hasSavedLinklyCloudSecret;

    [ObservableProperty]
    private string _timeoutSecondsText = CardTerminalConfiguration.Default.TerminalTimeoutSeconds.ToString();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _linklyConnectionSucceeded;

    [ObservableProperty]
    private string _linklyTestStatusMessage = string.Empty;

    [ObservableProperty]
    private string _receiptPrinterPortText = ReceiptPrinterSettings.Default.PrinterPort;

    [ObservableProperty]
    private string _receiptBrandNameText = ReceiptPrinterSettings.Default.BrandName;

    [ObservableProperty]
    private string _receiptStoreNameText = string.Empty;

    [ObservableProperty]
    private string _receiptStoreAddressText = string.Empty;

    [ObservableProperty]
    private string _receiptStorePhoneText = string.Empty;

    [ObservableProperty]
    private string _receiptAbnText = string.Empty;

    [ObservableProperty]
    private string _receiptReturnPolicyText = string.Empty;

    [ObservableProperty]
    private string _receiptPrinterTestStatusMessage = string.Empty;

    public SettingsViewModel(
        ICardTerminalSetupService setupService,
        ILocalizationService? localization = null,
        Func<CancellationToken, Task>? downloadCatalogAsync = null,
        Func<CancellationToken, Task>? resetCatalogAsync = null,
        Func<Task<DeviceReregistrationStartResult>>? reregisterDeviceAsync = null,
        Action? returnToPos = null,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore = null,
        IReceiptPrintService? receiptPrintService = null,
        Func<CancellationToken, Task>? resetTestSalesDataAsync = null,
        Func<bool>? confirmResetTestSalesData = null,
        ICardRecoveryResultDialogService? cardRecoveryResultDialogService = null)
    {
        _setupService = setupService;
        _localization = localization;
        _downloadCatalogAsync = downloadCatalogAsync;
        _resetCatalogAsync = resetCatalogAsync;
        _resetTestSalesDataAsync = resetTestSalesDataAsync;
        _confirmResetTestSalesData = confirmResetTestSalesData;
        _reregisterDeviceAsync = reregisterDeviceAsync;
        _returnToPos = returnToPos;
        _receiptPrinterSettingsStore = receiptPrinterSettingsStore;
        _receiptPrintService = receiptPrintService;
        _cardRecoveryResultDialogService = cardRecoveryResultDialogService;
        _dataMaintenanceSection = new DataMaintenanceSection(new DataMaintenanceContext(
            IsBusy: () => IsBusy,
            DownloadCatalogAsync: _downloadCatalogAsync,
            ResetCatalogAsync: _resetCatalogAsync,
            ResetTestSalesDataAsync: _resetTestSalesDataAsync,
            ConfirmResetTestSalesData: _confirmResetTestSalesData,
            ReregisterDeviceAsync: _reregisterDeviceAsync,
            RunBusyAsync: (action, operationName) => RunBusyAsync(action, operationName),
            SetStatus: key => SetStatus(key),
            SetStatusOverride: SetStatusOverride));
        _receiptPrinterSection = new ReceiptPrinterSection(new ReceiptPrinterContext(
            SettingsStore: _receiptPrinterSettingsStore,
            PrintService: _receiptPrintService,
            IsBusy: () => IsBusy,
            CreateSettingsFromFields: CreateReceiptPrinterSettingsFromFields,
            ApplySettings: ApplyReceiptPrinterSettings,
            RunBusyAsync: (action, operationName) => RunBusyAsync(action, operationName),
            SetStatus: key => SetStatus(key),
            SetStatusOverride: SetStatusOverride,
            ClearReceiptPrinterTestStatus: () => ReceiptPrinterTestStatusMessage = string.Empty,
            SetReceiptPrinterTestStatus: message =>
            {
                ReceiptPrinterTestStatusMessage = message;
                _receiptPrinterTestStatusOverride = message;
            }));
        if (_localization is not null)
        {
            _localization.CultureChanged += OnCultureChanged;
        }

        SelectDataMaintenanceCommand = new RelayCommand(() => SelectedCategory = SettingsCategory.DataMaintenance);
        SelectPaymentTerminalCommand = new RelayCommand(() => SelectedCategory = SettingsCategory.PaymentTerminal);
        SelectReceiptPrinterCommand = new RelayCommand(() => SelectedCategory = SettingsCategory.ReceiptPrinter);
        SelectDeviceRegistrationCommand = new RelayCommand(() => SelectedCategory = SettingsCategory.DeviceRegistration);
        LoadCommand = new AsyncRelayCommand(LoadAsync);
        LoadLocationsCommand = new AsyncRelayCommand(LoadLocationsAsync, CanLoadLocations);
        LoadDevicesCommand = new AsyncRelayCommand(LoadDevicesAsync, CanLoadDevices);
        SaveSquareCommand = new AsyncRelayCommand(SaveSquareAsync, CanSaveSquare);
        LoadDeviceCodesCommand = new AsyncRelayCommand(LoadDeviceCodesAsync, CanLoadDeviceCodes);
        CreateDeviceCodeCommand = new AsyncRelayCommand(CreateDeviceCodeAsync, CanCreateDeviceCode);
        RefreshDeviceCodeStatusCommand = new AsyncRelayCommand(RefreshDeviceCodeStatusAsync, CanRefreshDeviceCodeStatus);
        PairLinklyCloudCommand = new AsyncRelayCommand(PairLinklyCloudAsync, CanPairLinklyCloud);
        SaveLinklyCloudCredentialCommand = new AsyncRelayCommand(SaveLinklyCloudCredentialAsync, CanSaveLinklyCloudCredential);
        CancelLinklyCloudPairingCommand = new RelayCommand(CancelLinklyCloudPairing, CanCancelLinklyCloudPairing);
        OpenLinklySetupInstructionsCommand = new RelayCommand(() => IsLinklySetupInstructionsOpen = true);
        CloseLinklySetupInstructionsCommand = new RelayCommand(() => IsLinklySetupInstructionsOpen = false);
        TestLinklyCommand = new AsyncRelayCommand(TestLinklyAsync, CanTestLinkly);
        SaveLinklyCommand = new AsyncRelayCommand(SaveLinklyAsync, CanSaveLinkly);
        MoveLinklyPriorityUpCommand = new RelayCommand<LinklyModePriorityItem>(MoveLinklyPriorityUp, CanMoveLinklyPriorityUp);
        MoveLinklyPriorityDownCommand = new RelayCommand<LinklyModePriorityItem>(MoveLinklyPriorityDown, CanMoveLinklyPriorityDown);
        SelectLinklyPriorityModeCommand = new RelayCommand<LinklyModePriorityItem>(SelectLinklyPriorityMode);
        SaveReceiptPrinterCommand = new AsyncRelayCommand(SaveReceiptPrinterAsync, CanSaveReceiptPrinter);
        TestReceiptPrinterCommand = new AsyncRelayCommand(TestReceiptPrinterAsync, CanTestReceiptPrinter);
        DownloadCatalogCommand = new AsyncRelayCommand(DownloadCatalogAsync, CanDownloadCatalog);
        ResetCatalogCommand = new AsyncRelayCommand(ResetCatalogAsync, CanResetCatalog);
        ResetTestSalesDataCommand = new AsyncRelayCommand(ResetTestSalesDataAsync, CanResetTestSalesData);
        ReregisterDeviceCommand = new AsyncRelayCommand(ReregisterDeviceAsync, CanReregisterDevice);
        TestLinklyTransactionStatusCommand = new AsyncRelayCommand(TestLinklyTransactionStatusAsync, CanTestLinklyTransactionStatus);
        BackCommand = new RelayCommand(ReturnToPos, () => _returnToPos is not null);
        ResetLinklyModePriority(CardTerminalConfiguration.Default.LinklyConnectionModePriority);
        RefreshLocalizedMessages();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_localization is not null)
        {
            _localization.CultureChanged -= OnCultureChanged;
        }
    }

    public ObservableCollection<SquareLocationOption> SquareLocations { get; } = [];

    public ObservableCollection<SquareDeviceOption> SquareDevices { get; } = [];

    public ObservableCollection<SquareDeviceCodeOption> SquareDeviceCodes { get; } = [];

    public ObservableCollection<LinklyModePriorityItem> LinklyModePriorityItems { get; } = [];

    public void RaiseLinklyCloudPasswordInputChanged(bool hasPassword)
    {
        _hasLinklyCloudPasswordInput = hasPassword;
        Interlocked.Increment(ref _linklyCredentialEditVersion);
        ResetLinklyConnectionTest();
        RaiseCommandStates();
    }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand LoadLocationsCommand { get; }

    public IAsyncRelayCommand LoadDevicesCommand { get; }

    public IAsyncRelayCommand SaveSquareCommand { get; }

    public IAsyncRelayCommand LoadDeviceCodesCommand { get; }

    public IAsyncRelayCommand CreateDeviceCodeCommand { get; }

    public IAsyncRelayCommand RefreshDeviceCodeStatusCommand { get; }

    public IAsyncRelayCommand PairLinklyCloudCommand { get; }

    public IAsyncRelayCommand SaveLinklyCloudCredentialCommand { get; }

    public IRelayCommand CancelLinklyCloudPairingCommand { get; }

    public IRelayCommand OpenLinklySetupInstructionsCommand { get; }

    public IRelayCommand CloseLinklySetupInstructionsCommand { get; }

    public IAsyncRelayCommand TestLinklyCommand { get; }

    public IAsyncRelayCommand TestLinklyTransactionStatusCommand { get; }

    public IAsyncRelayCommand SaveLinklyCommand { get; }

    public IRelayCommand<LinklyModePriorityItem> MoveLinklyPriorityUpCommand { get; }

    public IRelayCommand<LinklyModePriorityItem> MoveLinklyPriorityDownCommand { get; }

    public IRelayCommand<LinklyModePriorityItem> SelectLinklyPriorityModeCommand { get; }

    public IRelayCommand SelectDataMaintenanceCommand { get; }

    public IRelayCommand SelectPaymentTerminalCommand { get; }

    public IRelayCommand SelectReceiptPrinterCommand { get; }

    public IRelayCommand SelectDeviceRegistrationCommand { get; }

    public IAsyncRelayCommand SaveReceiptPrinterCommand { get; }

    public IAsyncRelayCommand TestReceiptPrinterCommand { get; }

    public IAsyncRelayCommand DownloadCatalogCommand { get; }

    public IAsyncRelayCommand ResetCatalogCommand { get; }

    public IAsyncRelayCommand ResetTestSalesDataCommand { get; }

    public IAsyncRelayCommand ReregisterDeviceCommand { get; }

    public IRelayCommand BackCommand { get; }

    public string ScreenTitleText => T("settings.title");

    public string SettingsSubtitleText => SelectedCategory switch
    {
        SettingsCategory.DataMaintenance => T("settings.subtitle.dataMaintenance"),
        SettingsCategory.PaymentTerminal => T("settings.subtitle.paymentTerminal"),
        SettingsCategory.ReceiptPrinter => T("settings.subtitle.receiptPrinter"),
        SettingsCategory.DeviceRegistration => T("settings.subtitle.deviceRegistration"),
        _ => T("settings.title")
    };

    public string CardTerminalTitleText => T("settings.subtitle.paymentTerminal");

    public string DataMaintenanceTitleText => T("settings.category.dataMaintenance");

    public string DeviceRegistrationTitleText => T("settings.category.deviceRegistration");

    public string SquareTitleText => T("settings.square.title");

    public string LinklyTitleText => T("settings.linkly.title");

    public string ReceiptPrinterTitleText => T("settings.receiptPrinter.title");

    public bool IsDataMaintenanceSelected => SelectedCategory == SettingsCategory.DataMaintenance;

    public bool IsPaymentTerminalSelected => SelectedCategory == SettingsCategory.PaymentTerminal;

    public bool IsReceiptPrinterSelected => SelectedCategory == SettingsCategory.ReceiptPrinter;

    public bool IsDeviceRegistrationSelected => SelectedCategory == SettingsCategory.DeviceRegistration;

    public bool IsDebugTestSalesDataResetVisible
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public string SquareTokenStatusText => HasSavedSquareToken
        ? T("settings.square.tokenStatus.cached")
        : T("settings.square.tokenStatus.missing");

    public bool IsSquareDeviceCodesSupported => !IsSquareSandbox;

    public bool IsSquareDeviceCodesUnsupported => !IsSquareDeviceCodesSupported;

    public string SquareDeviceCodesUnavailableText => T("settings.square.deviceCodes.unsupported");

    public bool CanChangeEnvironment => !IsBusy;

    public LinklySettingsMode PrimaryLinklyMode => LinklyModePriorityItems.Count == 0
        ? SelectedLinklyMode
        : LinklyModePriorityItems[0].Mode;

    public bool IsLinklyCloudMode
    {
        get => SelectedLinklyMode is LinklySettingsMode.CloudDirectSync or LinklySettingsMode.CloudBackendAsync;
        set => SelectedLinklyMode = value ? LinklySettingsMode.CloudDirectSync : LinklySettingsMode.LocalIp;
    }

    public bool IsLinklyLocalMode => IsLinklyLocalIpMode;

    public bool IsLinklyLocalIpMode
    {
        get => SelectedLinklyMode == LinklySettingsMode.LocalIp;
        set
        {
            if (value)
            {
                SelectedLinklyMode = LinklySettingsMode.LocalIp;
            }
        }
    }

    public bool IsLinklyCloudDirectSyncMode
    {
        get => SelectedLinklyMode == LinklySettingsMode.CloudDirectSync;
        set
        {
            if (value)
            {
                SelectedLinklyMode = LinklySettingsMode.CloudDirectSync;
            }
        }
    }

    public bool IsLinklyCloudBackendAsyncMode
    {
        get => SelectedLinklyMode == LinklySettingsMode.CloudBackendAsync;
        set
        {
            if (value)
            {
                SelectedLinklyMode = LinklySettingsMode.CloudBackendAsync;
            }
        }
    }

    public bool IsLinklyStandardActionMode => !IsLinklyCloudBackendAsyncMode;

    public string LinklyCloudSecretStatusText => HasSavedLinklyCloudSecret
        ? T("settings.linkly.cloud.secretStatus.cached")
        : T("settings.linkly.cloud.secretStatus.missing");

    public string LinklyCloudCredentialStatusText => HasSavedLinklyCloudPassword
        ? T("settings.linkly.cloud.credentialStatus.cached")
        : T("settings.linkly.cloud.credentialStatus.missing");

    public bool CanSaveLinklyCloudCredentialFromView =>
        CanSaveLinklyCloudCredential() && _hasLinklyCloudPasswordInput;

    public bool CanPairLinklyCloudFromView => CanPairLinklyCloud();

    public bool CanCancelLinklyCloudPairingFromView => CanCancelLinklyCloudPairing();

    public CardTerminalEnvironment SelectedSquareEnvironment => IsSquareSandbox
        ? CardTerminalEnvironment.Sandbox
        : CardTerminalEnvironment.Production;

    public CardTerminalEnvironment SelectedLinklyEnvironment => IsLinklySandbox
        ? CardTerminalEnvironment.Sandbox
        : CardTerminalEnvironment.Production;

    public CardTerminalEnvironment SelectedEnvironment => SelectedLinklyEnvironment;

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            _loadedConfiguration = await _setupService.LoadConfigurationAsync();
            await LoadReceiptPrinterSettingsAsync();
            IsSquareSandbox = _loadedConfiguration.Environment == CardTerminalEnvironment.Sandbox;
            IsLinklySandbox = _loadedConfiguration.Environment == CardTerminalEnvironment.Sandbox;
            LinklyHostText = _loadedConfiguration.LinklyHost;
            LinklyPortText = _loadedConfiguration.LinklyPort.ToString();
            ResetLinklyModePriority(_loadedConfiguration.LinklyConnectionModePriority);
            SelectedLinklyMode = ToSettingsMode(_loadedConfiguration.LinklyConnectionMode);
            await LoadLinklyCloudCredentialFieldsAsync(SelectedLinklyEnvironment);
            HasSavedLinklyCloudSecret = _loadedConfiguration.HasProtectedLinklyCloudSecret;
            LinklyPairCodeText = string.Empty;
            TimeoutSecondsText = _loadedConfiguration.TerminalTimeoutSeconds.ToString();
            HasSavedSquareToken = _loadedConfiguration.HasProtectedSquareAccessToken;
            _savedSquareLocationId = _loadedConfiguration.SquareLocationId;
            _savedSquareDeviceId = _loadedConfiguration.SquareDeviceId;
            _devicesLoadedForLocationId = null;
            LinklyConnectionSucceeded = false;
            ClearLinklyTestStatus();
            SquareLocations.Clear();
            SquareDevices.Clear();
            ResetSquareDeviceCodes();
            SelectedSquareLocation = null;
            SelectedSquareDevice = null;
            LogSquareSettings(
                $"load settings succeeded squareEnvironment={SelectedSquareEnvironment} linklyEnvironment={SelectedLinklyEnvironment} hasSavedToken={HasSavedSquareToken} savedLocationId={LogValue(_savedSquareLocationId)} savedDeviceId={LogValue(_savedSquareDeviceId)}");
            SetStatus("settings.status.loaded");
        }, operationName: "load settings");
    }

    private async Task LoadLocationsAsync()
    {
        LogSquareSettings($"load locations requested environment={SelectedSquareEnvironment}");
        await RunBusyAsync(async () =>
        {
            SquareLocations.ReplaceWith(await _setupService.ListSquareLocationsAsync(
                accessToken: null,
                SelectedSquareEnvironment));
            SquareDevices.Clear();
            ResetSquareDeviceCodes();
            _devicesLoadedForLocationId = null;
            SelectedSquareDevice = null;
            SelectedSquareLocation = SquareLocations.FirstOrDefault(location =>
                string.Equals(location.Id, _savedSquareLocationId, StringComparison.OrdinalIgnoreCase));
            HasSavedSquareToken = true;
            LogSquareSettings(
                $"load locations succeeded environment={SelectedSquareEnvironment} count={SquareLocations.Count} selectedLocationId={LogValue(SelectedSquareLocation?.Id)}");
            SetStatus(
                SquareLocations.Count == 0 ? "settings.status.noSquareLocations" : "settings.status.squareLocationsLoaded",
                SquareLocations.Count);
        }, operationName: "load square locations");
    }

    private async Task LoadDevicesAsync()
    {
        if (SelectedSquareLocation is null)
        {
            SetStatus("settings.status.selectSquareLocation");
            return;
        }

        LogSquareSettings($"load devices requested environment={SelectedSquareEnvironment} locationId={LogValue(SelectedSquareLocation.Id)}");
        await RunBusyAsync(async () =>
        {
            await LoadSquareDevicesForLocationAsync(SelectedSquareLocation.Id, selectSavedDevice: true);
            HasSavedSquareToken = true;
            LogSquareSettings(
                $"load devices succeeded environment={SelectedSquareEnvironment} locationId={LogValue(SelectedSquareLocation.Id)} count={SquareDevices.Count} selectedDeviceId={LogValue(SelectedSquareDevice?.Id)}");
            SetStatus(
                SquareDevices.Count == 0 ? "settings.status.noSquareDevices" : "settings.status.squareDevicesLoaded",
                SquareDevices.Count);
        }, operationName: "load square devices");
    }

    private async Task SaveSquareAsync()
    {
        if (SelectedSquareLocation is null)
        {
            SetStatus("settings.status.selectSquareLocation");
            return;
        }

        if (SelectedSquareDevice is null)
        {
            SetStatus("settings.status.selectSquareDevice");
            return;
        }

        if (!SquareLocations.Any(location => string.Equals(location.Id, SelectedSquareLocation.Id, StringComparison.OrdinalIgnoreCase)) ||
            !SquareDevices.Any(device => SquareDeviceIdNormalizer.AreEquivalent(device.Id, SelectedSquareDevice.Id)) ||
            !string.Equals(_devicesLoadedForLocationId, SelectedSquareLocation.Id, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("settings.status.loadSquareBeforeSave");
            return;
        }

        LogSquareSettings(
            $"save square requested environment={SelectedSquareEnvironment} locationId={LogValue(SelectedSquareLocation.Id)} deviceId={LogValue(SelectedSquareDevice.Id)}");
        await RunBusyAsync(async () =>
        {
            var savedDeviceId = SquareDeviceIdNormalizer.NormalizeForTerminalCheckout(SelectedSquareDevice.Id);
            var configuration = new CardTerminalConfiguration(
                CardProcessorKind.Square,
                SelectedSquareEnvironment,
                NormalizeHost(LinklyHostText),
                ParsePort(LinklyPortText),
                SelectedSquareLocation.Id,
                savedDeviceId,
                HasSavedSquareToken,
                ParseTimeoutSeconds(TimeoutSecondsText));

            await _setupService.SaveSquareAsync(
                configuration,
                squareAccessToken: null);
            _loadedConfiguration = configuration;
            _savedSquareLocationId = configuration.SquareLocationId;
            _savedSquareDeviceId = configuration.SquareDeviceId;
            HasSavedSquareToken = configuration.HasProtectedSquareAccessToken;
            LogSquareSettings(
                $"save square succeeded environment={SelectedSquareEnvironment} locationId={LogValue(configuration.SquareLocationId)} selectedDeviceId={LogValue(SelectedSquareDevice.Id)} savedDeviceId={LogValue(configuration.SquareDeviceId)}");
            SetStatus("settings.status.squareSaved", SelectedSquareDevice.Name);
        }, operationName: "save square settings");
    }

    private async Task LoadDeviceCodesAsync()
    {
        if (SelectedSquareLocation is null)
        {
            SetStatus("settings.status.selectSquareLocation");
            return;
        }

        LogSquareSettings($"load device codes requested environment={SelectedSquareEnvironment} locationId={LogValue(SelectedSquareLocation.Id)}");
        await RunBusyAsync(async () =>
        {
            SquareDeviceCodes.ReplaceWith(await _setupService.ListSquareDeviceCodesAsync(
                accessToken: null,
                SelectedSquareEnvironment,
                SelectedSquareLocation.Id));
            SelectedSquareDeviceCode = SquareDeviceCodes.FirstOrDefault(deviceCode =>
                SquareDeviceIdNormalizer.AreEquivalent(deviceCode.DeviceId, _savedSquareDeviceId));
            HasSavedSquareToken = true;
            SuggestSquareDeviceCodeName(force: false);
            LogSquareSettings(
                $"load device codes succeeded environment={SelectedSquareEnvironment} locationId={LogValue(SelectedSquareLocation.Id)} count={SquareDeviceCodes.Count} selectedDeviceCodeId={LogValue(SelectedSquareDeviceCode?.Id)}");
            SetStatus("settings.status.squareDeviceCodesLoaded", SquareDeviceCodes.Count);
        }, operationName: "load square device codes");
    }

    private async Task CreateDeviceCodeAsync()
    {
        if (SelectedSquareLocation is null)
        {
            SetStatus("settings.status.selectSquareLocation");
            return;
        }

        if (string.IsNullOrWhiteSpace(SquareDeviceCodeNameText))
        {
            SetStatus("settings.status.squareDeviceCodeNameRequired");
            return;
        }

        LogSquareSettings(
            $"create device code requested environment={SelectedSquareEnvironment} locationId={LogValue(SelectedSquareLocation.Id)} name={LogValue(SquareDeviceCodeNameText.Trim())}");
        await RunBusyAsync(async () =>
        {
            var created = await _setupService.CreateSquareDeviceCodeAsync(
                accessToken: null,
                SelectedSquareEnvironment,
                SelectedSquareLocation.Id,
                SquareDeviceCodeNameText);
            SquareDeviceCodes.Insert(0, created);
            SelectedSquareDeviceCode = created;
            HasSavedSquareToken = true;
            LogSquareSettings(
                $"create device code succeeded environment={SelectedSquareEnvironment} locationId={LogValue(SelectedSquareLocation.Id)} deviceCodeId={created.Id} status={created.Status}");
            SetStatus("settings.status.squareDeviceCodeCreated", created.Code, created.Name);
        }, operationName: "create square device code");
    }

    private async Task RefreshDeviceCodeStatusAsync()
    {
        if (SelectedSquareDeviceCode is null)
        {
            SetStatus("settings.status.selectSquareDeviceCode");
            return;
        }

        LogSquareSettings(
            $"refresh device code requested environment={SelectedSquareEnvironment} deviceCodeId={LogValue(SelectedSquareDeviceCode.Id)}");
        await RunBusyAsync(async () =>
        {
            var refreshed = await _setupService.GetSquareDeviceCodeAsync(
                accessToken: null,
                SelectedSquareEnvironment,
                SelectedSquareDeviceCode.Id);
            ReplaceSquareDeviceCode(refreshed);
            SelectedSquareDeviceCode = refreshed;
            HasSavedSquareToken = true;

            if (string.Equals(refreshed.Status, "PAIRED", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refreshed.DeviceId) &&
                SelectedSquareLocation is not null)
            {
                await LoadSquareDevicesForLocationAsync(SelectedSquareLocation.Id, selectSavedDevice: false);
                SelectedSquareDevice = SquareDevices.FirstOrDefault(device =>
                    SquareDeviceIdNormalizer.AreEquivalent(device.Id, refreshed.DeviceId));

                if (SelectedSquareDevice is not null)
                {
                    LogSquareSettings(
                        $"refresh device code paired environment={SelectedSquareEnvironment} deviceCodeId={refreshed.Id} squareDeviceId={LogValue(refreshed.DeviceId)} selectedDeviceId={LogValue(SelectedSquareDevice.Id)}");
                    SetStatus("settings.status.squareDeviceCodePaired", SelectedSquareDevice.Name);
                    return;
                }
            }

            LogSquareSettings(
                $"refresh device code completed environment={SelectedSquareEnvironment} deviceCodeId={refreshed.Id} status={refreshed.Status} squareDeviceId={LogValue(refreshed.DeviceId)}");
            SetStatus("settings.status.squareDeviceCodeNotPaired", refreshed.Status);
        }, operationName: "refresh square device code");
    }

    private async Task TestLinklyAsync()
    {
        await RunBusyAsync(async () =>
        {
            try
            {
                LinklyConnectionSucceeded = false;
                ClearLinklyTestStatus();
                LinklyConnectionTestResult result;
                var testMode = PrimaryLinklyMode;
                if (testMode == LinklySettingsMode.CloudBackendAsync)
                {
                    result = await _setupService.TestLinklyCloudBackendConnectionAsync(SelectedLinklyEnvironment);
                }
                else if (testMode == LinklySettingsMode.CloudDirectSync)
                {
                    result = await _setupService.TestLinklyCloudConnectionAsync(SelectedLinklyEnvironment);
                }
                else
                {
                    result = await _setupService.TestLinklyConnectionAsync(
                        NormalizeHost(LinklyHostText),
                        ParsePort(LinklyPortText),
                        TimeSpan.FromSeconds(ParseTimeoutSeconds(TimeoutSecondsText)));
                }

                LinklyConnectionSucceeded = result.Succeeded;

                if (string.IsNullOrWhiteSpace(result.Message))
                {
                    var key = result.Succeeded
                        ? "settings.status.linklyTestSuccess"
                        : "settings.status.linklyTestFailed";
                    SetLinklyTestStatus(key);
                    SetStatus(key);
                }
                else
                {
                    SetLinklyTestStatusOverride(result.Message);
                    SetStatusOverride(result.Message);
                }

            }
            catch (Exception ex)
            {
                SetLinklyTestStatusOverride(ex.Message);
                SetStatusOverride(ex.Message);
                throw;
            }
        });
    }

    private void ShowFailedLastTransactionDialogIfNeeded(LinklyConnectionTestResult result)
    {
        if (result.Succeeded ||
            result.StatusTest is not { } status ||
            !IsFailedLastTransactionStatus(status, result.Message))
        {
            return;
        }

        _cardRecoveryResultDialogService?.Show(new CardRecoveryResultDialogViewModel(
            T("cardRecovery.dialog.title.failedLastTransaction"),
            T("cardRecovery.dialog.message.failedLastTransaction"),
            CardRecoveryResultSeverity.Warning,
            orderGuid: null,
            amount: null,
            sessionId: status.TransactionReference,
            txnRef: status.TxnRef,
            responseCode: status.ResponseCode,
            responseText: status.ResponseText ?? result.Message,
            timestamp: status.Timestamp ?? DateTimeOffset.Now));
    }

    private static bool IsFailedLastTransactionStatus(LinklyStatusTestDetails status, string? message)
    {
        var code = status.ResponseCode?.Trim();
        if (string.Equals(code, "00", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "T0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ContainsFailureText(status.ResponseText) ||
            ContainsFailureText(message);
    }

    private static bool ContainsFailureText(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
            (text.Contains("DECLINED", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("CANCELLED", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("FAILED", StringComparison.OrdinalIgnoreCase));
    }

    private async Task TestLinklyTransactionStatusAsync()
    {
        await RunBusyAsync(async () =>
        {
            try
            {
                ClearLinklyTestStatus();
                var result = await _setupService.TestLinklyCloudBackendTransactionStatusAsync(SelectedLinklyEnvironment);
                if (string.IsNullOrWhiteSpace(result.Message))
                {
                    var key = result.Succeeded
                        ? "settings.status.linklyTestSuccess"
                        : "settings.status.linklyTestFailed";
                    SetLinklyTestStatus(key);
                    SetStatus(key);
                }
                else
                {
                    SetLinklyTestStatusOverride(result.Message);
                    SetStatusOverride(result.Message);
                }

                ShowFailedLastTransactionDialogIfNeeded(result);
            }
            catch (Exception ex)
            {
                SetLinklyTestStatusOverride(ex.Message);
                SetStatusOverride(ex.Message);
                throw;
            }
        });
    }

    private async Task PairLinklyCloudAsync()
    {
        await PairLinklyCloudAsync(LinklyCloudPasswordText);
    }

    public async Task PairLinklyCloudAsync(string password)
    {
        var pairingEnvironment = SelectedLinklyEnvironment;
        var pairCode = LinklyPairCodeText;
        var username = LinklyCloudUsernameText;
        LogLinklyCloudSettings(
            $"pair clicked environment={pairingEnvironment} hasUsername={!string.IsNullOrWhiteSpace(username)} hasCurrentPassword=REDACTED hasSavedPassword=REDACTED hasPairCode={!string.IsNullOrWhiteSpace(pairCode)}");
        if (!ValidateLinklyCloudPairingInput(password))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            try
            {
                LinklyConnectionSucceeded = false;
                ClearLinklyTestStatus();
                var result = await _setupService.PairLinklyCloudAsync(
                    pairingEnvironment,
                    pairCode,
                    username,
                    password,
                    syncBackendTerminalCredential: IsLinklyCloudBackendAsyncMode);
                LogLinklyCloudSettings($"pair completed environment={pairingEnvironment} currentEnvironment={SelectedLinklyEnvironment} success={result.Succeeded} hasMessage={!string.IsNullOrWhiteSpace(result.Message)}");
                if (SelectedLinklyEnvironment == pairingEnvironment)
                {
                    HasSavedLinklyCloudSecret = result.Succeeded || HasSavedLinklyCloudSecret;
                }

                if (string.IsNullOrWhiteSpace(result.Message))
                {
                    var key = result.Succeeded
                        ? "settings.status.linklyCloudPaired"
                        : "settings.status.linklyCloudPairFailed";
                    SetLinklyTestStatus(key);
                    SetStatus(key);
                }
                else
                {
                    SetLinklyTestStatusOverride(result.Message);
                    SetStatusOverride(result.Message);
                }
            }
            catch (Exception ex)
            {
                SetLinklyTestStatusOverride(ex.Message);
                SetStatusOverride(ex.Message);
                throw;
            }
        });
    }

    private bool ValidateLinklyCloudPairingInput()
    {
        return ValidateLinklyCloudPairingInput(LinklyCloudPasswordText);
    }

    private bool ValidateLinklyCloudPairingInput(string password)
    {
        if (string.IsNullOrWhiteSpace(LinklyCloudUsernameText))
        {
            LogLinklyCloudSettings($"pair validation blocked environment={SelectedLinklyEnvironment} reason=missing-username");
            SetLinklyTestStatus("settings.linklyCloud.usernameRequired");
            SetStatus("settings.linklyCloud.usernameRequired");
            return false;
        }

        if (string.IsNullOrWhiteSpace(password) && !HasSavedLinklyCloudPassword)
        {
            LogLinklyCloudSettings($"pair validation blocked environment={SelectedLinklyEnvironment} reason=missing-password");
            SetLinklyTestStatus("settings.linklyCloud.passwordRequired");
            SetStatus("settings.linklyCloud.passwordRequired");
            return false;
        }

        if (string.IsNullOrWhiteSpace(LinklyPairCodeText))
        {
            LogLinklyCloudSettings($"pair validation blocked environment={SelectedLinklyEnvironment} reason=missing-pair-code");
            SetLinklyTestStatus("settings.linklyCloud.pairCodeRequired");
            SetStatus("settings.linklyCloud.pairCodeRequired");
            return false;
        }

        return true;
    }

    private async Task SaveLinklyCloudCredentialAsync()
    {
        await SaveLinklyCloudCredentialAsync(LinklyCloudPasswordText);
    }

    public async Task SaveLinklyCloudCredentialAsync(string password)
    {
        var credentialEnvironment = SelectedLinklyEnvironment;
        var syncBackendCredential = IsLinklyCloudBackendAsyncMode;
        var username = LinklyCloudUsernameText;
        await RunBusyAsync(async () =>
        {
            try
            {
                LogLinklyCloudSettings($"save credential clicked environment={credentialEnvironment} hasUsername={!string.IsNullOrWhiteSpace(username)} hasPassword=REDACTED");
                await _setupService.SaveLinklyCloudCredentialAsync(
                    credentialEnvironment,
                    username,
                    password,
                    syncBackendCredential: syncBackendCredential);
                if (SelectedLinklyEnvironment == credentialEnvironment)
                {
                    HasSavedLinklyCloudPassword = true;
                    LinklyCloudPasswordText = string.Empty;
                }

                LogLinklyCloudSettings($"save credential completed environment={credentialEnvironment} currentEnvironment={SelectedLinklyEnvironment} success=true");
                var statusKey = syncBackendCredential
                    ? "settings.status.linklyCloudCredentialSynced"
                    : "settings.status.linklyCloudCredentialSaved";
                SetLinklyTestStatus(statusKey);
                SetStatus(statusKey);
            }
            catch (Exception ex)
            {
                SetLinklyTestStatusOverride(ex.Message);
                SetStatusOverride(ex.Message);
                throw;
            }
        }, operationName: "save linkly cloud credential");
    }

    private void CancelLinklyCloudPairing()
    {
        // 取消配对时清空本地输入，避免旧凭据继续影响后续 Cloud API 操作。
        LogLinklyCloudSettings($"pair cancel clicked environment={SelectedLinklyEnvironment} hadPassword=REDACTED hadPairCode={!string.IsNullOrWhiteSpace(LinklyPairCodeText)}");
        LinklyPairCodeText = string.Empty;
        LinklyCloudPasswordText = string.Empty;
        _hasLinklyCloudPasswordInput = false;
        ResetLinklyConnectionTest();
        RaiseCommandStates();
    }

    private async Task SaveLinklyAsync()
    {
        if (!LinklyConnectionSucceeded)
        {
            SetStatus("settings.status.testLinklyBeforeSave");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var configuration = _loadedConfiguration with
            {
                Processor = CardProcessorKind.Linkly,
                Environment = SelectedLinklyEnvironment,
                LinklyConnectionMode = ToConnectionMode(PrimaryLinklyMode),
                LinklyConnectionModePriority = GetLinklyConnectionModePriority(),
                LinklyHost = NormalizeHost(LinklyHostText),
                LinklyPort = ParsePort(LinklyPortText),
                TerminalTimeoutSeconds = ParseTimeoutSeconds(TimeoutSecondsText),
                HasProtectedLinklyCloudSecret = HasSavedLinklyCloudSecret
            };

            if (IsLinklyCloudMode)
            {
                await _setupService.SaveLinklyCloudAsync(configuration);
            }
            else
            {
                await _setupService.SaveLinklyAsync(configuration);
            }

            _loadedConfiguration = configuration;
            SetStatus("settings.status.linklySaved");
        });
    }

    private async Task SaveReceiptPrinterAsync()
    {
        await _receiptPrinterSection.SaveAsync();
    }

    private async Task TestReceiptPrinterAsync()
    {
        await _receiptPrinterSection.TestAsync();
    }

    private async Task DownloadCatalogAsync(CancellationToken cancellationToken)
    {
        await _dataMaintenanceSection.DownloadCatalogAsync(cancellationToken);
    }

    private async Task ResetCatalogAsync(CancellationToken cancellationToken)
    {
        await _dataMaintenanceSection.ResetCatalogAsync(cancellationToken);
    }

    private async Task ResetTestSalesDataAsync(CancellationToken cancellationToken)
    {
        await _dataMaintenanceSection.ResetTestSalesDataAsync(cancellationToken);
    }

    private async Task ReregisterDeviceAsync()
    {
        await _dataMaintenanceSection.ReregisterDeviceAsync();
    }

    private bool CanLoadLocations()
    {
        return !IsBusy;
    }

    private bool CanLoadDevices()
    {
        return !IsBusy && SelectedSquareLocation is not null;
    }

    private bool CanSaveSquare()
    {
        return !IsBusy &&
            SelectedSquareLocation is not null &&
            SelectedSquareDevice is not null &&
            SquareLocations.Any(location => string.Equals(location.Id, SelectedSquareLocation.Id, StringComparison.OrdinalIgnoreCase)) &&
            SquareDevices.Any(device => string.Equals(device.Id, SelectedSquareDevice.Id, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(_devicesLoadedForLocationId, SelectedSquareLocation.Id, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanLoadDeviceCodes()
    {
        return !IsBusy && IsSquareDeviceCodesSupported && SelectedSquareLocation is not null;
    }

    private bool CanCreateDeviceCode()
    {
        return !IsBusy &&
            IsSquareDeviceCodesSupported &&
            SelectedSquareLocation is not null &&
            !string.IsNullOrWhiteSpace(SquareDeviceCodeNameText);
    }

    private bool CanRefreshDeviceCodeStatus()
    {
        return !IsBusy && IsSquareDeviceCodesSupported && SelectedSquareDeviceCode is not null;
    }

    private bool CanTestLinkly()
    {
        return !IsBusy && (PrimaryLinklyMode != LinklySettingsMode.CloudDirectSync || HasSavedLinklyCloudSecret);
    }

    private bool CanTestLinklyTransactionStatus()
    {
        return !IsBusy && IsLinklyCloudBackendAsyncMode;
    }

    private bool CanSaveLinkly()
    {
        return !IsBusy && LinklyConnectionSucceeded;
    }

    private bool CanPairLinklyCloud()
    {
        return !IsBusy && IsLinklyCloudMode;
    }

    private bool CanSaveLinklyCloudCredential()
    {
        return !IsBusy &&
            IsLinklyCloudMode &&
            !string.IsNullOrWhiteSpace(LinklyCloudUsernameText);
    }

    private bool CanCancelLinklyCloudPairing()
    {
        return !IsBusy &&
            IsLinklyCloudMode &&
            (!string.IsNullOrWhiteSpace(LinklyPairCodeText) ||
             _hasLinklyCloudPasswordInput ||
             !string.IsNullOrWhiteSpace(LinklyCloudPasswordText));
    }

    private bool CanSaveReceiptPrinter()
    {
        return _receiptPrinterSection.CanSave();
    }

    private bool CanTestReceiptPrinter()
    {
        return _receiptPrinterSection.CanTest();
    }

    private bool CanDownloadCatalog()
    {
        return _dataMaintenanceSection.CanDownloadCatalog();
    }

    private bool CanResetCatalog()
    {
        return _dataMaintenanceSection.CanResetCatalog();
    }

    private bool CanResetTestSalesData()
    {
        return _dataMaintenanceSection.CanResetTestSalesData();
    }

    private bool CanReregisterDevice()
    {
        return _dataMaintenanceSection.CanReregisterDevice();
    }

    private void ReturnToPos()
    {
        _returnToPos?.Invoke();
    }

    private async Task RunBusyAsync(Func<Task> action, string? operationName = null)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(operationName))
            {
                LogSquareSettings($"{operationName} canceled");
            }
            SetStatus("settings.status.operationCanceled");
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(operationName))
            {
                LogSquareSettings($"{operationName} failed message={LogValue(ex.Message)}");
            }
            SetStatusOverride(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(ScreenTitleText));
        OnPropertyChanged(nameof(CardTerminalTitleText));
        OnPropertyChanged(nameof(SettingsSubtitleText));
        OnPropertyChanged(nameof(DataMaintenanceTitleText));
        OnPropertyChanged(nameof(DeviceRegistrationTitleText));
        OnPropertyChanged(nameof(SquareTitleText));
        OnPropertyChanged(nameof(LinklyTitleText));
        OnPropertyChanged(nameof(LinklyCloudSecretStatusText));
        OnPropertyChanged(nameof(LinklyCloudCredentialStatusText));
        OnPropertyChanged(nameof(ReceiptPrinterTitleText));
        OnPropertyChanged(nameof(SquareTokenStatusText));
        OnPropertyChanged(nameof(SquareDeviceCodesUnavailableText));
        RefreshLocalizedMessages();
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RaiseLocalizedProperties();
    }

    partial void OnSelectedCategoryChanged(SettingsCategory value)
    {
        OnPropertyChanged(nameof(SettingsSubtitleText));
        OnPropertyChanged(nameof(IsDataMaintenanceSelected));
        OnPropertyChanged(nameof(IsPaymentTerminalSelected));
        OnPropertyChanged(nameof(IsReceiptPrinterSelected));
        OnPropertyChanged(nameof(IsDeviceRegistrationSelected));
    }

    private async Task LoadReceiptPrinterSettingsAsync()
    {
        await _receiptPrinterSection.LoadAsync();
    }

    private void ApplyReceiptPrinterSettings(ReceiptPrinterSettings settings)
    {
        ReceiptPrinterPortText = settings.PrinterPort;
        ReceiptBrandNameText = settings.BrandName;
        ReceiptStoreNameText = settings.StoreName;
        ReceiptStoreAddressText = settings.StoreAddress;
        ReceiptStorePhoneText = settings.StorePhone;
        ReceiptAbnText = settings.Abn;
        ReceiptReturnPolicyText = settings.ReturnPolicy;
    }

    private ReceiptPrinterSettings CreateReceiptPrinterSettingsFromFields()
    {
        return new ReceiptPrinterSettings(
            ReceiptPrinterPortText,
            ReceiptBrandNameText,
            ReceiptStoreNameText,
            ReceiptStoreAddressText,
            ReceiptStorePhoneText,
            ReceiptAbnText,
            ReceiptReturnPolicyText,
            ReceiptPrinterSettings.Default.CutDistance);
    }

    partial void OnIsSquareSandboxChanged(bool value)
    {
        LogSquareSettings($"square environment changed environment={SelectedSquareEnvironment}");
        SquareLocations.Clear();
        SquareDevices.Clear();
        ResetSquareDeviceCodes();
        _devicesLoadedForLocationId = null;
        SelectedSquareLocation = null;
        SelectedSquareDevice = null;
        // Square 和 Linkly 的编辑环境需要隔离，切换 Square 时不要清空 Linkly Cloud 输入。
        RaiseCommandStates();
        OnPropertyChanged(nameof(SelectedSquareEnvironment));
        OnPropertyChanged(nameof(IsSquareDeviceCodesSupported));
        OnPropertyChanged(nameof(IsSquareDeviceCodesUnsupported));
        OnPropertyChanged(nameof(SquareDeviceCodesUnavailableText));
    }

    partial void OnIsLinklySandboxChanged(bool value)
    {
        LogLinklyCloudSettings($"linkly environment changed environment={SelectedLinklyEnvironment}");
        ResetLinklyConnectionTest();
        LinklyCloudUsernameText = string.Empty;
        HasSavedLinklyCloudSecret = false;
        HasSavedLinklyCloudPassword = false;
        LinklyCloudPasswordText = string.Empty;
        LinklyPairCodeText = string.Empty;
        // Square 和 Linkly 的编辑环境需要隔离，切换 Linkly 时不要清空 Square 门店和设备列表。
        _ = RefreshLinklyCloudSecretStatusAsync(SelectedLinklyEnvironment);
        _ = RefreshLinklyCloudCredentialStatusAsync(SelectedLinklyEnvironment);
        RaiseCommandStates();
        OnPropertyChanged(nameof(SelectedLinklyEnvironment));
        OnPropertyChanged(nameof(SelectedEnvironment));
    }

    partial void OnHasSavedSquareTokenChanged(bool value)
    {
        RaiseCommandStates();
        OnPropertyChanged(nameof(SquareTokenStatusText));
    }

    partial void OnSelectedLinklyModeChanged(LinklySettingsMode value)
    {
        PromoteLinklyModeToPrimary(value);

        ResetLinklyConnectionTest();
        RaiseCommandStates();
        OnPropertyChanged(nameof(IsLinklyCloudMode));
        OnPropertyChanged(nameof(IsLinklyLocalMode));
        OnPropertyChanged(nameof(IsLinklyLocalIpMode));
        OnPropertyChanged(nameof(IsLinklyCloudDirectSyncMode));
        OnPropertyChanged(nameof(IsLinklyCloudBackendAsyncMode));
        OnPropertyChanged(nameof(IsLinklyStandardActionMode));
    }

    private void SelectLinklyPriorityMode(LinklyModePriorityItem? item)
    {
        if (item is null)
        {
            return;
        }

        // 选择某个模式时同步提升为主模式，保证测试和保存目标一致。
        SelectedLinklyMode = item.Mode;
    }

    private bool CanMoveLinklyPriorityUp(LinklyModePriorityItem? item)
    {
        return !IsBusy && item is not null && LinklyModePriorityItems.IndexOf(item) > 0;
    }

    private bool CanMoveLinklyPriorityDown(LinklyModePriorityItem? item)
    {
        return !IsBusy &&
            item is not null &&
            LinklyModePriorityItems.IndexOf(item) is var index &&
            index >= 0 &&
            index < LinklyModePriorityItems.Count - 1;
    }

    private void MoveLinklyPriorityUp(LinklyModePriorityItem? item)
    {
        if (item is null)
        {
            return;
        }

        MoveLinklyPriority(item, -1);
    }

    private void MoveLinklyPriorityDown(LinklyModePriorityItem? item)
    {
        if (item is null)
        {
            return;
        }

        MoveLinklyPriority(item, 1);
    }

    private void MoveLinklyPriority(LinklyModePriorityItem item, int offset)
    {
        var oldIndex = LinklyModePriorityItems.IndexOf(item);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= LinklyModePriorityItems.Count)
        {
            return;
        }

        LinklyModePriorityItems.Move(oldIndex, newIndex);
        RefreshLinklyPriorityRanks();
        SelectedLinklyMode = PrimaryLinklyMode;
        ResetLinklyConnectionTest();
        RaiseCommandStates();
        OnPropertyChanged(nameof(PrimaryLinklyMode));
    }

    partial void OnLinklyCloudUsernameTextChanged(string value)
    {
        Interlocked.Increment(ref _linklyCredentialEditVersion);
        ResetLinklyConnectionTest();
        RaiseCommandStates();
    }

    partial void OnLinklyCloudPasswordTextChanged(string value)
    {
        _hasLinklyCloudPasswordInput = !string.IsNullOrWhiteSpace(value);
        Interlocked.Increment(ref _linklyCredentialEditVersion);
        ResetLinklyConnectionTest();
        RaiseCommandStates();
    }

    partial void OnHasSavedLinklyCloudPasswordChanged(bool value)
    {
        RaiseCommandStates();
        OnPropertyChanged(nameof(LinklyCloudCredentialStatusText));
    }

    partial void OnLinklyPairCodeTextChanged(string value)
    {
        RaiseCommandStates();
    }

    partial void OnHasSavedLinklyCloudSecretChanged(bool value)
    {
        RaiseCommandStates();
        OnPropertyChanged(nameof(LinklyCloudSecretStatusText));
    }

    private async Task RefreshLinklyCloudSecretStatusAsync(CardTerminalEnvironment environment)
    {
        var version = Interlocked.Increment(ref _linklySecretStatusVersion);
        try
        {
            var hasSecret = await _setupService.HasLinklyCloudSecretAsync(environment);
            if (version == _linklySecretStatusVersion && SelectedLinklyEnvironment == environment)
            {
                HasSavedLinklyCloudSecret = hasSecret;
            }
        }
        catch (Exception ex)
        {
            LogSquareSettings($"refresh linkly cloud secret status failed environment={environment} message={LogValue(ex.Message)}");
        }
    }

    private static void ApplyLinklyCloudCredentialFields(
        LinklyCloudCredentialSettings credential,
        Action<string> setUsername,
        Action<string> setPassword,
        Action<bool> setHasSavedPassword)
    {
        setUsername(credential.Username ?? string.Empty);
        setPassword(string.Empty);
        setHasSavedPassword(credential.HasProtectedPassword);
    }

    private void ClearLinklyCloudCredentialFields()
    {
        LinklyCloudUsernameText = string.Empty;
        LinklyCloudPasswordText = string.Empty;
        HasSavedLinklyCloudPassword = false;
    }

    private async Task LoadLinklyCloudCredentialFieldsAsync(CardTerminalEnvironment environment)
    {
        try
        {
            var credential = await _setupService.LoadLinklyCloudCredentialAsync(environment);
            ApplyLinklyCloudCredentialFields(
                credential,
                username => LinklyCloudUsernameText = username,
                password => { },
                hasSavedPassword => HasSavedLinklyCloudPassword = hasSavedPassword);
        }
        catch (Exception ex)
        {
            LogSquareSettings($"load linkly cloud credential failed environment={environment} message={LogValue(ex.Message)}");
            ClearLinklyCloudCredentialFields();
        }
    }

    private async Task RefreshLinklyCloudCredentialStatusAsync(CardTerminalEnvironment environment)
    {
        var version = Interlocked.Increment(ref _linklyCredentialStatusVersion);
        var editVersion = _linklyCredentialEditVersion;
        try
        {
            var credential = await _setupService.LoadLinklyCloudCredentialAsync(environment);
            // 只有环境和编辑版本都未变化时，才回写异步加载结果，避免覆盖用户正在输入的内容。
            if (version == _linklyCredentialStatusVersion &&
                editVersion == _linklyCredentialEditVersion &&
                SelectedLinklyEnvironment == environment)
            {
                ApplyLinklyCloudCredentialFields(
                    credential,
                    username => LinklyCloudUsernameText = username,
                    password => { },
                    hasSavedPassword => HasSavedLinklyCloudPassword = hasSavedPassword);
            }
        }
        catch (Exception ex)
        {
            LogSquareSettings($"refresh linkly cloud credential status failed environment={environment} message={LogValue(ex.Message)}");
            if (version == _linklyCredentialStatusVersion &&
                editVersion == _linklyCredentialEditVersion &&
                SelectedLinklyEnvironment == environment)
            {
                ClearLinklyCloudCredentialFields();
            }
        }
    }

    partial void OnSelectedSquareLocationChanged(SquareLocationOption? value)
    {
        LogSquareSettings($"selected location changed locationId={LogValue(value?.Id)}");
        if (!string.Equals(_devicesLoadedForLocationId, value?.Id, StringComparison.OrdinalIgnoreCase))
        {
            SquareDevices.Clear();
            SelectedSquareDevice = null;
            _devicesLoadedForLocationId = null;
        }

        ResetSquareDeviceCodes();
        RaiseCommandStates();
    }

    partial void OnSelectedSquareDeviceChanged(SquareDeviceOption? value)
    {
        SuggestSquareDeviceCodeName(force: false);
        LogSquareSettings($"selected device changed deviceId={LogValue(value?.Id)}");
        if (!IsBusy &&
            value is not null &&
            SelectedSquareLocation is not null &&
            string.Equals(_devicesLoadedForLocationId, SelectedSquareLocation.Id, StringComparison.OrdinalIgnoreCase) &&
            !SquareDeviceIdNormalizer.AreEquivalent(value.Id, _savedSquareDeviceId))
        {
            SetStatus("settings.status.squareDeviceSwitchPendingSave", value.Name);
        }

        RaiseCommandStates();
    }

    partial void OnSelectedSquareDeviceCodeChanged(SquareDeviceCodeOption? value)
    {
        LogSquareSettings($"selected device code changed deviceCodeId={LogValue(value?.Id)} status={LogValue(value?.Status)}");
        RaiseCommandStates();
    }

    partial void OnSquareDeviceCodeNameTextChanged(string value)
    {
        RaiseCommandStates();
    }

    partial void OnLinklyHostTextChanged(string value)
    {
        ResetLinklyConnectionTest();
    }

    partial void OnLinklyPortTextChanged(string value)
    {
        ResetLinklyConnectionTest();
    }

    partial void OnTimeoutSecondsTextChanged(string value)
    {
        ResetLinklyConnectionTest();
    }

    partial void OnLinklyConnectionSucceededChanged(bool value)
    {
        RaiseCommandStates();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RaiseCommandStates();
        OnPropertyChanged(nameof(CanChangeEnvironment));
    }

    private void RaiseCommandStates()
    {
        LoadLocationsCommand.NotifyCanExecuteChanged();
        LoadDevicesCommand.NotifyCanExecuteChanged();
        SaveSquareCommand.NotifyCanExecuteChanged();
        LoadDeviceCodesCommand.NotifyCanExecuteChanged();
        CreateDeviceCodeCommand.NotifyCanExecuteChanged();
        RefreshDeviceCodeStatusCommand.NotifyCanExecuteChanged();
        PairLinklyCloudCommand.NotifyCanExecuteChanged();
        SaveLinklyCloudCredentialCommand.NotifyCanExecuteChanged();
        CancelLinklyCloudPairingCommand.NotifyCanExecuteChanged();
        TestLinklyCommand.NotifyCanExecuteChanged();
        TestLinklyTransactionStatusCommand.NotifyCanExecuteChanged();
        SaveLinklyCommand.NotifyCanExecuteChanged();
        MoveLinklyPriorityUpCommand.NotifyCanExecuteChanged();
        MoveLinklyPriorityDownCommand.NotifyCanExecuteChanged();
        SaveReceiptPrinterCommand.NotifyCanExecuteChanged();
        TestReceiptPrinterCommand.NotifyCanExecuteChanged();
        DownloadCatalogCommand.NotifyCanExecuteChanged();
        ResetCatalogCommand.NotifyCanExecuteChanged();
        ResetTestSalesDataCommand.NotifyCanExecuteChanged();
        ReregisterDeviceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSaveLinklyCloudCredentialFromView));
        OnPropertyChanged(nameof(CanPairLinklyCloudFromView));
        OnPropertyChanged(nameof(CanCancelLinklyCloudPairingFromView));
    }

    private void ResetLinklyConnectionTest()
    {
        LinklyConnectionSucceeded = false;
        ClearLinklyTestStatus();
    }

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusOverride = null;
        StatusMessage = Format(key, args);
    }

    private void SetStatusOverride(string statusText)
    {
        _statusOverride = statusText;
        StatusMessage = statusText;
    }

    private void SetLinklyTestStatus(string key, params object[] args)
    {
        _linklyTestStatusKey = key;
        _linklyTestStatusArgs = args;
        _linklyTestStatusOverride = null;
        LinklyTestStatusMessage = Format(key, args);
    }

    private void SetLinklyTestStatusOverride(string statusText)
    {
        _linklyTestStatusOverride = statusText;
        LinklyTestStatusMessage = statusText;
    }

    private void ClearLinklyTestStatus()
    {
        _linklyTestStatusKey = null;
        _linklyTestStatusArgs = [];
        _linklyTestStatusOverride = null;
        LinklyTestStatusMessage = string.Empty;
    }

    private void RefreshLocalizedMessages()
    {
        StatusMessage = _statusOverride ?? Format(_statusKey, _statusArgs);
        LinklyTestStatusMessage = _linklyTestStatusOverride
            ?? (_linklyTestStatusKey is null ? string.Empty : Format(_linklyTestStatusKey, _linklyTestStatusArgs));
        if (_receiptPrinterTestStatusOverride is not null)
        {
            ReceiptPrinterTestStatusMessage = _receiptPrinterTestStatusOverride;
        }
    }

    private string T(string key)
    {
        return _localization?.T(key) ?? LocalizationResourceProvider.Instance[key];
    }

    private string Format(string key, params object[] args)
    {
        var template = T(key);
        if (args.Length == 0)
        {
            return template;
        }

        var culture = _localization?.CurrentCulture ?? System.Globalization.CultureInfo.CurrentCulture;
        return string.Format(culture, template, args);
    }

    private static string NormalizeHost(string? host)
    {
        return string.IsNullOrWhiteSpace(host) ? CardTerminalConfiguration.Default.LinklyHost : host.Trim();
    }

    private static int ParsePort(string? text)
    {
        return int.TryParse(text, out var port) && port is > 0 and <= 65535
            ? port
            : CardTerminalConfiguration.Default.LinklyPort;
    }

    private static int ParseTimeoutSeconds(string? text)
    {
        return int.TryParse(text, out var seconds) && seconds > 0
            ? seconds
            : CardTerminalConfiguration.Default.TerminalTimeoutSeconds;
    }

    private static LinklySettingsMode ToSettingsMode(LinklyConnectionMode mode)
    {
        return CardTerminalSettings.NormalizeLinklyConnectionMode(mode) switch
        {
            LinklyConnectionMode.CloudDirectSync => LinklySettingsMode.CloudDirectSync,
            LinklyConnectionMode.CloudBackendAsync => LinklySettingsMode.CloudBackendAsync,
            _ => LinklySettingsMode.LocalIp
        };
    }

    private static LinklyConnectionMode ToConnectionMode(LinklySettingsMode mode)
    {
        return mode switch
        {
            LinklySettingsMode.CloudDirectSync => LinklyConnectionMode.CloudDirectSync,
            LinklySettingsMode.CloudBackendAsync => LinklyConnectionMode.CloudBackendAsync,
            _ => LinklyConnectionMode.LocalIp
        };
    }

    private void ResetLinklyModePriority(IReadOnlyList<LinklyConnectionMode>? priority)
    {
        LinklyModePriorityItems.Clear();
        var normalized = CardTerminalSettings.NormalizeLinklyConnectionModePriority(
            priority,
            _loadedConfiguration.LinklyConnectionMode);
        foreach (var mode in normalized)
        {
            LinklyModePriorityItems.Add(new LinklyModePriorityItem(ToSettingsMode(mode)));
        }

        RefreshLinklyPriorityRanks();
        OnPropertyChanged(nameof(PrimaryLinklyMode));
        MoveLinklyPriorityUpCommand.NotifyCanExecuteChanged();
        MoveLinklyPriorityDownCommand.NotifyCanExecuteChanged();
    }

    private void PromoteLinklyModeToPrimary(LinklySettingsMode mode)
    {
        var item = LinklyModePriorityItems.FirstOrDefault(candidate => candidate.Mode == mode);
        if (item is null)
        {
            return;
        }

        var index = LinklyModePriorityItems.IndexOf(item);
        if (index <= 0)
        {
            return;
        }

        // 选中某个模式后把它移动到第一位，让优先级顺序和当前主模式保持一致。
        LinklyModePriorityItems.Move(index, 0);
        RefreshLinklyPriorityRanks();
        OnPropertyChanged(nameof(PrimaryLinklyMode));
    }

    private IReadOnlyList<LinklyConnectionMode> GetLinklyConnectionModePriority()
    {
        return LinklyModePriorityItems
            .Select(item => ToConnectionMode(item.Mode))
            .ToArray();
    }

    private void RefreshLinklyPriorityRanks()
    {
        for (var index = 0; index < LinklyModePriorityItems.Count; index++)
        {
            LinklyModePriorityItems[index].Rank = index + 1;
        }

        MoveLinklyPriorityUpCommand.NotifyCanExecuteChanged();
        MoveLinklyPriorityDownCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadSquareDevicesForLocationAsync(string locationId, bool selectSavedDevice)
    {
        SquareDevices.ReplaceWith(await _setupService.ListSquareDevicesAsync(
            accessToken: null,
            SelectedSquareEnvironment,
            locationId));
        _devicesLoadedForLocationId = locationId;
        SelectedSquareDevice = selectSavedDevice
            ? SquareDevices.FirstOrDefault(device =>
                SquareDeviceIdNormalizer.AreEquivalent(device.Id, _savedSquareDeviceId))
            : SelectedSquareDevice is not null
                ? SquareDevices.FirstOrDefault(device =>
                    SquareDeviceIdNormalizer.AreEquivalent(device.Id, SelectedSquareDevice.Id))
                : null;
    }

    private void ResetSquareDeviceCodes()
    {
        SquareDeviceCodes.Clear();
        SelectedSquareDeviceCode = null;
        SuggestSquareDeviceCodeName(force: true);
    }

    private void ReplaceSquareDeviceCode(SquareDeviceCodeOption updated)
    {
        for (var index = 0; index < SquareDeviceCodes.Count; index++)
        {
            if (string.Equals(SquareDeviceCodes[index].Id, updated.Id, StringComparison.OrdinalIgnoreCase))
            {
                SquareDeviceCodes[index] = updated;
                return;
            }
        }

        SquareDeviceCodes.Insert(0, updated);
    }

    private void SuggestSquareDeviceCodeName(bool force)
    {
        var suggestion = SelectedSquareDevice?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            suggestion = DefaultSquareDeviceCodeName;
        }

        if (force ||
            string.IsNullOrWhiteSpace(SquareDeviceCodeNameText) ||
            string.Equals(SquareDeviceCodeNameText, _lastSquareDeviceCodeNameSuggestion, StringComparison.Ordinal))
        {
            SquareDeviceCodeNameText = suggestion;
        }

        _lastSquareDeviceCodeNameSuggestion = suggestion;
    }

    private static void LogSquareSettings(string message)
    {
        ConsoleLog.Write("Square", $"settings ui {message}");
    }

    private static void LogLinklyCloudSettings(string message)
    {
        LinklyJsonLog.WriteMessage("LinklyCloud", "settings-ui", $"settings ui {message}");
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value;
    }
}
