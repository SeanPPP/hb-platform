using System.Security.Claims;
using System.Text.Json;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Linkly;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class LinklySanitizedCardTransactionTests
{
    [Fact]
    public void Sanitized_contract_contains_only_the_approved_evidence_fields()
    {
        Assert.Equal(
        [
            "TxnRef",
            "Rfn",
            "AuthCode",
            "CardType",
            "MaskedCardNumber",
            "MerchantId",
            "ResponseCode",
            "ResponseText",
            "Stan",
            "BankDateTime",
            "AmountCents"
        ],
            typeof(LinklyCloudBackendCardTransactionDto)
                .GetProperties()
                .Select(property => property.Name));
    }

    [Fact]
    public void Sanitize_extracts_only_whitelisted_fields_and_masks_plain_pan()
    {
        var response = CreateResponse(
            """
            {
              "Response": {
                "Success": true,
                "TxnRef": "TXN-123",
                "ResponseCode": "00",
                "ResponseText": "APPROVED",
                "AmtPurchase": -1008,
                "AuthCode": 123456,
                "CardType": "VISA",
                "Pan": "4111111111111234",
                "Caid": "MID-42",
                "Stan": 42,
                "BankDateTime": "2026-06-05T14:30:00+10:00",
                "PurchaseAnalysisData": { "RFN": "RFN-1" },
                "AccessToken": "must-not-leak",
                "Track2": "4111111111111234=secret",
                "PinBlock": "pin-secret"
              }
            }
            """);

        var sanitized = LinklyCardTransactionSanitizer.Sanitize(response);

        Assert.NotNull(sanitized);
        Assert.Equal("TXN-123", sanitized!.TxnRef);
        Assert.Equal("RFN-1", sanitized.Rfn);
        Assert.Equal("123456", sanitized.AuthCode);
        Assert.Equal("VISA", sanitized.CardType);
        Assert.Equal("411111******1234", sanitized.MaskedCardNumber);
        Assert.Equal("MID-42", sanitized.MerchantId);
        Assert.Equal("00", sanitized.ResponseCode);
        Assert.Equal("APPROVED", sanitized.ResponseText);
        Assert.Equal("42", sanitized.Stan);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 5, 14, 30, 0, TimeSpan.FromHours(10)),
            sanitized.BankDateTime);
        Assert.Equal(1008, sanitized.AmountCents);

        var json = JsonSerializer.Serialize(sanitized);
        Assert.DoesNotContain("must-not-leak", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pin-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Track2", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReceiptText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CardBin", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4111111111111234", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_rejects_unsafe_text_pan_and_bank_time_without_offset()
    {
        var response = CreateResponse(
            $$"""
            {
              "Response": {
                "TxnRef": "TXN-123",
                "ResponseCode": "00",
                "ResponseText": "APPROVED",
                "AmtPurchase": 1008,
                "AuthCode": "12\n34",
                "CardType": "{{new string('V', 80)}}",
                "Pan": "4111111111",
                "Caid": "MID\u0000-42",
                "Stan": "4\t2",
                "BankDateTime": "2026-06-05T14:30:00"
              }
            }
            """);

        var sanitized = LinklyCardTransactionSanitizer.Sanitize(response);

        Assert.NotNull(sanitized);
        Assert.Null(sanitized!.AuthCode);
        Assert.Null(sanitized.CardType);
        Assert.Null(sanitized.MaskedCardNumber);
        Assert.Null(sanitized.MerchantId);
        Assert.Null(sanitized.Stan);
        Assert.Null(sanitized.BankDateTime);
        Assert.Equal(1008, sanitized.AmountCents);
    }

    [Fact]
    public void Sanitize_uses_transaction_matching_protected_result_and_ignores_stale_payload()
    {
        var response = CreateResponse(
            """
            {
              "Response": {
                "TxnRef": "TXN-APPROVED",
                "ResponseCode": "00",
                "ResponseText": "APPROVED",
                "Pan": "4111111111111234"
              }
            }
            """,
            """
            {
              "Response": {
                "TxnRef": "TXN-STALE",
                "ResponseCode": "05",
                "ResponseText": "DECLINED",
                "Pan": "5555555555554444",
                "AccessToken": "stale-secret"
              }
            }
            """);

        var sanitized = LinklyCardTransactionSanitizer.Sanitize(response);

        Assert.NotNull(sanitized);
        Assert.Equal("TXN-APPROVED", sanitized!.TxnRef);
        Assert.Equal("411111******1234", sanitized.MaskedCardNumber);
        var json = JsonSerializer.Serialize(sanitized);
        Assert.DoesNotContain("555555", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stale-secret", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{ "Response": { "TxnRef": "TXN-1" } }""")]
    public void Sanitize_returns_null_for_invalid_or_non_transaction_material(string payloadJson)
    {
        var notificationType = payloadJson == "{"
            ? "transaction"
            : "receipt";
        var response = CreateResponse(payloadJson, notificationType: notificationType);

        Assert.Null(LinklyCardTransactionSanitizer.Sanitize(response));
    }

    [Fact]
    public async Task Create_status_and_recover_endpoints_attach_sanitized_card_transaction()
    {
        var backendResponse = CreateResponse(
            """
            {
              "Response": {
                "TxnRef": "TXN-123",
                "ResponseCode": "00",
                "ResponseText": "APPROVED",
                "AmtPurchase": 1008,
                "Pan": "4111111111111234",
                "PurchaseAnalysisData": { "RFN": "RFN-1" }
              }
            }
            """);
        var controller = CreateController(new FixedResponseBackendService(backendResponse));

        var create = await controller.StartCloudBackendTransaction(
            new LinklyCloudBackendTransactionRequest("Sandbox", "P", 1008, null),
            CancellationToken.None);
        var status = await controller.GetCloudBackendTransactionStatus(
            backendResponse.SessionId,
            "Sandbox",
            CancellationToken.None);
        var recover = await controller.RecoverCloudBackendTransaction(
            backendResponse.SessionId,
            new LinklyCloudBackendRecoverRequest("Sandbox"),
            CancellationToken.None);

        AssertEvidence(create);
        AssertEvidence(status);
        AssertEvidence(recover);
    }

    [Fact]
    public void Legacy_constructor_and_null_evidence_remain_serialization_compatible()
    {
        var response = CreateResponse(notificationType: "receipt");

        Assert.Null(response.CardTransaction);
        var json = JsonSerializer.Serialize(response);
        Assert.Contains("\"Environment\":\"Sandbox\"", json, StringComparison.Ordinal);
        Assert.Contains("\"CardTransaction\":null", json, StringComparison.Ordinal);
    }

    private static void AssertEvidence(
        ActionResult<ApiResult<LinklyCloudBackendSessionResponse>> actionResult)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResult<LinklyCloudBackendSessionResponse>>(ok.Value);
        Assert.NotNull(envelope.Data?.CardTransaction);
        Assert.Equal("411111******1234", envelope.Data!.CardTransaction!.MaskedCardNumber);
        Assert.Equal("RFN-1", envelope.Data.CardTransaction.Rfn);
    }

    private static LinklyController CreateController(ILinklyCloudBackendAsyncService service)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(DeviceAuthConstants.StoreCodeClaim, "S01"),
                new Claim(DeviceAuthConstants.DeviceCodeClaim, "POS-01")
            ], "Test"))
        };
        return new LinklyController(null!, service, new NoOpLinklyCloudPairingService())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static LinklyCloudBackendSessionResponse CreateResponse(
        string payloadJson = """{ "ReceiptText": "receipt" }""",
        string? secondPayloadJson = null,
        string notificationType = "transaction")
    {
        var notifications = new List<LinklyCloudBackendNotificationDto>
        {
            new(notificationType, payloadJson, new DateTimeOffset(2026, 6, 5, 4, 0, 0, TimeSpan.Zero))
        };
        if (secondPayloadJson is not null)
        {
            notifications.Add(new LinklyCloudBackendNotificationDto(
                "transaction",
                secondPayloadJson,
                new DateTimeOffset(2026, 6, 5, 4, 1, 0, TimeSpan.Zero)));
        }

        return new LinklyCloudBackendSessionResponse(
            "Sandbox",
            "S01",
            "POS-01",
            "session-123",
            "Completed",
            "TXN-123",
            "00",
            "APPROVED",
            null,
            null,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            [],
            null,
            0,
            null,
            null,
            200,
            notifications,
            true);
    }

    private sealed class FixedResponseBackendService(
        LinklyCloudBackendSessionResponse response) : ILinklyCloudBackendAsyncService
    {
        public Task<LinklyCloudBackendSessionResponse> StartTransactionAsync(
            string storeCode,
            string deviceCode,
            LinklyCloudBackendTransactionRequest request,
            CancellationToken cancellationToken) => Task.FromResult(response);

        public Task<LinklyCloudBackendSessionResponse?> GetStatusAsync(
            string storeCode,
            string deviceCode,
            string environment,
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LinklyCloudBackendSessionResponse?>(response);

        public Task<LinklyCloudBackendSessionResponse> RecoverAsync(
            string storeCode,
            string deviceCode,
            string sessionId,
            LinklyCloudBackendRecoverRequest request,
            CancellationToken cancellationToken) => Task.FromResult(response);

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
}
