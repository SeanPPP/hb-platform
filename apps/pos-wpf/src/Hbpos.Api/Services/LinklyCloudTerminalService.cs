using System.Net;
using BlazorApp.Shared.Security;
using Hbpos.Api.Data;
using Hbpos.Contracts.Linkly;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface ILinklyCloudTerminalService
{
    Task<LinklyCloudTerminalListResponse> GetTerminalsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken);

    Task<string> GetConfigurationModeAsync(
        string environment,
        string storeCode,
        CancellationToken cancellationToken);

    Task<LinklyCloudTerminalSelectionResponse> SelectTerminalAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudTerminalSelectionRequest request,
        string? updatedBy,
        CancellationToken cancellationToken);

    Task<LinklyCloudTerminalPairResponse> PairTerminalAsync(
        string storeCode,
        string deviceCode,
        Guid terminalId,
        LinklyCloudBackendPairRequest request,
        string? updatedBy,
        CancellationToken cancellationToken);

    Task<LinklyCloudTerminalPaymentContext?> ResolvePaymentTerminalAsync(
        string environment,
        string storeCode,
        string deviceCode,
        Guid? terminalId,
        long? selectionRevision,
        CancellationToken cancellationToken);

    Task<LinklyCloudTerminalRecord?> GetTerminalAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken);

    // 响应展示只能读取非敏感元数据；默认返回空，禁止替代实现意外回退到凭据解密。
    Task<string?> GetTerminalDisplayNameAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    Task<LinklyCloudTerminalOperationLease> AcquireOperationLeaseAsync(
        string environment,
        string storeCode,
        string deviceCode,
        LinklyCloudTerminalPaymentContext terminalContext,
        CancellationToken cancellationToken);

    Task ReleaseOperationLeaseAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid leaseId,
        CancellationToken cancellationToken);

    // 健康快照只记录已明确完成的实体终端探测；不得影响配置版本、配对或 POS 选择。
    Task<bool> RecordHealthAsync(
        LinklyCloudTerminalPaymentContext terminalContext,
        string healthStatus,
        DateTime checkedAt,
        CancellationToken cancellationToken);
}

public interface ILinklyCloudTerminalRepository
{
    Task<IReadOnlyList<LinklyCloudTerminalRecord>> ListAsync(
        string environment,
        string storeCode,
        CancellationToken cancellationToken);

    Task<LinklyCloudTerminalRecord?> GetAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken);

    // 元数据查询与运行时凭据查询分离，状态/回单响应不得触碰 Password 或 Secret。
    Task<string?> GetDisplayNameAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    Task<LinklyCloudDeviceSelectionRecord?> GetSelectionAsync(
        string environment,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken);

    Task<LinklyCloudDeviceSelectionRecord> UpsertSelectionAsync(
        string environment,
        string storeCode,
        string deviceCode,
        Guid terminalId,
        long? expectedRevision,
        DateTime updatedAt,
        string? updatedBy,
        CancellationToken cancellationToken);

    Task<string> GetConfigurationModeAsync(
        string environment,
        string storeCode,
        CancellationToken cancellationToken);

    Task<LinklyCloudTerminalRecord?> TryBeginPairingAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid pairingAttemptId,
        DateTime pairingLeaseExpiresAt,
        DateTime expectedUpdatedAt,
        DateTime updatedAt,
        string? updatedBy,
        CancellationToken cancellationToken);

    Task<LinklyCloudTerminalRecord> UpdatePairingAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid expectedPairingAttemptId,
        DateTime expectedUpdatedAt,
        string pairingState,
        string? secret,
        string? posId,
        DateTime updatedAt,
        string? updatedBy,
        CancellationToken cancellationToken);

    Task ReleasePairingLeaseAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid expectedPairingAttemptId,
        CancellationToken cancellationToken);

    Task<bool> TryAcquireOperationLeaseAsync(
        string environment,
        string storeCode,
        string deviceCode,
        Guid terminalId,
        long expectedSelectionRevision,
        DateTime expectedTerminalUpdatedAt,
        Guid operationLeaseId,
        DateTime operationLeaseExpiresAt,
        DateTime now,
        CancellationToken cancellationToken);

    Task ReleaseOperationLeaseAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid expectedOperationLeaseId,
        CancellationToken cancellationToken);

    // 使用终端配置版本作 CAS，防止迟到的探测结果覆盖已重配对或已更新凭据的终端。
    Task<bool> TryRecordHealthAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        DateTime expectedTerminalUpdatedAt,
        string healthStatus,
        DateTime checkedAt,
        CancellationToken cancellationToken);
}

public sealed record LinklyCloudTerminalPaymentContext(
    LinklyCloudTerminalRecord Terminal,
    LinklyCloudDeviceSelectionRecord Selection);

public sealed record LinklyCloudTerminalOperationLease(
    Guid LeaseId,
    Guid TerminalId,
    DateTime ExpiresAt);

public sealed record LinklyCloudTerminalRecord
{
    public Guid TerminalId { get; init; }

    public string Environment { get; init; } = string.Empty;

    public string StoreCode { get; init; } = string.Empty;

    public int LaneNo { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string? Secret { get; init; }

    public byte CredentialProtectionVersion { get; init; } =
        LinklyCloudTerminalCredentialDataProtection.LegacyPlaintextVersion;

    internal bool HasUsableSecret { get; init; }

    public string? PosId { get; init; }

    public string PairingState { get; init; } = "Unpaired";

    public Guid? PairingAttemptId { get; init; }

    public DateTime? PairingLeaseExpiresAt { get; init; }

    public string? LastHealthStatus { get; init; }

    public DateTime? LastHealthAt { get; init; }

    public DateTime? CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public string? CreatedBy { get; init; }

    public string? UpdatedBy { get; init; }
}

public sealed class LinklyCloudDeviceSelectionRecord
{
    public string Environment { get; set; } = string.Empty;

    public string StoreCode { get; set; } = string.Empty;

    public string DeviceCode { get; set; } = string.Empty;

    public Guid TerminalId { get; set; }

