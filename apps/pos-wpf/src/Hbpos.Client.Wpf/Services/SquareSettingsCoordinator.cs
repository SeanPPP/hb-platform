using System.Collections.ObjectModel;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Services;

/// <summary>
/// Mutable state bag read/written by <see cref="SquareSettingsCoordinator"/> during async operations.
/// SettingsViewModel syncs this back to [ObservableProperty] bindings after each operation completes.
/// </summary>
public sealed class SquareSettingsState
{
    public SquareLocationOption? SelectedLocation;
    public SquareDeviceOption? SelectedDevice;
    public SquareDeviceCodeOption? SelectedDeviceCode;
    public string DeviceCodeNameText = SettingsViewModel.DefaultSquareDeviceCodeName;
    public bool HasSavedToken;
    public string? SavedLocationId;
    public string? SavedDeviceId;
    public string? DevicesLoadedForLocationId;
    public string LastDeviceCodeNameSuggestion = SettingsViewModel.DefaultSquareDeviceCodeName;
    public CardTerminalConfiguration LoadedConfiguration = CardTerminalConfiguration.Default;
}

/// <summary>
/// Orchestrates Square terminal configuration operations (load locations, devices, device codes, save).
/// Keeps Square-specific logic out of SettingsViewModel while sharing state via <see cref="SquareSettingsState"/>.
/// </summary>
public sealed class SquareSettingsCoordinator
{
    private readonly ICardTerminalSetupService _setup;
    private readonly Func<CardTerminalEnvironment> _getEnvironment;
    private readonly Func<Func<Task>, string?, Task> _runBusyAsync;
    private readonly Action<string, object[]> _setStatus;
    private readonly Action<string> _setStatusOverride;
    private readonly Action _raiseCommandStates;

    public SquareSettingsCoordinator(
        ICardTerminalSetupService setup,
        Func<CardTerminalEnvironment> getEnvironment,
        Func<Func<Task>, string?, Task> runBusyAsync,
        Action<string, object[]> setStatus,
        Action<string> setStatusOverride,
        Action raiseCommandStates)
    {
        _setup = setup;
        _getEnvironment = getEnvironment;
        _runBusyAsync = runBusyAsync;
        _setStatus = setStatus;
        _setStatusOverride = setStatusOverride;
        _raiseCommandStates = raiseCommandStates;
    }

    public async Task LoadLocationsAsync(
        ObservableCollection<SquareLocationOption> locations,
        ObservableCollection<SquareDeviceOption> devices,
        ObservableCollection<SquareDeviceCodeOption> deviceCodes,
        SquareSettingsState state)
    {
        var env = _getEnvironment();
        Log($"load locations requested environment={env}");
        await _runBusyAsync(async () =>
        {
            locations.ReplaceWith(await _setup.ListSquareLocationsAsync(accessToken: null, env));
            devices.Clear();
            ResetDeviceCodes(deviceCodes, state);
            state.DevicesLoadedForLocationId = null;
            state.SelectedDevice = null;
            state.SelectedLocation = locations.FirstOrDefault(loc =>
                string.Equals(loc.Id, state.SavedLocationId, StringComparison.OrdinalIgnoreCase));
            if (env == CardTerminalEnvironment.Sandbox && state.SelectedLocation is null && locations.Count == 1)
            {
                state.SelectedLocation = locations[0];
            }

            if (env == CardTerminalEnvironment.Sandbox && state.SelectedLocation is not null)
            {
                // Sandbox 官方测试设备在设备加载链路中补齐；只加载 location 后也要立即触发，避免设备下拉为空。
                await LoadDevicesForLocationAsync(state.SelectedLocation.Id, selectSavedDevice: true, devices, state);
            }

            state.HasSavedToken = true;
            Log($"load locations succeeded environment={env} count={locations.Count} selectedLocationId={LogVal(state.SelectedLocation?.Id)} devicesCount={devices.Count} selectedDeviceId={LogVal(state.SelectedDevice?.Id)}");
            Status(locations.Count == 0 ? "settings.status.noSquareLocations" : "settings.status.squareLocationsLoaded", locations.Count);
        }, "load square locations");
    }

    public async Task LoadDevicesAsync(
        ObservableCollection<SquareDeviceOption> devices,
        SquareSettingsState state)
    {
        if (state.SelectedLocation is null)
        {
            Status("settings.status.selectSquareLocation");
            return;
        }

        var env = _getEnvironment();
        Log($"load devices requested environment={env} locationId={LogVal(state.SelectedLocation.Id)}");
        await _runBusyAsync(async () =>
        {
            await LoadDevicesForLocationAsync(state.SelectedLocation.Id, selectSavedDevice: true, devices, state);
            state.HasSavedToken = true;
            Log($"load devices succeeded environment={env} locationId={LogVal(state.SelectedLocation.Id)} count={devices.Count} selectedDeviceId={LogVal(state.SelectedDevice?.Id)}");
            Status(devices.Count == 0 ? "settings.status.noSquareDevices" : "settings.status.squareDevicesLoaded", devices.Count);
        }, "load square devices");
    }

