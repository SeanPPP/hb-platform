using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class SquareTerminalPaymentClientTests
{
    private const string TestAccessToken = "opaque-square-payment-token";

    [Fact]
    public async Task GetCheckoutAsync_UsesConfiguredSquareV2BaseWithoutDoublePrefix()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(
                """
                {
                  "checkout": {
                    "id": "checkout-1",
                    "status": "COMPLETED",
                    "amount_money": { "amount": 1000, "currency": "AUD" },
                    "payment_ids": ["payment-1"]
                  }
                }
                """));
        });
        var client = new SquareTerminalPaymentClient(new HttpClient(handler));

        var result = await client.GetCheckoutAsync(CreateSettings(), "checkout-1");

        Assert.Equal("COMPLETED", result.Status);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://connect.squareup.com/v2/terminals/checkouts/checkout-1", capturedRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetPaymentAsync_UsesConfiguredSquareV2BaseWithoutDoublePrefix()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(
                """
                {
                  "payment": {
                    "id": "payment-1",
                    "status": "COMPLETED",
                    "amount_money": { "amount": 1000, "currency": "AUD" }
                  }
                }
                """));
        });
        var client = new SquareTerminalPaymentClient(new HttpClient(handler));

        var result = await client.GetPaymentAsync(CreateSettings(), "payment-1");

        Assert.Equal("COMPLETED", result.Status);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://connect.squareup.com/v2/payments/payment-1", capturedRequest!.RequestUri!.AbsoluteUri);
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
