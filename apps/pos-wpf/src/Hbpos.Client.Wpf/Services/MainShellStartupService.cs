using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Devices;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Hbpos.Client.Wpf.Services;

public sealed record MainShellStartupResult(
    PosSessionState Session,
    bool RequiresDeviceRegistration,
    LocalDeviceCache? CachedDevice);

public interface IMainShellStartupService
{
    Task<MainShellStartupResult> EvaluateAsync(
        PosSessionState session,
        bool previewMode,
        CancellationToken cancellationToken = default);

    Task<MainShellStartupResult> EvaluateAfterServerSwitchAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(session, previewMode: false, cancellationToken);

    void SetAuthorizedDevice(
        string deviceCode,
        string storeCode,
        string hardwareId,
        string authorizationCode);

    void ClearAuthorization();
}

public sealed class MainShellStartupService(
    ILocalDeviceRepository deviceRepository,
    IDeviceFingerprintService fingerprintService,
    DeviceAuthorizationState deviceAuthorizationState,
    IDeviceApiClient? deviceApiClient = null) : IMainShellStartupService
{
    public async Task<MainShellStartupResult> EvaluateAfterServerSwitchAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var cachedDevice = await deviceRepository.GetLatestAsync(cancellationToken);
        var hardwareId = fingerprintService.GetHardwareId();
        if (cachedDevice is null ||
            !cachedDevice.IsAllowed ||
            string.IsNullOrWhiteSpace(cachedDevice.AuthorizationCode) ||
            !string.Equals(cachedDevice.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase) ||
            deviceApiClient is null)
        {
            deviceAuthorizationState.Clear();
            return new MainShellStartupResult(session, true, cachedDevice);
        }

        DeviceVerifyResponse verification;
        try
        {
            verification = await deviceApiClient.VerifyAsync(
                new DeviceVerifyRequest(
                    cachedDevice.DeviceCode,
                    cachedDevice.StoreCode,
                    hardwareId,
                    Environment.MachineName),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CatalogApiException or JsonException)
        {
            // 热切换后禁止沿用旧服务器的离线授权；目标服务器 Verify 不成功即返回注册页。
            deviceAuthorizationState.Clear();
            ConsoleLog.Write(
                "DeviceServerSwitch",
                $"target verify failed; registration required error={ex.GetType().Name} message={ex.Message}");
            return new MainShellStartupResult(session, true, cachedDevice);
        }

        deviceAuthorizationState.Clear();
        await deviceRepository.SaveAsync(verification, hardwareId, cancellationToken);
        var verifiedDevice = CreateLocalDeviceCache(verification, hardwareId);
        if (verification.DeviceStatus != 1 ||
            !verification.IsAllowed ||
            string.IsNullOrWhiteSpace(verification.AuthorizationCode) ||
            !string.Equals(verification.DeviceCode, cachedDevice.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(verification.StoreCode, cachedDevice.StoreCode, StringComparison.OrdinalIgnoreCase))
        {
            return new MainShellStartupResult(session, true, verifiedDevice);
        }

        SetAuthorizedDevice(
            verification.DeviceCode,
            verification.StoreCode,
            hardwareId,
            verification.AuthorizationCode);
        return new MainShellStartupResult(
            session with
            {
                StoreCode = verification.StoreCode,
                StoreName = verification.StoreName,
                DeviceCode = verification.DeviceCode
            },
            false,
            verifiedDevice);
    }

    public async Task<MainShellStartupResult> EvaluateAsync(
        PosSessionState session,
        bool previewMode,
        CancellationToken cancellationToken = default)
    {
        if (previewMode)
        {
            deviceAuthorizationState.Clear();
            return new MainShellStartupResult(session, false, null);
        }

        var cachedDevice = await deviceRepository.GetLatestAsync(cancellationToken);
        var hardwareId = fingerprintService.GetHardwareId();
        if (cachedDevice is null ||
            !cachedDevice.IsAllowed ||
            string.IsNullOrWhiteSpace(cachedDevice.AuthorizationCode) ||
            !string.Equals(cachedDevice.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase))
        {
            deviceAuthorizationState.Clear();
            return new MainShellStartupResult(session, true, cachedDevice);
        }

        var startupDevice = cachedDevice;
        if (deviceApiClient is not null)
        {
            DeviceVerifyResponse? verification = null;
            try
            {
                verification = await deviceApiClient.VerifyAsync(
                    new DeviceVerifyRequest(
                        cachedDevice.DeviceCode,
                        cachedDevice.StoreCode,
                        hardwareId,
                        Environment.MachineName),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException ||
                ex is CatalogApiException apiException && IsRetryableDeviceApiStatus(apiException.StatusCode))
            {
                // 仅传输故障、超时、限流和服务端错误允许使用本地缓存继续离线营业。
                ConsoleLog.Write(
                    "DeviceStartup",
                    $"device authorization verify unavailable; using local cache error={ex.GetType().Name} message={ex.Message}");
            }
            catch (Exception ex) when (ex is CatalogApiException or JsonException)
            {
                // 确定性的鉴权或协议错误不能继续信任旧授权，持久化拒绝状态以防下次离线复活。
                var deniedVerification = new DeviceVerifyResponse(
                    cachedDevice.DeviceCode,
                    cachedDevice.StoreCode,
                    cachedDevice.StoreName,
                    3,
                    false,
                    ex.Message);
                deviceAuthorizationState.Clear();
                await deviceRepository.SaveAsync(deniedVerification, hardwareId, CancellationToken.None);
                return new MainShellStartupResult(
                    session,
                    true,
                    CreateLocalDeviceCache(deniedVerification, hardwareId));
            }

            if (verification is not null)
            {
                // 先清除旧内存授权，再持久化服务端结果；保存失败时绝不能恢复已失效的授权。
                deviceAuthorizationState.Clear();
                await deviceRepository.SaveAsync(verification, hardwareId, cancellationToken);
                startupDevice = new LocalDeviceCache(
                    verification.DeviceCode,
                    verification.StoreCode,
                    verification.StoreName,
                    hardwareId,
                    verification.DeviceStatus,
                    verification.IsAllowed,
                    verification.Message,
                    DateTimeOffset.UtcNow,
                    verification.AuthorizationCode);

                // 服务端是设备授权的最终依据；任何非启用、无授权码或身份不一致的响应都回到注册流程。
                if (verification.DeviceStatus != 1 ||
                    !verification.IsAllowed ||
                    string.IsNullOrWhiteSpace(verification.AuthorizationCode) ||
                    !string.Equals(verification.DeviceCode, cachedDevice.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(verification.StoreCode, cachedDevice.StoreCode, StringComparison.OrdinalIgnoreCase))
                {
                    return new MainShellStartupResult(session, true, startupDevice);
                }
            }
        }

        SetAuthorizedDevice(
            startupDevice.DeviceCode,
            startupDevice.StoreCode,
            startupDevice.HardwareId,
            startupDevice.AuthorizationCode!);

        return new MainShellStartupResult(
            session with
            {
                StoreCode = startupDevice.StoreCode,
                StoreName = startupDevice.StoreName,
                DeviceCode = startupDevice.DeviceCode
            },
            false,
            startupDevice);
    }

    public void SetAuthorizedDevice(
        string deviceCode,
        string storeCode,
        string hardwareId,
        string authorizationCode)
    {
        deviceAuthorizationState.Set(new DeviceAuthorizationContext(
            deviceCode,
            storeCode,
            hardwareId,
            authorizationCode));
    }

    public void ClearAuthorization()
    {
        deviceAuthorizationState.Clear();
    }

    private static bool IsRetryableDeviceApiStatus(HttpStatusCode? statusCode)
    {
        var numericStatus = (int?)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            numericStatus is >= 500 and <= 599;
    }

    private static LocalDeviceCache CreateLocalDeviceCache(DeviceVerifyResponse verification, string hardwareId)
    {
        return new LocalDeviceCache(
            verification.DeviceCode,
            verification.StoreCode,
            verification.StoreName,
            hardwareId,
            verification.DeviceStatus,
            verification.IsAllowed,
            verification.Message,
            DateTimeOffset.UtcNow,
            verification.AuthorizationCode);
    }
}
