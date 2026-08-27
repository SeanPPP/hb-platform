using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Devices;

namespace Hbpos.Client.Tests;

public sealed class DeviceActivationViewModelTests
{
    private const string ActivationCode = "HBDEV1-0123456789ABCDEFGHJKMNPQRS-6789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public async Task Scanner_previews_store_then_confirmation_persists_credentials_before_activation()
    {
        var workflow = new FakeActivationWorkflow();
        var scanner = new TrackingRawScannerService();
        using var viewModel = new DeviceRegistrationViewModel(
            workflow,
            rawScannerService: scanner);
        viewModel.Prepare(cachedDevice: null);
        var activatedAfterPersistence = false;
        viewModel.DeviceActivatedAsync += (_, args) =>
        {
            activatedAfterPersistence = workflow.CredentialsPersisted;
            Assert.Equal("1002", args.StoreCode);
            Assert.Equal("AUTH-001", args.AuthorizationCode);
            Assert.False(args.IsReregistered);
            return Task.CompletedTask;
        };

        scanner.Emit(" hbdev1-0123456789abcdefghjkmnpqrs-6789abcdefghjkmnpqrstvwxyz ");
        await Assert.IsAssignableFrom<Task>(viewModel.PreviewActivationCommand.ExecutionTask);

        Assert.Equal(1, workflow.PreviewCallCount);
        Assert.Equal(ActivationCode, workflow.LastActivationCode);
        Assert.True(viewModel.HasActivationPreview);
        Assert.Equal("Lutwyche (1002)", viewModel.PreviewStoreDisplay);
        Assert.Equal(DeviceSystems.Windows, viewModel.PreviewDeviceSystem);
        Assert.True(viewModel.ConfirmActivationCommand.CanExecute(null));
        Assert.Equal(0, workflow.RedeemCallCount);

        await viewModel.ConfirmActivationCommand.ExecuteAsync(null);

        Assert.Equal(1, workflow.RedeemCallCount);
        Assert.True(workflow.CredentialsPersisted);
        Assert.True(activatedAfterPersistence);
        Assert.Equal(string.Empty, viewModel.ActivationCode);
    }

    [Fact]
    public async Task Scanner_ignores_second_delivery_while_preview_is_in_flight()
    {
        var previewResult = new TaskCompletionSource<DeviceActivationPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FakeActivationWorkflow { PendingPreview = previewResult };
        using var viewModel = new DeviceRegistrationViewModel(workflow);
        viewModel.Prepare(cachedDevice: null);

        Assert.True(viewModel.ProcessScannerBarcode(ActivationCode, "scanner-device", "raw"));
        await workflow.PreviewStarted.Task;
        Assert.True(viewModel.ProcessScannerBarcode(ActivationCode, "keyboard", "keyboard-fallback"));

        Assert.Equal(1, workflow.PreviewCallCount);
        previewResult.SetResult(FakeActivationWorkflow.Preview);
        await Assert.IsAssignableFrom<Task>(viewModel.PreviewActivationCommand.ExecutionTask);
        Assert.True(viewModel.HasActivationPreview);
    }

    [Fact]
    public async Task Reregister_confirmation_uses_rebind_and_marks_activation_as_store_change()
    {
        var workflow = new FakeActivationWorkflow();
        using var viewModel = new DeviceRegistrationViewModel(workflow);
        viewModel.PrepareReregister("1001");
        DeviceActivatedEventArgs? activated = null;
        viewModel.DeviceActivatedAsync += (_, args) =>
        {
            activated = args;
            return Task.CompletedTask;
        };
        viewModel.ActivationCode = ActivationCode;

        await viewModel.PreviewActivationCommand.ExecuteAsync(null);

        Assert.Equal("1001 → Lutwyche (1002)", viewModel.StoreTransitionDisplay);
        await viewModel.ConfirmActivationCommand.ExecuteAsync(null);

        Assert.Equal(1, workflow.RebindCallCount);
        Assert.Equal(0, workflow.RedeemCallCount);
        Assert.NotNull(activated);
        Assert.True(activated!.IsReregistered);
        Assert.True(workflow.CredentialsPersisted);
    }

    [Fact]
    public async Task Initialize_recovers_encrypted_pending_code_without_requiring_preview_again()
    {
        var workflow = new FakeActivationWorkflow
        {
            RecoveryMode = DeviceActivationRecoveryMode.Rebind
        };
        using var viewModel = new DeviceRegistrationViewModel(workflow);
        DeviceActivatedEventArgs? activated = null;
        viewModel.DeviceActivatedAsync += (_, args) =>
        {
            activated = args;
            return Task.CompletedTask;
        };

        await viewModel.InitializeAsync(cachedDevice: null);

        Assert.Equal(1, workflow.RecoveryCallCount);
        Assert.Equal(0, workflow.PreviewCallCount);
        Assert.True(workflow.CredentialsPersisted);
        Assert.True(activated?.IsReregistered);
    }