    public async Task SaveAsync(
        ObservableCollection<SquareLocationOption> locations,
        ObservableCollection<SquareDeviceOption> devices,
        SquareSettingsState state,
        string linklyHostText,
        string linklyPortText,
        string timeoutSecondsText)
    {
        if (state.SelectedLocation is null) { Status("settings.status.selectSquareLocation"); return; }
        if (state.SelectedDevice is null) { Status("settings.status.selectSquareDevice"); return; }

        if (!locations.Any(l => string.Equals(l.Id, state.SelectedLocation.Id, StringComparison.OrdinalIgnoreCase)) ||
            !devices.Any(d => SquareDeviceIdNormalizer.AreEquivalent(d.Id, state.SelectedDevice.Id)) ||
            !string.Equals(state.DevicesLoadedForLocationId, state.SelectedLocation.Id, StringComparison.OrdinalIgnoreCase))
        {
            Status("settings.status.loadSquareBeforeSave");
            return;
        }

        var env = _getEnvironment();
        Log($"save square requested environment={env} locationId={LogVal(state.SelectedLocation.Id)} deviceId={LogVal(state.SelectedDevice.Id)}");
        await _runBusyAsync(async () =>
        {
            var savedDeviceId = SquareDeviceIdNormalizer.NormalizeForTerminalCheckout(state.SelectedDevice.Id);
            var config = new CardTerminalConfiguration(
                CardProcessorKind.Square, env,
                NormalizeHost(linklyHostText), ParsePort(linklyPortText),
                state.SelectedLocation.Id, savedDeviceId,
                state.HasSavedToken, ParseTimeoutSeconds(timeoutSecondsText));
            await _setup.SaveSquareAsync(config, squareAccessToken: null);
            state.SavedLocationId = config.SquareLocationId;
            state.SavedDeviceId = config.SquareDeviceId;
            state.HasSavedToken = config.HasProtectedSquareAccessToken;
            state.LoadedConfiguration = config;
            Log($"save square succeeded environment={env} locationId={LogVal(config.SquareLocationId)} selectedDeviceId={LogVal(state.SelectedDevice.Id)} savedDeviceId={LogVal(config.SquareDeviceId)}");
            Status("settings.status.squareSaved", state.SelectedDevice.Name);
        }, "save square settings");
    }

    public async Task LoadDeviceCodesAsync(
        ObservableCollection<SquareDeviceCodeOption> deviceCodes,
        SquareSettingsState state)
    {
        if (state.SelectedLocation is null) { Status("settings.status.selectSquareLocation"); return; }

        var env = _getEnvironment();
        Log($"load device codes requested environment={env} locationId={LogVal(state.SelectedLocation.Id)}");
        await _runBusyAsync(async () =>
        {
            deviceCodes.ReplaceWith(await _setup.ListSquareDeviceCodesAsync(accessToken: null, env, state.SelectedLocation.Id));
            state.SelectedDeviceCode = deviceCodes.FirstOrDefault(dc => SquareDeviceIdNormalizer.AreEquivalent(dc.DeviceId, state.SavedDeviceId));
            state.HasSavedToken = true;
            SuggestDeviceCodeName(force: false, state);
            Log($"load device codes succeeded environment={env} locationId={LogVal(state.SelectedLocation.Id)} count={deviceCodes.Count} selectedDeviceCodeId={LogVal(state.SelectedDeviceCode?.Id)}");
            Status("settings.status.squareDeviceCodesLoaded", deviceCodes.Count);
        }, "load square device codes");
    }

    public async Task CreateDeviceCodeAsync(
        ObservableCollection<SquareDeviceCodeOption> deviceCodes,
        SquareSettingsState state)
    {
        if (state.SelectedLocation is null) { Status("settings.status.selectSquareLocation"); return; }
        if (string.IsNullOrWhiteSpace(state.DeviceCodeNameText)) { Status("settings.status.squareDeviceCodeNameRequired"); return; }

        var env = _getEnvironment();
        Log($"create device code requested environment={env} locationId={LogVal(state.SelectedLocation.Id)} name={LogVal(state.DeviceCodeNameText.Trim())}");
        await _runBusyAsync(async () =>
        {
            var created = await _setup.CreateSquareDeviceCodeAsync(accessToken: null, env, state.SelectedLocation.Id, state.DeviceCodeNameText);
            deviceCodes.Insert(0, created);
            state.SelectedDeviceCode = created;
            state.HasSavedToken = true;
            Log($"create device code succeeded environment={env} locationId={LogVal(state.SelectedLocation.Id)} deviceCodeId={created.Id} status={created.Status}");
            Status("settings.status.squareDeviceCodeCreated", created.Code, created.Name);
        }, "create square device code");
    }

