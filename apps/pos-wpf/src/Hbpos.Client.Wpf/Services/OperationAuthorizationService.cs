using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hbpos.Client.Wpf.Localization;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Client.Wpf.Services;

public interface IOperationAuthorizationService : INotifyPropertyChanged
{
    string ScannerPageId { get; }

    bool IsPromptOpen { get; }

    bool IsBusy { get; }

    string PromptMessage { get; }

    string StatusMessage { get; }

    string PermissionCode { get; }

    string Screen { get; }

    string Action { get; }

    IRelayCommand CancelCommand { get; }

    event EventHandler? StatusChanged;

    Task<OperationAuthorizationScope?> AuthorizeAsync(
        string permissionCode,
        string screen,
        string action,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    bool ProcessScannerBarcode(string barcode);

    void Cancel();

    void RevokeAll();
}

public sealed class OperationAuthorizationScope : IDisposable
{
    private static readonly AsyncLocal<AmbientState?> Ambient = new();
    private readonly ScopeLifetime _lifetime;
    private readonly Action<OperationAuthorizationScope>? _onDisposed;
    private int _disposed;

    internal OperationAuthorizationScope(
        CashierSessionDto requestingSession,
        string permissionCode,
        string screen,
        string action,
        Action<OperationAuthorizationScope>? onDisposed = null)
    {
        _lifetime = new ScopeLifetime(requestingSession, permissionCode, screen, action);
        _onDisposed = onDisposed;
    }

    public bool IsActive => _lifetime.IsActive;

    public IDisposable Activate()
    {
        ObjectDisposedException.ThrowIf(!IsActive, this);
        var state = new AmbientState(_lifetime);
        Ambient.Value = state;
        return new Activation(state);
    }

    public static IDisposable Suspend()
    {
        var previous = Ambient.Value;
        Ambient.Value = null;
        return new AmbientSuspension(previous);
    }

    internal static CashierSessionDto? CurrentAuthorizingSession
    {
        get
        {
            var state = Ambient.Value;
            return state?.IsActive == true ? state.Lifetime.AuthorizingSession : null;
        }
    }

    internal static OperationAuthorizationContext? CurrentAuthorizationContext
    {
        get
        {
            var state = Ambient.Value;
            var authorizer = state?.IsActive == true ? state.Lifetime.AuthorizingSession : null;
            return authorizer is null
                ? null
                : new OperationAuthorizationContext(
                    state!.Lifetime.RequestingSession,
                    authorizer,
                    state.Lifetime.PermissionCode,
                    state.Lifetime.Screen,
                    state.Lifetime.Action);
        }
    }

    internal void SetAuthorizingSession(CashierSessionDto session)
    {
        if (_lifetime.IsActive)
        {
            _lifetime.AuthorizingSession = session;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // 中文注释：派生 ExecutionContext 共享同一个可失效状态，防止 Dispose 后继续泄漏授权票据。
        _lifetime.Deactivate();
        if (ReferenceEquals(Ambient.Value?.Lifetime, _lifetime))
        {
            Ambient.Value = null;
        }

        _onDisposed?.Invoke(this);
    }

    private sealed class ScopeLifetime(
        CashierSessionDto requestingSession,
        string permissionCode,
        string screen,
        string action)
    {
        private int _isActive = 1;

        public bool IsActive => Volatile.Read(ref _isActive) == 1;

        public CashierSessionDto RequestingSession { get; } = requestingSession;

        public string PermissionCode { get; } = permissionCode;

        public string Screen { get; } = screen;

        public string Action { get; } = action;

        public CashierSessionDto? AuthorizingSession { get; set; }

        public void Deactivate() => Interlocked.Exchange(ref _isActive, 0);
    }

    private sealed class AmbientState(ScopeLifetime lifetime)
    {
        private int _isActive = 1;

        public ScopeLifetime Lifetime { get; } = lifetime;

        public bool IsActive =>
            Volatile.Read(ref _isActive) == 1 && Lifetime.IsActive;

        public void Deactivate() => Interlocked.Exchange(ref _isActive, 0);
    }

    private sealed class Activation(AmbientState state) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            state.Deactivate();
            if (ReferenceEquals(Ambient.Value, state))
            {
                Ambient.Value = null;
            }
        }
    }