    public long Revision { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

public sealed class LinklyCloudTerminalNotFoundException()
    : Exception("Linkly Cloud terminal was not found in the authenticated store and environment.");

public sealed class LinklyCloudTerminalNotReadyException()
    : Exception("Linkly Cloud terminal is not paired and ready.");

public sealed class LinklyCloudTerminalSelectionConflictException(
    string message = "Linkly Cloud terminal selection changed or the terminal is busy.") : Exception(message);

public sealed class LinklyCloudTerminalAssignedException()
    : Exception("Linkly Cloud terminal is already assigned to another POS.");

public sealed class LinklyCloudTerminalSessionActiveException()
    : Exception("The Linkly Cloud terminal has an active or unacknowledged operation and cannot be paired.");

public sealed class LinklyCloudTerminalPairingConflictException()
    : Exception("The Linkly Cloud terminal changed while pairing. Refresh the terminal list and pair again.");

public sealed class LinklyCloudTerminalCredentialReentryRequiredException()
    : Exception("Linkly Cloud terminal credentials must be re-entered in the management portal.");

public sealed class LinklyCloudTerminalCredentialUnavailableException()
    : Exception("Linkly Cloud terminal credentials are unavailable. Re-enter them in the management portal.");

public sealed class LinklyCloudTerminalService(
    ILinklyCloudTerminalRepository repository,
    ILinklyCloudBackendAsyncRepository sessionRepository,
    ILinklyCloudPairingTransport pairingTransport,
    IOptions<LinklyCloudBackendAsyncOptions> options,
    ILogger<LinklyCloudTerminalService>? logger = null) : ILinklyCloudTerminalService
{
    private static readonly TimeSpan PairingLeaseDuration =
        LinklyTimeoutConstants.HttpTimeout + TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OperationLeaseDuration =
        LinklyTimeoutConstants.HttpTimeout + LinklyTimeoutConstants.HttpTimeout + TimeSpan.FromMinutes(1);

    public async Task<LinklyCloudTerminalListResponse> GetTerminalsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = NormalizeEnvironment(environment);
        var normalizedStoreCode = NormalizeRequired(storeCode, "storeCode");
        var normalizedDeviceCode = NormalizeRequired(deviceCode, "deviceCode");
        var terminals = await repository.ListAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            cancellationToken);
        var selection = await repository.GetSelectionAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            normalizedDeviceCode,
            cancellationToken);
        var mode = await repository.GetConfigurationModeAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            cancellationToken);

        var summaries = new List<LinklyCloudTerminalSummary>(terminals.Count);
        foreach (var terminal in terminals.OrderBy(item => item.LaneNo).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var active = await sessionRepository.GetActiveSessionByTerminalAsync(
                normalizedEnvironment,
                normalizedStoreCode,
                terminal.TerminalId,
                cancellationToken);
            summaries.Add(new LinklyCloudTerminalSummary(
                terminal.TerminalId,
                terminal.LaneNo,
                terminal.DisplayName,
                terminal.PairingState,
                active is not null,
                IsReady(terminal),
                terminal.LastHealthStatus,
                ToDateTimeOffset(terminal.LastHealthAt)));
        }

