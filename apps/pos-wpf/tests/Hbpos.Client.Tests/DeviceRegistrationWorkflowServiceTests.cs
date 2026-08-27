using System.Net;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Devices;

namespace Hbpos.Client.Tests;

public sealed class DeviceRegistrationWorkflowServiceTests
{
    private const string ActivationCode = "HBDEV1-0123456789ABCDEFGHJKMNPQRS-6789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string ActivationEndpoint = "https://hotbargain.vip/pos-api/";

    [Fact]
    public async Task PreviewActivationCodeAsync_sends_windows_platform_without_writing_recovery()
    {
        var api = new FakeDeviceApiClient
        {
            PreviewResponse = new DeviceActivationCodePreviewResponse(
                true,
                null,
                "1002",
                "Lutwyche",
                DeviceSystems.Windows,
                DateTime.UtcNow.AddMinutes(15),
                "Ready")
        };
        var recoveryStore = new FakeActivationRecoveryStore();
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        var result = await service.PreviewActivationCodeAsync(ActivationCode);

        Assert.Equal("1002", result.StoreCode);
        Assert.Equal(DeviceSystems.Windows, result.DeviceSystem);
        Assert.Equal(ActivationCode, api.LastPreviewRequest?.ActivationCode);
        Assert.Equal(DeviceSystems.Windows, api.LastPreviewRequest?.DeviceSystem);
        Assert.Null(recoveryStore.Pending);
        Assert.Empty(recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_writes_recovery_before_api_and_clears_only_after_credentials()
    {
        var events = new List<string>();
        var recoveryStore = new FakeActivationRecoveryStore(events);
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = AllowedActivationResponse(),
            BeforeRedeem = () =>
            {
                Assert.NotNull(recoveryStore.Pending);
                events.Add("api");
            }
        };
        var repository = new FakeLocalDeviceRepository { Events = events };
        var service = new DeviceRegistrationWorkflowService(
            api,
            repository,
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        var result = await service.RedeemActivationCodeAsync(ActivationCode, "HW-001");

        Assert.NotNull(recoveryStore.Pending);
        Assert.Null(repository.LastRegisterResponse);
        Assert.Equal(["recovery", "api"], events);

        await result.PersistAsync!(CancellationToken.None);

        Assert.Equal(["recovery", "api", "credentials", "clear"], events);
        Assert.Null(recoveryStore.Pending);
        Assert.Equal("AUTH-001", repository.LastRegisterResponse?.AuthorizationCode);
        Assert.True(result.ShouldRaiseActivated);
    }

    [Fact]
    public async Task RebindActivationCodeAsync_falls_back_to_idempotent_redeem_when_old_device_is_unauthorized()
    {
        var repository = new FakeLocalDeviceRepository();
        var api = new FakeDeviceApiClient
        {
            RebindException = new CatalogApiException(
                "Old device disabled",
                HttpStatusCode.Unauthorized,
                "DEVICE_AUTH_REQUIRED"),
            RedeemResponse = RecoveredActivationResponse()
        };
        var recoveryStore = new FakeActivationRecoveryStore();
        var service = new DeviceRegistrationWorkflowService(
            api,
            repository,
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        var result = await service.RebindActivationCodeAsync(ActivationCode, "HW-001");

        Assert.Equal(ActivationCode, api.LastRebindRequest?.ActivationCode);
        Assert.Null(api.LastRedeemRequest);
        Assert.Equal(ActivationCode, api.LastRecoveryRedeemRequest?.ActivationCode);
        Assert.Equal("HW-001", api.LastRecoveryRedeemRequest?.HardwareId);
        Assert.Equal(DeviceSystems.Windows, api.LastRecoveryRedeemRequest?.DeviceSystem);
        Assert.Equal(DeviceActivationRecoveryMode.Rebind, recoveryStore.Pending?.Mode);
        Assert.True(result.IsActivationRebind);
        Assert.True(result.ShouldRaiseActivated);

        await result.PersistAsync!(CancellationToken.None);

        Assert.Equal("AUTH-001", repository.LastRegisterResponse?.AuthorizationCode);
        Assert.Null(recoveryStore.Pending);
    }

    [Fact]
    public async Task RecoverActivationCodeAsync_retries_authenticated_rebind_when_first_request_never_arrived()
    {
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = AllowedActivationResponse()
        };
        var recoveryStore = new FakeActivationRecoveryStore();
        recoveryStore.Seed(ActivationCode, DeviceActivationRecoveryMode.Rebind);
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        var recovered = await service.RecoverActivationCodeAsync("HW-001");

        Assert.NotNull(recovered);
        Assert.Equal(DeviceActivationRecoveryMode.Rebind, recovered!.Mode);
        Assert.NotNull(api.LastRebindRequest);
        Assert.Null(api.LastRedeemRequest);
        Assert.True(recovered.ActionResult.IsActivationRebind);
        Assert.NotNull(recoveryStore.Pending);
    }

    [Fact]
    public async Task RecoverActivationCodeAsync_rejects_changed_hardware_without_calling_api_or_clearing_recovery()
    {
        var api = new FakeDeviceApiClient();
        var recoveryStore = new FakeActivationRecoveryStore();
        recoveryStore.Seed(
            ActivationCode,
            DeviceActivationRecoveryMode.Redeem,
            hardwareId: "HW-ORIGINAL");
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-CHANGED"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<DeviceActivationRecoveryUnreadableException>(() =>
            service.RecoverActivationCodeAsync("HW-CHANGED"));

        Assert.Null(api.LastRedeemRequest);
        Assert.Null(api.LastRecoveryRedeemRequest);
        Assert.Null(api.LastRebindRequest);
        Assert.NotNull(recoveryStore.Pending);
        Assert.Empty(recoveryStore.Events);
    }

    [Fact]
    public async Task RecoverActivationCodeAsync_uses_anonymous_redeem_after_submitted_rebind_disabled_old_identity()
    {
        var api = new FakeDeviceApiClient
        {
            RebindException = new CatalogApiException(
                "Old device was disabled after rebind",
                HttpStatusCode.Unauthorized,
                "DEVICE_AUTH_REQUIRED"),
            RedeemResponse = RecoveredActivationResponse()
        };
        var recoveryStore = new FakeActivationRecoveryStore();
        recoveryStore.Seed(ActivationCode, DeviceActivationRecoveryMode.Rebind);
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        var recovered = await service.RecoverActivationCodeAsync("HW-001");

        Assert.NotNull(recovered);
        Assert.NotNull(api.LastRebindRequest);
        Assert.Null(api.LastRedeemRequest);
        Assert.Equal(ActivationCode, api.LastRecoveryRedeemRequest?.ActivationCode);
        Assert.Equal("HW-001", api.LastRecoveryRedeemRequest?.HardwareId);
        Assert.True(recovered!.ActionResult.IsActivationRebind);
        Assert.NotNull(recoveryStore.Pending);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_clears_recovery_after_deterministic_business_rejection()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = new DeviceActivationCodeRedeemResponse(
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                false,
                "Code unavailable",
                ReasonCode: DeviceActivationReasonCodes.NotAvailable)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.Null(recoveryStore.Pending);
        Assert.Equal(["recovery", "clear"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_clears_recovery_after_structural_http_400()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemException = new CatalogApiException(
                "Malformed activation request",
                HttpStatusCode.BadRequest,
                "VALIDATION_ERROR")
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.Null(recoveryStore.Pending);
        Assert.Equal(["recovery", "clear"], recoveryStore.Events);
    }

    [Fact]
    public async Task RebindActivationCodeAsync_clears_recovery_after_structural_http_400()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RebindException = new CatalogApiException(
                "Malformed activation request",
                HttpStatusCode.BadRequest,
                "VALIDATION_ERROR")
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RebindActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.Null(recoveryStore.Pending);
        Assert.Equal(["recovery", "clear"], recoveryStore.Events);
    }

    [Fact]
    public async Task RecoverActivationCodeAsync_clears_recovery_after_structural_http_400()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        recoveryStore.Seed(ActivationCode, DeviceActivationRecoveryMode.Redeem);
        var api = new FakeDeviceApiClient
        {
            RedeemException = new CatalogApiException(
                "Malformed activation request",
                HttpStatusCode.BadRequest,
                "VALIDATION_ERROR")
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RecoverActivationCodeAsync("HW-001"));

        Assert.Null(recoveryStore.Pending);
        Assert.Equal(["clear"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_keeps_recovery_for_http_500()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemException = new CatalogApiException(
                "Activation endpoint unavailable",
                HttpStatusCode.InternalServerError,
                "SERVER_ERROR")
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_keeps_recovery_for_unknown_http_200_business_denial()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = new DeviceActivationCodeRedeemResponse(
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                false,
                "Activation denied",
                ReasonCode: "FUTURE_DETERMINISTIC_DENIAL")
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_keeps_recovery_when_http_200_denial_has_no_reason()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = new DeviceActivationCodeRedeemResponse(
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                false,
                "Activation denied",
                ReasonCode: null)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_keeps_recovery_for_empty_http_200_api_result()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemException = new CatalogApiException(
                "Device API returned no data.",
                HttpStatusCode.OK,
                DeviceActivationReasonCodes.NotAvailable)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_keeps_recovery_for_incomplete_allowed_credentials()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = new DeviceActivationCodeRedeemResponse(
                "POS-001",
                "1002",
                "Lutwyche",
                1,
                true,
                "Enabled",
                AuthorizationCode: null,
                ReasonCode: DeviceActivationReasonCodes.Activated)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Theory]
    [InlineData("FUTURE_ACTIVATION_SUCCESS")]
    [InlineData(null)]
    public async Task RedeemActivationCodeAsync_keeps_recovery_for_unknown_or_missing_success_reason(
        string? reasonCode)
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var repository = new FakeLocalDeviceRepository();
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = AllowedActivationResponse(reasonCode)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            repository,
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Null(repository.LastRegisterResponse);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_keeps_recovery_when_network_result_is_uncertain()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemException = new HttpRequestException("Connection dropped after submit")
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RedeemActivationCodeAsync_keeps_recovery_for_retryable_http_auth_or_rate_limit_status(
        HttpStatusCode statusCode)
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RedeemException = new CatalogApiException(
                "Activation endpoint temporarily unavailable",
                statusCode,
                statusCode == HttpStatusCode.TooManyRequests ? "RATE_LIMITED" : "DEVICE_AUTH_REQUIRED")
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RedeemActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RedeemActivationCodeAsync_keeps_recovery_when_local_credential_persistence_fails()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var repository = new FakeLocalDeviceRepository
        {
            RegisterSaveException = new IOException("Local credential file is unavailable")
        };
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = AllowedActivationResponse()
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            repository,
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);
        var result = await service.RedeemActivationCodeAsync(ActivationCode, "HW-001");

        await Assert.ThrowsAsync<IOException>(() => result.PersistAsync!(CancellationToken.None));

        Assert.NotNull(recoveryStore.Pending);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RebindActivationCodeAsync_clears_recovery_when_anonymous_redeem_explicitly_rejects()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var api = new FakeDeviceApiClient
        {
            RebindException = new CatalogApiException(
                "Old device disabled",
                HttpStatusCode.Forbidden,
                "DEVICE_DISABLED"),
            RedeemResponse = new DeviceActivationCodeRedeemResponse(
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                false,
                "Code unavailable",
                ReasonCode: DeviceActivationReasonCodes.NotAvailable)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            new FakeLocalDeviceRepository(),
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RebindActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(api.LastRebindRequest);
        Assert.Null(api.LastRedeemRequest);
        Assert.NotNull(api.LastRecoveryRedeemRequest);
        Assert.Null(recoveryStore.Pending);
        Assert.Equal(["recovery", "clear"], recoveryStore.Events);
    }

    [Theory]
    [InlineData(DeviceActivationReasonCodes.Activated)]
    [InlineData(null)]
    public async Task RebindActivationCodeAsync_keeps_recovery_when_recovery_only_success_is_not_recovered(
        string? reasonCode)
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var repository = new FakeLocalDeviceRepository();
        var api = new FakeDeviceApiClient
        {
            RebindException = new CatalogApiException(
                "Old device disabled",
                HttpStatusCode.Unauthorized,
                "DEVICE_AUTH_REQUIRED"),
            RedeemResponse = AllowedActivationResponse(reasonCode)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            repository,
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RebindActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(api.LastRecoveryRedeemRequest);
        Assert.NotNull(recoveryStore.Pending);
        Assert.Null(repository.LastRegisterResponse);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task RebindActivationCodeAsync_keeps_recovery_when_recovered_response_has_incomplete_credentials()
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var repository = new FakeLocalDeviceRepository();
        var api = new FakeDeviceApiClient
        {
            RebindException = new CatalogApiException(
                "Old device disabled",
                HttpStatusCode.Forbidden,
                "DEVICE_DISABLED"),
            RedeemResponse = new DeviceActivationCodeRedeemResponse(
                "POS-001",
                "1002",
                "Lutwyche",
                1,
                true,
                "Enabled",
                AuthorizationCode: null,
                ReasonCode: DeviceActivationReasonCodes.ActivationRecovered)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            repository,
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RebindActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(api.LastRecoveryRedeemRequest);
        Assert.NotNull(recoveryStore.Pending);
        Assert.Null(repository.LastRegisterResponse);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Theory]
    [InlineData("FUTURE_REBIND_SUCCESS")]
    [InlineData(null)]
    public async Task RebindActivationCodeAsync_keeps_recovery_for_unknown_or_missing_authenticated_success_reason(
        string? reasonCode)
    {
        var recoveryStore = new FakeActivationRecoveryStore();
        var repository = new FakeLocalDeviceRepository();
        var api = new FakeDeviceApiClient
        {
            RedeemResponse = AllowedActivationResponse(reasonCode)
        };
        var service = new DeviceRegistrationWorkflowService(
            api,
            repository,
            new FakeFingerprintService("HW-001"),
            activationRecoveryStore: recoveryStore);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            service.RebindActivationCodeAsync(ActivationCode, "HW-001"));

        Assert.NotNull(api.LastRebindRequest);
        Assert.Null(api.LastRecoveryRedeemRequest);
        Assert.NotNull(recoveryStore.Pending);
        Assert.Null(repository.LastRegisterResponse);
        Assert.Equal(["recovery"], recoveryStore.Events);
    }

    [Fact]
    public async Task LoadStoresAsync_WithCachedDevice_SelectsCachedStoreAndPendingStatus()
    {
        var api = new FakeDeviceApiClient
        {
            Stores =
            [
                new StoreSelectionItem("1002", "Lutwyche", true),
                new StoreSelectionItem("1003", "Zillmere", true)
            ]
        };
        var service = new DeviceRegistrationWorkflowService(api, new FakeLocalDeviceRepository(), new FakeFingerprintService("HW-001"));
        var cached = new LocalDeviceCache("POS-001", "1003", "Zillmere", "HW-001", -1, false, null, DateTimeOffset.UtcNow);

        var result = await service.LoadStoresAsync(cached, isReregisterMode: false);

        Assert.Equal("POS-001", result.DeviceCode);
        Assert.True(result.HasPendingRegistration);
        Assert.Equal("Device registration is pending approval.", result.StatusMessage);
        Assert.Equal("1003", result.SelectedStore?.StoreCode);
        Assert.Equal(2, result.Stores.Count);
    }

    [Fact]
    public async Task LoadStoresAsync_WithRejectedCachedDevice_DoesNotMapToPendingStatus()
    {
        var api = new FakeDeviceApiClient
        {
            Stores = [new StoreSelectionItem("1003", "Zillmere", true)]
        };
        var service = new DeviceRegistrationWorkflowService(api, new FakeLocalDeviceRepository(), new FakeFingerprintService("HW-001"));
        var cached = new LocalDeviceCache("POS-OLD", "1002", "Lutwyche", "HW-001", 1, false, "Device hardware is already registered to another store.", DateTimeOffset.UtcNow);

        var result = await service.LoadStoresAsync(cached, isReregisterMode: false);

        Assert.False(result.HasPendingRegistration);
        Assert.Equal("POS-OLD", result.DeviceCode);
        Assert.Equal("1003", result.SelectedStore?.StoreCode);
    }

    [Fact]
    public async Task LoadStoresAsync_ReregisterMode_HidesCurrentAndInactiveStores()
    {
        var api = new FakeDeviceApiClient
        {
            Stores =
            [
                new StoreSelectionItem("1002", "Current", true),
                new StoreSelectionItem("1003", "Inactive Target", false),
                new StoreSelectionItem("1004", "Active Target", true)
            ]
        };
        var service = new DeviceRegistrationWorkflowService(api, new FakeLocalDeviceRepository(), new FakeFingerprintService("HW-001"));

        var result = await service.LoadStoresAsync(
            cachedDevice: null,
            isReregisterMode: true,
            excludedStoreCode: "1002");

        var store = Assert.Single(result.Stores);
        Assert.Equal("1004", store.StoreCode);
        Assert.Equal("1004", result.SelectedStore?.StoreCode);
    }

    [Fact]
    public async Task RegisterAsync_SavesResponseAndReturnsPendingResult()
    {
        var api = new FakeDeviceApiClient
        {
            RegisterResponse = new DeviceRegisterResponse("POS-001", "1002", "Lutwyche", -1, false, "Pending approval")
        };
        var repository = new FakeLocalDeviceRepository();
        var service = new DeviceRegistrationWorkflowService(api, repository, new FakeFingerprintService("HW-001"));

        var result = await service.RegisterAsync(new StoreSelectionItem("1002", "Lutwyche", true), "HW-001");

        Assert.Equal("1002", api.LastRegisterRequest?.StoreCode);
        Assert.Equal("HW-001", api.LastRegisterRequest?.HardwareId);
        Assert.Null(repository.LastRegisterResponse);
        await result.PersistAsync!(CancellationToken.None);
        Assert.NotNull(repository.LastRegisterResponse);
        Assert.Equal("HW-001", repository.LastHardwareId);
        Assert.Equal("POS-001", result.DeviceCode);
        Assert.True(result.HasPendingRegistration);
        Assert.Equal("Pending approval", result.StatusMessage);
        Assert.False(result.ShouldRaiseActivated);
    }

    [Fact]
    public async Task RegisterAsync_WhenResponseIsRejected_DoesNotMapToPendingRegistration()
    {
        var api = new FakeDeviceApiClient
        {
            RegisterResponse = new DeviceRegisterResponse(
                "POS-OLD",
                "1002",
                "Lutwyche",
                1,
                false,
                "Device hardware is already registered to another store.")
        };
        var repository = new FakeLocalDeviceRepository();
        var service = new DeviceRegistrationWorkflowService(api, repository, new FakeFingerprintService("HW-001"));

        var result = await service.RegisterAsync(new StoreSelectionItem("1003", "Zillmere", true), "HW-001");

        Assert.False(result.HasPendingRegistration);
        Assert.False(result.ShouldRaiseActivated);
        Assert.Equal("Device hardware is already registered to another store.", result.StatusMessage);
        Assert.Null(repository.LastRegisterResponse);
    }

    [Fact]
    public async Task VerifyAsync_WhenAuthorizationCodeIsMissing_ReturnsVerifyAgainMessage()
    {
        var api = new FakeDeviceApiClient
        {
            VerifyResponse = new DeviceVerifyResponse("POS-001", "1002", "Lutwyche", 1, true, "Device is enabled.", null)
        };
        var repository = new FakeLocalDeviceRepository();
        var service = new DeviceRegistrationWorkflowService(api, repository, new FakeFingerprintService("HW-001"));

        var result = await service.VerifyAsync(new StoreSelectionItem("1002", "Lutwyche", true), "POS-001", "HW-001");

        Assert.Null(repository.LastVerifyResponse);
        await result.PersistAsync!(CancellationToken.None);
        Assert.NotNull(repository.LastVerifyResponse);
        Assert.Equal("POS-001", api.LastVerifyRequest?.DeviceCode);
        Assert.Equal("1002", api.LastVerifyRequest?.StoreCode);
        Assert.False(result.HasPendingRegistration);
        Assert.Equal("Device authorization code was not returned. Please verify again.", result.StatusMessage);
        Assert.False(result.ShouldRaiseActivated);
    }

    [Fact]
    public async Task VerifyAsync_WhenCallerCancelsAfterApiReturns_ThrowsAndDoesNotSaveCache()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var api = new FakeDeviceApiClient
        {
            VerifyResponse = new DeviceVerifyResponse("POS-001", "1002", "Lutwyche", 1, true, "Device is enabled.", "AUTH-001"),
            VerifyCancellationSourceToCancel = cancellationTokenSource
        };
        var repository = new FakeLocalDeviceRepository();
        var service = new DeviceRegistrationWorkflowService(api, repository, new FakeFingerprintService("HW-001"));

        // 模拟 API 已经成功返回，但调用方在本地缓存写入前取消。
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.VerifyAsync(
            new StoreSelectionItem("1002", "Lutwyche", true),
            "POS-001",
            "HW-001",
            cancellationTokenSource.Token));

        Assert.Null(repository.LastVerifyResponse);
    }