    private sealed class AmbientSuspension(AmbientState? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Ambient.Value = previous;
            }
        }
    }
}

internal sealed record OperationAuthorizationContext(
    CashierSessionDto RequestingSession,
    CashierSessionDto AuthorizingSession,
    string PermissionCode,
    string Screen,
    string Action);

public sealed class OperationAuthorizationService : IOperationAuthorizationService
{
    public const string PageId = "OperationAuthorization";
    private readonly ICashierLoginService _cashierLoginService;
    private readonly ICashierSessionContext _cashierSessionContext;
    private readonly IOperationAuditLogger _auditLogger;
    private readonly TimeProvider _clock;
    private readonly ILocalizationService? _localization;
    private readonly object _gate = new();
    private readonly HashSet<OperationAuthorizationScope> _issuedScopes = [];
    private PendingAuthorization? _pending;
    private int _revocationGeneration;
    private bool _isPromptOpen;
    private bool _isBusy;
    private string _promptMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private string _permissionCode = string.Empty;
    private string _screen = string.Empty;
    private string _action = string.Empty;

    public OperationAuthorizationService(
        ICashierLoginService cashierLoginService,
        ICashierSessionContext cashierSessionContext,
        IOperationAuditLogger auditLogger,
        ILocalizationService? localization = null)
        : this(cashierLoginService, cashierSessionContext, auditLogger, TimeProvider.System, localization)
    {
    }

    internal OperationAuthorizationService(
        ICashierLoginService cashierLoginService,
        ICashierSessionContext cashierSessionContext,
        IOperationAuditLogger auditLogger,
        TimeProvider clock)
        : this(cashierLoginService, cashierSessionContext, auditLogger, clock, localization: null)
    {
    }

