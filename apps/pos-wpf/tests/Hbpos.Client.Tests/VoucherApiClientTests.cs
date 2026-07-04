using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Vouchers;

namespace Hbpos.Client.Tests;

public sealed class VoucherApiClientTests
{
    [Fact]
    public async Task RedeemAsync_includes_remaining_balance_in_reference_when_voucher_is_partially_used()
    {
        var client = new VoucherApiClient(new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "found": true,
                        "voucher": {
                          "voucherCode": "VC100",
                          "storeCode": "S001",
                          "voucherType": 1,
                          "amount": 20.00,
                          "remainingAmount": 20.00,
                          "status": "1",
                          "expiredAt": "2027-05-26T00:00:00Z",
                          "customerCode": null,
                          "discountRate": 0,
                          "remark": null
                        }
                      }
                    }
                    """);
            }

            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "voucherCode": "VC100",
                    "lockedAmount": 5.00,
                    "reservationToken": "LOCK-1",
                    "expiresAt": "2026-05-26T00:05:00Z",
                    "remainingAmountAfterLock": 11.00
                  }
                }
                """);
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var result = await client.RedeemAsync(5m, Session, "VC100");

        Assert.True(result.Approved);
        Assert.Equal("VOUCHER:VC100:LOCK-1:11.00", result.Reference);
        Assert.Equal(5m, result.AuthorizedAmount);
    }

    [Fact]
    public async Task RedeemAsync_omits_remaining_balance_when_voucher_is_fully_used()
    {
        var client = new VoucherApiClient(new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "found": true,
                        "voucher": {
                          "voucherCode": "VC101",
                          "storeCode": "S001",
                          "voucherType": 1,
                          "amount": 5.00,
                          "remainingAmount": 5.00,
                          "status": "1",
                          "expiredAt": "2027-05-26T00:00:00Z",
                          "customerCode": null,
                          "discountRate": 0,
                          "remark": null
                        }
                      }
                    }
                    """);
            }

            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "voucherCode": "VC101",
                    "lockedAmount": 5.00,
                    "reservationToken": "LOCK-2",
                    "expiresAt": "2026-05-26T00:05:00Z",
                    "remainingAmountAfterLock": 0.00
                  }
                }
                """);
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var result = await client.RedeemAsync(5m, Session, "VC101");

        Assert.True(result.Approved);
        Assert.Equal("VOUCHER:VC101:LOCK-2", result.Reference);
        Assert.Equal(5m, result.AuthorizedAmount);
    }

    [Fact]
    public async Task RedeemAsync_omits_remaining_balance_when_lock_response_does_not_confirm_it()
    {
        var client = new VoucherApiClient(new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(
                    """
                    {
                      "success": true,
                      "data": {
                        "found": true,
                        "voucher": {
                          "voucherCode": "VC102",
                          "storeCode": "S001",
                          "voucherType": 1,
                          "amount": 20.00,
                          "remainingAmount": 20.00,
                          "status": "1",
                          "expiredAt": "2027-05-26T00:00:00Z",
                          "customerCode": null,
                          "discountRate": 0,
                          "remark": null
                        }
                      }
                    }
                    """);
            }

            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "voucherCode": "VC102",
                    "lockedAmount": 5.00,
                    "reservationToken": "LOCK-3",
                    "expiresAt": "2026-05-26T00:05:00Z"
                  }
                }
                """);
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var result = await client.RedeemAsync(5m, Session, "VC102");

        Assert.True(result.Approved);
        Assert.Equal("VOUCHER:VC102:LOCK-3", result.Reference);
        Assert.Equal(5m, result.AuthorizedAmount);
    }

    [Fact]
    public async Task IssueRefundAsync_returns_refund_voucher_reference()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = new VoucherApiClient(new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = CloneRequestWithBody(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "success": true,
                      "data": {
                        "voucherCode": "RF123",
                        "amount": 9.50,
                        "remainingAmount": 9.50,
                        "status": "1",
                        "expiredAt": "2027-05-26T00:00:00Z"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var result = await client.IssueRefundAsync(
            9.5m,
            new PosSessionState("HB POS", "S001", "Main", "POS-01", "C001", "Alice", true, 0),
            "11111111-1111-1111-1111-111111111111",
            "11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222",
            "Refund reason");

        Assert.True(result.Approved);
        Assert.Equal("VOUCHER_REFUND:RF123", result.Reference);
        Assert.Equal(9.5m, result.AuthorizedAmount);
        Assert.NotNull(capturedRequest);
        Assert.Equal("http://localhost/api/v1/vouchers/refund", capturedRequest!.RequestUri!.AbsoluteUri);
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"storeCode\":\"S001\"", body, StringComparison.Ordinal);
        Assert.Contains("\"cashierId\":\"C001\"", body, StringComparison.Ordinal);
        Assert.Contains("\"idempotencyKey\":\"11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222\"", body, StringComparison.Ordinal);
        Assert.Contains("\"orderReference\":\"11111111-1111-1111-1111-111111111111\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IssueVoucherAsync_posts_issue_request()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = new VoucherApiClient(new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = CloneRequestWithBody(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "success": true,
                      "data": {
                        "voucherCode": "VC123",
                        "amount": 20.00,
                        "remainingAmount": 20.00,
                        "status": "1",
                        "expiredAt": "2027-05-26T00:00:00Z",
                        "storeCode": "S001",
                        "customerCode": "CUS001"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var result = await client.IssueVoucherAsync(
            new StoreVoucherIssueRequest("S001", 20m, "C001", "ISSUE-1", CustomerCode: "CUS001", Reason: "Manual issue"));

        Assert.Equal("VC123", result.VoucherCode);
        Assert.Equal(20m, result.RemainingAmount);
        Assert.Equal("S001", result.StoreCode);
        Assert.Equal("CUS001", result.CustomerCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal("http://localhost/api/v1/vouchers/issue", capturedRequest!.RequestUri!.AbsoluteUri);
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"storeCode\":\"S001\"", body, StringComparison.Ordinal);
        Assert.Contains("\"cashierId\":\"C001\"", body, StringComparison.Ordinal);
        Assert.Contains("\"idempotencyKey\":\"ISSUE-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"customerCode\":\"CUS001\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseAsync_posts_release_request()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = new VoucherApiClient(new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = CloneRequestWithBody(request);
            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "voucherCode": "VC100",
                    "reservationToken": "LOCK-1",
                    "released": true
                  }
                }
                """);
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var result = await client.ReleaseAsync(
            new StoreVoucherReleaseRequest("S001", "VC100", "LOCK-1"));

        Assert.True(result.Released);
        Assert.Equal("VC100", result.VoucherCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal("http://localhost/api/v1/vouchers/release", capturedRequest!.RequestUri!.AbsoluteUri);
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"storeCode\":\"S001\"", body, StringComparison.Ordinal);
        Assert.Contains("\"voucherCode\":\"VC100\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reservationToken\":\"LOCK-1\"", body, StringComparison.Ordinal);
    }

    private static HttpRequestMessage CloneRequestWithBody(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
        {
            var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return clone;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request, cancellationToken));
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static PosSessionState Session { get; } = new("HB POS", "S001", "Main", "POS-01", "C001", "Alice", true, 0);
}
