namespace Hbpos.Client.Wpf.Services;

public interface IScannerBindingService
{
    Task<string?> GetBoundDevicePathAsync(CancellationToken cancellationToken = default);

    Task SetBoundDevicePathAsync(string devicePath, CancellationToken cancellationToken = default);

    Task ClearBoundDevicePathAsync(CancellationToken cancellationToken = default);
}

public sealed class ScannerBindingService(ILocalAppSettingsRepository settingsRepository) : IScannerBindingService
{
    public const string DevicePathSettingKey = "RawScanner.DevicePath";

    public Task<string?> GetBoundDevicePathAsync(CancellationToken cancellationToken = default)
    {
        return settingsRepository.GetValueAsync(DevicePathSettingKey, cancellationToken);
    }

    public Task SetBoundDevicePathAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        return settingsRepository.SetValueAsync(DevicePathSettingKey, devicePath, cancellationToken);
    }

    public Task ClearBoundDevicePathAsync(CancellationToken cancellationToken = default)
    {
        return settingsRepository.DeleteValueAsync(DevicePathSettingKey, cancellationToken);
    }
}