    private OperationAuthorizationService(
        ICashierLoginService cashierLoginService,
        ICashierSessionContext cashierSessionContext,
        IOperationAuditLogger auditLogger,
        TimeProvider clock,
        ILocalizationService? localization)
    {
        _cashierLoginService = cashierLoginService;
        _cashierSessionContext = cashierSessionContext;
        _auditLogger = auditLogger;
        _clock = clock;
        _localization = localization;
        CancelCommand = new RelayCommand(Cancel, () => IsPromptOpen);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? StatusChanged;

    public string ScannerPageId => PageId;

    public bool IsPromptOpen
    {
        get => _isPromptOpen;
        private set => SetProperty(ref _isPromptOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string PromptMessage
    {
        get => _promptMessage;
        private set => SetProperty(ref _promptMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public IRelayCommand CancelCommand { get; }

    public string PermissionCode
    {
        get => _permissionCode;
        private set => SetProperty(ref _permissionCode, value);
    }

    public string Screen
    {
        get => _screen;
        private set => SetProperty(ref _screen, value);
    }

    public string Action
    {
        get => _action;
        private set => SetProperty(ref _action, value);
    }

    public async Task<OperationAuthorizationScope?> AuthorizeAsync(
        string permissionCode,
        string screen,
        string action,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(screen);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var requestingSession = session.CashierSession;
        if (requestingSession is null)
        {
            StatusMessage = T("operationAuthorization.noSession", "No cashier session is active.");
            return null;
        }

        int generation;
        lock (_gate)
        {
            generation = _revocationGeneration;
        }

        if (_cashierSessionContext.HasPermission(permissionCode))
        {
            return CreateScope(requestingSession, permissionCode, screen, action, generation);
        }

        PendingAuthorization pending;
        lock (_gate)
        {
            if (_pending is not null)
            {
                StatusMessage = T("operationAuthorization.pending", "Another operation is already waiting for authorization.");
                return null;
            }

            var scope = CreateScope(requestingSession, permissionCode, screen, action, generation);
            if (scope is null)
            {
                return null;
            }
            pending = new PendingAuthorization(permissionCode, screen, action, session, scope);
            _pending = pending;
        }

        PromptMessage = T(
            "operationAuthorization.prompt",
            "Scan an authorized employee barcode with permission {0}.",
            permissionCode);
        PermissionCode = permissionCode;
        Screen = screen;
        Action = action;
        StatusMessage = T("operationAuthorization.waiting", "Waiting for authorization scan.");
        IsPromptOpen = true;
        NotifyCancelState();

        try
        {
            var authorized = await pending.Completion.Task.WaitAsync(cancellationToken);
            return authorized && pending.Scope.IsActive ? pending.Scope : null;
        }
        catch
        {
            pending.Scope.Dispose();
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                }
            }

            IsPromptOpen = false;
            IsBusy = false;
            PermissionCode = string.Empty;
            Screen = string.Empty;
            Action = string.Empty;
            NotifyCancelState();
        }
    }

    public bool ProcessScannerBarcode(string barcode)
    {
        PendingAuthorization? pending;
        lock (_gate)
        {
            pending = _pending;
            if (pending is not null &&
                (pending.IsCompleting ||
                 pending.Completion.Task.IsCompleted ||
                 !pending.Scope.IsActive))
            {
                return true;
            }
        }

        if (pending is null)
        {
            return false;
        }

        var normalized = barcode.Trim();
        if (normalized.Length == 0 || IsBusy)
        {
            return true;
        }

        _ = ProcessScannerBarcodeAsync(pending, normalized);
        return true;
    }

    public void Cancel()
    {
        PendingAuthorization? pending;
        lock (_gate)
        {
            pending = _pending;
        }

        if (pending is null)
        {
            return;
        }

        StatusMessage = T("operationAuthorization.cancelled", "Authorization cancelled.");
        var cancelled = false;
        lock (_gate)
        {
            if (pending.IsCompleting)
            {
                return;
            }

            pending.Scope.Dispose();
            cancelled = pending.Completion.TrySetResult(false);
        }

        if (cancelled)
        {
            RecordResult(pending, null, "Denied", "CANCELLED", "cancelled");
        }
    }

    public void RevokeAll()
    {
        lock (_gate)
        {
            _revocationGeneration++;
        }
        Cancel();
        OperationAuthorizationScope[] scopes;
        lock (_gate)
        {
            scopes = [.. _issuedScopes];
            _issuedScopes.Clear();
        }

        foreach (var scope in scopes)
        {
            scope.Dispose();
        }
    }

    private OperationAuthorizationScope? CreateScope(
        CashierSessionDto requestingSession,
        string permissionCode,
        string screen,
        string action,
        int expectedGeneration)
    {
        lock (_gate)
        {
            if (_revocationGeneration != expectedGeneration)
            {
                return null;
            }

            var scope = new OperationAuthorizationScope(
                requestingSession,
                permissionCode,
                screen,
                action,
                RemoveIssuedScope);
            _issuedScopes.Add(scope);
            return scope;
        }
    }

    private void RemoveIssuedScope(OperationAuthorizationScope scope)
    {
        lock (_gate)
        {
            _issuedScopes.Remove(scope);
        }
    }

    private async Task ProcessScannerBarcodeAsync(PendingAuthorization pending, string barcode)
    {
        IsBusy = true;
        StatusMessage = T("operationAuthorization.verifying", "Verifying authorized employee.");
        NotifyCancelState();
        try
        {
            var result = await _cashierLoginService.LoginAsync(
                pending.Session.StoreCode,
                pending.Session.DeviceCode,
                barcode,
                attemptOnline: pending.Session.IsOnline);
            if (!IsPendingActive(pending))
            {
                return;
            }

            var authorizer = result.Session;
            if (!result.Succeeded || authorizer is null)
            {
                StatusMessage = result.Message;
                RecordResult(
                    pending,
                    null,
                    "Failed",
                    result.ErrorCode ?? "AUTHORIZATION_UNAVAILABLE",
                    result.ErrorCode == "CASHIER_LOGIN_API_UNAVAILABLE" ? "unavailable" : "online-rejected");
                return;
            }

            var validation = ValidateAuthorizer(pending, authorizer);
            if (validation is not null)
            {
                StatusMessage = validation.Message;
                RecordResult(
                    pending,
                    authorizer,
                    "Denied",
                    validation.Code,
                    GetAuthorizationMode(authorizer));
                return;
            }

            lock (_gate)
            {
                if (!ReferenceEquals(_pending, pending) ||
                    !pending.Scope.IsActive ||
                    pending.Completion.Task.IsCompleted ||
                    pending.IsCompleting)
                {
                    return;
                }

                pending.IsCompleting = true;
                pending.Scope.SetAuthorizingSession(authorizer);
            }
            OperationAuditEvents.RecordPermissionOverride(
                _auditLogger,
                pending.Session,
                authorizer,
                pending.PermissionCode,
                pending.Screen,
                pending.Action,
                "Succeeded",
                reasonCode: null,
                authorizationMode: GetAuthorizationMode(authorizer));
            pending.Completion.TrySetResult(true);
            StatusMessage = T("operationAuthorization.success", "Authorization succeeded.");
        }
        catch (Exception ex)
        {
            if (!IsPendingActive(pending))
            {
                return;
            }

            StatusMessage = T(
                "operationAuthorization.exception",
                "Authorization verification failed: {0}",
                ex.GetType().Name);
            RecordResult(
                pending,
                null,
                "Failed",
                $"AUTHORIZATION_EXCEPTION_{ex.GetType().Name.ToUpperInvariant()}",
                "unavailable");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    IsBusy = false;
                    NotifyCancelState();
                }
            }
        }
    }

    private bool IsPendingActive(PendingAuthorization pending)
    {
        lock (_gate)
        {
            return ReferenceEquals(_pending, pending) &&
                pending.Scope.IsActive &&
                !pending.Completion.Task.IsCompleted &&
                !pending.IsCompleting;
        }
    }

    private AuthorizationValidationError? ValidateAuthorizer(
        PendingAuthorization pending,
        CashierSessionDto authorizer)
    {
        if (authorizer.IsEmergencyOverride)
        {
            return new(
                "EMERGENCY_OVERRIDE_DENIED",
                T("operationAuthorization.emergencyDenied", "Emergency authorization sessions cannot approve permission overrides."));
        }

        if (!string.Equals(authorizer.StoreCode, pending.Session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(authorizer.DeviceCode, pending.Session.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                "STORE_OR_DEVICE_MISMATCH",
                T("operationAuthorization.scopeMismatch", "The authorizing employee session does not match this store or device."));
        }

        var now = _clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(authorizer.AuthorizationToken) ||
            authorizer.AuthorizationExpiresAtUtc is null ||
            authorizer.AuthorizationExpiresAtUtc <= now)
        {
            return new(
                "AUTHORIZATION_TICKET_INVALID",
                T("operationAuthorization.ticketInvalid", "The authorizing employee ticket is invalid or expired."));
        }

        var authorizerContext = new CashierSessionContext(_clock);
        authorizerContext.SetCurrent(authorizer);
        return authorizerContext.HasPermission(pending.PermissionCode)
            ? null
            : new(
                "PERMISSION_DENIED",
                T("operationAuthorization.permissionDenied", "This employee does not have permission to approve the operation."));
    }

    private string T(string key, string fallback, params object[] args)
    {
        var value = _localization?.T(key);
        var template = string.IsNullOrWhiteSpace(value) || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
        return args.Length == 0
            ? template
            : string.Format(_localization?.CurrentCulture ?? System.Globalization.CultureInfo.CurrentCulture, template, args);
    }

    private void RecordResult(
        PendingAuthorization pending,
        CashierSessionDto? authorizer,
        string outcome,
        string reasonCode,
        string authorizationMode)
    {
        OperationAuditEvents.RecordPermissionOverride(
            _auditLogger,
            pending.Session,
            authorizer,
            pending.PermissionCode,
            pending.Screen,
            pending.Action,
            outcome,
            reasonCode,
            authorizationMode);
    }

    private static string GetAuthorizationMode(CashierSessionDto session) =>
        session.IsOfflineCached ? "offline-cache" : "online";

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void NotifyCancelState()
    {
        CancelCommand.NotifyCanExecuteChanged();
    }

    private sealed record PendingAuthorization(
        string PermissionCode,
        string Screen,
        string Action,
        PosSessionState Session,
        OperationAuthorizationScope Scope)
    {
        public bool IsCompleting { get; set; }

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record AuthorizationValidationError(string Code, string Message);
}