        return new LinklyCloudTerminalListResponse(
            normalizedEnvironment,
            selection?.TerminalId,
            selection?.Revision,
            summaries,
            mode);
    }

    public Task<string> GetConfigurationModeAsync(
        string environment,
        string storeCode,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = NormalizeEnvironment(environment);
        var normalizedStoreCode = NormalizeRequired(storeCode, "storeCode");
        return repository.GetConfigurationModeAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            cancellationToken);
    }

    public async Task<LinklyCloudTerminalSelectionResponse> SelectTerminalAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudTerminalSelectionRequest request,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var environment = NormalizeEnvironment(request.Environment);
        var normalizedStoreCode = NormalizeRequired(storeCode, "storeCode");
        var normalizedDeviceCode = NormalizeRequired(deviceCode, "deviceCode");
        if (request.TerminalId == Guid.Empty)
        {
            throw new LinklyCloudBackendValidationException("terminalId is required.");
        }

        var terminal = await GetRequiredTerminalAsync(
            environment,
            normalizedStoreCode,
            request.TerminalId,
            cancellationToken);
        if (!IsReady(terminal))
        {
            throw new LinklyCloudTerminalNotReadyException();
        }

        var resumable = await sessionRepository.GetResumableSessionAsync(
            environment,
            normalizedStoreCode,
            normalizedDeviceCode,
            cancellationToken);
        if (resumable is not null)
        {
            throw new LinklyCloudTerminalSelectionConflictException(
                "Current POS has a Linkly Cloud transaction that must be recovered or acknowledged before switching terminals.");
        }

        var current = await repository.GetSelectionAsync(
            environment,
            normalizedStoreCode,
            normalizedDeviceCode,
            cancellationToken);
        if (current is not null && request.ExpectedRevision != current.Revision ||
            current is null && request.ExpectedRevision is not null and not 0)
        {
            throw new LinklyCloudTerminalSelectionConflictException();
        }

        var saved = await repository.UpsertSelectionAsync(
            environment,
            normalizedStoreCode,
            normalizedDeviceCode,
            terminal.TerminalId,
            request.ExpectedRevision,
            DateTime.UtcNow,
            NormalizeOptional(updatedBy),
            cancellationToken);
        return new LinklyCloudTerminalSelectionResponse(environment, saved.TerminalId, saved.Revision);
    }

    public async Task<LinklyCloudTerminalPairResponse> PairTerminalAsync(
        string storeCode,
        string deviceCode,
        Guid terminalId,
        LinklyCloudBackendPairRequest request,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var environment = NormalizeEnvironment(request.Environment);
        var normalizedStoreCode = NormalizeRequired(storeCode, "storeCode");
        _ = NormalizeRequired(deviceCode, "deviceCode");
        var pairCode = NormalizePairCode(request.PairCode);
        if (terminalId == Guid.Empty)
        {
            throw new LinklyCloudPairingValidationException("terminalId is required.");
        }

        var terminal = await GetRequiredTerminalAsync(
            environment,
            normalizedStoreCode,
            terminalId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(terminal.Username) || string.IsNullOrWhiteSpace(terminal.Password))
        {
            throw new LinklyCloudPairingCredentialMissingException();
        }

        var username = terminal.Username.Trim();
        var password = terminal.Password.Trim();
        var expectedUpdatedAt = terminal.UpdatedAt
            ?? throw new LinklyCloudTerminalPairingConflictException();
        var originalPairingState = terminal.PairingState;
        var originalSecret = terminal.Secret;
        var originalPosId = terminal.PosId;
        var pairingAttemptId = Guid.NewGuid();
        var markerUpdatedAt = NextUpdatedAt(expectedUpdatedAt);
        // 数据库租约是跨 API 实例的唯一配对闸门；超时/网络不确定时保留租约，避免重复上游配对。
        var pairingMarker = await repository.TryBeginPairingAsync(
            environment,
            normalizedStoreCode,
            terminalId,
            pairingAttemptId,
            markerUpdatedAt.Add(PairingLeaseDuration),
            expectedUpdatedAt,
            markerUpdatedAt,
            NormalizeOptional(updatedBy),
            cancellationToken);
        if (pairingMarker is null)
        {
            var blockingSession = await sessionRepository.GetBlockingSessionByTerminalAsync(
                environment,
                normalizedStoreCode,
                terminalId,
                cancellationToken);
            if (blockingSession is not null)
            {
                throw new LinklyCloudTerminalSessionActiveException();
            }

            var current = await repository.GetAsync(
                environment,
                normalizedStoreCode,
                terminalId,
                cancellationToken);
            if (current?.PairingAttemptId is not null &&
                current.PairingLeaseExpiresAt is not null &&
                current.PairingLeaseExpiresAt > DateTime.UtcNow)
            {
                throw new LinklyCloudPairingInProgressException();
            }

            throw new LinklyCloudTerminalPairingConflictException();
        }

        if (pairingMarker.PairingAttemptId != pairingAttemptId || pairingMarker.UpdatedAt != markerUpdatedAt)
        {
            throw new LinklyCloudTerminalPairingConflictException();
        }

        LinklyCloudPairingTransportResponse upstream;
        try
        {
            upstream = await pairingTransport.PairAsync(
                GetAuthBaseUrl(environment),
                username,
                password,
                pairCode,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new LinklyCloudPairingTimeoutException();
        }
        catch (Exception ex)
        {
            LogPairFailure(environment, normalizedStoreCode, terminalId, ex);
            throw new LinklyCloudPairingUpstreamException();
        }

        if (upstream.StatusCode == HttpStatusCode.RequestTimeout)
        {
            throw new LinklyCloudPairingTimeoutException();
        }

        if ((int)upstream.StatusCode is >= 400 and < 500)
        {
            await RestorePairingStateAsync(
                pairingMarker,
                originalPairingState,
                originalSecret,
                originalPosId,
                updatedBy);
            throw new LinklyCloudPairingRejectedException();
        }

        if (!((int)upstream.StatusCode is >= 200 and < 300) || string.IsNullOrWhiteSpace(upstream.Secret))
        {
            throw new LinklyCloudPairingUpstreamException();
        }

        var posId = IsUuidV4(terminal.PosId)
            ? terminal.PosId!.Trim()
            : Guid.NewGuid().ToString("D");
        LinklyCloudTerminalRecord saved;
        try
        {
            saved = await repository.UpdatePairingAsync(
                environment,
                normalizedStoreCode,
                terminalId,
                pairingAttemptId,
                pairingMarker.UpdatedAt ?? throw new LinklyCloudTerminalPairingConflictException(),
                "Ready",
                upstream.Secret.Trim(),
                posId,
                NextUpdatedAt(pairingMarker.UpdatedAt),
                NormalizeOptional(updatedBy),
                CancellationToken.None);
        }
        catch (LinklyCloudTerminalPairingConflictException)
        {
            // 上游已经成功，Pair Code 可能已被消费；CAS 冲突属于未知结果，必须保留租约阻止重放。
            throw new LinklyCloudPairingPersistenceException();
        }
        catch (Exception ex)
        {
            LogPairFailure(environment, normalizedStoreCode, terminalId, ex);
            throw new LinklyCloudPairingPersistenceException();
        }

        if (!IsReady(saved))
        {
            throw new LinklyCloudPairingPersistenceException();
        }

        return new LinklyCloudTerminalPairResponse(
            saved.TerminalId,
            saved.Environment,
            saved.DisplayName,
            saved.PairingState,
            true,
            "Linkly Cloud terminal paired successfully.");
    }

    public async Task<LinklyCloudTerminalPaymentContext?> ResolvePaymentTerminalAsync(
        string environment,
        string storeCode,
        string deviceCode,
        Guid? terminalId,
        long? selectionRevision,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = NormalizeEnvironment(environment);
        var normalizedStoreCode = NormalizeRequired(storeCode, "storeCode");
        var normalizedDeviceCode = NormalizeRequired(deviceCode, "deviceCode");
        var mode = await repository.GetConfigurationModeAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            cancellationToken);
        if (!string.Equals(mode, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (terminalId is null || terminalId == Guid.Empty || selectionRevision is null)
        {
            throw new LinklyCloudTerminalSelectionConflictException(
                "Active Linkly Cloud multi-terminal mode requires terminalId and selectionRevision.");
        }

        var selection = await repository.GetSelectionAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            normalizedDeviceCode,
            cancellationToken);
        if (selection is null ||
            selection.TerminalId != terminalId.Value ||
            selection.Revision != selectionRevision.Value)
        {
            throw new LinklyCloudTerminalSelectionConflictException();
        }

        var terminal = await GetRequiredTerminalAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            terminalId.Value,
            cancellationToken);
        if (!IsReady(terminal))
        {
            throw new LinklyCloudTerminalNotReadyException();
        }

        return new LinklyCloudTerminalPaymentContext(terminal, selection);
    }

    public Task<LinklyCloudTerminalRecord?> GetTerminalAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        return repository.GetAsync(
            NormalizeEnvironment(environment),
            NormalizeRequired(storeCode, "storeCode"),
            terminalId,
            cancellationToken);
    }

    public Task<string?> GetTerminalDisplayNameAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        return repository.GetDisplayNameAsync(
            NormalizeEnvironment(environment),
            NormalizeRequired(storeCode, "storeCode"),
            terminalId,
            cancellationToken);
    }

    public async Task<LinklyCloudTerminalOperationLease> AcquireOperationLeaseAsync(
        string environment,
        string storeCode,
        string deviceCode,
        LinklyCloudTerminalPaymentContext terminalContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminalContext);
        var normalizedEnvironment = NormalizeEnvironment(environment);
        var normalizedStoreCode = NormalizeRequired(storeCode, "storeCode");
        var normalizedDeviceCode = NormalizeRequired(deviceCode, "deviceCode");
        var terminal = terminalContext.Terminal;
        var selection = terminalContext.Selection;
        var expectedTerminalUpdatedAt = terminal.UpdatedAt
            ?? throw new LinklyCloudTerminalSelectionConflictException();
        if (!string.Equals(terminal.Environment, normalizedEnvironment, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(terminal.StoreCode, normalizedStoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selection.Environment, normalizedEnvironment, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selection.StoreCode, normalizedStoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selection.DeviceCode, normalizedDeviceCode, StringComparison.OrdinalIgnoreCase) ||
            selection.TerminalId != terminal.TerminalId)
        {
            throw new LinklyCloudTerminalSelectionConflictException();
        }

        var now = DateTime.UtcNow;
        var leaseId = Guid.NewGuid();
        var expiresAt = now.Add(OperationLeaseDuration);
        var acquired = await repository.TryAcquireOperationLeaseAsync(
            normalizedEnvironment,
            normalizedStoreCode,
            normalizedDeviceCode,
            terminal.TerminalId,
            selection.Revision,
            expectedTerminalUpdatedAt,
            leaseId,
            expiresAt,
            now,
            cancellationToken);
        if (!acquired)
        {
            var blockingSession = await sessionRepository.GetBlockingSessionByTerminalAsync(
                normalizedEnvironment,
                normalizedStoreCode,
                terminal.TerminalId,
                cancellationToken);
            if (blockingSession is not null)
            {
                throw new LinklyCloudBackendActiveTransactionException(blockingSession.SessionId);
            }

            var current = await repository.GetAsync(
                normalizedEnvironment,
                normalizedStoreCode,
                terminal.TerminalId,
                cancellationToken);
            if (current?.PairingAttemptId is not null &&
                current.PairingLeaseExpiresAt is not null &&
                current.PairingLeaseExpiresAt > now)
            {
                throw new LinklyCloudBackendActiveTransactionException(null);
            }

            throw new LinklyCloudTerminalSelectionConflictException();
        }

        return new LinklyCloudTerminalOperationLease(leaseId, terminal.TerminalId, expiresAt);
    }

    public Task ReleaseOperationLeaseAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        return repository.ReleaseOperationLeaseAsync(
            NormalizeEnvironment(environment),
            NormalizeRequired(storeCode, "storeCode"),
            terminalId,
            leaseId,
            cancellationToken);
    }

    public Task<bool> RecordHealthAsync(
        LinklyCloudTerminalPaymentContext terminalContext,
        string healthStatus,
        DateTime checkedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminalContext);
        var expectedUpdatedAt = terminalContext.Terminal.UpdatedAt
            ?? throw new LinklyCloudTerminalSelectionConflictException();
        var normalizedStatus = healthStatus.Trim();
        if (!string.Equals(normalizedStatus, "Healthy", StringComparison.Ordinal) &&
            !string.Equals(normalizedStatus, "Unhealthy", StringComparison.Ordinal))
        {
            throw new ArgumentException("healthStatus must be Healthy or Unhealthy.", nameof(healthStatus));
        }

        return repository.TryRecordHealthAsync(
            NormalizeEnvironment(terminalContext.Terminal.Environment),
            NormalizeRequired(terminalContext.Terminal.StoreCode, "storeCode"),
            terminalContext.Terminal.TerminalId,
            expectedUpdatedAt,
            normalizedStatus,
            DateTime.SpecifyKind(checkedAt, DateTimeKind.Utc),
            cancellationToken);
    }

    private async Task<LinklyCloudTerminalRecord> GetRequiredTerminalAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        return await repository.GetAsync(environment, storeCode, terminalId, cancellationToken)
            ?? throw new LinklyCloudTerminalNotFoundException();
    }

    private async Task RestorePairingStateAsync(
        LinklyCloudTerminalRecord pairingMarker,
        string pairingState,
        string? secret,
        string? posId,
        string? updatedBy)
    {
        try
        {
            await repository.UpdatePairingAsync(
                pairingMarker.Environment,
                pairingMarker.StoreCode,
                pairingMarker.TerminalId,
                pairingMarker.PairingAttemptId ?? throw new LinklyCloudTerminalPairingConflictException(),
                pairingMarker.UpdatedAt ?? throw new LinklyCloudTerminalPairingConflictException(),
                pairingState,
                secret,
                posId,
                NextUpdatedAt(pairingMarker.UpdatedAt),
                NormalizeOptional(updatedBy),
                CancellationToken.None);
        }
        catch (LinklyCloudTerminalPairingConflictException)
        {
            // Web 已写入更新版本时以最新状态为准，绝不恢复旧配对材料。
            if (pairingMarker.PairingAttemptId is { } attemptId)
            {
                await ReleasePairingLeaseSafelyAsync(
                    pairingMarker.Environment,
                    pairingMarker.StoreCode,
                    pairingMarker.TerminalId,
                    attemptId);
            }
        }
        catch (Exception ex)
        {
            LogPairFailure(pairingMarker.Environment, pairingMarker.StoreCode, pairingMarker.TerminalId, ex);
        }
    }

    private async Task ReleasePairingLeaseSafelyAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid pairingAttemptId)
    {
        try
        {
            await repository.ReleasePairingLeaseAsync(
                environment,
                storeCode,
                terminalId,
                pairingAttemptId,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 上游结果已明确但并发管理端写入使 CAS 失效；租约清理失败时仅等待自然过期。
            LogPairFailure(environment, storeCode, terminalId, ex);
        }
    }

    private string GetAuthBaseUrl(string environment)
    {
        return string.Equals(environment, "Sandbox", StringComparison.Ordinal)
            ? options.Value.SandboxAuthBaseUrl
            : options.Value.ProductionAuthBaseUrl;
    }

    private void LogPairFailure(string environment, string storeCode, Guid terminalId, Exception exception)
    {
        // 配对日志只保留范围及异常类型；账号、密码、Pair Code、Secret 与 PosId 永不记录。
        logger?.LogWarning(
            "Linkly Cloud terminal pairing failed environment={Environment} store={StoreCode} terminalId={TerminalId} error={ErrorType}",
            environment,
            storeCode,
            terminalId,
            exception.GetType().Name);
    }

    private static bool IsReady(LinklyCloudTerminalRecord terminal)
    {
        return terminal.CredentialProtectionVersion ==
                LinklyCloudTerminalCredentialDataProtection.CurrentVersion &&
            string.Equals(terminal.PairingState, "Ready", StringComparison.OrdinalIgnoreCase) &&
            (terminal.HasUsableSecret || !string.IsNullOrWhiteSpace(terminal.Secret)) &&
            IsUuidV4(terminal.PosId);
    }

    private static string NormalizeEnvironment(string? environment)
    {
        return LinklyCloudCredentialService.NormalizeEnvironment(environment)
            ?? throw new LinklyCloudBackendValidationException("environment must be Production or Sandbox");
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new LinklyCloudBackendValidationException($"{fieldName} is required.")
            : value.Trim();
    }

    private static string NormalizePairCode(string? pairCode)
    {
        var normalized = pairCode?.Trim();
        if (normalized is null || normalized.Length != 6 || normalized.Any(character => character is < '0' or > '9'))
        {
            throw new LinklyCloudPairingValidationException("pairCode must contain exactly 6 digits.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime NextUpdatedAt(DateTime? current)
    {
        var now = DateTime.UtcNow;
        return current.HasValue && now <= current.Value ? current.Value.AddTicks(1) : now;
    }

    private static bool IsUuidV4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized.Length == 36 &&
            Guid.TryParse(normalized, out _) &&
            normalized[14] == '4' &&
            normalized[19] is '8' or '9' or 'a' or 'A' or 'b' or 'B';
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null;
    }
}

public sealed class SqlSugarLinklyCloudTerminalRepository(
    HbposSqlSugarContext dbContext,
    ILinklyCloudTerminalCredentialProtector credentialProtector,
    ILogger<SqlSugarLinklyCloudTerminalRepository>? logger = null) : ILinklyCloudTerminalRepository
{
    internal const string TryRecordHealthSql = """
        UPDATE [dbo].[POSM_LinklyCloudTerminal]
        SET [LastHealthStatus] = @HealthStatus,
            [LastHealthAt] = @CheckedAt
        WHERE [Environment] = @Environment
          AND [StoreCode] = @StoreCode
          AND [TerminalId] = @TerminalId
          AND [UpdatedAt] = @ExpectedTerminalUpdatedAt
          AND ([LastHealthAt] IS NULL OR [LastHealthAt] <= @CheckedAt);
        """;

    internal const string GetDisplayNameSql = """
        SELECT TOP 1 [DisplayName]
        FROM [dbo].[POSM_LinklyCloudTerminal]
        WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode AND [TerminalId] = @TerminalId;
        """;

    internal const string UpsertSelectionSql = """
        SET XACT_ABORT ON;
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        BEGIN TRANSACTION;

        -- 全部写路径统一按“会话 -> 终端 -> 选择”取锁。
        IF EXISTS (
            SELECT TOP (1) 1
            FROM [dbo].[POSM_LinklyCloudBackendSession] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [DeviceCode] = @DeviceCode
              AND (
                  [IsActive] = 1
                  OR (
                      [Status] IN (N'Completed', N'Cancelled', N'Failed', N'NotSubmitted')
                      AND [ClientAcknowledgedAt] IS NULL
                  )
              ))
            THROW 51002, 'Current POS has a Linkly Cloud operation that must be recovered or acknowledged.', 1;

        IF NOT EXISTS (
            SELECT TOP (1) 1
            FROM [dbo].[POSM_LinklyCloudTerminal] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId
              AND [CredentialProtectionVersion] = 1
              AND [PairingState] = N'Ready'
              AND NULLIF(LTRIM(RTRIM([Secret])), N'') IS NOT NULL
              AND NULLIF(LTRIM(RTRIM([PosId])), N'') IS NOT NULL)
            THROW 51003, 'Linkly Cloud terminal is not paired and ready.', 1;

        -- 物理终端归属按环境、门店和 TerminalId 唯一；Serializable + 范围锁负责并发串行化，唯一索引兜底。
        IF EXISTS (
            SELECT TOP (1) 1
            FROM [dbo].[POSM_LinklyCloudDeviceSelection] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId
              AND [DeviceCode] <> @DeviceCode)
            THROW 51004, 'Linkly Cloud terminal is already assigned to another POS.', 1;

        IF EXISTS (
            SELECT 1 FROM [dbo].[POSM_LinklyCloudDeviceSelection] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode AND [DeviceCode] = @DeviceCode)
        BEGIN
            IF @ExpectedRevision IS NULL OR NOT EXISTS (
                SELECT 1 FROM [dbo].[POSM_LinklyCloudDeviceSelection]
                WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode
                  AND [DeviceCode] = @DeviceCode AND [Revision] = @ExpectedRevision)
                THROW 51001, 'Linkly Cloud terminal selection revision conflict.', 1;

            UPDATE [dbo].[POSM_LinklyCloudDeviceSelection]
            SET [TerminalId] = @TerminalId,
                [Revision] = [Revision] + 1,
                [UpdatedAt] = @UpdatedAt,
                [UpdatedBy] = @UpdatedBy
            WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode AND [DeviceCode] = @DeviceCode;
        END
        ELSE
        BEGIN
            IF @ExpectedRevision IS NOT NULL AND @ExpectedRevision <> 0
                THROW 51001, 'Linkly Cloud terminal selection revision conflict.', 1;

            INSERT INTO [dbo].[POSM_LinklyCloudDeviceSelection]
                ([Environment], [StoreCode], [DeviceCode], [TerminalId], [Revision], [UpdatedAt], [UpdatedBy])
            VALUES (@Environment, @StoreCode, @DeviceCode, @TerminalId, 1, @UpdatedAt, @UpdatedBy);
        END;
        COMMIT TRANSACTION;
        """;

    internal const string TryBeginPairingSql = """
        SET XACT_ABORT ON;
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        BEGIN TRANSACTION;
        DECLARE @HasBlockingSession BIT = 0;
        DECLARE @UpdatedTerminal TABLE (
            [TerminalId] UNIQUEIDENTIFIER,
            [Environment] NVARCHAR(32),
            [StoreCode] NVARCHAR(32),
            [LaneNo] INT,
            [DisplayName] NVARCHAR(128),
            [Username] NVARCHAR(128),
            [Password] NVARCHAR(2048),
            [Secret] NVARCHAR(2048),
            [CredentialProtectionVersion] TINYINT,
            [PosId] NVARCHAR(64),
            [PairingState] NVARCHAR(32),
            [PairingAttemptId] UNIQUEIDENTIFIER,
            [PairingLeaseExpiresAt] DATETIME2(7),
            [LastHealthStatus] NVARCHAR(32),
            [LastHealthAt] DATETIME2(7),
            [CreatedAt] DATETIME2(7),
            [UpdatedAt] DATETIME2(7),
            [CreatedBy] NVARCHAR(128),
            [UpdatedBy] NVARCHAR(128));

        SELECT TOP (1) @HasBlockingSession = 1
        FROM [dbo].[POSM_LinklyCloudBackendSession] WITH (UPDLOCK, HOLDLOCK)
        WHERE [Environment] = @Environment
          AND [StoreCode] = @StoreCode
          AND [TerminalId] = @TerminalId
          AND (
              [IsActive] = 1
              OR (
                  [Status] IN (N'Completed', N'Cancelled', N'Failed', N'NotSubmitted')
                  AND [ClientAcknowledgedAt] IS NULL
              )
          );

        IF @HasBlockingSession = 0
        BEGIN
            UPDATE [dbo].[POSM_LinklyCloudTerminal] WITH (UPDLOCK, HOLDLOCK)
            SET [PairingState] = N'Unknown',
                [PairingAttemptId] = @PairingAttemptId,
                [PairingLeaseExpiresAt] = @PairingLeaseExpiresAt,
                [UpdatedAt] = @UpdatedAt,
                [UpdatedBy] = @UpdatedBy
            OUTPUT inserted.[TerminalId], inserted.[Environment], inserted.[StoreCode], inserted.[LaneNo],
                   inserted.[DisplayName], inserted.[Username], inserted.[Password], inserted.[Secret],
                   inserted.[CredentialProtectionVersion], inserted.[PosId], inserted.[PairingState], inserted.[PairingAttemptId],
                   inserted.[PairingLeaseExpiresAt], inserted.[LastHealthStatus], inserted.[LastHealthAt],
                   inserted.[CreatedAt], inserted.[UpdatedAt], inserted.[CreatedBy], inserted.[UpdatedBy]
            INTO @UpdatedTerminal
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId
              AND [CredentialProtectionVersion] = 1
              AND [UpdatedAt] = @ExpectedUpdatedAt
              AND (
                  [PairingAttemptId] IS NULL
                  OR [PairingLeaseExpiresAt] IS NULL
                  OR [PairingLeaseExpiresAt] <= @UpdatedAt);
        END;

        COMMIT TRANSACTION;
        SELECT * FROM @UpdatedTerminal;
        """;

    internal const string TryAcquireOperationLeaseSql = """
        SET XACT_ABORT ON;
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        BEGIN TRANSACTION;
        DECLARE @HasBlockingSession BIT = 0;
        DECLARE @StoredTerminalId UNIQUEIDENTIFIER = NULL;
        DECLARE @StoredSelectionRevision BIGINT = NULL;
        DECLARE @Mode NVARCHAR(16) = N'Legacy';
        DECLARE @Acquired BIT = 0;

        -- 固定锁序：会话 -> 实体终端 -> POS 选择 -> 配置模式。
        SELECT TOP (1) @HasBlockingSession = 1
        FROM [dbo].[POSM_LinklyCloudBackendSession] WITH (UPDLOCK, HOLDLOCK)
        WHERE [Environment] = @Environment
          AND [StoreCode] = @StoreCode
          AND [DeviceCode] = @DeviceCode
          AND (
              [IsActive] = 1
              OR (
                  [Status] IN (N'Completed', N'Cancelled', N'Failed', N'NotSubmitted')
                  AND [ClientAcknowledgedAt] IS NULL
              )
          );

        IF @HasBlockingSession = 0
        BEGIN
            SELECT TOP (1) @HasBlockingSession = 1
            FROM [dbo].[POSM_LinklyCloudBackendSession] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId
              AND (
                  [IsActive] = 1
                  OR (
                      [Status] IN (N'Completed', N'Cancelled', N'Failed', N'NotSubmitted')
                      AND [ClientAcknowledgedAt] IS NULL
                  )
              );
        END;

        SELECT @StoredTerminalId = [TerminalId]
        FROM [dbo].[POSM_LinklyCloudTerminal] WITH (UPDLOCK, HOLDLOCK)
        WHERE [Environment] = @Environment
          AND [StoreCode] = @StoreCode
          AND [TerminalId] = @TerminalId
          AND [CredentialProtectionVersion] = 1
          AND [UpdatedAt] = @ExpectedTerminalUpdatedAt
          AND [PairingState] = N'Ready'
          AND NULLIF(LTRIM(RTRIM([Secret])), N'') IS NOT NULL
          AND NULLIF(LTRIM(RTRIM([PosId])), N'') IS NOT NULL
          AND (
              [PairingAttemptId] IS NULL
              OR [PairingLeaseExpiresAt] IS NULL
              OR [PairingLeaseExpiresAt] <= @Now
          );

        SELECT @StoredSelectionRevision = [Revision]
        FROM [dbo].[POSM_LinklyCloudDeviceSelection] WITH (UPDLOCK, HOLDLOCK)
        WHERE [Environment] = @Environment
          AND [StoreCode] = @StoreCode
          AND [DeviceCode] = @DeviceCode
          AND [TerminalId] = @TerminalId
          AND [Revision] = @ExpectedSelectionRevision;

        SELECT @Mode = [Mode]
        FROM [dbo].[POSM_LinklyCloudConfigurationMode] WITH (UPDLOCK, HOLDLOCK)
        WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode;

        IF @HasBlockingSession = 0
           AND @StoredTerminalId IS NOT NULL
           AND @StoredSelectionRevision IS NOT NULL
           AND ISNULL(@Mode, N'Legacy') = N'Active'
        BEGIN
            UPDATE [dbo].[POSM_LinklyCloudTerminal]
            SET [PairingAttemptId] = @OperationLeaseId,
                [PairingLeaseExpiresAt] = @OperationLeaseExpiresAt
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId;
            IF @@ROWCOUNT = 1 SET @Acquired = 1;
        END;

        COMMIT TRANSACTION;
        SELECT @Acquired AS [Acquired];
        """;

    public async Task<IReadOnlyList<LinklyCloudTerminalRecord>> ListAsync(
        string environment,
        string storeCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT [TerminalId], [Environment], [StoreCode], [LaneNo], [DisplayName],
                   [Username], [Password], [Secret], [CredentialProtectionVersion], [PosId], [PairingState],
                   [PairingAttemptId], [PairingLeaseExpiresAt],
                   [LastHealthStatus], [LastHealthAt], [CreatedAt], [UpdatedAt], [CreatedBy], [UpdatedBy]
            FROM [dbo].[POSM_LinklyCloudTerminal]
            WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode
            ORDER BY [LaneNo], [DisplayName];
            """;
        var stored = await dbContext.PosmDb.Ado.SqlQueryAsync<LinklyCloudTerminalRecord>(
            sql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode));
        return stored
            .Select(item => MaterializeListTerminal(item, credentialProtector))
            .ToArray();
    }

    public async Task<LinklyCloudTerminalRecord?> GetAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1 [TerminalId], [Environment], [StoreCode], [LaneNo], [DisplayName],
                   [Username], [Password], [Secret], [CredentialProtectionVersion], [PosId], [PairingState],
                   [PairingAttemptId], [PairingLeaseExpiresAt],
                   [LastHealthStatus], [LastHealthAt], [CreatedAt], [UpdatedAt], [CreatedBy], [UpdatedBy]
            FROM [dbo].[POSM_LinklyCloudTerminal]
            WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode AND [TerminalId] = @TerminalId;
            """;
        var stored = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<LinklyCloudTerminalRecord>(
            sql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@TerminalId", terminalId));
        return stored is null ? null : MaterializeRuntimeTerminalWithLogging(stored);
    }

    public async Task<string?> GetDisplayNameAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<LinklyCloudTerminalDisplayNameRow>(
            GetDisplayNameSql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@TerminalId", terminalId));
        return row?.DisplayName;
    }

    internal static LinklyCloudTerminalRecord MaterializeRuntimeTerminal(
        LinklyCloudTerminalRecord stored,
        ILinklyCloudTerminalCredentialProtector protector)
    {
        if (stored.CredentialProtectionVersion !=
            LinklyCloudTerminalCredentialDataProtection.CurrentVersion)
        {
            throw new LinklyCloudTerminalCredentialReentryRequiredException();
        }

        try
        {
            var password = protector.UnprotectPassword(stored.Password);
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new LinklyCloudTerminalCredentialUnavailableException();
            }

            string? secret = null;
            if (!string.IsNullOrWhiteSpace(stored.Secret))
            {
                secret = protector.UnprotectSecret(stored.Secret);
                if (string.IsNullOrWhiteSpace(secret))
                {
                    throw new LinklyCloudTerminalCredentialUnavailableException();
                }
            }

            return stored with
            {
                Password = password,
                Secret = secret,
                HasUsableSecret = !string.IsNullOrWhiteSpace(secret)
            };
        }
        catch (LinklyCloudTerminalCredentialUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            // Data Protection 的底层异常和密文都不得越过 repository 边界。
            throw new LinklyCloudTerminalCredentialUnavailableException();
        }
    }

    internal static LinklyCloudTerminalRecord MaterializeListTerminal(
        LinklyCloudTerminalRecord stored,
        ILinklyCloudTerminalCredentialProtector protector)
    {
        try
        {
            var runtime = MaterializeRuntimeTerminal(stored, protector);
            return runtime with
            {
                Password = string.Empty,
                Secret = null,
                HasUsableSecret = !string.IsNullOrWhiteSpace(runtime.Secret)
            };
        }
        catch (LinklyCloudTerminalCredentialReentryRequiredException)
        {
            return MarkCredentialRepairRequired(stored);
        }
        catch (LinklyCloudTerminalCredentialUnavailableException)
        {
            return MarkCredentialRepairRequired(stored);
        }
    }

    internal static LinklyCloudTerminalRecord MaterializePairingCompletion(
        LinklyCloudTerminalRecord stored)
    {
        // Pair 完成写入已经提交；这里只返回响应所需状态，禁止再次解密或向 service 传递密文。
        return stored with
        {
            Username = string.Empty,
            Password = string.Empty,
            Secret = null,
            HasUsableSecret = stored.CredentialProtectionVersion ==
                LinklyCloudTerminalCredentialDataProtection.CurrentVersion &&
                !string.IsNullOrWhiteSpace(stored.Secret)
        };
    }

    internal static string? ProtectSecretForStorage(
        string? secret,
        ILinklyCloudTerminalCredentialProtector protector)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        try
        {
            var protectedSecret = protector.ProtectSecret(secret);
            return string.IsNullOrWhiteSpace(protectedSecret)
                ? throw new LinklyCloudTerminalCredentialUnavailableException()
                : protectedSecret;
        }
        catch (LinklyCloudTerminalCredentialUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new LinklyCloudTerminalCredentialUnavailableException();
        }
    }

    private LinklyCloudTerminalRecord MaterializeRuntimeTerminalWithLogging(
        LinklyCloudTerminalRecord stored)
    {
        try
        {
            return MaterializeRuntimeTerminal(stored, credentialProtector);
        }
        catch (Exception ex) when (
            ex is LinklyCloudTerminalCredentialReentryRequiredException or
                LinklyCloudTerminalCredentialUnavailableException)
        {
            logger?.LogWarning(
                "Linkly Cloud terminal credential materialization failed environment={Environment} store={StoreCode} terminalId={TerminalId} error={ErrorType}",
                stored.Environment,
                stored.StoreCode,
                stored.TerminalId,
                ex.GetType().Name);
            throw;
        }
    }

    private static LinklyCloudTerminalRecord MarkCredentialRepairRequired(
        LinklyCloudTerminalRecord stored) => stored with
        {
            Password = string.Empty,
            Secret = null,
            PairingState = "NeedsRepair",
            HasUsableSecret = false
        };

    public async Task<LinklyCloudDeviceSelectionRecord?> GetSelectionAsync(
        string environment,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1 [Environment], [StoreCode], [DeviceCode], [TerminalId], [Revision], [UpdatedAt], [UpdatedBy]
            FROM [dbo].[POSM_LinklyCloudDeviceSelection]
            WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode AND [DeviceCode] = @DeviceCode;
            """;
        return await dbContext.PosmDb.Ado.SqlQuerySingleAsync<LinklyCloudDeviceSelectionRecord>(
            sql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@DeviceCode", deviceCode));
    }

    internal static bool IsTerminalAssignmentViolation(Exception exception)
    {
        var details = exception.ToString();
        return details.Contains("51004", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("UX_POSM_LinklyCloudDeviceSelection_Scope_Terminal", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("already assigned to another POS", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<LinklyCloudDeviceSelectionRecord> UpsertSelectionAsync(
        string environment,
        string storeCode,
        string deviceCode,
        Guid terminalId,
        long? expectedRevision,
        DateTime updatedAt,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.PosmDb.Ado.ExecuteCommandAsync(
                UpsertSelectionSql,
                new SugarParameter("@Environment", environment),
                new SugarParameter("@StoreCode", storeCode),
                new SugarParameter("@DeviceCode", deviceCode),
                new SugarParameter("@TerminalId", terminalId),
                new SugarParameter("@ExpectedRevision", expectedRevision),
                new SugarParameter("@UpdatedAt", updatedAt),
                new SugarParameter("@UpdatedBy", updatedBy));
        }
        catch (Exception ex) when (IsTerminalAssignmentViolation(ex))
        {
            throw new LinklyCloudTerminalAssignedException();
        }
        catch (Exception ex) when (ex.ToString().Contains("51001", StringComparison.OrdinalIgnoreCase) ||
            ex.ToString().Contains("51002", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("must be recovered or acknowledged", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("selection revision conflict", StringComparison.OrdinalIgnoreCase))
        {
            throw new LinklyCloudTerminalSelectionConflictException();
        }
        catch (Exception ex) when (ex.ToString().Contains("51003", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("not paired and ready", StringComparison.OrdinalIgnoreCase))
        {
            throw new LinklyCloudTerminalNotReadyException();
        }

        return await GetSelectionAsync(environment, storeCode, deviceCode, cancellationToken)
            ?? throw new LinklyCloudTerminalSelectionConflictException();
    }

    public async Task<string> GetConfigurationModeAsync(
        string environment,
        string storeCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1 [Mode]
            FROM [dbo].[POSM_LinklyCloudConfigurationMode]
            WHERE [Environment] = @Environment AND [StoreCode] = @StoreCode;
            """;
        var result = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<LinklyCloudConfigurationModeRow>(
            sql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode));
        return string.IsNullOrWhiteSpace(result?.Mode) ? "Legacy" : result.Mode.Trim();
    }

    public async Task<LinklyCloudTerminalRecord?> TryBeginPairingAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid pairingAttemptId,
        DateTime pairingLeaseExpiresAt,
        DateTime expectedUpdatedAt,
        DateTime updatedAt,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<LinklyCloudTerminalRecord>(
            TryBeginPairingSql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@PairingAttemptId", pairingAttemptId),
            new SugarParameter("@PairingLeaseExpiresAt", pairingLeaseExpiresAt),
            new SugarParameter("@ExpectedUpdatedAt", expectedUpdatedAt),
            new SugarParameter("@UpdatedAt", updatedAt),
            new SugarParameter("@UpdatedBy", updatedBy));
        return stored is null ? null : MaterializeRuntimeTerminalWithLogging(stored);
    }

    public async Task<LinklyCloudTerminalRecord> UpdatePairingAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid expectedPairingAttemptId,
        DateTime expectedUpdatedAt,
        string pairingState,
        string? secret,
        string? posId,
        DateTime updatedAt,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @UpdatedTerminal TABLE (
                [TerminalId] UNIQUEIDENTIFIER,
                [Environment] NVARCHAR(32),
                [StoreCode] NVARCHAR(32),
                [LaneNo] INT,
                [DisplayName] NVARCHAR(128),
                [Username] NVARCHAR(128),
                [Password] NVARCHAR(2048),
                [Secret] NVARCHAR(2048),
                [CredentialProtectionVersion] TINYINT,
                [PosId] NVARCHAR(64),
                [PairingState] NVARCHAR(32),
                [PairingAttemptId] UNIQUEIDENTIFIER,
                [PairingLeaseExpiresAt] DATETIME2(7),
                [LastHealthStatus] NVARCHAR(32),
                [LastHealthAt] DATETIME2(7),
                [CreatedAt] DATETIME2(7),
                [UpdatedAt] DATETIME2(7),
                [CreatedBy] NVARCHAR(128),
                [UpdatedBy] NVARCHAR(128));

            UPDATE [dbo].[POSM_LinklyCloudTerminal]
            SET [PairingState] = @PairingState,
                [Secret] = @Secret,
                [CredentialProtectionVersion] = @CredentialProtectionVersion,
                [PosId] = @PosId,
                [PairingAttemptId] = NULL,
                [PairingLeaseExpiresAt] = NULL,
                [UpdatedAt] = @UpdatedAt,
                [UpdatedBy] = @UpdatedBy
            OUTPUT inserted.[TerminalId], inserted.[Environment], inserted.[StoreCode], inserted.[LaneNo],
                   inserted.[DisplayName], inserted.[Username], inserted.[Password], inserted.[Secret],
                   inserted.[CredentialProtectionVersion], inserted.[PosId], inserted.[PairingState], inserted.[PairingAttemptId],
                   inserted.[PairingLeaseExpiresAt], inserted.[LastHealthStatus], inserted.[LastHealthAt],
                   inserted.[CreatedAt], inserted.[UpdatedAt], inserted.[CreatedBy], inserted.[UpdatedBy]
            INTO @UpdatedTerminal
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId
              AND [PairingAttemptId] = @ExpectedPairingAttemptId
              AND [UpdatedAt] = @ExpectedUpdatedAt;

            SELECT * FROM @UpdatedTerminal;
            """;
        var protectedSecret = ProtectSecretForStorage(secret, credentialProtector);
        var updated = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<LinklyCloudTerminalRecord>(
            sql,
            new SugarParameter("@PairingState", pairingState),
            new SugarParameter("@Secret", protectedSecret),
            new SugarParameter(
                "@CredentialProtectionVersion",
                LinklyCloudTerminalCredentialDataProtection.CurrentVersion),
            new SugarParameter("@PosId", posId),
            new SugarParameter("@UpdatedAt", updatedAt),
            new SugarParameter("@UpdatedBy", updatedBy),
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@ExpectedPairingAttemptId", expectedPairingAttemptId),
            new SugarParameter("@ExpectedUpdatedAt", expectedUpdatedAt));
        if (updated is null)
        {
            throw new LinklyCloudTerminalPairingConflictException();
        }

        return MaterializePairingCompletion(updated);
    }

    public async Task<bool> TryAcquireOperationLeaseAsync(
        string environment,
        string storeCode,
        string deviceCode,
        Guid terminalId,
        long expectedSelectionRevision,
        DateTime expectedTerminalUpdatedAt,
        Guid operationLeaseId,
        DateTime operationLeaseExpiresAt,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<LinklyCloudOperationLeaseResult>(
            TryAcquireOperationLeaseSql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@DeviceCode", deviceCode),
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@ExpectedSelectionRevision", expectedSelectionRevision),
            new SugarParameter("@ExpectedTerminalUpdatedAt", expectedTerminalUpdatedAt),
            new SugarParameter("@OperationLeaseId", operationLeaseId),
            new SugarParameter("@OperationLeaseExpiresAt", operationLeaseExpiresAt),
            new SugarParameter("@Now", now));
        return result?.Acquired == true;
    }

    public async Task ReleasePairingLeaseAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid expectedPairingAttemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE [dbo].[POSM_LinklyCloudTerminal]
            SET [PairingAttemptId] = NULL,
                [PairingLeaseExpiresAt] = NULL
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId
              AND [PairingAttemptId] = @ExpectedPairingAttemptId;
            """;
        await dbContext.PosmDb.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@ExpectedPairingAttemptId", expectedPairingAttemptId));
    }

    public async Task ReleaseOperationLeaseAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        Guid expectedOperationLeaseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE [dbo].[POSM_LinklyCloudTerminal]
            SET [PairingAttemptId] = NULL,
                [PairingLeaseExpiresAt] = NULL
            WHERE [Environment] = @Environment
              AND [StoreCode] = @StoreCode
              AND [TerminalId] = @TerminalId
              AND [PairingAttemptId] = @ExpectedOperationLeaseId;
            """;
        await dbContext.PosmDb.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@ExpectedOperationLeaseId", expectedOperationLeaseId));
    }

    public async Task<bool> TryRecordHealthAsync(
        string environment,
        string storeCode,
        Guid terminalId,
        DateTime expectedTerminalUpdatedAt,
        string healthStatus,
        DateTime checkedAt,
        CancellationToken cancellationToken)
    {
        var affected = await dbContext.PosmDb.Ado.ExecuteCommandAsync(
            TryRecordHealthSql,
            new SugarParameter("@Environment", environment),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@ExpectedTerminalUpdatedAt", expectedTerminalUpdatedAt),
            new SugarParameter("@HealthStatus", healthStatus),
            new SugarParameter("@CheckedAt", checkedAt));
        return affected == 1;
    }

    private sealed class LinklyCloudConfigurationModeRow
    {
        public string? Mode { get; set; }
    }

    private sealed class LinklyCloudOperationLeaseResult
    {
        public bool Acquired { get; set; }
    }

    private sealed class LinklyCloudTerminalDisplayNameRow
    {
        public string? DisplayName { get; set; }
    }

}
