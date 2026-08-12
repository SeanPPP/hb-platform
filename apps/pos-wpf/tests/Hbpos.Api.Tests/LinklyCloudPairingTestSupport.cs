using System.Text.Json;
using Hbpos.Api.Services;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Api.Tests;

internal sealed class NoOpLinklyCloudPairingService : ILinklyCloudPairingService
{
    public Task<LinklyCloudBackendTerminalCredentialResponse> PairAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudBackendPairRequest request,
        string? updatedBy,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Pairing is not part of this test.");
}

internal sealed class CapturingLinklyCloudPairingService(
    Exception? exception = null,
    LinklyCloudBackendTerminalCredentialResponse? response = null) : ILinklyCloudPairingService
{
    public int Calls { get; private set; }

    public string? StoreCode { get; private set; }

    public string? DeviceCode { get; private set; }

    public LinklyCloudBackendPairRequest? Request { get; private set; }

    public string? UpdatedBy { get; private set; }

    public Task<LinklyCloudBackendTerminalCredentialResponse> PairAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudBackendPairRequest request,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        Calls++;
        StoreCode = storeCode;
        DeviceCode = deviceCode;
        Request = request;
        UpdatedBy = updatedBy;
        if (exception is not null)
        {
            throw exception;
        }

        return Task.FromResult(response ?? new LinklyCloudBackendTerminalCredentialResponse(
            "Sandbox",
            storeCode,
            deviceCode,
            true,
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            DateTimeOffset.UtcNow));
    }
}

internal sealed class NoOpLinklyCloudCredentialService : ILinklyCloudCredentialService
{
    public Task<LinklyCloudCredentialResponse?> GetByStoreCodeAsync(
        string storeCode,
        string environment,
        CancellationToken cancellationToken) =>
        Task.FromResult<LinklyCloudCredentialResponse?>(null);

    public Task<LinklyCloudCredentialUpsertResponse> UpsertAsync(
        string storeCode,
        LinklyCloudCredentialUpsertRequest request,
        string? updatedBy,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class NoOpLinklyCloudBackendAsyncService : ILinklyCloudBackendAsyncService
{
    public Task<LinklyCloudBackendSessionResponse> StartTransactionAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudBackendTransactionRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendSessionResponse?> GetStatusAsync(
        string storeCode,
        string deviceCode,
        string environment,
        string sessionId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendSessionResponse?> GetActiveSessionAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendSessionResponse?> GetResumableSessionAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendHealthResponse> GetHealthAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendLogonTestResponse> RunLogonTestAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendStatusTestResponse> RunStatusTestAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendTerminalCredentialResponse> UpsertTerminalCredentialAsync(
        string storeCode,
        string deviceCode,
        LinklyCloudBackendTerminalCredentialUpsertRequest request,
        string? updatedBy,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendSessionResponse> RecoverAsync(
        string storeCode,
        string deviceCode,
        string sessionId,
        LinklyCloudBackendRecoverRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendSessionResponse> SendKeyAsync(
        string storeCode,
        string deviceCode,
        string sessionId,
        LinklyCloudBackendSendKeyRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendSessionResponse> MarkReceiptPrintedAsync(
        string storeCode,
        string deviceCode,
        string sessionId,
        LinklyCloudBackendMarkReceiptPrintedRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LinklyCloudBackendSessionResponse> AcknowledgeSessionAsync(
        string storeCode,
        string deviceCode,
        string environment,
        string sessionId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ReceiveNotificationAsync(
        string environment,
        string sessionId,
        string type,
        string? authorizationHeader,
        JsonElement payload,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class NoOpLinklySchemaInitializer :
    IStoreSchemaInitializer,
    IAttendanceQrKeySchemaInitializer,
    IDeviceRuntimeStatusSchemaInitializer,
    ILinklyCloudCredentialSchemaInitializer,
    ILinklyCloudBackendAsyncSchemaInitializer,
    ISquareTokenSchemaInitializer,
    IAdvertisementSchemaInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