    public async Task RefreshDeviceCodeStatusAsync(
        ObservableCollection<SquareDeviceOption> devices,
        ObservableCollection<SquareDeviceCodeOption> deviceCodes,
        SquareSettingsState state)
    {
        if (state.SelectedDeviceCode is null) { Status("settings.status.selectSquareDeviceCode"); return; }

        var env = _getEnvironment();
        Log($"refresh device code requested environment={env} deviceCodeId={LogVal(state.SelectedDeviceCode.Id)}");
        await _runBusyAsync(async () =>
        {
            var refreshed = await _setup.GetSquareDeviceCodeAsync(accessToken: null, env, state.SelectedDeviceCode.Id);
            ReplaceDeviceCode(deviceCodes, refreshed);
            state.SelectedDeviceCode = refreshed;
            state.HasSavedToken = true;

            if (string.Equals(refreshed.Status, "PAIRED", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refreshed.DeviceId) && state.SelectedLocation is not null)
            {
                await LoadDevicesForLocationAsync(state.SelectedLocation.Id, selectSavedDevice: false, devices, state);
                state.SelectedDevice = devices.FirstOrDefault(d => SquareDeviceIdNormalizer.AreEquivalent(d.Id, refreshed.DeviceId));
                if (state.SelectedDevice is not null)
                {
                    Log($"refresh device code paired environment={env} deviceCodeId={refreshed.Id} squareDeviceId={LogVal(refreshed.DeviceId)} selectedDeviceId={LogVal(state.SelectedDevice.Id)}");
                    Status("settings.status.squareDeviceCodePaired", state.SelectedDevice.Name);
                    return;
                }
            }

            Log($"refresh device code completed environment={env} deviceCodeId={refreshed.Id} status={refreshed.Status} squareDeviceId={LogVal(refreshed.DeviceId)}");
            Status("settings.status.squareDeviceCodeNotPaired", refreshed.Status);
        }, "refresh square device code");
    }

    public async Task LoadDevicesForLocationAsync(
        string locationId,
        bool selectSavedDevice,
        ObservableCollection<SquareDeviceOption> devices,
        SquareSettingsState state)
    {
        devices.ReplaceWith(await _setup.ListSquareDevicesAsync(accessToken: null, _getEnvironment(), locationId));
        state.DevicesLoadedForLocationId = locationId;
        state.SelectedDevice = selectSavedDevice
            ? devices.FirstOrDefault(d => SquareDeviceIdNormalizer.AreEquivalent(d.Id, state.SavedDeviceId))
            : state.SelectedDevice is not null
                ? devices.FirstOrDefault(d => SquareDeviceIdNormalizer.AreEquivalent(d.Id, state.SelectedDevice.Id))
                : null;
    }

    public void ResetDeviceCodes(ObservableCollection<SquareDeviceCodeOption> deviceCodes, SquareSettingsState state)
    {
        deviceCodes.Clear();
        state.SelectedDeviceCode = null;
        SuggestDeviceCodeName(force: true, state);
    }

    public void SuggestDeviceCodeName(bool force, SquareSettingsState state)
    {
        var suggestion = state.SelectedDevice?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(suggestion))
            suggestion = SettingsViewModel.DefaultSquareDeviceCodeName;

        if (force || string.IsNullOrWhiteSpace(state.DeviceCodeNameText) ||
            string.Equals(state.DeviceCodeNameText, state.LastDeviceCodeNameSuggestion, StringComparison.Ordinal))
        {
            state.DeviceCodeNameText = suggestion;
        }

        state.LastDeviceCodeNameSuggestion = suggestion;
    }

    private static void ReplaceDeviceCode(ObservableCollection<SquareDeviceCodeOption> deviceCodes, SquareDeviceCodeOption updated)
    {
        for (var i = 0; i < deviceCodes.Count; i++)
        {
            if (string.Equals(deviceCodes[i].Id, updated.Id, StringComparison.OrdinalIgnoreCase))
            {
                deviceCodes[i] = updated;
                return;
            }
        }
        deviceCodes.Insert(0, updated);
    }

    // ── helpers ──

    private void Status(string key) => _setStatus(key, Array.Empty<object>());
    private void Status(string key, object a1) => _setStatus(key, new[] { a1 });
    private void Status(string key, object a1, object a2) => _setStatus(key, new[] { a1, a2 });

    private static void Log(string msg) => ConsoleLog.Write("Square", $"settings ui {msg}");
    private static string LogVal(string? v) => string.IsNullOrWhiteSpace(v) ? "<null>" : v;
    private static int ParsePort(string? t) => int.TryParse(t, out var p) && p is > 0 and <= 65535 ? p : CardTerminalConfiguration.Default.LinklyPort;
    private static int ParseTimeoutSeconds(string? t) => int.TryParse(t, out var s) && s > 0 ? s : CardTerminalConfiguration.Default.TerminalTimeoutSeconds;
    private static string NormalizeHost(string? h) => string.IsNullOrWhiteSpace(h) ? CardTerminalConfiguration.Default.LinklyHost : h.Trim();
}