    [Fact]
    public async Task ReregisterAsync_SavesOnlyAcceptedPendingResponses()
    {
        var api = new FakeDeviceApiClient
        {
            ReregisterResponse = new DeviceReregisterResponse("POS-NEW", "1003", "Zillmere", -1, false, "Pending approval")
        };
        var repository = new FakeLocalDeviceRepository();
        var service = new DeviceRegistrationWorkflowService(api, repository, new FakeFingerprintService("HW-001"));

        var accepted = await service.ReregisterAsync(new StoreSelectionItem("1003", "Zillmere", true), "HW-001");

        Assert.Null(repository.LastReregisterResponse);
        await accepted.PersistAsync!(CancellationToken.None);
        Assert.NotNull(repository.LastReregisterResponse);
        Assert.True(accepted.ShouldRaiseReregistered);
        Assert.Equal("Pending approval", accepted.StatusMessage);

        repository.Reset();
        api.ReregisterResponse = new DeviceReregisterResponse("POS-NEW", "1003", "Zillmere", 1, true, "Device is enabled.", "AUTH-001");

        var allowed = await service.ReregisterAsync(new StoreSelectionItem("1003", "Zillmere", true), "HW-001");

        Assert.Null(repository.LastReregisterResponse);
        Assert.False(allowed.ShouldRaiseReregistered);
        Assert.True(allowed.ShouldRaiseActivated);
    }