    private sealed class FakeActivationWorkflow : IDeviceRegistrationWorkflowService
    {
        public static readonly DeviceActivationPreviewResult Preview = new(
            ActivationCode,
            "1002",
            "Lutwyche",
            "Windows",
            DateTimeOffset.UtcNow.AddMinutes(15));

        public int PreviewCallCount { get; private set; }

        public int RedeemCallCount { get; private set; }

        public int RebindCallCount { get; private set; }

        public int RecoveryCallCount { get; private set; }

        public string? LastActivationCode { get; private set; }

        public bool CredentialsPersisted { get; private set; }

        public TaskCompletionSource PreviewStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<DeviceActivationPreviewResult>? PendingPreview { get; init; }

        public DeviceActivationRecoveryMode? RecoveryMode { get; init; }

        public string GetHardwareId() => "HW-001";

        public Task<DeviceActivationPreviewResult> PreviewActivationCodeAsync(
            string activationCode,
            CancellationToken cancellationToken = default)
        {
            PreviewCallCount++;
            LastActivationCode = activationCode;
            PreviewStarted.TrySetResult();
            return PendingPreview?.Task ?? Task.FromResult(Preview);
        }

        public Task<DeviceRegistrationActionResult> RedeemActivationCodeAsync(
            string activationCode,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            RedeemCallCount++;
            LastActivationCode = activationCode;
            return Task.FromResult(CreateAllowedAction(isRebind: false));
        }

        public Task<DeviceRegistrationActionResult> RebindActivationCodeAsync(
            string activationCode,
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            RebindCallCount++;
            LastActivationCode = activationCode;
            return Task.FromResult(CreateAllowedAction(isRebind: true));
        }

        public Task<DeviceActivationRecoveryResult?> RecoverActivationCodeAsync(
            string hardwareId,
            CancellationToken cancellationToken = default)
        {
            RecoveryCallCount++;
            return Task.FromResult(RecoveryMode is null
                ? null
                : new DeviceActivationRecoveryResult(
                    CreateAllowedAction(RecoveryMode == DeviceActivationRecoveryMode.Rebind),
                    RecoveryMode.Value));
        }

        private DeviceRegistrationActionResult CreateAllowedAction(bool isRebind)
        {
            return new DeviceRegistrationActionResult(
                "POS-001",
                "1002",
                "Lutwyche",
                "HW-001",
                false,
                "Enabled",
                "AUTH-001",
                true,
                false)
            {
                IsActivationRebind = isRebind,
                PersistAsync = PersistAsync
            };
        }

        public Task<DeviceRegistrationLoadResult> LoadStoresAsync(
            LocalDeviceCache? cachedDevice,
            bool isReregisterMode,
            string? excludedStoreCode = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeviceRegistrationActionResult> RegisterAsync(
            StoreSelectionItem selectedStore,
            string hardwareId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeviceRegistrationActionResult> VerifyAsync(
            StoreSelectionItem selectedStore,
            string deviceCode,
            string hardwareId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeviceRegistrationActionResult> ReregisterAsync(
            StoreSelectionItem selectedStore,
            string hardwareId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private async Task PersistAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            CredentialsPersisted = true;
        }

    }

    private sealed class TrackingRawScannerService : IRawScannerService
    {
        private readonly Dictionary<string, Action<RawBarcodeScannedEventArgs>> _handlers = [];

        public bool IsActive => true;

        public string? ActivePageId { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Subscribe(string pageId, Action<RawBarcodeScannedEventArgs> handler) => _handlers[pageId] = handler;

        public void Unsubscribe(string pageId) => _handlers.Remove(pageId);

        public void SetActivePage(string? pageId) => ActivePageId = pageId;

        public void Start(IntPtr hwnd) { }

        public void Stop() { }

        public void ClearPendingInput() { }

        public Task ResetBindingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public IntPtr ProcessWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) => IntPtr.Zero;

        public void Emit(string barcode)
        {
            Assert.True(_handlers.TryGetValue(DeviceRegistrationViewModel.PageId, out var handler));
            handler!(new RawBarcodeScannedEventArgs(barcode, "scanner-device", DateTimeOffset.UtcNow));
        }

        public void Dispose() => _handlers.Clear();
    }
}
