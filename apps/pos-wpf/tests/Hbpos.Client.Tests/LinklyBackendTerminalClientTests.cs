using System.Net;
using System.Text;
using System.Text.Json;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Client.Tests;

public sealed class LinklyBackendTerminalClientTests
{
    [Fact]
    public async Task PurchaseAsync_uses_localized_backend_message_for_invalid_amount()
    {
        var localization = new LocalizationService();
        localization.SetCulture("zh-CN");
        var client = CreateClient(
            new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called.")),
            new FakeLinklyTerminalDialogService(),
            localization);

        var result = await client.PurchaseAsync(0m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("刷卡金额必须大于零。", result.Message);
    }

    [Fact]
    public async Task PurchaseAsync_rejects_backend_completed_transaction_when_transaction_success_is_false()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-failed-success-session",
                        "status": "Completed",
                        "txnRef": "260601120199",
                        "transactionSuccess": false,
                        "responseCode": "00",
                        "responseText": "SYSTEM ERROR",
                        "displayText": "SYSTEM ERROR",
                        "receiptText": "DECLINED RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """));
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("00", result.ResponseCode);
        Assert.Equal("SYSTEM ERROR", result.ResponseText);
        Assert.Equal(0, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_treats_approved_response_code_as_success_when_transaction_success_is_missing()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-approved-without-transaction-success",
                        "status": "Completed",
                        "txnRef": "260601120198",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "displayText": "CARDHOLDER DISPLAY SECRET",
                        "receiptText": "APPROVED RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """));
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal("00", result.ResponseCode);
        Assert.Equal("APPROVED", result.ResponseText);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task TestConnectionAsync_uses_backend_logon_test_endpoint_without_local_linkly_token()
    {
        using var logs = new ConsoleLogCapture();
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "transactionReference": "logon-session-1",
                    "requestedAt": "2026-06-05T04:00:00Z",
                    "httpStatus": 200,
                    "succeeded": true,
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "catid": "12345678",
                    "caid": "123456789012345",
                    "pinPadVersion": "1.8.6.0",
                    "message": "ANZ Linkly Cloud logon succeeded."
                  }
                }
                """);
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.TestConnectionAsync(CardTerminalEnvironment.Sandbox);

        Assert.True(result.Succeeded);
        Assert.Contains("logon", result.Message, StringComparison.OrdinalIgnoreCase);
        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://api.example/api/v1/linkly/cloud-backend/logon-test?environment=Sandbox",
            request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Content);

        using var requestLog = FindLinklyLog(logs.Lines, "logon-test", "request");
        Assert.Equal("POST", requestLog.RootElement.GetProperty("request").GetProperty("method").GetString());
        using var responseLog = FindLinklyLog(logs.Lines, "logon-test", "response");
        Assert.Equal("00", responseLog.RootElement.GetProperty("response").GetProperty("body").GetProperty("data").GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task TestTransactionStatusAsync_posts_status_test_endpoint_and_logs_request_response()
    {
        using var logs = new ConsoleLogCapture();
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "transactionReference": "status-session-1",
                    "requestedAt": "2026-06-05T04:00:00Z",
                    "httpStatus": 200,
                    "succeeded": false,
                    "responseCode": "05",
                    "responseText": "DECLINED",
                    "responseTxnRef": "LAST-TXN-1",
                    "responseDate": "050626",
                    "responseTime": "143000",
                    "message": "DECLINED"
                  }
                }
                """);
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.TestTransactionStatusAsync(CardTerminalEnvironment.Sandbox);

        Assert.False(result.Succeeded);
        Assert.Equal("DECLINED", result.Message);
        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://api.example/api/v1/linkly/cloud-backend/status-test?environment=Sandbox",
            request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Content);

        using var requestLog = FindLinklyLog(logs.Lines, "transaction-status-test", "request");
        Assert.Equal("POST", requestLog.RootElement.GetProperty("request").GetProperty("method").GetString());
        Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/status-test?environment=Sandbox", requestLog.RootElement.GetProperty("request").GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, requestLog.RootElement.GetProperty("request").GetProperty("body").ValueKind);

        using var responseLog = FindLinklyLog(logs.Lines, "transaction-status-test", "response");
        Assert.Equal(200, responseLog.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal("LAST-TXN-1", responseLog.RootElement.GetProperty("details").GetProperty("txnRef").GetString());
        Assert.Equal("DECLINED", responseLog.RootElement.GetProperty("response").GetProperty("body").GetProperty("data").GetProperty("responseText").GetString());
    }

    [Fact]
    public async Task TestConnectionAsync_returns_failed_when_backend_logon_is_declined()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {
              "success": true,
              "data": {
                "environment": "Sandbox",
                "storeCode": "S01",
                "deviceCode": "TERM-1",
                "transactionReference": "logon-session-1",
                "requestedAt": "2026-06-05T04:00:00Z",
                "httpStatus": 200,
                "succeeded": false,
                "responseCode": "99",
                "responseText": "LOGON REQUIRED",
                "catid": null,
                "caid": null,
                "pinPadVersion": null,
                "message": "LOGON REQUIRED"
              }
            }
            """));
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.TestConnectionAsync(CardTerminalEnvironment.Sandbox);

        Assert.False(result.Succeeded);
        Assert.Equal("LOGON REQUIRED", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_logs_failed_backend_logon_response()
    {
        using var logs = new ConsoleLogCapture();
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {
              "success": true,
              "data": {
                "environment": "Sandbox",
                "storeCode": "S01",
                "deviceCode": "TERM-1",
                "transactionReference": "logon-session-1",
                "requestedAt": "2026-06-05T04:00:00Z",
                "httpStatus": 200,
                "succeeded": false,
                "responseCode": "99",
                "responseText": "LOGON REQUIRED",
                "catid": null,
                "caid": null,
                "pinPadVersion": null,
                "message": "LOGON REQUIRED"
              }
            }
            """));
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.TestConnectionAsync(CardTerminalEnvironment.Sandbox);

        Assert.False(result.Succeeded);
        Assert.Equal("LOGON REQUIRED", result.Message);
        using var responseLog = FindLinklyLog(logs.Lines, "logon-test", "response");
        Assert.Equal("99", responseLog.RootElement.GetProperty("response").GetProperty("body").GetProperty("data").GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task TestConnectionAsync_does_not_treat_missing_logon_test_endpoint_as_success()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.TestConnectionAsync(CardTerminalEnvironment.Sandbox);

        Assert.False(result.Succeeded);
        Assert.Contains("404", result.Message, StringComparison.Ordinal);
        Assert.Equal(
            "https://api.example/api/v1/linkly/cloud-backend/logon-test?environment=Sandbox",
            Assert.Single(requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PurchaseAsync_uses_backend_contract_without_client_secret_payload()
    {
        using var logs = new ConsoleLogCapture();
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-1",
                        "status": "Pending",
                        "txnRef": "260601120001",
                        "displayText": "PRESENT CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "okKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"PRESS OK\"], \"OKKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:03Z"
                          }
                        ]
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-1",
                        "status": "Completed",
                        "txnRef": "260601120001",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "MERCHANT RECEIPT\nAPPROVED",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:00Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal(
            "ANZBACKEND:260601120001:session=backend-session-1:environment=Sandbox",
            result.Reference);
        Assert.True(LinklyBackendPaymentReference.TryGetPrintMarker(result.Reference, out var environment, out var sessionId));
        Assert.Equal("Sandbox", environment);
        Assert.Equal("backend-session-1", sessionId);
        using var activeRequestLog = FindLinklyLog(logs.Lines, "active session", "request");
        Assert.Equal("GET", activeRequestLog.RootElement.GetProperty("request").GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, activeRequestLog.RootElement.GetProperty("request").GetProperty("body").ValueKind);
        using var startRequestLog = FindLinklyLog(logs.Lines, "start transaction", "request");
        Assert.Equal("POST", startRequestLog.RootElement.GetProperty("request").GetProperty("method").GetString());
        Assert.Equal("Sandbox", startRequestLog.RootElement.GetProperty("request").GetProperty("body").GetProperty("environment").GetString());
        Assert.Equal("P", startRequestLog.RootElement.GetProperty("request").GetProperty("body").GetProperty("txnType").GetString());
        using var startResponseLog = FindLinklyLog(logs.Lines, "start transaction", "response");
        Assert.Equal("260601120001", startResponseLog.RootElement.GetProperty("details").GetProperty("txnRef").GetString());
        Assert.Equal("backend-session-1", startResponseLog.RootElement.GetProperty("response").GetProperty("body").GetProperty("data").GetProperty("sessionId").GetString());
        using var statusResponseLog = FindLinklyLog(logs.Lines, "status", "response");
        Assert.Equal("GET", statusResponseLog.RootElement.GetProperty("response").GetProperty("method").GetString());
        Assert.Equal("Completed", statusResponseLog.RootElement.GetProperty("response").GetProperty("body").GetProperty("data").GetProperty("status").GetString());
        var loggedDisplayText = statusResponseLog.RootElement
            .GetProperty("response")
            .GetProperty("body")
            .GetProperty("data")
            .GetProperty("displayText");
        Assert.True(loggedDisplayText.GetProperty("hasValue").GetBoolean());
        var loggedReceiptText = statusResponseLog.RootElement
            .GetProperty("response")
            .GetProperty("body")
            .GetProperty("data")
            .GetProperty("receiptText");
        Assert.True(loggedReceiptText.GetProperty("hasValue").GetBoolean());
        Assert.Equal(2, loggedReceiptText.GetProperty("lineCount").GetInt32());
        Assert.DoesNotContain("CARDHOLDER DISPLAY SECRET", logs.Lines, StringComparer.Ordinal);
        Assert.DoesNotContain("MERCHANT RECEIPT", logs.Lines, StringComparer.Ordinal);
        Assert.Collection(
            requests,
            active =>
            {
                Assert.Equal(HttpMethod.Get, active.Method);
                Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/active?environment=Sandbox", active.RequestUri!.AbsoluteUri);
            },
            create =>
            {
                Assert.Equal(HttpMethod.Post, create.Method);
                Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions", create.RequestUri!.AbsoluteUri);
            },
            status =>
            {
                Assert.Equal(HttpMethod.Get, status.Method);
                Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/backend-session-1/status?environment=Sandbox", status.RequestUri!.AbsoluteUri);
            });
        var createBody = await requests[1].Content!.ReadAsStringAsync();
        Assert.Equal("Sandbox", ReadJsonString(createBody, "environment"));
        Assert.Equal("P", ReadJsonString(createBody, "txnType"));
        Assert.Equal("1000", ReadJsonString(createBody, "amtPurchase"));
        Assert.Null(TryReadJsonString(createBody, "accessToken"));
        Assert.Null(TryReadJsonString(createBody, "restBaseUrl"));
        Assert.Null(TryReadJsonString(createBody, "storeCode"));
        Assert.Null(TryReadJsonString(createBody, "deviceCode"));
        Assert.Null(TryReadJsonString(createBody, "txnRef"));
        Assert.Contains(dialog.States, state => state.DisplayText == "PRESENT CARD");
        Assert.Contains(dialog.States, state => state.ReceiptText == "MERCHANT RECEIPT\nAPPROVED");
        Assert.Equal("MERCHANT RECEIPT\nAPPROVED", Assert.Single(result.CardTransactions!).ReceiptText);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_uses_official_get_transaction_payload_refund_reference_in_payment_reference()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(PendingSessionJson("backend-rfn-session", "TXN-RFN")),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-rfn-session",
                        "status": "Completed",
                        "txnRef": "TXN-RFN",
                        "responseCode": "08",
                        "responseText": "APPROVE WITH SIG",
                        "transactionSuccess": true,
                        "displayText": "APPROVE WITH SIG",
                        "receiptText": "MERCHANT RECEIPT\nAPPROVE WITH SIG",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{ \"Response\": { \"Success\": true, \"TxnRef\": \"TXN-RFN\", \"ResponseCode\": \"08\", \"ResponseText\": \"APPROVE WITH SIG\", \"AmtPurchase\": 1008, \"PurchaseAnalysisData\": { \"RFN\": \"RFN-OFFICIAL\" } } }",
                            "receivedAt": "2026-06-01T02:00:03Z"
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal(
            "ANZBACKEND:TXN-RFN:RFN-OFFICIAL:session=backend-rfn-session:environment=Sandbox",
            result.Reference);
        Assert.Equal(10.08m, result.AuthorizedAmount);
        Assert.Equal(10.08m, Assert.Single(result.CardTransactions!).Amount);
    }

    [Fact]
    public async Task PurchaseAsync_returns_result_unknown_when_status_poll_fails_after_backend_start()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-unknown",
                        "status": "Pending",
                        "txnRef": "260601120099",
                        "displayText": "PRESENT CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new HttpRequestException("backend status offline")
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.True(result.ResultUnknown);
        Assert.False(result.FallbackAllowed);
        Assert.Equal("linkly.backend.resultUnknown", result.StatusKey);
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task PurchaseAsync_returns_result_unknown_when_start_response_read_is_cancelled_after_backend_submit()
    {
        using var logs = new ConsoleLogCapture();
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => throw new OperationCanceledException("start response read cancelled"),
                _ => throw new InvalidOperationException("No further backend calls are expected.")
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.True(result.ResultUnknown);
        Assert.False(result.FallbackAllowed);
        Assert.Equal("linkly.backend.resultUnknown", result.StatusKey);
        Assert.DoesNotContain("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown", result.Message, StringComparison.OrdinalIgnoreCase);
        AssertContainsOperationCancelledLog(logs.Lines, transactionSubmitted: true, businessTimeoutCancelled: false);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task PurchaseAsync_allows_fallback_when_active_session_read_is_cancelled_before_backend_submit()
    {
        using var logs = new ConsoleLogCapture();
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => throw new OperationCanceledException("active session read cancelled"),
                _ => throw new InvalidOperationException("No further backend calls are expected.")
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.True(result.FallbackAllowed);
        Assert.Equal("linkly.backend.waitCancelled", result.StatusKey);
        Assert.DoesNotContain("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
        AssertContainsOperationCancelledLog(logs.Lines, transactionSubmitted: false, businessTimeoutCancelled: false);
        Assert.Single(requests);
    }

    [Fact]
    public async Task PurchaseAsync_blocks_cloud_backend_when_health_request_is_offline_before_start()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(
            request =>
            {
                requests.Add(CloneRequestWithBody(request));
                if (request.RequestUri!.AbsolutePath.EndsWith("/health", StringComparison.Ordinal))
                {
                    throw new HttpRequestException("backend offline");
                }

                throw new InvalidOperationException("Transaction should not start when backend health is offline.");
            },
            passHealthRequestsToHandler: true);
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.True(result.FallbackAllowed);
        Assert.Equal("linkly.backend.unavailable", result.StatusKey);
        Assert.Single(requests);
        Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/health?environment=Sandbox", requests[0].RequestUri!.AbsoluteUri);
        Assert.Empty(dialog.States);
    }

    [Fact]
    public async Task PurchaseAsync_blocks_cloud_backend_when_health_is_not_ready_before_start()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(
            request =>
            {
                requests.Add(CloneRequestWithBody(request));
                return request.RequestUri!.AbsolutePath switch
                {
                    "/api/v1/linkly/cloud-backend/health" => JsonResponse(
                        """
                        {
                          "success": true,
                          "data": {
                            "environment": "Sandbox",
                            "storeCode": "S01",
                            "deviceCode": "TERM-1",
                            "isReady": false,
                            "publicNotificationBaseUrl": null,
                            "checks": [
                              {
                                "code": "PUBLIC_CALLBACK_URL",
                                "isReady": false,
                                "message": "Linkly Cloud notification callback URL must be public HTTPS."
                              }
                            ]
                          }
                        }
                        """),
                    _ => throw new InvalidOperationException("Transaction should not start when backend health is not ready.")
                };
            },
            passHealthRequestsToHandler: true);
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.True(result.FallbackAllowed);
        Assert.Equal("linkly.backend.unavailable", result.StatusKey);
        Assert.Contains("public HTTPS", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(requests);
        Assert.Empty(dialog.States);
    }

    [Fact]
    public async Task PurchaseAsync_allows_fallback_when_backend_rejects_start_before_session()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ => JsonResponse(
                    """
                    {
                      "success": false,
                      "message": "Linkly Cloud notification bearer is not configured."
                    }
                    """,
                    HttpStatusCode.BadRequest)
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.True(result.FallbackAllowed);
        Assert.False(result.ResultUnknown);
        Assert.Equal("linkly.backend.configIncomplete", result.StatusKey);
        Assert.Contains("notification bearer", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task PurchaseAsync_binds_local_attempt_when_backend_returns_session_before_final_status()
    {
        var bindCount = 0;
        string? boundSessionId = null;
        string? boundTxnRef = null;
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-early",
                        "status": "Pending",
                        "txnRef": "260601120099",
                        "displayText": "PRESENT CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-early",
                        "status": "Completed",
                        "txnRef": "260601120099",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "APPROVED RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:00Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog, TimeSpan.Zero, null, null, accessor);
        using var scope = accessor.Begin(new LinklyPaymentAttemptContext(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            (sessionId, txnRef, _, _) =>
            {
                bindCount++;
                boundSessionId = sessionId;
                boundTxnRef = txnRef;
                return Task.CompletedTask;
            }));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal(1, bindCount);
        Assert.Equal("backend-session-early", boundSessionId);
        Assert.Equal("260601120099", boundTxnRef);
    }

    [Fact]
    public async Task PurchaseAsync_keeps_final_dialog_open_when_backend_declines()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "declined-session-1",
                        "status": "Completed",
                        "txnRef": "260601120012",
                        "responseCode": "05",
                        "responseText": "DECLINED",
                        "displayText": "DECLINED",
                        "receiptText": "DECLINED RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:00Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """);
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal(0, dialog.CloseCallCount);
        var finalState = Assert.Single(dialog.States.Where(state => state.IsFinal));
        Assert.Equal("DECLINED", finalState.DisplayText);
        Assert.Equal("DECLINED", finalState.ResponseText);
    }

    [Fact]
    public async Task PurchaseAsync_treats_signature_approval_response_code_08_as_approved_and_closes_dialog()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-approved-session-1",
                        "status": "Completed",
                        "txnRef": "260601120014",
                        "responseCode": "08",
                        "responseText": "APPROVE WITH SIG",
                        "transactionSuccess": true,
                        "displayText": "Completed",
                        "receiptText": "TOTAL      AUD     $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """);
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal("08", result.ResponseCode);
        Assert.Equal("APPROVE WITH SIG", result.ResponseText);
        var transaction = Assert.Single(result.CardTransactions!);
        Assert.Equal("08", transaction.ResponseCode);
        Assert.Equal("APPROVE WITH SIG", transaction.ResponseText);
        Assert.Contains("PLEASE SIGN:", transaction.ReceiptText, StringComparison.Ordinal);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_posts_auth_sendkey_for_signature_confirmation_before_completing()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-sendkey-session-1",
                        "status": "Pending",
                        "txnRef": "260601120015",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "TOTAL      AUD     $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "authoriseKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"AuthoriseKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-sendkey-session-1",
                        "status": "Pending",
                        "txnRef": "260601120015",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "PROCESSING",
                        "receiptText": "TOTAL      AUD     $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 202,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-sendkey-session-1",
                        "status": "Completed",
                        "txnRef": "260601120015",
                        "responseCode": "08",
                        "responseText": "APPROVE WITH SIG",
                        "transactionSuccess": true,
                        "displayText": "Completed",
                        "receiptText": "TOTAL      AUD     $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.Auth, null));
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal("08", result.ResponseCode);
        Assert.Equal("APPROVE WITH SIG", result.ResponseText);
        Assert.Collection(
            requests,
            active => Assert.Equal(HttpMethod.Get, active.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method),
            sendKey => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/signature-sendkey-session-1/sendkey", sendKey.RequestUri!.AbsoluteUri),
            status => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/signature-sendkey-session-1/status?environment=Sandbox", status.RequestUri!.AbsoluteUri));
        var sendKeyBody = await requests[2].Content!.ReadAsStringAsync();
        Assert.Equal("Sandbox", ReadJsonString(sendKeyBody, "environment"));
        Assert.Equal(LinklyTerminalDialogKeys.Auth, ReadJsonString(sendKeyBody, "key"));
        Assert.Null(TryReadJsonString(sendKeyBody, "data"));
        var signatureState = Assert.Single(dialog.States.Where(state => state.DisplayText == "SIGNATURE OK?"));
        var button = Assert.Single(signatureState.DisplayButtons!);
        Assert.Equal("linkly.backend.dialog.button.authoriseSignature", button.TextResourceKey);
        Assert.Equal(LinklyTerminalDialogKeys.Auth, button.Key);
        Assert.Contains("PLEASE SIGN:", Assert.Single(result.CardTransactions!).ReceiptText, StringComparison.Ordinal);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_waits_for_final_decline_after_signature_is_rejected()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-receipt-fallback-session-1",
                        "status": "Pending",
                        "txnRef": "260601120016",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "TOTAL      AUD     $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "acceptYesKeyFlag": true,
                        "declineNoKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SWIPE CARD\"],\"CancelKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T01:59:58Z"
                          },
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"AcceptYesKeyFlag\":true,\"DeclineNoKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-receipt-fallback-session-1",
                        "status": "Pending",
                        "txnRef": "260601120016",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "PROCESSING",
                        "receiptText": "TOTAL      AUD     $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 202,
                        "notifications": []
                      }
                    }
                    """),
                4 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-receipt-fallback-session-1",
                        "status": "Completed",
                        "txnRef": "260601120016",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "----------------------\n*** MERCHANT COPY ***\nTOTAL      AUD     $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-receipt-fallback-session-1",
                        "status": "Completed",
                        "txnRef": "260601120016",
                        "responseCode": "Q6",
                        "responseText": "SIGNATURE ERROR",
                        "transactionSuccess": false,
                        "displayText": "TRANSACTION DECLINED",
                        "receiptText": "DECLINED - Q6\nSIGNATURE ERROR",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":false,\"TxnRef\":\"260601120016\",\"ResponseCode\":\"Q6\",\"ResponseText\":\"SIGNATURE ERROR\",\"AmtPurchase\":1008}}",
                            "receivedAt": "2026-06-01T02:00:02Z"
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.No, null));
        var signaturePrinter = new FakeLinklyBankReceiptPrinter();
        var client = CreateClient(handler, dialog, signaturePrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("Q6", result.ResponseCode);
        Assert.Equal("SIGNATURE ERROR", result.ResponseText);
        Assert.Collection(
            requests,
            active => Assert.Equal(HttpMethod.Get, active.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method),
            sendKey => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/signature-receipt-fallback-session-1/sendkey", sendKey.RequestUri!.AbsoluteUri),
            completedWithoutResult => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/signature-receipt-fallback-session-1/status?environment=Sandbox", completedWithoutResult.RequestUri!.AbsoluteUri),
            declined => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/signature-receipt-fallback-session-1/status?environment=Sandbox", declined.RequestUri!.AbsoluteUri));
        var sendKeyBody = await requests[2].Content!.ReadAsStringAsync();
        Assert.Equal("Sandbox", ReadJsonString(sendKeyBody, "environment"));
        Assert.Equal(LinklyTerminalDialogKeys.No, ReadJsonString(sendKeyBody, "key"));
        Assert.Null(TryReadJsonString(sendKeyBody, "data"));
        Assert.Collection(
            signaturePrinter.Prints,
            signature => Assert.Equal(LinklyBankReceiptKind.SignatureRequired, signature.Kind),
            declined =>
            {
                Assert.Equal(LinklyBankReceiptKind.Declined, declined.Kind);
                Assert.Equal("DECLINED - Q6\nSIGNATURE ERROR", declined.ReceiptText);
            });
        var failedTransaction = Assert.Single(result.CardTransactions!);
        Assert.Equal("Q6", failedTransaction.ResponseCode);
        Assert.Equal("SIGNATURE ERROR", failedTransaction.ResponseText);
        var signatureState = Assert.Single(dialog.States.Where(state => state.DisplayText == "SIGNATURE OK?"));
        Assert.False(signatureState.SupportsCancelPayment);
        Assert.Equal(0, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_prints_signature_slip_once_when_signature_prompt_repeats()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 or 3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-print-once-session",
                        "status": "Pending",
                        "txnRef": "260601120017",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "LINE2\nCREDIT ACCOUNT\nPURCHASE AUD $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "acceptYesKeyFlag": true,
                        "declineNoKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"AcceptYesKeyFlag\":true,\"DeclineNoKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          }
                        ]
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-print-once-session",
                        "status": "Completed",
                        "txnRef": "260601120017",
                        "responseCode": "08",
                        "responseText": "APPROVE WITH SIG",
                        "transactionSuccess": true,
                        "displayText": "Completed",
                        "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var signaturePrinter = new FakeLinklyBankReceiptPrinter();
        var client = CreateClient(handler, dialog, signaturePrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var print = Assert.Single(signaturePrinter.Prints);
        Assert.Equal("Sandbox", print.Environment);
        Assert.Equal("signature-print-once-session", print.SessionId);
        Assert.Contains("PLEASE SIGN:", print.ReceiptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PurchaseAsync_prints_declined_bank_receipt_after_signature_decline_final_result()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-declined-print-session",
                        "status": "Pending",
                        "txnRef": "260601120038",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "LINE2\nCREDIT ACCOUNT\nPURCHASE AUD $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "acceptYesKeyFlag": true,
                        "declineNoKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"AcceptYesKeyFlag\":true,\"DeclineNoKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-declined-print-session",
                        "status": "Pending",
                        "txnRef": "260601120038",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "PROCESSING",
                        "receiptText": "LINE2\nCREDIT ACCOUNT\nPURCHASE AUD $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 202,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-declined-print-session",
                        "status": "Completed",
                        "txnRef": "260601120038",
                        "responseCode": "Q6",
                        "responseText": "SIGNATURE ERROR",
                        "transactionSuccess": false,
                        "displayText": "TRANSACTION DECLINED",
                        "receiptText": "DECLINED - Q6\nSIGNATURE ERROR",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":false,\"TxnRef\":\"260601120038\",\"ResponseCode\":\"Q6\",\"ResponseText\":\"SIGNATURE ERROR\",\"AmtPurchase\":1008,\"CardType\":\"VISA\",\"Pan\":\"4111111111111234\"}}",
                            "receivedAt": "2026-06-01T02:00:04Z"
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.No, null));
        var bankReceiptPrinter = new FakeLinklyBankReceiptPrinter();
        var client = CreateClient(handler, dialog, bankReceiptPrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("Q6", result.ResponseCode);
        Assert.Equal("SIGNATURE ERROR", result.ResponseText);
        Assert.Collection(
            bankReceiptPrinter.Prints,
            signature =>
            {
                Assert.Equal(LinklyBankReceiptKind.SignatureRequired, signature.Kind);
                Assert.Contains("PLEASE SIGN:", signature.ReceiptText, StringComparison.Ordinal);
            },
            declined =>
            {
                Assert.Equal(LinklyBankReceiptKind.Declined, declined.Kind);
                Assert.Equal("DECLINED - Q6\nSIGNATURE ERROR", declined.ReceiptText);
                Assert.Equal("VISA", declined.CardType);
                Assert.Equal("****1234", declined.MaskedCardNumber);
                Assert.Equal("Q6", declined.ResponseCode);
                Assert.Equal("SIGNATURE ERROR", declined.ResponseText);
            });
        var sendKeyBody = await requests[2].Content!.ReadAsStringAsync();
        Assert.Equal(LinklyTerminalDialogKeys.No, ReadJsonString(sendKeyBody, "key"));
    }

    [Fact]
    public async Task PurchaseAsync_keeps_declined_when_signature_declined_receipt_print_fails()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "signature-declined-print-fail-session",
                    "status": "Pending",
                    "txnRef": "260601120039",
                    "responseCode": null,
                    "responseText": null,
                    "displayText": "SIGNATURE OK?",
                    "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "declineNoKeyFlag": true,
                    "notifications": [
                      {
                        "type": "display",
                        "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"DeclineNoKeyFlag\":true}}",
                        "receivedAt": "2026-06-01T02:00:00Z"
                      }
                    ]
                  }
                }
                """),
            "/api/v1/linkly/cloud-backend/transactions/signature-declined-print-fail-session/sendkey" => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "signature-declined-print-fail-session",
                    "status": "Pending",
                    "txnRef": "260601120039",
                    "displayText": "PROCESSING",
                    "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 202,
                    "notifications": []
                  }
                }
                """),
            _ => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "signature-declined-print-fail-session",
                    "status": "Completed",
                    "txnRef": "260601120039",
                    "responseCode": "Q6",
                    "responseText": "SIGNATURE ERROR",
                    "transactionSuccess": false,
                    "displayText": "TRANSACTION DECLINED",
                    "receiptText": "DECLINED - Q6\nSIGNATURE ERROR",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """)
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.No, null));
        var bankReceiptPrinter = new FakeLinklyBankReceiptPrinter
        {
            Results =
            {
                new ReceiptPrintResult(true, "signature printed"),
                new ReceiptPrintResult(false, "printer offline")
            }
        };
        var client = CreateClient(handler, dialog, bankReceiptPrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("Q6", result.ResponseCode);
        Assert.Equal("SIGNATURE ERROR", result.ResponseText);
        Assert.Contains("SIGNATURE ERROR", result.Message, StringComparison.Ordinal);
        Assert.Contains("printer offline", result.Message, StringComparison.Ordinal);
        Assert.Collection(
            bankReceiptPrinter.Prints,
            signature => Assert.Equal(LinklyBankReceiptKind.SignatureRequired, signature.Kind),
            declined => Assert.Equal(LinklyBankReceiptKind.Declined, declined.Kind));
    }

    [Fact]
    public async Task PurchaseAsync_prints_declined_bank_receipt_when_signature_decline_sendkey_network_fails_but_terminal_declines()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-decline-sendkey-network-session",
                        "status": "Pending",
                        "txnRef": "260601120041",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "declineNoKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"DeclineNoKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          }
                        ]
                      }
                    }
                    """),
                "/api/v1/linkly/cloud-backend/transactions/signature-decline-sendkey-network-session/sendkey" => throw new HttpRequestException("connection reset after send"),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-decline-sendkey-network-session",
                        "status": "Completed",
                        "txnRef": "260601120041",
                        "responseCode": "Q6",
                        "responseText": "SIGNATURE ERROR",
                        "transactionSuccess": false,
                        "displayText": "TRANSACTION DECLINED",
                        "receiptText": "DECLINED - Q6\nSIGNATURE ERROR",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":false,\"TxnRef\":\"260601120041\",\"ResponseCode\":\"Q6\",\"ResponseText\":\"SIGNATURE ERROR\",\"AmtPurchase\":1008}}",
                            "receivedAt": "2026-06-01T02:00:04Z"
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.No, null));
        var bankReceiptPrinter = new FakeLinklyBankReceiptPrinter();
        var client = CreateClient(handler, dialog, bankReceiptPrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("Q6", result.ResponseCode);
        Assert.Equal("SIGNATURE ERROR", result.ResponseText);
        Assert.Contains(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/sendkey", StringComparison.OrdinalIgnoreCase));
        Assert.Collection(
            bankReceiptPrinter.Prints,
            signature => Assert.Equal(LinklyBankReceiptKind.SignatureRequired, signature.Kind),
            declined =>
            {
                Assert.Equal(LinklyBankReceiptKind.Declined, declined.Kind);
                Assert.Equal("DECLINED - Q6\nSIGNATURE ERROR", declined.ReceiptText);
            });
    }

    [Fact]
    public async Task PurchaseAsync_does_not_print_declined_bank_receipt_when_signature_decline_sendkey_is_rejected()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-decline-sendkey-rejected-session",
                        "status": "Pending",
                        "txnRef": "260601120042",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "declineNoKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"DeclineNoKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          }
                        ]
                      }
                    }
                    """),
                "/api/v1/linkly/cloud-backend/transactions/signature-decline-sendkey-rejected-session/sendkey" => JsonResponse(
                    """
                    {
                      "success": false,
                      "data": null,
                      "errorCode": "LINKLY_CLOUD_BACKEND_REQUEST_INVALID",
                      "message": "Linkly Cloud rejected the terminal action."
                    }
                    """,
                    HttpStatusCode.BadRequest),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-decline-sendkey-rejected-session",
                        "status": "Completed",
                        "txnRef": "260601120042",
                        "responseCode": "Q6",
                        "responseText": "SIGNATURE ERROR",
                        "transactionSuccess": false,
                        "displayText": "TRANSACTION DECLINED",
                        "receiptText": "DECLINED - Q6\nSIGNATURE ERROR",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":false,\"TxnRef\":\"260601120042\",\"ResponseCode\":\"Q6\",\"ResponseText\":\"SIGNATURE ERROR\",\"AmtPurchase\":1008}}",
                            "receivedAt": "2026-06-01T02:00:04Z"
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.No, null));
        var bankReceiptPrinter = new FakeLinklyBankReceiptPrinter();
        var client = CreateClient(handler, dialog, bankReceiptPrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("Q6", result.ResponseCode);
        Assert.Equal("SIGNATURE ERROR", result.ResponseText);
        Assert.Contains(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/sendkey", StringComparison.OrdinalIgnoreCase));
        var print = Assert.Single(bankReceiptPrinter.Prints);
        Assert.Equal(LinklyBankReceiptKind.SignatureRequired, print.Kind);
        Assert.Contains("PLEASE SIGN:", print.ReceiptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PurchaseAsync_does_not_print_declined_bank_receipt_for_non_signature_decline()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "ordinary-declined-session",
                    "status": "Completed",
                    "txnRef": "260601120040",
                    "responseCode": "55",
                    "responseText": "DECLINED",
                    "transactionSuccess": false,
                    "displayText": "TRANSACTION DECLINED",
                    "receiptText": "DECLINED - 55",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """)
        });
        var bankReceiptPrinter = new FakeLinklyBankReceiptPrinter();
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService(), bankReceiptPrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Empty(bankReceiptPrinter.Prints);
    }

    [Fact]
    public async Task PurchaseAsync_blocks_signature_approval_until_signature_slip_prints()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-print-retry-session",
                        "status": "Pending",
                        "txnRef": "260601120018",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "SIGNATURE OK?",
                        "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "authoriseKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{\"Response\":{\"DisplayText\":[\"SIGNATURE OK?\"],\"AuthoriseKeyFlag\":true}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-print-retry-session",
                        "status": "Pending",
                        "txnRef": "260601120018",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "PROCESSING",
                        "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 202,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-print-retry-session",
                        "status": "Completed",
                        "txnRef": "260601120018",
                        "responseCode": "08",
                        "responseText": "APPROVE WITH SIG",
                        "transactionSuccess": true,
                        "displayText": "Completed",
                        "receiptText": "APPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.Auth, null));
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.Auth, null));
        var signaturePrinter = new FakeLinklyBankReceiptPrinter
        {
            Results =
            {
                new ReceiptPrintResult(false, "printer offline"),
                new ReceiptPrintResult(true, "printed")
            }
        };
        var client = CreateClient(handler, dialog, signaturePrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal(2, signaturePrinter.Prints.Count);
        Assert.Single(requests.Where(request => request.RequestUri!.AbsolutePath.EndsWith("/sendkey", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(dialog.States, state => state.Message == "printer offline");
        Assert.Contains(dialog.States, state => state.DisplayText == "SIGNATURE OK?" && state.Message is null);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Canceled")]
    public async Task PurchaseAsync_treats_backend_cancel_status_as_final_cancel(string status)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(
                    $$"""
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "cancelled-session-1",
                        "status": "{{status}}",
                        "txnRef": "260601120013",
                        "responseCode": "C0",
                        "responseText": "CANCELLED",
                        "displayText": "CANCELLED",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """);
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(
            handler,
            dialog,
            TimeSpan.Zero,
            null,
            null,
            businessWait: TimeSpan.FromMilliseconds(20));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.Contains("cancel", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, requests.Count);
        var finalState = Assert.Single(dialog.States.Where(state => state.IsFinal));
        Assert.Equal("CANCELLED", finalState.ResponseText);
    }

    [Fact]
    public async Task RefundAsync_recovers_missing_backend_rfn_from_original_session_notification()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/original-session/status" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "original-session",
                        "status": "Completed",
                        "txnRef": "260601120001",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "MERCHANT RECEIPT\nAPPROVED",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":true,\"ResponseCode\":\"00\",\"ResponseText\":\"APPROVED\",\"PurchaseAnalysisData\":{\"RFN\":\"RFN-ORIGINAL\"}}}",
                            "receivedAt": "2026-06-01T02:00:01Z"
                          }
                        ]
                      }
                    }
                    """),
                "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "refund-session",
                        "status": "Completed",
                        "txnRef": "260601120099",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "REFUND RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.RefundAsync(
            2m,
            CreateSession(),
            CreateSettings(),
            "ANZBACKEND:260601120001:session=original-session:environment=Sandbox");

        Assert.True(result.Approved);
        var createBody = await requests.Single(request => request.Method == HttpMethod.Post).Content!.ReadAsStringAsync();
        Assert.Contains("RFN-ORIGINAL", createBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefundAsync_recovers_missing_backend_rfn_from_array_purchase_analysis_data()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/original-session/status" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "original-session",
                        "status": "Completed",
                        "txnRef": "260601120001",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "MERCHANT RECEIPT\nAPPROVED",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":true,\"ResponseCode\":\"00\",\"ResponseText\":\"APPROVED\",\"PurchaseAnalysisData\":[{\"Key\":\"AMT\",\"Value\":\"200\"},{\"Key\":\"RFN\",\"Value\":\"RFN-ARRAY\"}]}}",
                            "receivedAt": "2026-06-01T02:00:01Z"
                          }
                        ]
                      }
                    }
                    """),
                "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "refund-session",
                        "status": "Completed",
                        "txnRef": "260601120099",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "REFUND RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.RefundAsync(
            2m,
            CreateSession(),
            CreateSettings(),
            "ANZBACKEND:260601120001:session=original-session:environment=Sandbox");

        Assert.True(result.Approved);
        var createBody = await requests.Single(request => request.Method == HttpMethod.Post).Content!.ReadAsStringAsync();
        Assert.Contains("RFN-ARRAY", createBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefundAsync_uses_original_backend_txn_ref_when_session_has_no_rfn()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/original-session/status" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "original-session",
                        "status": "Completed",
                        "txnRef": "260601120001",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "MERCHANT RECEIPT\nAPPROVED",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":true,\"ResponseCode\":\"00\",\"ResponseText\":\"APPROVED\",\"PurchaseAnalysisData\":{}}}",
                            "receivedAt": "2026-06-01T02:00:01Z"
                          }
                        ]
                      }
                    }
                    """),
                "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "refund-session",
                        "status": "Completed",
                        "txnRef": "260601120099",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "REFUND RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.RefundAsync(
            2m,
            CreateSession(),
            CreateSettings(),
            "ANZBACKEND:260601120001:session=original-session:environment=Sandbox");

        Assert.True(result.Approved);
        var createBody = await requests.Single(request => request.Method == HttpMethod.Post).Content!.ReadAsStringAsync();
        Assert.Contains("260601120001", createBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PurchaseAsync_final_dialog_state_uses_response_text_instead_of_stale_display_prompt()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "final-display-session-1",
                        "status": "Completed",
                        "txnRef": "260601120011",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "PRESENT CARD",
                        "receiptText": "FINAL RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:00Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """);
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var finalState = Assert.Single(dialog.States.Where(state => state.IsFinal));
        Assert.Equal("APPROVED", finalState.DisplayText);
        Assert.Equal("APPROVED", finalState.ResponseText);
        Assert.DoesNotContain(dialog.States, state =>
            state.IsFinal &&
            state.DisplayText == "PRESENT CARD");
    }

    [Fact]
    public async Task PurchaseAsync_waits_briefly_for_receipt_after_transaction_completion()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-2",
                        "status": "Pending",
                        "txnRef": "260601120002",
                        "displayText": "PRESENT CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "okKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"PRESS OK\"], \"OKKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:03Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-2",
                        "status": "Completed",
                        "txnRef": "260601120002",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backend-session-2",
                        "status": "Completed",
                        "txnRef": "260601120002",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "CUSTOMER RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:01Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal(4, requests.Count);
        Assert.Equal("CUSTOMER RECEIPT", Assert.Single(result.CardTransactions!).ReceiptText);
    }

    [Fact]
    public async Task PurchaseAsync_uses_protected_session_result_when_later_transaction_notification_conflicts()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "protected-session-1",
                        "status": "Completed",
                        "txnRef": "260601120099",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "APPROVED RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": [
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":true,\"ResponseCode\":\"00\",\"ResponseText\":\"APPROVED\",\"AuthCode\":\"AUTH1\"}}",
                            "receivedAt": "2026-06-01T02:00:00Z"
                          },
                          {
                            "type": "transaction",
                            "payloadJson": "{\"Response\":{\"Success\":false,\"ResponseCode\":\"05\",\"ResponseText\":\"DECLINED\",\"AuthCode\":\"BAD\"}}",
                            "receivedAt": "2026-06-01T02:00:01Z"
                          }
                        ]
                      }
                    }
                    """);
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var transaction = Assert.Single(result.CardTransactions!);
        Assert.Equal("00", transaction.ResponseCode);
        Assert.Equal("APPROVED", transaction.ResponseText);
        Assert.Equal("AUTH1", transaction.AuthCode);
    }

    [Fact]
    public async Task PurchaseAsync_rejects_existing_active_session_before_starting_new_transaction()
    {
        var requests = new List<HttpRequestMessage>();
        var bindCount = 0;
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "active-session-1",
                        "status": "Pending",
                        "txnRef": "260601120010",
                        "displayText": "REMOVE CARD",
                        "receiptText": null,
                        "recoveryCount": 1,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 409,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog, TimeSpan.Zero, null, null, accessor);
        using var scope = accessor.Begin(new LinklyPaymentAttemptContext(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            (_, _, _, _) =>
            {
                bindCount++;
                return Task.CompletedTask;
            }));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Contains("unfinished card transaction", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, bindCount);
        Assert.DoesNotContain(requests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri!.AbsolutePath.EndsWith("/transactions", StringComparison.Ordinal));
        var activeRequest = Assert.Single(requests);
        Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/active?environment=Sandbox", activeRequest.RequestUri!.AbsoluteUri);
        var finalState = Assert.Single(dialog.States);
        Assert.Equal("active-session-1", finalState.SessionId);
        Assert.True(finalState.IsFinal);
        Assert.Contains("unfinished card transaction", finalState.DisplayText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_rejects_active_session_after_conflict_without_generic_backend_failure()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": false,
                      "message": "Active session exists."
                    }
                    """,
                    HttpStatusCode.Conflict),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "conflict-session-1",
                        "status": "Pending",
                        "txnRef": "260601120020",
                        "displayText": "WAITING",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 409,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.NotEqual("ANZ Linkly Cloud backend communication failed.", result.Message);
        Assert.Contains("unfinished card transaction", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Collection(
            requests,
            activeBeforeStart => Assert.Equal(HttpMethod.Get, activeBeforeStart.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method),
            activeAfterConflict => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/active?environment=Sandbox", activeAfterConflict.RequestUri!.AbsoluteUri));
        var finalState = Assert.Single(dialog.States);
        Assert.Equal("conflict-session-1", finalState.SessionId);
        Assert.True(finalState.IsFinal);
        Assert.Contains("unfinished card transaction", finalState.DisplayText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_sends_dialog_key_during_status_polling()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "key-session-1",
                        "status": "Pending",
                        "txnRef": "260601120030",
                        "displayText": "PRESS OK",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "key-session-1",
                        "status": "Pending",
                        "txnRef": "260601120030",
                        "displayText": "PROCESSING",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "key-session-1",
                        "status": "Completed",
                        "txnRef": "260601120030",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "KEY RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:04Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction("OK", null));
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Collection(
            requests,
            active => Assert.Equal(HttpMethod.Get, active.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method),
            sendKey => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/key-session-1/sendkey", sendKey.RequestUri!.AbsoluteUri),
            status => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/key-session-1/status?environment=Sandbox", status.RequestUri!.AbsoluteUri));
        var sendKeyBody = await requests[2].Content!.ReadAsStringAsync();
        Assert.Equal("Sandbox", ReadJsonString(sendKeyBody, "environment"));
        Assert.Equal("0", ReadJsonString(sendKeyBody, "key"));
        Assert.Null(TryReadJsonString(sendKeyBody, "data"));
        Assert.Null(TryReadJsonString(sendKeyBody, "accessToken"));
        Assert.Contains(dialog.States, state => state.DisplayText == "PRESS OK");
    }

    [Fact]
    public async Task PurchaseAsync_real_backend_dialog_cancel_payment_posts_cancel_sendkey()
    {
        var requests = new List<HttpRequestMessage>();
        var dialog = new WpfLinklyTerminalDialogService(new LocalizationService());
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "real-dialog-cancel-session",
                        "status": "Pending",
                        "txnRef": "260601120031",
                        "displayText": "SWIPE CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "cancelKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"SWIPE CARD\"], \"CancelKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:03Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => StatusAfterCashierClicksCancel(dialog),
                4 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "real-dialog-cancel-session",
                        "status": "Completed",
                        "txnRef": "260601120031",
                        "responseCode": "TM",
                        "responseText": "OPERATOR TIMEOUT",
                        "displayText": "CANCELLED",
                        "receiptText": "CANCEL RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = new LinklyBackendTerminalClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") },
            dialog,
            TimeSpan.Zero,
            delayAsync: null,
            localization: null,
            paymentAttemptContextAccessor: null,
            businessWait: TimeSpan.FromSeconds(5));

        var result = await client.PurchaseAsync(8m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Equal("linkly.backend.cancelled", result.StatusKey);
        Assert.Equal("ANZ Linkly Cloud transaction was cancelled.", result.Message);
        Assert.Equal("TM", result.ResponseCode);
        Assert.Equal("OPERATOR TIMEOUT", result.ResponseText);
        Assert.Collection(
            requests,
            active => Assert.Equal(HttpMethod.Get, active.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method),
            status => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/real-dialog-cancel-session/status?environment=Sandbox", status.RequestUri!.AbsoluteUri),
            sendKey => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/real-dialog-cancel-session/sendkey", sendKey.RequestUri!.AbsoluteUri));
        var sendKeyBody = await requests[3].Content!.ReadAsStringAsync();
        Assert.Equal("Sandbox", ReadJsonString(sendKeyBody, "environment"));
        Assert.Equal(LinklyTerminalDialogKeys.OkCancel, ReadJsonString(sendKeyBody, "key"));
        Assert.Null(TryReadJsonString(sendKeyBody, "data"));
        Assert.False(dialog.LocalCancelToken.IsCancellationRequested);

        static HttpResponseMessage StatusAfterCashierClicksCancel(WpfLinklyTerminalDialogService dialog)
        {
            Assert.True(dialog.IsCancelPaymentVisible);
            // 这里模拟收银员在 SWIPE CARD 等待页点击取消；真实 dialog 必须发 Linkly Key=0，而不是本地停止等待。
            dialog.CancelPaymentCommand.Execute(null);
            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "real-dialog-cancel-session",
                    "status": "Pending",
                    "txnRef": "260601120031",
                    "displayText": "SWIPE CARD",
                    "receiptText": null,
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "cancelKeyFlag": true,
                    "notifications": [
                      {
                        "type": "display",
                        "payloadJson": "{ \"Response\": { \"DisplayText\": [\"SWIPE CARD\"], \"CancelKeyFlag\": \"1\" } }",
                        "receivedAt": "2026-06-01T02:00:04Z"
                      }
                    ]
                  }
                }
                """);
        }
    }

    [Fact]
    public async Task PurchaseAsync_returns_cancelled_unknown_when_backend_async_dialog_requests_local_cancel()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "local-cancel-session",
                        "status": "Pending",
                        "txnRef": "260601120130",
                        "displayText": "PRESENT CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 500,
                        "recoveryAction": "Retry",
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException("Local cancel should stop polling before the next status request.")
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.LocalCancel, null));
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.True(result.ResultUnknown);
        Assert.False(result.FallbackAllowed);
        Assert.Equal("linkly.backend.cancelledUnknown", result.StatusKey);
        Assert.Equal(2, requests.Count);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_stops_waiting_when_local_cancel_is_requested_during_poll_delay()
    {
        var requests = new List<HttpRequestMessage>();
        var dialog = new FakeLinklyTerminalDialogService();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "delay-cancel-session",
                        "status": "Pending",
                        "txnRef": "260601120132",
                        "displayText": "PRESENT CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 500,
                        "recoveryAction": "Retry",
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException("Local cancel during delay should stop before the next status request.")
            };
        });
        var client = CreateClient(
            handler,
            dialog,
            TimeSpan.FromMilliseconds(10),
            (_, token) =>
            {
                dialog.RequestLocalCancel();
                return Task.Delay(TimeSpan.FromMilliseconds(10), token);
            });

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.True(result.ResultUnknown);
        Assert.False(result.FallbackAllowed);
        Assert.Equal("linkly.backend.cancelledUnknown", result.StatusKey);
        Assert.Equal(2, requests.Count);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_closes_dialog_with_uncancelled_token_when_caller_token_is_cancelled_after_final_status()
    {
        using var callerCts = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "final-close-session",
                    "status": "Completed",
                    "txnRef": "260601120133",
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "displayText": "APPROVED",
                    "receiptText": "FINAL RECEIPT",
                    "recoveryCount": 0,
                    "receiptPrintedAt": "2026-06-01T02:00:04Z",
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """),
            _ => throw new InvalidOperationException("Final completed transaction should not poll again.")
        });
        var dialog = new FakeLinklyTerminalDialogService
        {
            OnUpdate = _ => callerCts.Cancel()
        };
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings(), callerCts.Token);

        Assert.True(result.Approved);
        Assert.Equal(1, dialog.CloseCallCount);
        var closeTokenWasCancelled = Assert.Single(dialog.CloseTokenCancellationStates);
        Assert.False(closeTokenWasCancelled);
    }

    [Fact]
    public async Task PurchaseAsync_continues_polling_when_cancel_sendkey_is_rejected_after_submission()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "cancel-key-session",
                        "status": "Pending",
                        "txnRef": "260601120131",
                        "displayText": "PRESS CANCEL",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "cancelKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"PRESS CANCEL\"], \"CancelKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:04Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": false,
                      "data": null,
                      "errorCode": "LINKLY_CLOUD_BACKEND_REQUEST_INVALID",
                      "message": "Linkly Cloud rejected the terminal action. Continue waiting for the transaction result."
                    }
                    """,
                    HttpStatusCode.BadRequest),
                4 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "cancel-key-session",
                        "status": "Completed",
                        "txnRef": "260601120131",
                        "responseCode": "TM",
                        "responseText": "OPERATOR TIMEOUT",
                        "displayText": "TRANSACTION DECLINED",
                        "receiptText": "CANCEL RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:04Z",
                        "lastHttpStatus": 400,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException("Cancel sendkey rejection should poll the final session result once.")
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.OkCancel, null));
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.False(result.FallbackAllowed);
        Assert.Equal("linkly.backend.cancelled", result.StatusKey);
        Assert.Equal("ANZ Linkly Cloud transaction was cancelled.", result.Message);
        Assert.Equal("TM", result.ResponseCode);
        Assert.Equal("OPERATOR TIMEOUT", result.ResponseText);
        Assert.Equal(4, requests.Count);
        Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/cancel-key-session/sendkey", requests[2].RequestUri!.AbsoluteUri);
        Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/cancel-key-session/status?environment=Sandbox", requests[3].RequestUri!.AbsoluteUri);
        Assert.Equal(1, dialog.CloseCallCount);
    }

    [Fact]
    public async Task PurchaseAsync_does_not_show_sendkey_failed_message_when_cancel_sendkey_is_rejected_but_session_is_still_pending()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "cancel-key-pending-session",
                        "status": "Pending",
                        "txnRef": "260601120132",
                        "displayText": "SWIPE CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "cancelKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"SWIPE CARD\"], \"CancelKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:04Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": false,
                      "data": null,
                      "errorCode": "LINKLY_CLOUD_BACKEND_REQUEST_INVALID",
                      "message": "Linkly Cloud rejected the terminal action."
                    }
                    """,
                    HttpStatusCode.BadRequest),
                4 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "cancel-key-pending-session",
                        "status": "Pending",
                        "txnRef": "260601120132",
                        "displayText": "SWIPE CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "cancelKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"SWIPE CARD\"], \"CancelKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:05Z"
                          }
                        ]
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "cancel-key-pending-session",
                        "status": "Cancelled",
                        "txnRef": "260601120132",
                        "responseCode": "C0",
                        "responseText": "CANCELLED",
                        "displayText": "CANCELLED",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.OkCancel, null));
        var client = CreateClient(handler, dialog, TimeSpan.Zero, null);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.Equal("CANCELLED", result.ResponseText);
        Assert.DoesNotContain(dialog.States, state =>
            state.SessionId == "cancel-key-pending-session" &&
            state.DisplayText == "SWIPE CARD" &&
            state.Message == "Card terminal action failed. Try again or recover the transaction.");
    }

    [Fact]
    public async Task PurchaseAsync_initial_pending_without_display_notification_shows_no_terminal_actions()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "initial-pending-session",
                    "status": "Pending",
                    "txnRef": "260601120030",
                    "displayText": "TAP OK TO CONTINUE",
                    "receiptText": null,
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 202,
                    "okKeyFlag": true,
                    "notifications": []
                  }
                }
                """),
            _ => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "initial-pending-session",
                    "status": "Completed",
                    "txnRef": "260601120030",
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "displayText": "APPROVED",
                    "receiptText": "INITIAL PENDING RECEIPT",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """)
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var pending = Assert.Single(dialog.States.Where(state => state.SessionId == "initial-pending-session" && state.Status == "Pending"));
        Assert.Empty(pending.DisplayButtons!);
    }

    [Fact]
    public async Task PurchaseAsync_display_notification_without_key_flags_shows_no_terminal_actions()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "display-no-key-session",
                    "status": "Pending",
                    "txnRef": "260601120033",
                    "displayText": "TAP OK TO CONTINUE",
                    "receiptText": null,
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 202,
                    "notifications": [
                      {
                        "type": "display",
                        "payloadJson": "{ \"Response\": { \"DisplayText\": [\"TAP OK TO CONTINUE\"] } }",
                        "receivedAt": "2026-06-01T02:00:05Z"
                      }
                    ]
                  }
                }
                """),
            _ => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "display-no-key-session",
                    "status": "Completed",
                    "txnRef": "260601120033",
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "displayText": "APPROVED",
                    "receiptText": "NO KEY RECEIPT",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """)
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var pending = Assert.Single(dialog.States.Where(state => state.SessionId == "display-no-key-session" && state.Status == "Pending"));
        Assert.Equal("Waiting for card terminal result...", pending.DisplayText);
        Assert.Empty(pending.DisplayButtons!);
    }

    [Fact]
    public async Task PurchaseAsync_display_notification_without_key_flags_uses_localized_waiting_result_message()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "display-no-key-session",
                    "status": "Pending",
                    "txnRef": "260601120033",
                    "displayText": "TAP OK TO CONTINUE",
                    "receiptText": null,
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 202,
                    "notifications": [
                      {
                        "type": "display",
                        "payloadJson": "{ \"Response\": { \"DisplayText\": [\"TAP OK TO CONTINUE\"] } }",
                        "receivedAt": "2026-06-01T02:00:05Z"
                      }
                    ]
                  }
                }
                """),
            _ => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "display-no-key-session",
                    "status": "Completed",
                    "txnRef": "260601120033",
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "displayText": "APPROVED",
                    "receiptText": "NO KEY RECEIPT",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """)
        });
        var localization = new LocalizationService();
        localization.SetCulture("zh-CN");
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog, localization);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var pending = Assert.Single(dialog.States.Where(state => state.SessionId == "display-no-key-session" && state.Status == "Pending"));
        Assert.Equal("等待刷卡终端返回结果...", pending.DisplayText);
        Assert.Empty(pending.DisplayButtons!);
    }

    [Theory]
    [InlineData("SWIPE CARD", "cancelKeyFlag")]
    [InlineData("PRESENT CARD", "cancelKeyFlag")]
    [InlineData("TAP CARD", "okKeyFlag")]
    [InlineData("INSERT CARD", "cancelKeyFlag")]
    public async Task PurchaseAsync_card_terminal_wait_display_suppresses_sendkey_buttons(
        string displayText,
        string enabledFlag)
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                $$"""
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "card-wait-session",
                    "status": "Pending",
                    "txnRef": "260601120035",
                    "displayText": "{{displayText}}",
                    "receiptText": null,
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 202,
                    "{{enabledFlag}}": true,
                    "notifications": [
                      {
                        "type": "display",
                        "payloadJson": "{ \"Response\": { \"DisplayText\": [\"{{displayText}}\"], \"{{enabledFlag}}\": \"1\" } }",
                        "receivedAt": "2026-06-01T02:00:05Z"
                      }
                    ]
                  }
                }
                """),
            _ => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "card-wait-session",
                    "status": "Completed",
                    "txnRef": "260601120035",
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "displayText": "APPROVED",
                    "receiptText": "CARD WAIT RECEIPT",
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """)
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var pending = Assert.Single(dialog.States.Where(state => state.SessionId == "card-wait-session" && state.Status == "Pending"));
        Assert.Equal(displayText, pending.DisplayText);
        Assert.Empty(pending.DisplayButtons!);
    }

    [Fact]
    public async Task PurchaseAsync_tap_ok_prompt_is_masked_without_sending_key_and_refreshes_status_immediately()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "tap-ok-session",
                        "status": "Pending",
                        "txnRef": "260601120036",
                        "displayText": "TAP OK TO CONTINUE",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 202,
                        "okKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"TAP OK TO CONTINUE\"], \"OKKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:05Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "tap-ok-session",
                        "status": "Pending",
                        "txnRef": "260601120036",
                        "displayText": "SWIPE CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 202,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"SWIPE CARD\"], \"CancelKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:06Z"
                          }
                        ]
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "tap-ok-session",
                        "status": "Completed",
                        "txnRef": "260601120036",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "AUTO OK RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Collection(
            requests,
            active => Assert.Equal(HttpMethod.Get, active.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method),
            firstStatus => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/tap-ok-session/status?environment=Sandbox", firstStatus.RequestUri!.AbsoluteUri),
            finalStatus => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/tap-ok-session/status?environment=Sandbox", finalStatus.RequestUri!.AbsoluteUri));
        Assert.DoesNotContain(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/sendkey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dialog.States, state => state.DisplayText == "TAP OK TO CONTINUE");
        Assert.Contains(dialog.States, state =>
            state.SessionId == "tap-ok-session" &&
            state.Status == "Pending" &&
            state.DisplayText == "Waiting for card terminal result..." &&
            state.DisplayButtons!.Count == 0 &&
            !state.SupportsCancelPayment);
        Assert.Contains(dialog.States, state =>
            state.SessionId == "tap-ok-session" &&
            state.Status == "Pending" &&
            state.DisplayText == "SWIPE CARD" &&
            state.DisplayButtons!.Count == 0 &&
            state.SupportsCancelPayment);
    }

    [Fact]
    public async Task PurchaseAsync_recovers_signature_buttons_from_latest_display_notification_when_status_flags_are_stale()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-stale-flags-session",
                        "status": "Pending",
                        "txnRef": "260601120037",
                        "responseCode": null,
                        "responseText": null,
                        "displayText": "PROCESSING\r\nPLEASE WAIT",
                        "receiptText": "LINE2\nCREDIT ACCOUNT\nPURCHASE AUD $10.08\nAPPROVE WITH SIG - 08\nPLEASE SIGN:",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:06Z",
                        "lastHttpStatus": 202,
                        "cancelKeyFlag": true,
                        "okKeyFlag": false,
                        "acceptYesKeyFlag": false,
                        "declineNoKeyFlag": false,
                        "authoriseKeyFlag": false,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"PROCESSING\", \"PLEASE WAIT\"], \"CancelKeyFlag\": false, \"AcceptYesKeyFlag\": false, \"DeclineNoKeyFlag\": false, \"AuthoriseKeyFlag\": false, \"OKKeyFlag\": false } }",
                            "receivedAt": "2026-06-01T02:00:04Z"
                          },
                          {
                            "type": "receipt",
                            "payloadJson": "{ \"Response\": { \"ReceiptText\": [\"APPROVE WITH SIG - 08\", \"PLEASE SIGN:\"] } }",
                            "receivedAt": "2026-06-01T02:00:05Z"
                          },
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"SIGNATURE OK?\"], \"CancelKeyFlag\": false, \"AcceptYesKeyFlag\": true, \"DeclineNoKeyFlag\": true, \"AuthoriseKeyFlag\": false, \"OKKeyFlag\": false } }",
                            "receivedAt": "2026-06-01T02:00:06Z"
                          }
                        ]
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "signature-stale-flags-session",
                        "status": "Completed",
                        "txnRef": "260601120037",
                        "responseCode": "Q6",
                        "responseText": "SIGNATURE ERROR",
                        "transactionSuccess": false,
                        "displayText": "TRANSACTION DECLINED",
                        "receiptText": "DECLINED - Q6\nSIGNATURE ERROR",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:06Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.No, null));
        var signaturePrinter = new FakeLinklyBankReceiptPrinter();
        var client = CreateClient(handler, dialog, signaturePrinter);

        var result = await client.PurchaseAsync(10.08m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        var signatureState = Assert.Single(dialog.States.Where(state => state.SessionId == "signature-stale-flags-session" && state.Status == "Pending"));
        Assert.Equal("PROCESSING\r\nPLEASE WAIT", signatureState.DisplayText);
        Assert.Collection(
            signatureState.DisplayButtons!,
            yes =>
            {
                Assert.Equal("linkly.backend.dialog.button.yesApproved", yes.TextResourceKey);
                Assert.Equal(LinklyTerminalDialogKeys.Yes, yes.Key);
            },
            no =>
            {
                Assert.Equal("linkly.backend.dialog.button.noDeclined", no.TextResourceKey);
                Assert.Equal(LinklyTerminalDialogKeys.No, no.Key);
            });
        Assert.False(signatureState.SupportsCancelPayment);
        var sendKeyBody = await requests[2].Content!.ReadAsStringAsync();
        Assert.Equal(LinklyTerminalDialogKeys.No, ReadJsonString(sendKeyBody, "key"));
        var declinedPrint = Assert.Single(signaturePrinter.Prints);
        Assert.Equal(LinklyBankReceiptKind.Declined, declinedPrint.Kind);
        Assert.Equal("DECLINED - Q6\nSIGNATURE ERROR", declinedPrint.ReceiptText);
    }

    [Fact]
    public async Task PurchaseAsync_keeps_polling_current_session_when_sendkey_request_fails()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "key-failure-session-1",
                        "status": "Pending",
                        "txnRef": "260601120031",
                        "displayText": "PRESS OK",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "okKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"PRESS OK\"], \"OKKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:04Z"
                          }
                        ]
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": false,
                      "message": "Send key failed."
                    }
                    """,
                    HttpStatusCode.InternalServerError),
                4 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "key-failure-session-1",
                        "status": "Pending",
                        "txnRef": "260601120031",
                        "displayText": "WAITING FOR CARD",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "key-failure-session-1",
                        "status": "Completed",
                        "txnRef": "260601120031",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "RECOVERED RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:04Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        dialog.EnqueueAction(new LinklyTerminalDialogAction("OK", null));
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Collection(
            requests,
            active => Assert.Equal(HttpMethod.Get, active.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method),
            sendKey => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/key-failure-session-1/sendkey", sendKey.RequestUri!.AbsoluteUri),
            status => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/key-failure-session-1/status?environment=Sandbox", status.RequestUri!.AbsoluteUri),
            finalStatus => Assert.Equal("https://api.example/api/v1/linkly/cloud-backend/transactions/key-failure-session-1/status?environment=Sandbox", finalStatus.RequestUri!.AbsoluteUri));

        // sendkey 失败后应继续展示当前会话，并提示用户重试或恢复交易。
        Assert.Contains(dialog.States, state =>
            state.SessionId == "key-failure-session-1" &&
            state.Message == "Card terminal action failed. Try again or recover the transaction." &&
            state.DisplayText == "WAITING FOR CARD" &&
            state.DisplayButtons is { Count: 0 });
        Assert.Contains(dialog.States, state =>
            state.SessionId == "key-failure-session-1" &&
            state.DisplayText == "WAITING FOR CARD");
        Assert.Equal("RECOVERED RECEIPT", Assert.Single(result.CardTransactions!).ReceiptText);
    }

    [Fact]
    public async Task PurchaseAsync_builds_single_ok_cancel_display_button_when_ok_and_cancel_flags_are_both_true()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "ok-cancel-session-1",
                        "status": "Pending",
                        "txnRef": "260601120032",
                        "displayText": "CHOOSE ACTION",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "okKeyFlag": true,
                        "cancelKeyFlag": true,
                        "notifications": [
                          {
                            "type": "display",
                            "payloadJson": "{ \"Response\": { \"DisplayText\": [\"CHOOSE ACTION\"], \"OKKeyFlag\": \"1\", \"CancelKeyFlag\": \"1\" } }",
                            "receivedAt": "2026-06-01T02:00:05Z"
                          }
                        ]
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "ok-cancel-session-1",
                        "status": "Completed",
                        "txnRef": "260601120032",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "OK CANCEL RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": "2026-06-01T02:00:05Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var dialogState = Assert.Single(dialog.States.Where(state => state.Status == "Pending"));
        var button = Assert.Single(dialogState.DisplayButtons!);
        Assert.Equal("linkly.backend.dialog.button.okCancel", button.TextResourceKey);
        Assert.Equal(LinklyTerminalDialogKeys.OkCancel, button.Key);
    }

    [Fact]
    public async Task PurchaseAsync_builds_cancel_button_only_when_cancel_flag_is_true()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/linkly/cloud-backend/transactions/active" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "cancel-only-session-1",
                    "status": "Pending",
                    "txnRef": "260601120034",
                    "displayText": "CANCEL AVAILABLE",
                    "receiptText": null,
                    "recoveryCount": 0,
                    "receiptPrintedAt": null,
                    "lastHttpStatus": 200,
                    "cancelKeyFlag": true,
                    "notifications": [
                      {
                        "type": "display",
                        "payloadJson": "{ \"Response\": { \"DisplayText\": [\"CANCEL AVAILABLE\"], \"CancelKeyFlag\": \"1\" } }",
                        "receivedAt": "2026-06-01T02:00:06Z"
                      }
                    ]
                  }
                }
                """),
            _ => JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "cancel-only-session-1",
                    "status": "Completed",
                    "txnRef": "260601120034",
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "displayText": "APPROVED",
                    "receiptText": "CANCEL ONLY RECEIPT",
                    "recoveryCount": 0,
                    "receiptPrintedAt": "2026-06-01T02:00:06Z",
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """)
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        var dialogState = Assert.Single(dialog.States.Where(state => state.SessionId == "cancel-only-session-1" && state.Status == "Pending"));
        var button = Assert.Single(dialogState.DisplayButtons!);
        Assert.Equal("linkly.backend.dialog.button.cancel", button.TextResourceKey);
        Assert.Equal(LinklyTerminalDialogKeys.OkCancel, button.Key);
        Assert.True(button.IsDestructive);
    }

    [Fact]
    public async Task PurchaseAsync_refreshes_202_pending_status_immediately()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "short-poll-session",
                        "status": "Pending",
                        "txnRef": "260601120040",
                        "recoveryAction": "Retry",
                        "displayText": "PROCESSING",
                        "receiptText": null,
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 202,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "short-poll-session",
                        "status": "Completed",
                        "txnRef": "260601120040",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "SHORT POLL RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var delays = new List<TimeSpan>();
        var client = CreateClient(
            handler,
            new FakeLinklyTerminalDialogService(),
            TimeSpan.FromMilliseconds(100),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Empty(delays);
        Assert.Equal(
            "https://api.example/api/v1/linkly/cloud-backend/transactions/short-poll-session/status?environment=Sandbox",
            requests[2].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PurchaseAsync_uses_exponential_backoff_for_408_and_5xx_recovery()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backoff-session",
                        "status": "Pending",
                        "txnRef": "260601120050",
                        "recoveryAction": "Retry",
                        "displayText": "RECOVERING",
                        "receiptText": null,
                        "recoveryCount": 1,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 408,
                        "notifications": []
                      }
                    }
                    """),
                3 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backoff-session",
                        "status": "Pending",
                        "txnRef": "260601120050",
                        "recoveryAction": "Retry",
                        "displayText": "RECOVERING",
                        "receiptText": null,
                        "recoveryCount": 2,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 500,
                        "notifications": []
                      }
                    }
                    """),
                _ => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "backoff-session",
                        "status": "Completed",
                        "txnRef": "260601120050",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "BACKOFF RECEIPT",
                        "recoveryCount": 2,
                        "receiptPrintedAt": "2026-06-01T02:00:05Z",
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """)
            };
        });
        var delays = new List<TimeSpan>();
        var client = CreateClient(
            handler,
            new FakeLinklyTerminalDialogService(),
            TimeSpan.FromMilliseconds(100),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Collection(
            delays,
            first =>
            {
                Assert.InRange(first.TotalMilliseconds, 100, 300);
                Assert.NotEqual(TimeSpan.FromMilliseconds(200), first);
            },
            second =>
            {
                Assert.InRange(second.TotalMilliseconds, 200, 600);
                Assert.NotEqual(TimeSpan.FromMilliseconds(400), second);
            });
        Assert.Equal(
            "https://api.example/api/v1/linkly/cloud-backend/transactions/backoff-session/recover",
            requests[2].RequestUri!.AbsoluteUri);
        Assert.Equal(
            "https://api.example/api/v1/linkly/cloud-backend/transactions/backoff-session/recover",
            requests[3].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PurchaseAsync_does_not_mark_receipt_printed_before_print_service_confirms()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 => JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "environment": "Sandbox",
                        "storeCode": "S01",
                        "deviceCode": "TERM-1",
                        "sessionId": "mark-receipt-session",
                        "status": "Completed",
                        "txnRef": "260601120060",
                        "responseCode": "00",
                        "responseText": "APPROVED",
                        "transactionSuccess": true,
                        "displayText": "APPROVED",
                        "receiptText": "MARK RECEIPT",
                        "recoveryCount": 0,
                        "receiptPrintedAt": null,
                        "lastHttpStatus": 200,
                        "notifications": []
                      }
                    }
                    """),
                _ => throw new InvalidOperationException("PurchaseAsync must not call receipt/printed before the receipt is actually printed.")
            };
        });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal("MARK RECEIPT", Assert.Single(result.CardTransactions!).ReceiptText);
        Assert.Collection(
            requests,
            active => Assert.Equal(HttpMethod.Get, active.Method),
            start => Assert.Equal(HttpMethod.Post, start.Method));
        Assert.DoesNotContain(requests, request =>
            request.RequestUri!.AbsolutePath.EndsWith("/receipt/printed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PurchaseAsync_rejects_printed_active_session_without_returning_receipt_text()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(CloneRequestWithBody(request));
            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "environment": "Sandbox",
                    "storeCode": "S01",
                    "deviceCode": "TERM-1",
                    "sessionId": "printed-session",
                    "status": "Completed",
                    "txnRef": "260601120070",
                    "responseCode": "00",
                    "responseText": "APPROVED",
                    "transactionSuccess": true,
                    "displayText": "APPROVED",
                    "receiptText": "ALREADY PRINTED RECEIPT",
                    "recoveryCount": 1,
                    "receiptPrintedAt": "2026-06-01T02:00:07Z",
                    "lastHttpStatus": 200,
                    "notifications": []
                  }
                }
                """);
        });
        var dialog = new FakeLinklyTerminalDialogService();
        var client = CreateClient(handler, dialog);

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.Null(result.CardTransactions);
        Assert.Single(requests);
        var finalState = Assert.Single(dialog.States);
        Assert.Equal("printed-session", finalState.SessionId);
        Assert.True(finalState.IsFinal);
    }

    [Fact]
    public async Task PurchaseAsync_does_not_use_short_configured_timeout_before_linkly_business_wait()
    {
        var requests = new List<string>();
        var statusWait = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(
            (request, cancellationToken) =>
            {
                requests.Add($"{request.Method} {request.RequestUri!.AbsoluteUri}");
                if (request.RequestUri!.AbsolutePath.EndsWith("/active", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                if (request.RequestUri.AbsolutePath.EndsWith("/transactions", StringComparison.Ordinal))
                {
                    return Task.FromResult(JsonResponse(PendingSessionJson("short-timeout-session", "TXN-SHORT")));
                }

                if (request.RequestUri.AbsolutePath.Contains("/transactions/short-timeout-session", StringComparison.Ordinal))
                {
                    return statusWait.Task.WaitAsync(cancellationToken);
                }

                throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
            });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());
        var settings = CreateSettings() with { TerminalTimeout = TimeSpan.FromMilliseconds(30) };

        var purchaseTask = client.PurchaseAsync(10m, CreateSession(), settings);
        await Task.Delay(120);

        if (purchaseTask.IsCompleted)
        {
            var early = await purchaseTask;
            Assert.Fail($"Purchase completed before business wait. statusKey={early.StatusKey} unknown={early.ResultUnknown} message={early.Message} requests={string.Join(" | ", requests)}");
        }

        statusWait.SetResult(JsonResponse(ApprovedSessionJson("short-timeout-session", "TXN-SHORT")));
        var result = await purchaseTask;
        Assert.True(result.Approved);
    }

    [Fact]
    public async Task PurchaseAsync_starts_transaction_business_wait_after_preflight()
    {
        var tokens = new Dictionary<string, CancellationToken>(StringComparer.Ordinal);
        var handler = new StubHttpMessageHandler(
            (request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/active", StringComparison.Ordinal))
                {
                    tokens["active"] = cancellationToken;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                if (path.EndsWith("/transactions", StringComparison.Ordinal))
                {
                    tokens["start"] = cancellationToken;
                    return Task.FromResult(JsonResponse(PendingSessionJson("fresh-business-wait-session", "TXN-FRESH")));
                }

                if (path.Contains("/transactions/fresh-business-wait-session", StringComparison.Ordinal))
                {
                    tokens["status"] = cancellationToken;
                    return Task.FromResult(JsonResponse(ApprovedSessionJson("fresh-business-wait-session", "TXN-FRESH")));
                }

                throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
            },
            (request, cancellationToken) =>
            {
                tokens["health"] = cancellationToken;
                return ReadyHealthResponse();
            });
        var client = CreateClient(handler, new FakeLinklyTerminalDialogService());

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.NotEqual(tokens["health"], tokens["start"]);
        Assert.NotEqual(tokens["active"], tokens["start"]);
        Assert.NotEqual(tokens["health"], tokens["status"]);
        Assert.NotEqual(tokens["active"], tokens["status"]);
    }

    [Fact]
    public async Task ResumeSessionUntilFinalAsync_times_out_with_result_unknown_details()
    {
        var recoverWait = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler((request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/transactions/recovery-timeout-session/recover", StringComparison.Ordinal))
            {
                return recoverWait.Task.WaitAsync(cancellationToken);
            }

            throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
        });
        var client = CreateClient(
            handler,
            new FakeLinklyTerminalDialogService(),
            TimeSpan.Zero,
            delayAsync: null,
            localization: null,
            businessWait: TimeSpan.FromMilliseconds(20));
        var activeStatus = new LinklyCloudBackendSessionResponse(
            "Sandbox",
            "S01",
            "TERM-1",
            "recovery-timeout-session",
            "Pending",
            "TXN-RECOVERY",
            ResponseCode: null,
            ResponseText: null,
            RecoveryAction: "Retry",
            DisplayText: "PRESENT CARD",
            CancelKeyFlag: false,
            OKKeyFlag: false,
            AcceptYesKeyFlag: false,
            DeclineNoKeyFlag: false,
            AuthoriseKeyFlag: false,
            InputType: null,
            GraphicCode: null,
            DisplayLines: null,
            ReceiptText: null,
            RecoveryCount: 0,
            ReceiptPrintedAt: null,
            ClientAcknowledgedAt: null,
            LastHttpStatus: 202,
            Notifications: []);

        var exception = await Assert.ThrowsAsync<LinklyBackendResultUnknownException>(
            () => client.ResumeSessionUntilFinalAsync(CreateSettings(), activeStatus));

        Assert.Contains("recovery timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionId=recovery-timeout-session", exception.Message, StringComparison.Ordinal);
        Assert.Contains("TxnRef=TXN-RECOVERY", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Status=Pending", exception.Message, StringComparison.Ordinal);
        Assert.Contains("result is unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LinklyBackendTerminalClient CreateClient(
        StubHttpMessageHandler handler,
        FakeLinklyTerminalDialogService dialog)
    {
        return CreateClient(handler, dialog, TimeSpan.Zero, null, null);
    }

    private static LinklyBackendTerminalClient CreateClient(
        StubHttpMessageHandler handler,
        FakeLinklyTerminalDialogService dialog,
        ILinklyBankReceiptPrinter bankReceiptPrinter)
    {
        return CreateClient(handler, dialog, TimeSpan.Zero, null, null, bankReceiptPrinter: bankReceiptPrinter);
    }

    private static LinklyBackendTerminalClient CreateClient(
        StubHttpMessageHandler handler,
        FakeLinklyTerminalDialogService dialog,
        ILocalizationService localization)
    {
        return CreateClient(handler, dialog, TimeSpan.Zero, null, localization);
    }

    private static LinklyBackendTerminalClient CreateClient(
        StubHttpMessageHandler handler,
        FakeLinklyTerminalDialogService dialog,
        TimeSpan pollInterval,
        Func<TimeSpan, CancellationToken, Task>? delayAsync)
    {
        return CreateClient(handler, dialog, pollInterval, delayAsync, null);
    }

    private static LinklyBackendTerminalClient CreateClient(
        StubHttpMessageHandler handler,
        FakeLinklyTerminalDialogService dialog,
        TimeSpan pollInterval,
        Func<TimeSpan, CancellationToken, Task>? delayAsync,
        ILocalizationService? localization,
        ILinklyPaymentAttemptContextAccessor? paymentAttemptContextAccessor = null,
        TimeSpan? businessWait = null,
        ILinklyBankReceiptPrinter? bankReceiptPrinter = null)
    {
        return new LinklyBackendTerminalClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") },
            dialog,
            pollInterval,
            delayAsync,
            localization,
            paymentAttemptContextAccessor,
            businessWait,
            bankReceiptPrinter);
    }

    private static PosSessionState CreateSession()
    {
        return new PosSessionState(
            "HB POS",
            "S01",
            "Main",
            "TERM-1",
            "C001",
            "Cashier",
            true,
            0);
    }

    private static CardTerminalSettings CreateSettings()
    {
        return CardTerminalSettings.FromEnvironment() with
        {
            Processor = CardProcessorKind.Linkly,
            Environment = CardTerminalEnvironment.Sandbox,
            LinklyConnectionMode = LinklyConnectionMode.CloudBackendAsync,
            TerminalTimeout = TimeSpan.FromSeconds(5)
        };
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage ReadyHealthResponse()
    {
        return JsonResponse(
            """
            {
              "success": true,
              "data": {
                "environment": "Sandbox",
                "storeCode": "S01",
                "deviceCode": "TERM-1",
                "isReady": true,
                "publicNotificationBaseUrl": "https://pos.example/linkly/",
                "checks": [
                  {
                    "code": "STORE_CREDENTIAL",
                    "isReady": true,
                    "message": "Linkly Cloud store credential is configured."
                  }
                ]
              }
            }
            """);
    }

    private static string PendingSessionJson(string sessionId, string txnRef)
    {
        return $$"""
            {
              "success": true,
              "data": {
                "environment": "Sandbox",
                "storeCode": "S01",
                "deviceCode": "TERM-1",
                "sessionId": "{{sessionId}}",
                "status": "Pending",
                "txnRef": "{{txnRef}}",
                "responseCode": null,
                "responseText": null,
                "displayText": "Processing",
                "recoveryCount": 0,
                "lastHttpStatus": 202,
                "notifications": []
              }
            }
            """;
    }

    private static string ApprovedSessionJson(string sessionId, string txnRef)
    {
        return $$"""
            {
              "success": true,
              "data": {
                "environment": "Sandbox",
                "storeCode": "S01",
                "deviceCode": "TERM-1",
                "sessionId": "{{sessionId}}",
                "status": "Completed",
                "txnRef": "{{txnRef}}",
                "responseCode": "00",
                "responseText": "APPROVED",
                "transactionSuccess": true,
                "displayText": "APPROVED",
                "receiptText": "APPROVED RECEIPT",
                "recoveryCount": 0,
                "lastHttpStatus": 200,
                "notifications": []
              }
            }
            """;
    }

    private static HttpRequestMessage CloneRequestWithBody(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
        {
            clone.Content = new StringContent(
                request.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                Encoding.UTF8,
                "application/json");
        }

        return clone;
    }

    private static string? ReadJsonString(string json, string propertyName)
    {
        return TryReadJsonString(json, propertyName)
            ?? throw new InvalidOperationException($"Missing JSON property {propertyName}.");
    }

    private static string? TryReadJsonString(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? _healthHandler;
        private readonly bool _passHealthRequestsToHandler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> handler,
            bool passHealthRequestsToHandler = false)
            : this((request, _) => Task.FromResult(handler(request)), passHealthRequestsToHandler)
        {
        }

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
            bool passHealthRequestsToHandler = false)
        {
            _handler = handler;
            _passHealthRequestsToHandler = passHealthRequestsToHandler;
        }

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
            Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> healthHandler)
        {
            _handler = handler;
            _healthHandler = healthHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!_passHealthRequestsToHandler &&
                request.RequestUri!.AbsolutePath.EndsWith("/api/v1/linkly/cloud-backend/health", StringComparison.Ordinal))
            {
                return Task.FromResult(_healthHandler?.Invoke(request, cancellationToken) ?? ReadyHealthResponse());
            }

            return _handler(request, cancellationToken);
        }
    }

    private sealed class FakeLinklyTerminalDialogService : ILinklyTerminalDialogService
    {
        private readonly Queue<LinklyTerminalDialogAction?> _actions = new();
        private readonly CancellationTokenSource _localCancelCts = new();

        public List<LinklyTerminalDialogState> States { get; } = [];

        public List<bool> CloseTokenCancellationStates { get; } = [];

        public int CloseCallCount { get; private set; }

        public Action<LinklyTerminalDialogState>? OnUpdate { get; set; }

        public CancellationToken LocalCancelToken => _localCancelCts.Token;

        public void EnqueueAction(LinklyTerminalDialogAction? action)
        {
            _actions.Enqueue(action);
        }

        public void RequestLocalCancel()
        {
            _localCancelCts.Cancel();
        }

        public Task<LinklyTerminalDialogAction?> UpdateAsync(
            LinklyTerminalDialogState state,
            CancellationToken cancellationToken)
        {
            States.Add(state);
            OnUpdate?.Invoke(state);
            return Task.FromResult(_actions.Count == 0 ? null : _actions.Dequeue());
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCallCount++;
            CloseTokenCancellationStates.Add(cancellationToken.IsCancellationRequested);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinklyBankReceiptPrinter : ILinklyBankReceiptPrinter
    {
        public List<(string Environment, string SessionId, string ReceiptText, LinklyBankReceiptKind Kind, string? CardType, string? MaskedCardNumber, string? ResponseCode, string? ResponseText)> Prints { get; } = [];

        public List<ReceiptPrintResult> Results { get; } = [];

        public Task<ReceiptPrintResult> PrintAsync(
            string environment,
            string sessionId,
            string receiptText,
            LinklyBankReceiptKind kind = LinklyBankReceiptKind.SignatureRequired,
            string? cardType = null,
            string? maskedCardNumber = null,
            string? responseCode = null,
            string? responseText = null,
            CancellationToken cancellationToken = default)
        {
            Prints.Add((environment, sessionId, receiptText, kind, cardType, maskedCardNumber, responseCode, responseText));
            if (Results.Count == 0)
            {
                return Task.FromResult(new ReceiptPrintResult(true, "printed"));
            }

            var result = Results[0];
            Results.RemoveAt(0);
            return Task.FromResult(result);
        }
    }

    private static JsonDocument FindLinklyLog(
        IReadOnlyList<string> lines,
        string operation,
        string phase)
    {
        foreach (var line in lines)
        {
            var jsonStart = line.IndexOf('{', StringComparison.Ordinal);
            if (jsonStart < 0)
            {
                continue;
            }

            var document = JsonDocument.Parse(line[jsonStart..]);
            if (string.Equals(document.RootElement.GetProperty("operation").GetString(), operation, StringComparison.Ordinal) &&
                string.Equals(document.RootElement.GetProperty("phase").GetString(), phase, StringComparison.Ordinal))
            {
                return document;
            }

            document.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"Expected Linkly JSON log operation={operation} phase={phase}.");
    }

    private static void AssertContainsOperationCancelledLog(
        IReadOnlyList<string> lines,
        bool transactionSubmitted,
        bool businessTimeoutCancelled)
    {
        var logLine = Assert.Single(lines, line => line.Contains("operation-cancelled", StringComparison.Ordinal));
        Assert.Contains("source=OperationCanceledException", logLine, StringComparison.Ordinal);
        Assert.Contains($"transactionSubmitted={transactionSubmitted}", logLine, StringComparison.Ordinal);
        Assert.Contains($"businessTimeoutCancelled={businessTimeoutCancelled}", logLine, StringComparison.Ordinal);
        Assert.Contains("localCancelRequested=False", logLine, StringComparison.Ordinal);
        Assert.Contains("callerCancelled=False", logLine, StringComparison.Ordinal);
    }

    private sealed class ConsoleLogCapture : IDisposable
    {
        private readonly object syncRoot = new();
        private readonly List<string> lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (syncRoot)
                {
                    return lines.ToArray();
                }
            }
        }

        public ConsoleLogCapture()
        {
            ConsoleLog.LineWritten += OnLineWritten;
        }

        public void Dispose()
        {
            ConsoleLog.LineWritten -= OnLineWritten;
        }

        private void OnLineWritten(string line)
        {
            lock (syncRoot)
            {
                lines.Add(line);
            }
        }
    }
}
