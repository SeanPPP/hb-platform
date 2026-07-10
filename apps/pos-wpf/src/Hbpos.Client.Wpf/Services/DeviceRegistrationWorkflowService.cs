using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Devices;

namespace Hbpos.Client.Wpf.Services;

public interface IDeviceRegistrationWorkflowService
{
    string GetHardwareId();

    Task<DeviceRegistrationLoadResult> LoadStoresAsync(
        LocalDeviceCache? cachedDevice,
        bool isReregisterMode,
        string? excludedStoreCode = null,
        CancellationToken cancellationToken = default);

    Task<DeviceRegistrationActionResult> RegisterAsync(
        StoreSelectionItem selectedStore,
        string hardwareId,
        CancellationToken cancellationToken = default);

    Task<DeviceRegistrationActionResult> VerifyAsync(
        StoreSelectionItem selectedStore,
        string deviceCode,
        string hardwareId,
        CancellationToken cancellationToken = default);

    Task<DeviceRegistrationActionResult> ReregisterAsync(
        StoreSelectionItem selectedStore,
        string hardwareId,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceRegistrationLoadResult(
    IReadOnlyList<StoreSelectionItem> Stores,
    StoreSelectionItem? SelectedStore,
    string DeviceCode,
    bool HasPendingRegistration,
    string StatusMessage);

public sealed record DeviceRegistrationActionResult(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    string HardwareId,
    bool HasPendingRegistration,
    string StatusMessage,
    string? AuthorizationCode,
    bool ShouldRaiseActivated,
    bool ShouldRaiseReregistered)
{
    internal Func<CancellationToken, Task>? PersistAsync { get; init; }
}

public sealed class DeviceRegistrationWorkflowService(
    IDeviceApiClient deviceApiClient,
    ILocalDeviceRepository deviceRepository,
    IDeviceFingerprintService fingerprintService,
    ILocalizationService? localization = null) : IDeviceRegistrationWorkflowService
{
    private const int PendingDeviceStatus = -1;

    public const string LoadingStoresMessage = "Loading stores...";

    public string GetHardwareId()
    {
        return fingerprintService.GetHardwareId();
    }

    public async Task<DeviceRegistrationLoadResult> LoadStoresAsync(
        LocalDeviceCache? cachedDevice,
        bool isReregisterMode,
        string? excludedStoreCode = null,
        CancellationToken cancellationToken = default)
    {
        var stores = await deviceApiClient.GetStoresAsync(cancellationToken);
        var visibleStores = stores
            .Where(store => CanShowStore(store, excludedStoreCode))
            .ToArray();

        if (cachedDevice is not null)
        {
            return new DeviceRegistrationLoadResult(
                visibleStores,
                visibleStores.FirstOrDefault(x => string.Equals(x.StoreCode, cachedDevice.StoreCode, StringComparison.OrdinalIgnoreCase))
                    ?? visibleStores.FirstOrDefault(),
                cachedDevice.DeviceCode,
                cachedDevice.DeviceStatus == PendingDeviceStatus,
                cachedDevice.Message ?? T("deviceRegistration.status.pendingApproval", "Device registration is pending approval."));
        }

        return new DeviceRegistrationLoadResult(
            visibleStores,
            visibleStores.FirstOrDefault(),
            string.Empty,
            false,
            isReregisterMode
                ? visibleStores.Length == 0
                    ? T("deviceRegistration.status.noReregisterStores", "No other active stores are available.")
                    : T("deviceRegistration.status.selectReregisterStore", "Select a new store and submit device reregistration.")
                : visibleStores.Length == 0
                    ? T("deviceRegistration.status.noStores", "No active stores are available.")
                    : T("deviceRegistration.status.selectStore", "Select a store and submit this register for approval."));
    }

    public async Task<DeviceRegistrationActionResult> RegisterAsync(
        StoreSelectionItem selectedStore,
        string hardwareId,
        CancellationToken cancellationToken = default)
    {
        var response = await deviceApiClient.RegisterAsync(
            new DeviceRegisterRequest(selectedStore.StoreCode, hardwareId, Environment.MachineName),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var result = CreateActionResult(
            response.DeviceCode,
            response.StoreCode,
            response.StoreName,
            response.DeviceStatus,
            response.IsAllowed,
            response.Message,
            response.AuthorizationCode,
            hardwareId,
            shouldRaiseReregistered: false);

        return ShouldSaveRegisterResponse(response)
            ? result with
            {
                PersistAsync = persistenceCancellationToken =>
                    deviceRepository.SaveAsync(response, hardwareId, persistenceCancellationToken)
            }
            : result;
    }

    public async Task<DeviceRegistrationActionResult> VerifyAsync(
        StoreSelectionItem selectedStore,
        string deviceCode,
        string hardwareId,
        CancellationToken cancellationToken = default)
    {
        var response = await deviceApiClient.VerifyAsync(
            new DeviceVerifyRequest(deviceCode, selectedStore.StoreCode, hardwareId, Environment.MachineName),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var result = CreateActionResult(
            response.DeviceCode,
            response.StoreCode,
            response.StoreName,
            response.DeviceStatus,
            response.IsAllowed,
            response.Message,
            response.AuthorizationCode,
            hardwareId,
            shouldRaiseReregistered: false);

        // 由调用方确认认证结果仍属于当前轮询会话后，才允许写入本地设备缓存。
        return result with
        {
            PersistAsync = persistenceCancellationToken =>
                deviceRepository.SaveAsync(response, hardwareId, persistenceCancellationToken)
        };
    }

    public async Task<DeviceRegistrationActionResult> ReregisterAsync(
        StoreSelectionItem selectedStore,
        string hardwareId,
        CancellationToken cancellationToken = default)
    {
        var response = await deviceApiClient.ReregisterAsync(
            new DeviceReregisterRequest(selectedStore.StoreCode, hardwareId, Environment.MachineName),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var shouldRaiseReregistered = IsAcceptedReregister(response);

        var result = CreateActionResult(
            response.DeviceCode,
            response.StoreCode,
            response.StoreName,
            response.DeviceStatus,
            response.IsAllowed,
            response.Message,
            response.AuthorizationCode,
            hardwareId,
            shouldRaiseReregistered);

        return shouldRaiseReregistered
            ? result with
            {
                PersistAsync = persistenceCancellationToken =>
                    deviceRepository.SaveAsync(response, hardwareId, persistenceCancellationToken)
            }
            : result;
    }

    private DeviceRegistrationActionResult CreateActionResult(
        string deviceCode,
        string storeCode,
        string storeName,
        int deviceStatus,
        bool isAllowed,
        string? message,
        string? authorizationCode,
        string hardwareId,
        bool shouldRaiseReregistered)
    {
        var statusMessage = message ?? (isAllowed
            ? T("deviceRegistration.status.enabled", "Device is enabled.")
            : T("deviceRegistration.status.pendingApproval", "Device registration is pending approval."));
        var shouldRaiseActivated = false;

        if (isAllowed)
        {
            if (string.IsNullOrWhiteSpace(authorizationCode))
            {
                statusMessage = T(
                    "deviceRegistration.status.missingAuthorization",
                    "Device authorization code was not returned. Please verify again.");
            }
            else
            {
                shouldRaiseActivated = true;
            }
        }

        return new DeviceRegistrationActionResult(
            deviceCode,
            storeCode,
            storeName,
            hardwareId,
            deviceStatus == PendingDeviceStatus,
            statusMessage,
            authorizationCode,
            shouldRaiseActivated,
            shouldRaiseReregistered);
    }

    private string T(string key, string fallback)
    {
        return localization?.T(key) ?? fallback;
    }

    private static bool IsAcceptedReregister(DeviceReregisterResponse response)
    {
        return response.DeviceStatus == PendingDeviceStatus
            && !string.IsNullOrWhiteSpace(response.DeviceCode)
            && !string.IsNullOrWhiteSpace(response.StoreCode);
    }

    private static bool ShouldSaveRegisterResponse(DeviceRegisterResponse response)
    {
        return response.DeviceStatus == PendingDeviceStatus
            || response.IsAllowed && !string.IsNullOrWhiteSpace(response.AuthorizationCode);
    }

    private static bool CanShowStore(StoreSelectionItem store, string? excludedStoreCode)
    {
        // 门店列表只允许启用分店，重新注册时再排除当前分店。
        return store.IsActive
            && (string.IsNullOrWhiteSpace(excludedStoreCode)
                || !string.Equals(store.StoreCode, excludedStoreCode, StringComparison.OrdinalIgnoreCase));
    }
}
