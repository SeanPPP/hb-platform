using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Devices;
using System.Net;
using System.Text.Json;

namespace Hbpos.Client.Tests;

public sealed class MainShellStartupServiceTests
{
    private static readonly PosSessionState StartupSession = new(
        "HB POS",
        "DEFAULT",
        "Default Store",
        "Terminal 04",
        "C001",
        "Alice",
        false,
        0);

    [Fact]
    public async Task EvaluateAsync_WithAuthorizedCachedDevice_AllowsOfflineStartupWithoutApi()
    {
        var authorizationState = new DeviceAuthorizationState();
        var repository = new FakeLocalDeviceRepository
        {
            Latest = CreateAllowedDevice("1042")
        };
        var service = new MainShellStartupService(
            repository,
            new FakeDeviceFingerprintService("HW-001"),
            authorizationState);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.False(result.RequiresDeviceRegistration);
        Assert.Equal("1042", result.Session.StoreCode);
        Assert.Equal("POS-001", result.Session.DeviceCode);
        Assert.Equal(1, repository.GetLatestCallCount);
        Assert.NotNull(authorizationState.Current);
        Assert.Equal("AUTH-001", authorizationState.Current.AuthorizationCode);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRemoteVerifyReturnsUnregistered_RequiresRegistrationAndClearsAuthorization()
    {
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-OLD", "1001", "HW-001", "AUTH-OLD"));
        var verification = new DeviceVerifyResponse(
            "POS-001",
            "1042",
            "Main Store",
            3,
            false,
            "Device is not registered.");
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromResult(verification)
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.True(result.RequiresDeviceRegistration);
        Assert.Null(authorizationState.Current);
        Assert.Equal("POS-001", apiClient.LastVerifyRequest?.DeviceCode);
        Assert.Equal("1042", apiClient.LastVerifyRequest?.StoreCode);
        Assert.Equal("HW-001", apiClient.LastVerifyRequest?.HardwareId);
        Assert.Same(verification, repository.SavedVerifyResponse);
        Assert.Equal("HW-001", repository.SavedVerifyHardwareId);
        Assert.Equal(3, repository.SavedVerifyResponse?.DeviceStatus);
        Assert.False(repository.SavedVerifyResponse?.IsAllowed);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRemoteVerifyReturnsEnabled_UsesServerAuthorizationAndStoreName()
    {
        var authorizationState = new DeviceAuthorizationState();
        var verification = new DeviceVerifyResponse(
            "POS-001",
            "1042",
            "Verified Store",
            1,
            true,
            "Device is enabled.",
            "AUTH-SERVER");
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromResult(verification)
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.False(result.RequiresDeviceRegistration);
        Assert.Equal("Verified Store", result.Session.StoreName);
        Assert.Equal("AUTH-SERVER", authorizationState.Current?.AuthorizationCode);
        Assert.Same(verification, repository.SavedVerifyResponse);
        Assert.Equal("HW-001", repository.SavedVerifyHardwareId);
        Assert.Equal("AUTH-SERVER", repository.SavedVerifyResponse?.AuthorizationCode);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRemoteVerifyTransportFails_AllowsOfflineStartupFromCache()
    {
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-OLD", "1001", "HW-001", "AUTH-OLD"));
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromException<DeviceVerifyResponse>(
                new HttpRequestException("Device API is temporarily unavailable."))
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.False(result.RequiresDeviceRegistration);
        Assert.Equal("Main Store", result.Session.StoreName);
        Assert.Equal("AUTH-001", authorizationState.Current?.AuthorizationCode);
        Assert.Null(repository.SavedVerifyResponse);
    }

    [Fact]
    public async Task EvaluateAfterServerSwitchAsync_WhenRemoteVerifyFails_RequiresRegistrationWithoutOfflineFallback()
    {
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-OLD", "1001", "HW-001", "AUTH-OLD"));
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromException<DeviceVerifyResponse>(
                new HttpRequestException("Target server verify unavailable."))
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAfterServerSwitchAsync(StartupSession, CancellationToken.None);

        Assert.True(result.RequiresDeviceRegistration);
        Assert.Same(repository.Latest, result.CachedDevice);
        Assert.Null(authorizationState.Current);
    }

    [Fact]
    public async Task EvaluateAsync_WhenDeniedResponseSaveFails_ClearsAuthorizationAndPropagatesFailure()
    {
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-001", "1042", "HW-001", "AUTH-OLD"));
        var saveFailure = new InvalidOperationException("Device cache write failed.");
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromResult(new DeviceVerifyResponse(
                "POS-001",
                "1042",
                "Main Store",
                3,
                false,
                "Device is not registered."))
        };
        var repository = new FakeLocalDeviceRepository
        {
            Latest = CreateAllowedDevice("1042"),
            VerifySaveException = saveFailure
        };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EvaluateAsync(StartupSession, previewMode: false));

        Assert.Same(saveFailure, exception);
        Assert.Null(authorizationState.Current);
    }

    [Fact]
    public async Task EvaluateAsync_WhenEnabledResponseSaveFails_ClearsAuthorizationAndPropagatesFailure()
    {
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-001", "1042", "HW-001", "AUTH-OLD"));
        var saveFailure = new InvalidOperationException("Device cache write failed.");
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromResult(new DeviceVerifyResponse(
                "POS-001",
                "1042",
                "Verified Store",
                1,
                true,
                "Device is enabled.",
                "AUTH-SERVER"))
        };
        var repository = new FakeLocalDeviceRepository
        {
            Latest = CreateAllowedDevice("1042"),
            VerifySaveException = saveFailure
        };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EvaluateAsync(StartupSession, previewMode: false));

