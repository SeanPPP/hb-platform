using System.Security.Cryptography;
using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class AppUpdateDeviceCredentialProviderTests
{
    [Fact]
    public async Task GetCredentialsAsync_without_cache_initializes_only_device_cache_and_returns_null()
    {
        var initializer = new FakeDeviceCacheInitializer();
        var provider = CreateProvider(initializer, latest: null);

        var result = await provider.GetCredentialsAsync();

        Assert.Null(result);
        Assert.Equal(1, initializer.InitializeCallCount);
    }

    [Fact]
    public async Task GetCredentialsAsync_uninitialized_device_cache_creates_only_required_schema_and_falls_back_to_legacy_check()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-update-cache-{Guid.NewGuid():N}.db");
        try
        {
            var store = new LocalSqliteStore(databasePath);
            var provider = new AppUpdateDeviceCredentialProvider(
                new AppStartupOptions([], PreviewMode: false, InitialScreen: null, InitialCulture: null),
                new AppUpdateDeviceCacheInitializer(store),
                new LocalDeviceRepository(store, new PassthroughProtector()),
                new StaticFingerprintService("HW-001"));

            var result = await provider.GetCredentialsAsync();

            Assert.Null(result);
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'DeviceCache';";
            Assert.Equal("DeviceCache", await command.ExecuteScalarAsync());
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task GetCredentialsAsync_decryption_failure_falls_back_to_legacy_check()
    {
        var provider = CreateProvider(
            new FakeDeviceCacheInitializer(),
            repository: new ThrowingDeviceRepository(new CryptographicException("cannot decrypt")));

        var result = await provider.GetCredentialsAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_preview_mode_never_reads_or_sends_device_credentials()
    {
        var initializer = new FakeDeviceCacheInitializer();
        var repository = new FakeDeviceRepository { Latest = CreateAllowedCache() };
        var provider = CreateProvider(initializer, repository, previewMode: true);

        var result = await provider.GetCredentialsAsync();

        Assert.Null(result);
        Assert.Equal(0, initializer.InitializeCallCount);
        Assert.Equal(0, repository.GetLatestCallCount);
    }

    [Fact]
    public async Task GetCredentialsAsync_regular_mode_uses_valid_cached_device_credentials()
    {
        var initializer = new FakeDeviceCacheInitializer();
        var repository = new FakeDeviceRepository { Latest = CreateAllowedCache() };
        var provider = new AppUpdateDeviceCredentialProvider(
            new AppStartupOptions([], PreviewMode: false, InitialScreen: null, InitialCulture: null),
            initializer,
            repository,
            new StaticFingerprintService("HW-001"));

        var result = await provider.GetCredentialsAsync();

        Assert.NotNull(result);
        Assert.Equal("POS-001", result.DeviceCode);
        Assert.Equal("AUTH-001", result.AuthorizationCode);
        Assert.Equal(1, initializer.InitializeCallCount);
        Assert.Equal(1, repository.GetLatestCallCount);
    }

    [Fact]
    public async Task GetCredentialsAsync_disabled_cache_falls_back_to_legacy_check()
    {
        var provider = CreateProvider(
            new FakeDeviceCacheInitializer(),
            latest: CreateAllowedCache() with { DeviceStatus = 3, IsAllowed = false });

        var result = await provider.GetCredentialsAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_hardware_mismatch_falls_back_to_legacy_check()
    {
        var provider = CreateProvider(
            new FakeDeviceCacheInitializer(),
            latest: CreateAllowedCache(hardwareId: "HW-CACHED"),
            currentHardwareId: "HW-CURRENT");

        var result = await provider.GetCredentialsAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_valid_cold_start_returns_cached_device_without_touching_global_authorization_state()
    {
        var provider = CreateProvider(new FakeDeviceCacheInitializer(), latest: CreateAllowedCache());

        var result = await provider.GetCredentialsAsync();

        Assert.NotNull(result);
        Assert.Equal("POS-001", result.DeviceCode);
        Assert.Equal("1002", result.StoreCode);
        Assert.Equal("HW-001", result.HardwareId);
        Assert.Equal("AUTH-001", result.AuthorizationCode);
    }

    private static AppUpdateDeviceCredentialProvider CreateProvider(
        IAppUpdateDeviceCacheInitializer initializer,
        ILocalDeviceRepository? repository = null,
        LocalDeviceCache? latest = null,
        bool previewMode = false,
        string currentHardwareId = "HW-001")
    {
        return new AppUpdateDeviceCredentialProvider(
            new AppStartupOptions([], previewMode, null, null),
            initializer,
            repository ?? new FakeDeviceRepository { Latest = latest },
            new StaticFingerprintService(currentHardwareId));
    }

    private static LocalDeviceCache CreateAllowedCache(string hardwareId = "HW-001") =>
        new("POS-001", "1002", "Lutwyche", hardwareId, 1, true, "enabled", DateTimeOffset.UtcNow, "AUTH-001");

    private sealed class StaticFingerprintService(string hardwareId) : IDeviceFingerprintService
    {
        public string GetHardwareId() => hardwareId;
    }

    private sealed class FakeDeviceCacheInitializer : IAppUpdateDeviceCacheInitializer
    {
        public int InitializeCallCount { get; private set; }

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceRepository : ILocalDeviceRepository
    {
        public LocalDeviceCache? Latest { get; init; }

        public int GetLatestCallCount { get; private set; }

        public Task<LocalDeviceCache?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            GetLatestCallCount++;
            return Task.FromResult(Latest);
        }

        public Task SaveAsync(Hbpos.Contracts.Devices.DeviceRegisterResponse response, string hardwareId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(Hbpos.Contracts.Devices.DeviceVerifyResponse response, string hardwareId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(Hbpos.Contracts.Devices.DeviceReregisterResponse response, string hardwareId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingDeviceRepository(Exception exception) : ILocalDeviceRepository
    {
        public Task<LocalDeviceCache?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromException<LocalDeviceCache?>(exception);

        public Task SaveAsync(Hbpos.Contracts.Devices.DeviceRegisterResponse response, string hardwareId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(Hbpos.Contracts.Devices.DeviceVerifyResponse response, string hardwareId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(Hbpos.Contracts.Devices.DeviceReregisterResponse response, string hardwareId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PassthroughProtector : IDeviceAuthorizationProtector
    {
        public string? Protect(string? value) => value;

        public string? Unprotect(string? protectedValue) => protectedValue;
    }
}