    private sealed class FakeDeviceApiClient : IDeviceApiClient
    {
        public IReadOnlyList<StoreSelectionItem> Stores { get; init; } = [];

        public DeviceRegisterResponse? RegisterResponse { get; init; }

        public DeviceVerifyResponse? VerifyResponse { get; init; }

        public DeviceReregisterResponse? ReregisterResponse { get; set; }

        public DeviceActivationCodePreviewResponse? PreviewResponse { get; init; }

        public DeviceActivationCodeRedeemResponse? RedeemResponse { get; init; }

        public CatalogApiException? RebindException { get; init; }

        public Exception? RedeemException { get; init; }

        public Action? BeforeRedeem { get; init; }

        public CancellationTokenSource? VerifyCancellationSourceToCancel { get; init; }

        public DeviceRegisterRequest? LastRegisterRequest { get; private set; }

        public DeviceVerifyRequest? LastVerifyRequest { get; private set; }

        public DeviceActivationCodePreviewRequest? LastPreviewRequest { get; private set; }

        public DeviceActivationCodeRedeemRequest? LastRedeemRequest { get; private set; }

        public DeviceActivationCodeRedeemRequest? LastRecoveryRedeemRequest { get; private set; }

        public DeviceActivationCodeRebindRequest? LastRebindRequest { get; private set; }