        Assert.Same(saveFailure, exception);
        Assert.Null(authorizationState.Current);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task EvaluateAsync_WhenRemoteVerifyReturnsAuthorizationFailure_PersistsDeniedStateAndRequiresRegistration(
        HttpStatusCode statusCode)
    {
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-001", "1042", "HW-001", "AUTH-001"));
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromException<DeviceVerifyResponse>(
                new CatalogApiException("Device authorization was rejected.", statusCode))
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.True(result.RequiresDeviceRegistration);
        Assert.Null(authorizationState.Current);
        Assert.Equal(3, repository.SavedVerifyResponse?.DeviceStatus);
        Assert.False(repository.SavedVerifyResponse?.IsAllowed);
        Assert.Null(repository.SavedVerifyResponse?.AuthorizationCode);
        Assert.Equal("Device authorization was rejected.", repository.SavedVerifyResponse?.Message);
        Assert.Equal("HW-001", repository.SavedVerifyHardwareId);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRemoteVerifyReturnsServerError_AllowsOfflineStartupWithoutSaving()
    {
        var authorizationState = new DeviceAuthorizationState();
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromException<DeviceVerifyResponse>(
                new CatalogApiException("Temporary server failure.", HttpStatusCode.InternalServerError))
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.False(result.RequiresDeviceRegistration);
        Assert.Equal("AUTH-001", authorizationState.Current?.AuthorizationCode);
        Assert.Null(repository.SavedVerifyResponse);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRemoteVerifyResponseIsInvalid_PersistsDeniedStateAndRequiresRegistration()
    {
        var authorizationState = new DeviceAuthorizationState();
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) => Task.FromException<DeviceVerifyResponse>(
                new JsonException("Device API returned invalid JSON."))
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.True(result.RequiresDeviceRegistration);
        Assert.Null(authorizationState.Current);
        Assert.Equal(3, repository.SavedVerifyResponse?.DeviceStatus);
        Assert.False(repository.SavedVerifyResponse?.IsAllowed);
        Assert.Null(repository.SavedVerifyResponse?.AuthorizationCode);
        Assert.Equal("Device API returned invalid JSON.", repository.SavedVerifyResponse?.Message);
    }

    [Fact]
    public async Task EvaluateAsync_WhenDeterministicFailureCancelsCallerDuringVerify_PersistsDeniedStateWithoutCallerToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-001", "1042", "HW-001", "AUTH-OLD"));
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, _) =>
            {
                cancellationSource.Cancel();
                return Task.FromException<DeviceVerifyResponse>(new CatalogApiException(
                    "Device authorization was rejected.",
                    HttpStatusCode.Unauthorized));
            }
        };
        var repository = new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") };
        var service = CreateServiceWithApi(authorizationState, apiClient, repository);

        var result = await service.EvaluateAsync(
            StartupSession,
            previewMode: false,
            cancellationSource.Token);

        Assert.True(result.RequiresDeviceRegistration);
        Assert.Null(authorizationState.Current);
        Assert.NotNull(repository.SavedVerifyCancellationToken);
        Assert.NotEqual(cancellationSource.Token, repository.SavedVerifyCancellationToken.Value);
        Assert.False(repository.SavedVerifyCancellationToken.Value.IsCancellationRequested);
        Assert.Equal(3, repository.SavedVerifyResponse?.DeviceStatus);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRemoteVerifyIsCancelled_PropagatesCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var authorizationState = new DeviceAuthorizationState();
        var apiClient = new FakeDeviceApiClient
        {
            VerifyAsyncHandler = (_, cancellationToken) => Task.FromCanceled<DeviceVerifyResponse>(cancellationToken)
        };
        var service = CreateServiceWithApi(authorizationState, apiClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EvaluateAsync(
            StartupSession,
            previewMode: false,
            cancellationSource.Token));
    }

    [Theory]
    [MemberData(nameof(InvalidCachedDevices))]
    public async Task EvaluateAsync_WithMissingOrInvalidCachedDevice_RequiresRegistration(LocalDeviceCache? cachedDevice)
    {
        var authorizationState = new DeviceAuthorizationState();
        var service = new MainShellStartupService(
            new FakeLocalDeviceRepository { Latest = cachedDevice },
            new FakeDeviceFingerprintService("HW-001"),
            authorizationState);

        var result = await service.EvaluateAsync(StartupSession, previewMode: false);

        Assert.True(result.RequiresDeviceRegistration);
        Assert.Same(cachedDevice, result.CachedDevice);
        Assert.Null(authorizationState.Current);
    }

    public static IEnumerable<object?[]> InvalidCachedDevices()
    {
        yield return [null];
        yield return [CreateAllowedDevice("1042") with { IsAllowed = false }];
        yield return [CreateAllowedDevice("1042") with { AuthorizationCode = null }];
        yield return [CreateAllowedDevice("1042") with { HardwareId = "HW-OTHER" }];
    }

    private static LocalDeviceCache CreateAllowedDevice(string storeCode)
    {
        return new LocalDeviceCache(
            "POS-001",
            storeCode,
            "Main Store",
            "HW-001",
            1,
            true,
            null,
            DateTimeOffset.UtcNow,
            "AUTH-001");
    }

    private static MainShellStartupService CreateServiceWithApi(
        DeviceAuthorizationState authorizationState,
        IDeviceApiClient apiClient,
        FakeLocalDeviceRepository? repository = null)
    {
        return new MainShellStartupService(
            repository ?? new FakeLocalDeviceRepository { Latest = CreateAllowedDevice("1042") },
            new FakeDeviceFingerprintService("HW-001"),
            authorizationState,
            apiClient);
    }

    private sealed class FakeLocalDeviceRepository : ILocalDeviceRepository
    {
        public LocalDeviceCache? Latest { get; init; }

        public int GetLatestCallCount { get; private set; }

        public DeviceVerifyResponse? SavedVerifyResponse { get; private set; }

        public string? SavedVerifyHardwareId { get; private set; }

        public CancellationToken? SavedVerifyCancellationToken { get; private set; }

        public Exception? VerifySaveException { get; init; }

        public Task<LocalDeviceCache?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            GetLatestCallCount++;
            return Task.FromResult(Latest);
        }

        public Task SaveAsync(DeviceRegisterResponse response, string hardwareId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("启动评估不应写入设备缓存。");
        }

        public Task SaveAsync(DeviceVerifyResponse response, string hardwareId, CancellationToken cancellationToken = default)
        {
            SavedVerifyCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (VerifySaveException is not null)
            {
                return Task.FromException(VerifySaveException);
            }

            SavedVerifyResponse = response;
            SavedVerifyHardwareId = hardwareId;
            return Task.CompletedTask;
        }

        public Task SaveAsync(DeviceReregisterResponse response, string hardwareId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("启动评估不应写入设备缓存。");
        }
    }

    private sealed class FakeDeviceFingerprintService(string hardwareId) : IDeviceFingerprintService
    {
        public string GetHardwareId() => hardwareId;
    }

    private sealed class FakeDeviceApiClient : IDeviceApiClient
    {
        public required Func<DeviceVerifyRequest, CancellationToken, Task<DeviceVerifyResponse>> VerifyAsyncHandler { get; init; }

        public DeviceVerifyRequest? LastVerifyRequest { get; private set; }

        public Task<IReadOnlyList<StoreSelectionItem>> GetStoresAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DeviceRegisterResponse> RegisterAsync(
            DeviceRegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DeviceVerifyResponse> VerifyAsync(
            DeviceVerifyRequest request,
            CancellationToken cancellationToken = default)
        {
            LastVerifyRequest = request;
            return VerifyAsyncHandler(request, cancellationToken);
        }

        public Task<DeviceReregisterResponse> ReregisterAsync(
            DeviceReregisterRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
