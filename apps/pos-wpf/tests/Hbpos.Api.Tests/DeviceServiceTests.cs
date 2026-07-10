using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;

namespace Hbpos.Api.Tests;

public sealed class DeviceServiceTests
{
    [Fact]
    public void FindLatestByHardwareIdAndStoreCodeSql_LocksTargetRangeAndSelectsLatestRecord()
    {
        var sql = SqlSugarDeviceRegistrationRepository.FindLatestByHardwareIdAndStoreCodeSql;

        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[设备硬件识别码] = @HardwareId", sql, StringComparison.Ordinal);
        Assert.Contains("[分店代码] = @StoreCode", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [ID] DESC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetRegistrationForReregisterSql_UsesSnapshotConcurrencyConditions()
    {
        var sql = SqlSugarDeviceRegistrationRepository.ResetRegistrationForReregisterSql;

        Assert.Contains("[ID] = @RegistrationId", sql, StringComparison.Ordinal);
        Assert.Contains("[设备硬件识别码] = @HardwareId", sql, StringComparison.Ordinal);
        Assert.Contains("[分店代码] = @StoreCode", sql, StringComparison.Ordinal);
        Assert.Contains("[系统设备编号] = @DeviceCode", sql, StringComparison.Ordinal);
        Assert.Contains("[设备状态] = @ExpectedDeviceStatus", sql, StringComparison.Ordinal);
        Assert.Contains("[设备授权码] = @ExpectedAuthorizationCode", sql, StringComparison.Ordinal);
        Assert.Contains("[设备授权码] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("@ExpectedAuthorizationCode IS NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void FindAllByHardwareIdForRegistrationSql_LocksWholeHardwareRange()
    {
        var sql = SqlSugarDeviceRegistrationRepository.FindAllByHardwareIdForRegistrationSql;

        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[设备硬件识别码] = @HardwareId", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [ID] DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@StoreCode", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task RegisterAsync_WhenTargetHasAnonymousReusableStatus_ReusesDeviceAndRefreshesAuthorization(int targetStatus)
    {
        var now = new DateTime(2026, 7, 10, 11, 0, 0);
        var target = new DeviceRegistrationRecord
        {
            Id = 11,
            DeviceCode = "POS_1003_0915",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = targetStatus,
            AuthorizationCode = "AUTH-OLD"
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            LatestByHardwareId = target,
            RegistrationsForUpdate = [target]
        };
        var service = new DeviceService(repository, LoadStoreAsync, () => now);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal("POS_1003_0915", response.DeviceCode);
        Assert.Equal("1003", response.StoreCode);
        Assert.Equal("Chermside", response.StoreName);
        Assert.Equal(-1, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Equal(1, repository.TransactionCallCount);
        Assert.Empty(repository.DisabledRequests);
        Assert.Empty(repository.CreatedRegistrations);
        var reset = Assert.Single(repository.ResetRequests);
        Assert.Equal(11, reset.RegistrationId);
        Assert.Equal(targetStatus, reset.ExpectedDeviceStatus);
        Assert.Equal("AUTH-OLD", reset.ExpectedAuthorizationCode);
        Assert.NotEqual("AUTH-OLD", reset.AuthorizationCode);
        Assert.Equal(now, reset.ModifiedAt);
    }

    [Fact]
    public async Task RegisterAsync_WhenTargetHasNoRegistration_CreatesPendingInsideTransaction()
    {
        var now = new DateTime(2026, 7, 10, 11, 1, 0);
        var repository = new FakeDeviceRegistrationRepository();
        var service = new DeviceService(repository, LoadStoreAsync, () => now);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal("POS_1003_1101", response.DeviceCode);
        Assert.Equal(-1, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Equal(1, repository.TransactionCallCount);
        Assert.Empty(repository.DisabledRequests);
        Assert.Empty(repository.ResetRequests);
        var created = Assert.Single(repository.CreatedRegistrations);
        Assert.Equal(now, created.CreatedAt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RegisterAsync_WhenAnyRegistrationIsActiveOrLocked_RejectsWithoutWrites(int blockingStatus)
    {
        var targetPending = new DeviceRegistrationRecord
        {
            Id = 20,
            DeviceCode = "POS_1003_PENDING",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var blocking = new DeviceRegistrationRecord
        {
            Id = 19,
            DeviceCode = "POS_BLOCKING",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = blockingStatus,
            AuthorizationCode = "AUTH-SECRET"
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            LatestByHardwareId = targetPending,
            RegistrationsForUpdate = [targetPending, blocking]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal("POS_BLOCKING", response.DeviceCode);
        Assert.Equal("1002", response.StoreCode);
        Assert.Equal(blockingStatus, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Equal(1, repository.TransactionCallCount);
        Assert.Empty(repository.DisabledRequests);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenTargetPendingExists_ReturnsItIdempotentlyWithoutWrites()
    {
        var targetPending = new DeviceRegistrationRecord
        {
            Id = 30,
            DeviceCode = "POS_1003_PENDING",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = -1,
            AuthorizationCode = "AUTH-PENDING"
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            LatestByHardwareId = targetPending,
            RegistrationsForUpdate = [targetPending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal("POS_1003_PENDING", response.DeviceCode);
        Assert.Equal("1003", response.StoreCode);
        Assert.Equal(-1, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Equal(1, repository.TransactionCallCount);
        Assert.Empty(repository.DisabledRequests);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenLatestPendingBelongsToAnotherStore_DisablesItAndCreatesTargetPending()
    {
        var oldPending = new DeviceRegistrationRecord
        {
            Id = 40,
            DeviceCode = "POS_1002_1011",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            LatestByHardwareId = oldPending,
            RegistrationsForUpdate = [oldPending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal("1003", response.StoreCode);
        Assert.Equal(-1, response.DeviceStatus);
        Assert.Null(response.AuthorizationCode);
        Assert.Equal(1, repository.TransactionCallCount);
        var disabled = Assert.Single(repository.DisabledRequests);
        Assert.Equal("POS_1002_1011", disabled.DeviceCode);
        Assert.Single(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenTwoOtherStorePendingsExist_DisablesBothBeforeCreatingTarget()
    {
        var newestPending = new DeviceRegistrationRecord
        {
            Id = 48,
            DeviceCode = "POS_1002_NEWER",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var olderPending = new DeviceRegistrationRecord
        {
            Id = 47,
            DeviceCode = "POS_1004_OLDER",
            StoreCode = "1004",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            RegistrationsForUpdate = [newestPending, olderPending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal(
            ["POS_1002_NEWER", "POS_1004_OLDER"],
            repository.DisabledRequests.Select(static request => request.DeviceCode));
        Assert.Equal(2, repository.DisabledPendingCount);
        Assert.Empty(repository.ResetRequests);
        Assert.Single(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenTargetDisabledAndTwoOtherStorePendingsExist_DisablesBothBeforeResettingTarget()
    {
        var targetDisabled = new DeviceRegistrationRecord
        {
            Id = 60,
            DeviceCode = "POS_1003_DISABLED",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = 0,
            AuthorizationCode = "AUTH-OLD"
        };
        var newestPending = new DeviceRegistrationRecord
        {
            Id = 59,
            DeviceCode = "POS_1002_NEWER",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var olderPending = new DeviceRegistrationRecord
        {
            Id = 58,
            DeviceCode = "POS_1004_OLDER",
            StoreCode = "1004",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            RegistrationsForUpdate = [targetDisabled, newestPending, olderPending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal("POS_1003_DISABLED", response.DeviceCode);
        Assert.Equal(
            ["POS_1002_NEWER", "POS_1004_OLDER"],
            repository.DisabledRequests.Select(static request => request.DeviceCode));
        Assert.Equal(2, repository.DisabledPendingCount);
        Assert.Single(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenTargetLatestPendingExists_DisablesEveryOtherPendingAndKeepsTarget()
    {
        var targetLatestPending = new DeviceRegistrationRecord
        {
            Id = 70,
            DeviceCode = "POS_1003_LATEST",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var targetOlderPending = new DeviceRegistrationRecord
        {
            Id = 69,
            DeviceCode = "POS_1003_OLDER",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var otherStorePending = new DeviceRegistrationRecord
        {
            Id = 68,
            DeviceCode = "POS_1002_PENDING",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            RegistrationsForUpdate = [targetLatestPending, targetOlderPending, otherStorePending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal("POS_1003_LATEST", response.DeviceCode);
        Assert.Equal(
            ["POS_1003_OLDER", "POS_1002_PENDING"],
            repository.DisabledRequests.Select(static request => request.DeviceCode));
        Assert.Equal(2, repository.DisabledPendingCount);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenAnyPendingDisableMisses_RollsBackAllWritesAndDoesNotCreateOrReset()
    {
        var newestPending = new DeviceRegistrationRecord
        {
            Id = 80,
            DeviceCode = "POS_1002_NEWER",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var olderPending = new DeviceRegistrationRecord
        {
            Id = 79,
            DeviceCode = "POS_1004_OLDER",
            StoreCode = "1004",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            RegistrationsForUpdate = [newestPending, olderPending],
            DisablePendingRowsAffectedSequence = [1, 0]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None));

        Assert.Equal(2, repository.DisabledRequests.Count);
        Assert.Equal(0, repository.DisabledPendingCount);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenTargetStatusIsUnknown_RejectsBeforeDisablingAnyPending()
    {
        var targetUnknown = new DeviceRegistrationRecord
        {
            Id = 90,
            DeviceCode = "POS_1003_UNKNOWN",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = 4
        };
        var otherPending = new DeviceRegistrationRecord
        {
            Id = 89,
            DeviceCode = "POS_1002_PENDING",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            RegistrationsForUpdate = [targetUnknown, otherPending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal(4, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Empty(repository.DisabledRequests);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task RegisterAsync_WhenReusableTargetHasEmptyDeviceCode_RejectsBeforeDisablingAnyPending(int targetStatus)
    {
        var targetWithoutCode = new DeviceRegistrationRecord
        {
            Id = 100,
            DeviceCode = string.Empty,
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = targetStatus
        };
        var otherPending = new DeviceRegistrationRecord
        {
            Id = 99,
            DeviceCode = "POS_1002_PENDING",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            RegistrationsForUpdate = [targetWithoutCode, otherPending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal(targetStatus, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Empty(repository.DisabledRequests);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenTargetPendingHasEmptyDeviceCode_RejectsBeforeDisablingAnyPending()
    {
        var targetPendingWithoutCode = new DeviceRegistrationRecord
        {
            Id = 110,
            DeviceCode = string.Empty,
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var otherPending = new DeviceRegistrationRecord
        {
            Id = 109,
            DeviceCode = "POS_1002_PENDING",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            RegistrationsForUpdate = [targetPendingWithoutCode, otherPending]
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        var response = await service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None);

        Assert.Equal(0, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Empty(repository.DisabledRequests);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Fact]
    public async Task RegisterAsync_WhenTargetResetSnapshotMisses_RollsBackOldPendingDisable()
    {
        var oldPending = new DeviceRegistrationRecord
        {
            Id = 50,
            DeviceCode = "POS_1002_PENDING",
            StoreCode = "1002",
            HardwareId = "HW-001",
            DeviceStatus = -1
        };
        var targetDisabled = new DeviceRegistrationRecord
        {
            Id = 49,
            DeviceCode = "POS_1003_DISABLED",
            StoreCode = "1003",
            HardwareId = "HW-001",
            DeviceStatus = 0,
            AuthorizationCode = "AUTH-OLD"
        };
        var repository = new FakeDeviceRegistrationRepository
        {
            LatestByHardwareId = oldPending,
            RegistrationsForUpdate = [oldPending, targetDisabled],
            ResetRowsAffected = 0
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new DeviceRegisterRequest("1003", "HW-001", "Counter 2"),
            CancellationToken.None));

        Assert.Equal(1, repository.TransactionCallCount);
        Assert.Single(repository.DisabledRequests);
        Assert.Single(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
        Assert.True(repository.PendingRegistrationEnabled);
    }

    [Fact]
    public async Task ReregisterAsync_WhenTargetStoreHasDisabledRegistration_ReusesDeviceCodeAndRefreshesRegistration()
    {
        var now = new DateTime(2026, 7, 10, 10, 15, 0);
        var repository = new FakeDeviceRegistrationRepository
        {
            TargetRegistration = new DeviceRegistrationRecord
            {
                Id = 42,
                DeviceCode = "POS_1003_0915",
                StoreCode = "1003",
                HardwareId = "HW-001",
                DeviceStatus = 0,
                AuthorizationCode = "AUTH-OLD"
            }
        };
        var service = new DeviceService(repository, LoadStoreAsync, () => now);

        var response = await service.ReregisterAsync(
            new DeviceReregisterRequest("1003", "HW-001", "Counter 2"),
            new DeviceReregisterContext("POS_1002_0800", "1002", "HW-001"),
            CancellationToken.None);

        Assert.Equal("POS_1003_0915", response.DeviceCode);
        Assert.Equal("1003", response.StoreCode);
        Assert.Equal("Chermside", response.StoreName);
        Assert.Equal(-1, response.DeviceStatus);
        Assert.False(response.IsAllowed);
        Assert.Null(response.AuthorizationCode);
        Assert.Single(repository.ActiveDisabledRequests);
        Assert.Single(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);

        var reset = repository.ResetRequests[0];
        Assert.Equal(42, reset.RegistrationId);
        Assert.Equal("HW-001", reset.HardwareId);
        Assert.Equal("1003", reset.StoreCode);
        Assert.Equal("POS_1003_0915", reset.DeviceCode);
        Assert.Equal(0, reset.ExpectedDeviceStatus);
        Assert.Equal("AUTH-OLD", reset.ExpectedAuthorizationCode);
        Assert.NotEmpty(reset.AuthorizationCode);
        Assert.NotEqual("AUTH-OLD", reset.AuthorizationCode);
        Assert.Equal(now, reset.ModifiedAt);
        Assert.Equal("HBPOS_CLIENT", reset.ModifiedBy);
        Assert.Contains("Counter 2", reset.RemarkSuffix, StringComparison.Ordinal);
        Assert.False(repository.CurrentRegistrationEnabled);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReregisterAsync_WhenTargetStoreHasRegistrationInAnyOtherStatus_ReusesAndResetsIt(int targetStatus)
    {
        var repository = new FakeDeviceRegistrationRepository
        {
            TargetRegistration = new DeviceRegistrationRecord
            {
                Id = 43,
                DeviceCode = "POS_1003_0916",
                StoreCode = "1003",
                HardwareId = "HW-001",
                DeviceStatus = targetStatus,
                AuthorizationCode = "AUTH-OLD"
            }
        };
        var service = new DeviceService(repository, LoadStoreAsync, () => new DateTime(2026, 7, 10, 10, 16, 0));

        var response = await service.ReregisterAsync(
            new DeviceReregisterRequest("1003", "HW-001", "Counter 2"),
            new DeviceReregisterContext("POS_1002_0800", "1002", "HW-001"),
            CancellationToken.None);

        Assert.Equal("POS_1003_0916", response.DeviceCode);
        Assert.Equal(-1, response.DeviceStatus);
        Assert.Single(repository.ResetRequests);
        Assert.Equal(targetStatus, repository.ResetRequests[0].ExpectedDeviceStatus);
        Assert.Equal("AUTH-OLD", repository.ResetRequests[0].ExpectedAuthorizationCode);
        Assert.Empty(repository.CreatedRegistrations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ReregisterAsync_WhenTargetAuthorizationCodeIsNullOrEmpty_PreservesExpectedSnapshot(string? authorizationCode)
    {
        var repository = new FakeDeviceRegistrationRepository
        {
            TargetRegistration = new DeviceRegistrationRecord
            {
                Id = 46,
                DeviceCode = "POS_1003_0919",
                StoreCode = "1003",
                HardwareId = "HW-001",
                DeviceStatus = 2,
                AuthorizationCode = authorizationCode
            }
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        await service.ReregisterAsync(
            new DeviceReregisterRequest("1003", "HW-001", "Counter 2"),
            new DeviceReregisterContext("POS_1002_0800", "1002", "HW-001"),
            CancellationToken.None);

        var reset = Assert.Single(repository.ResetRequests);
        Assert.Equal(authorizationCode, reset.ExpectedAuthorizationCode);
    }

    [Fact]
    public async Task ReregisterAsync_WhenTargetStoreHasNoRegistration_CreatesNewPendingRegistration()
    {
        var now = new DateTime(2026, 7, 10, 10, 17, 0);
        var repository = new FakeDeviceRegistrationRepository();
        var service = new DeviceService(repository, LoadStoreAsync, () => now);

        var response = await service.ReregisterAsync(
            new DeviceReregisterRequest("1003", "HW-001", "Counter 2"),
            new DeviceReregisterContext("POS_1002_0800", "1002", "HW-001"),
            CancellationToken.None);

        Assert.Equal("POS_1003_1017", response.DeviceCode);
        Assert.Single(repository.ActiveDisabledRequests);
        Assert.Empty(repository.ResetRequests);
        var created = Assert.Single(repository.CreatedRegistrations);
        Assert.Equal("POS_1003_1017", created.DeviceCode);
        Assert.Equal("1003", created.StoreCode);
        Assert.Equal("HW-001", created.HardwareId);
        Assert.Equal(-1, created.DeviceStatus);
        Assert.Equal(now, created.CreatedAt);
    }

    [Fact]
    public async Task ReregisterAsync_WhenCurrentRegistrationCannotBeDisabled_ThrowsBeforeTargetWriteAndRollsBack()
    {
        var repository = new FakeDeviceRegistrationRepository
        {
            TargetRegistration = new DeviceRegistrationRecord
            {
                Id = 44,
                DeviceCode = "POS_1003_0917",
                StoreCode = "1003",
                HardwareId = "HW-001",
                DeviceStatus = 0
            },
            DisableActiveRowsAffected = 0
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReregisterAsync(
            new DeviceReregisterRequest("1003", "HW-001", "Counter 2"),
            new DeviceReregisterContext("POS_1002_0800", "1002", "HW-001"),
            CancellationToken.None));

        Assert.Equal(1, repository.TransactionCallCount);
        Assert.Single(repository.ActiveDisabledRequests);
        Assert.Empty(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
        Assert.True(repository.CurrentRegistrationEnabled);
    }

    [Fact]
    public async Task ReregisterAsync_WhenTargetRegistrationCannotBeReset_ThrowsAndRollsBackCurrentDisable()
    {
        var repository = new FakeDeviceRegistrationRepository
        {
            TargetRegistration = new DeviceRegistrationRecord
            {
                Id = 45,
                DeviceCode = "POS_1003_0918",
                StoreCode = "1003",
                HardwareId = "HW-001",
                DeviceStatus = 0
            },
            ResetRowsAffected = 0
        };
        var service = new DeviceService(repository, LoadStoreAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReregisterAsync(
            new DeviceReregisterRequest("1003", "HW-001", "Counter 2"),
            new DeviceReregisterContext("POS_1002_0800", "1002", "HW-001"),
            CancellationToken.None));

        Assert.Equal(1, repository.TransactionCallCount);
        Assert.Single(repository.ActiveDisabledRequests);
        Assert.Single(repository.ResetRequests);
        Assert.Empty(repository.CreatedRegistrations);
        Assert.True(repository.CurrentRegistrationEnabled);
    }

    [Fact]
    public async Task UpdateRuntimeStatusAsync_KeepsSameCashierLoginTimeAndClearsEmptyCashier()
    {
        var firstNow = new DateTime(2026, 7, 1, 10, 0, 0);
        var secondNow = new DateTime(2026, 7, 1, 10, 1, 0);
        var repository = new FakeDeviceRegistrationRepository();
        var service = new DeviceService(repository, LoadStoreAsync, () => firstNow);

        var firstResult = await service.UpdateRuntimeStatusAsync(
            "HW-001",
            "POS-001",
            "1002",
            true,
            "CASHIER-1",
            "Alice",
            CancellationToken.None);
        var secondResult = await new DeviceService(repository, LoadStoreAsync, () => secondNow)
            .UpdateRuntimeStatusAsync(
                "HW-001",
                "POS-001",
                "1002",
                true,
                "CASHIER-1",
                "Alice",
                CancellationToken.None);

        Assert.True(firstResult);
        Assert.True(secondResult);
        Assert.Equal(firstNow, repository.LastRuntimeStatus!.CashierLoginAt);
        Assert.Equal(secondNow, repository.LastRuntimeStatus.LastHeartbeatAt);
        Assert.Equal("CASHIER-1", repository.LastRuntimeStatus.CashierId);

        var clearResult = await new DeviceService(repository, LoadStoreAsync, () => secondNow.AddMinutes(1))
            .UpdateRuntimeStatusAsync(
                "HW-001",
                "POS-001",
                "1002",
                false,
                null,
                null,
                CancellationToken.None);

        Assert.True(clearResult);
        Assert.False(repository.LastRuntimeStatus!.IsOnline);
        Assert.Null(repository.LastRuntimeStatus.CashierId);
        Assert.Null(repository.LastRuntimeStatus.CashierName);
        Assert.Null(repository.LastRuntimeStatus.CashierLoginAt);
    }

    private static Task<DeviceStoreInfo?> LoadStoreAsync(string storeCode, CancellationToken cancellationToken)
    {
        DeviceStoreInfo? store = storeCode switch
        {
            "1002" => new DeviceStoreInfo("1002", "Lutwyche"),
            "1003" => new DeviceStoreInfo("1003", "Chermside"),
            _ => null
        };

        return Task.FromResult(store);
    }

    private sealed class FakeDeviceRegistrationRepository : IDeviceRegistrationRepository
    {
        public DeviceRegistrationRecord? LatestByHardwareId { get; init; }

        public DeviceRegistrationRecord? ActiveOrLockedRegistration { get; init; }

        public DeviceRegistrationRecord? TargetRegistration { get; init; }

        public IReadOnlyList<DeviceRegistrationRecord> RegistrationsForUpdate { get; init; } = [];

        public int DisablePendingRowsAffected { get; init; } = 1;

        public IReadOnlyList<int>? DisablePendingRowsAffectedSequence { get; init; }

        public int DisableActiveRowsAffected { get; init; } = 1;

        public int ResetRowsAffected { get; init; } = 1;

        public List<DeviceRegistrationDisableRequest> DisabledRequests { get; } = [];

        public List<ActiveDisableSnapshot> ActiveDisabledRequests { get; } = [];

        public List<DeviceRegistrationResetForReregisterRequest> ResetRequests { get; } = [];

        public List<DeviceRegistrationCreateRequest> CreatedRegistrations { get; } = [];

        public int TransactionCallCount { get; private set; }

        public RuntimeStatusSnapshot? LastRuntimeStatus { get; private set; }

        public bool CurrentRegistrationEnabled { get; private set; } = true;

        public bool PendingRegistrationEnabled { get; private set; } = true;

        public int DisabledPendingCount { get; private set; }

        private int disablePendingCallCount;

        public Task<DeviceRegistrationRecord?> FindByDeviceCodeAsync(
            string deviceCode,
            string storeCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<DeviceRegistrationRecord?>(null);
        }

        public Task<DeviceRegistrationRecord?> FindLatestByHardwareIdAsync(
            string hardwareId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(LatestByHardwareId);
        }

        public Task<DeviceRegistrationRecord?> FindActiveOrLockedRegistrationAsync(
            string hardwareId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ActiveOrLockedRegistration);
        }

        public Task<DeviceRegistrationRecord?> FindLatestByHardwareIdAndStoreCodeAsync(
            string hardwareId,
            string storeCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(TargetRegistration);
        }

        public Task<IReadOnlyList<DeviceRegistrationRecord>> FindAllByHardwareIdForRegistrationAsync(
            string hardwareId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(RegistrationsForUpdate);
        }

        public Task<int> DisableActiveRegistrationAsync(
            string hardwareId,
            string deviceCode,
            string storeCode,
            string remarkSuffix,
            CancellationToken cancellationToken)
        {
            ActiveDisabledRequests.Add(new ActiveDisableSnapshot(hardwareId, deviceCode, storeCode, remarkSuffix));
            if (DisableActiveRowsAffected == 1)
            {
                CurrentRegistrationEnabled = false;
            }

            return Task.FromResult(DisableActiveRowsAffected);
        }

        public Task<int> ResetRegistrationForReregisterAsync(
            DeviceRegistrationResetForReregisterRequest request,
            CancellationToken cancellationToken)
        {
            ResetRequests.Add(request);
            return Task.FromResult(ResetRowsAffected);
        }

        public Task<int> DisablePendingRegistrationAsync(
            DeviceRegistrationDisableRequest request,
            CancellationToken cancellationToken)
        {
            DisabledRequests.Add(request);
            var rowsAffected = DisablePendingRowsAffectedSequence is not null
                && disablePendingCallCount < DisablePendingRowsAffectedSequence.Count
                    ? DisablePendingRowsAffectedSequence[disablePendingCallCount]
                    : DisablePendingRowsAffected;
            disablePendingCallCount++;
            if (rowsAffected == 1)
            {
                PendingRegistrationEnabled = false;
                DisabledPendingCount++;
            }

            return Task.FromResult(rowsAffected);
        }

        public Task CreateRegistrationAsync(
            DeviceRegistrationCreateRequest request,
            CancellationToken cancellationToken)
        {
            CreatedRegistrations.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> UpdateRuntimeStatusAsync(
            DeviceRuntimeStatusUpdateRequest request,
            CancellationToken cancellationToken)
        {
            var nextCashierId = string.IsNullOrWhiteSpace(request.CashierId) ? null : request.CashierId.Trim();
            var nextCashierName = string.IsNullOrWhiteSpace(request.CashierName) ? null : request.CashierName.Trim();
            var cashierLoginAt = LastRuntimeStatus?.CashierLoginAt;
            if (nextCashierId is null && nextCashierName is null)
            {
                cashierLoginAt = null;
            }
            else if (LastRuntimeStatus?.CashierId != nextCashierId || cashierLoginAt is null)
            {
                cashierLoginAt = request.ReportedAt;
            }

            LastRuntimeStatus = new RuntimeStatusSnapshot(
                request.IsOnline,
                request.ReportedAt,
                nextCashierId,
                nextCashierName,
                cashierLoginAt);
            return Task.FromResult(1);
        }

        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken)
        {
            TransactionCallCount++;
            var currentRegistrationEnabled = CurrentRegistrationEnabled;
            var pendingRegistrationEnabled = PendingRegistrationEnabled;
            var disabledPendingCount = DisabledPendingCount;
            try
            {
                await action(cancellationToken);
            }
            catch
            {
                CurrentRegistrationEnabled = currentRegistrationEnabled;
                PendingRegistrationEnabled = pendingRegistrationEnabled;
                DisabledPendingCount = disabledPendingCount;
                throw;
            }
        }
    }

    private sealed record ActiveDisableSnapshot(
        string HardwareId,
        string DeviceCode,
        string StoreCode,
        string RemarkSuffix);

    private sealed record RuntimeStatusSnapshot(
        bool IsOnline,
        DateTime LastHeartbeatAt,
        string? CashierId,
        string? CashierName,
        DateTime? CashierLoginAt);
}