        public Task<IReadOnlyList<StoreSelectionItem>> GetStoresAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StoreSelectionItem>>(Stores);
        }

        public Task<DeviceRegisterResponse> RegisterAsync(
            DeviceRegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRegisterRequest = request;
            return Task.FromResult(RegisterResponse!);
        }

        public Task<DeviceVerifyResponse> VerifyAsync(
            DeviceVerifyRequest request,
            CancellationToken cancellationToken = default)
        {
            LastVerifyRequest = request;
            // 在返回响应前触发外部取消，稳定复现 await 之后的取消窗口。
            VerifyCancellationSourceToCancel?.Cancel();
            return Task.FromResult(VerifyResponse!);
        }

        public Task<DeviceReregisterResponse> ReregisterAsync(
            DeviceReregisterRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReregisterResponse!);
        }

        public Task<DeviceActivationCodePreviewResponse> PreviewActivationCodeAsync(
            DeviceActivationCodePreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPreviewRequest = request;
            return Task.FromResult(PreviewResponse!);
        }

        public Task<DeviceActivationCodeRedeemResponse> RedeemActivationCodeAsync(
            DeviceActivationCodeRedeemRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRedeemRequest = request;
            BeforeRedeem?.Invoke();
            return RedeemException is null
                ? Task.FromResult(RedeemResponse!)
                : Task.FromException<DeviceActivationCodeRedeemResponse>(RedeemException);
        }

        public Task<DeviceActivationCodeRedeemResponse> RedeemActivationCodeForRecoveryAsync(
            DeviceActivationCodeRedeemRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRecoveryRedeemRequest = request;
            BeforeRedeem?.Invoke();
            return RedeemException is null
                ? Task.FromResult(RedeemResponse!)
                : Task.FromException<DeviceActivationCodeRedeemResponse>(RedeemException);
        }

        public Task<DeviceActivationCodeRedeemResponse> RebindActivationCodeAsync(
            DeviceActivationCodeRebindRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRebindRequest = request;
            return RebindException is null
                ? Task.FromResult(RedeemResponse!)
                : Task.FromException<DeviceActivationCodeRedeemResponse>(RebindException);
        }
    }

    private sealed class FakeLocalDeviceRepository : ILocalDeviceRepository
    {
        public DeviceRegisterResponse? LastRegisterResponse { get; private set; }

        public DeviceVerifyResponse? LastVerifyResponse { get; private set; }

        public DeviceReregisterResponse? LastReregisterResponse { get; private set; }

        public string? LastHardwareId { get; private set; }

        public IList<string>? Events { get; init; }

        public Exception? RegisterSaveException { get; init; }

        public Task<LocalDeviceCache?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LocalDeviceCache?>(null);
        }

        public Task SaveAsync(DeviceRegisterResponse response, string hardwareId, CancellationToken cancellationToken = default)
        {
            if (RegisterSaveException is not null)
            {
                return Task.FromException(RegisterSaveException);
            }

            LastRegisterResponse = response;
            LastHardwareId = hardwareId;
            Events?.Add("credentials");
            return Task.CompletedTask;
        }

        public Task SaveAsync(DeviceVerifyResponse response, string hardwareId, CancellationToken cancellationToken = default)
        {
            LastVerifyResponse = response;
            LastHardwareId = hardwareId;
            return Task.CompletedTask;
        }

        public Task SaveAsync(DeviceReregisterResponse response, string hardwareId, CancellationToken cancellationToken = default)
        {
            LastReregisterResponse = response;
            LastHardwareId = hardwareId;
            return Task.CompletedTask;
        }

        public void Reset()
        {
            LastRegisterResponse = null;
            LastVerifyResponse = null;
            LastReregisterResponse = null;
            LastHardwareId = null;
        }
    }

    private sealed class FakeFingerprintService(string hardwareId) : IDeviceFingerprintService
    {
        public string GetHardwareId() => hardwareId;
    }

    private sealed class FakeActivationRecoveryStore : IDeviceActivationRecoveryStore
    {
        private readonly IList<string> _events;

        public FakeActivationRecoveryStore(IList<string>? events = null)
        {
            _events = events ?? new List<string>();
        }

        public DeviceActivationRecovery? Pending { get; private set; }

        public IReadOnlyList<string> Events => _events.ToArray();

        public void Seed(
            string activationCode,
            DeviceActivationRecoveryMode mode,
            string apiEndpoint = ActivationEndpoint,
            string hardwareId = "HW-001")
        {
            Pending = new DeviceActivationRecovery(
                activationCode,
                mode,
                apiEndpoint,
                hardwareId,
                DateTimeOffset.UtcNow);
        }

        public Task<DeviceActivationRecovery?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Pending);
        }

        public Task SaveAsync(
            string activationCode,
            DeviceActivationRecoveryMode mode,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            Pending = new DeviceActivationRecovery(
                activationCode,
                mode,
                ActivationEndpoint,
                hardwareId,
                DateTimeOffset.UtcNow);
            _events.Add("recovery");
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Pending = null;
            _events.Add("clear");
            return Task.CompletedTask;
        }
    }

    private static DeviceActivationCodeRedeemResponse AllowedActivationResponse(
        string? reasonCode = DeviceActivationReasonCodes.Activated)
    {
        return new DeviceActivationCodeRedeemResponse(
            "POS-001",
            "1002",
            "Lutwyche",
            1,
            true,
            "Enabled",
            "AUTH-001",
            reasonCode);
    }

    private static DeviceActivationCodeRedeemResponse RecoveredActivationResponse()
    {
        return AllowedActivationResponse(DeviceActivationReasonCodes.ActivationRecovered);
    }
}
