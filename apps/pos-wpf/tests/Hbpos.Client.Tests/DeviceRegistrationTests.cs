using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;

namespace Hbpos.Client.Tests;

public sealed class DeviceRegistrationTests
{
    [Fact]
    public async Task LocalDeviceRepository_SavesAndRestoresPendingRegistration()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var repository = new LocalDeviceRepository(store, new FakeAuthorizationProtector());
            await schema.InitializeAsync();

            await repository.SaveAsync(
                new Hbpos.Contracts.Devices.DeviceRegisterResponse("POS-001", "1002", "Lutwyche", 1, true, "Device is enabled.", "AUTH-001"),
                "HW-001");

            var cached = await repository.GetLatestAsync();

            Assert.NotNull(cached);
            Assert.Equal("POS-001", cached.DeviceCode);
            Assert.Equal("1002", cached.StoreCode);
            Assert.Equal("Lutwyche", cached.StoreName);
            Assert.Equal("HW-001", cached.HardwareId);
            Assert.Equal(1, cached.DeviceStatus);
            Assert.True(cached.IsAllowed);
            Assert.Equal("Device is enabled.", cached.Message);
            Assert.Equal("AUTH-001", cached.AuthorizationCode);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_LoadsStoresAndMapsPendingRegistrationResult()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                string.Empty,
                false,
                "Select a store and submit this register for approval."),
            RegisterResult = new DeviceRegistrationActionResult(
                "POS-001",
                "1002",
                "Lutwyche",
                "HW-001",
                true,
                "Pending approval",
                null,
                false,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);

        await viewModel.InitializeAsync(cachedDevice: null);
        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("HW-001", viewModel.HardwareId);
        Assert.Equal("Lutwyche (1002)", viewModel.SelectedStore?.DisplayName);
        Assert.Equal("POS-001", viewModel.DeviceCode);
        Assert.True(viewModel.HasPendingRegistration);
        Assert.Equal("Pending approval", viewModel.StatusMessage);
        Assert.Equal("1002", workflow.LastRegisterStoreCode);
        Assert.Equal("HW-001", workflow.LastRegisterHardwareId);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_VerifyRaisesActivationWhenWorkflowReturnsActivated()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-001",
                true,
                "Pending approval"),
            VerifyResult = new DeviceRegistrationActionResult(
                "POS-001",
                "1002",
                "Lutwyche",
                "HW-001",
                false,
                "Device is enabled.",
                "AUTH-001",
                true,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        var cached = new LocalDeviceCache("POS-001", "1002", "Lutwyche", "HW-001", -1, false, "Pending approval", DateTimeOffset.UtcNow);
        DeviceActivatedEventArgs? activated = null;
        viewModel.DeviceActivated += (_, args) => activated = args;

        await viewModel.InitializeAsync(cached);
        await viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.NotNull(activated);
        Assert.Equal("POS-001", activated.DeviceCode);
        Assert.Equal("1002", activated.StoreCode);
        Assert.Equal("Lutwyche", activated.StoreName);
        Assert.Equal("HW-001", activated.HardwareId);
        Assert.Equal("AUTH-001", activated.AuthorizationCode);
        Assert.Equal("POS-001", workflow.LastVerifyDeviceCode);
        Assert.Equal("1002", workflow.LastVerifyStoreCode);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_ManualVerifyResultAfterStoreChangeDoesNotPersistOrActivate()
    {
        var pendingVerifyResult = new TaskCompletionSource<DeviceRegistrationActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-OLD",
                false,
                "Verify the device approval status."),
            PendingVerifyResult = pendingVerifyResult
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        DeviceActivatedEventArgs? activated = null;
        viewModel.DeviceActivated += (_, args) => activated = args;

        await viewModel.InitializeAsync(cachedDevice: null);
        var verifyTask = viewModel.VerifyCommand.ExecuteAsync(null);
        await workflow.WaitForVerifyStartedAsync();
        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");

        var oldResultPersistenceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingVerifyResult.SetResult(new DeviceRegistrationActionResult(
            "POS-OLD",
            "1002",
            "Lutwyche",
            "HW-001",
            false,
            "Device is enabled.",
            "AUTH-OLD",
            true,
            false)
        {
            PersistAsync = _ =>
            {
                oldResultPersistenceStarted.TrySetResult();
                return Task.CompletedTask;
            }
        });
        await verifyTask;

        Assert.Null(activated);
        Assert.False(oldResultPersistenceStarted.Task.IsCompleted);
        Assert.Equal("1003", viewModel.SelectedStore?.StoreCode);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_RegisterPending_StartsPollingAndActivatesWithoutManualVerify()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                string.Empty,
                false,
                "Select a store and submit this register for approval."),
            RegisterResult = new DeviceRegistrationActionResult(
                "POS-001",
                "1002",
                "Lutwyche",
                "HW-001",
                true,
                "Pending approval",
                null,
                false,
                false),
            VerifyResult = new DeviceRegistrationActionResult(
                "POS-001",
                "1002",
                "Lutwyche",
                "HW-001",
                false,
                "Device is enabled.",
                "AUTH-001",
                true,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            approvalPollingInterval: TimeSpan.Zero,
            delayAsync: (_, _) => Task.CompletedTask);
        DeviceActivatedEventArgs? activated = null;
        viewModel.DeviceActivated += (_, args) => activated = args;

        await viewModel.InitializeAsync(cachedDevice: null);
        await viewModel.RegisterCommand.ExecuteAsync(null);
        await workflow.WaitForVerifyStartedAsync();

        Assert.NotNull(activated);
        Assert.Equal("POS-001", activated.DeviceCode);
        Assert.Equal("AUTH-001", activated.AuthorizationCode);
        Assert.Equal(1, workflow.VerifyCallCount);
        Assert.Equal("POS-001", workflow.LastVerifyDeviceCode);
        Assert.Equal("1002", workflow.LastVerifyStoreCode);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_InitializeWithPendingCache_StartsPollingAndActivates()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-001",
                true,
                "Pending approval"),
            VerifyResult = new DeviceRegistrationActionResult(
                "POS-001",
                "1002",
                "Lutwyche",
                "HW-001",
                false,
                "Device is enabled.",
                "AUTH-001",
                true,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            approvalPollingInterval: TimeSpan.Zero,
            delayAsync: (_, _) => Task.CompletedTask);
        var cached = new LocalDeviceCache("POS-001", "1002", "Lutwyche", "HW-001", -1, false, "Pending approval", DateTimeOffset.UtcNow);
        DeviceActivatedEventArgs? activated = null;
        viewModel.DeviceActivated += (_, args) => activated = args;

        await viewModel.InitializeAsync(cached);
        await workflow.WaitForVerifyStartedAsync();

        Assert.NotNull(activated);
        Assert.Equal("POS-001", activated.DeviceCode);
        Assert.Equal("AUTH-001", activated.AuthorizationCode);
        Assert.Equal(1, workflow.VerifyCallCount);
        Assert.Equal("POS-001", workflow.LastVerifyDeviceCode);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_RejectedRegister_DoesNotStartPolling()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                string.Empty,
                false,
                "Select a store and submit this register for approval."),
            RegisterResult = new DeviceRegistrationActionResult(
                "POS-001",
                "1002",
                "Lutwyche",
                "HW-001",
                false,
                "Device registration was rejected.",
                null,
                false,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            approvalPollingInterval: TimeSpan.Zero,
            delayAsync: (_, _) => Task.CompletedTask);

        await viewModel.InitializeAsync(cachedDevice: null);
        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(0, workflow.VerifyCallCount);
        Assert.False(viewModel.HasPendingRegistration);
        Assert.Empty(viewModel.DeviceCode);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_RegisterResultAfterStoreChangeDoesNotPersistOrApply()
    {
        var pendingRegisterResult = new TaskCompletionSource<DeviceRegistrationActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                string.Empty,
                false,
                "Select a store and submit this register for approval."),
            PendingRegisterResult = pendingRegisterResult
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);

        await viewModel.InitializeAsync(cachedDevice: null);
        var registerTask = viewModel.RegisterCommand.ExecuteAsync(null);
        await workflow.WaitForRegisterStartedAsync();
        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");

        var oldResultPersistenceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRegisterResult.SetResult(new DeviceRegistrationActionResult(
            "POS-OLD",
            "1002",
            "Lutwyche",
            "HW-001",
            true,
            "Pending approval",
            null,
            false,
            false)
        {
            PersistAsync = _ =>
            {
                oldResultPersistenceStarted.TrySetResult();
                return Task.CompletedTask;
            }
        });
        await registerTask;

        Assert.False(oldResultPersistenceStarted.Task.IsCompleted);
        Assert.Equal("1003", viewModel.SelectedStore?.StoreCode);
        Assert.Empty(viewModel.DeviceCode);
        Assert.False(viewModel.HasPendingRegistration);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_SwitchingStoreCancelsPendingPollingBeforeNewSubmit()
    {
        var pollingDelayCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task DelayUntilCancelled(TimeSpan _, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => pollingDelayCancelled.TrySetResult());
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-OLD",
                true,
                "Pending approval"),
            RegisterResult = new DeviceRegistrationActionResult(
                "POS-NEW",
                "1003",
                "Zillmere",
                "HW-001",
                true,
                "Pending approval",
                null,
                false,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            approvalPollingInterval: TimeSpan.FromMinutes(1),
            delayAsync: DelayUntilCancelled);
        var cached = new LocalDeviceCache("POS-OLD", "1002", "Lutwyche", "HW-001", -1, false, "Pending approval", DateTimeOffset.UtcNow);

        await viewModel.InitializeAsync(cached);
        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");
        await pollingDelayCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(0, workflow.VerifyCallCount);
        Assert.Equal("1003", workflow.LastRegisterStoreCode);
        Assert.Equal("POS-NEW", viewModel.DeviceCode);
        Assert.DoesNotContain("POS-OLD", workflow.VerifiedDeviceCodes);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_RestartRequired_DoesNotRestartPendingPollingAfterStoreReselection()
    {
        var pollingStartCount = 0;
        Task DelayUntilCancelled(TimeSpan _, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref pollingStartCount);
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-OLD",
                true,
                "Pending approval")
        };
        var apiServerSettings = CreateApiServerSettings();
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            approvalPollingInterval: TimeSpan.FromMinutes(1),
            delayAsync: DelayUntilCancelled,
            apiServerSettings: apiServerSettings);
        var cached = new LocalDeviceCache(
            "POS-OLD",
            "1002",
            "Lutwyche",
            "HW-001",
            -1,
            false,
            "Pending approval",
            DateTimeOffset.UtcNow);

        await viewModel.InitializeAsync(cached);
        var originalPollingTask = viewModel.ApprovalPollingTask;
        Assert.NotNull(originalPollingTask);
        Assert.Equal(1, Volatile.Read(ref pollingStartCount));

        try
        {
            apiServerSettings.ServerAddressText = "https://new.example.com";
            await apiServerSettings.SaveCommand.ExecuteAsync(null);
            await originalPollingTask!.WaitAsync(TimeSpan.FromSeconds(3));

            viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");
            viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1002");
            await Task.Yield();

            Assert.True(apiServerSettings.RestartRequired);
            Assert.Same(originalPollingTask, viewModel.ApprovalPollingTask);
            Assert.Equal(1, Volatile.Read(ref pollingStartCount));
            Assert.Equal(0, workflow.VerifyCallCount);
        }
        finally
        {
            // 测试结束时取消可能被错误重启的轮询，避免后台任务泄漏到其他用例。
            viewModel.Prepare(cachedDevice: null);
        }
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_ClearingRestartRequired_ResumesPendingPolling()
    {
        var pollingStartCount = 0;
        Task DelayFirstPollingUntilCancelled(TimeSpan _, CancellationToken cancellationToken)
        {
            return Interlocked.Increment(ref pollingStartCount) == 1
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.CompletedTask;
        }

        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-OLD",
                true,
                "Pending approval"),
            VerifyResult = new DeviceRegistrationActionResult(
                "POS-OLD",
                "1002",
                "Lutwyche",
                "HW-001",
                false,
                "Device is enabled.",
                "AUTH-001",
                true,
                false)
        };
        var apiServerSettings = CreateApiServerSettings();
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            approvalPollingInterval: TimeSpan.FromMinutes(1),
            delayAsync: DelayFirstPollingUntilCancelled,
            apiServerSettings: apiServerSettings);
        var cached = new LocalDeviceCache(
            "POS-OLD",
            "1002",
            "Lutwyche",
            "HW-001",
            -1,
            false,
            "Pending approval",
            DateTimeOffset.UtcNow);

        await viewModel.InitializeAsync(cached);
        var originalPollingTask = viewModel.ApprovalPollingTask;
        Assert.NotNull(originalPollingTask);

        try
        {
            apiServerSettings.ServerAddressText = "https://new.example.com";
            await apiServerSettings.SaveCommand.ExecuteAsync(null);
            await originalPollingTask!.WaitAsync(TimeSpan.FromSeconds(3));

            apiServerSettings.ServerAddressText = "https://current.example.com/";
            await apiServerSettings.SaveCommand.ExecuteAsync(null);
            await workflow.WaitForVerifyStartedAsync().WaitAsync(TimeSpan.FromSeconds(3));

            Assert.False(apiServerSettings.RestartRequired);
            Assert.NotSame(originalPollingTask, viewModel.ApprovalPollingTask);
            Assert.Equal(2, Volatile.Read(ref pollingStartCount));
            Assert.Equal(1, workflow.VerifyCallCount);
        }
        finally
        {
            viewModel.Prepare(cachedDevice: null);
        }
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_InFlightOldPollingResultDoesNotOverrideNewPendingRegistration()
    {
        var pendingOldVerifyResult = new TaskCompletionSource<DeviceRegistrationActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCount = 0;
        Task DelayFirstPollOnly(TimeSpan _, CancellationToken cancellationToken)
        {
            return Interlocked.Increment(ref delayCount) == 1
                ? Task.CompletedTask
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-OLD",
                true,
                "Pending approval"),
            RegisterResult = new DeviceRegistrationActionResult(
                "POS-NEW",
                "1003",
                "Zillmere",
                "HW-001",
                true,
                "Pending approval",
                null,
                false,
                false),
            PendingVerifyResult = pendingOldVerifyResult
        };
        var viewModel = new DeviceRegistrationViewModel(
            workflow,
            approvalPollingInterval: TimeSpan.Zero,
            delayAsync: DelayFirstPollOnly);
        var cached = new LocalDeviceCache("POS-OLD", "1002", "Lutwyche", "HW-001", -1, false, "Pending approval", DateTimeOffset.UtcNow);
        DeviceActivatedEventArgs? activated = null;
        viewModel.DeviceActivated += (_, args) => activated = args;

        await viewModel.InitializeAsync(cached);
        await workflow.WaitForVerifyStartedAsync();
        var oldPollingTask = viewModel.ApprovalPollingTask;
        Assert.NotNull(oldPollingTask);
        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");
        await viewModel.RegisterCommand.ExecuteAsync(null);

        var oldResultPersistenceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingOldVerifyResult.SetResult(new DeviceRegistrationActionResult(
            "POS-OLD",
            "1002",
            "Lutwyche",
            "HW-001",
            false,
            "Device is enabled.",
            "AUTH-OLD",
            true,
            false)
        {
            PersistAsync = _ =>
            {
                oldResultPersistenceStarted.TrySetResult();
                return Task.CompletedTask;
            }
        });
        await oldPollingTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Null(activated);
        Assert.False(oldResultPersistenceStarted.Task.IsCompleted);
        Assert.Equal("POS-NEW", viewModel.DeviceCode);
        Assert.True(viewModel.HasPendingRegistration);
        Assert.Equal("Pending approval", viewModel.StatusMessage);

        viewModel.Prepare(cachedDevice: null);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_PendingRegistration_AllowsSwitchingStoreBeforeSubmit()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-OLD",
                true,
                "Pending approval"),
            RegisterResult = new DeviceRegistrationActionResult(
                "POS-NEW",
                "1003",
                "Zillmere",
                "HW-001",
                true,
                "Pending approval",
                null,
                false,
                false),
            VerifyResult = new DeviceRegistrationActionResult(
                "POS-NEW",
                "1003",
                "Zillmere",
                "HW-001",
                true,
                "Pending approval",
                null,
                false,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        var cached = new LocalDeviceCache("POS-OLD", "1002", "Lutwyche", "HW-001", -1, false, "Pending approval", DateTimeOffset.UtcNow);

        await viewModel.InitializeAsync(cached);

        Assert.Equal("POS-OLD", viewModel.DeviceCode);
        Assert.True(viewModel.HasPendingRegistration);
        Assert.False(viewModel.RegisterCommand.CanExecute(null));
        Assert.True(viewModel.VerifyCommand.CanExecute(null));

        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");

        Assert.Empty(viewModel.DeviceCode);
        Assert.False(viewModel.HasPendingRegistration);
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(viewModel.VerifyCommand.CanExecute(null));
        Assert.Equal("Submit Store Switch Registration", viewModel.RegisterButtonText);

        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1002");

        Assert.Equal("POS-OLD", viewModel.DeviceCode);
        Assert.True(viewModel.HasPendingRegistration);
        Assert.False(viewModel.RegisterCommand.CanExecute(null));
        Assert.True(viewModel.VerifyCommand.CanExecute(null));

        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");
        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("1003", workflow.LastRegisterStoreCode);
        Assert.Equal("POS-NEW", viewModel.DeviceCode);
        Assert.True(viewModel.HasPendingRegistration);
        Assert.False(viewModel.RegisterCommand.CanExecute(null));
        Assert.True(viewModel.VerifyCommand.CanExecute(null));

        await viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.Equal("1003", workflow.LastVerifyStoreCode);
        Assert.Equal("POS-NEW", workflow.LastVerifyDeviceCode);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_PendingRegistration_AllowsSwitchWhenCachedStoreIsNotVisible()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1003", "Zillmere", true),
                "POS-OLD",
                true,
                "Pending approval")
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        var cached = new LocalDeviceCache("POS-OLD", "1002", "Lutwyche", "HW-001", -1, false, "Pending approval", DateTimeOffset.UtcNow);

        await viewModel.InitializeAsync(cached);

        Assert.Equal("1003", viewModel.SelectedStore?.StoreCode);
        Assert.Empty(viewModel.DeviceCode);
        Assert.False(viewModel.HasPendingRegistration);
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(viewModel.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_RejectedCachedDevice_DoesNotBecomePendingRegistration()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1003", "Zillmere", true),
                "POS-OLD",
                false,
                "Device hardware is already registered to another store.")
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        var cached = new LocalDeviceCache("POS-OLD", "1002", "Lutwyche", "HW-001", 1, false, "Device hardware is already registered to another store.", DateTimeOffset.UtcNow);

        await viewModel.InitializeAsync(cached);

        Assert.False(viewModel.HasPendingRegistration);
        Assert.Equal("POS-OLD", viewModel.DeviceCode);
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.True(viewModel.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_RejectedStoreSwitch_DoesNotBecomePendingRegistration()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1002", "Lutwyche", true), new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1002", "Lutwyche", true),
                "POS-OLD",
                true,
                "Pending approval"),
            RegisterResult = new DeviceRegistrationActionResult(
                "POS-OLD",
                "1002",
                "Lutwyche",
                "HW-001",
                false,
                "Device hardware is already registered to another store.",
                null,
                false,
                false)
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        var cached = new LocalDeviceCache("POS-OLD", "1002", "Lutwyche", "HW-001", -1, false, "Pending approval", DateTimeOffset.UtcNow);

        await viewModel.InitializeAsync(cached);
        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1003");
        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.DeviceCode);
        Assert.False(viewModel.HasPendingRegistration);
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(viewModel.VerifyCommand.CanExecute(null));
        Assert.Equal("Device hardware is already registered to another store.", viewModel.StatusMessage);

        viewModel.SelectedStore = viewModel.Stores.Single(store => store.StoreCode == "1002");

        Assert.Empty(viewModel.DeviceCode);
        Assert.False(viewModel.HasPendingRegistration);
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(viewModel.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_ReregisterMode_MapsResultAndRaisesReregistered()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            HardwareId = "HW-001",
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1003", "Zillmere", true),
                string.Empty,
                false,
                "Select a new store and submit device reregistration."),
            ReregisterResult = new DeviceRegistrationActionResult(
                "POS-NEW",
                "1003",
                "Zillmere",
                "HW-001",
                true,
                "Pending approval",
                null,
                false,
                true)
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        DeviceReregisteredEventArgs? reregistered = null;
        viewModel.DeviceReregistered += (_, args) => reregistered = args;

        viewModel.PrepareReregister("1002");
        await viewModel.LoadStoresAsync(cachedDevice: null);

        Assert.Equal("Reregister Device to Another Store", viewModel.TitleText);
        Assert.Equal("Submit Store Switch Reregistration", viewModel.RegisterButtonText);

        Assert.Null(viewModel.SelectedStore);
        Assert.False(viewModel.RegisterCommand.CanExecute(null));

        viewModel.SelectedStore = Assert.Single(viewModel.Stores);
        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("1002", workflow.LastLoadExcludedStoreCode);
        Assert.Equal("Zillmere (1003)", viewModel.SelectedStore?.DisplayName);
        Assert.Equal("POS-NEW", viewModel.DeviceCode);
        Assert.True(viewModel.HasPendingRegistration);
        Assert.Equal("Pending approval", viewModel.StatusMessage);
        Assert.False(viewModel.IsReregisterMode);
        Assert.False(viewModel.CanCancel);
        Assert.Equal("1003", workflow.LastReregisterStoreCode);
        Assert.NotNull(reregistered);
    }

    [Fact]
    public void DeviceRegistrationViewModel_ReregisterMode_AllowsCancelWhileLoading()
    {
        var viewModel = new DeviceRegistrationViewModel(new FakeDeviceRegistrationWorkflowService());
        var cancelRequested = false;

        viewModel.CancelRequested += (_, _) => cancelRequested = true;
        viewModel.PrepareReregister("1002");
        viewModel.IsBusy = true;

        Assert.True(viewModel.CancelCommand.CanExecute(null));

        viewModel.CancelCommand.Execute(null);

        Assert.True(cancelRequested);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_ReregisterMode_CancelDuringSubmitIgnoresLaterResult()
    {
        var pendingReregister = new TaskCompletionSource<DeviceRegistrationActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            LoadResult = new DeviceRegistrationLoadResult(
                [new StoreSelectionItem("1003", "Zillmere", true)],
                new StoreSelectionItem("1003", "Zillmere", true),
                string.Empty,
                false,
                "Select a new store and submit device reregistration."),
            PendingReregisterResult = pendingReregister
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);
        var cancelRequested = false;
        DeviceReregisteredEventArgs? reregistered = null;

        viewModel.CancelRequested += (_, _) => cancelRequested = true;
        viewModel.DeviceReregistered += (_, args) => reregistered = args;
        viewModel.PrepareReregister("1002");
        await viewModel.LoadStoresAsync(cachedDevice: null);
        viewModel.SelectedStore = Assert.Single(viewModel.Stores);

        var submitTask = viewModel.RegisterCommand.ExecuteAsync(null);
        await workflow.WaitForReregisterStartedAsync();

        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CancelCommand.CanExecute(null));

        viewModel.CancelCommand.Execute(null);
        pendingReregister.SetResult(new DeviceRegistrationActionResult(
            "POS-NEW",
            "1003",
            "Zillmere",
            "HW-001",
            true,
            "Pending approval",
            null,
            false,
            true));
        await submitTask;

        Assert.True(cancelRequested);
        Assert.Null(reregistered);
        Assert.True(viewModel.IsReregisterMode);
        Assert.True(viewModel.CanCancel);
    }

    [Fact]
    public async Task DeviceRegistrationViewModel_ReregisterMode_EmptyStoresKeepsSubmitDisabled()
    {
        var workflow = new FakeDeviceRegistrationWorkflowService
        {
            LoadResult = new DeviceRegistrationLoadResult(
                [],
                null,
                string.Empty,
                false,
                "No other stores are available for device reregistration.")
        };
        var viewModel = new DeviceRegistrationViewModel(workflow);

        viewModel.PrepareReregister("1002");
        await viewModel.LoadStoresAsync(cachedDevice: null);

        Assert.Empty(viewModel.Stores);
        Assert.Null(viewModel.SelectedStore);
        Assert.False(viewModel.RegisterCommand.CanExecute(null));
        Assert.Equal("No other stores are available for device reregistration.", viewModel.StatusMessage);
        Assert.True(viewModel.CancelCommand.CanExecute(null));
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"hbpos-client-device-{Guid.NewGuid():N}.db");
    }

    private static ApiServerSettingsViewModel CreateApiServerSettings()
    {
        var service = new ApiServerSettingsService(
            new HttpClient(new SuccessfulHealthHandler()),
            () => "https://current.example.com/",
            _ => { });
        return new ApiServerSettingsViewModel(service, new LocalizationService());
    }

    private static void DeleteTempDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private sealed class FakeDeviceRegistrationWorkflowService : IDeviceRegistrationWorkflowService
    {
        public string HardwareId { get; init; } = "HW-001";

        public DeviceRegistrationLoadResult LoadResult { get; init; } = new([], null, string.Empty, false, string.Empty);

        public DeviceRegistrationActionResult RegisterResult { get; init; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty, null, false, false);

        public DeviceRegistrationActionResult VerifyResult { get; init; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty, null, false, false);

        public DeviceRegistrationActionResult ReregisterResult { get; init; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty, null, false, false);

        public TaskCompletionSource<DeviceRegistrationActionResult>? PendingRegisterResult { get; init; }

        public TaskCompletionSource<DeviceRegistrationActionResult>? PendingReregisterResult { get; init; }

        public TaskCompletionSource<DeviceRegistrationActionResult>? PendingVerifyResult { get; init; }

        private TaskCompletionSource RegisterStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource ReregisterStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource VerifyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly List<string> _verifiedDeviceCodes = [];

        public string? LastLoadExcludedStoreCode { get; private set; }

        public string? LastRegisterStoreCode { get; private set; }

        public string? LastRegisterHardwareId { get; private set; }

        public string? LastVerifyStoreCode { get; private set; }

        public string? LastVerifyDeviceCode { get; private set; }

        public string? LastReregisterStoreCode { get; private set; }

        public int VerifyCallCount { get; private set; }

        public IReadOnlyList<string> VerifiedDeviceCodes => _verifiedDeviceCodes;

        public string GetHardwareId() => HardwareId;

        public Task<DeviceRegistrationLoadResult> LoadStoresAsync(
            LocalDeviceCache? cachedDevice,
            bool isReregisterMode,
            string? excludedStoreCode = null,
            CancellationToken cancellationToken = default)
        {
            LastLoadExcludedStoreCode = excludedStoreCode;
            return Task.FromResult(LoadResult);
        }

        public Task<DeviceRegistrationActionResult> RegisterAsync(
            StoreSelectionItem selectedStore,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            LastRegisterStoreCode = selectedStore.StoreCode;
            LastRegisterHardwareId = hardwareId;
            RegisterStarted.TrySetResult();
            return PendingRegisterResult?.Task ?? Task.FromResult(RegisterResult);
        }

        public Task<DeviceRegistrationActionResult> VerifyAsync(
            StoreSelectionItem selectedStore,
            string deviceCode,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            LastVerifyStoreCode = selectedStore.StoreCode;
            LastVerifyDeviceCode = deviceCode;
            VerifyCallCount++;
            _verifiedDeviceCodes.Add(deviceCode);
            VerifyStarted.TrySetResult();
            return PendingVerifyResult?.Task ?? Task.FromResult(VerifyResult);
        }

        public Task<DeviceRegistrationActionResult> ReregisterAsync(
            StoreSelectionItem selectedStore,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            LastReregisterStoreCode = selectedStore.StoreCode;
            ReregisterStarted.TrySetResult();
            return PendingReregisterResult?.Task ?? Task.FromResult(ReregisterResult);
        }

        public Task WaitForReregisterStartedAsync() => ReregisterStarted.Task;

        public Task WaitForRegisterStartedAsync() => RegisterStarted.Task;

        public Task WaitForVerifyStartedAsync() => VerifyStarted.Task;
    }

    private sealed class SuccessfulHealthHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(ApiResult<HealthCheckResponse>.Ok(
                    new HealthCheckResponse(true, DateTimeOffset.UnixEpoch, "ok")))
            });
        }
    }

    private sealed class FakeAuthorizationProtector : IDeviceAuthorizationProtector
    {
        public string? LastProtectedValue { get; private set; }

        public string? Protect(string? value)
        {
            LastProtectedValue = value;
            return string.IsNullOrWhiteSpace(value) ? null : $"protected:{value}";
        }

        public string? Unprotect(string? protectedValue)
        {
            return protectedValue?.StartsWith("protected:", StringComparison.Ordinal) == true
                ? protectedValue["protected:".Length..]
                : null;
        }
    }
}

