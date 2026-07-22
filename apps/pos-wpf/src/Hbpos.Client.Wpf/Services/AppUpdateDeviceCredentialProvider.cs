using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public sealed record AppUpdateDeviceCredentials(
    string DeviceCode,
    string StoreCode,
    string HardwareId,
    string AuthorizationCode);

public interface IAppUpdateDeviceCredentialProvider
{
    Task<AppUpdateDeviceCredentials?> GetCredentialsAsync(
        CancellationToken cancellationToken = default);
}

public interface IAppUpdateDeviceCacheInitializer
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}

public sealed class AppUpdateDeviceCacheInitializer(LocalSqliteStore store) : IAppUpdateDeviceCacheInitializer
{
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS DeviceCache (
                DeviceCode TEXT PRIMARY KEY,
                StoreCode TEXT NOT NULL,
                StoreName TEXT NOT NULL,
                HardwareId TEXT NOT NULL DEFAULT '',
                DeviceStatus INTEGER NOT NULL DEFAULT 0,
                IsAllowed INTEGER NOT NULL,
                Message TEXT NULL,
                AuthorizationCodeProtected TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """, cancellationToken);

        var columns = await ReadColumnNamesAsync(connection, cancellationToken);
        if (!columns.Contains("HardwareId"))
        {
            await ExecuteAsync(connection, "ALTER TABLE DeviceCache ADD COLUMN HardwareId TEXT NOT NULL DEFAULT '';", cancellationToken);
        }

        if (!columns.Contains("DeviceStatus"))
        {
            await ExecuteAsync(connection, "ALTER TABLE DeviceCache ADD COLUMN DeviceStatus INTEGER NOT NULL DEFAULT 0;", cancellationToken);
        }

        if (!columns.Contains("Message"))
        {
            await ExecuteAsync(connection, "ALTER TABLE DeviceCache ADD COLUMN Message TEXT NULL;", cancellationToken);
        }

        if (!columns.Contains("AuthorizationCodeProtected"))
        {
            await ExecuteAsync(connection, "ALTER TABLE DeviceCache ADD COLUMN AuthorizationCodeProtected TEXT NULL;", cancellationToken);
        }
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(DeviceCache);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return result;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class AppUpdateDeviceCredentialProvider(
    AppStartupOptions startupOptions,
    IAppUpdateDeviceCacheInitializer cacheInitializer,
    ILocalDeviceRepository deviceRepository,
    IDeviceFingerprintService fingerprintService) : IAppUpdateDeviceCredentialProvider
{
    public async Task<AppUpdateDeviceCredentials?> GetCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        // 中文注释：仅演示启动使用临时数据；preview 发布渠道的正式机器仍需携带身份获取定向更新策略。
        if (startupOptions.PreviewMode)
        {
            return null;
        }

        try
        {
            // 中文注释：冷启动更新检查只建/读 DeviceCache，不能提前触发完整业务库迁移或全局授权状态写入。
            await cacheInitializer.EnsureInitializedAsync(cancellationToken);
            var cachedDevice = await deviceRepository.GetLatestAsync(cancellationToken);
            var currentHardwareId = fingerprintService.GetHardwareId().Trim();
            if (cachedDevice is null ||
                cachedDevice.DeviceStatus != 1 ||
                !cachedDevice.IsAllowed ||
                string.IsNullOrWhiteSpace(cachedDevice.DeviceCode) ||
                string.IsNullOrWhiteSpace(cachedDevice.StoreCode) ||
                string.IsNullOrWhiteSpace(cachedDevice.HardwareId) ||
                string.IsNullOrWhiteSpace(currentHardwareId) ||
                string.IsNullOrWhiteSpace(cachedDevice.AuthorizationCode) ||
                !string.Equals(cachedDevice.HardwareId.Trim(), currentHardwareId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new AppUpdateDeviceCredentials(
                cachedDevice.DeviceCode.Trim(),
                cachedDevice.StoreCode.Trim(),
                currentHardwareId,
                cachedDevice.AuthorizationCode.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 解密失败、旧库损坏或缓存读取失败均回退旧更新检查，且不能记录任何设备凭据。
            return null;
        }
    }
}
