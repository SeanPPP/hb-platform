using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class SquareTerminalPaymentClientTests
{
    private const string TestAccessToken = "opaque-square-payment-token";
    private static readonly Uri HbposApiBaseAddress = new("http://localhost:5159/");

    [Fact]
    public async Task GetCheckoutAsync_UsesHbposApiCheckoutEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "checkoutId": "checkout-1",
                    "environment": "Production",
                    "status": "CANCELED",
                    "amountMoney": { "amount": 1000, "currency": "AUD" },
                    "paymentIds": [ "payment-1", "payment-2" ],
                    "cancelReason": "SELLER_CANCELED"
                  }
                }
                """));
        });
        var client = CreateClient(handler);

        var result = await client.GetCheckoutAsync(CreateSettings(), "checkout-1");

        Assert.Equal("CANCELED", result.Status);
        Assert.Equal(["payment-1", "payment-2"], result.PaymentIds);
        Assert.Equal("SELLER_CANCELED", result.CancelReason);
        Assert.NotNull(capturedRequest);
        AssertHbposApiRequest(capturedRequest!, "api/v1/square/checkouts/checkout-1?environment=Production");
        AssertNoSquareHeaders(capturedRequest!);
    }

    [Fact]
    public async Task GetPaymentAsync_UsesHbposApiPaymentEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "paymentId": "payment-1",
                    "status": "COMPLETED",
                    "approvedMoney": { "amount": 1000, "currency": "AUD" },
                    "cardBrand": "VISA",
                    "maskedCardNumber": "****1111",
                    "authCode": "68aLBM"
                  }
                }
                """));
        });
        var client = CreateClient(handler);

        var result = await client.GetPaymentAsync(CreateSettings(), "payment-1");

        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("VISA", result.CardBrand);
        Assert.Equal("****1111", result.MaskedCardNumber);
        Assert.Equal("68aLBM", result.AuthCode);
        Assert.NotNull(capturedRequest);
        AssertHbposApiRequest(capturedRequest!, "api/v1/square/payments/payment-1?environment=Production");
        AssertNoSquareHeaders(capturedRequest!);
    }

    private static CardTerminalSettings CreateSettings()
    {
        return new CardTerminalSettings(
            CardProcessorKind.Square,
            CardTerminalEnvironment.Production,
            "127.0.0.1",
            2011,
            TestAccessToken,
            "LOC-1",
            "DEVICE-1",
            CardTerminalSettings.GetSquareApiBaseUrl(CardTerminalEnvironment.Production),
            TimeSpan.FromSeconds(30));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static SquareTerminalPaymentClient CreateClient(HttpMessageHandler handler)
    {
        return new SquareTerminalPaymentClient(new HttpClient(handler)
        {
            BaseAddress = HbposApiBaseAddress
        });
    }

    private static void AssertHbposApiRequest(HttpRequestMessage request, string relativePathAndQuery)
    {
        Assert.Equal(new Uri(HbposApiBaseAddress, relativePathAndQuery), request.RequestUri);
        var absoluteUri = request.RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("connect.squareup.com", absoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connect.squareupsandbox.com", absoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoSquareHeaders(HttpRequestMessage request)
    {
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Square-Version"));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return await handler(request, cancellationToken);
        }
    }
}
