using System.Text.Json;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Api.Services;

public interface ILinklySettlementSyncService
{
    Task<LinklySettlementSyncResponse> SyncAsync(
        LinklySettlementSyncRequest request,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken);
}

internal sealed class LinklySettlementSyncService(
    ILinklySettlementRepository repository,
    TimeProvider? timeProvider = null) : ILinklySettlementSyncService
{
    private const int MaximumReceiptCount = 16;
    private const int MaximumReceiptLength = 64 * 1024;
    private const int MaximumReceiptTotalLength = 512 * 1024;
    private const int MaximumSettlementDataLength = 256 * 1024;
    private const string CloudBackendMode = "CloudBackendAsync";
    private static readonly string[] AllowedModes = ["LocalIp", "CloudDirectSync", CloudBackendMode];
    private static readonly string[] AllowedEnvironments = ["Production", "Sandbox"];
    private static readonly string[] AllowedStatuses = ["Pending", "Unknown", "Succeeded", "Failed"];
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<LinklySettlementSyncResponse> SyncAsync(
        LinklySettlementSyncRequest request,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = ValidateAndNormalize(request, storeCode, deviceCode, clock.GetUtcNow());
        normalized = await AttachCloudBackendSessionAsync(normalized, cancellationToken);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await repository.GetAsync(
                normalized.StoreCode,
                normalized.DeviceCode,
                normalized.SettlementGuid,
                cancellationToken);
            var providerExisting = string.IsNullOrWhiteSpace(normalized.ProviderSessionId)
                ? null
                : await repository.GetByProviderSessionAsync(
                    normalized.ConnectionMode,
                    normalized.Environment,
                    normalized.StoreCode,
                    normalized.DeviceCode,
                    normalized.ProviderSessionId,
                    cancellationToken);
            if (providerExisting is not null && providerExisting.SettlementGuid != normalized.SettlementGuid)
            {
                throw Conflict(
                    "PROVIDER_SESSION_CONFLICT",
                    "The Linkly provider session is already linked to another settlement record.");
            }

            if (existing is null)
            {
                if (await repository.TryInsertAsync(normalized, cancellationToken))
                {
                    return new LinklySettlementSyncResponse(true, false, normalized.ClientRevision);
                }

                continue;
            }

            if (normalized.ClientRevision < existing.ClientRevision)
            {
                return new LinklySettlementSyncResponse(true, true, existing.ClientRevision);
            }

            if (normalized.ClientRevision == existing.ClientRevision)
            {
                if (!Equivalent(existing, normalized))
                {
                    throw Conflict(
                        "REVISION_CONTENT_CONFLICT",
                        "The same Linkly settlement revision was uploaded with different content.");
                }

                return new LinklySettlementSyncResponse(true, true, existing.ClientRevision);
            }

            ValidateHigherRevision(existing, normalized);
            normalized.Id = existing.Id;
            normalized.ReceivedAtUtc = existing.ReceivedAtUtc;
            if (await repository.TryUpdateAsync(normalized, existing.ClientRevision, cancellationToken))
            {
                return new LinklySettlementSyncResponse(true, false, normalized.ClientRevision);
            }
        }

        throw Conflict(
            "SETTLEMENT_SYNC_CONCURRENT_UPDATE",
            "The Linkly settlement changed concurrently. Retry the latest snapshot.");
    }

    private PosmLinklySettlementRecord ValidateAndNormalize(
        LinklySettlementSyncRequest request,
        string storeCode,
        string deviceCode,
        DateTimeOffset now)
    {
        if (request.SchemaVersion != 1)
        {
            throw Invalid("UNSUPPORTED_SCHEMA_VERSION", "schemaVersion must be 1.");
        }

        if (request.SettlementGuid == Guid.Empty)
        {
            throw Invalid("SETTLEMENT_GUID_REQUIRED", "settlementGuid is required.");
        }

        var normalizedStoreCode = Required(storeCode, 32, "STORE_CODE_REQUIRED", "Authenticated store code is required.");
        var normalizedDeviceCode = Required(deviceCode, 64, "DEVICE_CODE_REQUIRED", "Authenticated device code is required.");
        var connectionMode = Canonical(request.ConnectionMode, AllowedModes, "INVALID_CONNECTION_MODE");
        var environment = Canonical(request.Environment, AllowedEnvironments, "INVALID_ENVIRONMENT");
        var status = Canonical(request.Status, AllowedStatuses, "INVALID_SETTLEMENT_STATUS");
        var providerSessionId = Optional(request.ProviderSessionId, 64, "PROVIDER_SESSION_TOO_LONG");
        var providerSubmissionState = ResolveProviderSubmissionState(
            request.ProviderSubmissionState,
            status,
            providerSessionId);
        if (request.BusinessDate == default)
        {
            throw Invalid("BUSINESS_DATE_REQUIRED", "businessDate is required.");
        }

        if (request.RequestedAt == default)
        {
            throw Invalid("REQUESTED_AT_REQUIRED", "requestedAt is required.");
        }

        if (request.ClientRevision <= 0)
        {
            throw Invalid("INVALID_CLIENT_REVISION", "clientRevision must be greater than zero.");
        }

        if (request.PrintCount < 0)
        {
            throw Invalid("INVALID_PRINT_COUNT", "printCount cannot be negative.");
        }

        if (request.CompletedAt is { } completedAt && completedAt < request.RequestedAt)
        {
            throw Invalid("INVALID_COMPLETED_AT", "completedAt cannot be earlier than requestedAt.");
        }

        if (request.FirstPrintedAt is { } firstPrintedAt &&
            request.LastPrintedAt is { } lastPrintedAt &&
            lastPrintedAt < firstPrintedAt)
        {
            throw Invalid("INVALID_PRINT_TIMESTAMPS", "lastPrintedAt cannot be earlier than firstPrintedAt.");
        }

        if (request.PrintCount == 0 &&
            (request.FirstPrintedAt is not null || request.LastPrintedAt is not null))
        {
            throw Invalid("INVALID_PRINT_AUDIT", "print timestamps require a positive printCount.");
        }

        if (request.PrintCount > 0 &&
            (request.FirstPrintedAt is null || request.LastPrintedAt is null))
        {
            throw Invalid("INVALID_PRINT_AUDIT", "a positive printCount requires firstPrintedAt and lastPrintedAt.");
        }

        if (status is "Succeeded" or "Failed" && request.CompletedAt is null)
        {
            throw Invalid("COMPLETED_AT_REQUIRED", "Final settlement status requires completedAt.");
        }

        ValidateCloudBackendSubmissionState(
            connectionMode,
            status,
            providerSubmissionState,
            providerSessionId);

        var receiptTexts = LinklyReceiptTextSanitizer.SanitizeReceipts(request.ReceiptTexts);
        if (receiptTexts.Count > MaximumReceiptCount ||
            receiptTexts.Any(static receipt => receipt.Length > MaximumReceiptLength) ||
            receiptTexts.Sum(static receipt => receipt.Length) > MaximumReceiptTotalLength)
        {
            throw Invalid("RECEIPT_PAYLOAD_TOO_LARGE", "Linkly settlement receipt payload is too large.");
        }

        return new PosmLinklySettlementRecord
        {
            SettlementGuid = request.SettlementGuid,
            StoreCode = normalizedStoreCode,
            DeviceCode = normalizedDeviceCode,
            BusinessDate = request.BusinessDate.ToDateTime(TimeOnly.MinValue),
            ConnectionMode = connectionMode,
            Environment = environment,
            ProviderSessionId = providerSessionId,
            ProviderSubmissionState = providerSubmissionState.ToString(),
            Status = status,
            ResponseCode = Optional(request.ResponseCode, 32, "RESPONSE_CODE_TOO_LONG"),
            ResponseText = Optional(LinklyReceiptTextSanitizer.Sanitize(request.ResponseText), 512, "RESPONSE_TEXT_TOO_LONG"),
            SettlementData = Optional(LinklyReceiptTextSanitizer.SanitizeSettlementData(request.SettlementData), MaximumSettlementDataLength, "SETTLEMENT_DATA_TOO_LARGE"),
            ReceiptTextsJson = JsonSerializer.Serialize(receiptTexts),
            RequestedAtUtc = request.RequestedAt.ToUniversalTime(),
            CompletedAtUtc = request.CompletedAt?.ToUniversalTime(),
            FirstPrintedAtUtc = request.FirstPrintedAt?.ToUniversalTime(),
            LastPrintedAtUtc = request.LastPrintedAt?.ToUniversalTime(),
            PrintCount = request.PrintCount,
            LastPrintError = Optional(LinklyReceiptTextSanitizer.Sanitize(request.LastPrintError), 512, "PRINT_ERROR_TOO_LONG"),
            ClientRevision = request.ClientRevision,
            ReceivedAtUtc = now.ToUniversalTime(),
            UpdatedAtUtc = now.ToUniversalTime()
        };
    }

    private async Task<PosmLinklySettlementRecord> AttachCloudBackendSessionAsync(
        PosmLinklySettlementRecord settlement,
        CancellationToken cancellationToken)
    {
        if (settlement.ConnectionMode != CloudBackendMode ||
            string.IsNullOrWhiteSpace(settlement.ProviderSessionId))
        {
            return settlement;
        }

        var fact = await repository.GetCloudBackendSettlementAsync(
            settlement.Environment,
            settlement.StoreCode,
            settlement.DeviceCode,
            settlement.ProviderSessionId,
            cancellationToken);
        if (fact is null)
        {
            throw Conflict(
                "CLOUD_BACKEND_SESSION_NOT_FOUND",
                "The CloudBackendAsync settlement session was not found in the authenticated device scope.");
        }

        settlement.CloudBackendSessionId = fact.Id;
        var finalStatus = ResolveCloudBackendFinalStatus(fact);
        if (settlement.Status is "Succeeded" or "Failed")
        {
            if (finalStatus is null)
            {
                throw Conflict(
                    "CLOUD_BACKEND_SESSION_NOT_FINAL",
                    "The CloudBackendAsync session does not yet contain a final settlement result.");
            }

            if (!Same(finalStatus, settlement.Status))
            {
                throw Conflict(
                    "CLOUD_BACKEND_RESULT_CONFLICT",
                    "The uploaded settlement result does not match the linked CloudBackendAsync session.");
            }
        }

        // CloudBackend 原始事实保留在既有 session 表；这里只保存客户端快照和稳定关联，避免晚到回调破坏 revision 幂等。
        return settlement;
    }

    private static string? ResolveCloudBackendFinalStatus(LinklyCloudBackendSettlementFact fact)
    {
        if (LinklyCloudBackendStatusConstants.IsSettlementFailureStatus(fact.Status))
        {
            return "Failed";
        }

        if (fact.OperationSuccess == false)
        {
            return "Failed";
        }

        if (fact.OperationSuccess is not null && HasSettlementReceipt(fact.SettlementReceiptTexts))
        {
            return LinklyCloudBackendStatusConstants.IsSuccessfulSettlement(
                fact.OperationSuccess,
                fact.ResponseCode)
                ? "Succeeded"
                : "Failed";
        }

        return null;
    }

    private static bool HasSettlementReceipt(string? receiptTextsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(receiptTextsJson ?? "[]")?
                .Any(static receipt => !string.IsNullOrWhiteSpace(receipt)) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ProviderSubmissionState ResolveProviderSubmissionState(
        ProviderSubmissionState? requestedState,
        string status,
        string? providerSessionId)
    {
        if (status is "Pending" or "Unknown")
        {
            if (requestedState is { } state && state != ProviderSubmissionState.Unknown)
            {
                throw Invalid(
                    "INVALID_PROVIDER_SUBMISSION_STATE",
                    "Pending or unknown settlements must use providerSubmissionState Unknown.");
            }

            return ProviderSubmissionState.Unknown;
        }

        if (requestedState is { } explicitState)
        {
            return explicitState;
        }

        // 兼容 SchemaVersion=1 的旧客户端：只能根据既有会话关联做保守推断。
        return status == "Succeeded" || providerSessionId is not null
            ? ProviderSubmissionState.Submitted
            : status == "Failed"
                ? ProviderSubmissionState.NotSubmitted
                : ProviderSubmissionState.Unknown;
    }

    private static void ValidateCloudBackendSubmissionState(
        string connectionMode,
        string status,
        ProviderSubmissionState providerSubmissionState,
        string? providerSessionId)
    {
        if (status is "Pending" or "Unknown")
        {
            if (providerSubmissionState != ProviderSubmissionState.Unknown)
            {
                throw Invalid(
                    "INVALID_PROVIDER_SUBMISSION_STATE",
                    "Pending or unknown CloudBackendAsync settlements must have an unknown provider submission state.");
            }

            return;
        }

        if (status == "Succeeded")
        {
            if (providerSubmissionState != ProviderSubmissionState.Submitted)
            {
                throw Invalid(
                    "INVALID_PROVIDER_SUBMISSION_STATE",
                    "A successful settlement must have been submitted to the provider.");
            }

            if (Same(connectionMode, CloudBackendMode) && providerSessionId is null)
            {
                throw Invalid(
                    "PROVIDER_SESSION_REQUIRED",
                    "A submitted CloudBackendAsync settlement requires providerSessionId.");
            }

            return;
        }

        if (providerSubmissionState == ProviderSubmissionState.Unknown)
        {
            throw Invalid(
                "INVALID_PROVIDER_SUBMISSION_STATE",
                "A failed settlement must be Submitted or NotSubmitted.");
        }

        if (!Same(connectionMode, CloudBackendMode))
        {
            return;
        }

        if (providerSubmissionState == ProviderSubmissionState.Submitted)
        {
            if (providerSessionId is null)
            {
                throw Invalid(
                    "PROVIDER_SESSION_REQUIRED",
                    "A submitted CloudBackendAsync settlement requires providerSessionId.");
            }

            return;
        }

        if (providerSubmissionState == ProviderSubmissionState.NotSubmitted && providerSessionId is null)
        {
            return;
        }

        throw Invalid(
            "INVALID_PROVIDER_SUBMISSION_STATE",
            "A failed CloudBackendAsync settlement must be Submitted with providerSessionId or NotSubmitted without providerSessionId.");
    }

    private static ProviderSubmissionState GetStoredProviderSubmissionState(PosmLinklySettlementRecord settlement)
    {
        if (Enum.TryParse<ProviderSubmissionState>(settlement.ProviderSubmissionState, ignoreCase: false, out var state) &&
            Enum.IsDefined(state))
        {
            return state;
        }

        return ResolveProviderSubmissionState(
            requestedState: null,
            status: settlement.Status,
            providerSessionId: settlement.ProviderSessionId);
    }

    private static void ValidateHigherRevision(
        PosmLinklySettlementRecord existing,
        PosmLinklySettlementRecord incoming)
    {
        if (existing.BusinessDate.Date != incoming.BusinessDate.Date ||
            !Same(existing.ConnectionMode, incoming.ConnectionMode) ||
            !Same(existing.Environment, incoming.Environment) ||
            existing.RequestedAtUtc.UtcDateTime != incoming.RequestedAtUtc.UtcDateTime)
        {
            throw Conflict("IMMUTABLE_FIELDS_CONFLICT", "Immutable Linkly settlement fields cannot change.");
        }

        if (existing.ProviderSessionId is not null &&
            !Same(existing.ProviderSessionId, incoming.ProviderSessionId))
        {
            throw Conflict("PROVIDER_SESSION_CONFLICT", "providerSessionId cannot change once assigned.");
        }

        if (existing.CloudBackendSessionId is not null &&
            existing.CloudBackendSessionId != incoming.CloudBackendSessionId)
        {
            throw Conflict("CLOUD_BACKEND_SESSION_CONFLICT", "CloudBackendAsync session linkage cannot change.");
        }

        if (!IsAllowedStatusProgression(existing.Status, incoming.Status))
        {
            throw Conflict("STATUS_REGRESSION", "Linkly settlement status cannot regress or change between final states.");
        }

        var statusAdvanced = !Same(existing.Status, incoming.Status);
        if (!statusAdvanced &&
            (GetStoredProviderSubmissionState(existing) != GetStoredProviderSubmissionState(incoming) ||
             !Same(existing.ResponseCode, incoming.ResponseCode) ||
             !Same(existing.ResponseText, incoming.ResponseText) ||
             !Same(existing.SettlementData, incoming.SettlementData) ||
             !Same(existing.ReceiptTextsJson, incoming.ReceiptTextsJson) ||
             !SameInstant(existing.CompletedAtUtc, incoming.CompletedAtUtc)))
        {
            throw Conflict(
                "BANK_EVIDENCE_CONFLICT",
                "A higher revision cannot rewrite bank evidence without a valid status progression.");
        }

        ValidatePrintProgression(existing, incoming);
    }

    private static void ValidatePrintProgression(
        PosmLinklySettlementRecord existing,
        PosmLinklySettlementRecord incoming)
    {
        if (incoming.PrintCount < existing.PrintCount ||
            existing.FirstPrintedAtUtc is not null &&
            !SameInstant(existing.FirstPrintedAtUtc, incoming.FirstPrintedAtUtc) ||
            existing.LastPrintedAtUtc is not null &&
            (incoming.LastPrintedAtUtc is null || incoming.LastPrintedAtUtc < existing.LastPrintedAtUtc))
        {
            throw Conflict("PRINT_AUDIT_REGRESSION", "Linkly settlement print audit cannot regress.");
        }

        if (incoming.PrintCount == existing.PrintCount &&
            (!SameInstant(existing.FirstPrintedAtUtc, incoming.FirstPrintedAtUtc) ||
             !SameInstant(existing.LastPrintedAtUtc, incoming.LastPrintedAtUtc)))
        {
            throw Conflict(
                "PRINT_AUDIT_CONFLICT",
                "Print timestamps cannot change without advancing printCount.");
        }
    }

    private static bool IsAllowedStatusProgression(string existing, string incoming)
    {
        if (Same(existing, incoming))
        {
            return true;
        }

        return existing switch
        {
            "Pending" => incoming is "Unknown" or "Succeeded" or "Failed",
            "Unknown" => incoming is "Succeeded" or "Failed",
            _ => false
        };
    }

    private static bool Equivalent(PosmLinklySettlementRecord left, PosmLinklySettlementRecord right)
    {
        return left.SettlementGuid == right.SettlementGuid &&
            Same(left.StoreCode, right.StoreCode) &&
            Same(left.DeviceCode, right.DeviceCode) &&
            left.BusinessDate.Date == right.BusinessDate.Date &&
            Same(left.ConnectionMode, right.ConnectionMode) &&
            Same(left.Environment, right.Environment) &&
            Same(left.ProviderSessionId, right.ProviderSessionId) &&
            GetStoredProviderSubmissionState(left) == GetStoredProviderSubmissionState(right) &&
            left.CloudBackendSessionId == right.CloudBackendSessionId &&
            Same(left.Status, right.Status) &&
            Same(left.ResponseCode, right.ResponseCode) &&
            Same(left.ResponseText, right.ResponseText) &&
            Same(left.SettlementData, right.SettlementData) &&
            Same(left.ReceiptTextsJson, right.ReceiptTextsJson) &&
            left.RequestedAtUtc.UtcDateTime == right.RequestedAtUtc.UtcDateTime &&
            SameInstant(left.CompletedAtUtc, right.CompletedAtUtc) &&
            SameInstant(left.FirstPrintedAtUtc, right.FirstPrintedAtUtc) &&
            SameInstant(left.LastPrintedAtUtc, right.LastPrintedAtUtc) &&
            left.PrintCount == right.PrintCount &&
            Same(left.LastPrintError, right.LastPrintError) &&
            left.ClientRevision == right.ClientRevision;
    }

    private static bool SameInstant(DateTimeOffset? left, DateTimeOffset? right)
    {
        return left?.UtcDateTime == right?.UtcDateTime;
    }

    private static bool Same(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static string Canonical(string? value, IEnumerable<string> allowed, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(errorCode, $"{errorCode}.");
        }

        var canonical = allowed.FirstOrDefault(candidate =>
            string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return canonical ?? throw Invalid(errorCode, $"{errorCode}.");
    }

    private static string Required(string? value, int maxLength, string errorCode, string message)
    {
        return Optional(value, maxLength, errorCode) ?? throw Invalid(errorCode, message);
    }

    private static string? Optional(string? value, int maxLength, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw Invalid(errorCode, $"Value exceeds {maxLength} characters.");
    }

    private static LinklySettlementValidationException Invalid(string code, string message)
    {
        return new LinklySettlementValidationException(code, message);
    }

    private static LinklySettlementConflictException Conflict(string code, string message)
    {
        return new LinklySettlementConflictException(code, message);
    }
}

public sealed class LinklySettlementValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class LinklySettlementConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
