using System.Net;
using System.Text;
using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Client.Tests;

public sealed class LinklyBackendTerminalClientTask2BTests
{
    [Fact]
    public async Task PurchaseAsync_takes_over_preflight_active_card_session_then_starts_new_transaction_once()
    {
        var requests = new List<HttpRequestMessage>();
        var takeoverInvocations = new List<string?>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => JsonResponse(ActiveSessionJson("active-session-1", "TXN-OLD")),
                "/api/v1/linkly/cloud-backend/transactions" => JsonResponse(ApprovedSessionJson("new-session-1", "TXN-NEW", "1000")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (settings, activeStatus, _) =>
            {
                takeoverInvocations.Add(activeStatus.SessionId);
                return Task.FromResult(LinklyActiveSessionTakeoverResult.Success);
            }));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal("new-session-1", result.SessionId);
        Assert.Equal(["active-session-1"], takeoverInvocations);
        Assert.Equal(1, requests.Count(request => request.Method == HttpMethod.Post));
        Assert.Equal(
            "https://api.example/api/v1/linkly/cloud-backend/transactions",
            Assert.Single(requests, request => request.Method == HttpMethod.Post).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PurchaseAsync_returns_not_submitted_and_skips_new_charge_when_preflight_takeover_fails()
    {
        var requests = new List<HttpRequestMessage>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => JsonResponse(ActiveSessionJson("active-session-1", "TXN-OLD")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, _, _) => Task.FromResult(
                LinklyActiveSessionTakeoverResult.Failed("Previous session could not be resolved."))));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.Equal("linkly.backend.activeSessionRequiresRecovery", result.StatusKey);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task PurchaseAsync_returns_not_submitted_for_active_settlement_without_takeover_or_start()
    {
        var requests = new List<HttpRequestMessage>();
        var takeoverInvocations = 0;
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => JsonResponse(SettlementSessionJson("settlement-session-1")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, _, _) =>
            {
                takeoverInvocations++;
                return Task.FromResult(LinklyActiveSessionTakeoverResult.Success);
            }));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.True(result.FallbackAllowed);
        Assert.Equal(0, takeoverInvocations);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task PurchaseAsync_takes_over_conflict_once_then_retries_start_exactly_once()
    {
        var requests = new List<HttpRequestMessage>();
        var takeoverInvocations = new List<string?>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 =>
                    new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("""{"success":false,"message":"Active session exists."}""", Encoding.UTF8, "application/json") },
                3 => JsonResponse(ActiveSessionJson("conflict-session-1", "TXN-OLD")),
                4 => JsonResponse(ApprovedSessionJson("new-session-1", "TXN-NEW", "1000")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, activeStatus, _) =>
            {
                takeoverInvocations.Add(activeStatus.SessionId);
                return Task.FromResult(LinklyActiveSessionTakeoverResult.Success);
            }));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.True(result.Approved);
        Assert.Equal("new-session-1", result.SessionId);
        Assert.Equal(["conflict-session-1"], takeoverInvocations);
        Assert.Equal(2, requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task PurchaseAsync_rejects_second_conflict_without_second_takeover()
    {
        var requests = new List<HttpRequestMessage>();
        var takeoverInvocations = new List<string?>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 or 4 =>
                    new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("""{"success":false,"message":"Active session exists."}""", Encoding.UTF8, "application/json") },
                3 or 5 => JsonResponse(ActiveSessionJson("conflict-session-1", "TXN-OLD")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, activeStatus, _) =>
            {
                takeoverInvocations.Add(activeStatus.SessionId);
                return Task.FromResult(LinklyActiveSessionTakeoverResult.Success);
            }));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.Equal("linkly.backend.activeSessionRequiresRecovery", result.StatusKey);
        Assert.Equal(["conflict-session-1"], takeoverInvocations);
        Assert.Equal(2, requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task PurchaseAsync_captures_different_session_after_second_conflict_without_third_start()
    {
        var requests = new List<HttpRequestMessage>();
        var takeoverInvocations = new List<string?>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 or 4 =>
                    new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("""{"success":false,"message":"Active session exists."}""", Encoding.UTF8, "application/json") },
                3 => JsonResponse(ActiveSessionJson("conflict-session-1", "TXN-OLD-1")),
                5 => JsonResponse(ActiveSessionJson("conflict-session-2", "TXN-OLD-2")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, activeStatus, _) =>
            {
                takeoverInvocations.Add(activeStatus.SessionId);
                return Task.FromResult(LinklyActiveSessionTakeoverResult.Success);
            }));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.Equal("linkly.backend.activeSessionRequiresRecovery", result.StatusKey);
        Assert.Equal(["conflict-session-1", "conflict-session-2"], takeoverInvocations);
        Assert.Equal(2, requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task PurchaseAsync_skips_retry_when_conflict_takeover_fails()
    {
        var requests = new List<HttpRequestMessage>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 =>
                    new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("""{"success":false,"message":"Active session exists."}""", Encoding.UTF8, "application/json") },
                3 => JsonResponse(ActiveSessionJson("conflict-session-1", "TXN-OLD")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, _, _) => Task.FromResult(
                LinklyActiveSessionTakeoverResult.Failed("Previous session could not be resolved."))));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.Equal("linkly.backend.activeSessionRequiresRecovery", result.StatusKey);
        Assert.Equal(1, requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task PurchaseAsync_does_not_mark_new_attempt_unknown_when_conflict_session_lookup_fails()
    {
        var requests = new List<HttpRequestMessage>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.NotFound),
                2 =>
                    new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("""{"success":false,"message":"Active session exists."}""", Encoding.UTF8, "application/json") },
                3 => throw new HttpRequestException("Active session lookup failed."),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, _, _) => Task.FromResult(LinklyActiveSessionTakeoverResult.Success)));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings());

        Assert.False(result.Approved);
        Assert.False(result.ResultUnknown);
        Assert.False(result.FallbackAllowed);
        Assert.Equal("linkly.backend.activeSessionRequiresRecovery", result.StatusKey);
        Assert.Equal(1, requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task PurchaseAsync_propagates_real_caller_cancellation_during_takeover()
    {
        using var callerCts = new CancellationTokenSource();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => JsonResponse(ActiveSessionJson("active-session-1", "TXN-OLD")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, _, cancellationToken) =>
            {
                callerCts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(LinklyActiveSessionTakeoverResult.Success);
            }));

        await Assert.ThrowsAsync<CardTerminalNotSubmittedException>(
            () => client.PurchaseAsync(10m, CreateSession(), CreateSettings(), callerCts.Token));
    }

    [Fact]
    public async Task PurchaseAsync_treats_cancellation_returned_from_preflight_takeover_as_not_submitted()
    {
        using var callerCts = new CancellationTokenSource();
        var requests = new List<HttpRequestMessage>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/linkly/cloud-backend/transactions/active" => JsonResponse(ActiveSessionJson("active-session-1", "TXN-OLD")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(
            takeOver: (_, _, _) =>
            {
                callerCts.Cancel();
                return Task.FromResult(LinklyActiveSessionTakeoverResult.Success);
            }));

        await Assert.ThrowsAsync<CardTerminalNotSubmittedException>(
            () => client.PurchaseAsync(10m, CreateSession(), CreateSettings(), callerCts.Token));

        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task PurchaseAsync_returns_unknown_when_caller_cancels_after_start_request_is_sent()
    {
        using var callerCts = new CancellationTokenSource();
        var requests = new List<HttpRequestMessage>();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var handler = new RecordingHandler((request, _) =>
        {
            requests.Add(CloneRequest(request));
            if (request.Method == HttpMethod.Post)
            {
                callerCts.Cancel();
                throw new OperationCanceledException(callerCts.Token);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler, accessor);
        using var scope = accessor.Begin(CreateContext(takeOver: null));

        var result = await client.PurchaseAsync(10m, CreateSession(), CreateSettings(), callerCts.Token);

        Assert.False(result.Approved);
        Assert.True(result.ResultUnknown);
        Assert.False(result.FallbackAllowed);
        Assert.Equal("linkly.backend.cancelledUnknown", result.StatusKey);
        Assert.Equal(1, requests.Count(request => request.Method == HttpMethod.Post));
    }

    private static LinklyBackendTerminalClient CreateClient(
        RecordingHandler handler,
        ILinklyPaymentAttemptContextAccessor accessor)
    {
        return new LinklyBackendTerminalClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") },
            new FakeDialog(),
            TimeSpan.Zero,
            delayAsync: null,
            localization: null,
            accessor,
            businessWait: TimeSpan.FromSeconds(5));
    }

    private static LinklyPaymentAttemptContext CreateContext(
        Func<CardTerminalSettings, LinklyCloudBackendSessionResponse, CancellationToken, Task<LinklyActiveSessionTakeoverResult>>? takeOver)
    {
        return new LinklyPaymentAttemptContext(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            (_, _, _, _) => Task.CompletedTask,
            TakeOverActiveSessionAsync: takeOver);
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

    private static string ActiveSessionJson(string sessionId, string txnRef)
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
                "lastHttpStatus": 409,
                "operationType": "Transaction",
                "notifications": []
              }
            }
            """;
    }

    private static string SettlementSessionJson(string sessionId)
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
                "txnRef": null,
                "responseCode": null,
                "responseText": null,
                "displayText": "Settling",
                "recoveryCount": 0,
                "lastHttpStatus": 409,
                "operationType": "Settlement",
                "notifications": []
              }
            }
            """;
    }

    private static string ApprovedSessionJson(string sessionId, string txnRef, string minorAmount)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Response = new
            {
                Success = true,
                TxnRef = txnRef,
                ResponseCode = "00",
                ResponseText = "APPROVED",
                AmtPurchase = long.Parse(minorAmount, System.Globalization.CultureInfo.InvariantCulture)
            }
        });
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
                "notifications": [
                  {
                    "type": "transaction",
                    "payloadJson": {{JsonSerializer.Serialize(payload)}},
                    "receivedAt": "2026-06-01T02:00:00Z"
                  }
                ]
              }
            }
            """;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
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

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/linkly/cloud-backend/health", StringComparison.Ordinal))
            {
                return Task.FromResult(ReadyHealthResponse());
            }

            return Task.FromResult(handler(request, cancellationToken));
        }
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
                "checks": []
              }
            }
            """);
    }

    private sealed class FakeDialog : ILinklyTerminalDialogService
    {
        private readonly CancellationTokenSource _localCancelCts = new();

        public CancellationToken LocalCancelToken => _localCancelCts.Token;

        public Task<LinklyTerminalDialogAction?> UpdateAsync(
            LinklyTerminalDialogState state,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<LinklyTerminalDialogAction?>(null);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
