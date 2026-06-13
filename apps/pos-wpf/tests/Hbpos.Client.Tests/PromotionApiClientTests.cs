using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Promotions;

namespace Hbpos.Client.Tests;

public sealed class PromotionApiClientTests
{
    [Fact]
    public async Task GetRulesAsync_builds_expected_request_and_unwraps_api_result()
    {
        HttpRequestMessage? capturedRequest = null;
        var asOf = DateTimeOffset.Parse("2026-06-13T12:34:56Z");
        var expected = new PromotionRulesResponse(
            "S001",
            asOf,
            [
                new PromotionRuleDto(
                    "PROMO-001",
                    "Tea Bundle",
                    asOf.AddDays(-1).UtcDateTime,
                    asOf.AddDays(1).UtcDateTime,
                    true,
                    15,
                    2,
                    8.5m,
                    1,
                    [new PromotionRuleProductDto("SKU-001", 2)])
            ]);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return JsonResponse(ApiResult<PromotionRulesResponse>.Ok(expected));
        });
        var client = new PromotionApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5000/")
        });

        var response = await client.GetRulesAsync(" S001 ", asOf);

        Assert.Equal(HttpMethod.Get, capturedRequest?.Method);
        Assert.Equal(
            "http://localhost:5000/api/v1/promotions/rules?storeCode=S001&asOf=2026-06-13T12%3A34%3A56.0000000%2B00%3A00",
            capturedRequest?.RequestUri?.ToString());
        Assert.Equal("S001", response.StoreCode);
        Assert.Equal("PROMO-001", Assert.Single(response.Rules).Id);
    }

    [Fact]
    public async Task GetRulesAsync_throws_when_api_returns_failure()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            JsonResponse(
                ApiResult<PromotionRulesResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"),
                HttpStatusCode.BadRequest));
        var client = new PromotionApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5000/")
        });

        var ex = await Assert.ThrowsAsync<CatalogApiException>(() => client.GetRulesAsync("S001"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("STORE_CODE_REQUIRED", ex.ErrorCode);
    }

    private static HttpResponseMessage JsonResponse<T>(T payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
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
}
